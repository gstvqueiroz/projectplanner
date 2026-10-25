using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ProjectPlanner.Servidor.Dados;

namespace ProjectPlanner.Servidor.Servicos;

/// <summary>
/// Bloco 53: um grupo do Telegram por obra, com a equipe. O dono da conta (com o chat privado do bot já ligado) põe o bot no
/// grupo e manda /obra; a equipe não precisa de conta, e os créditos são do dono. A conversa do grupo é ignorada (não gasta
/// nada): o bot só age numa foto, numa mensagem com #atualizacao, quando é marcado (@bot) ou quando respondem a ele. Cada
/// pessoa tem um papel: acompanhar (só pergunta), campo (manda fotos e atualizações) ou engenharia (também muda os papéis).
/// Tudo o que muda a obra vai para a caixa de entrada, e o dono aplica no programa.
/// </summary>
public sealed partial class AtendenteDoTelegram
{
    public const string AjudaDoGrupo =
        "Como usar o ProjectPlanner neste grupo:\n" +
        "• mande fotos da obra (com legenda, se quiser): vão para a caixa da obra;\n" +
        "• escreva #atualizacao e o que mudou (\"#atualizacao alvenaria do 2º piso em 70%\");\n" +
        "• para perguntar, marque o bot (@{0}) ou responda a uma mensagem dele;\n" +
        "• ordens de serviço: toque nos botões da OS (Aceitar, Comecei, Terminei); /os lista as abertas, /recusar 14 e o motivo recusa; #os14 na legenda da foto a liga à OS.\n\n" +
        "O resto da conversa do grupo o bot não lê. Tudo vai para a caixa da obra, e o dono confere e aplica no programa.\n" +
        "/equipe mostra os papéis · /papel (respondendo à mensagem de alguém) muda o papel · /obra troca a obra · /sair desliga o grupo.";

    public const string GrupoSemObra =
        "Este grupo ainda não está ligado a uma obra. O dono da conta ProjectPlanner (com o chat privado do bot já ligado) manda /obra aqui.";

    public const string SoQuemEnvia =
        "Quem só acompanha a obra não manda fotos nem atualizações. Peça a quem é da engenharia para mudar o seu papel (/papel campo).";

    [GeneratedRegex(@"#atualiza[cç][aã]o\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarcaDeAtualizacao();

    private string NomeDoBot => Config.NomeDoBot?.Trim().TrimStart('@') ?? "";

    private async Task AtenderNoGrupoAsync(JsonElement mensagem, JsonElement chat, long chatId, string texto, DateTime agora, CancellationToken c)
    {
        // O grupo virou supergrupo: o Telegram troca o id do chat, e a ligação vai junto.
        if (mensagem.TryGetProperty("migrate_to_chat_id", out var novoId) && novoId.TryGetInt64(out var paraOId))
        {
            await MudarDeChatAsync(chatId, paraOId, c);
            return;
        }
        if (!mensagem.TryGetProperty("from", out var de) || !de.TryGetProperty("id", out var idDe) || !idDe.TryGetInt64(out var pessoaId)
            || de.TryGetProperty("is_bot", out var ehBot) && ehBot.ValueKind == JsonValueKind.True)
            return;
        var nome = NomeDe(de);
        if (EntrouOBot(mensagem))
        {
            await telegram.EnviarAsync(chatId, "Olá! Sou o assistente do ProjectPlanner. " + GrupoSemObra, c);
            return;
        }

        var (comando, argumento) = Comando(texto);
        var mencao = NomeDoBot.Length > 0 && texto.Contains("@" + NomeDoBot, StringComparison.OrdinalIgnoreCase);
        var respostaAoBot = mensagem.TryGetProperty("reply_to_message", out var respondida) && respondida.TryGetProperty("from", out var autorDaRespondida)
                            && string.Equals(Texto(autorDaRespondida, "username"), NomeDoBot, StringComparison.OrdinalIgnoreCase) && NomeDoBot.Length > 0;
        var marcada = MarcaDeAtualizacao().IsMatch(texto);
        var temFoto = mensagem.TryGetProperty("photo", out var fotos) && fotos.ValueKind == JsonValueKind.Array && fotos.GetArrayLength() > 0;
        if (comando is null && !mencao && !respostaAoBot && !marcada && !temFoto)
            return; // a conversa da equipe: não lê, não gasta
        if (!conversas.Permitir(chatId, agora, Config.MensagensPorMinuto))
        {
            await telegram.EnviarAsync(chatId, "Muitas mensagens seguidas para o bot. Esperem um minuto e mandem de novo.", c);
            return;
        }

        var grupo = await banco.GruposDoTelegram.FindAsync([chatId], c);
        var proprio = await banco.VinculosDoTelegram.FirstOrDefaultAsync(v => v.ChatId == pessoaId, c); // o chat privado de quem mandou
        if (comando == "/obra")
        {
            await ObraDoGrupoAsync(chatId, chat, grupo, proprio, pessoaId, nome, argumento, agora, c);
            return;
        }
        if (grupo is null)
        {
            if (comando is not null || mencao || respostaAoBot)
                await telegram.EnviarAsync(chatId, GrupoSemObra, c);
            return;
        }
        var dono = await banco.Usuarios.FindAsync([grupo.UsuarioId], c);
        var obra = await banco.Obras.FirstOrDefaultAsync(o => o.Id == grupo.ObraId && o.UsuarioId == grupo.UsuarioId, c);
        if (dono is null || obra is null)
        {
            await telegram.EnviarAsync(chatId, "A obra deste grupo não está mais no Telegram. O dono pode ligar outra com /obra.", c);
            return;
        }
        var ehDono = proprio?.UsuarioId == dono.Id;
        var membro = await MembroAsync(chatId, pessoaId, nome, ehDono ? MembroDoGrupo.Engenharia : MembroDoGrupo.Campo, agora, c);
        switch (comando)
        {
            case "/papel":
                await PapelAsync(chatId, mensagem, membro, ehDono, dono.Id, argumento, agora, c);
                return;
            case "/equipe":
                await EquipeAsync(chatId, dono.Id, c);
                return;
            case "/sair":
                await SairDoGrupoAsync(chatId, grupo, ehDono, c);
                return;
            case "/os": // Bloco 59
                await OrdensDoGrupoAsync(chatId, grupo, argumento, c);
                return;
            case "/recusar":
                await RecusarAsync(chatId, grupo, membro, argumento, agora, c);
                return;
            case "/ajuda" or "/help" or "/start":
                await telegram.EnviarAsync(chatId, string.Format(AjudaDoGrupo, NomeDoBot) + $"\n\nObra: {obra.Nome}.", c);
                return;
            case not null when !mencao && !respostaAoBot && !marcada && !temFoto:
                return; // um comando de outro bot
        }
        if (dono.Plano != Planos.Profissional)
        {
            await telegram.EnviarAsync(chatId, "O assistente no Telegram é do plano Profissional, e a conta do dono deste grupo não está nele.", c);
            return;
        }
        if (temFoto)
        {
            if (!membro.Envia)
                await telegram.EnviarAsync(chatId, SoQuemEnvia, c);
            else
                await FotoAsync(chatId, dono, obra, fotos, Limpar(texto), agora, c, autor: nome);
            return;
        }
        var limpo = Limpar(texto);
        if (limpo.Length == 0)
        {
            await telegram.EnviarAsync(chatId, string.Format(AjudaDoGrupo, NomeDoBot), c);
            return;
        }
        if (marcada && !membro.Envia)
        {
            await telegram.EnviarAsync(chatId, SoQuemEnvia, c);
            return;
        }
        await PerguntaAsync(chatId, dono, obra, limpo, agora, c, autor: nome, aceitaAtualizacoes: membro.Envia);
    }

    /// <summary>/obra no grupo: só o dono (com o chat privado ligado). Sem número, lista as obras; com número, liga o grupo a ela.</summary>
    private async Task ObraDoGrupoAsync(long chatId, JsonElement chat, GrupoDoTelegram? grupo, VinculoDoTelegram? proprio, long pessoaId, string nome,
        string argumento, DateTime agora, CancellationToken c)
    {
        if (proprio is null)
        {
            await telegram.EnviarAsync(chatId, "Só o dono da conta ProjectPlanner liga o grupo a uma obra. Primeiro, ligue o seu chat privado " +
                                               "com o bot (no programa, Conta > Conectar o Telegram…) e depois mande /obra aqui.", c);
            return;
        }
        if (grupo is not null && grupo.UsuarioId != proprio.UsuarioId)
        {
            await telegram.EnviarAsync(chatId, "Este grupo já está ligado à obra de outra conta. Só o dono dela pode trocar.", c);
            return;
        }
        var obras = await banco.Obras.Where(o => o.UsuarioId == proprio.UsuarioId).OrderBy(o => o.Nome).ToListAsync(c);
        if (obras.Count == 0)
        {
            await telegram.EnviarAsync(chatId, "Nenhuma obra sua está no Telegram ainda. No programa, abra a obra e marque Conta > Usar esta obra no Telegram.", c);
            return;
        }
        if (!int.TryParse(argumento, out var numero) || numero < 1 || numero > obras.Count)
        {
            await telegram.EnviarAsync(chatId, "Suas obras no Telegram:\n" +
                string.Join("\n", obras.Select((o, i) => $"{i + 1}. {o.Nome}{(o.Id == grupo?.ObraId ? " ← este grupo" : "")}")) +
                "\n\nPara ligar este grupo, mande /obra e o número (por exemplo, /obra 1).", c);
            return;
        }
        var obra = obras[numero - 1];
        var titulo = Texto(chat, "title");
        if (grupo is null)
            banco.GruposDoTelegram.Add(new GrupoDoTelegram
            {
                ChatId = chatId, UsuarioId = proprio.UsuarioId, ObraId = obra.Id, Titulo = Cortar(titulo, 200), CriadoEm = agora,
            });
        else
        {
            grupo.ObraId = obra.Id;
            grupo.Titulo = Cortar(titulo, 200);
        }
        await banco.SaveChangesAsync(c);
        var dono = await MembroAsync(chatId, pessoaId, nome, MembroDoGrupo.Engenharia, agora, c);
        dono.Papel = MembroDoGrupo.Engenharia;
        await banco.SaveChangesAsync(c);
        conversas.Esquecer(chatId);
        await telegram.EnviarAsync(chatId, $"Pronto: este grupo é da obra {obra.Nome}. Quem falar com o bot entra na equipe com o papel \"campo\".\n\n" +
                                           string.Format(AjudaDoGrupo, NomeDoBot), c);
    }

    /// <summary>/papel acompanhar|campo|engenharia, respondendo à mensagem de alguém: muda o papel dessa pessoa.</summary>
    private async Task PapelAsync(long chatId, JsonElement mensagem, MembroDoGrupo quem, bool ehDono, Guid donoId, string argumento, DateTime agora,
        CancellationToken c)
    {
        if (!ehDono && !quem.Gerencia)
        {
            await telegram.EnviarAsync(chatId, "Só o dono e quem é da engenharia mudam os papéis.", c);
            return;
        }
        var papel = argumento.Trim().ToLowerInvariant();
        if (!MembroDoGrupo.Papeis.Contains(papel) || !mensagem.TryGetProperty("reply_to_message", out var respondida)
            || !respondida.TryGetProperty("from", out var alvo) || !alvo.TryGetProperty("id", out var idDoAlvo) || !idDoAlvo.TryGetInt64(out var alvoId)
            || alvo.TryGetProperty("is_bot", out var ehBot) && ehBot.ValueKind == JsonValueKind.True)
        {
            await telegram.EnviarAsync(chatId, "Para mudar o papel de alguém, responda a uma mensagem dessa pessoa com /papel e o papel:\n" +
                                               "• acompanhar: só pergunta;\n• campo: manda fotos e atualizações;\n• engenharia: também muda os papéis.", c);
            return;
        }
        if (await banco.VinculosDoTelegram.AnyAsync(v => v.ChatId == alvoId && v.UsuarioId == donoId, c))
        {
            await telegram.EnviarAsync(chatId, "O dono da conta é sempre da engenharia.", c);
            return;
        }
        var membro = await MembroAsync(chatId, alvoId, NomeDe(alvo), papel, agora, c);
        membro.Papel = papel;
        await banco.SaveChangesAsync(c);
        await telegram.EnviarAsync(chatId, $"{membro.Nome} agora é \"{papel}\".", c);
    }

    private async Task EquipeAsync(long chatId, Guid donoId, CancellationToken c)
    {
        var membros = await banco.MembrosDosGrupos.Where(m => m.ChatId == chatId).OrderBy(m => m.Nome).ToListAsync(c);
        var donos = await banco.VinculosDoTelegram.Where(v => v.UsuarioId == donoId).Select(v => v.ChatId).ToListAsync(c);
        await telegram.EnviarAsync(chatId, "Equipe deste grupo:\n" +
            string.Join("\n", membros.Select(m => $"• {m.Nome}: {m.Papel}{(donos.Contains(m.PessoaId) ? " (dono)" : "")}")) +
            "\n\nQuem ainda não falou com o bot entra como \"campo\" na primeira mensagem para ele.", c);
    }

    private async Task SairDoGrupoAsync(long chatId, GrupoDoTelegram grupo, bool ehDono, CancellationToken c)
    {
        if (!ehDono)
        {
            await telegram.EnviarAsync(chatId, "Só o dono da conta desliga o grupo.", c);
            return;
        }
        banco.MembrosDosGrupos.RemoveRange(banco.MembrosDosGrupos.Where(m => m.ChatId == chatId));
        banco.GruposDoTelegram.Remove(grupo);
        await banco.SaveChangesAsync(c);
        conversas.Esquecer(chatId);
        await telegram.EnviarAsync(chatId, "Pronto: este grupo não está mais ligado a uma obra. O que já foi para a caixa continua lá.", c);
    }

    // O membro (criado na primeira mensagem para o bot, com o papel padrão); o nome acompanha o do Telegram.
    private async Task<MembroDoGrupo> MembroAsync(long chatId, long pessoaId, string nome, string papelInicial, DateTime agora, CancellationToken c)
    {
        var membro = await banco.MembrosDosGrupos.FindAsync([chatId, pessoaId], c);
        if (membro is null)
        {
            membro = new MembroDoGrupo { ChatId = chatId, PessoaId = pessoaId, Nome = nome, Papel = papelInicial, DesdeEm = agora };
            banco.MembrosDosGrupos.Add(membro);
            await banco.SaveChangesAsync(c);
        }
        else if (membro.Nome != nome)
        {
            membro.Nome = nome;
            await banco.SaveChangesAsync(c);
        }
        return membro;
    }

    private async Task MudarDeChatAsync(long deId, long paraId, CancellationToken c)
    {
        if (await banco.GruposDoTelegram.FindAsync([deId], c) is not { } antigo || await banco.GruposDoTelegram.FindAsync([paraId], c) is not null)
            return;
        var membros = await banco.MembrosDosGrupos.Where(m => m.ChatId == deId).ToListAsync(c);
        banco.GruposDoTelegram.Remove(antigo);
        banco.MembrosDosGrupos.RemoveRange(membros);
        await banco.SaveChangesAsync(c);
        banco.GruposDoTelegram.Add(new GrupoDoTelegram { ChatId = paraId, UsuarioId = antigo.UsuarioId, ObraId = antigo.ObraId, Titulo = antigo.Titulo, CriadoEm = antigo.CriadoEm });
        banco.MembrosDosGrupos.AddRange(membros.Select(m => new MembroDoGrupo { ChatId = paraId, PessoaId = m.PessoaId, Nome = m.Nome, Papel = m.Papel, DesdeEm = m.DesdeEm }));
        await banco.SaveChangesAsync(c);
        conversas.Esquecer(deId);
    }

    private bool EntrouOBot(JsonElement mensagem) =>
        NomeDoBot.Length > 0 && mensagem.TryGetProperty("new_chat_members", out var novos) && novos.ValueKind == JsonValueKind.Array
        && novos.EnumerateArray().Any(n => string.Equals(Texto(n, "username"), NomeDoBot, StringComparison.OrdinalIgnoreCase));

    /// <summary>"/obra@MeuBot 2" → ("/obra", "2"); um comando para outro bot, ou um texto sem comando → (null, "").</summary>
    private (string? Comando, string Argumento) Comando(string texto)
    {
        if (!texto.StartsWith('/'))
            return (null, "");
        var espaco = texto.IndexOfAny([' ', '\n']);
        var cabeca = espaco < 0 ? texto : texto[..espaco];
        var argumento = espaco < 0 ? "" : texto[(espaco + 1)..].Trim();
        var arroba = cabeca.IndexOf('@');
        if (arroba >= 0)
        {
            if (!string.Equals(cabeca[(arroba + 1)..], NomeDoBot, StringComparison.OrdinalIgnoreCase))
                return (null, "");
            cabeca = cabeca[..arroba];
        }
        return (cabeca.ToLowerInvariant(), argumento);
    }

    // Tira a menção ao bot e a marca #atualizacao do texto que vai à IA.
    private string Limpar(string texto)
    {
        var limpo = MarcaDeAtualizacao().Replace(texto, "");
        if (NomeDoBot.Length > 0)
            limpo = Regex.Replace(limpo, "@" + Regex.Escape(NomeDoBot), "", RegexOptions.IgnoreCase);
        return Regex.Replace(limpo, @"\s{2,}", " ").Trim();
    }

    private static string NomeDe(JsonElement pessoa)
    {
        var nome = string.Join(" ", new[] { Texto(pessoa, "first_name"), Texto(pessoa, "last_name") }.Where(p => !string.IsNullOrWhiteSpace(p))).Trim();
        return Cortar(nome.Length > 0 ? nome : Texto(pessoa, "username") ?? "Alguém", 120)!;
    }

    private static string? Cortar(string? texto, int tamanho) => texto is null || texto.Length <= tamanho ? texto : texto[..tamanho];
}
