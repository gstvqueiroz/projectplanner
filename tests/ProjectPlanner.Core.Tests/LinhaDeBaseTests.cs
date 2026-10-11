using ProjectPlanner.Core.Edicao;
using ProjectPlanner.Core.Gantt;
using ProjectPlanner.Core.Planejamento;

namespace ProjectPlanner.Core.Tests;

// Bloco 31: linhas de base (salvar, usar, remover), desvios, a barra no Gantt e o resumo do projeto. Projeto na seg
// 28/09/2026: A (3 d) → B (2 d).
public class LinhaDeBaseTests
{
    private static readonly DateTime Seg = new(2026, 9, 28);
    private static readonly DateTime Agora = new(2026, 9, 28, 10, 0, 0);

    private static (Cronograma C, Tarefa A, Tarefa B) Obra()
    {
        var c = new Cronograma("Obra", Seg);
        var a = new Tarefa("A", Seg, 3);
        var b = new Tarefa("B", Seg, 2);
        c.AdicionarTarefa(a);
        c.AdicionarTarefa(b);
        c.AdicionarPredecessora(new Predecessora(a.Id, b.Id));
        a.DefinirValor(3000);
        b.DefinirValor(2000);
        CalculadoraCronograma.Calcular(c);
        return (c, a, b);
    }

    private static DateTime D(int dia, int mes = 10) => new(2026, mes, dia);

    [Fact]
    public void Salvar_GuardaAsDatasCalculadasDuracoesEValores_EPassaASerAEmUso()
    {
        var (c, a, b) = Obra();

        var lb0 = c.SalvarLinhaDeBase("  Contrato assinado ", Agora);

        (lb0.Numero, lb0.Nome, lb0.SalvaEm, lb0.Termino, c.LinhaDeBaseEmUso).Should().Be((0, "Contrato assinado", Agora, D(2), lb0));
        lb0.Tarefas[a.Id].Should().Be(new TarefaNaLinhaDeBase(Seg, new DateTime(2026, 9, 30), 3, 3000));
        lb0.Tarefas[b.Id].Should().Be(new TarefaNaLinhaDeBase(D(1), D(2), 2, 2000));
        lb0.Descricao.Should().Be("LB0 · Contrato assinado · salva em 28/09/2026 10:00");
        c.SalvarLinhaDeBase(null, Agora).Descricao.Should().Be("LB1 · salva em 28/09/2026 10:00");
        (c.LinhasDeBase.Count, c.LinhaDeBaseEmUso!.Numero).Should().Be((2, 1));
    }

    [Fact]
    public void Salvar_Recusas()
    {
        var vazio = new Cronograma("Obra", Seg);
        ((Action)(() => vazio.SalvarLinhaDeBase(null, Agora))).Should().Throw<DomainValidationException>()
            .WithMessage("O projeto ainda não tem tarefas para guardar na linha de base.");
        vazio.AdicionarTarefa(new Tarefa("A", Seg, 1));
        ((Action)(() => vazio.SalvarLinhaDeBase(null, Agora))).Should().Throw<DomainValidationException>()
            .WithMessage("Calcule o cronograma antes de salvar a linha de base.");

        var (c, _, _) = Obra();
        for (var i = 0; i < Cronograma.LinhasDeBaseMaximas; i++)
            c.SalvarLinhaDeBase(null, Agora);
        ((Action)(() => c.SalvarLinhaDeBase(null, Agora))).Should().Throw<DomainValidationException>().WithMessage("*remova uma antiga*");
        ((Action)(() => c.SalvarLinhaDeBase(new string('x', 81), Agora))).Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Usar_ERemover()
    {
        var (c, _, _) = Obra();
        var lb0 = c.SalvarLinhaDeBase(null, Agora);
        var lb1 = c.SalvarLinhaDeBase(null, Agora);

        c.UsarLinhaDeBase(0);
        c.LinhaDeBaseEmUso.Should().Be(lb0);
        c.UsarLinhaDeBase(null);
        c.LinhaDeBaseEmUso.Should().BeNull();
        ((Action)(() => c.UsarLinhaDeBase(9))).Should().Throw<DomainValidationException>();

        c.UsarLinhaDeBase(1);
        c.RemoverLinhaDeBase(1);
        (c.LinhasDeBase.Single(), c.LinhaDeBaseEmUso).Should().Be((lb0, lb0), "a em uso passa a ser a mais recente que sobrou");
        c.SalvarLinhaDeBase(null, Agora).Numero.Should().Be(1, "o número continua depois da maior");
        ((Action)(() => c.RemoverLinhaDeBase(7))).Should().Throw<DomainValidationException>();
        _ = lb1;
    }

    [Fact]
    public void Desvios_EmDiasUteis_TarefaNovaSemLinhaDeBase()
    {
        var (c, a, b) = Obra();
        var lb = c.SalvarLinhaDeBase(null, Agora);
        a.AlterarDuracao(5); // B passa de 01–02/10 para 05–06/10
        var nova = new Tarefa("Nova", Seg, 1);
        c.AdicionarTarefa(nova);
        CalculadoraCronograma.Calcular(c);

        (DesvioDaLinhaDeBase.DoInicio(b, lb), DesvioDaLinhaDeBase.DoTermino(b, lb), DesvioDaLinhaDeBase.DoTermino(a, lb)).Should().Be((2, 2, 2));
        (DesvioDaLinhaDeBase.DoInicio(nova, lb), DesvioDaLinhaDeBase.DoInicio(b, null)).Should().Be((null, null));
        a.AlterarDuracao(1);
        CalculadoraCronograma.Calcular(c);
        DesvioDaLinhaDeBase.DoTermino(b, lb).Should().Be(-2, "adiantou");
    }

    [Fact]
    public void Copiar_LevaAsLinhasDeBaseEAEmUso_EDesfazerVoltaSemEla()
    {
        var (c, _, _) = Obra();
        var historico = new HistoricoDeAlteracoes(c);
        var lb = c.SalvarLinhaDeBase(null, Agora);
        historico.Registrar("Salvar a linha de base LB0", c);

        var copia = c.Copiar();
        (copia.LinhasDeBase.Single(), copia.LinhaDeBaseEmUso).Should().Be((lb, lb));

        var (antes, _) = historico.Desfazer(c);
        (antes.LinhasDeBase.Count, antes.LinhaDeBaseEmUso).Should().Be((0, null));
    }

    [Fact]
    public void Gantt_BarraDaLinhaDeBase_EPrazo()
    {
        var (c, a, b) = Obra();
        c.SalvarLinhaDeBase(null, Agora);
        a.AlterarDuracao(5);
        b.DefinirPrazo(D(2));
        var resultado = CalculadoraCronograma.Calcular(c);

        var layout = CalculadoraLayoutGantt.Calcular(c, resultado, new OpcoesGantt(10));
        var barraB = layout.Barras.Single(x => x.TarefaId == b.Id);

        var inicioEixo = Seg.AddDays(-CalculadoraLayoutGantt.DiasAntesDoInicio);
        (barraB.XLinhaDeBase, barraB.LarguraLinhaDeBase).Should().Be(((D(1) - inicioEixo).Days * 10.0, 20.0));
        barraB.X.Should().BeGreaterThan(barraB.XLinhaDeBase!.Value, "atrasou em relação à linha de base");
        (barraB.XDoPrazo, barraB.ForaDoPrazo).Should().Be(((D(2) - inicioEixo).Days * 10.0 + 10, true));
        layout.Barras.Single(x => x.TarefaId == a.Id).XDoPrazo.Should().BeNull();
    }

    [Fact]
    public void Gantt_SemLinhaDeBase_SemBarra_EOEixoVaiAteOFimDaLinhaDeBase()
    {
        var (c, a, _) = Obra();
        var semLinha = CalculadoraLayoutGantt.Calcular(c, CalculadoraCronograma.Calcular(c), new OpcoesGantt(10));
        semLinha.Barras.Should().OnlyContain(x => x.XLinhaDeBase == null);

        a.AlterarDuracao(20);
        CalculadoraCronograma.Calcular(c);
        c.SalvarLinhaDeBase(null, Agora);
        a.AlterarDuracao(1);
        var layout = CalculadoraLayoutGantt.Calcular(c, CalculadoraCronograma.Calcular(c), new OpcoesGantt(10));

        layout.FimEixo.Should().BeOnOrAfter(c.LinhaDeBaseEmUso!.Termino!.Value);
    }

    [Fact]
    public void Resumo_ForaDoPrazoEDesvioDoTermino()
    {
        var (c, a, b) = Obra();
        c.SalvarLinhaDeBase(null, Agora);
        a.AlterarDuracao(5);
        b.DefinirPrazo(D(2));
        CalculadoraCronograma.Calcular(c);

        var resumo = ResumoDoProjeto.Calcular(c, AcompanhamentoDoCronograma.Calcular(c, Seg), Seg);

        resumo.ForaDoPrazo.Select(t => (t.Edt, t.Nome, t.Data)).Should().Equal(("2", "B", D(2)));
        (resumo.TerminoDaLinhaDeBase, resumo.DesvioDoTermino).Should().Be((D(2), 2));
        var semLinha = ResumoDoProjeto.Calcular(new Cronograma("x", Seg), new Dictionary<Guid, AndamentoDaTarefa>(), Seg);
        (semLinha.TerminoDaLinhaDeBase, semLinha.DesvioDoTermino, semLinha.ForaDoPrazo.Count).Should().Be((null, null, 0));
    }

    [Fact]
    public void Resumo_ValorEASomaDasSubtarefas()
    {
        var c = new Cronograma("Obra", Seg);
        var fase = new Tarefa("Fase", Seg, 1);
        c.AdicionarTarefa(fase);
        var x = new Tarefa("X", Seg, 2);
        var y = new Tarefa("Y", Seg, 2);
        c.AdicionarTarefa(x, fase.Id);
        c.AdicionarTarefa(y, fase.Id);
        x.DefinirValor(1500.10m);
        y.DefinirValor(499.90m);

        CalculadoraCronograma.Calcular(c);

        fase.Valor.Should().Be(2000m);
        ((Action)(() => fase.DefinirValor(1))).Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void CopiarEColar_LevamRestricaoPrazoEValor()
    {
        var (c, a, _) = Obra();
        a.DefinirRestricaoDeInicio(D(5));
        a.DefinirPrazo(D(9));

        var nova = CopiaDeTarefas.Colar(c, CopiaDeTarefas.Copiar(c, [a.Id]), null, 2).Single();

        (nova.NaoIniciarAntesDe, nova.Prazo, nova.Valor).Should().Be((D(5), D(9), 3000m));
    }

    // ---- Moeda ----

    [Theory]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("R$ 1.234,56", 1234.56)]
    [InlineData("1234,5", 1234.5)]
    [InlineData("1234.5", 1234.5)]
    [InlineData("1.234", 1234)]
    [InlineData("1.234.567,89", 1234567.89)]
    [InlineData("  250 ", 250)]
    [InlineData("0,99", 0.99)]
    public void Moeda_Ler(string texto, double esperado)
    {
        Moeda.Ler(texto).Should().Be((decimal)esperado);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1,2,3")]
    [InlineData("12.34.5")]
    public void Moeda_LerInvalido_Recusa(string texto)
    {
        ((Action)(() => Moeda.Ler(texto))).Should().Throw<FormatException>();
    }

    [Fact]
    public void Moeda_VazioENulo_EFormatar()
    {
        (Moeda.Ler(""), Moeda.Ler(null), Moeda.Ler(" R$ ")).Should().Be((null, null, null));
        (Moeda.Formatar(1234567.8m), Moeda.ComSimbolo(0.5m)).Should().Be(("1.234.567,80", "R$ 0,50"));
    }
}
