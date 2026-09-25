using Microsoft.Extensions.DependencyInjection;
using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core;
using ProjectPlanner.Data;

namespace ProjectPlanner.App.Tests;

public class ComposicaoTests
{
    [Fact]
    public void Servicos_MontamAJanelaPrincipalComAsImplementacoesReais()
    {
        using var servicos = Servicos.Configurar();

        servicos.GetRequiredService<MainWindowViewModel>().Should().NotBeNull();
        servicos.GetRequiredService<IProjetoRepository>().Should().BeOfType<SqliteProjetoRepository>();
        servicos.GetRequiredService<ICalculadoraCronograma>().Should().BeOfType<CalculadoraCronogramaServico>();
        servicos.GetRequiredService<IDialogosService>().Should().BeOfType<DialogosAvalonia>();
    }
}
