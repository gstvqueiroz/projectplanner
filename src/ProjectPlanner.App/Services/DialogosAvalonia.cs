using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using ProjectPlanner.App.Views;

namespace ProjectPlanner.App.Services;

/// <summary>
/// Diálogos nativos do sistema (no macOS, os painéis Abrir/Salvar do Finder) pelo StorageProvider do
/// Avalonia, e a pergunta de alterações pendentes numa janela modal própria.
/// </summary>
public sealed class DialogosAvalonia : IDialogosService
{
    public const string Extensao = "pplan";

    private static readonly FilePickerFileType ArquivoDeProjeto = new("Projeto do ProjectPlanner")
    {
        Patterns = [$"*.{Extensao}"],
    };

    private static Window JanelaPrincipal =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow
        ?? throw new InvalidOperationException("A janela principal ainda não foi criada.");

    public async Task<string?> EscolherArquivoParaAbrirAsync()
    {
        var arquivos = await JanelaPrincipal.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir projeto",
            AllowMultiple = false,
            FileTypeFilter = [ArquivoDeProjeto],
        });
        return arquivos.Count > 0 ? arquivos[0].TryGetLocalPath() : null;
    }

    public async Task<string?> EscolherArquivoParaSalvarAsync(string nomeSugerido)
    {
        var arquivo = await JanelaPrincipal.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Salvar projeto",
            SuggestedFileName = $"{nomeSugerido}.{Extensao}",
            DefaultExtension = Extensao,
            FileTypeChoices = [ArquivoDeProjeto],
            ShowOverwritePrompt = true,
        });
        return arquivo?.TryGetLocalPath();
    }

    public Task<RespostaAlteracoesPendentes> PerguntarSobreAlteracoesPendentesAsync(string nomeProjeto) =>
        new AlteracoesPendentesWindow(nomeProjeto).ShowDialog<RespostaAlteracoesPendentes>(JanelaPrincipal);
}
