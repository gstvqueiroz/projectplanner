using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectPlanner.Servidor.Dados;
using ProjectPlanner.Servidor.Servicos;

namespace ProjectPlanner.Servidor.Rotas;

public sealed record PedidoDeObra(string? Nome, JsonElement Retrato);

/// <summary>
/// Bloco 49: o Telegram. O webhook recebe as mensagens (conferindo o segredo) e as põe na fila do atendente; o programa marca
/// as obras (com o retrato), liga o Telegram com um código e leva o que chegou na caixa de entrada.
/// </summary>
public static class Telegram
{
    public const string CabecalhoDoSegredo = "X-Telegram-Bot-Api-Secret-Token";
    public const int MegabytesPorRetrato = 4;
    public const int MinutosDoCodigo = 15;
    private const string Letras = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // sem 0/O e 1/I

    public static void Mapear(WebApplication app)
    {
        app.MapPost("/v1/telegram/webhook", Webhook);
        var conta = app.MapGroup("/v1").RequireAuthorization().RequireRateLimiting(LimitesDeUso.Ia); // por conta, não por IP
        conta.MapGet("/telegram", Situacao);
        conta.MapPost("/telegram/codigo", Codigo);
        conta.MapDelete("/telegram", Desligar);
        conta.MapGet("/obras", Obras);
        conta.MapPut("/obras/{projeto:guid}", Marcar);
        conta.MapDelete("/obras/{projeto:guid}", Desmarcar);
        conta.MapGet("/obras/{projeto:guid}/caixa", Caixa);
        conta.MapGet("/caixa/{id:guid}/foto", Foto);
        conta.MapDelete("/caixa/{id:guid}", Levar);
    }

    /// <summary>O aviso do Telegram: confere o segredo e responde na hora; o atendimento é na fila.</summary>
    private static async Task<IResult> Webhook(HttpRequest http, FilaDoTelegram fila, IOptions<OpcoesDoServidor> opcoes, CancellationToken c)
    {
        var config = opcoes.Value.Telegram;
        if (!config.Ligado || !Segredos.Iguais(config.Segredo, http.Headers[CabecalhoDoSegredo].ToString()))
            return Results.Unauthorized();
        try
        {
            using var json = await JsonDocument.ParseAsync(http.Body, cancellationToken: c);
            fila.Colocar(json.RootElement);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }
        return Results.Ok();
    }

    private static async Task<(Usuario? Usuario, IResult? Falha)> Conta(ClaimsPrincipal quem, BancoDoServidor banco, bool soProfissional = false)
    {
        if (await Usuarios.Atual(quem, banco) is not { } usuario)
            return (null, Contas.Falha(401, "Sessão vencida: entre de novo."));
        if (soProfissional && usuario.Plano != Planos.Profissional)
            return (null, Contas.Falha(403, "O assistente no Telegram é do plano Profissional. Veja em Conta > Minha conta."));
        return (usuario, null);
    }

    private static async Task<IResult> Situacao(ClaimsPrincipal quem, BancoDoServidor banco, IOptions<OpcoesDoServidor> opcoes)
    {
        var (usuario, falha) = await Conta(quem, banco);
        if (falha is not null)
            return falha;
        var vinculo = await banco.VinculosDoTelegram.FindAsync(usuario!.Id);
        return Results.Ok(new { disponivel = opcoes.Value.Telegram.Ligado, ligado = vinculo is not null, nome = vinculo?.Nome, bot = opcoes.Value.Telegram.NomeDoBot });
    }

    /// <summary>Um código de 8 letras (15 min) e o link que abre o bot com ele.</summary>
    private static async Task<IResult> Codigo(ClaimsPrincipal quem, BancoDoServidor banco, IOptions<OpcoesDoServidor> opcoes, TimeProvider relogio)
    {
        var (usuario, falha) = await Conta(quem, banco, soProfissional: true);
        if (falha is not null)
            return falha;
        var config = opcoes.Value.Telegram;
        if (!config.Ligado)
            return Contas.Falha(503, "O Telegram ainda não está configurado no servidor.");
        await banco.CodigosDoTelegram.Where(x => x.UsuarioId == usuario!.Id).ExecuteDeleteAsync();
        var codigo = new string(Enumerable.Range(0, 8).Select(_ => Letras[RandomNumberGenerator.GetInt32(Letras.Length)]).ToArray());
        banco.CodigosDoTelegram.Add(new CodigoDoTelegram
        {
            Hash = Segredos.Hash(codigo), UsuarioId = usuario!.Id, ExpiraEm = relogio.GetUtcNow().UtcDateTime.AddMinutes(MinutosDoCodigo),
        });
        await banco.SaveChangesAsync();
        return Results.Ok(new
        {
            codigo,
            link = string.IsNullOrWhiteSpace(config.NomeDoBot) ? null : $"https://t.me/{config.NomeDoBot.TrimStart('@')}?start={codigo}",
            bot = config.NomeDoBot,
            minutos = MinutosDoCodigo,
        });
    }

    private static async Task<IResult> Desligar(ClaimsPrincipal quem, BancoDoServidor banco)
    {
        var (usuario, falha) = await Conta(quem, banco);
        if (falha is not null)
            return falha;
        await banco.VinculosDoTelegram.Where(v => v.UsuarioId == usuario!.Id).ExecuteDeleteAsync();
        return Results.NoContent();
    }

    /// <summary>As obras marcadas, com quantos itens esperam na caixa de cada uma.</summary>
    private static async Task<IResult> Obras(ClaimsPrincipal quem, BancoDoServidor banco)
    {
        var (usuario, falha) = await Conta(quem, banco);
        if (falha is not null)
            return falha;
        var obras = await banco.Obras.Where(o => o.UsuarioId == usuario!.Id).Select(o => new { o.Id, o.Projeto, o.Nome, o.AtualizadoEm }).ToListAsync();
        var pendentes = await banco.CaixaDeEntrada.Where(i => i.UsuarioId == usuario!.Id).GroupBy(i => i.ObraId)
            .Select(g => new { Obra = g.Key, Quantos = g.Count() }).ToListAsync();
        return Results.Ok(obras.Select(o => new { projeto = o.Projeto, nome = o.Nome, atualizadoEm = o.AtualizadoEm,
            pendentes = pendentes.FirstOrDefault(p => p.Obra == o.Id)?.Quantos ?? 0 }));
    }

    /// <summary>Marca a obra (ou atualiza o retrato dela): o Telegram passa a enxergá-la.</summary>
    private static async Task<IResult> Marcar(Guid projeto, HttpRequest http, ClaimsPrincipal quem, BancoDoServidor banco, TimeProvider relogio, CancellationToken c)
    {
        var (usuario, falha) = await Conta(quem, banco, soProfissional: true);
        if (falha is not null)
            return falha;
        if (http.ContentLength > MegabytesPorRetrato * 1024L * 1024L)
            return Contas.Falha(413, $"O retrato da obra passa de {MegabytesPorRetrato} MB.");
        PedidoDeObra? pedido;
        try
        {
            pedido = await JsonSerializer.DeserializeAsync<PedidoDeObra>(http.Body, AtendenteDoTelegram.Json, c);
        }
        catch (JsonException)
        {
            pedido = null;
        }
        var nome = pedido?.Nome?.Trim() ?? "";
        if (pedido is null || pedido.Retrato.ValueKind != JsonValueKind.Object || nome.Length == 0)
            return Contas.Falha(400, "Pedido incompleto: falta o nome ou o retrato da obra.");
        var obra = await banco.Obras.FirstOrDefaultAsync(o => o.UsuarioId == usuario!.Id && o.Projeto == projeto, c);
        if (obra is null)
        {
            obra = new ObraNoServidor { Id = Guid.NewGuid(), UsuarioId = usuario!.Id, Projeto = projeto };
            banco.Obras.Add(obra);
        }
        (obra.Nome, obra.Retrato, obra.AtualizadoEm) = (nome.Length > 200 ? nome[..200] : nome, pedido.Retrato.GetRawText(), relogio.GetUtcNow().UtcDateTime);
        await banco.SaveChangesAsync(c);
        return Results.Ok(new { projeto, nome = obra.Nome, atualizadoEm = obra.AtualizadoEm });
    }

    /// <summary>Tira a obra do Telegram (e o que esperava na caixa dela).</summary>
    private static async Task<IResult> Desmarcar(Guid projeto, ClaimsPrincipal quem, BancoDoServidor banco)
    {
        var (usuario, falha) = await Conta(quem, banco);
        if (falha is not null)
            return falha;
        if (await banco.Obras.FirstOrDefaultAsync(o => o.UsuarioId == usuario!.Id && o.Projeto == projeto) is { } obra)
        {
            await banco.CaixaDeEntrada.Where(i => i.ObraId == obra.Id).ExecuteDeleteAsync();
            await banco.VinculosDoTelegram.Where(v => v.ObraAtiva == obra.Id).ExecuteUpdateAsync(v => v.SetProperty(x => x.ObraAtiva, (Guid?)null));
            banco.Obras.Remove(obra);
            await banco.SaveChangesAsync();
        }
        return Results.NoContent();
    }

    /// <summary>O que espera na caixa da obra (sem os bytes das fotos).</summary>
    private static async Task<IResult> Caixa(Guid projeto, ClaimsPrincipal quem, BancoDoServidor banco)
    {
        var (usuario, falha) = await Conta(quem, banco);
        if (falha is not null)
            return falha;
        if (await banco.Obras.FirstOrDefaultAsync(o => o.UsuarioId == usuario!.Id && o.Projeto == projeto) is not { } obra)
            return Results.Ok(Array.Empty<object>());
        var itens = await banco.CaixaDeEntrada.Where(i => i.ObraId == obra.Id).OrderBy(i => i.CriadoEm)
            .Select(i => new { i.Id, i.Tipo, i.CriadoEm, i.Texto, i.Conteudo, i.Tarefas, i.NomeDaFoto }).ToListAsync();
        return Results.Ok(itens.Select(i => new
        {
            id = i.Id, tipo = i.Tipo, criadoEm = i.CriadoEm, texto = i.Texto, nomeDaFoto = i.NomeDaFoto,
            atualizacoes = i.Conteudo is null ? (JsonElement?)null : JsonDocument.Parse(i.Conteudo).RootElement.Clone(),
            tarefas = i.Tarefas is null ? (JsonElement?)null : JsonDocument.Parse(i.Tarefas).RootElement.Clone(),
        }));
    }

    private static async Task<IResult> Foto(Guid id, ClaimsPrincipal quem, BancoDoServidor banco)
    {
        var (usuario, falha) = await Conta(quem, banco);
        if (falha is not null)
            return falha;
        var item = await banco.CaixaDeEntrada.Where(i => i.Id == id && i.UsuarioId == usuario!.Id).Select(i => new { i.Foto }).FirstOrDefaultAsync();
        return item?.Foto is { } bytes ? Results.Bytes(bytes, "image/jpeg") : Contas.Falha(404, "Essa foto não está mais na caixa.");
    }

    /// <summary>O programa levou o item: some do servidor.</summary>
    private static async Task<IResult> Levar(Guid id, ClaimsPrincipal quem, BancoDoServidor banco)
    {
        var (usuario, falha) = await Conta(quem, banco);
        if (falha is not null)
            return falha;
        await banco.CaixaDeEntrada.Where(i => i.Id == id && i.UsuarioId == usuario!.Id).ExecuteDeleteAsync();
        return Results.NoContent();
    }
}
