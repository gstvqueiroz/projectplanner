namespace ProjectPlanner.Core.Tests;

// Bloco 31: a restrição "não iniciar antes de" e o prazo "não terminar depois de" no cálculo. Projeto começando na seg
// 28/09/2026: A (3 d) → B (2 d); C (2 d) solta.
public class RestricoesEPrazosTests
{
    private static readonly DateTime Seg = new(2026, 9, 28);

    private static (Cronograma C, Tarefa A, Tarefa B, Tarefa Solta) Obra()
    {
        var c = new Cronograma("Obra", Seg);
        var a = new Tarefa("A", Seg, 3);
        var b = new Tarefa("B", Seg, 2);
        var solta = new Tarefa("C", Seg, 2);
        c.AdicionarTarefa(a);
        c.AdicionarTarefa(b);
        c.AdicionarTarefa(solta);
        c.AdicionarPredecessora(new Predecessora(a.Id, b.Id));
        return (c, a, b, solta);
    }

    private static DateTime D(int dia, int mes = 10) => new(2026, mes, dia);

    // ---- Dias úteis para trás ----

    [Theory]
    [InlineData("2026-09-28", -1, "2026-09-25")] // seg − 1 = sex
    [InlineData("2026-09-30", -2, "2026-09-28")]
    [InlineData("2026-09-30", -3, "2026-09-25")]
    [InlineData("2026-09-30", -7, "2026-09-21")]
    [InlineData("2026-10-02", -5, "2026-09-25")] // sex − 5 = sex anterior
    [InlineData("2026-09-28", 0, "2026-09-28")]
    public void DiasUteis_AdicionarNegativo(string de, int dias, string esperado)
    {
        DiasUteis.Adicionar(DateTime.Parse(de), dias).Should().Be(DateTime.Parse(esperado));
    }

    [Theory]
    [InlineData("2026-09-28", "2026-10-05", 5)]
    [InlineData("2026-10-05", "2026-09-28", -5)]
    [InlineData("2026-09-28", "2026-09-28", 0)]
    [InlineData("2026-09-28", "2026-09-25", -1)]
    public void DiasUteis_IndiceComSinal(string inicio, string data, long esperado)
    {
        DiasUteis.Indice(DateTime.Parse(inicio), DateTime.Parse(data)).Should().Be(esperado);
    }

    // ---- Restrição ----

    [Fact]
    public void Restricao_ADataSeguraOInicio_EASucessoraAcompanha()
    {
        var (c, a, b, _) = Obra();
        a.DefinirRestricaoDeInicio(D(5)); // seg 05/10

        CalculadoraCronograma.Calcular(c);

        (a.InicioMaisCedo, a.TerminoMaisCedo, b.InicioMaisCedo).Should().Be((D(5), D(7), D(8)));
        (a.NaoIniciarAntesDe, a.DataInicio).Should().Be((D(5), D(5)));
    }

    [Fact]
    public void Restricao_AntesDoQueAsPredecessorasPermitem_ValeAPredecessora()
    {
        var (c, _, b, _) = Obra();
        b.DefinirRestricaoDeInicio(Seg);

        CalculadoraCronograma.Calcular(c);

        b.InicioMaisCedo.Should().Be(D(1), "A termina na qua 30/09");
        b.NaoIniciarAntesDe.Should().Be(Seg, "a restrição fica guardada");
    }

    [Fact]
    public void Restricao_NoFimDeSemana_VaiParaASegunda_EAntesDoProjeto_NaoMuda()
    {
        var (c, _, _, solta) = Obra();

        solta.DefinirRestricaoDeInicio(D(3)); // sábado
        CalculadoraCronograma.Calcular(c);
        (solta.NaoIniciarAntesDe, solta.InicioMaisCedo).Should().Be((D(5), D(5)));

        solta.DefinirRestricaoDeInicio(new DateTime(2026, 9, 1));
        CalculadoraCronograma.Calcular(c);
        solta.InicioMaisCedo.Should().Be(Seg);

        solta.DefinirRestricaoDeInicio(null);
        CalculadoraCronograma.Calcular(c);
        (solta.NaoIniciarAntesDe, solta.InicioMaisCedo).Should().Be((null, Seg));
    }

    [Fact]
    public void Restricao_DaFolgaAsOutras_ECriticaAcompanha()
    {
        var (c, a, b, solta) = Obra();
        solta.DefinirRestricaoDeInicio(D(12));

        var resultado = CalculadoraCronograma.Calcular(c);

        (resultado.DataTermino, solta.EhCritica, a.EhCritica, a.Folga).Should().Be((D(13), true, false, 7));
        _ = b;
    }

    [Fact]
    public void Restricao_NumMarco_NoComecoDoDia()
    {
        var c = new Cronograma("Obra", Seg);
        var a = new Tarefa("A", Seg, 3);
        var marco = new Tarefa("Entrega", Seg, 0);
        c.AdicionarTarefa(a);
        c.AdicionarTarefa(marco);
        c.AdicionarPredecessora(new Predecessora(a.Id, marco.Id));
        marco.DefinirRestricaoDeInicio(D(6));

        CalculadoraCronograma.Calcular(c);

        (marco.InicioMaisCedo, marco.MarcoNoFimDoDia).Should().Be((D(6), false));
        marco.DefinirRestricaoDeInicio(null);
        CalculadoraCronograma.Calcular(c);
        (marco.InicioMaisCedo, marco.MarcoNoFimDoDia).Should().Be((new DateTime(2026, 9, 30), true), "sem a restrição, fica no fim do dia de A");
    }

    [Fact]
    public void Restricao_NumResumo_Recusada_EVirarResumoTiraARestricaoEOValor()
    {
        var c = new Cronograma("Obra", Seg);
        var fase = new Tarefa("Fase", Seg, 1);
        c.AdicionarTarefa(fase);
        fase.DefinirRestricaoDeInicio(D(5));
        fase.DefinirValor(1000);

        c.AdicionarTarefa(new Tarefa("Sub", Seg, 2), fase.Id);

        (fase.NaoIniciarAntesDe, fase.EhResumo).Should().Be((null, true));
        CalculadoraCronograma.Calcular(c);
        fase.Valor.Should().BeNull("a subtarefa não tem valor");
        ((Action)(() => fase.DefinirRestricaoDeInicio(D(5)))).Should().Throw<DomainValidationException>()
            .WithMessage("O início de um resumo é calculado a partir das subtarefas.");
    }

    // ---- Término muda a duração ----

    [Fact]
    public void Termino_MudaADuracao_MantendoOInicioCalculado()
    {
        var (c, _, b, _) = Obra();
        CalculadoraCronograma.Calcular(c); // B: 01/10 a 02/10

        b.AlterarTerminoMantendoOInicio(D(7));
        CalculadoraCronograma.Calcular(c);

        (b.Duracao, b.InicioMaisCedo, b.TerminoMaisCedo).Should().Be((5, D(1), D(7)));
        ((Action)(() => b.AlterarTerminoMantendoOInicio(new DateTime(2026, 9, 30)))).Should().Throw<DomainValidationException>();
        b.AlterarTerminoMantendoOInicio(D(4)); // domingo: volta para a sexta
        b.Duracao.Should().Be(2);
    }

    [Fact]
    public void Termino_NumMarco_MoveOMarco()
    {
        var c = new Cronograma("Obra", Seg);
        var marco = new Tarefa("Entrega", Seg, 0);
        c.AdicionarTarefa(marco);

        marco.AlterarTerminoMantendoOInicio(D(10)); // sábado: o marco vai para a sexta
        CalculadoraCronograma.Calcular(c);

        (marco.Duracao, marco.NaoIniciarAntesDe, marco.InicioMaisCedo).Should().Be((0, D(9), D(9)));
    }

    // ---- Prazo ----

    [Fact]
    public void Prazo_QueCabe_SoDiminuiAFolga()
    {
        var (c, a, b, solta) = Obra();
        solta.DefinirPrazo(D(1)); // C termina 29/09

        CalculadoraCronograma.Calcular(c);

        (solta.Folga, solta.ForaDoPrazo, solta.EhCritica).Should().Be((2, false, false));
        (a.Folga, b.Folga).Should().Be((0, 0));
    }

    [Fact]
    public void Prazo_QueNaoCabe_FolgaNegativa_NaTarefaENasQueAEmpurram()
    {
        var (c, a, b, solta) = Obra();
        b.DefinirPrazo(D(1)); // B termina 02/10

        var resultado = CalculadoraCronograma.Calcular(c);

        (b.Folga, a.Folga, b.ForaDoPrazo, a.ForaDoPrazo).Should().Be((-1, -1, true, false));
        (b.EhCritica, a.EhCritica).Should().Be((true, true));
        (b.TerminoMaisCedo, b.TerminoMaisTarde, a.TerminoMaisTarde).Should().Be((D(2), D(1), new DateTime(2026, 9, 29)));
        resultado.DataTermino.Should().Be(D(2), "o prazo não move nada");
        solta.Folga.Should().Be(3, "C termina em 29/09, e o projeto, em 02/10");
    }

    [Fact]
    public void Prazo_AntesDoInicioDoProjeto_DatasTardeAntesDoInicio()
    {
        var (c, a, b, _) = Obra();
        b.DefinirPrazo(new DateTime(2026, 9, 25)); // sex antes do início

        CalculadoraCronograma.Calcular(c);

        (b.Folga, b.TerminoMaisTarde, b.InicioMaisTarde, a.TerminoMaisTarde, a.InicioMaisTarde).Should().Be(
            (-5, new DateTime(2026, 9, 25), new DateTime(2026, 9, 24), new DateTime(2026, 9, 23), new DateTime(2026, 9, 21)));
    }

    [Fact]
    public void Prazo_NoResumo_ValeParaAsSubtarefas_EOResumoFicaForaDoPrazo()
    {
        var c = new Cronograma("Obra", Seg);
        var fase = new Tarefa("Fase", Seg, 1);
        var x = new Tarefa("X", Seg, 4);
        var y = new Tarefa("Y", Seg, 2);
        c.AdicionarTarefa(fase);
        c.AdicionarTarefa(x, fase.Id);
        c.AdicionarTarefa(y, fase.Id);
        fase.DefinirPrazo(new DateTime(2026, 9, 30)); // qua: X termina qui 01/10

        CalculadoraCronograma.Calcular(c);

        (x.Folga, y.Folga, fase.Folga, fase.ForaDoPrazo, x.ForaDoPrazo).Should().Be((-1, 1, -1, true, false));
        fase.DefinirPrazo(null);
        CalculadoraCronograma.Calcular(c);
        x.Folga.Should().Be(0);
    }

    [Fact]
    public void Prazo_NumMarco_NoComecoDoDiaOuNoFim()
    {
        var c = new Cronograma("Obra", Seg);
        var a = new Tarefa("A", Seg, 3);
        var depois = new Tarefa("Depois de A", Seg, 0);
        var solto = new Tarefa("Solto", Seg, 0);
        c.AdicionarTarefa(a);
        c.AdicionarTarefa(depois);
        c.AdicionarTarefa(solto);
        c.AdicionarPredecessora(new Predecessora(a.Id, depois.Id)); // fica no fim de 30/09
        solto.DefinirRestricaoDeInicio(D(2));
        depois.DefinirPrazo(new DateTime(2026, 9, 30));
        solto.DefinirPrazo(D(2));

        CalculadoraCronograma.Calcular(c);

        (depois.ForaDoPrazo, depois.Folga >= 0, solto.ForaDoPrazo, solto.Folga >= 0).Should().Be((false, true, false, true));
        solto.DefinirPrazo(D(1));
        CalculadoraCronograma.Calcular(c);
        (solto.ForaDoPrazo, solto.Folga < 0).Should().Be((true, true));
    }

    // ---- Cópia ----

    [Fact]
    public void Copiar_LevaRestricaoPrazoEValor()
    {
        var (c, a, _, _) = Obra();
        a.DefinirRestricaoDeInicio(D(5));
        a.DefinirPrazo(D(9));
        a.DefinirValor(1234.567m);

        var copia = c.Copiar().Tarefas[0];

        (copia.NaoIniciarAntesDe, copia.Prazo, copia.Valor).Should().Be((D(5), D(9), 1234.57m));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1_000_000_000_000)]
    public void Valor_ForaDosLimites_Recusado(double valor)
    {
        var (_, a, _, _) = Obra();

        ((Action)(() => a.DefinirValor((decimal)valor))).Should().Throw<DomainValidationException>();
        a.DefinirValor(null);
        a.Valor.Should().BeNull();
    }
}
