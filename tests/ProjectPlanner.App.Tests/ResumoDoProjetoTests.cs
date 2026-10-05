using Avalonia.Controls;
using Avalonia.Threading;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.App.Views;

namespace ProjectPlanner.App.Tests;

// Bloco 25 (C7): Exibir > Resumo do projeto ("hoje" é seg 28/09/2026).
public class ResumoDoProjetoTests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;

    public ResumoDoProjetoTests() => _vm = _ambiente.CriarViewModel();

    // Começou uma semana antes: A (5 d, 100%), B (10 d, 0%, pred A, já devia estar em 0% hoje), Entrega (marco, pred B).
    private void Montar()
    {
        _vm.InicioProjeto = new DateTime(2026, 9, 21);
        _vm.NomeProjeto = "Residência";
        Ambiente.AdicionarTarefa(_vm, "A", 5).PercentualConcluido = 100;
        Ambiente.AdicionarTarefa(_vm, "B", 10, "1");
        Ambiente.AdicionarTarefa(_vm, "Entrega", 0, "2");
    }

    [Fact]
    public void Comando_AbreOResumoDeHoje()
    {
        Montar();

        _vm.ResumoDoProjetoCommand.Execute(null);

        var resumo = _ambiente.Dialogos.Resumos.Single();
        (resumo.Titulo, resumo.Situacao, resumo.Periodo).Should().Be(
            ("Resumo do projeto — Residência", "Situação em 28/09/2026", "Início 21/09/2026 · Término 09/10/2026 · 15 dias úteis"));
        (resumo.PercentualConcluido, resumo.Andamento, resumo.AbaixoDoPrevisto).Should().Be((33, "33% concluído · previsto para hoje: 33%", false));
        resumo.Contagens.Select(c => (c.Nome, c.Quantidade)).Should().Equal(
            ("Tarefas", 2), ("Resumos", 0), ("Marcos", 1), ("Concluídas", 1), ("Em dia", 0), ("Não iniciadas", 2),
            ("Atrasadas", 0), ("Críticas em aberto", 2), ("Fotos", 0));
        resumo.TemAtrasadas.Should().BeFalse();
        resumo.ProximosMarcos.Should().Equal("3 Entrega — 09/10/2026");
    }

    [Fact]
    public void Atrasada_EmDestaque_EAbaixoDoPrevisto()
    {
        Montar();
        _vm.InicioProjeto = new DateTime(2026, 9, 14); // B devia estar em 50% hoje

        _vm.ResumoDoProjetoCommand.Execute(null);

        var resumo = _ambiente.Dialogos.Resumos.Single();
        resumo.Atrasadas.Should().Equal("2 B — 02/10/2026 · 0% (previsto 50%)");
        resumo.Contagens.Single(c => c.Nome == "Atrasadas").Should().Be(new ContagemNoResumo("Atrasadas", 1, Destaque: true));
        resumo.AbaixoDoPrevisto.Should().BeTrue();
    }

    [Fact]
    public void ProjetoVazio_SemMarcos()
    {
        _vm.ResumoDoProjetoCommand.Execute(null);

        var resumo = _ambiente.Dialogos.Resumos.Single();
        resumo.Periodo.Should().EndWith("o projeto ainda não tem tarefas");
        (resumo.TemProximosMarcos, resumo.SemProximosMarcos).Should().Be((false, "Nenhum marco nos próximos 30 dias."));
        var nulo = () => new ResumoDoProjetoViewModel(null!, DateTime.Today);
        nulo.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public Task Janela_MostraOResumo() => Tela.Executar(() =>
    {
        Montar();
        _vm.ResumoDoProjetoCommand.Execute(null);
        var janela = new ResumoDoProjetoWindow(_ambiente.Dialogos.Resumos.Single());
        janela.Show();
        Dispatcher.UIThread.RunJobs();

        (janela.FindControl<ProgressBar>("Barra")!.Value, janela.FindControl<ItemsControl>("Contagens")!.ItemCount).Should().Be((33d, 9));
        janela.FindControl<TextBlock>("Andamento")!.Text.Should().Be("33% concluído · previsto para hoje: 33%");
        janela.FindControl<ItemsControl>("ProximosMarcos")!.ItemCount.Should().Be(1);
        janela.Close();
    });
}
