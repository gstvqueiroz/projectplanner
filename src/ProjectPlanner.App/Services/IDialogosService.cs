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

    /// <summary>Escolhe documentos para anexar no painel do assistente (Bloco 19).</summary>
    /// <returns>Os caminhos escolhidos (vazio se o usuário cancelar).</returns>
    Task<IReadOnlyList<string>> EscolherDocumentosAsync();

    /// <summary>Escolhe fotos para adicionar a uma tarefa (Bloco 22), várias de uma vez.</summary>
    /// <returns>Os caminhos escolhidos (vazio se o usuário cancelar).</returns>
    Task<IReadOnlyList<string>> EscolherFotosAsync();

    /// <summary>Mostra uma foto ampliada, com anterior/próxima, e espera ser fechado.</summary>
    Task MostrarFotoAmpliadaAsync(ViewModels.VisualizadorDeFotosViewModel visualizador);

    /// <summary>Mostra a galeria de fotos de uma tarefa e espera ser fechada.</summary>
    Task MostrarFotosAsync(ViewModels.GaleriaFotosViewModel galeria);

    /// <summary>Mostra as configurações do assistente (chave e modelo, Bloco 23) e espera ser fechada.</summary>
    Task MostrarConfiguracaoAssistenteAsync(ViewModels.ConfiguracaoAssistenteViewModel configuracao);

    /// <summary>Abre a pasta no Finder ou no Explorador de Arquivos (Bloco 25), criando-a se ainda não existe.</summary>
    Task AbrirPastaAsync(string pasta);

    /// <summary>Escolhe onde gravar o CSV exportado (Bloco 25).</summary>
    /// <returns>O caminho escolhido, ou nulo se o usuário cancelar.</returns>
    Task<string?> EscolherArquivoCsvAsync(string nomeSugerido);

    /// <summary>Pede um texto curto (Bloco 25: o Nº da tarefa em Ir para a tarefa).</summary>
    /// <returns>O texto digitado, ou nulo se o usuário cancelar.</returns>
    Task<string?> PedirTextoAsync(string titulo, string pergunta, string? valorInicial = null);

    /// <summary>Mostra Exibir > Resumo do projeto (Bloco 25) e espera ser fechada.</summary>
    Task MostrarResumoDoProjetoAsync(ViewModels.ResumoDoProjetoViewModel resumo);

    /// <summary>Mostra Ajuda > Sobre o ProjectPlanner (Bloco 25) e espera ser fechada.</summary>
    Task MostrarSobreAsync(ViewModels.SobreViewModel sobre);

    /// <summary>Mostra Arquivo > Relatório fotográfico (Bloco 27) e espera ser fechada.</summary>
    Task MostrarRelatorioFotograficoAsync(ViewModels.RelatorioFotograficoViewModel relatorio);

    /// <summary>Onde salvar o relatório (Bloco 27), com a extensão "pdf" ou "docx"; nulo se cancelar.</summary>
    Task<string?> EscolherArquivoDoRelatorioAsync(string nomeSugerido, string extensao);

    /// <summary>Uma imagem PNG ou JPG (o logotipo do relatório, Bloco 27); nulo se cancelar.</summary>
    Task<string?> EscolherImagemAsync();

    /// <summary>Abre o arquivo no programa padrão do sistema (o relatório gerado, Bloco 27).</summary>
    Task AbrirArquivoAsync(string caminho);
}

public enum RespostaAlteracoesPendentes
{
    // Cancelar vem primeiro: fechar o diálogo pelo botão da janela devolve o valor padrão.
    Cancelar = 0,
    Salvar,
    NaoSalvar,
}
