using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ProjectPlanner.IA;
using ProjectPlanner.Servidor.Dados;
using ProjectPlanner.Servidor.Servicos;

namespace ProjectPlanner.Servidor.Tests;

// Bloco 53: um grupo do Telegram por obra: o dono liga com /obra, a equipe ganha papéis, a conversa do grupo não gasta nada
// e o bot só age em fotos, #atualizacao, menções e respostas a ele. Os créditos são do dono.
public sealed class GruposDoTelegramTests : IDisposable
{
    private const long Dono = 777_001; // o chat privado do dono (no Telegram, o id do chat privado é o id da pessoa)
    private const long Grupo = -100_500;
    private const long Joao = 888_002;
    private const long Bia = 888_003;
    private const string Bot = "ProjectPlannerTesteBot";
    private static readonly Guid Projeto = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly Servidor _s = new();

    public void Dispose() => _s.Dispose();

    private static ContextoDoProjeto Retrato() => new("Casa da Ana", new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 18), new DateOnly(2026, 10, 18),
    [
        new TarefaNoContexto(1, "1", "Alvenaria", 0, TipoNoContexto.Tarefa, 15, new DateOnly(2026, 10, 15), new DateOnly(2026, 11, 4), 40, "", 0, true, null, 0,
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001")),
    ]);

    private static string Resposta(string json) => new JsonObject
    {
        ["candidates"] = new JsonArray(new JsonObject
        {
            ["content"] = new JsonObject { ["role"] = "model", ["parts"] = new JsonArray(new JsonObject { ["text"] = json }) },
        }),
        ["usageMetadata"] = new JsonObject { ["promptTokenCount"] = 1_000, ["candidatesTokenCount"] = 100 },
    }.ToJsonString();

    /// <summary>A conta Profissional da Ana, com a obra marcada e o chat privado ligado; o grupo ainda não.</summary>
    private async Task Preparar(string plano = Planos.Profissional)
    {
        var sessao = await _s.CriarConta();
        await AdminTests.TrocarPlano(_s, "ana@exemplo.com", plano, Servidor.ChaveDeAdmin);
        var marcar = new HttpRequestMessage(HttpMethod.Put, $"/v1/obras/{Projeto}") { Content = JsonContent.Create(new { nome = "Casa da Ana", retrato = Retrato() }, options: AtendenteDoTelegram.Json) };
        marcar.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessao.Acesso);
        await _s.CreateClient().SendAsync(marcar);
        var gerar = new HttpRequestMessage(HttpMethod.Post, "/v1/telegram/codigo");
        gerar.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessao.Acesso);
        var codigo = JsonDocument.Parse(await (await _s.CreateClient().SendAsync(gerar)).Content.ReadAsStringAsync()).RootElement.GetProperty("codigo").GetString();
        await Atender(new JsonObject { ["chat"] = new JsonObject { ["id"] = Dono, ["type"] = "private" }, ["from"] = Pessoa(Dono, "Ana"), ["text"] = $"/start {codigo}" });
    }

    private static JsonObject Pessoa(long id, string nome, string? usuario = null, bool bot = false)
    {
        var pessoa = new JsonObject { ["id"] = id, ["first_name"] = nome, ["is_bot"] = bot };
        if (usuario is not null)
            pessoa["username"] = usuario;
        return pessoa;
    }

    // Uma mensagem no grupo.
    private Task NoGrupo(long de, string nome, string? texto, string? foto = null, JsonObject? respondendo = null, long grupo = Grupo, JsonObject? extra = null)
    {
        var mensagem = new JsonObject { ["chat"] = new JsonObject { ["id"] = grupo, ["type"] = "supergroup", ["title"] = "Obra Casa da Ana" }, ["from"] = Pessoa(de, nome) };
        if (foto is not null)
        {
            mensagem["photo"] = new JsonArray(new JsonObject { ["file_id"] = foto, ["file_size"] = 90_000 });
            if (texto is not null)
                mensagem["caption"] = texto;
        }
        else if (texto is not null)
            mensagem["text"] = texto;
        if (respondendo is not null)
            mensagem["reply_to_message"] = new JsonObject { ["from"] = respondendo, ["text"] = "…" };
        foreach (var (chave, valor) in extra ?? [])
            mensagem[chave] = valor?.DeepClone();
        return Atender(mensagem);
    }

    private async Task Atender(JsonObject mensagem)
    {
        using var escopo = _s.Services.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<AtendenteDoTelegram>()
            .AtenderAsync(JsonDocument.Parse(new JsonObject { ["update_id"] = 1, ["message"] = mensagem }.ToJsonString()).RootElement, default);
    }

    private List<string> DoGrupo() => _s.Telegram.Mensagens.Where(m => m.Chat == Grupo).Select(m => m.Texto).ToList();

    private async Task Ligar()
    {
        await Preparar();
        await NoGrupo(Dono, "Ana", "/obra@" + Bot + " 1");
    }

    [Fact]
    public async Task Ligar_SoODonoComOChatPrivado_ListaEEscolheAObra()
    {
        await Preparar();
        await NoGrupo(Joao, "João", "bom dia, pessoal");
        DoGrupo().Should().BeEmpty("a conversa do grupo é ignorada, mesmo sem obra");
        await NoGrupo(Joao, "João", $"@{Bot} oi");
        DoGrupo().Last().Should().Be(AtendenteDoTelegram.GrupoSemObra);
        await NoGrupo(Joao, "João", "/obra 1");
        DoGrupo().Last().Should().StartWith("Só o dono da conta ProjectPlanner liga o grupo");

        await NoGrupo(Dono, "Ana", "/obra");
        DoGrupo().Last().Should().Contain("1. Casa da Ana").And.Contain("/obra 1");
        await NoGrupo(Dono, "Ana", $"/obra@{Bot} 1");

        DoGrupo().Last().Should().StartWith("Pronto: este grupo é da obra Casa da Ana.");
        _s.NoBanco(b => b.GruposDoTelegram.Single()).Should().Match<GrupoDoTelegram>(g => g.ChatId == Grupo && g.Titulo == "Obra Casa da Ana");
        _s.NoBanco(b => b.MembrosDosGrupos.Single()).Should().Match<MembroDoGrupo>(m => m.PessoaId == Dono && m.Papel == "engenharia");
        _s.Gemini.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task Conversa_Ignorada_MencaoERespostaAoBotChamamAIA_ComONomeDeQuemFalou_ECreditosDoDono()
    {
        await Ligar();
        var antes = DoGrupo().Count;

        await NoGrupo(Joao, "João", "alguém viu a betoneira?");
        await NoGrupo(Joao, "João", "/start@OutroBot"); // comando de outro bot
        DoGrupo().Count.Should().Be(antes);
        _s.Gemini.Pedidos.Should().BeEmpty("nada disso gasta");

        await NoGrupo(Joao, "João", $"@{Bot} quanto falta da alvenaria?");
        var pergunta = JsonNode.Parse(_s.Gemini.Pedidos.Single().Corpo)!["contents"]!.AsArray()[^1]!["parts"]![0]!["text"]!.GetValue<string>();
        pergunta.Should().Be("João: quanto falta da alvenaria?", "sem a menção, com o nome de quem perguntou");
        DoGrupo().Last().Should().Be("Olá!");
        await NoGrupo(Bia, "Bia", "e a laje?", respondendo: Pessoa(1, Bot, Bot, bot: true));
        _s.Gemini.Pedidos.Should().HaveCount(2, "responder ao bot também é falar com ele");

        var dono = _s.NoBanco(b => b.Usuarios.Single().Id);
        _s.NoBanco(b => b.UsosDeIa.ToList()).Should().HaveCount(2).And.OnlyContain(u => u.UsuarioId == dono && u.Rotina == "telegram");
        _s.NoBanco(b => b.MembrosDosGrupos.OrderBy(m => m.Nome).Select(m => m.Nome + ":" + m.Papel).ToList())
            .Should().Equal("Ana:engenharia", "Bia:campo", "João:campo");
    }

    [Fact]
    public async Task FotoSemIA_EAtualizacaoComAMarca_VaoParaACaixaComOAutor()
    {
        await Ligar();
        _s.Telegram.Arquivos["foto-1"] = [1, 2, 3];
        _s.Gemini.Resposta = Resposta("""{"mensagem":"Anotei.","alteracoes":[],"atualizacoes":[{"tipo":"percentual","tarefa":"1","percentual":70}]}""");

        await NoGrupo(Joao, "João", "parede da cozinha", foto: "foto-1");
        await NoGrupo(Joao, "João", "#atualizacao alvenaria em 70%");

        var itens = _s.NoBanco(b => b.CaixaDeEntrada.OrderBy(i => i.Tipo).ToList());
        itens.Select(i => (i.Tipo, i.Texto)).Should().Equal(("atualizacoes", "João: alvenaria em 70%"), ("foto", "João: parede da cozinha"));
        DoGrupo().Should().Contain(t => t.StartsWith("📷 Foto de João guardada na caixa da obra Casa da Ana"));
        DoGrupo().Last().Should().Contain("1 atualização ficou na caixa da obra Casa da Ana");
        _s.Gemini.Pedidos.Should().HaveCount(1, "a foto não passa pela IA");
    }

    [Fact]
    public async Task Papeis_DonoMuda_QuemAcompanhaSoPergunta_EQuemNaoGerenciaNaoMuda()
    {
        await Ligar();
        await NoGrupo(Joao, "João", $"@{Bot} oi");

        await NoGrupo(Dono, "Ana", "/papel acompanhar", respondendo: Pessoa(Joao, "João"));
        DoGrupo().Last().Should().Be("João agora é \"acompanhar\".");
        _s.Telegram.Arquivos["foto-2"] = [9];
        await NoGrupo(Joao, "João", null, foto: "foto-2");
        await NoGrupo(Joao, "João", "#atualização laje concluída");
        DoGrupo().TakeLast(2).Should().OnlyContain(t => t == AtendenteDoTelegram.SoQuemEnvia);
        _s.NoBanco(b => b.CaixaDeEntrada.Count()).Should().Be(0);

        // Quem acompanha pergunta; uma atualização proposta pela IA não vai para a caixa.
        _s.Gemini.Resposta = Resposta("""{"mensagem":"Ok.","alteracoes":[],"atualizacoes":[{"tipo":"percentual","tarefa":"1","percentual":80}]}""");
        await NoGrupo(Joao, "João", $"@{Bot} põe a alvenaria em 80%");
        DoGrupo().Last().Should().Contain("Quem só acompanha a obra não manda atualizações");
        _s.NoBanco(b => b.CaixaDeEntrada.Count()).Should().Be(0);

        await NoGrupo(Joao, "João", "/papel engenharia", respondendo: Pessoa(Bia, "Bia"));
        DoGrupo().Last().Should().Be("Só o dono e quem é da engenharia mudam os papéis.");
        await NoGrupo(Dono, "Ana", "/papel acompanhar", respondendo: Pessoa(Dono, "Ana"));
        DoGrupo().Last().Should().Be("O dono da conta é sempre da engenharia.");
        await NoGrupo(Dono, "Ana", "/papel chefe", respondendo: Pessoa(Bia, "Bia"));
        DoGrupo().Last().Should().StartWith("Para mudar o papel de alguém");
        await NoGrupo(Dono, "Ana", "/papel engenharia", respondendo: Pessoa(Bia, "Bia"));
        await NoGrupo(Bia, "Bia", "/papel campo", respondendo: Pessoa(Joao, "João"));
        DoGrupo().Last().Should().Be("João agora é \"campo\".", "quem é da engenharia também muda");

        await NoGrupo(Joao, "João", "/equipe");
        DoGrupo().Last().Should().Contain("• Ana: engenharia (dono)").And.Contain("• Bia: engenharia").And.Contain("• João: campo");
    }

    [Fact]
    public async Task Sair_SoODono_ESupergrupoLevaALigacao_EGratisNaoUsa()
    {
        await Ligar();
        await NoGrupo(Joao, "João", $"@{Bot} oi");

        await NoGrupo(Dono, "Ana", "", extra: new JsonObject { ["migrate_to_chat_id"] = -100_999 });
        _s.NoBanco(b => (b.GruposDoTelegram.Single().ChatId, b.MembrosDosGrupos.Count(m => m.ChatId == -100_999))).Should().Be((-100_999L, 2));

        await NoGrupo(Joao, "João", "/sair", grupo: -100_999);
        _s.Telegram.Mensagens.Last().Texto.Should().Be("Só o dono da conta desliga o grupo.");
        await NoGrupo(Dono, "Ana", "/sair", grupo: -100_999);
        _s.NoBanco(b => (b.GruposDoTelegram.Count(), b.MembrosDosGrupos.Count())).Should().Be((0, 0));

        await AdminTests.TrocarPlano(_s, "ana@exemplo.com", Planos.Gratis, Servidor.ChaveDeAdmin);
        await NoGrupo(Dono, "Ana", "/obra 1");
        await NoGrupo(Joao, "João", $"@{Bot} oi");
        DoGrupo().Last().Should().Contain("é do plano Profissional");
        _s.Gemini.Pedidos.Should().HaveCount(1, "só a pergunta de antes");
    }

    [Fact]
    public async Task BotEntraNoGrupo_Explica_EMensagensDeBotsSaoIgnoradas()
    {
        await Preparar();
        await NoGrupo(Dono, "Ana", null, extra: new JsonObject { ["new_chat_members"] = new JsonArray(Pessoa(1, Bot, Bot, bot: true)) });
        DoGrupo().Single().Should().StartWith("Olá! Sou o assistente do ProjectPlanner.");

        await Atender(new JsonObject
        {
            ["chat"] = new JsonObject { ["id"] = Grupo, ["type"] = "group" }, ["from"] = Pessoa(5, "Outro", "OutroBot", bot: true), ["text"] = $"@{Bot} oi",
        });
        DoGrupo().Should().HaveCount(1);
    }
}
