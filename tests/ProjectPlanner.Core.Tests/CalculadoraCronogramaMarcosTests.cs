using static ProjectPlanner.Core.Tests.Datas;

namespace ProjectPlanner.Core.Tests;

// Projeto começando na segunda 28/09/2026. "A" é sempre uma tarefa de 5 dias: segunda a sexta 02/10.
public class CalculadoraCronogramaMarcosTests
{
    [Fact]
    public void MarcoDepoisDeUmaTarefa_FicaNoDiaEmQueElaTermina_EASucessoraComecaNoDiaUtilSeguinte()
    {
        var cenario = new MontadorCronograma().ComTarefa("A", 5).ComTarefa("M", 0, "A").ComTarefa("B", 2, "M");

        var resultado = cenario.Calcular();

        var marco = cenario["M"];
        (marco.InicioMaisCedo, marco.TerminoMaisCedo, marco.MarcoNoFimDoDia).Should().Be((Sexta, Sexta, true));
        (marco.Folga, marco.EhCritica).Should().Be((0, true));
        cenario["B"].InicioMaisCedo.Should().Be(ProximaSegunda);
        resultado.DataTermino.Should().Be(ProximaTerca);
        resultado.CaminhosCriticos.Should().ContainSingle().Which.Select(t => t.Nome).Should().Equal("A", "M", "B");
    }

    [Fact]
    public void MarcoSemPredecessora_FicaNoInicioDoProjeto_EASucessoraComecaNoMesmoDia()
    {
        var cenario = new MontadorCronograma().ComTarefa("Início da obra", 0).ComTarefa("A", 3, "Início da obra");

        cenario.Calcular();

        var marco = cenario["Início da obra"];
        (marco.InicioMaisCedo, marco.TerminoMaisCedo, marco.MarcoNoFimDoDia).Should().Be((Segunda, Segunda, false));
        cenario["A"].InicioMaisCedo.Should().Be(Segunda);
        marco.EhCritica.Should().BeTrue();
    }

    public static TheoryData<TipoDependencia, int, DateTime, bool> LigacoesComAPredecessora => new()
    {
        // Ligado ao término de A: no fim do dia (A termina sexta 02/10).
        { TipoDependencia.FS, 0, Sexta, true },
        { TipoDependencia.FS, 2, ProximaTerca, true },
        { TipoDependencia.FS, -2, Quarta, true },
        { TipoDependencia.FF, 0, Sexta, true },
        // Ligado ao início de A: no começo do dia (A começa segunda 28/09).
        { TipoDependencia.SS, 0, Segunda, false },
        { TipoDependencia.SS, 2, Quarta, false },
        // Um lead que levaria o marco para antes do projeto o deixa no início do projeto.
        { TipoDependencia.SS, -3, Segunda, false },
        { TipoDependencia.FS, -5, Segunda, false },
    };

    [Theory]
    [MemberData(nameof(LigacoesComAPredecessora))]
    public void Marco_FicaNoInstanteDeQuemODetermina(TipoDependencia tipo, int lag, DateTime dia, bool noFimDoDia)
    {
        var cenario = new MontadorCronograma().ComTarefa("A", 5).ComTarefa("M", 0).ComVinculo("A", "M", tipo, lag);

        cenario.Calcular();

        var marco = cenario["M"];
        (marco.InicioMaisCedo, marco.TerminoMaisCedo, marco.MarcoNoFimDoDia).Should().Be((dia, dia, noFimDoDia));
    }

    [Fact]
    public void MarcoInicioTermino_FicaNoDiaEmQueAPredecessoraComeca()
    {
        // X (2 dias) empurra A para quarta; o marco SF de A fica no começo de quarta.
        var cenario = new MontadorCronograma().ComTarefa("X", 2).ComTarefa("A", 5, "X").ComTarefa("M", 0)
            .ComVinculo("A", "M", TipoDependencia.SF);

        cenario.Calcular();

        (cenario["M"].InicioMaisCedo, cenario["M"].MarcoNoFimDoDia).Should().Be((Quarta, false));
    }

    [Fact]
    public void Empate_EntreFimDeUmDiaEComecoDoSeguinte_ValeOComecoDoDia()
    {
        // A termina no fim de sexta; C começa segunda 28/09, e SS+5 cai no começo da segunda 05/10:
        // o mesmo instante de trabalho, e o relógio mais tarde é a segunda.
        var cenario = new MontadorCronograma().ComTarefa("A", 5).ComTarefa("C", 1).ComTarefa("M", 0, "A")
            .ComVinculo("C", "M", TipoDependencia.SS, 5);

        cenario.Calcular();

        (cenario["M"].InicioMaisCedo, cenario["M"].MarcoNoFimDoDia).Should().Be((ProximaSegunda, false));
    }

    [Fact]
    public void MarcoDepoisDeMarco_HerdaOLadoDoDia()
    {
        var cenario = new MontadorCronograma()
            .ComTarefa("A", 5).ComTarefa("M1", 0, "A").ComTarefa("M2", 0, "M1").ComTarefa("B", 1, "M2")
            .ComTarefa("I1", 0).ComTarefa("I2", 0, "I1");

        cenario.Calcular();

        (cenario["M2"].InicioMaisCedo, cenario["M2"].MarcoNoFimDoDia).Should().Be((Sexta, true));
        cenario["B"].InicioMaisCedo.Should().Be(ProximaSegunda);
        (cenario["I2"].InicioMaisCedo, cenario["I2"].MarcoNoFimDoDia).Should().Be((Segunda, false));
    }

    [Fact]
    public void MarcoNoFimDoProjeto_OTerminoDoProjetoEODiaEmQueAUltimaTarefaTermina()
    {
        var cenario = new MontadorCronograma().ComTarefa("A", 5).ComTarefa("Concluído", 0, "A");

        cenario.Calcular().DataTermino.Should().Be(Sexta);
    }

    [Fact]
    public void MarcoNoComecoDeUmDiaDepoisDeTudo_OProjetoTerminaNesseDia()
    {
        var cenario = new MontadorCronograma().ComTarefa("A", 5).ComTarefa("M", 0).ComVinculo("A", "M", TipoDependencia.SS, 5);

        cenario.Calcular().DataTermino.Should().Be(ProximaSegunda);
    }

    [Fact]
    public void ProjetoSoComUmMarco_TerminaNoInicio()
    {
        var cenario = new MontadorCronograma().ComTarefa("M", 0);

        var resultado = cenario.Calcular();

        resultado.DataTermino.Should().Be(Segunda);
        (cenario["M"].Folga, cenario["M"].EhCritica).Should().Be((0, true));
        resultado.TarefasCriticas.Should().ContainSingle();
    }

    [Fact]
    public void MarcoSolto_TemFolgaAteOFimDoProjeto_SemPassarDele()
    {
        // O marco no começo de segunda pode escorregar até o fim de sexta (5 dias úteis), que é o fim do projeto.
        var cenario = new MontadorCronograma().ComTarefa("A", 5).ComTarefa("M", 0);

        cenario.Calcular();

        var marco = cenario["M"];
        (marco.InicioMaisCedo, marco.MarcoNoFimDoDia).Should().Be((Segunda, false));
        (marco.InicioMaisTarde, marco.TerminoMaisTarde, marco.MarcoTardeNoFimDoDia).Should().Be((Sexta, Sexta, true));
        (marco.Folga, marco.EhCritica).Should().Be((5, false));
    }

    [Fact]
    public void MarcoComFolga_AsDatasTardeFicamNoMesmoLadoDoDia()
    {
        // X (1 dia, segunda) → M no fim de segunda → B (1 dia). B pode terminar até sexta (fim do projeto,
        // por causa de A), então começar sexta; M pode ficar até o fim de quinta: 3 dias de folga.
        var cenario = new MontadorCronograma().ComTarefa("A", 5).ComTarefa("X", 1).ComTarefa("M", 0, "X").ComTarefa("B", 1, "M");

        cenario.Calcular();

        var marco = cenario["M"];
        (marco.InicioMaisCedo, marco.MarcoNoFimDoDia).Should().Be((Segunda, true));
        (marco.InicioMaisTarde, marco.TerminoMaisTarde, marco.MarcoTardeNoFimDoDia).Should().Be((Quinta, Quinta, true));
        marco.Folga.Should().Be(3);
        cenario["B"].InicioMaisTarde.Should().Be(Sexta);
    }
}
