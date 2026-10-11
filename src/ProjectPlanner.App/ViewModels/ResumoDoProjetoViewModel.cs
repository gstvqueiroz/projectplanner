using System.Globalization;
using ProjectPlanner.Core;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Uma contagem do resumo do projeto: "Atrasadas: 2".</summary>
public sealed record ContagemNoResumo(string Nome, int Quantidade, bool Destaque = false);

/// <summary>Exibir > Resumo do projeto… (Bloco 25, C7): o andamento do projeto na data de hoje, num relance.</summary>
public sealed class ResumoDoProjetoViewModel : ViewModelBase
{
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("pt-BR");

    public ResumoDoProjetoViewModel(ResumoDoProjeto resumo, DateTime dataDeStatus)
    {
        ArgumentNullException.ThrowIfNull(resumo);
        Resumo = resumo;
        DataDeStatus = dataDeStatus.Date;
    }

    public ResumoDoProjeto Resumo { get; }

    public DateTime DataDeStatus { get; }

    public string Titulo => $"Resumo do projeto — {Resumo.Nome}";

    public string Situacao => $"Situação em {Data(DataDeStatus)}";

    public string Periodo => Resumo.Termino is { } termino
        ? $"Início {Data(Resumo.Inicio)} · Término {Data(termino)} · " + (Resumo.DiasUteis == 1 ? "1 dia útil" : $"{Resumo.DiasUteis} dias úteis")
        : $"Início {Data(Resumo.Inicio)} · o projeto ainda não tem tarefas";

    public int PercentualConcluido => Resumo.PercentualConcluido;

    public string Andamento => $"{Resumo.PercentualConcluido}% concluído · previsto para hoje: {Resumo.PercentualPrevisto}%";

    /// <summary>O projeto está abaixo do previsto para hoje.</summary>
    public bool AbaixoDoPrevisto => Resumo.PercentualConcluido < Resumo.PercentualPrevisto;

    public IReadOnlyList<ContagemNoResumo> Contagens =>
    [
        new("Tarefas", Resumo.Tarefas),
        new("Resumos", Resumo.Resumos),
        new("Marcos", Resumo.Marcos),
        new("Concluídas", Resumo.Concluidas),
        new("Em dia", Resumo.EmDia),
        new("Não iniciadas", Resumo.NaoIniciadas),
        new("Atrasadas", Resumo.Atrasadas, Destaque: Resumo.Atrasadas > 0),
        new("Críticas em aberto", Resumo.Criticas),
        new("Fotos", Resumo.Fotos),
    ];

    /// <summary>"3 Marco 1 — 25/09/2026 · 0% (previsto 100%)".</summary>
    public IReadOnlyList<string> Atrasadas =>
        Resumo.ListaDeAtrasadas.Select(t => $"{t.Edt} {t.Nome} — {Data(t.Data)} · {t.PercentualConcluido}% (previsto {t.PercentualPrevisto}%)").ToList();

    public bool TemAtrasadas => Resumo.ListaDeAtrasadas.Count > 0;

    /// <summary>"4 Marco 2 — 15/10/2026".</summary>
    public IReadOnlyList<string> ProximosMarcos => Resumo.ProximosMarcos.Select(m => $"{m.Edt} {m.Nome} — {Data(m.Data)}").ToList();

    public string SemProximosMarcos => $"Nenhum marco nos próximos {ResumoDoProjeto.DiasDosProximosMarcos} dias.";

    public bool TemProximosMarcos => Resumo.ProximosMarcos.Count > 0;

    /// <summary>Bloco 31: "4 Laje — prazo 15/10/2026".</summary>
    public IReadOnlyList<string> ForaDoPrazo => Resumo.ForaDoPrazo.Select(t => $"{t.Edt} {t.Nome} — prazo {Data(t.Data)}").ToList();

    public bool TemForaDoPrazo => Resumo.ForaDoPrazo.Count > 0;

    /// <summary>Bloco 31: "Linha de base: término 30/11/2026 · 5 dias úteis de atraso"; vazio sem linha de base.</summary>
    public string LinhaDeBase => Resumo is { TerminoDaLinhaDeBase: { } previsto, DesvioDoTermino: { } desvio }
        ? $"Linha de base: término {Data(previsto)} · " + desvio switch
        {
            0 => "no prazo previsto",
            1 => "1 dia útil de atraso",
            > 1 => $"{desvio} dias úteis de atraso",
            -1 => "1 dia útil adiantado",
            _ => $"{-desvio} dias úteis adiantado",
        }
        : "";

    public bool AtrasouNaLinhaDeBase => Resumo.DesvioDoTermino > 0;

    /// <summary>Bloco 28: "5 Alvenaria — Sem foto há 9 dias (a última é de 02/10/2026)".</summary>
    public IReadOnlyList<string> SemFotoRecente => Resumo.SemFotoRecente.Select(t => $"{t.Edt} {t.Tarefa.Nome} — {t.Descricao}").ToList();

    public bool TemSemFotoRecente => Resumo.SemFotoRecente.Count > 0;

    private static string Data(DateTime data) => data.ToString("dd/MM/yyyy", Cultura);
}
