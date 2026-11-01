using ProjectPlanner.Core;
using ProjectPlanner.Core.Gantt;
using ProjectPlanner.Core.Recursos;

namespace ProjectPlanner.Core.Tests;

// Bloco 71: o calendário do projeto em todo o cálculo: o caminho crítico pula os feriados e as paralisações, conta o sábado
// de trabalho, as defasagens e os prazos vão pelos mesmos dias úteis, e a troca do calendário entra na cópia (o Desfazer).
public class CalendarioNoCronogramaTests
{
    private static DateTime D(string data) => DateTime.Parse(data);

    private static DateOnly O(string data) => DateOnly.Parse(data);

    private static readonly DayOfWeek[] SegundaASabado =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday];

    // Começa na segunda 05/10/2026: A (5 dias), B (5 dias) depois de A, C (3 dias) depois de B com 2 dias de defasagem.
    private static (Cronograma Cronograma, Tarefa A, Tarefa B, Tarefa C) Obra(CalendarioDoProjeto calendario)
    {
        var c = new Cronograma("Obra", D("2026-10-05"));
        c.DefinirCalendario(calendario);
        var (a, b, t) = (new Tarefa("A", D("2026-10-05"), 5), new Tarefa("B", D("2026-10-05"), 5), new Tarefa("C", D("2026-10-05"), 3));
        foreach (var x in new[] { a, b, t })
            c.AdicionarTarefa(x);
        c.AdicionarPredecessora(new Predecessora(a.Id, b.Id, TipoDependencia.FS, 0));
        c.AdicionarPredecessora(new Predecessora(b.Id, t.Id, TipoDependencia.FS, 2));
        CalculadoraCronograma.Calcular(c);
        return (c, a, b, t);
    }

    private static string Datas(Tarefa t) => $"{t.InicioMaisCedo:dd/MM}–{t.TerminoMaisCedo:dd/MM}";

    [Fact]
    public void Padrao_ComoAntes()
    {
        var (_, a, b, c) = Obra(CalendarioDoProjeto.Padrao);

        (Datas(a), Datas(b), Datas(c)).Should().Be(("05/10–09/10", "12/10–16/10", "21/10–23/10"));
    }

    [Fact]
    public void Brasil_PulaONossaSenhoraAparecida_EOsFinados()
    {
        var (c, a, b, t) = Obra(CalendarioDoProjeto.Brasil);

        // 12/10 (segunda) é feriado: B começa na terça 13 e termina na segunda 19; a defasagem de 2 dias úteis pula 20 e 21.
        (Datas(a), Datas(b), Datas(t)).Should().Be(("05/10–09/10", "13/10–19/10", "22/10–26/10"));
        CalculadoraCronograma.Calcular(c).DataTermino.Should().Be(D("2026-10-26"));
    }

    [Fact]
    public void SegundaASabado_ComChuva_EUmDomingoTrabalhado()
    {
        var calendario = CalendarioDoProjeto.Criar(SegundaASabado, false, false, [],
            [new ExcecaoDoCalendario(O("2026-10-07"), O("2026-10-08"), "Chuva", false), new ExcecaoDoCalendario(O("2026-10-11"), O("2026-10-11"), "Mutirão", true)]);
        var (_, a, b, _) = Obra(calendario);

        // A: seg 05, ter 06, (chuva qua e qui), sex 09, sáb 10, dom 11 (mutirão) = 5 dias. B: seg 12 a sex 16.
        (Datas(a), Datas(b)).Should().Be(("05/10–11/10", "12/10–16/10"));
    }

    [Fact]
    public void TrocarOCalendario_LevaOInicioEAsDatasInformadasParaDiasUteis_EACopiaLevaJunto()
    {
        var c = new Cronograma("Obra", D("2026-10-12"));
        c.DataInicio.Should().Be(D("2026-10-12"), "no padrão o 12 de outubro é dia útil");
        var tarefa = new Tarefa("A", D("2026-10-12"), 2);
        c.AdicionarTarefa(tarefa);

        c.DefinirCalendario(CalendarioDoProjeto.Brasil);

        (c.DataInicio, tarefa.DataInicio, tarefa.DataTermino).Should().Be((D("2026-10-13"), D("2026-10-13"), D("2026-10-14")));
        tarefa.Calendario.Should().BeSameAs(CalendarioDoProjeto.Brasil);
        var copia = c.Copiar();
        copia.Calendario.Should().BeSameAs(CalendarioDoProjeto.Brasil);
        copia.Tarefas.Single().Calendario.Should().BeSameAs(CalendarioDoProjeto.Brasil);
        var nova = new Tarefa("B", D("2026-11-02"), 1);
        c.AdicionarTarefa(nova);
        nova.DataInicio.Should().Be(D("2026-11-03"), "a tarefa nova passa a contar pelo calendário do projeto (02/11 é Finados)");
    }

    [Fact]
    public void DigitarOTermino_ContaAsDuracoesSemOsFeriados()
    {
        var c = new Cronograma("Obra", D("2026-11-16"));
        c.DefinirCalendario(CalendarioDoProjeto.Brasil);
        var tarefa = new Tarefa("A", D("2026-11-16"), 1);
        c.AdicionarTarefa(tarefa);
        CalculadoraCronograma.Calcular(c);

        tarefa.AlterarTerminoMantendoOInicio(D("2026-11-23"));

        tarefa.Duracao.Should().Be(5, "de 16 a 23/11 são 6 dias de segunda a sexta, menos o 20 (Consciência Negra)");
    }

    [Fact]
    public void RestricaoEPrazo_PeloCalendario()
    {
        var c = new Cronograma("Obra", D("2026-10-05"));
        c.DefinirCalendario(CalendarioDoProjeto.Brasil);
        var tarefa = new Tarefa("A", D("2026-10-05"), 3);
        c.AdicionarTarefa(tarefa);
        tarefa.DefinirRestricaoDeInicio(D("2026-10-12"));
        tarefa.DefinirPrazo(D("2026-10-14"));

        CalculadoraCronograma.Calcular(c);

        (Datas(tarefa), tarefa.Folga).Should().Be(("13/10–15/10", -1), "não começa no feriado, e o prazo de 14/10 fica 1 dia útil curto");
    }

    [Fact]
    public void GanttEUsoDeRecursos_PelosDiasSemTrabalho()
    {
        var (c, a, _, _) = Obra(CalendarioDoProjeto.Brasil);
        var pedreiro = new Recurso(Guid.NewGuid(), "Pedreiro", TipoDeRecurso.Profissional, "dia", 280, 1, null);
        c.Recursos.Adicionar(pedreiro);
        c.Recursos.Atribuir(c.Tarefas[1], [new AtribuicaoDeRecurso(pedreiro.Id, 2)]);

        CalculadoraLayoutGantt.DiasSemTrabalho(c.Calendario, D("2026-10-10"), D("2026-10-13")).Should().Equal(D("2026-10-10"), D("2026-10-11"), D("2026-10-12"));
        var layout = CalculadoraLayoutGantt.Calcular(c, CalculadoraCronograma.Calcular(c), new OpcoesGantt());
        layout.FinsDeSemana.Select(f => f.Dia).Should().Contain(D("2026-10-12"), "o feriado aparece no fundo do Gantt como os fins de semana");
        UsoDeRecursos.Calcular(c).Single().Dias.Select(d => d.Dia).Should().Equal(O("2026-10-13"), O("2026-10-14"), O("2026-10-15"), O("2026-10-16"), O("2026-10-19"));
        UsoDeRecursos.Superalocacoes(c).Single().Should().Match<Superalocacao>(s => s.De == O("2026-10-13") && s.Ate == O("2026-10-19") && s.Dias == 5,
            "o trecho segue seguido por cima do fim de semana");
        a.Calendario.ContarDiasUteis(D("2026-10-01"), D("2026-10-31")).Should().Be(21);
    }

    [Fact]
    public void FisicoFinanceiro_DistribuiPelosDiasUteisDoCalendario()
    {
        var c = new Cronograma("Obra", D("2026-11-16"));
        c.DefinirCalendario(CalendarioDoProjeto.Brasil);
        var tarefa = new Tarefa("A", D("2026-11-16"), 11);
        c.AdicionarTarefa(tarefa);
        tarefa.DefinirValor(11_000);
        CalculadoraCronograma.Calcular(c);

        // 16/11 a 01/12: 11 dias úteis (o 20/11 fica de fora): 10 em novembro e 1 em dezembro.
        Datas(tarefa).Should().Be("16/11–01/12");
        var periodos = Planejamento.PeriodosFinanceiros.Gerar(O("2026-11-01"), O("2026-12-31"), Planejamento.Periodicidade.Mensal);
        var fisico = Planejamento.CalculadoraFisicoFinanceira.Calcular(c, Planejamento.BaseDoFisicoFinanceiro.Atual, periodos, Planejamento.LinhasDoFisicoFinanceiro.Tarefas);
        fisico.Linhas.Single().ValorPorPeriodo.Should().Equal(10_000m, 1_000m);
    }
}
