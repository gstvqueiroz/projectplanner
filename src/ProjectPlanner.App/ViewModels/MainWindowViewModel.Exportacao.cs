using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;

namespace ProjectPlanner.App.ViewModels;

// Bloco 25 (E1): Arquivo > Exportar para CSV…, uma linha por tarefa, com as datas do cálculo (as das barras do Gantt).
public sealed partial class MainWindowViewModel
{
    public static IReadOnlyList<string> ColunasDoCsv { get; } =
    [
        "Nº", "EDT", "Nível", "Nome", "Tipo", "Duração (dias úteis)", "Início (cronograma)", "Término (cronograma)",
        "% concluído", "Predecessoras", "Sucessoras", "Folga (dias úteis)", "Crítica", "Fotos", "Notas",
    ];

    /// <summary>O CSV do cronograma, na ordem da grid (com as tarefas de resumos recolhidos).</summary>
    public string TabelaEmCsv()
    {
        var edicao = (IEdicaoCronograma)this;
        var linhas = Tarefas.Select(linha =>
        {
            var tarefa = linha.Tarefa;
            var (inicio, termino) = (tarefa.InicioMaisCedo ?? tarefa.DataInicio, tarefa.TerminoMaisCedo ?? tarefa.DataTermino);
            return (IReadOnlyList<string>)
            [
                linha.Numero.ToString(Cultura),
                linha.Edt,
                (linha.Nivel + 1).ToString(Cultura),
                TabelaCsv.TextoLivre(tarefa.Nome),
                linha.EhResumo ? "Resumo" : linha.EhMarco ? "Marco" : "Tarefa",
                tarefa.Duracao.ToString(Cultura),
                inicio.ToString("dd/MM/yyyy", Cultura),
                termino.ToString("dd/MM/yyyy", Cultura),
                tarefa.PercentualConcluido.ToString(Cultura),
                edicao.TextoPredecessoras(tarefa),
                edicao.TextoSucessoras(tarefa),
                tarefa.Folga?.ToString(Cultura) ?? "",
                tarefa.EhCritica ? "Sim" : "Não",
                tarefa.Fotos.Count.ToString(Cultura),
                TabelaCsv.TextoLivre(tarefa.Notas),
            ];
        });
        return TabelaCsv.Gerar(ColunasDoCsv, linhas);
    }

    [RelayCommand]
    private async Task ExportarCsv()
    {
        var sugestao = (CaminhoArquivo is null ? _cronograma.Nome : Path.GetFileNameWithoutExtension(CaminhoArquivo)) + ".csv";
        if (await _dialogos.EscolherArquivoCsvAsync(sugestao) is not { } caminho)
            return;
        try
        {
            File.WriteAllText(caminho, TabelaEmCsv(), TabelaCsv.Codificacao);
            Mensagem = $"Cronograma exportado para {Path.GetFileName(caminho)} ({(Tarefas.Count == 1 ? "1 tarefa" : $"{Tarefas.Count} tarefas")}).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Mensagem = $"Não foi possível exportar o cronograma. {MensagensDeErro.Descrever(ex)}";
        }
    }
}
