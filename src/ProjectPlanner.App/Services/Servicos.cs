using Microsoft.Extensions.DependencyInjection;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core;
using ProjectPlanner.Data;

namespace ProjectPlanner.App.Services;

/// <summary>Composição da aplicação: quem implementa cada dependência dos ViewModels.</summary>
public static class Servicos
{
    public static ServiceProvider Configurar() => new ServiceCollection()
        .AddSingleton(TimeProvider.System)
        .AddSingleton<IProjetoRepository>(s => new SqliteProjetoRepository(s.GetRequiredService<TimeProvider>()))
        .AddSingleton<ICalculadoraCronograma, CalculadoraCronogramaServico>()
        .AddSingleton<IDialogosService, DialogosAvalonia>()
        .AddTransient<MainWindowViewModel>()
        .BuildServiceProvider();
}
