using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectPlanner.IA;
using ProjectPlanner.IA.Gemini;
using ProjectPlanner.Servidor.Dados;

namespace ProjectPlanner.Servidor.Servicos;

/// <summary>Bloco 49: as últimas mensagens de cada chat (para o assistente seguir a conversa) e o limite por minuto.</summary>
public sealed class ConversasDoTelegram
{
    public const int MensagensGuardadas = 8;

    private readonly ConcurrentDictionary<long, List<MensagemChat>> _historicos = new();
    private readonly ConcurrentDictionary<long, Queue<DateTime>> _envios = new();

    public IReadOnlyList<MensagemChat> Historico(long chat)
    {
        var lista = _historicos.GetOrAdd(chat, _ => []);
        lock (lista)
            return [.. lista];
    }

    public void Guardar(long chat, MensagemChat pergunta, MensagemChat resposta)
    {
        var lista = _historicos.GetOrAdd(chat, _ => []);
        lock (lista)
        {
            lista.Add(pergunta);
            lista.Add(resposta);
            if (lista.Count > MensagensGuardadas)
                lista.RemoveRange(0, lista.Count - MensagensGuardadas);
        }
    }

    public void Esquecer(long chat) => _historicos.TryRemove(chat, out _);

    /// <summary>Conta a mensagem; falso se passou do limite do último minuto.</summary>
    public bool Permitir(long chat, DateTime agora, int porMinuto)
    {
        var fila = _envios.GetOrAdd(chat, _ => new Queue<DateTime>());
        lock (fila)
        {
            while (fila.Count > 0 && fila.Peek() < agora.AddMinutes(-1))
                fila.Dequeue();
            if (fila.Count >= porMinuto)
                return false;
            fila.Enqueue(agora);
            return true;
        }
    }
}

/// <summary>
/// Bloco 49: atende uma mensagem do Telegram. Liga o chat à conta (/start com o código do programa), troca a obra (/obra),
/// guarda fotos na caixa de entrada e, no texto, chama o mesmo assistente do programa com o retrato da obra (no canal
/// Telegram): a resposta volta ao chat, e as atualizações propostas vão para a caixa, para o usuário conferir no programa.
/// </summary>
public sealed class AtendenteDoTelegram(BancoDoServidor banco, IOptions<OpcoesDoServidor> opcoes, CatalogoDeModelos catalogo, IClienteDoTelegram telegram,
    IHttpClientFactory clientes, ConversasDoTelegram conversas, IEnviadorDeEmail email, TimeProvider relogio, ILogger<AtendenteDoTelegram> log)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public const string SemVinculo =
        "Olá! Este é o assistente do ProjectPlanner. Para usar, ligue a sua conta: no programa, abra Conta > Conectar o Telegram… " +
        "e toque no link (ou mande aqui /start seguido do código).";

    public const string Ajuda =
        "Fale da obra como falaria com o engenheiro:\n" +
        "• \"quanto já medi?\", \"o que está atrasado?\"\n" +
        "• \"a alvenaria está em 60%\", \"gastei R$ 1.200 de cimento na fundação\"\n" +
        "• mande fotos (com uma legenda, se quiser)\n\n" +
        "As atualizações e as fotos ficam na caixa de entrada da obra: confira e aplique no programa (Conta > Buscar do Telegram).\n" +
        "/obra mostra e troca a obra · /sair desliga este chat da conta.";

    public const string NaoRespondeu = "Não consegui responder agora. Tente de novo em instantes.";

    public const string SoProfissional =
        "O assistente no Telegram é do plano Profissional. No programa, veja em Conta > Minha conta.";

    private OpcoesDoTelegram Config => opcoes.Value.Telegram;

    public async Task AtenderAsync(JsonElement aviso, CancellationToken c)
    {
        if (!aviso.TryGetProperty("message", out var mensagem) || !mensagem.TryGetProperty("chat", out var chat)
            || !chat.TryGetProperty("id", out var idDoChat) || !idDoChat.TryGetInt64(out var chatId))
            return;
        var texto = (Texto(mensagem, "text") ?? Texto(mensagem, "caption") ?? "").Trim();
        var nome = mensagem.TryGetProperty("from", out var de) ? Texto(de, "first_name") : null;
        var agora = relogio.GetUtcNow().UtcDateTime;
        if (!conversas.Permitir(chatId, agora, Config.MensagensPorMinuto))
        {
            await telegram.EnviarAsync(chatId, "Muitas mensagens seguidas. Espere um minuto e mande de novo.", c);
            return;
        }

        if (texto.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            var codigo = texto["/start".Length..].Trim();
            if (codigo.Length > 0)
            {
                await LigarAsync(chatId, codigo, nome, agora, c);
                return;
            }
        }

        var vinculo = await banco.VinculosDoTelegram.FirstOrDefaultAsync(v => v.ChatId == chatId, c);
        var usuario = vinculo is null ? null : await banco.Usuarios.FindAsync([vinculo.UsuarioId], c);
        if (vinculo is null || usuario is null)
        {
            await telegram.EnviarAsync(chatId, SemVinculo, c);
            return;
        }
        if (usuario.Plano != Planos.Profissional)
        {
            await telegram.EnviarAsync(chatId, SoProfissional, c);
            return;
        }

        if (texto.Equals("/sair", StringComparison.OrdinalIgnoreCase))
        {
            banco.VinculosDoTelegram.Remove(vinculo);
            await banco.SaveChangesAsync(c);
            conversas.Esquecer(chatId);
            await telegram.EnviarAsync(chatId, "Pronto: este chat não está mais ligado à sua conta ProjectPlanner.", c);
            return;
        }
        if (texto.StartsWith("/start", StringComparison.OrdinalIgnoreCase) || texto.StartsWith("/ajuda", StringComparison.OrdinalIgnoreCase)
            || texto.StartsWith("/help", StringComparison.OrdinalIgnoreCase))
        {
            await telegram.EnviarAsync(chatId, Ajuda, c);
            return;
        }

        var obras = await banco.Obras.Where(o => o.UsuarioId == usuario.Id).OrderBy(o => o.Nome).ToListAsync(c);
        if (texto.StartsWith("/obra", StringComparison.OrdinalIgnoreCase))
        {
            await ObraAsync(chatId, vinculo, obras, texto["/obra".Length..].Trim(), c);
            return;
        }
        if (obras.Count == 0)
        {
            await telegram.EnviarAsync(chatId, "Nenhuma obra está no Telegram ainda. No programa, abra a obra e marque Conta > Usar esta obra no Telegram.", c);
            return;
        }
        var obra = obras.FirstOrDefault(o => o.Id == vinculo.ObraAtiva) ?? obras[0];

        if (mensagem.TryGetProperty("photo", out var fotos) && fotos.ValueKind == JsonValueKind.Array && fotos.GetArrayLength() > 0)
        {
            await FotoAsync(chatId, usuario, obra, fotos, texto, agora, c);
            return;
        }
        if (texto.Length == 0)
        {
            await telegram.EnviarAsync(chatId, "Mande um texto ou uma foto. " + Ajuda, c);
            return;
        }
        await PerguntaAsync(chatId, usuario, obra, texto, agora, c);
    }

    private async Task LigarAsync(long chatId, string codigo, string? nome, DateTime agora, CancellationToken c)
    {
        var hash = Segredos.Hash(codigo.ToUpperInvariant());
        var registro = await banco.CodigosDoTelegram.FirstOrDefaultAsync(x => x.Hash == hash, c);
        if (registro is null || registro.ExpiraEm < agora || await banco.Usuarios.FindAsync([registro.UsuarioId], c) is not { } usuario)
        {
            await telegram.EnviarAsync(chatId, "Código inválido ou vencido. No programa, abra de novo Conta > Conectar o Telegram… para gerar outro.", c);
            return;
        }
        banco.CodigosDoTelegram.Remove(registro);
        // Um chat é de uma conta só, e uma conta tem um chat só.
        foreach (var antigo in banco.VinculosDoTelegram.Where(v => v.ChatId == chatId || v.UsuarioId == usuario.Id))
            banco.VinculosDoTelegram.Remove(antigo);
        await banco.SaveChangesAsync(c);
        banco.VinculosDoTelegram.Add(new VinculoDoTelegram { UsuarioId = usuario.Id, ChatId = chatId, Nome = nome?.Length > 120 ? nome[..120] : nome, CriadoEm = agora });
        await banco.SaveChangesAsync(c);
        conversas.Esquecer(chatId);
        var obras = await banco.Obras.Where(o => o.UsuarioId == usuario.Id).OrderBy(o => o.Nome).Select(o => o.Nome).ToListAsync(c);
        await telegram.EnviarAsync(chatId, $"Pronto, {usuario.Nome}! Este chat está ligado à sua conta ProjectPlanner." +
            (usuario.Plano == Planos.Profissional ? "" : "\n\n" + SoProfissional) +
            (obras.Count == 0 ? "\n\nAgora marque uma obra no programa: Conta > Usar esta obra no Telegram."
                : $"\n\nObra: {obras[0]}" + (obras.Count > 1 ? " (/obra para trocar)" : "")) + "\n\n" + Ajuda, c);
    }

    private async Task ObraAsync(long chatId, VinculoDoTelegram vinculo, List<ObraNoServidor> obras, string escolha, CancellationToken c)
    {
        if (obras.Count == 0)
        {
            await telegram.EnviarAsync(chatId, "Nenhuma obra está no Telegram ainda. No programa, abra a obra e marque Conta > Usar esta obra no Telegram.", c);
            return;
        }
        if (int.TryParse(escolha, out var numero) && numero >= 1 && numero <= obras.Count)
        {
            vinculo.ObraAtiva = obras[numero - 1].Id;
            await banco.SaveChangesAsync(c);
            conversas.Esquecer(chatId);
            await telegram.EnviarAsync(chatId, $"Obra: {obras[numero - 1].Nome}.", c);
            return;
        }
        var atual = obras.FirstOrDefault(o => o.Id == vinculo.ObraAtiva) ?? obras[0];
        await telegram.EnviarAsync(chatId, "Suas obras no Telegram:\n" +
            string.Join("\n", obras.Select((o, i) => $"{i + 1}. {o.Nome}{(o.Id == atual.Id ? " ← agora" : "")}")) +
            "\n\nPara trocar, mande /obra e o número (por exemplo, /obra 2).", c);
    }

    private async Task FotoAsync(long chatId, Usuario usuario, ObraNoServidor obra, JsonElement fotos, string legenda, DateTime agora, CancellationToken c)
    {
        var maior = fotos.EnumerateArray().Last(); // o Telegram manda os tamanhos do menor para o maior
        var tamanho = maior.TryGetProperty("file_size", out var t) && t.TryGetInt64(out var n) ? n : 0;
        if (tamanho > Config.MegabytesPorFoto * 1024L * 1024L)
        {
            await telegram.EnviarAsync(chatId, $"A foto passa de {Config.MegabytesPorFoto} MB. Mande como foto (não como arquivo).", c);
            return;
        }
        var pendentes = await banco.CaixaDeEntrada.CountAsync(i => i.UsuarioId == usuario.Id && i.Tipo == ItemDaCaixa.DeFoto, c);
        if (pendentes >= Config.FotosPendentesPorConta)
        {
            await telegram.EnviarAsync(chatId, $"Já há {pendentes} fotos esperando na caixa de entrada. Importe-as no programa (Conta > Buscar do Telegram) e mande de novo.", c);
            return;
        }
        var bytes = await telegram.BaixarAsync(Texto(maior, "file_id") ?? "", c);
        if (bytes is null || bytes.Length == 0)
        {
            await telegram.EnviarAsync(chatId, "Não deu para receber a foto. Mande de novo.", c);
            return;
        }
        banco.CaixaDeEntrada.Add(new ItemDaCaixa
        {
            Id = Guid.NewGuid(), UsuarioId = usuario.Id, ObraId = obra.Id, Tipo = ItemDaCaixa.DeFoto, CriadoEm = agora, Texto = legenda.Length > 1000 ? legenda[..1000] : legenda,
            Foto = bytes, NomeDaFoto = $"telegram-{agora:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.jpg",
        });
        await banco.SaveChangesAsync(c);
        await telegram.EnviarAsync(chatId, $"📷 Foto guardada na caixa da obra {obra.Nome}" + (legenda.Length > 0 ? $" (\"{legenda}\")" : "") +
            ". Ela aparece para importar quando você abrir a obra no programa (Conta > Buscar do Telegram).", c);
    }

    private async Task PerguntaAsync(long chatId, Usuario usuario, ObraNoServidor obra, string texto, DateTime agora, CancellationToken c)
    {
        var planos = opcoes.Value.Planos;
        var saldo = await Rotas.Cotas.SaldoAsync(banco, usuario, planos, relogio);
        if (saldo.Esgotado)
        {
            await telegram.EnviarAsync(chatId, Rotas.Ia.CotaEsgotada, c);
            return;
        }
        // O nível Padrão, seja do Gemini (direto) ou da Anthropic (pela ponte, 0.47.2).
        var modelo = catalogo.Todos.FirstOrDefault(m => m.Nivel == Niveis.Padrao);
        if (modelo is null || !catalogo.Disponivel(modelo))
        {
            await telegram.EnviarAsync(chatId, Rotas.Ia.SemChave, c);
            return;
        }
        ContextoDoProjeto? retrato;
        try
        {
            retrato = JsonSerializer.Deserialize<ContextoDoProjeto>(obra.Retrato, Json);
        }
        catch (JsonException)
        {
            retrato = null;
        }
        await telegram.DigitandoAsync(chatId, c);

        RespostaIA resposta;
        TokensDoPedido tokens;
        using (var medicao = MedidorDeTokens.Abrir())
        {
            try
            {
                var assistente = modelo.Provedor == Provedores.Anthropic
                    ? new AssistenteGemini(clientes.CreateClient(PonteParaOClaude.Cliente), "pela-ponte", modelo.Id) // a chave de verdade fica na ponte
                    : new AssistenteGemini(clientes.CreateClient(MedidorDeTokens.Cliente), opcoes.Value.Gemini.Chave!, modelo.Id);
                resposta = await assistente.EnviarAsync(new PedidoIA(texto, [], conversas.Historico(chatId), retrato) { Canal = CanaisDaConversa.Telegram }, c);
            }
            catch (ErroDoAssistenteException ex)
            {
                // O motivo vai para o log; no chat, uma mensagem neutra (o assistente fala em "Gemini" mesmo quando é o Claude).
                log.LogWarning("O assistente do Telegram falhou ({Modelo}): {Motivo}", modelo.Id, ex.Message);
                await telegram.EnviarAsync(chatId, NaoRespondeu, c);
                return;
            }
            tokens = medicao.Tokens;
        }
        var custo = tokens.Microdolares(modelo);
        banco.UsosDeIa.Add(UsoDeIa.Novo(usuario.Id, agora, modelo.Id, tokens, custo, RotinasDeIa.Telegram));
        Rotas.Cotas.Debitar(usuario, saldo, custo);

        var mensagem = resposta.Texto.Trim();
        if (resposta.Atualizacoes.Count > 0)
        {
            banco.CaixaDeEntrada.Add(new ItemDaCaixa
            {
                Id = Guid.NewGuid(), UsuarioId = usuario.Id, ObraId = obra.Id, Tipo = ItemDaCaixa.DeAtualizacoes, CriadoEm = agora,
                Texto = texto.Length > 1000 ? texto[..1000] : texto,
                Conteudo = JsonSerializer.Serialize(resposta.Atualizacoes, Json),
                Tarefas = JsonSerializer.Serialize(retrato?.Tarefas.Select(t => new TarefaDaCaixa(t.Numero, t.Id)) ?? [], Json),
            });
            var n = resposta.Atualizacoes.Count;
            mensagem += $"\n\n📥 {(n == 1 ? "1 atualização ficou" : $"{n} atualizações ficaram")} na caixa da obra {obra.Nome}: confira e aplique no programa (Conta > Buscar do Telegram).";
        }
        await banco.SaveChangesAsync(c);
        await Painel.ConferirAlertaAsync(banco, opcoes.Value, email, agora, log, c);
        conversas.Guardar(chatId, new MensagemChat(AutorMensagem.Usuario, texto, relogio.GetLocalNow(), []),
            new MensagemChat(AutorMensagem.Assistente, resposta.Texto, relogio.GetLocalNow(), []));
        await telegram.EnviarAsync(chatId, mensagem.Length == 0 ? "Anotado." : mensagem, c);
    }

    private static string? Texto(JsonElement objeto, string nome) =>
        objeto.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>Uma tarefa do retrato do momento (o Nº que a IA usou e o id da tarefa).</summary>
public sealed record TarefaDaCaixa(int Numero, Guid Id);
