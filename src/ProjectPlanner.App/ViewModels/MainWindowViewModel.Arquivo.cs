using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;

namespace ProjectPlanner.App.ViewModels;

// Bloco 70 (saiu de MainWindowViewModel.cs): novo, abrir, salvar e salvar como, e a pergunta sobre as alterações pendentes.
public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task NovoProjeto()
    {
        if (await PodeDescartarAlteracoesAsync())
            Carregar(NovoCronograma(), caminho: null);
    }

    [RelayCommand]
    private async Task AbrirProjeto()
    {
        if (!await PodeDescartarAlteracoesAsync())
            return;
        var caminho = await _dialogos.EscolherArquivoParaAbrirAsync();
        if (caminho is not null)
            AbrirArquivo(caminho);
    }

    /// <summary>
    /// Abre um projeto pedido pelo sistema: duplo clique ou "Abrir com" num arquivo .pplan, argumentos da
    /// linha de comando, ou arquivos entregues pelo Finder no macOS (também com o programa já aberto).
    /// Vale o primeiro projeto (ver <see cref="ArquivosPedidos"/>), com a mesma pergunta sobre alterações
    /// pendentes e as mesmas mensagens de erro do "Abrir…"; os outros não são abertos (um projeto por
    /// janela), e a barra de status avisa. Sem nenhum projeto nos argumentos, não faz nada.
    /// </summary>
    public async Task AbrirPedidoDoSistemaAsync(IEnumerable<string?> arquivos)
    {
        var pedido = ArquivosPedidos.Interpretar(arquivos);
        if (pedido.Caminho is null || !await PodeDescartarAlteracoesAsync())
            return;

        AbrirArquivo(pedido.Caminho);
        if (pedido.NaoAbertos > 0)
        {
            var quais = pedido.NaoAbertos == 1
                ? "1 outro arquivo não foi aberto"
                : $"{pedido.NaoAbertos} outros arquivos não foram abertos";
            // O aviso vem antes, para não ser cortado pela barra de status depois de um caminho longo.
            Mensagem = $"O ProjectPlanner abre um projeto por janela: {quais}. {Mensagem}";
        }
    }

    private void AbrirArquivo(string caminho)
    {
        try
        {
            var aberto = _repositorio.Abrir(caminho);
            Carregar(aberto, caminho);
            _caminhoNaMensagem = caminho;
            Mensagem = $"Projeto aberto: {caminho}";
            if (Core.OrdensDeServico.TextosDaOrdem.Aviso(aberto.OrdensDeServico.Todas, DateOnly.FromDateTime(_relogio.GetLocalNow().DateTime)) is { } avisoDasOrdens)
                Mensagem += " " + avisoDasOrdens; // Bloco 61
            _ = AtualizarTelegramAsync(); // Bloco 49
            if (aberto.ConversaDescartadaAoAbrir)
                Mensagem += " A conversa com o assistente guardada no arquivo não pôde ser lida e ficou de fora.";
            if (aberto.FotosDescartadasAoAbrir > 0)
                Mensagem += aberto.FotosDescartadasAoAbrir == 1
                    ? " 1 registro de exemplo de foto (sem imagem) foi descartado; ele some do arquivo ao salvar."
                    : $" {aberto.FotosDescartadasAoAbrir} registros de exemplo de fotos (sem imagem) foram descartados; eles somem do arquivo ao salvar.";
        }
        catch (Exception ex)
        {
            // Qualquer falha de arquivo vira mensagem; o projeto atual continua aberto.
            Mensagem = $"Não foi possível abrir o projeto. {MensagensDeErro.Descrever(ex)}";
        }
    }

    [RelayCommand]
    private Task SalvarProjeto() => SalvarAsync();

    [RelayCommand]
    private Task SalvarComoProjeto() => SalvarAsync(perguntarOndeSalvar: true);

    /// <summary>
    /// Salva no arquivo atual. Pergunta onde salvar quando o projeto nunca foi salvo ou quando
    /// <paramref name="perguntarOndeSalvar"/> é verdadeiro ("Salvar como"); o arquivo escolhido
    /// passa a ser o do projeto.
    /// </summary>
    /// <returns>Se o projeto foi salvo.</returns>
    public async Task<bool> SalvarAsync(bool perguntarOndeSalvar = false)
    {
        var caminho = perguntarOndeSalvar || CaminhoArquivo is null
            ? await _dialogos.EscolherArquivoParaSalvarAsync(NomeParaExibir)
            : CaminhoArquivo;
        if (caminho is null)
            return false;

        // Bloco 30: as imagens que o Desfazer pode trazer de volta e que só estão no arquivo de origem vão para a memória
        // antes de ele ser substituído.
        var fotosAbertas = _cronograma.Tarefas.SelectMany(t => t.FotosNaOrdemDeCadastro).Select(f => f.Id).ToHashSet();
        _fotos.Preservar(_historico.FotosNoHistorico().Where(id => !fotosAbertas.Contains(id)));
        try
        {
            _repositorio.Salvar(_cronograma, caminho, _fotos.ParaSalvar());
        }
        catch (Exception ex)
        {
            // Sem permissão, disco cheio, arquivo em uso...: vira mensagem, e as alterações continuam pendentes.
            Mensagem = $"Não foi possível salvar o projeto. {MensagensDeErro.Descrever(ex)}";
            return false;
        }

        _fotos.Salvou(caminho, fotosAbertas);
        _historico.MarcarSalvo();
        _alteracoesForaDoHistorico = false;
        CaminhoArquivo = caminho;
        PossuiAlteracoesNaoSalvas = false;
        _caminhoNaMensagem = caminho;
        Mensagem = $"Projeto salvo em {caminho}";
        await AtualizarRetratoNoTelegramAsync(); // Bloco 49
        return true;
    }

    /// <summary>
    /// Se houver alterações não salvas, pergunta o que fazer com elas (Salvar, Não salvar ou Cancelar).
    /// Usado antes de Novo, Abrir e ao fechar a janela.
    /// </summary>
    /// <returns>Se pode seguir e descartar o projeto atual.</returns>
    public async Task<bool> PodeDescartarAlteracoesAsync()
    {
        if (!PossuiAlteracoesNaoSalvas)
            return true;

        return await _dialogos.PerguntarSobreAlteracoesPendentesAsync(NomeParaExibir) switch
        {
            RespostaAlteracoesPendentes.Salvar => await SalvarAsync(),
            RespostaAlteracoesPendentes.NaoSalvar => true,
            _ => false,
        };
    }
}
