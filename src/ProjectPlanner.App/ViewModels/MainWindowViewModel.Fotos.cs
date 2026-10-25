using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.Core.Fotos;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.ViewModels;

// Bloco 28: Exibir > Fotos do projeto… e o alerta de tarefa em andamento sem foto recente na grid.
public sealed partial class MainWindowViewModel
{
    // Bloco 29: a IA que olha as fotos (legendas, % pelas fotos, texto do relatório).
    private readonly IAnalistaDeFotos _analista;

    /// <summary>Os locais padronizados e o alerta de foto do projeto aberto.</summary>
    public ConfiguracaoDasFotos ConfiguracaoDasFotos => _cronograma.ConfiguracaoDasFotos;

    private FotosDoProjetoViewModel NovasFotosDoProjeto()
    {
        Recalcular(); // a tarefa sugerida pela data e o alerta usam as datas calculadas
        return new FotosDoProjetoViewModel(_cronograma, _fotos, _dialogos, _relogio, ProjetoMudouNasFotos, ConfiguracaoDasFotosMudou, MostrarFotosAsync, _analista);
    }

    [RelayCommand]
    private Task FotosDoProjeto() => _dialogos.MostrarFotosDoProjetoAsync(NovasFotosDoProjeto());

    /// <summary>
    /// Bloco 42 (o chat): abre Fotos do projeto > Importar em lote com as fotos anexadas (ou as novas da pasta da obra) e a IA
    /// já as classificando; o usuário confere e importa.
    /// </summary>
    public Task ImportarFotosAsync(string origem, IReadOnlyList<string> caminhos) => ImportarFotosAsync(origem, caminhos, null);

    /// <param name="dicas">Bloco 62: a legenda e as tarefas (da OS citada) de cada arquivo, pelo caminho.</param>
    public async Task ImportarFotosAsync(string origem, IReadOnlyList<string> caminhos, IReadOnlyDictionary<string, DicaDaFoto>? dicas)
    {
        var fotos = NovasFotosDoProjeto();
        if (dicas is not null)
            fotos.Dicas = dicas;
        fotos.AbaEscolhida = FotosDoProjetoViewModel.AbaImportacao;
        var leitura = origem == OrigensDasFotos.Pasta ? fotos.BuscarNaPastaDaObraCommand.ExecuteAsync(null) : fotos.ImportarComIaAsync(caminhos);
        await _dialogos.MostrarFotosDoProjetoAsync(fotos);
        await leitura;
    }

    // Fotos importadas, legendas ou % sugeridos pela IA aplicados: as linhas dessas tarefas se atualizam, e é um passo do
    // Desfazer com a descrição.
    private void ProjetoMudouNasFotos(IReadOnlyCollection<Guid> tarefas, string descricao)
    {
        foreach (var linha in Tarefas.Where(l => tarefas.Contains(l.Tarefa.Id)))
            linha.FotosMudaram();
        OnPropertyChanged(nameof(FotosDaSelecionada));
        OnPropertyChanged(nameof(DicaFotosDaSelecionada));
        MarcarAlteracao(descricao);
    }

    private void ConfiguracaoDasFotosMudou()
    {
        MarcarSemRecalcular();
        OnPropertyChanged(nameof(ConfiguracaoDasFotos));
        AtualizarAlertasDeFotos();
    }

    /// <summary>Os locais sugeridos na galeria: os padronizados e os já usados nas fotos do projeto.</summary>
    private IReadOnlyList<string> SugestoesDeLocal() =>
        _cronograma.ConfiguracaoDasFotos.Sugestoes(_cronograma.Tarefas.SelectMany(t => t.FotosNaOrdemDeCadastro));

    /// <summary>Põe (ou tira) o aviso de "sem foto há N dias" em cada linha da grid, na data de hoje.</summary>
    private void AtualizarAlertasDeFotos()
    {
        var alertas = AlertaDeFotos.Calcular(_cronograma, _relogio.GetLocalNow().DateTime).ToDictionary(a => a.Tarefa.Id);
        foreach (var linha in Tarefas)
            linha.AlertaDeFoto = alertas.TryGetValue(linha.Tarefa.Id, out var alerta) ? alerta.Descricao : null;
    }

    /// <summary>
    /// Abre a galeria de fotos da tarefa, tenha ela fotos ou não (ícone de câmera no Gantt e na grid, menu
    /// Tarefa e menus de contexto). Uma foto adicionada na galeria entra na tarefa e marca o projeto como
    /// alterado; a câmera da grid e do Gantt aparece na hora.
    /// </summary>
    public Task MostrarFotosAsync(Guid tarefaId)
    {
        var linha = Tarefas.First(t => t.Tarefa.Id == tarefaId);
        return _dialogos.MostrarFotosAsync(new GaleriaFotosViewModel(linha, _fotos, _dialogos, _relogio, () =>
        {
            linha.FotosMudaram();
            OnPropertyChanged(nameof(FotosDaSelecionada));
            OnPropertyChanged(nameof(DicaFotosDaSelecionada));
            MarcarAlteracao($"Fotos de \"{linha.Nome}\"");
        }, SugestoesDeLocal));
    }

    /// <summary>
    /// Tarefa > Gerenciar fotos… e os menus de contexto da grid e do Gantt: a galeria da tarefa da linha
    /// (<paramref name="linha"/>) ou, sem ela, da selecionada.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PodeGerenciarFotos))]
    private Task GerenciarFotos(TarefaViewModel? linha) => MostrarFotosAsync((linha ?? TarefaSelecionada)!.Tarefa.Id);

    private bool PodeGerenciarFotos(TarefaViewModel? linha) => (linha ?? TarefaSelecionada) is not null;

    /// <summary>A dica do item Gerenciar fotos… (desabilitado sem tarefa selecionada, com o motivo).</summary>
    public string DicaGerenciarFotos => TarefaSelecionada is null
        ? "Selecione uma tarefa para gerenciar suas fotos"
        : "Ver as fotos da tarefa selecionada e adicionar novas";

    void IEdicaoCronograma.GerenciarFotos(TarefaViewModel linha) => _ = MostrarFotosAsync(linha.Tarefa.Id);
}
