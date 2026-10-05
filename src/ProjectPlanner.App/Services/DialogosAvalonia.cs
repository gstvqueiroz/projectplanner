using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using ProjectPlanner.App.Views;

namespace ProjectPlanner.App.Services;

/// <summary>
/// Diálogos nativos do sistema (no macOS, os painéis Abrir/Salvar do Finder) pelo StorageProvider do
/// Avalonia, e as perguntas (alterações pendentes, confirmações) em janelas modais próprias.
/// </summary>
public sealed class DialogosAvalonia : IDialogosService
{
    public const string Extensao = "pplan";

    private static readonly FilePickerFileType ArquivoDeProjeto = new("Projeto do ProjectPlanner")
    {
        Patterns = [$"*.{Extensao}"],
    };

    // Os formatos que o assistente lê (Bloco 23): PDF, imagens e texto.
    private static readonly FilePickerFileType Documentos = new($"Documentos ({IA.AnexosAceitos.Descricao})")
    {
        Patterns = [.. IA.AnexosAceitos.Padroes, .. IA.AnexosAceitos.Padroes.Select(p => p.ToUpperInvariant())],
    };

    private static readonly FilePickerFileType Fotos = new("Fotos (JPG, PNG, WebP)")
    {
        Patterns = ["*.jpg", "*.jpeg", "*.png", "*.webp", "*.JPG", "*.JPEG", "*.PNG", "*.WEBP"],
    };

    // Dono dos diálogos: a janela ativa (a galeria, quando ela está aberta), para eles aparecerem por cima dela.
    private static Window JanelaAtiva =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows.LastOrDefault(w => w.IsActive)
        ?? JanelaPrincipal;

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

    public async Task<IReadOnlyList<string>> EscolherDocumentosAsync()
    {
        var arquivos = await JanelaPrincipal.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Anexar documentos",
            AllowMultiple = true,
            FileTypeFilter = [Documentos, FilePickerFileTypes.All],
        });
        return arquivos.Select(a => a.TryGetLocalPath()).OfType<string>().ToList();
    }

    public async Task<IReadOnlyList<string>> EscolherFotosAsync()
    {
        var arquivos = await JanelaAtiva.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Adicionar fotos",
            AllowMultiple = true,
            FileTypeFilter = [Fotos, FilePickerFileTypes.All],
        });
        return arquivos.Select(a => a.TryGetLocalPath()).OfType<string>().ToList();
    }

    public Task MostrarFotoAmpliadaAsync(ViewModels.VisualizadorDeFotosViewModel visualizador) =>
        new VisualizadorFotosWindow(visualizador).ShowDialog(JanelaAtiva);

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

    public Task<bool> ConfirmarAsync(string titulo, string mensagem, string textoDoBotao) =>
        new ConfirmacaoWindow(titulo, mensagem, textoDoBotao).ShowDialog<bool>(JanelaAtiva);

    public Task MostrarFotosAsync(ViewModels.GaleriaFotosViewModel galeria) =>
        new GaleriaFotosWindow(galeria).ShowDialog(JanelaPrincipal);

    public Task MostrarConfiguracaoAssistenteAsync(ViewModels.ConfiguracaoAssistenteViewModel configuracao) =>
        new ConfiguracaoAssistenteWindow(configuracao).ShowDialog(JanelaPrincipal);

    public Task MostrarSobreAsync(ViewModels.SobreViewModel sobre) => new SobreWindow(sobre).ShowDialog(JanelaPrincipal);

    public Task MostrarResumoDoProjetoAsync(ViewModels.ResumoDoProjetoViewModel resumo) =>
        new ResumoDoProjetoWindow(resumo).ShowDialog(JanelaPrincipal);

    public async Task AbrirPastaAsync(string pasta)
    {
        try
        {
            Directory.CreateDirectory(pasta);
            await JanelaPrincipal.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(pasta));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sem a pasta, não há o que mostrar.
        }
    }

    public async Task<string?> EscolherArquivoCsvAsync(string nomeSugerido)
    {
        var arquivo = await JanelaPrincipal.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar para CSV",
            SuggestedFileName = nomeSugerido,
            DefaultExtension = "csv",
            FileTypeChoices = [new FilePickerFileType("Planilha CSV (Excel)") { Patterns = ["*.csv"] }],
            ShowOverwritePrompt = true,
        });
        return arquivo?.TryGetLocalPath();
    }

    public Task<string?> PedirTextoAsync(string titulo, string pergunta, string? valorInicial = null) =>
        new EntradaDeTextoWindow(titulo, pergunta, valorInicial).ShowDialog<string?>(JanelaAtiva);
}
