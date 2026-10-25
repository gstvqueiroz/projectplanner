using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProjectPlanner.Servidor.Dados;

namespace ProjectPlanner.Servidor.Servicos;

/// <summary>Bloco 59: o que vai na caixa quando a equipe mexe numa OS pelo Telegram.</summary>
/// <param name="Acao">aceitar, recusar, iniciar ou concluir.</param>
public sealed record EventoDaOrdemNoTelegram(Guid Ordem, string Acao, string Autor, DateTime Quando, string? Motivo = null);

/// <summary>Uma mensagem de uma OS num grupo.</summary>
public sealed record MensagemDaOrdem(long Chat, long Mensagem);

/// <summary>
/// Bloco 59: as ordens de serviço nos grupos da obra. O programa publica a OS (o texto e a situação) e o bot põe a mensagem
/// com os botões em cada grupo da obra: emitida, Aceitar e Recusar; aceita, Comecei e Terminei; em execução, Terminei. Quem é
/// do campo ou da engenharia toca no botão: a situação muda na hora (a mensagem se atualiza) e o aviso vai para a caixa, e o
/// programa aplica na OS ao buscar do Telegram. Recusar pede o motivo (/recusar 14 e o motivo); /os lista as OS abertas e
/// /os 14 manda a de novo. Nada disso usa a IA.
/// </summary>
public sealed partial class AtendenteDoTelegram
{
    public const string PrefixoDosBotoes = "os:";

    private static readonly JsonSerializerOptions JsonDasMensagens = new(JsonSerializerDefaults.Web);

    /// <summary>Publica (ou atualiza) a OS em cada grupo da obra; devolve em quantos grupos ela está.</summary>
    public async Task<int> PublicarAsync(OrdemNoTelegram ordem, CancellationToken c)
    {
        var grupos = await banco.GruposDoTelegram.Where(g => g.ObraId == ordem.ObraId && g.UsuarioId == ordem.UsuarioId).Select(g => g.ChatId).ToListAsync(c);
        var mensagens = Mensagens(ordem).Where(m => grupos.Contains(m.Chat)).ToList();
        var (texto, botoes) = (TextoDa(ordem), BotoesDa(ordem));
        // Uma OS que já terminou (ou nem saiu) só atualiza as mensagens que existem: não aparece nova no grupo.
        var emAndamento = ordem.Situacao is OrdemNoTelegram.Emitida or OrdemNoTelegram.Aceita or OrdemNoTelegram.EmExecucao
            or OrdemNoTelegram.Concluida or OrdemNoTelegram.Recusada;
        foreach (var chat in grupos)
        {
            if (mensagens.FirstOrDefault(m => m.Chat == chat) is { } existente)
                await telegram.EditarAsync(chat, existente.Mensagem, texto, botoes, c);
            else if (emAndamento && await telegram.EnviarComBotoesAsync(chat, texto, botoes, c) is { } id)
                mensagens.Add(new MensagemDaOrdem(chat, id));
        }
        ordem.Mensagens = JsonSerializer.Serialize(mensagens, JsonDasMensagens);
        return grupos.Count;
    }

    /// <summary>O texto da mensagem: o número e o título, a situação (e quem mudou), o corpo do programa e o que fazer agora.</summary>
    public static string TextoDa(OrdemNoTelegram o)
    {
        var dica = o.Situacao switch
        {
            OrdemNoTelegram.Emitida => $"Executante: toque em Aceitar (ou Recusar, mandando /recusar {o.Numero} e o motivo).",
            OrdemNoTelegram.Aceita => "Toque em Comecei ao iniciar e em Terminei ao acabar. Mande fotos do serviço aqui no grupo.",
            OrdemNoTelegram.EmExecucao => "Toque em Terminei ao acabar. Mande fotos do serviço aqui no grupo.",
            OrdemNoTelegram.Concluida => "Esperando a conferência da engenharia.",
            OrdemNoTelegram.Recusada => "A engenharia vai revisar a OS.",
            _ => null,
        };
        var autor = o.Autor is null ? "" : $" ({o.Autor})";
        return $"📋 {o.Codigo} · {o.Titulo}\nSituação: {Situacao(o)}{autor}\n\n{o.Texto}" + (dica is null ? "" : $"\n\n{dica}");
    }

    /// <summary>"Em execução", "Conferida e aprovada ✅".</summary>
    public static string Situacao(OrdemNoTelegram o) => o.Situacao switch
    {
        OrdemNoTelegram.Emitida => "Emitida",
        OrdemNoTelegram.Aceita => "Aceita",
        OrdemNoTelegram.Recusada => "Recusada",
        OrdemNoTelegram.EmExecucao => "Em execução",
        OrdemNoTelegram.Concluida => "Concluída",
        "Conferida" => "Conferida e aprovada ✅",
        "Paga" => "Paga ✅",
        "Cancelada" => "Cancelada",
        _ => "Em preparo",
    };

    public static IReadOnlyList<BotaoDoTelegram> BotoesDa(OrdemNoTelegram o)
    {
        BotaoDoTelegram Botao(string texto, string acao) => new(texto, $"{PrefixoDosBotoes}{o.Id:N}:{acao}");
        return o.Situacao switch
        {
            OrdemNoTelegram.Emitida => [Botao("✅ Aceitar", "aceitar"), Botao("❌ Recusar", "recusar")],
            OrdemNoTelegram.Aceita => [Botao("▶️ Comecei", "iniciar"), Botao("🏁 Terminei", "concluir")],
            OrdemNoTelegram.EmExecucao => [Botao("🏁 Terminei", "concluir")],
            _ => [],
        };
    }

    private static List<MensagemDaOrdem> Mensagens(OrdemNoTelegram o)
    {
        try
        {
            return JsonSerializer.Deserialize<List<MensagemDaOrdem>>(o.Mensagens, JsonDasMensagens) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Um toque num botão de OS.</summary>
    private async Task AtenderBotaoAsync(JsonElement consulta, CancellationToken c)
    {
        var id = Texto(consulta, "id");
        var dados = Texto(consulta, "data") ?? "";
        if (id is null || !consulta.TryGetProperty("from", out var de) || !de.TryGetProperty("id", out var idDe) || !idDe.TryGetInt64(out var pessoaId)
            || !consulta.TryGetProperty("message", out var mensagem) || !mensagem.TryGetProperty("chat", out var chat)
            || !chat.TryGetProperty("id", out var idDoChat) || !idDoChat.TryGetInt64(out var chatId))
            return;
        var partes = dados.Split(':');
        if (partes.Length != 3 || partes[0] + ":" != PrefixoDosBotoes || !Guid.TryParseExact(partes[1], "N", out var ordemId))
        {
            await telegram.ResponderAoBotaoAsync(id, "Botão desconhecido.", c);
            return;
        }
        var ordem = await banco.OrdensNoTelegram.FindAsync([ordemId], c);
        var grupo = await banco.GruposDoTelegram.FindAsync([chatId], c);
        if (ordem is null || grupo is null || grupo.ObraId != ordem.ObraId || grupo.UsuarioId != ordem.UsuarioId)
        {
            await telegram.ResponderAoBotaoAsync(id, "Esta OS não está mais ligada a este grupo.", c);
            return;
        }
        var nome = NomeDe(de);
        var agora = relogio.GetUtcNow().UtcDateTime;
        var membro = await MembroDaOrdemAsync(chatId, pessoaId, nome, grupo.UsuarioId, agora, c);
        if (await banco.Usuarios.FindAsync([grupo.UsuarioId], c) is not { Plano: Planos.Profissional })
        {
            await telegram.ResponderAoBotaoAsync(id, "O ProjectPlanner no Telegram é do plano Profissional, e a conta do dono deste grupo não está nele.", c);
            return;
        }
        if (!membro.Envia)
        {
            await telegram.ResponderAoBotaoAsync(id, "Quem só acompanha a obra não mexe nas OS. Peça a quem é da engenharia: /papel campo.", c);
            return;
        }
        if (partes[2] == "recusar")
        {
            await telegram.ResponderAoBotaoAsync(id, $"Para recusar, mande /recusar {ordem.Numero} e o motivo.", c);
            if (ordem.Situacao == OrdemNoTelegram.Emitida)
                await telegram.EnviarAsync(chatId, $"Para recusar a {ordem.Codigo}, mande /recusar {ordem.Numero} e o motivo (por exemplo: /recusar {ordem.Numero} o preço não cobre o material).", c);
            return;
        }
        await MudarAsync(ordem, partes[2], nome, null, chatId, agora, c, botao: id);
    }

    // Aplica a ação na OS (se a situação deixa), avisa a caixa, atualiza as mensagens e responde.
    private async Task MudarAsync(OrdemNoTelegram ordem, string acao, string autor, string? motivo, long chatId, DateTime agora, CancellationToken c,
        string? botao = null)
    {
        var (permitidas, nova, feito) = acao switch
        {
            "aceitar" => (new[] { OrdemNoTelegram.Emitida }, OrdemNoTelegram.Aceita, "aceitou"),
            "recusar" => ([OrdemNoTelegram.Emitida], OrdemNoTelegram.Recusada, "recusou"),
            "iniciar" => ([OrdemNoTelegram.Emitida, OrdemNoTelegram.Aceita], OrdemNoTelegram.EmExecucao, "começou"),
            "concluir" => ([OrdemNoTelegram.Aceita, OrdemNoTelegram.EmExecucao], OrdemNoTelegram.Concluida, "terminou"),
            _ => (Array.Empty<string>(), "", ""),
        };
        if (!permitidas.Contains(ordem.Situacao))
        {
            var aviso = nova.Length == 0 ? "Botão desconhecido." : $"A {ordem.Codigo} já está \"{Situacao(ordem).ToLowerInvariant()}\".";
            if (botao is not null)
                await telegram.ResponderAoBotaoAsync(botao, aviso, c);
            else
                await telegram.EnviarAsync(chatId, aviso, c);
            return;
        }
        (ordem.Situacao, ordem.Autor, ordem.AtualizadoEm) = (nova, autor, agora);
        banco.CaixaDeEntrada.Add(new ItemDaCaixa
        {
            Id = Guid.NewGuid(), UsuarioId = ordem.UsuarioId, ObraId = ordem.ObraId, Tipo = ItemDaCaixa.DeOrdem, CriadoEm = agora,
            Texto = Cortar($"{autor} {feito} a {ordem.Codigo}" + (motivo is null ? "" : $": {motivo}"))!,
            Conteudo = JsonSerializer.Serialize(new EventoDaOrdemNoTelegram(ordem.Id, acao, autor, agora, motivo), Json),
        });
        await PublicarAsync(ordem, c);
        await banco.SaveChangesAsync(c);
        if (botao is not null)
            await telegram.ResponderAoBotaoAsync(botao, $"{ordem.Codigo}: {feito}. Avisado à engenharia.", c);
        var icone = acao switch { "aceitar" => "✅", "recusar" => "❌", "iniciar" => "▶️", _ => "🏁" };
        await telegram.EnviarAsync(chatId, $"{icone} {autor} {feito} a {ordem.Codigo} ({ordem.Titulo})" + (motivo is null ? "." : $": {motivo}.") +
                                           " A engenharia vê no programa.", c);
    }

    /// <summary>/os: as OS abertas da obra do grupo; /os 14: a mensagem da OS de novo, com os botões.</summary>
    private async Task OrdensDoGrupoAsync(long chatId, GrupoDoTelegram grupo, string argumento, CancellationToken c)
    {
        var abertas = await banco.OrdensNoTelegram.Where(o => o.ObraId == grupo.ObraId && o.UsuarioId == grupo.UsuarioId).OrderBy(o => o.Numero).ToListAsync(c);
        if (int.TryParse(new string(argumento.Where(char.IsAsciiDigit).ToArray()), out var numero))
        {
            if (abertas.FirstOrDefault(o => o.Numero == numero) is not { } ordem)
            {
                await telegram.EnviarAsync(chatId, $"A OS-{numero:000} não foi mandada a este grupo. /os mostra as que estão aqui.", c);
                return;
            }
            // Manda de novo (a mensagem antiga pode ter sumido na conversa) e passa a atualizar esta.
            var mensagens = Mensagens(ordem).Where(m => m.Chat != chatId).ToList();
            if (await telegram.EnviarComBotoesAsync(chatId, TextoDa(ordem), BotoesDa(ordem), c) is { } id)
                mensagens.Add(new MensagemDaOrdem(chatId, id));
            ordem.Mensagens = JsonSerializer.Serialize(mensagens, JsonDasMensagens);
            await banco.SaveChangesAsync(c);
            return;
        }
        var lista = abertas.Where(o => o.Situacao is OrdemNoTelegram.Emitida or OrdemNoTelegram.Aceita or OrdemNoTelegram.EmExecucao or OrdemNoTelegram.Concluida).ToList();
        await telegram.EnviarAsync(chatId, lista.Count == 0
            ? "Nenhuma OS aberta nesta obra."
            : "OS abertas:\n" + string.Join("\n", lista.Select(o => $"• {o.Codigo} · {o.Titulo}: {Situacao(o).ToLowerInvariant()}")) +
              "\n\nPara ver uma com os botões, mande /os e o número (por exemplo, /os " + lista[0].Numero + ").", c);
    }

    /// <summary>/recusar 14 o motivo.</summary>
    private async Task RecusarAsync(long chatId, GrupoDoTelegram grupo, MembroDoGrupo membro, string argumento, DateTime agora, CancellationToken c)
    {
        if (!membro.Envia)
        {
            await telegram.EnviarAsync(chatId, SoQuemEnvia, c);
            return;
        }
        var espaco = argumento.IndexOf(' ');
        var numero = espaco < 0 ? argumento : argumento[..espaco];
        var motivo = espaco < 0 ? "" : argumento[(espaco + 1)..].Trim();
        if (!int.TryParse(new string(numero.Where(char.IsAsciiDigit).ToArray()), out var n) || motivo.Length == 0)
        {
            await telegram.EnviarAsync(chatId, "Para recusar uma OS, mande /recusar, o número e o motivo (por exemplo: /recusar 14 o preço não cobre o material).", c);
            return;
        }
        var ordem = await banco.OrdensNoTelegram.FirstOrDefaultAsync(o => o.ObraId == grupo.ObraId && o.UsuarioId == grupo.UsuarioId && o.Numero == n, c);
        if (ordem is null)
        {
            await telegram.EnviarAsync(chatId, $"A OS-{n:000} não foi mandada a este grupo. /os mostra as que estão aqui.", c);
            return;
        }
        await MudarAsync(ordem, "recusar", membro.Nome, Cortar(motivo, 300), chatId, agora, c);
    }

    // O membro de quem tocou no botão (o dono é sempre da engenharia).
    private async Task<MembroDoGrupo> MembroDaOrdemAsync(long chatId, long pessoaId, string nome, Guid donoId, DateTime agora, CancellationToken c)
    {
        var ehDono = await banco.VinculosDoTelegram.AnyAsync(v => v.ChatId == pessoaId && v.UsuarioId == donoId, c);
        return await MembroAsync(chatId, pessoaId, nome, ehDono ? MembroDoGrupo.Engenharia : MembroDoGrupo.Campo, agora, c);
    }
}
