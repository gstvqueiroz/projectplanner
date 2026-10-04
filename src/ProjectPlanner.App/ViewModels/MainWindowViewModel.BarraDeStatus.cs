using System.Globalization;
using ProjectPlanner.App.Services;
using ProjectPlanner.Core;

namespace ProjectPlanner.App.ViewModels;

// Bloco 25 (V1): à direita da barra de status, o tamanho do projeto, o período dele, a tarefa selecionada (com as datas
// do cálculo, as mesmas das barras do Gantt) e a versão. A mensagem continua à esquerda.
public sealed partial class MainWindowViewModel
{
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>"37 tarefas (6 resumos, 2 marcos)", "1 tarefa", "Nenhuma tarefa".</summary>
    public string TextoTarefasNaBarra
    {
        get
        {
            var total = Tarefas.Count;
            if (total == 0)
                return "Nenhuma tarefa";
            var (resumos, marcos) = (Tarefas.Count(t => t.EhResumo), Tarefas.Count(t => t.EhMarco));
            var detalhes = new List<string>();
            if (resumos > 0)
                detalhes.Add(resumos == 1 ? "1 resumo" : $"{resumos} resumos");
            if (marcos > 0)
                detalhes.Add(marcos == 1 ? "1 marco" : $"{marcos} marcos");
            return (total == 1 ? "1 tarefa" : $"{total} tarefas") + (detalhes.Count == 0 ? "" : $" ({string.Join(", ", detalhes)})");
        }
    }

    /// <summary>"07/09/2026 → 09/02/2027 · 108 dias úteis"; vazio sem tarefas ou sem cálculo.</summary>
    public string TextoPeriodoNaBarra
    {
        get
        {
            if (Tarefas.Count == 0 || TerminoProjeto is not { } termino)
                return "";
            var inicio = _cronograma.DataInicio;
            var dias = termino < inicio ? 0 : CalendarioDeTrabalho.ContarDiasUteis(inicio, termino);
            return $"{inicio.ToString("dd/MM/yyyy", Cultura)} → {termino.ToString("dd/MM/yyyy", Cultura)} · " +
                   (dias == 1 ? "1 dia útil" : $"{dias} dias úteis");
        }
    }

    /// <summary>"2.1 Escavação: 07/10/2026 → 09/10/2026"; vazio sem seleção.</summary>
    public string TextoSelecionadaNaBarra
    {
        get
        {
            if (TarefaSelecionada is not { } linha)
                return "";
            var tarefa = linha.Tarefa;
            var (inicio, termino) = (tarefa.InicioMaisCedo ?? tarefa.DataInicio, tarefa.TerminoMaisCedo ?? tarefa.DataTermino);
            var datas = tarefa.EhMarco
                ? inicio.ToString("dd/MM/yyyy", Cultura)
                : $"{inicio.ToString("dd/MM/yyyy", Cultura)} → {termino.ToString("dd/MM/yyyy", Cultura)}";
            return $"{linha.Edt} {tarefa.Nome}: {datas}";
        }
    }

    public string TextoVersaoNaBarra => $"v{VersaoDoProduto.Atual}";

    /// <summary>
    /// A dica de uma barra do Gantt (Bloco 25, V4): EDT e nome, datas do cálculo, duração, % concluído, folga, se é
    /// crítica, predecessoras e fotos. Linhas sem informação ficam de fora.
    /// </summary>
    public string DicaDaTarefaNoGantt(Guid tarefaId)
    {
        if (Tarefas.FirstOrDefault(l => l.Tarefa.Id == tarefaId) is not { } linha)
            return "";
        var tarefa = linha.Tarefa;
        var (inicio, termino) = (tarefa.InicioMaisCedo ?? tarefa.DataInicio, tarefa.TerminoMaisCedo ?? tarefa.DataTermino);
        string Data(DateTime data) => data.ToString("dd/MM/yyyy", Cultura);
        var linhas = new List<string> { $"{linha.Edt} {tarefa.Nome}" };
        if (tarefa.EhMarco)
            linhas.Add($"Marco em {Data(inicio)}");
        else
            linhas.Add($"{Data(inicio)} → {Data(termino)} · " + (tarefa.Duracao == 1 ? "1 dia" : $"{tarefa.Duracao} dias") +
                       (tarefa.EhResumo ? " (resumo)" : ""));
        if (!tarefa.EhMarco)
            linhas.Add($"{tarefa.PercentualConcluido}% concluído");
        if (tarefa.Folga is { } folga && !tarefa.EhResumo)
            linhas.Add("Folga: " + (folga == 1 ? "1 dia" : $"{folga} dias") + (tarefa.EhCritica ? " (crítica)" : ""));
        if (!string.IsNullOrWhiteSpace(linha.Predecessoras))
            linhas.Add($"Predecessoras: {linha.Predecessoras}");
        if (tarefa.Fotos.Count > 0)
            linhas.Add(tarefa.Fotos.Count == 1 ? "1 foto" : $"{tarefa.Fotos.Count} fotos");
        return string.Join("\n", linhas);
    }

    private void AtualizarBarraDeStatus()
    {
        OnPropertyChanged(nameof(TextoTarefasNaBarra));
        OnPropertyChanged(nameof(TextoPeriodoNaBarra));
        OnPropertyChanged(nameof(TextoSelecionadaNaBarra));
    }
}
