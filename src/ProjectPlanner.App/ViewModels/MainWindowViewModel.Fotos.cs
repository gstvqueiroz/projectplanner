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
    public async Task ImportarFotosAsync(string origem, IReadOnlyList<string> caminhos)
    {
        var fotos = NovasFotosDoProjeto();
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
}
