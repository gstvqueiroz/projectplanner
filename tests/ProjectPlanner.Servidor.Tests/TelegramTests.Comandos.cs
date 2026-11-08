using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ProjectPlanner.IA;
using ProjectPlanner.Servidor.Servicos;

namespace ProjectPlanner.Servidor.Tests;

// Bloco 83: a ajuda com um botão por assunto, o menu de comandos do Telegram, as consultas que respondem sem a IA (/resumo,
// /hoje, /atrasadas), a caixa (/caixa) e o desfazer (/desfazer e o botão embaixo do que foi anotado).
public sealed partial class TelegramTests
{
    private static DateOnly D(string data) => DateOnly.Parse(data, System.Globalization.CultureInfo.InvariantCulture);

    private static TarefaNoContexto T(int numero, string edt, string nome, TipoNoContexto tipo, string inicio, string termino, int duracao, int feito) =>
        new(numero, edt, nome, edt.Count(ch => ch == '.'), tipo, duracao, D(inicio), D(termino), feito, "", 0, false, null, 0, Guid.NewGuid());

    // Quarta 21/10/2026, 10h em Brasília. Os pilares deviam ter terminado ontem; as vigas estão abaixo do previsto; a laje começa
    // hoje; há uma vistoria (marco) hoje e a alvenaria começa na segunda.
    private static ContextoDoProjeto RetratoCompleto() => new("Casa da Ana", D("2026-10-01"), D("2026-11-06"), D("2026-10-21"),
    [
        T(1, "1", "Fundação", TipoNoContexto.Tarefa, "2026-10-01", "2026-10-14", 10, 100),
        T(2, "2", "Estrutura", TipoNoContexto.Resumo, "2026-10-15", "2026-10-30", 12, 40),
        T(3, "2.1", "Pilares", TipoNoContexto.Tarefa, "2026-10-15", "2026-10-20", 4, 60),
        T(4, "2.2", "Vigas", TipoNoContexto.Tarefa, "2026-10-19", "2026-10-23", 5, 20),
        T(5, "2.3", "Laje", TipoNoContexto.Tarefa, "2026-10-21", "2026-10-30", 8, 0),
        T(6, "2.4", "Laje pronta", TipoNoContexto.Marco, "2026-10-30", "2026-10-30", 0, 0),
        T(7, "3", "Alvenaria", TipoNoContexto.Tarefa, "2026-10-26", "2026-11-06", 10, 0),
        T(8, "4", "Vistoria da prefeitura", TipoNoContexto.Marco, "2026-10-21", "2026-10-21", 0, 0),
    ])
    {
        DiasUteis = CalendarioNoContexto.SegundaASexta with { SemTrabalho = [D("2026-10-12")] },
        Andamento = new AndamentoNoContexto(250_000m, 60_000m, 2, 1, D("2026-11-04")),
        Diario =
        [
            "2 dias; nenhum fechado.",
            "RDO nº 001 · 20/10/2026 (ter) · aberto · manhã: Bom; tarde: Bom · efetivo: nenhum",
            "RDO nº 002 · 21/10/2026 (qua) · aberto · manhã: Bom; tarde: Chuva, impraticável · efetivo 6: Pedreiro 4, Servente 2",
        ],
    };

    private async Task<string> PrepararComORetratoCompleto()
    {
        _s.Relogio.Agora = new DateTimeOffset(2026, 10, 21, 13, 0, 0, TimeSpan.Zero); // antes da conta: a sessão não vence no meio
        var acesso = await Preparar();
        await Pedir(HttpMethod.Put, $"/v1/obras/{Projeto}", acesso, new { nome = "Casa da Ana", retrato = RetratoCompleto() });
        return acesso;
    }

    // Um toque num botão de uma mensagem do bot no chat privado.
    private async Task Tocar(string dados, long mensagem = 5000, string texto = "…")
    {
        var consulta = new JsonObject
        {
            ["id"] = $"toque-{dados}", ["data"] = dados, ["from"] = new JsonObject { ["id"] = Chat, ["first_name"] = "Ana" },
            ["message"] = new JsonObject { ["message_id"] = mensagem, ["text"] = texto, ["chat"] = new JsonObject { ["id"] = Chat, ["type"] = "private" } },
        };
        using var escopo = _s.Services.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<AtendenteDoTelegram>()
            .AtenderAsync(JsonDocument.Parse(new JsonObject { ["update_id"] = 3, ["callback_query"] = consulta }.ToJsonString()).RootElement, default);
    }

    [Fact]
    public async Task Ajuda_UmBotaoPorAssunto_TrocaAMesmaMensagem_EVolta()
    {
        await Preparar();
        await Mandar("/ajuda");

        var menu = _s.Telegram.UltimaComBotoes;
        menu.Texto.Should().Be("👷 Assistente da obra no Telegram\nObra: Casa da Ana (/obra troca)\n\n" +
                               "Escreva (ou mande um áudio) como falaria com o engenheiro: eu respondo e anoto. O que muda a obra fica na caixa de entrada, e você confere e " +
                               "aplica no programa (Conta > Buscar do Telegram).\n\nToque num assunto para ver exemplos:");
        menu.Botoes.GroupBy(b => b.Linha).Select(g => g.Count()).Should().Equal(2, 2, 2, 2, 3);
        menu.Botoes.Select(b => b.Dados).Should().Equal("aj:andamento", "aj:diario", "aj:fotos", "aj:custos", "aj:os", "aj:kanban", "aj:grupo",
            "aj:comandos", "cmd:resumo", "cmd:hoje", "cmd:atrasadas");
        menu.Botoes.Should().OnlyContain(b => System.Text.Encoding.UTF8.GetByteCount(b.Dados) <= 64, "o Telegram aceita até 64 bytes");

        await Tocar("aj:diario", menu.Id);
        var assunto = _s.Telegram.Edicoes.Last();
        (assunto.Mensagem, assunto.Texto.Split('\n')[0]).Should().Be((menu.Id, "📒 Diário de obra (RDO)"));
        assunto.Texto.Should().Contain("\"hoje vieram 4 pedreiros e 3 serventes da Construtora Silva\"").And.NotContain("#atualizacao", "no privado não precisa");
        assunto.Botoes.Should().Equal(new BotaoDoTelegram("⬅️ Voltar aos assuntos", "aj:menu"));
        _s.Telegram.RespostasAosBotoes.Last().Should().Be(("toque-aj:diario", ""), "responde ao toque (some o relógio do botão)");

        await Tocar("aj:menu", menu.Id);
        (_s.Telegram.Edicoes.Last().Texto, _s.Telegram.Edicoes.Last().Botoes.Count).Should().Be((menu.Texto, 11));

        foreach (var a in AjudaDoTelegram.Assuntos)
            AjudaDoTelegram.Texto(a.Chave, grupo: false, "Bot")!.Length.Should().BeInRange(100, 1500, "cada assunto cabe numa tela");
        var edicoes = _s.Telegram.Edicoes.Count;
        await Tocar("aj:nada", menu.Id);
        (_s.Telegram.Edicoes.Count, _s.Telegram.RespostasAosBotoes.Last().Texto).Should().Be((edicoes, "Assunto desconhecido."));
        _s.Gemini.Pedidos.Should().BeEmpty("a ajuda não usa a IA");
    }

    [Fact]
    public async Task MenuDeComandos_RegistradoAoSubir_NosPrivadosENosGrupos()
    {
        _ = _s.CreateClient();
        for (var i = 0; i < 50 && _s.Telegram.MenusDeComandos.Count < 2; i++)
            await Task.Delay(50);

        var menus = _s.Telegram.MenusDeComandos.ToDictionary(m => m.Escopo, m => m.Comandos.Select(k => k.Comando).ToList());
        menus[EscoposDosComandos.Privados].Should().Equal("ajuda", "resumo", "hoje", "atrasadas", "caixa", "desfazer", "obra", "sair");
        menus[EscoposDosComandos.Grupos].Should().Contain(["os", "recusar", "equipe", "papel", "resumo", "desfazer"]);
        _s.Telegram.MenusDeComandos.SelectMany(m => m.Comandos).Should().OnlyContain(k =>
            System.Text.RegularExpressions.Regex.IsMatch(k.Comando, "^[a-z0-9_]{1,32}$") && k.Descricao.Length >= 3 && k.Descricao.Length <= 256,
            "as regras do setMyCommands");
    }

    [Fact]
    public async Task Resumo_HojeEAtrasadas_PeloRetrato_SemIA()
    {
        await PrepararComORetratoCompleto();

        await Mandar("/resumo");
        _s.Telegram.Ultima.Should().Be(
            "📊 Casa da Ana — 21/10/2026\n" +
            "Avanço físico: 36% (previsto para hoje: 43%) — 7 pontos atrás\n" +
            "Término: 06/11/2026 (linha de base 04/11/2026, 2 dias depois)\n" +
            "Tarefas: 2 em andamento · 2 atrasada(s) · 1 de 7 concluída(s)\n" +
            "Medido: R$ 60.000,00 de R$ 250.000,00 (24%)\n" +
            "OS: 2 em andamento · 1 vencida(s) ⚠️\n\n" +
            "(Pelo cronograma salvo no programa em 21/10 às 10:00.)\nMais: /hoje · /atrasadas · /caixa");

        await Mandar("/hoje");
        _s.Telegram.Ultima.Should().Be(
            "📅 Hoje, quarta-feira 21/10 — Casa da Ana\n\n" +
            "Em andamento (3):\n" +
            "• 2.1 Pilares: 60% (previsto 100%), devia ter terminado em 20/10 ⚠️\n" +
            "• 2.2 Vigas: 20% (previsto 40%), até 23/10 ⚠️\n" +
            "• 2.3 Laje: 0% (previsto 0%), começa hoje\n\n" +
            "Marcos de hoje:\n• 🏁 4 Vistoria da prefeitura\n\n" +
            "Começam nos próximos 7 dias:\n• 3 Alvenaria em 26/10\n\n" +
            "📒 RDO nº 002 de hoje (aberto): manhã: Bom; tarde: Chuva, impraticável · efetivo 6: Pedreiro 4, Servente 2\n\n" +
            "(Pelo cronograma salvo no programa em 21/10 às 10:00.)");

        await Mandar("/atrasadas@ProjectPlannerTesteBot");
        _s.Telegram.Ultima.Should().Be(
            "⚠️ Atrasadas em Casa da Ana hoje (21/10): 2\n" +
            "• 2.1 Pilares: feito 60%, previsto 100%, terminava em 20/10\n" +
            "• 2.2 Vigas: feito 20%, previsto 40%, termina em 23/10\n\n" +
            "Para atualizar, conte o andamento aqui (\"a impermeabilização terminou\", \"o contrapiso está em 30%\").\n" +
            "(Pelo cronograma salvo no programa em 21/10 às 10:00.)");

        // O atalho da ajuda responde o mesmo comando.
        await Tocar("cmd:resumo");
        _s.Telegram.Ultima.Should().StartWith("📊 Casa da Ana — 21/10/2026");
        _s.Gemini.Pedidos.Should().BeEmpty("as consultas não usam a IA");
        _s.NoBanco(b => b.UsosDeIa.Count()).Should().Be(0, "e não gastam créditos");
    }

    [Fact]
    public void Consultas_DiaSemTrabalho_ObraEmDia_ERetratoAntigo()
    {
        var sabado = D("2026-10-24");
        var salvo = new DateTime(2026, 10, 23, 18, 30, 0);
        ConsultasDaObra.Hoje("Casa", RetratoCompleto(), sabado, salvo).Should().Contain("\nHoje não é dia de trabalho no calendário da obra.\n");

        var emDia = RetratoCompleto() with { Tarefas = [T(1, "1", "Fundação", TipoNoContexto.Tarefa, "2026-10-01", "2026-10-14", 10, 100)] };
        ConsultasDaObra.Atrasadas("Casa", emDia, D("2026-10-21"), salvo)
            .Should().Be("✅ Nenhuma tarefa atrasada em Casa hoje (21/10).\n\n(Pelo cronograma salvo no programa em 23/10 às 18:30.)");

        // Um retrato de antes do Bloco 83 (sem o calendário e sem os números do programa): de segunda a sexta, sem contrato nem OS.
        var antigo = RetratoCompleto() with { DiasUteis = null, Andamento = null, Diario = null };
        var resumo = ConsultasDaObra.Resumo("Casa", antigo, D("2026-10-21"), salvo, new ConsultasDaObra.CaixaEmNumeros(2, 1, 0));
        resumo.Should().Contain("Término: 06/11/2026\n").And.NotContain("Medido").And.NotContain("OS:")
            .And.Contain("📥 Na caixa, esperando o programa: 2 mensagens com atualizações, 1 foto");
        ConsultasDaObra.Hoje("Casa", antigo, D("2026-10-21"), salvo).Should().Contain("📒 O RDO de hoje ainda não está no programa.");
        ConsultasDaObra.Resumo("Casa", antigo with { Tarefas = [] }, D("2026-10-21"), salvo, new ConsultasDaObra.CaixaEmNumeros(0, 0, 0))
            .Should().Contain("O cronograma ainda não tem tarefas.");
    }

    [Fact]
    public async Task Caixa_EDesfazer_PeloComandoEPeloBotao()
    {
        await Preparar();
        _s.Telegram.Arquivos["foto-1"] = [1, 2, 3];
        _s.Gemini.Resposta = RespostaComAtualizacao;
        await Mandar("a alvenaria está em 60%");
        _s.Relogio.Andar(TimeSpan.FromMinutes(5));
        await Mandar(null, foto: "foto-1");
        var confirmacaoDaFoto = _s.Telegram.UltimaComBotoes;

        await Mandar("/caixa");
        _s.Telegram.Ultima.Should().Be("📥 Na caixa da obra Casa da Ana, esperando o programa: 2\n" +
                                       "• 18/10 09:00 · \"a alvenaria está em 60%\" (1 atualização)\n" +
                                       "• 18/10 09:05 · 📷 foto\n\n" +
                                       "No programa: Conta > Buscar do Telegram. /desfazer tira da caixa a última coisa que você mandou.");

        await Mandar("/desfazer");
        _s.Telegram.Ultima.Should().Be("↩️ Tirei da caixa: a foto.");
        _s.Telegram.SemBotoes.Should().Equal((Chat, confirmacaoDaFoto.Id));
        await Mandar("/desfazer");
        _s.Telegram.Ultima.Should().Be("↩️ Tirei da caixa: \"a alvenaria está em 60%\" (1 atualização).");
        await Mandar("/desfazer");
        _s.Telegram.Ultima.Should().Be(AtendenteDoTelegram.NadaParaDesfazer);
        await Mandar("/caixa");
        _s.Telegram.Ultima.Should().Be("📥 A caixa da obra Casa da Ana está vazia: tudo o que foi mandado já está no programa.");

        // O botão Desfazer embaixo do que foi anotado.
        await Mandar("a alvenaria está em 60%");
        var anotado = _s.Telegram.UltimaComBotoes;
        await Tocar(anotado.Botoes.Single().Dados, anotado.Id, anotado.Texto);
        _s.NoBanco(b => b.CaixaDeEntrada.Count()).Should().Be(0);
        _s.Telegram.RespostasAosBotoes.Last().Texto.Should().Be("Desfeito: saiu da caixa.");
        var editada = _s.Telegram.Edicoes.Last();
        (editada.Mensagem, editada.Texto, editada.Botoes.Count).Should().Be((anotado.Id, anotado.Texto + "\n\n↩️ Desfeito: saiu da caixa.", 0));

        // Depois que o programa levou (ou já desfeito), o botão só some.
        await Tocar(anotado.Botoes.Single().Dados, anotado.Id, anotado.Texto);
        _s.Telegram.RespostasAosBotoes.Last().Texto.Should().StartWith("Isto já foi para o programa");
        _s.Telegram.SemBotoes.Last().Should().Be((Chat, anotado.Id));
        await Tocar("cx:nao-e-um-id");
        _s.Telegram.RespostasAosBotoes.Last().Texto.Should().StartWith("Isto já foi para o programa");
    }

    // Bloco 84: "fizemos 12 m² de alvenaria hoje": a IA manda a produção (e o serviço no RDO) e o bot mostra a conta pelo total.
    [Fact]
    public async Task Producao_ABotMostraAContaPeloTotalDaTarefa()
    {
        var acesso = await Preparar();
        var retrato = Retrato() with
        {
            Tarefas = [Retrato().Tarefas[0], Retrato().Tarefas[1] with { Quantidade = 120m, Unidade = "m²", Executado = 48m }],
        };
        await Pedir(HttpMethod.Put, $"/v1/obras/{Projeto}", acesso, new { nome = "Casa da Ana", retrato });
        _s.Gemini.Resposta = Resposta("""
            {"mensagem":"Anotei a alvenaria de hoje.","alteracoes":[],"atualizacoes":[{"tipo":"producao","tarefa":"2","quantidade":12,"unidade":"m²"},
             {"tipo":"diario","acao":"servico","tarefa":"2","quantidade":12,"unidade":"m²"}]}
            """);

        await Mandar("fizemos 12 m² de alvenaria hoje");

        _s.Telegram.UltimaComBotoes.Texto.Should().Be("Anotei a alvenaria de hoje.\n\n📥 Anotado na caixa da obra Casa da Ana:\n" +
                                                      "• 2 Alvenaria: +12 m² executados (10% de 120 m²): 40% → 50% concluído\n" +
                                                      "• RDO de hoje: serviço 2 Alvenaria, 12 m²\n" +
                                                      "Confira e aplique no programa (Conta > Buscar do Telegram).");
        var instrucoes = JsonNode.Parse(_s.Gemini.Pedidos.Last().Corpo)!["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>();
        instrucoes.Should().Contain("ANDAMENTO POR QUANTIDADE").And.Contain("- 2 (2 Alvenaria): 120 m²; executado 48");
    }

    [Fact]
    public async Task ComandoDesconhecido_NaoVaiParaAIA_ESemObraAvisa()
    {
        await Preparar();
        await Mandar("/qualquercoisa");
        _s.Telegram.Ultima.Should().Be("Não conheço o comando /qualquercoisa. /ajuda mostra o que dá para fazer.");
        _s.Gemini.Pedidos.Should().BeEmpty();

        await Mandar("/start");
        _s.Telegram.UltimaComBotoes.Texto.Should().StartWith("👷 Assistente da obra no Telegram", "o /start sem código, já ligado, é a ajuda");
        _s.Telegram.ComBotoes.Should().HaveCount(2, "a ajuda também veio logo depois de ligar a conta");
    }
}
