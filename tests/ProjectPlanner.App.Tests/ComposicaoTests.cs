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
        // Com um cofre em memória: o de verdade (Chaves do macOS) pediria a senha ao executor dos testes e o seguraria.
        using var servicos = Servicos.Configurar(new CofreEmMemoria());

        servicos.GetRequiredService<MainWindowViewModel>().Should().NotBeNull();
        servicos.GetRequiredService<IProjetoRepository>().Should().BeOfType<SqliteProjetoRepository>();
        servicos.GetRequiredService<ICalculadoraCronograma>().Should().BeOfType<CalculadoraCronogramaServico>();
        servicos.GetRequiredService<IDialogosService>().Should().BeOfType<DialogosAvalonia>();
        servicos.GetRequiredService<Services.Cofre.ICofreDeSegredos>().Should().BeOfType<CofreEmMemoria>();
    }
}
