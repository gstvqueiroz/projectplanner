using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ProjectPlanner.App.Views;

/// <summary>Bloco 72: pergunta e devolve a resposta escolhida (o índice), ou nulo em Cancelar e Esc.</summary>
public partial class EscolhaWindow : Window
{
    // Exigido pelo carregador de XAML e pelo designer.
    public EscolhaWindow()
    {
        InitializeComponent();
    }

    public EscolhaWindow(string titulo, string mensagem, IReadOnlyList<string> opcoes) : this()
    {
        Title = titulo;
        Mensagem.Text = mensagem;
        for (var i = 0; i < opcoes.Count; i++)
        {
            var indice = i;
            var botao = new Button { Content = opcoes[i], IsDefault = i == 0 };
            if (i == 0)
                botao.Classes.Add("accent");
            botao.Click += (_, _) => Close(indice);
            Opcoes.Children.Insert(Opcoes.Children.Count - 1, botao);
        }
    }

    private void Cancelar(object? sender, RoutedEventArgs e) => Close(null);
}
