using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectPlanner.Servidor.Dados;
using ProjectPlanner.Servidor.Servicos;

namespace ProjectPlanner.Servidor.Rotas;

/// <summary>
/// Bloco 47: a cobrança pelo Mercado Pago. O programa pede um link (assinar o Profissional ou comprar um pacote de créditos) e
/// o abre no navegador; o Mercado Pago avisa o servidor (webhook) e o servidor confere o aviso perguntando à API do Mercado
/// Pago, com o access token dele. Só o que a API diz muda o plano ou soma créditos: um aviso falso não tem efeito.
/// </summary>
public static class Pagamentos
{
    public const string Desligados = "Os pagamentos ainda não estão disponíveis. Tente mais tarde.";

    public static void Mapear(WebApplication app)
    {
        var conta = app.MapGroup("/v1/pagamentos").RequireAuthorization().RequireRateLimiting(LimitesDeUso.Contas);
        conta.MapGet("", Situacao);
        conta.MapPost("/assinatura", Assinar);
        conta.MapPost("/assinatura/cancelar", Cancelar);
        conta.MapPost("/creditos", ComprarCreditos);
        app.MapPost("/v1/pagamentos/webhook", Webhook).RequireRateLimiting(LimitesDeUso.Contas);
        app.MapGet("/pagamento", () => Results.Content(PaginaDeRetorno, "text/html; charset=utf-8"));
    }

    /// <summary>A página para onde o Mercado Pago volta depois do pagamento.</summary>
    public const string PaginaDeRetorno = """
        <!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
        <title>ProjectPlanner</title><style>body{font-family:system-ui,sans-serif;max-width:32rem;margin:15vh auto;padding:0 16px;line-height:1.5;
        color:#1f2933;background:#fff}@media(prefers-color-scheme:dark){body{color:#e4e7eb;background:#111827}}</style></head>
        <body><h1>Obrigado!</h1><p>Assim que o Mercado Pago confirmar o pagamento, o seu plano e os seus créditos são atualizados
        (com Pix, em instantes; com cartão, pode levar alguns minutos).</p><p>Volte ao ProjectPlanner e clique em
        <strong>Atualizar</strong> em Conta &gt; Minha conta. Pode fechar esta página.</p></body></html>
        """;

    private static IResult Falha(int status, string mensagem) => Contas.Falha(status, mensagem);

    private static async Task<IResult> Situacao(ClaimsPrincipal quem, BancoDoServidor banco, IOptions<OpcoesDoServidor> opcoes)
    {
        if (await Usuarios.Atual(quem, banco) is not { } usuario)
            return Falha(401, "Sessão vencida: entre de novo.");
        var config = opcoes.Value;
        var assinatura = await UltimaAssinatura(banco, usuario.Id);
        return Results.Ok(new
        {
            disponivel = config.MercadoPago.Ligado,
            modoDeTeste = config.MercadoPago.ModoDeTeste,
            plano = usuario.Plano,
            profissionalAte = usuario.ProfissionalAte,
            precoMensal = config.Financeiro.PrecoProfissionalEmReais,
            creditosDoProfissional = config.Planos.ProfissionalCreditosPorMes,
            creditosDoPacote = config.MercadoPago.CreditosDoPacote,
            precoDoPacote = config.MercadoPago.PrecoDoPacoteEmReais,
            creditosAvulsos = Math.Round(config.Planos.Creditos(usuario.MicrodolaresAvulsos), 1, MidpointRounding.AwayFromZero),
            assinatura = assinatura is null ? null : assinatura.Status,
            proximaCobranca = assinatura?.ProximaCobranca,
        });
    }

    private static Task<Assinatura?> UltimaAssinatura(BancoDoServidor banco, Guid usuario) =>
        banco.Assinaturas.Where(a => a.UsuarioId == usuario).OrderByDescending(a => a.Status == StatusDoMercadoPago.Autorizada)
            .ThenByDescending(a => a.AtualizadaEm).FirstOrDefaultAsync();

    private static async Task<IResult> Assinar(ClaimsPrincipal quem, BancoDoServidor banco, IOptions<OpcoesDoServidor> opcoes, MercadoPago mercadoPago,
        TimeProvider relogio, CancellationToken c)
    {
        if (await Usuarios.Atual(quem, banco) is not { } usuario)
            return Falha(401, "Sessão vencida: entre de novo.");
        var config = opcoes.Value;
        if (!config.MercadoPago.Ligado)
            return Falha(503, Desligados);
        if (await banco.Assinaturas.AnyAsync(a => a.UsuarioId == usuario.Id && a.Status == StatusDoMercadoPago.Autorizada, c))
            return Falha(409, "Você já assina o Profissional.");
        var valor = config.Financeiro.PrecoProfissionalEmReais;
        LinkDePagamento link;
        try
        {
            link = await mercadoPago.CriarAssinaturaAsync(usuario.Id, usuario.Email, valor, c);
        }
        catch (ErroDoMercadoPagoException ex)
        {
            return Falha(502, ex.Message);
        }
        var agora = relogio.GetUtcNow().UtcDateTime;
        banco.Assinaturas.Add(new Assinatura
        {
            Id = link.Id, UsuarioId = usuario.Id, Status = StatusDoMercadoPago.Pendente, ValorEmReais = valor, CriadaEm = agora, AtualizadaEm = agora,
        });
        await banco.SaveChangesAsync(c);
        return Results.Ok(new { url = link.Url });
    }

    private static async Task<IResult> Cancelar(ClaimsPrincipal quem, BancoDoServidor banco, MercadoPago mercadoPago, TimeProvider relogio, CancellationToken c)
    {
        if (await Usuarios.Atual(quem, banco) is not { } usuario)
            return Falha(401, "Sessão vencida: entre de novo.");
        var ativa = await banco.Assinaturas.FirstOrDefaultAsync(a => a.UsuarioId == usuario.Id && a.Status == StatusDoMercadoPago.Autorizada, c);
        if (ativa is null)
            return Falha(404, "Você não tem uma assinatura ativa.");
        try
        {
            await mercadoPago.CancelarAssinaturaAsync(ativa.Id, c);
        }
        catch (ErroDoMercadoPagoException ex)
        {
            return Falha(502, ex.Message);
        }
        var agora = relogio.GetUtcNow().UtcDateTime;
        Conciliacao.Encerrar(usuario, ativa, agora);
        ativa.Status = StatusDoMercadoPago.Cancelada;
        ativa.AtualizadaEm = agora;
        await banco.SaveChangesAsync(c);
        return Results.Ok(new { profissionalAte = usuario.ProfissionalAte });
    }

    private static async Task<IResult> ComprarCreditos(ClaimsPrincipal quem, BancoDoServidor banco, IOptions<OpcoesDoServidor> opcoes, MercadoPago mercadoPago,
        CancellationToken c)
    {
        if (await Usuarios.Atual(quem, banco) is not { } usuario)
            return Falha(401, "Sessão vencida: entre de novo.");
        var config = opcoes.Value.MercadoPago;
        if (!config.Ligado)
            return Falha(503, Desligados);
        try
        {
            var link = await mercadoPago.CriarCompraDeCreditosAsync(usuario.Id, config.CreditosDoPacote, config.PrecoDoPacoteEmReais, c);
            return Results.Ok(new { url = link.Url });
        }
        catch (ErroDoMercadoPagoException ex)
        {
            return Falha(502, ex.Message);
        }
    }

    /// <summary>
    /// O aviso do Mercado Pago: <c>{"type": "payment", "data": {"id": "123"}}</c> (ou, no formato antigo, <c>?topic=payment&amp;id=123</c>).
    /// Com a assinatura secreta configurada, um aviso com <c>x-signature</c> errada é recusado. Responde 200 para o Mercado Pago
    /// não repetir; 500 (ele repete depois) se a API do Mercado Pago falhou.
    /// </summary>
    private static async Task<IResult> Webhook(HttpRequest http, BancoDoServidor banco, IOptions<OpcoesDoServidor> opcoes, MercadoPago mercadoPago,
        TimeProvider relogio, ILoggerFactory logs, CancellationToken c)
    {
        var config = opcoes.Value.MercadoPago;
        var log = logs.CreateLogger(typeof(Pagamentos));
        if (!config.Ligado)
            return Results.Ok();
        string? tipo = http.Query["type"].FirstOrDefault() ?? http.Query["topic"].FirstOrDefault();
        string? id = http.Query["data.id"].FirstOrDefault() ?? http.Query["id"].FirstOrDefault();
        try
        {
            using var corpo = await JsonDocument.ParseAsync(http.Body, cancellationToken: c);
            var raiz = corpo.RootElement;
            if (raiz.ValueKind == JsonValueKind.Object)
            {
                if (raiz.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String)
                    tipo = t.GetString();
                if (raiz.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object && d.TryGetProperty("id", out var i))
                    id = i.ValueKind == JsonValueKind.Number ? i.GetRawText() : i.GetString();
            }
        }
        catch (JsonException)
        {
            // sem corpo: o formato antigo, só na consulta
        }
        var assinado = http.Headers["x-signature"].ToString();
        if (!string.IsNullOrWhiteSpace(config.SegredoDoWebhook) && assinado.Length > 0
            && !AssinaturaDoWebhook.Valida(config.SegredoDoWebhook, assinado, http.Headers["x-request-id"].ToString(), http.Query["data.id"].FirstOrDefault() ?? id))
        {
            log.LogWarning("Aviso do Mercado Pago com assinatura errada ({Tipo} {Id})", tipo, id);
            return Results.Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64)
            return Results.Ok();
        try
        {
            var agora = relogio.GetUtcNow().UtcDateTime;
            switch (tipo)
            {
                case "subscription_preapproval" or "preapproval":
                    await Conciliacao.AssinaturaAsync(banco, await mercadoPago.AssinaturaAsync(id, c), agora, log, c);
                    break;
                case "payment":
                    await Conciliacao.PagamentoAsync(banco, opcoes.Value.Planos, await mercadoPago.PagamentoAsync(id, c), agora, log, c);
                    break;
            }
        }
        catch (ErroDoMercadoPagoException)
        {
            return Results.StatusCode(500); // o Mercado Pago tenta de novo depois
        }
        catch (DbUpdateException ex)
        {
            log.LogWarning(ex, "Aviso do Mercado Pago repetido ao mesmo tempo ({Tipo} {Id})", tipo, id);
            return Results.StatusCode(500);
        }
        return Results.Ok();
    }
}

public static class StatusDoMercadoPago
{
    public const string Pendente = "pending";
    public const string Autorizada = "authorized";
    public const string Pausada = "paused";
    public const string Cancelada = "cancelled";
    public const string Aprovado = "approved";
}

/// <summary>Bloco 47: o que muda na conta com o que a API do Mercado Pago informou.</summary>
public static class Conciliacao
{
    public static async Task AssinaturaAsync(BancoDoServidor banco, AssinaturaNoMercadoPago informada, DateTime agora, ILogger log, CancellationToken c)
    {
        if (!Guid.TryParse(informada.Referencia, out var dono) || await banco.Usuarios.FindAsync([dono], c) is not { } usuario)
        {
            log.LogWarning("Assinatura do Mercado Pago sem conta conhecida ({Id})", informada.Id);
            return;
        }
        var assinatura = await banco.Assinaturas.FindAsync([informada.Id], c);
        if (assinatura is null)
        {
            assinatura = new Assinatura { Id = informada.Id, UsuarioId = usuario.Id, CriadaEm = agora };
            banco.Assinaturas.Add(assinatura);
        }
        if (assinatura.UsuarioId != usuario.Id)
        {
            log.LogWarning("Assinatura do Mercado Pago de outra conta ({Id})", informada.Id);
            return;
        }
        assinatura.Status = informada.Status;
        assinatura.AtualizadaEm = agora;
        assinatura.ValorEmReais = informada.Valor > 0 ? informada.Valor : assinatura.ValorEmReais;
        assinatura.ProximaCobranca = informada.ProximaCobranca ?? assinatura.ProximaCobranca;
        if (informada.Status == StatusDoMercadoPago.Autorizada)
        {
            usuario.Plano = Planos.Profissional;
            usuario.ProfissionalAte = null;
        }
        else if (informada.Status is StatusDoMercadoPago.Cancelada or StatusDoMercadoPago.Pausada
                 && !await banco.Assinaturas.AnyAsync(a => a.UsuarioId == usuario.Id && a.Id != informada.Id && a.Status == StatusDoMercadoPago.Autorizada, c))
            Encerrar(usuario, assinatura, agora);
        await banco.SaveChangesAsync(c);
    }

    /// <summary>
    /// A assinatura acabou: o Profissional vale até a próxima cobrança que não vai acontecer (o mês já pago); sem ela, acaba
    /// agora. Um Profissional dado pelo admin (sem assinatura) não passa por aqui.
    /// </summary>
    public static void Encerrar(Usuario usuario, Assinatura assinatura, DateTime agora)
    {
        if (usuario.Plano != Planos.Profissional)
            return;
        if (assinatura.ProximaCobranca is { } ate && ate > agora)
            usuario.ProfissionalAte = ate;
        else
        {
            usuario.Plano = Planos.Gratis;
            usuario.ProfissionalAte = null;
        }
    }

    public static async Task PagamentoAsync(BancoDoServidor banco, OpcoesDosPlanos planos, PagamentoNoMercadoPago informado, DateTime agora, ILogger log,
        CancellationToken c)
    {
        if (informado.Status != StatusDoMercadoPago.Aprovado || await banco.Pagamentos.FindAsync([informado.Id], c) is not null)
            return;
        var referencia = informado.Referencia ?? "";
        long creditos = 0;
        Guid dono;
        if (referencia.StartsWith(MercadoPago.PrefixoDosCreditos, StringComparison.Ordinal))
        {
            // creditos:{usuario}:{creditos}:{aleatório}
            var partes = referencia[MercadoPago.PrefixoDosCreditos.Length..].Split(':');
            if (partes.Length < 2 || !Guid.TryParse(partes[0], out dono) || !long.TryParse(partes[1], NumberStyles.None, CultureInfo.InvariantCulture, out creditos)
                || creditos <= 0)
            {
                log.LogWarning("Pagamento de créditos com referência estranha ({Id})", informado.Id);
                return;
            }
        }
        else if (!Guid.TryParse(referencia, out dono))
            return; // um pagamento que não é do ProjectPlanner
        if (await banco.Usuarios.FindAsync([dono], c) is not { } usuario)
        {
            log.LogWarning("Pagamento do Mercado Pago sem conta conhecida ({Id})", informado.Id);
            return;
        }
        banco.Pagamentos.Add(new Pagamento
        {
            Id = informado.Id, UsuarioId = usuario.Id, Tipo = creditos > 0 ? Pagamento.DeCreditos : Pagamento.DeAssinatura, ValorEmReais = informado.Valor,
            Creditos = creditos, Quando = agora,
        });
        if (creditos > 0)
            usuario.MicrodolaresAvulsos += planos.Microdolares(creditos);
        await banco.SaveChangesAsync(c);
    }
}

/// <summary>Bloco 47: de hora em hora, as contas cujo Profissional cancelado venceu voltam ao Grátis.</summary>
public sealed class ExpiracaoDosPlanos(IServiceScopeFactory escopos, TimeProvider relogio, ILogger<ExpiracaoDosPlanos> log) : BackgroundService
{
    public static async Task<int> ExpirarAsync(BancoDoServidor banco, DateTime agora, CancellationToken c)
    {
        var vencidas = await banco.Usuarios.Where(u => u.Plano == Planos.Profissional && u.ProfissionalAte != null && u.ProfissionalAte <= agora).ToListAsync(c);
        foreach (var usuario in vencidas)
        {
            usuario.Plano = Planos.Gratis;
            usuario.ProfissionalAte = null;
        }
        await banco.SaveChangesAsync(c);
        return vencidas.Count;
    }

    protected override async Task ExecuteAsync(CancellationToken parar)
    {
        using var relogioDeHora = new PeriodicTimer(TimeSpan.FromHours(1), relogio);
        do
        {
            try
            {
                using var escopo = escopos.CreateScope();
                var quantas = await ExpirarAsync(escopo.ServiceProvider.GetRequiredService<BancoDoServidor>(), relogio.GetUtcNow().UtcDateTime, parar);
                if (quantas > 0)
                    log.LogInformation("{Quantas} contas voltaram ao Grátis (assinatura cancelada e vencida)", quantas);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Falha ao conferir os planos vencidos");
            }
        } while (await relogioDeHora.WaitForNextTickAsync(parar));
    }
}
