using static ProjectPlanner.Core.Tests.Datas;

namespace ProjectPlanner.Core.Tests;

// Exemplo de referência do Bloco 11, com o projeto começando na segunda 28/09/2026:
//   Obra
//     Fase 1
//       A (3 dias, 100%)            seg 28/09 – qua 30/09
//       B (2 dias, depois de A)     qui 01/10 – sex 02/10
//       Fase 1 concluída (marco, depois de B)   sex 02/10, no fim do dia
//     Fase 2
//       C (4 dias, depois do marco) seg 05/10 – qui 08/10
// Tudo está no caminho crítico. Fase 1: 5 dias, (3×100 + 2×0) ÷ 5 = 60%. Fase 2: 4 dias, 0%.
// Obra: 28/09 – 08/10, 9 dias, 300 ÷ 9 = 33%.
public class CalculadoraResumosTests
{
    private readonly Cronograma _projeto = new("Projeto", Segunda);

    private Tarefa Nova(string nome, int duracao = 1, Tarefa? pai = null)
    {
        var tarefa = new Tarefa(nome, Segunda, duracao);
        _projeto.AdicionarTarefa(tarefa, pai?.Id);
        return tarefa;
    }

    private void Ligar(Tarefa de, Tarefa para, TipoDependencia tipo = TipoDependencia.FS, int lag = 0) =>
        _projeto.AdicionarPredecessora(new Predecessora(de.Id, para.Id, tipo, lag));

    private (Tarefa Obra, Tarefa Fase1, Tarefa A, Tarefa B, Tarefa Marco, Tarefa Fase2, Tarefa C) Referencia()
    {
        var obra = Nova("Obra");
        var fase1 = Nova("Fase 1", pai: obra);
        var a = Nova("A", 3, fase1);
        var b = Nova("B", 2, fase1);
        var marco = Nova("Fase 1 concluída", 0, fase1);
        var fase2 = Nova("Fase 2", pai: obra);
        var c = Nova("C", 4, fase2);
        Ligar(a, b);
        Ligar(b, marco);
        Ligar(marco, c);
        a.AtualizarPercentualConcluido(100);
        return (obra, fase1, a, b, marco, fase2, c);
    }

    private ResultadoCronograma Calcular() => CalculadoraCronograma.Calcular(_projeto);

    private static (DateTime Inicio, DateTime Termino, int Duracao, int Percentual) Planejado(Tarefa tarefa) =>
        (tarefa.DataInicio, tarefa.DataTermino, tarefa.Duracao, tarefa.PercentualConcluido);

    private static (DateTime? Ies, DateTime? Ief, DateTime? Ils, DateTime? Ilf, int? Folga, bool Critica) Calculado(Tarefa tarefa) =>
        (tarefa.InicioMaisCedo, tarefa.TerminoMaisCedo, tarefa.InicioMaisTarde, tarefa.TerminoMaisTarde, tarefa.Folga, tarefa.EhCritica);

    [Fact]
    public void Referencia_ResumosAgregamDatasDuracaoEPercentualEmTodosOsNiveis()
    {
        var (obra, fase1, _, _, _, fase2, _) = Referencia();

        Calcular();

        var quinta8 = new DateTime(2026, 10, 8);
        Planejado(fase1).Should().Be((Segunda, Sexta, 5, 60));
        Planejado(fase2).Should().Be((ProximaSegunda, quinta8, 4, 0));
        Planejado(obra).Should().Be((Segunda, quinta8, 9, 33));
        Calculado(fase1).Should().Be((Segunda, Sexta, Segunda, Sexta, 0, true));
        Calculado(obra).Should().Be((Segunda, quinta8, Segunda, quinta8, 0, true));
        new[] { obra, fase1, fase2 }.Should().OnlyContain(t => t.Tipo == TipoTarefa.Resumo);
    }

    [Fact]
    public void Referencia_OCaminhoCriticoTemSoAsFolhas()
    {
        var (_, _, a, b, marco, _, c) = Referencia();

        var resultado = Calcular();

        resultado.TarefasCriticas.Should().Equal(a, b, marco, c);
        resultado.CaminhosCriticos.Should().ContainSingle().Which.Should().Equal(a, b, marco, c);
        resultado.DataTermino.Should().Be(new DateTime(2026, 10, 8));
    }

    [Fact]
    public void EditarUmaSubtarefa_AtualizaOResumoEOResumoDeCima()
    {
        // A passa a 5 dias (até sex 02/10): B vai para seg 05 – ter 06, o marco para ter 06, e C para
        // qua 07 – seg 12. Fase 1: 7 dias, 500 ÷ 7 = 71%. Obra: 11 dias, 500 ÷ 11 = 45%.
        var (obra, fase1, a, _, _, fase2, _) = Referencia();
        Calcular();

        a.AlterarDuracao(5);
        Calcular();

        var terca6 = new DateTime(2026, 10, 6);
        var segunda12 = new DateTime(2026, 10, 12);
        Planejado(fase1).Should().Be((Segunda, terca6, 7, 71));
        Planejado(fase2).Should().Be((new DateTime(2026, 10, 7), segunda12, 4, 0));
        Planejado(obra).Should().Be((Segunda, segunda12, 11, 45));
    }

    [Fact]
    public void MudarOPercentualDeUmaSubtarefa_AtualizaOsResumos()
    {
        var (obra, fase1, _, b, _, _, _) = Referencia();

        b.AtualizarPercentualConcluido(50);
        Calcular();

        fase1.PercentualConcluido.Should().Be(80);   // (300 + 100) ÷ 5
        obra.PercentualConcluido.Should().Be(44);    // 400 ÷ 9 = 44,4
    }

    [Fact]
    public void Percentual_PesaCadaDiaDeTrabalhoUmaVez_EmQualquerNivel()
    {
        // Pelas folhas: (5×100 + 5×0 + 5×0) ÷ 15 = 33%. Pelas filhas diretas daria 25%
        // (a subfase, de 5 dias corridos, pesaria 5 em vez de 10).
        var resumo = Nova("Resumo");
        var subfase = Nova("Subfase", pai: resumo);
        var p1 = Nova("P1", 5, subfase);
        var p2 = Nova("P2", 5, subfase);
        var l = Nova("L", 5, resumo);
        Ligar(p1, l);
        Ligar(p2, l);
        p1.AtualizarPercentualConcluido(100);

        Calcular();

        subfase.PercentualConcluido.Should().Be(50);
        resumo.PercentualConcluido.Should().Be(33);
    }

    [Fact]
    public void ResumoSoComMarcos_DuracaoZeroEMediaSimplesDosPercentuais()
    {
        var resumo = Nova("Marcos");
        Nova("M1", 0, resumo);
        var m2 = Nova("M2", 0, resumo);
        m2.AtualizarPercentualConcluido(100);

        Calcular();

        Planejado(resumo).Should().Be((Segunda, Segunda, 0, 50));
        resumo.Tipo.Should().Be(TipoTarefa.Resumo);
    }

    [Fact]
    public void ResumoQueComecaComUmMarcoNoFimDoDia_ComecaNesseDia()
    {
        var x = Nova("X", 5);
        var resumo = Nova("Entrega");
        var marco = Nova("Entregue", 0, resumo);
        Ligar(x, marco);

        Calcular();

        Planejado(resumo).Should().Be((Sexta, Sexta, 0, 0));
    }

    [Fact]
    public void ResumoQueTerminaComUmMarcoNoComecoDoDia_NaoContaEsseDia()
    {
        // O marco fica no começo da segunda 05/10: o resumo vai de seg 28/09 a seg 05/10, mas tem
        // 5 dias de trabalho (seg a sex), como no MS Project.
        var resumo = Nova("Fase");
        var a = Nova("A", 5, resumo);
        var marco = Nova("M", 0, resumo);
        Ligar(a, marco, TipoDependencia.SS, 5);

        Calcular();

        Planejado(resumo).Should().Be((Segunda, ProximaSegunda, 5, 0));
    }

    [Fact]
    public void ResumoSemSubtarefasCriticas_TemAMenorFolgaENaoECritico()
    {
        Nova("A", 5);
        var resumo = Nova("Paralela");
        var d = Nova("D", 1, resumo);
        var e = Nova("E", 2, resumo);

        Calcular();

        (d.Folga, e.Folga).Should().Be((4, 3));
        Calculado(resumo).Should().Be((Segunda, Terca, Quinta, Sexta, 3, false));
    }

    [Fact]
    public void ResumoNaoEntraNoCalculo_ADuracaoQueEleTinhaAntesNaoConta()
    {
        var fase = Nova("Fase", 50);
        Nova("A", 2, fase);

        var resultado = Calcular();

        resultado.DataTermino.Should().Be(Terca);
        resultado.TarefasCriticas.Should().NotContain(fase);
        fase.Duracao.Should().Be(2);
    }

    [Fact]
    public void ResumoQuePerdeAsSubtarefas_VoltaASerTarefaComumComOsUltimosValores()
    {
        var (_, _, _, _, _, fase2, c) = Referencia();
        Calcular();

        _projeto.Promover(c.Id);
        Calcular();

        fase2.Tipo.Should().Be(TipoTarefa.Normal);
        fase2.Duracao.Should().Be(4);
        // Agora é uma tarefa comum sem vínculos: começa no início do projeto.
        (fase2.InicioMaisCedo, fase2.TerminoMaisCedo).Should().Be((Segunda, Quinta));
    }
}
