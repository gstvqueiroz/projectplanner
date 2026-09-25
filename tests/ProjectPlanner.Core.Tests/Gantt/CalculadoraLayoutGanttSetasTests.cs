using System.Globalization;
using ProjectPlanner.Core.Gantt;

namespace ProjectPlanner.Core.Tests.Gantt;

public class CalculadoraLayoutGanttSetasTests
{
    private static readonly OpcoesGantt Opcoes = new(10, alturaLinha: 30) { Cultura = CultureInfo.GetCultureInfo("pt-BR") };

    // Montagem: A (linha 0, 5 dias de seg 28/09 a sex 02/10) → [vínculo em teste] → B (linha 1, 3 dias).
    // Eixo desde sex 25/09 a 10 px/dia: A vai de X 30 a 80, no centro Y 15; B fica no centro Y 45.
    // Afastamento da seta: 8. Ponta: 6 de comprimento.
    private static SetaGantt SetaEntre(TipoDependencia tipo, int lag = 0, bool sucessoraEmCima = false)
    {
        var cenario = new MontadorCronograma();
        if (sucessoraEmCima)
            cenario.ComTarefa("B", 3).ComTarefa("A", 5);
        else
            cenario.ComTarefa("A", 5).ComTarefa("B", 3);
        cenario.ComVinculo("A", "B", tipo, lag);

        return CalculadoraLayoutGantt.Calcular(cenario.Cronograma, cenario.Calcular(), Opcoes).Setas.Single();
    }

    private static void DeveSerOrtogonal(SetaGantt seta) =>
        seta.Pontos.Zip(seta.Pontos.Skip(1)).Should().OnlyContain(s => s.First.X == s.Second.X || s.First.Y == s.Second.Y);

    // FS: B começa seg 05/10 (X 100 a 130). Sai do fim de A (80) e chega no início de B (100).
    // Direto: 80 + 8 = 88 cabe antes de 100 − 8 = 92.
    [Fact]
    public void FS_SaiDoFimDaPredecessoraEChegaNoInicioDaSucessora()
    {
        var seta = SetaEntre(TipoDependencia.FS);

        seta.Pontos.Should().Equal(new PontoGantt(80, 15), new PontoGantt(88, 15), new PontoGantt(88, 45), new PontoGantt(100, 45));
        seta.Ponta.Should().Equal(new PontoGantt(100, 45), new PontoGantt(94, 42), new PontoGantt(94, 48));
    }

    // SS: B começa junto com A, seg 28/09 (X 30 a 60). Sai do início de A (30) para a esquerda e
    // chega no início de B (30) andando para a direita.
    [Fact]
    public void SS_SaiDoInicioDaPredecessoraEChegaNoInicioDaSucessora()
    {
        var seta = SetaEntre(TipoDependencia.SS);

        seta.Pontos.Should().Equal(new PontoGantt(30, 15), new PontoGantt(22, 15), new PontoGantt(22, 45), new PontoGantt(30, 45));
        seta.Ponta[1].X.Should().Be(24, "a ponta aponta para a direita, entrando pelo início");
    }

    // FF: B termina junto com A, sex 02/10 (X 50 a 80). Sai do fim de A (80) e chega no fim de B (80)
    // andando para a esquerda.
    [Fact]
    public void FF_SaiDoFimDaPredecessoraEChegaNoFimDaSucessora()
    {
        var seta = SetaEntre(TipoDependencia.FF);

        seta.Pontos.Should().Equal(new PontoGantt(80, 15), new PontoGantt(88, 15), new PontoGantt(88, 45), new PontoGantt(80, 45));
        seta.Ponta[1].X.Should().Be(86, "a ponta aponta para a esquerda, entrando pelo fim");
    }

    // SF: B precisaria terminar na sex 25/09, antes do projeto; começa então no início, seg 28/09
    // (X 30 a 60). Sai do início de A (30) para a esquerda e precisa chegar no fim de B (60) vindo da
    // direita: contorna pela divisa entre as linhas (Y 30).
    [Fact]
    public void SF_SaiDoInicioDaPredecessoraEChegaNoFimDaSucessora()
    {
        var seta = SetaEntre(TipoDependencia.SF);

        seta.Pontos.Should().Equal(
            new PontoGantt(30, 15), new PontoGantt(22, 15), new PontoGantt(22, 30),
            new PontoGantt(68, 30), new PontoGantt(68, 45), new PontoGantt(60, 45));
        seta.Ponta[1].X.Should().Be(66);
    }

    [Theory]
    [InlineData(TipoDependencia.FS, 80.0, 100.0)]
    [InlineData(TipoDependencia.SS, 30.0, 30.0)]
    [InlineData(TipoDependencia.FF, 80.0, 80.0)]
    [InlineData(TipoDependencia.SF, 30.0, 60.0)]
    public void CadaTipo_LigaOsPontosCertosPelosCentrosDasBarrasComSegmentosRetos(TipoDependencia tipo, double xSaida, double xChegada)
    {
        var seta = SetaEntre(tipo);

        seta.Tipo.Should().Be(tipo);
        seta.Saida.Should().Be(new PontoGantt(xSaida, 15));
        seta.Chegada.Should().Be(new PontoGantt(xChegada, 45));
        DeveSerOrtogonal(seta);
    }

    // FS com lead de 2: B começa qui 01/10 (X 60 a 90), antes do fim de A (80). A seta sai para a
    // direita (88), contorna pela divisa (Y 30) e entra no início de B vindo da esquerda (52).
    [Fact]
    public void FSComLead_ContornaParaChegarPelaEsquerda()
    {
        var seta = SetaEntre(TipoDependencia.FS, lag: -2);

        seta.Pontos.Should().Equal(
            new PontoGantt(80, 15), new PontoGantt(88, 15), new PontoGantt(88, 30),
            new PontoGantt(52, 30), new PontoGantt(52, 45), new PontoGantt(60, 45));
    }

    // Com B na linha 0 e A na linha 1, a seta sobe; o contorno usa a divisa abaixo de B (Y 30).
    [Fact]
    public void SucessoraAcima_ASetaSobe()
    {
        var direta = SetaEntre(TipoDependencia.FS, sucessoraEmCima: true);
        var contorno = SetaEntre(TipoDependencia.FS, lag: -2, sucessoraEmCima: true);

        direta.Pontos.Should().Equal(new PontoGantt(80, 45), new PontoGantt(88, 45), new PontoGantt(88, 15), new PontoGantt(100, 15));
        contorno.Pontos.Should().Equal(
            new PontoGantt(80, 45), new PontoGantt(88, 45), new PontoGantt(88, 30),
            new PontoGantt(52, 30), new PontoGantt(52, 15), new PontoGantt(60, 15));
    }

    [Fact]
    public void Setas_SoSaoCriticasNosVinculosDoCaminhoCritico()
    {
        // A(2) → B(3) → C(1), mais o atalho A → C. As três tarefas são críticas, mas A → C tem folga
        // (C é determinada por B), então só A → B e B → C são setas críticas. No cenário do Bloco 3,
        // A → B → D → F é o caminho crítico; A → C → E → F não.
        var atalho = new MontadorCronograma()
            .ComTarefa("A", 2)
            .ComTarefa("B", 3, "A")
            .ComTarefa("C", 1, "B", "A");
        var referencia = new MontadorCronograma()
            .ComTarefa("A", 3).ComTarefa("B", 4, "A").ComTarefa("C", 2, "A")
            .ComTarefa("D", 5, "B").ComTarefa("E", 3, "C").ComTarefa("F", 2, "D", "E");

        string Criticas(MontadorCronograma cenario) => string.Join(", ",
            CalculadoraLayoutGantt.Calcular(cenario.Cronograma, cenario.Calcular(), Opcoes).Setas
                .Where(s => s.EhCritica)
                .Select(s => $"{Nome(cenario, s.OrigemId)}→{Nome(cenario, s.DestinoId)}"));

        Criticas(atalho).Should().Be("A→B, B→C");
        Criticas(referencia).Should().Be("A→B, B→D, D→F");
    }

    private static string Nome(MontadorCronograma cenario, Guid id) => cenario.Cronograma.Tarefas.Single(t => t.Id == id).Nome;
}
