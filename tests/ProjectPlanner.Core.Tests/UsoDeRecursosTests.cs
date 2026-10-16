using ProjectPlanner.Core.Planejamento;
using ProjectPlanner.Core.Recursos;

namespace ProjectPlanner.Core.Tests;

// Bloco 34: uso de recursos no tempo, superalocações, necessidades por período e nivelamento sugerido. Projeto na seg
// 28/09/2026: A (5 d: 28/09–02/10) e B (3 d: 28/09–30/09), em paralelo; C (2 d) depois de A. Pedreiro: 2 por dia.
public class UsoDeRecursosTests
{
    private static readonly DateTime Seg = new(2026, 9, 28);

    private static DateOnly D(int dia, int mes = 10) => new(2026, mes, dia);

    private static readonly Recurso Pedreiro = new(Guid.NewGuid(), "Pedreiro", TipoDeRecurso.Profissional, null, 300, 2);
    private static readonly Recurso Cimento = new(Guid.NewGuid(), "Cimento", TipoDeRecurso.Material, "sc", 40);

    private static (Cronograma C, Tarefa A, Tarefa B, Tarefa Ct) Obra()
    {
        var c = new Cronograma("Casa", Seg);
        var a = new Tarefa("A", Seg, 5);
        var b = new Tarefa("B", Seg, 3);
        var ct = new Tarefa("C", Seg, 2);
        c.AdicionarTarefa(a);
        c.AdicionarTarefa(b);
        c.AdicionarTarefa(ct);
        c.AdicionarPredecessora(new Predecessora(a.Id, ct.Id));
        c.Recursos.Adicionar(Pedreiro);
        c.Recursos.Adicionar(Cimento);
        c.Recursos.Atribuir(a, [new(Pedreiro.Id, 2), new(Cimento.Id, 100)]);
        c.Recursos.Atribuir(b, [new(Pedreiro.Id, 1)]);
        c.Recursos.Atribuir(ct, [new(Pedreiro.Id, 1)]);
        CalculadoraCronograma.Calcular(c);
        return (c, a, b, ct);
    }

    [Fact]
    public void Uso_PorDiaEMaterialDistribuido()
    {
        var (c, a, b, _) = Obra();

        var usos = UsoDeRecursos.Calcular(c);

        var pedreiro = usos.Single(u => u.Recurso == Pedreiro);
        pedreiro.Dias.Select(d => (d.Dia, d.Quantidade)).Should().Equal(
            (D(28, 9), 3m), (D(29, 9), 3m), (D(30, 9), 3m), (D(1), 2m), (D(2), 2m), (D(5), 1m), (D(6), 1m));
        pedreiro.Dias[0].Tarefas.Should().Equal(a.Id, b.Id);
        (pedreiro.Pico, pedreiro.AcimaDoLimite.Count).Should().Be((3m, 3));
        usos.Single(u => u.Recurso == Cimento).Dias.Should().HaveCount(5).And.OnlyContain(d => d.Quantidade == 20, "100 sc em 5 dias");
    }

    [Fact]
    public void Uso_SemCalculo_Recusa_EMarcoLevaOMaterialNoDia()
    {
        var c = new Cronograma("P", Seg);
        var marco = new Tarefa("Entrega", Seg, 0);
        c.AdicionarTarefa(marco);
        ((Action)(() => UsoDeRecursos.Calcular(c))).Should().Throw<DomainValidationException>().WithMessage("Calcule o cronograma antes do uso de recursos.");
        c.Recursos.Adicionar(Cimento);
        c.Recursos.Adicionar(Pedreiro);
        c.Recursos.Atribuir(marco, [new(Cimento.Id, 10), new(Pedreiro.Id, 1)]);
        CalculadoraCronograma.Calcular(c);

        var usos = UsoDeRecursos.Calcular(c);

        var dia = usos.Single(u => u.Recurso == Cimento).Dias.Single();
        (dia.Dia, dia.Quantidade, dia.Tarefas.Single()).Should().Be((D(28, 9), 10m, marco.Id));
        usos.Single(u => u.Recurso == Pedreiro).Dias.Should().BeEmpty("um marco não tem dias de trabalho");
    }

    [Fact]
    public void Superalocacoes_TrechosSeguidos()
    {
        var (c, _, _, _) = Obra();

        var lista = UsoDeRecursos.Superalocacoes(c);

        lista.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Recurso = Pedreiro, De = D(28, 9), Ate = D(30, 9), Dias = 3, Pico = 3m, Limite = 2m, Tarefas = new[] { "1 A", "2 B" },
        });
    }

    [Fact]
    public void Necessidades_PorPeriodo_ComCusto()
    {
        var (c, _, _, _) = Obra();

        var lista = UsoDeRecursos.Necessidades(c, PeriodosFinanceiros.Gerar(D(28, 9), D(6), Periodicidade.Semanal));

        lista.Select(n => (n.Periodo.Numero, n.Recurso.Nome, n.Quantidade, n.Custo)).Should().Equal(
            (1, "Pedreiro", 13m, 3900m), (1, "Cimento", 100m, 4000m), (2, "Pedreiro", 2m, 600m));
    }

    [Fact]
    public void Nivelamento_AdiaQuemComecaPorUltimo_SemMudarAteAplicar()
    {
        var (c, _, b, ct) = Obra();

        var sugestao = UsoDeRecursos.Sugerir(c);

        // A ocupa os 2 pedreiros até 02/10: B (1 por dia) só cabe a partir de 05/10, junto com C.
        sugestao.Mudancas.Select(m => (m.Nome, m.InicioAtual, m.InicioNovo, m.Dias, m.Direta)).Should().Equal(("B", D(28, 9), D(5), 5, true));
        (sugestao.TerminoAtual, sugestao.TerminoNovo, sugestao.NaoResolvidos.Count).Should().Be((D(6), D(7), 0));
        b.NaoIniciarAntesDe.Should().BeNull("sugerir não muda nada");

        UsoDeRecursos.Aplicar(c, sugestao).Should().Be(1);
        CalculadoraCronograma.Calcular(c);
        (b.NaoIniciarAntesDe, UsoDeRecursos.Superalocacoes(c).Count).Should().Be((new DateTime(2026, 10, 5), 0));
        _ = ct;
    }

    [Fact]
    public void Nivelamento_TarefaIniciadaNaoMuda_EAQuePedeMaisQueOLimiteFicaNoAviso()
    {
        var (c, a, b, _) = Obra();
        b.AtualizarPercentualConcluido(10);
        c.Recursos.Atribuir(a, [new(Pedreiro.Id, 3)]); // sozinha passa do disponível (2)
        CalculadoraCronograma.Calcular(c);

        var sugestao = UsoDeRecursos.Sugerir(c);

        sugestao.Mudancas.Should().BeEmpty();
        sugestao.NaoResolvidos.Should().Equal("Pedreiro em 28/09/2026: as tarefas já começaram ou pedem sozinhas mais do que o disponível");
        sugestao.NadaAMudar.Should().BeTrue();
    }
}
