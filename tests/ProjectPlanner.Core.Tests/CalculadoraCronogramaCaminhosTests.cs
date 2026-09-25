namespace ProjectPlanner.Core.Tests;

public class CalculadoraCronogramaCaminhosTests
{
    // Dois caminhos independentes, com dois inícios e dois fins:
    //   A(4) → B(6): A 0–3, B 4–9   → termina no dia 9, o fim do projeto
    //   C(2) → D(3): C 0–1, D 2–4   → termina no dia 4
    // Volta: B LF 9, LS 4; A LF 3, LS 0; D LF 9, LS 7; C LF 7 − 1 = 6, LS 5.
    // Folga: A 0, B 0, C 5, D 5 → só A → B é crítico.
    [Fact]
    public void Calcular_DoisCaminhosParalelos_SoOMaisLongoEhCritico()
    {
        var cenario = new MontadorCronograma()
            .ComTarefa("A", 4)
            .ComTarefa("B", 6, "A")
            .ComTarefa("C", 2)
            .ComTarefa("D", 3, "C");

        var resultado = cenario.Calcular();

        cenario.Linhas().Should().Equal(
            //           Nome  ES  EF  LS  LF  Folga  Crítica
            new LinhaCpm("A",   0,  3,  0,  3,  0,    true),
            new LinhaCpm("B",   4,  9,  4,  9,  0,    true),
            new LinhaCpm("C",   0,  1,  5,  6,  5,    false),
            new LinhaCpm("D",   2,  4,  7,  9,  5,    false));
        MontadorCronograma.Caminhos(resultado).Should().Equal("A → B");
    }

    // Losango: A(2); B(3) e C(3) depois de A; D(2) depois de B e de C.
    //   Ida: A 0–1; B 2–4; C 2–4; D 5–6.  Volta: D LF 6, LS 5; B e C LF 4, LS 2; A LF 1, LS 0.
    //   Tudo com folga 0: dois caminhos críticos empatados, A → B → D e A → C → D.
    [Fact]
    public void Calcular_DoisCaminhosCriticosEmpatados_RetornaOsDois()
    {
        var cenario = new MontadorCronograma()
            .ComTarefa("A", 2)
            .ComTarefa("B", 3, "A")
            .ComTarefa("C", 3, "A")
            .ComTarefa("D", 2, "B", "C");

        var resultado = cenario.Calcular();

        cenario.Linhas().Should().Equal(
            new LinhaCpm("A", 0, 1, 0, 1, 0, true),
            new LinhaCpm("B", 2, 4, 2, 4, 0, true),
            new LinhaCpm("C", 2, 4, 2, 4, 0, true),
            new LinhaCpm("D", 5, 6, 5, 6, 0, true));
        resultado.TarefasCriticas.Select(t => t.Nome).Should().Equal("A", "B", "C", "D");
        MontadorCronograma.Caminhos(resultado).Should().Equal("A → B → D", "A → C → D");
    }

    // Duas cadeias independentes que terminam juntas no dia 4 (dois inícios e dois fins críticos):
    //   A(3) → B(2): 0–2, 3–4.   C(2) → D(3): 0–1, 2–4.
    [Fact]
    public void Calcular_CadeiasIndependentesEmpatadas_SaoDoisCaminhosCriticos()
    {
        var cenario = new MontadorCronograma()
            .ComTarefa("A", 3)
            .ComTarefa("B", 2, "A")
            .ComTarefa("C", 2)
            .ComTarefa("D", 3, "C");

        var resultado = cenario.Calcular();

        cenario.Linhas().Should().OnlyContain(l => l.Folga == 0 && l.Critica);
        MontadorCronograma.Caminhos(resultado).Should().Equal("A → B", "C → D");
    }

    // A(2) → B(3) → C(1), e também A → C. C começa no dia 5 por causa de B; pelo vínculo A → C
    // poderia começar já no dia 2. As três são críticas, mas A → C tem folga e não é caminho crítico.
    [Fact]
    public void Calcular_VinculoComFolgaEntreTarefasCriticas_NaoFormaCaminhoCritico()
    {
        var cenario = new MontadorCronograma()
            .ComTarefa("A", 2)
            .ComTarefa("B", 3, "A")
            .ComTarefa("C", 1, "B", "A");

        var resultado = cenario.Calcular();

        resultado.TarefasCriticas.Select(t => t.Nome).Should().Equal("A", "B", "C");
        MontadorCronograma.Caminhos(resultado).Should().Equal("A → B → C");
    }

    [Fact]
    public void Calcular_TarefasAdicionadasForaDeOrdem_RetornaCriticasEmOrdemTopologica()
    {
        // O cenário de referência de CalculadoraCronogramaTests, com as tarefas adicionadas de trás
        // para frente.
        var cenario = new MontadorCronograma()
            .ComTarefa("F", 2)
            .ComTarefa("E", 3)
            .ComTarefa("D", 5)
            .ComTarefa("C", 2)
            .ComTarefa("B", 4)
            .ComTarefa("A", 3)
            .ComVinculo("A", "B")
            .ComVinculo("A", "C")
            .ComVinculo("B", "D")
            .ComVinculo("C", "E")
            .ComVinculo("D", "F")
            .ComVinculo("E", "F");

        var resultado = cenario.Calcular();

        resultado.TarefasCriticas.Select(t => t.Nome).Should().Equal("A", "B", "D", "F");
        MontadorCronograma.Caminhos(resultado).Should().Equal("A → B → D → F");
        cenario.Linha("F").Should().Be(new LinhaCpm("F", 12, 13, 12, 13, 0, true));
    }
}
