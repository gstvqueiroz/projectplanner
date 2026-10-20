using Avalonia.Controls;
using Avalonia.Interactivity;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Views;

public partial class TelegramWindow : Window
{
    public TelegramWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            if (DataContext is TelegramViewModel telegram)
                await telegram.AbrirAsync();
        };
    }

    public TelegramWindow(TelegramViewModel telegram) : this() => DataContext = telegram;

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
