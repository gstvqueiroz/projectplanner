namespace ProjectPlanner.App.Services;

/// <summary>Diálogos que os ViewModels precisam, sem depender de janelas do Avalonia.</summary>
public interface IDialogosService
{
    /// <returns>O caminho escolhido, ou <c>null</c> se o usuário cancelar.</returns>
    Task<string?> EscolherArquivoParaAbrirAsync();

    /// <returns>O caminho escolhido, ou <c>null</c> se o usuário cancelar.</returns>
    Task<string?> EscolherArquivoParaSalvarAsync(string nomeSugerido);

    Task<RespostaAlteracoesPendentes> PerguntarSobreAlteracoesPendentesAsync(string nomeProjeto);

    /// <summary>Pede confirmação antes de uma ação que não dá para desfazer.</summary>
    /// <returns>Se o usuário confirmou (fechar o diálogo vale como não).</returns>
    Task<bool> ConfirmarAsync(string titulo, string mensagem, string textoDoBotao);
}

public enum RespostaAlteracoesPendentes
{
    // Cancelar vem primeiro: fechar o diálogo pelo botão da janela devolve o valor padrão.
    Cancelar = 0,
    Salvar,
    NaoSalvar,
}
