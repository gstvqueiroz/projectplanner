using static ProjectPlanner.Core.Tests.Datas;

namespace ProjectPlanner.Core.Tests;

public class CalculadoraCronogramaVinculosTests
{
    // Montagem: X(5) → A(5) → [vínculo em teste] → B(3) → C(10), mais S(30) solta, só para fixar o
    // fim do projeto no dia 29 (C nunca passa do dia 24). A fica nos dias 5 a 9.
    // A volta de C e de B não depende do vínculo: C LF 29, LS 20; B LF 19, LS 17.
    //
    // Ida: ES(B) pela regra do tipo, com EF(B) = ES(B) + 2.
    //   FS: Início(B) ≥ Término(A) + 1 + lag = 10 + lag
    //   SS: Início(B) ≥ Início(A) + lag      =  5 + lag
    //   FF: Término(B) ≥ Término(A) + lag    =  9 + lag   → ES = 7 + lag
    //   SF: Término(B) ≥ Início(A) − 1 + lag =  4 + lag   → ES = 2 + lag
    //
    // Volta: LF(A) pela mesma regra, isolando A (o fim do projeto nunca é o limite aqui).
    //   FS: Término(A) ≤ Início(B) − 1 − lag  = 16 − lag
    //   SS: Início(A) ≤ Início(B) − lag       = 17 − lag   → LF = 17 − lag + 4 = 21 − lag
    //   FF: Término(A) ≤ Término(B) − lag     = 19 − lag
    //   SF: Início(A) ≤ Término(B) + 1 − lag  = 20 − lag   → LF = 20 − lag + 4 = 24 − lag
    [Theory]
    //          tipo              lag  ES(B) EF(B) LF(A)
    [InlineData(TipoDependencia.FS,  0,  10,   12,   16)]
    [InlineData(TipoDependencia.FS,  2,  12,   14,   14)]
    [InlineData(TipoDependencia.FS, -2,   8,   10,   18)]
    [InlineData(TipoDependencia.SS,  0,   5,    7,   21)]
    [InlineData(TipoDependencia.SS,  2,   7,    9,   19)]
    [InlineData(TipoDependencia.SS, -2,   3,    5,   23)]
    [InlineData(TipoDependencia.FF,  0,   7,    9,   19)]
    [InlineData(TipoDependencia.FF,  2,   9,   11,   17)]
    [InlineData(TipoDependencia.FF, -2,   5,    7,   21)]
    [InlineData(TipoDependencia.SF,  0,   2,    4,   24)]
    [InlineData(TipoDependencia.SF,  2,   4,    6,   22)]
    [InlineData(TipoDependencia.SF, -2,   0,    2,   26)]
    public void Calcular_CadaTipoELag_DeslocaASucessoraNaIdaEAPredecessoraNaVolta(
        TipoDependencia tipo, int lag, int inicioB, int terminoB, int terminoMaisTardeA)
    {
        var cenario = new MontadorCronograma()
            .ComTarefa("X", 5)
            .ComTarefa("A", 5, "X")
            .ComTarefa("B", 3)
            .ComTarefa("C", 10, "B")
            .ComTarefa("S", 30)
            .ComVinculo("A", "B", tipo, lag);

        var resultado = cenario.Calcular();

        resultado.DataTermino.Should().Be(DiaUtil(29));
        cenario.Linha("B").ES.Should().Be(inicioB);
        cenario.Linha("B").EF.Should().Be(terminoB);
        cenario.Linha("A").LF.Should().Be(terminoMaisTardeA);
    }

    [Fact]
    public void Calcular_FSComLagPositivo_EsperaOsDiasUteisDoLag()
    {
        // A vai de seg 28/09 a sex 02/10. Com FS +2, a espera é seg 05/10 e ter 06/10, e B (3 dias)
        // vai de qua 07/10 (dia 7) a sex 09/10 (dia 9).
        var cenario = new MontadorCronograma()
            .ComTarefa("A", 5)
            .ComTarefa("B", 3)
            .ComVinculo("A", "B", TipoDependencia.FS, 2);

        cenario.Calcular();

        cenario["B"].InicioMaisCedo.Should().Be(new DateTime(2026, 10, 7));
        cenario["B"].TerminoMaisCedo.Should().Be(new DateTime(2026, 10, 9));
    }

    [Fact]
    public void Calcular_FSComLead_AntecipaASucessora()
    {
        // A vai de seg 28/09 a sex 02/10 (dias 0 a 4). Com FS −2, B começa 2 dias úteis antes do dia
        // seguinte ao término de A: 5 − 2 = dia 3, qui 01/10. B (3 dias) termina seg 05/10 (dia 5),
        // depois do fim de semana. Fim do projeto: dia 5. Volta: B LF 5, LS 3; A LF ≤ 3 − 1 + 2 = 4.
        var cenario = new MontadorCronograma()
            .ComTarefa("A", 5)
            .ComTarefa("B", 3)
            .ComVinculo("A", "B", TipoDependencia.FS, -2);

        var resultado = cenario.Calcular();

        cenario["B"].InicioMaisCedo.Should().Be(new DateTime(2026, 10, 1));
        cenario["B"].TerminoMaisCedo.Should().Be(new DateTime(2026, 10, 5));
        cenario.Linhas().Should().Equal(
            new LinhaCpm("A", 0, 4, 0, 4, 0, true),
            new LinhaCpm("B", 3, 5, 3, 5, 0, true));
        MontadorCronograma.Caminhos(resultado).Should().Equal("A → B");
    }

    [Fact]
    public void Calcular_LeadQueCairiaAntesDoInicioDoProjeto_ComecaNoInicioDoProjeto()
    {
        // SS −3 a partir de A (dia 0) daria o dia −3, mas nenhuma tarefa começa antes do projeto.
        var cenario = new MontadorCronograma()
            .ComTarefa("A", 5)
            .ComTarefa("B", 3)
            .ComVinculo("A", "B", TipoDependencia.SS, -3);

        cenario.Calcular();

        cenario["B"].InicioMaisCedo.Should().Be(Segunda);
    }

    [Fact]
    public void Calcular_LeadExtremo_NaoEstouraEComecaNoInicioDoProjeto()
    {
        var cenario = new MontadorCronograma()
            .ComTarefa("A", 5)
            .ComTarefa("B", 3)
            .ComVinculo("A", "B", TipoDependencia.FS, int.MinValue);

        cenario.Calcular();

        cenario.Linha("B").Should().Be(new LinhaCpm("B", 0, 2, 2, 4, 2, false));
    }

    [Fact]
    public void Calcular_LagExtremo_LancaExcecaoDeDataMaxima()
    {
        var cenario = new MontadorCronograma()
            .ComTarefa("A", 5)
            .ComTarefa("B", 3)
            .ComVinculo("A", "B", TipoDependencia.FS, int.MaxValue);

        var calcular = () => cenario.Calcular();

        calcular.Should().Throw<DomainValidationException>()
            .Which.Propriedade.Should().Be(nameof(Tarefa.TerminoMaisCedo));
    }

    // Cenário com os quatro tipos:
    //   Tarefa  Duração  Vínculo de entrada
    //     A        5      —
    //     B        3      A →SS +2→ B
    //     C        4      A →FF→ C
    //     D        2      B →FS→ D
    //     E        3      D →SF→ E
    //
    // Ida:
    //   A: ES 0, EF 4
    //   B: Início(B) ≥ Início(A) + 2 = 2                     → ES 2, EF 4
    //   C: Término(C) ≥ Término(A) = 4 → ES ≥ 4 − 3 = 1      → ES 1, EF 4
    //   D: Início(D) ≥ Término(B) + 1 = 5                    → ES 5, EF 6
    //   E: Término(E) ≥ Início(D) − 1 = 4 → ES ≥ 4 − 2 = 2   → ES 2, EF 4
    //   Fim do projeto: maior EF = 6 (D)
    //
    // Volta:
    //   E: sem sucessoras → LF 6, LS 4
    //   D: SF para E: Início(D) ≤ Término(E) + 1 = 7 → LF ≤ 7 + 1 = 8; o fim do projeto limita → LF 6, LS 5
    //   C: sem sucessoras → LF 6, LS 3
    //   B: FS para D: Término(B) ≤ Início(D) − 1 = 4 → LF 4, LS 2
    //   A: SS +2 para B: Início(A) ≤ Início(B) − 2 = 0 → LF ≤ 0 + 4 = 4
    //      FF para C:    Término(A) ≤ Término(C) = 6
    //      → LF min(4, 6, 6) = 4, LS 0
    //
    // Folga: A 0, B 0, C 2, D 0, E 2 → caminho crítico A → B → D.
    [Fact]
    public void Calcular_ComOsQuatroTiposDeDependencia_BateComOCalculoManual()
    {
        var cenario = new MontadorCronograma()
            .ComTarefa("A", 5)
            .ComTarefa("B", 3)
            .ComTarefa("C", 4)
            .ComTarefa("D", 2, "B")
            .ComTarefa("E", 3)
            .ComVinculo("A", "B", TipoDependencia.SS, 2)
            .ComVinculo("A", "C", TipoDependencia.FF)
            .ComVinculo("D", "E", TipoDependencia.SF);

        var resultado = cenario.Calcular();

        cenario.Linhas().Should().Equal(
            //           Nome  ES  EF  LS  LF  Folga  Crítica
            new LinhaCpm("A",   0,  4,  0,  4,  0,    true),
            new LinhaCpm("B",   2,  4,  2,  4,  0,    true),
            new LinhaCpm("C",   1,  4,  3,  6,  2,    false),
            new LinhaCpm("D",   5,  6,  5,  6,  0,    true),
            new LinhaCpm("E",   2,  4,  4,  6,  2,    false));
        MontadorCronograma.Caminhos(resultado).Should().Equal("A → B → D");
    }
}
