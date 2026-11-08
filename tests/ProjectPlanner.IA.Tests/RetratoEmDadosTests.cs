using System.Text.Json;

namespace ProjectPlanner.IA.Tests;

// Bloco 83: o retrato em dados (o calendário e os números da obra, para o servidor responder sem a IA), o andamento de uma tarefa
// num dia pelas regras do programa e a lista do que a IA anotou, que o Telegram mostra embaixo da resposta.
public sealed class RetratoEmDadosTests
{
    private static DateOnly D(string data) => DateOnly.Parse(data, System.Globalization.CultureInfo.InvariantCulture);

    // Seg 12/10/2026 é feriado; o sábado 17/10 foi trabalhado.
    private static readonly CalendarioNoContexto Calendario = CalendarioNoContexto.SegundaASexta with
    {
        SemTrabalho = [D("2026-10-12")],
        Extras = [D("2026-10-17")],
    };

    private static TarefaNoContexto Tarefa(int numero, string nome, string inicio, string termino, int duracao, int feito,
        TipoNoContexto tipo = TipoNoContexto.Tarefa) =>
        new(numero, numero.ToString(System.Globalization.CultureInfo.InvariantCulture), nome, 0, tipo, duracao, D(inicio), D(termino), feito, "", 0, false, null, 0,
            Guid.NewGuid());

    [Fact]
    public void Calendario_ContaOsDiasUteis_ComFeriadoEDiaExtra()
    {
        (Calendario.EhDiaUtil(D("2026-10-12")), Calendario.EhDiaUtil(D("2026-10-13")), Calendario.EhDiaUtil(D("2026-10-17")), Calendario.EhDiaUtil(D("2026-10-18")))
            .Should().Be((false, true, true, false));
        Calendario.ContarDiasUteis(D("2026-10-12"), D("2026-10-18")).Should().Be(5, "terça a sexta e o sábado trabalhado");
        Calendario.ContarDiasUteis(D("2026-10-18"), D("2026-10-12")).Should().Be(0);
        CalendarioNoContexto.SegundaASexta.ContarDiasUteis(D("2026-10-12"), D("2026-10-18")).Should().Be(5);
    }

    [Fact]
    public void Andamento_PrevistoESituacao_ComoOPrograma()
    {
        // Seis dias úteis, de terça 13/10 a segunda 19/10 (com o sábado 17).
        var alvenaria = Tarefa(1, "Alvenaria", "2026-10-13", "2026-10-19", 6, 50);
        AndamentoNoDia.Previsto(alvenaria, Calendario, D("2026-10-13")).Should().Be(0, "no primeiro dia o previsto ainda é zero");
        AndamentoNoDia.Previsto(alvenaria, Calendario, D("2026-10-16")).Should().Be(50, "3 de 6 dias úteis");
        AndamentoNoDia.Previsto(alvenaria, Calendario, D("2026-10-19")).Should().Be(83, "5 de 6 (o sábado conta), arredondado para baixo");
        AndamentoNoDia.Previsto(alvenaria, Calendario, D("2026-10-20")).Should().Be(100, "depois do término");

        AndamentoNoDia.Situacao(alvenaria, Calendario, D("2026-10-16")).Should().Be(SituacaoNoDia.EmDia);
        AndamentoNoDia.Situacao(alvenaria, Calendario, D("2026-10-19")).Should().Be(SituacaoNoDia.Atrasada);
        AndamentoNoDia.Situacao(alvenaria with { PercentualConcluido = 0 }, Calendario, D("2026-10-13")).Should().Be(SituacaoNoDia.NaoIniciada);
        AndamentoNoDia.Situacao(alvenaria with { PercentualConcluido = 100 }, Calendario, D("2026-10-30")).Should().Be(SituacaoNoDia.Concluida);

        var marco = Tarefa(2, "Laje pronta", "2026-10-15", "2026-10-15", 0, 0, TipoNoContexto.Marco);
        (AndamentoNoDia.Situacao(marco, Calendario, D("2026-10-15")), AndamentoNoDia.Situacao(marco, Calendario, D("2026-10-16")))
            .Should().Be((SituacaoNoDia.NaoIniciada, SituacaoNoDia.Atrasada), "o marco atrasa quando o dia dele passou");
    }

    [Fact]
    public void RetratoEmJson_LevaOCalendarioEOsNumeros_EUmAntigoChegaSemEles()
    {
        var retrato = new ContextoDoProjeto("Casa", D("2026-10-01"), D("2026-12-18"), D("2026-10-18"), [])
        {
            DiasUteis = Calendario,
            Andamento = new AndamentoNoContexto(250_000m, 60_000m, 2, 1, D("2026-12-11")),
        };
        var opcoes = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var lido = JsonSerializer.Deserialize<ContextoDoProjeto>(JsonSerializer.Serialize(retrato, opcoes), opcoes)!;

        lido.Andamento.Should().Be(retrato.Andamento);
        (lido.DiasUteis!.EhDiaUtil(D("2026-10-12")), lido.DiasUteis.EhDiaUtil(D("2026-10-17")), lido.DiasUteis.DiasDaSemana.Count).Should().Be((false, true, 5));
        var antigo = JsonSerializer.Deserialize<ContextoDoProjeto>("""{"nome":"Casa","inicio":"2026-10-01","termino":"2026-12-18","hoje":"2026-10-18","tarefas":[]}""", opcoes)!;
        (antigo.DiasUteis, antigo.Andamento).Should().Be((null, null));
    }

    [Fact]
    public void Resumo_UmaLinhaPorAtualizacao_ComATarefaPeloNumero()
    {
        var retrato = new ContextoDoProjeto("Casa", D("2026-10-01"), D("2026-12-18"), D("2026-10-18"),
        [
            Tarefa(1, "Fundação", "2026-10-01", "2026-10-14", 10, 100) with { Edt = "1.1" },
            Tarefa(2, "Alvenaria", "2026-10-15", "2026-11-04", 15, 40) with { Edt = "1.2" },
        ]);
        AtualizacaoDaObra[] atualizacoes =
        [
            new("percentual", Tarefa: "2", Percentual: 60),
            new("percentual", Tarefa: "9", Percentual: 10),
            new("custo", Valor: 1200m, Descricao: "cimento", Categoria: "material", Tarefa: "1"),
            new("medicao", Tarefa: "2", Quantidade: 30m, Unidade: "m²"),
            new("inicio", Tarefa: "#2", Data: "2026-11-10"),
            new("diario", Acao: "clima", Periodo: "tarde", Nome: "chuva", Praticavel: "nao"),
            new("diario", Acao: "efetivo", Data: "2026-10-17", Nome: "Pedreiro", Quantidade: 4, Fornecedor: "Construtora Silva"),
            new("diario", Acao: "servico", Tarefa: "2", Percentual: 55),
            new("diario", Acao: "ocorrencia", Descricao: "visita do fiscal"),
            new("calendario", Acao: "parar", De: "2026-10-20", Descricao: "chuva"),
            new("quadro", Acao: "mover", Tarefa: "2", Coluna: "Impedida"),
            new("situacaoOs", Os: 3, Acao: "concluir"),
            new("os", Titulo: "Reboco", Executante: "João"),
            new("linhaDeBase"),
        ];

        var linhas = ResumoDasAtualizacoes.Linhas(atualizacoes, retrato);

        linhas.Should().Equal(
            "1.2 Alvenaria: 40% → 60% concluído",
            "tarefa nº 9: 10% concluído",
            "Custo: R$ 1.200,00 · cimento (material) · 1.1 Fundação",
            "Medição: 1.2 Alvenaria, 30 m²",
            "1.2 Alvenaria: não iniciar antes de 10/11",
            "RDO de hoje: tarde chuva, sem trabalho",
            "RDO de 17/10: 4 Pedreiro (Construtora Silva)",
            "RDO de hoje: serviço 1.2 Alvenaria (55%)",
            "RDO de hoje: ocorrência: visita do fiscal",
            "Calendário: sem trabalho em 20/10 (chuva)",
            "Kanban: 1.2 Alvenaria → Impedida",
            "e mais 3");
        ResumoDasAtualizacoes.Linha(atualizacoes[12], retrato).Should().Be("Nova OS: Reboco · João");
        ResumoDasAtualizacoes.Linha(atualizacoes[11], null).Should().Be("OS-003: concluir");
        ResumoDasAtualizacoes.Linha(new AtualizacaoDaObra("percentual", Tarefa: "2", Percentual: 60), null).Should().Be("tarefa nº 2: 60% concluído", "sem o retrato");
        ResumoDasAtualizacoes.Linha(new AtualizacaoDaObra("novidade"), retrato).Should().Be("novidade");
    }

    // Bloco 84: a produção, com a conta pelo total e pelo executado da tarefa no retrato (a mesma regra do programa).
    [Fact]
    public void Resumo_DaProducao_ComAConta()
    {
        var reboco = Tarefa(3, "Reboco", "2026-10-15", "2026-11-04", 15, 30) with { Edt = "1.3", Quantidade = 200m, Unidade = "m²", Executado = 60m };
        var limpeza = Tarefa(4, "Limpeza", "2026-10-15", "2026-10-16", 2, 0);
        var retrato = new ContextoDoProjeto("Casa", D("2026-10-01"), D("2026-12-18"), D("2026-10-18"), [reboco, limpeza]);
        string L(AtualizacaoDaObra a) => ResumoDasAtualizacoes.Linha(a, retrato);

        L(new("producao", Tarefa: "3", Quantidade: 20)).Should().Be("1.3 Reboco: +20 m² executados (10% de 200 m²): 30% → 40% concluído");
        L(new("producao", Tarefa: "3", Quantidade: 120, Unidade: "m²", Acao: "total")).Should().Be("1.3 Reboco: 120 m² executados no total (60% de 200 m²): 30% → 60% concluído");
        L(new("producao", Tarefa: "3", Quantidade: 139.9m, Acao: "somar")).Should().Be("1.3 Reboco: +139,9 m² executados (70% de 200 m²): 30% → 99% concluído", "só o total conclui");
        L(new("producao", Tarefa: "3", Quantidade: 5, Unidade: "m³")).Should().Be("1.3 Reboco: +5 m³ executados (a tarefa é medida em m²: confira no programa)");
        L(new("producao", Tarefa: "4", Quantidade: 10, Unidade: "m²")).Should().Be("4 Limpeza: +10 m² executados", "sem o total no retrato, sem a conta");
        L(new("diario", Tarefa: "3", Quantidade: 20, Acao: "servico")).Should().Be("RDO de hoje: serviço 1.3 Reboco, 20 m²");
        retrato.ComoTexto().Should().Contain("- 3 (1.3 Reboco): 200 m²; executado 60").And.NotContain("(4 Limpeza)");
    }
}
