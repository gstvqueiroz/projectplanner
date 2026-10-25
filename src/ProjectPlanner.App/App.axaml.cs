using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
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
            var janelaPrincipal = servicos.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = janelaPrincipal };
            desktop.Exit += (_, _) => servicos.Dispose();
            // Bloco 69: os erros inesperados também vão ao suporte, se o usuário ligou (Conta > Minha conta); e o aviso de versão nova.
            if (Program.Tratamento is { } tratamento)
                tratamento.Enviar = servicos.GetRequiredService<EnvioDeErros>().EnviarAsync;
            _ = janelaPrincipal.VerificarVersaoAsync();

            // Duplo clique num .pplan: no Windows (e na linha de comando) o arquivo chega nos argumentos;
            // no macOS, o Finder o entrega pelo evento de ativação, na abertura e com o programa já aberto.
            _ = janelaPrincipal.AbrirPedidoDoSistemaAsync(desktop.Args ?? []);
            if (this.TryGetFeature<IActivatableLifetime>() is { } ativacao)
                ativacao.Activated += (_, e) =>
                {
                    if (e is FileActivatedEventArgs arquivos)
                        _ = janelaPrincipal.AbrirPedidoDoSistemaAsync(arquivos.Files.Select(f => f.TryGetLocalPath()));
                };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
