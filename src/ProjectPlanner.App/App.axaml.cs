using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.App.Views;

namespace ProjectPlanner.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var servicos = Servicos.Configurar();
            desktop.MainWindow = new MainWindow
            {
                DataContext = servicos.GetRequiredService<MainWindowViewModel>(),
            };
            desktop.Exit += (_, _) => servicos.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
