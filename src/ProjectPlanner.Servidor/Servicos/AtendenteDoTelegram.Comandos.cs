using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ProjectPlanner.IA;
using ProjectPlanner.Servidor.Dados;

namespace ProjectPlanner.Servidor.Servicos;

/// <summary>
/// Bloco 83: a ajuda com botões (um assunto por botão, na mesma mensagem), as consultas que respondem sem a IA (/resumo, /hoje,
/// /atrasadas), a caixa (/caixa) e o desfazer (/desfazer e o botão Desfazer embaixo do que foi anotado): o que ainda está na caixa
/// sai dela; o que o programa já trouxe se desfaz lá.
/// </summary>
public sealed partial class AtendenteDoTelegram
{
    /// <summary>Os dados do botão Desfazer ("cx:" e o id do item da caixa).</summary>
    public const string PrefixoDaCaixa = "cx:";

    /// <summary>Bloco 85: os botões do /obra ("ob:2" é a obra 2 da lista).</summary>
    public const string PrefixoDaObra = "ob:";

    /// <summary>As obras da lista do /obra, uma por linha (até 10; depois disso, pelo número).</summary>
    private static List<BotaoDoTelegram> BotoesDasObras(IReadOnlyList<ObraNoServidor> obras, Guid? atual) =>
        [.. obras.Take(10).Select((o, i) => new BotaoDoTelegram((o.Id == atual ? "✓ " : "🏗 ") + (o.Nome.Length > 40 ? o.Nome[..39] + "…" : o.Nome),
            PrefixoDaObra + (i + 1).ToString(CultureInfo.InvariantCulture), i))];

    public const string NadaParaDesfazer =
        "Não há nada seu esperando na caixa. O que o programa já trouxe se desfaz lá (Editar > Desfazer).";

    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Os comandos que respondem pelo retrato, sem a IA.</summary>
    private static bool EhConsulta(string? comando) => comando is "/resumo" or "/status" or "/hoje" or "/atrasadas";

    private static BotaoDoTelegram BotaoDesfazer(ItemDaCaixa item) => new("↩️ Desfazer", PrefixoDaCaixa + item.Id.ToString("N"));

    /// <summary>/ajuda: o começo da ajuda, com um botão por assunto e os atalhos.</summary>
    private Task AjudaAsync(long chatId, bool grupo, string? obra, CancellationToken c) =>
        telegram.EnviarComBotoesAsync(chatId, AjudaDoTelegram.Inicio(grupo, obra, NomeDoBot), AjudaDoTelegram.BotoesDoMenu, c);

    /// <summary>/resumo, /hoje e /atrasadas, pelo último retrato da obra.</summary>
    private async Task ConsultaAsync(long chatId, ObraNoServidor obra, string comando, CancellationToken c)
    {
        if (LerRetrato(obra) is not { } retrato)
        {
            await telegram.EnviarAsync(chatId, "Não deu para ler os dados desta obra. No programa, salve a obra de novo (ela é mandada ao salvar).", c);
            return;
        }
        var hoje = DiaNoBrasil(relogio.GetUtcNow().UtcDateTime);
        var salvoEm = obra.AtualizadoEm.AddHours(-3); // no horário de Brasília
        var texto = comando switch
        {
            "/hoje" => ConsultasDaObra.Hoje(obra.Nome, retrato, hoje, salvoEm),
            "/atrasadas" => ConsultasDaObra.Atrasadas(obra.Nome, retrato, hoje, salvoEm),
            _ => ConsultasDaObra.Resumo(obra.Nome, retrato, hoje, salvoEm, await CaixaEmNumerosAsync(obra, c)),
        };
        await telegram.EnviarAsync(chatId, texto, c);
    }

    private async Task<ConsultasDaObra.CaixaEmNumeros> CaixaEmNumerosAsync(ObraNoServidor obra, CancellationToken c)
    {
        var tipos = await banco.CaixaDeEntrada.Where(i => i.ObraId == obra.Id).Select(i => i.Tipo).ToListAsync(c);
        return new(tipos.Count(t => t == ItemDaCaixa.DeAtualizacoes), tipos.Count(t => t == ItemDaCaixa.DeFoto), tipos.Count(t => t == ItemDaCaixa.DeOrdem));
    }

    /// <summary>/caixa: o que espera o programa (no privado, tudo da obra; num grupo, só o que veio do grupo).</summary>
    private async Task CaixaAsync(long chatId, ObraNoServidor obra, bool grupo, CancellationToken c)
    {
        var itens = await banco.CaixaDeEntrada.Where(i => i.ObraId == obra.Id && (!grupo || i.ChatId == chatId)).OrderBy(i => i.CriadoEm)
            .Select(i => new { i.Tipo, i.CriadoEm, i.Texto, i.Conteudo }).ToListAsync(c);
        if (itens.Count == 0)
        {
            await telegram.EnviarAsync(chatId, grupo
                ? $"📥 Nada deste grupo espera o programa na caixa da obra {obra.Nome}: o que foi mandado já está lá."
                : $"📥 A caixa da obra {obra.Nome} está vazia: tudo o que foi mandado já está no programa.", c);
            return;
        }
        var linhas = itens.TakeLast(ConsultasDaObra.ItensPorLista).Select(i =>
        {
            var quando = i.CriadoEm.AddHours(-3).ToString("dd/MM HH:mm", Brasil);
            var texto = i.Texto.Trim().TrimEnd(':').Trim();
            var oQue = i.Tipo switch
            {
                ItemDaCaixa.DeFoto => "📷 foto" + (texto.Length == 0 ? "" : $" \"{Curto(texto)}\""),
                ItemDaCaixa.DeOrdem => $"📋 {Curto(texto)}",
                _ => $"\"{Curto(texto)}\" ({Atualizacoes(QuantasAtualizacoes(i.Conteudo))})",
            };
            return $"• {quando} · {oQue}";
        });
        var mais = itens.Count > ConsultasDaObra.ItensPorLista ? $"\n(e mais {itens.Count - ConsultasDaObra.ItensPorLista}, mais antigos)" : "";
        await telegram.EnviarAsync(chatId, $"📥 Na caixa da obra {obra.Nome}, esperando o programa: {itens.Count}\n" + string.Join("\n", linhas) + mais +
            (grupo ? "\n\nO dono traz tudo para o programa e aplica." : "\n\nNo programa: Conta > Buscar do Telegram.") +
            " /desfazer tira da caixa a última coisa que você mandou.", c);
    }

    /// <summary>/desfazer: tira da caixa a última atualização ou foto que a pessoa mandou deste chat (se o programa ainda não a trouxe).</summary>
    private async Task DesfazerAsync(long chatId, long pessoaId, CancellationToken c)
    {
        var item = await banco.CaixaDeEntrada
            .Where(i => i.ChatId == chatId && i.PessoaId == pessoaId && (i.Tipo == ItemDaCaixa.DeAtualizacoes || i.Tipo == ItemDaCaixa.DeFoto))
            .OrderByDescending(i => i.CriadoEm).FirstOrDefaultAsync(c);
        if (item is null)
        {
            await telegram.EnviarAsync(chatId, NadaParaDesfazer, c);
            return;
        }
        banco.CaixaDeEntrada.Remove(item);
        await banco.SaveChangesAsync(c);
        if (item.MensagemId is { } mensagem)
            await telegram.TirarBotoesAsync(chatId, mensagem, c);
        await telegram.EnviarAsync(chatId, $"↩️ Tirei da caixa: {Descricao(item)}.", c);
    }

    /// <summary>
    /// Bloco 83: os botões que não são de OS: os assuntos da ajuda, os atalhos dos comandos e o Desfazer. Falso se não é um deles.
    /// </summary>
    private async Task<bool> AtenderOutroBotaoAsync(string consulta, string dados, JsonElement de, long pessoaId, JsonElement mensagem, JsonElement chat,
        long chatId, CancellationToken c)
    {
        var grupo = Texto(chat, "type") is "group" or "supergroup";
        var idDaMensagem = mensagem.TryGetProperty("message_id", out var m) && m.TryGetInt64(out var n) ? n : (long?)null;
        if (dados.StartsWith(AjudaDoTelegram.PrefixoDoAssunto, StringComparison.Ordinal))
        {
            var chave = dados[AjudaDoTelegram.PrefixoDoAssunto.Length..];
            var (texto, botoes) = chave == AjudaDoTelegram.Menu
                ? (AjudaDoTelegram.Inicio(grupo, await NomeDaObraDoChatAsync(chatId, grupo, c), NomeDoBot), AjudaDoTelegram.BotoesDoMenu)
                : (AjudaDoTelegram.Texto(chave, grupo, NomeDoBot), AjudaDoTelegram.BotoesDoAssunto);
            await telegram.ResponderAoBotaoAsync(consulta, texto is null ? "Assunto desconhecido." : "", c);
            if (texto is not null && idDaMensagem is { } id)
                await telegram.EditarAsync(chatId, id, texto, botoes, c);
            return true;
        }
        if (dados.StartsWith(AjudaDoTelegram.PrefixoDoComando, StringComparison.Ordinal))
        {
            var comando = dados[AjudaDoTelegram.PrefixoDoComando.Length..];
            await telegram.ResponderAoBotaoAsync(consulta, AjudaDoTelegram.Atalhos.Contains(comando) ? "" : "Comando desconhecido.", c);
            if (AjudaDoTelegram.Atalhos.Contains(comando))
                await ComoSeDigitasseAsync(chat, de, "/" + comando, c);
            return true;
        }
        if (dados.StartsWith(PrefixoDaObra, StringComparison.Ordinal)) // Bloco 85: a obra tocada na lista do /obra
        {
            var numero = dados[PrefixoDaObra.Length..];
            await telegram.ResponderAoBotaoAsync(consulta, int.TryParse(numero, out _) ? "" : "Botão desconhecido.", c);
            // A lista fica com os botões (no grupo, quem não pode ligar a obra também toca; tocar de novo só escolhe de novo).
            if (int.TryParse(numero, out _))
                await ComoSeDigitasseAsync(chat, de, "/obra " + numero, c);
            return true;
        }
        if (dados.StartsWith(PrefixoDaCaixa, StringComparison.Ordinal))
        {
            await DesfazerPeloBotaoAsync(consulta, dados[PrefixoDaCaixa.Length..], mensagem, chatId, idDaMensagem, pessoaId, NomeDe(de), grupo, c);
            return true;
        }
        return false;
    }

    // O mesmo caminho de quem digita o comando (o limite por minuto, a obra do chat, o papel no grupo, quem pode ligar o grupo).
    private async Task ComoSeDigitasseAsync(JsonElement chat, JsonElement de, string texto, CancellationToken c)
    {
        var aviso = new JsonObject
        {
            ["message"] = new JsonObject { ["chat"] = JsonNode.Parse(chat.GetRawText()), ["from"] = JsonNode.Parse(de.GetRawText()), ["text"] = texto },
        };
        using var json = JsonDocument.Parse(aviso.ToJsonString());
        await AtenderAsync(json.RootElement, c);
    }

    private async Task DesfazerPeloBotaoAsync(string consulta, string idDoItem, JsonElement mensagem, long chatId, long? idDaMensagem, long pessoaId,
        string nome, bool grupo, CancellationToken c)
    {
        var item = Guid.TryParseExact(idDoItem, "N", out var id) ? await banco.CaixaDeEntrada.FirstOrDefaultAsync(i => i.Id == id, c) : null;
        if (item is null || item.ChatId != chatId)
        {
            await telegram.ResponderAoBotaoAsync(consulta, "Isto já foi para o programa (ou já foi desfeito): se precisar, desfaça lá.", c);
            if (idDaMensagem is { } semItem)
                await telegram.TirarBotoesAsync(chatId, semItem, c);
            return;
        }
        if (item.PessoaId != pessoaId && !(grupo && await GerenciaOGrupoAsync(chatId, pessoaId, c)))
        {
            await telegram.ResponderAoBotaoAsync(consulta, "Só quem mandou (ou quem é da engenharia) desfaz.", c);
            return;
        }
        banco.CaixaDeEntrada.Remove(item);
        await banco.SaveChangesAsync(c);
        await telegram.ResponderAoBotaoAsync(consulta, "Desfeito: saiu da caixa.", c);
        if (idDaMensagem is { } idMsg)
            await telegram.EditarAsync(chatId, idMsg, (Texto(mensagem, "text") ?? "").TrimEnd() + $"\n\n↩️ Desfeito{(grupo ? $" por {nome}" : "")}: saiu da caixa.", [], c);
    }

    // O dono da conta do grupo ou quem é da engenharia nele.
    private async Task<bool> GerenciaOGrupoAsync(long chatId, long pessoaId, CancellationToken c)
    {
        if (await banco.GruposDoTelegram.FindAsync([chatId], c) is not { } grupo)
            return false;
        if (await banco.VinculosDoTelegram.AnyAsync(v => v.ChatId == pessoaId && v.UsuarioId == grupo.UsuarioId, c))
            return true;
        return await banco.MembrosDosGrupos.FindAsync([chatId, pessoaId], c) is { Gerencia: true };
    }

    // A obra do chat, para o começo da ajuda (a do grupo; no privado, a escolhida com /obra ou a primeira).
    private async Task<string?> NomeDaObraDoChatAsync(long chatId, bool grupo, CancellationToken c)
    {
        if (grupo)
            return await banco.GruposDoTelegram.Where(g => g.ChatId == chatId)
                .Join(banco.Obras, g => g.ObraId, o => o.Id, (g, o) => o.Nome).FirstOrDefaultAsync(c);
        if (await banco.VinculosDoTelegram.FirstOrDefaultAsync(v => v.ChatId == chatId, c) is not { } vinculo)
            return null;
        var obras = await banco.Obras.Where(o => o.UsuarioId == vinculo.UsuarioId).OrderBy(o => o.Nome).Select(o => new { o.Id, o.Nome }).ToListAsync(c);
        return (obras.FirstOrDefault(o => o.Id == vinculo.ObraAtiva) ?? obras.FirstOrDefault())?.Nome;
    }

    private static ContextoDoProjeto? LerRetrato(ObraNoServidor obra)
    {
        try
        {
            return JsonSerializer.Deserialize<ContextoDoProjeto>(obra.Retrato, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // "a alvenaria está em 60%" (1 atualização), a foto "laje concretada".
    private static string Descricao(ItemDaCaixa item)
    {
        var texto = Curto(item.Texto.Trim().TrimEnd(':').Trim());
        return item.Tipo == ItemDaCaixa.DeFoto
            ? texto.Length == 0 ? "a foto" : $"a foto \"{texto}\""
            : $"\"{texto}\" ({Atualizacoes(QuantasAtualizacoes(item.Conteudo))})";
    }

    private static int QuantasAtualizacoes(string? conteudo)
    {
        try
        {
            using var json = JsonDocument.Parse(conteudo ?? "[]");
            return json.RootElement.ValueKind == JsonValueKind.Array ? json.RootElement.GetArrayLength() : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static string Atualizacoes(int n) => n == 1 ? "1 atualização" : $"{n} atualizações";

    private static string Curto(string texto) => texto.Length <= 80 ? texto : texto[..79] + "…";
}
