namespace ProjectPlanner.App.Services;

/// <summary>Diálogos que os ViewModels precisam, sem depender de janelas do Avalonia.</summary>
public interface IDialogosService
{
    /// <returns>O caminho escolhido, ou <c>null</c> se o usuário cancelar.</returns>
    Task<string?> EscolherArquivoParaAbrirAsync();

    /// <returns>O caminho escolhido, ou <c>null</c> se o usuário cancelar.</returns>
    Task<string?> EscolherArquivoParaSalvarAsync(string nomeSugerido);

    Task<RespostaAlteracoesPendentes> PerguntarSobreAlteracoesPendentesAsync(string nomeProjeto);
}

public enum RespostaAlteracoesPendentes
{
    // Cancelar vem primeiro: fechar o diálogo pelo botão da janela devolve o valor padrão.
    Cancelar = 0,
    Salvar,
    NaoSalvar,
}
