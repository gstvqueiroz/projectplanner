using static ProjectPlanner.Core.Tests.Datas;

namespace ProjectPlanner.Core.Tests;

public class CalculadoraCronogramaTests
{
    // Cenário de referência, só com FS e lag 0. Dias úteis numerados a partir do início do projeto:
    // dia 0 = seg 28/09/2026, dia 4 = sex 02/10, dia 5 = seg 05/10 ... dia 13 = qui 15/10.
    //
    //   Tarefa  Duração  Predecessoras
    //     A        3      —
    //     B        4      A
    //     C        2      A
    //     D        5      B
    //     E        3      C
    //     F        2      D, E
    //
    // Ida (ES = maior EF das predecessoras + 1; EF = ES + duração − 1):
    //   A: ES 0                    EF 0 + 3 − 1 = 2
    //   B: ES 2 + 1 = 3            EF 3 + 4 − 1 = 6
    //   C: ES 2 + 1 = 3            EF 3 + 2 − 1 = 4
    //   D: ES 6 + 1 = 7            EF 7 + 5 − 1 = 11
    //   E: ES 4 + 1 = 5            EF 5 + 3 − 1 = 7
    //   F: ES max(11, 7) + 1 = 12  EF 12 + 2 − 1 = 13   → fim do projeto: dia 13
    //
    // Volta (LF = menor LS das sucessoras − 1, ou o fim do projeto; LS = LF − duração + 1):
    //   F: LF 13                   LS 13 − 2 + 1 = 12
    //   E: LF 12 − 1 = 11          LS 11 − 3 + 1 = 9
    //   D: LF 12 − 1 = 11          LS 11 − 5 + 1 = 7
    //   C: LF 9 − 1 = 8            LS 8 − 2 + 1 = 7
    //   B: LF 7 − 1 = 6            LS 6 − 4 + 1 = 3
    //   A: LF min(3, 7) − 1 = 2    LS 2 − 3 + 1 = 0
    //
    // Folga (LS − ES): A 0, B 0, C 4, D 0, E 4, F 0 → caminho crítico A → B → D → F.
    private static MontadorCronograma CenarioDeReferencia() => new MontadorCronograma()
        .ComTarefa("A", 3)
        .ComTarefa("B", 4, "A")
        .ComTarefa("C", 2, "A")
        .ComTarefa("D", 5, "B")
        .ComTarefa("E", 3, "C")
        .ComTarefa("F", 2, "D", "E");

    [Fact]
    public void Calcular_CenarioDeReferencia_BateComOCalculoManual()
    {
        var cenario = CenarioDeReferencia();

        cenario.Calcular();

        cenario.Linhas().Should().Equal(
            //           Nome  ES  EF  LS  LF  Folga  Crítica
            new LinhaCpm("A",   0,  2,  0,  2,  0,    true),
            new LinhaCpm("B",   3,  6,  3,  6,  0,    true),
            new LinhaCpm("C",   3,  4,  7,  8,  4,    false),
            new LinhaCpm("D",   7, 11,  7, 11,  0,    true),
            new LinhaCpm("E",   5,  7,  9, 11,  4,    false),
            new LinhaCpm("F",  12, 13, 12, 13,  0,    true));
    }

    [Fact]
    public void Calcular_CenarioDeReferencia_RetornaFimDoProjetoETarefasECaminhoCriticos()
    {
        var cenario = CenarioDeReferencia();

        var resultado = cenario.Calcular();

        resultado.DataTermino.Should().Be(new DateTime(2026, 10, 15));
        resultado.TarefasCriticas.Select(t => t.Nome).Should().Equal("A", "B", "D", "F");
        MontadorCronograma.Caminhos(resultado).Should().Equal("A → B → D → F");
    }

    [Fact]
    public void Calcular_SoTarefasComFolgaZeroSaoCriticas()
    {
        var cenario = CenarioDeReferencia();

        cenario.Calcular();

        var tarefas = cenario.Cronograma.Tarefas;
        tarefas.Where(t => t.EhCritica).Select(t => (t.Nome, t.Folga)).Should()
            .Equal(("A", 0), ("B", 0), ("D", 0), ("F", 0));
        tarefas.Where(t => !t.EhCritica).Select(t => (t.Nome, t.Folga)).Should()
            .Equal(("C", 4), ("E", 4));
    }

    [Fact]
    public void Calcular_ConverteDiasUteisEmDatasPulandoFinsDeSemana()
    {
        var cenario = CenarioDeReferencia();

        cenario.Calcular();

        // E vai do dia 5 ao 7 (seg 05/10 a qua 07/10) e pode ir até os dias 9 a 11 (sex 09/10 a ter 13/10).
        var e = cenario["E"];
        e.InicioMaisCedo.Should().Be(new DateTime(2026, 10, 5));
        e.TerminoMaisCedo.Should().Be(new DateTime(2026, 10, 7));
        e.InicioMaisTarde.Should().Be(new DateTime(2026, 10, 9));
        e.TerminoMaisTarde.Should().Be(new DateTime(2026, 10, 13));
    }

    [Fact]
    public void Calcular_CronogramaComUmaTarefa_ElaEhCritica()
    {
        var cenario = new MontadorCronograma().ComTarefa("A", 4);

        var resultado = cenario.Calcular();

        cenario.Linha("A").Should().Be(new LinhaCpm("A", 0, 3, 0, 3, 0, true));
        resultado.DataTermino.Should().Be(Quinta);
        MontadorCronograma.Caminhos(resultado).Should().Equal("A");
    }

    [Fact]
    public void Calcular_CronogramaVazio_TerminaNoInicioDoProjeto()
    {
        var resultado = CalculadoraCronograma.Calcular(new Cronograma(Segunda));

        resultado.DataTermino.Should().Be(Segunda);
        resultado.TarefasCriticas.Should().BeEmpty();
        resultado.CaminhosCriticos.Should().BeEmpty();
    }

    // A (10 dias) →SS→ B (2 dias). B é a única tarefa sem sucessoras, mas termina no dia 1; o projeto
    // só termina no dia 9, com A. Volta: B LF 9, LS 8; A: Início(A) ≤ Início(B) = 8 daria LF 17,
    // mas o fim do projeto limita a 9 → LS 0. A é crítica; B tem folga 8.
    [Fact]
    public void Calcular_FimDoProjetoEhOMaiorTerminoEntreTodasAsTarefas()
    {
        var cenario = new MontadorCronograma()
            .ComTarefa("A", 10)
            .ComTarefa("B", 2)
            .ComVinculo("A", "B", TipoDependencia.SS);

        var resultado = cenario.Calcular();

        resultado.DataTermino.Should().Be(DiaUtil(9));
        cenario.Linhas().Should().Equal(
            new LinhaCpm("A", 0, 9, 0, 9, 0, true),
            new LinhaCpm("B", 0, 1, 8, 9, 8, false));
        MontadorCronograma.Caminhos(resultado).Should().Equal("A");
    }

    [Fact]
    public void TarefaNuncaCalculada_NaoTemDatasCalculadasNemEhCritica()
    {
        var tarefa = new Tarefa("A", Segunda, 3);

        tarefa.InicioMaisCedo.Should().BeNull();
        tarefa.TerminoMaisCedo.Should().BeNull();
        tarefa.InicioMaisTarde.Should().BeNull();
        tarefa.TerminoMaisTarde.Should().BeNull();
        tarefa.Folga.Should().BeNull();
        tarefa.EhCritica.Should().BeFalse();
    }

    [Fact]
    public void Calcular_NaoAlteraAsDatasPlanejadas()
    {
        var planejada = new DateTime(2027, 3, 1);
        var cronograma = new Cronograma(Segunda);
        var tarefa = new Tarefa("A", planejada, 4);
        cronograma.AdicionarTarefa(tarefa);

        CalculadoraCronograma.Calcular(cronograma);

        tarefa.DataInicio.Should().Be(planejada);
        tarefa.DataTermino.Should().Be(new DateTime(2027, 3, 4));
        tarefa.InicioMaisCedo.Should().Be(Segunda);
    }

    // Com C passando de 2 para 7 dias, o ramo A → C → E → F fica mais longo:
    // C 3–9, E 10–12, F max(11, 12) + 1 = 13 → 13–14. Na volta, B e D ganham 1 dia de folga.
    [Fact]
    public void Calcular_DeNovoDepoisDeUmaAlteracao_AtualizaOsResultados()
    {
        var cenario = CenarioDeReferencia();
        cenario.Calcular();

        cenario["C"].AlterarDuracao(7);
        var resultado = cenario.Calcular();

        resultado.DataTermino.Should().Be(DiaUtil(14));
        MontadorCronograma.Caminhos(resultado).Should().Equal("A → C → E → F");
        cenario.Linha("B").Folga.Should().Be(1);
        cenario.Linha("D").Folga.Should().Be(1);
    }

    [Fact]
    public void Calcular_ComFimDoProjetoAlemDaMaiorDataSuportada_LancaExcecaoSemAlterarAsTarefas()
    {
        // Começando na última segunda representável, A (5 dias) acaba em 31/12/9999 e B não cabe.
        var cronograma = new Cronograma(UltimaSegunda);
        var a = new Tarefa("A", Segunda, 5);
        var b = new Tarefa("B", Segunda, 1);
        cronograma.AdicionarTarefa(a);
        cronograma.AdicionarTarefa(b);
        cronograma.AdicionarPredecessora(new Predecessora(a.Id, b.Id));

        var calcular = () => CalculadoraCronograma.Calcular(cronograma);

        calcular.Should().Throw<DomainValidationException>()
            .Which.Propriedade.Should().Be(nameof(Tarefa.TerminoMaisCedo));
        a.InicioMaisCedo.Should().BeNull();
        b.InicioMaisCedo.Should().BeNull();
    }

    [Fact]
    public void Calcular_Nulo_LancaArgumentNullException()
    {
        var calcular = () => CalculadoraCronograma.Calcular(null!);

        calcular.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Calcular_CicloEhRecusadoAntesDoCalculo_EOCronogramaContinuaCalculavel()
    {
        // O Cronograma recusa o vínculo que fecharia o ciclo (Bloco 2), então um grafo com ciclo
        // nunca chega à calculadora. A proteção da própria calculadora está em OrdenacaoTopologicaTests.
        var cenario = new MontadorCronograma()
            .ComTarefa("A", 1)
            .ComTarefa("B", 1, "A")
            .ComTarefa("C", 1, "B");

        var fecharCiclo = () => cenario.ComVinculo("C", "A");

        fecharCiclo.Should().Throw<CircularDependencyException>()
            .Which.Ciclo.Should().Equal(cenario["C"].Id, cenario["A"].Id, cenario["B"].Id, cenario["C"].Id);
        MontadorCronograma.Caminhos(cenario.Calcular()).Should().Equal("A → B → C");
    }
}
