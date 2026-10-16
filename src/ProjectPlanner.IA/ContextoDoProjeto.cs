using System.Globalization;
using System.Text;

namespace ProjectPlanner.IA;

/// <summary>Como a tarefa aparece no retrato do cronograma.</summary>
public enum TipoNoContexto
{
    Tarefa,
    Resumo,
    Marco,
}

/// <summary>Uma linha do cronograma no retrato mandado ao assistente.</summary>
/// <param name="Numero">O Nº da linha (é por ele que a coluna Predecessoras se refere às outras tarefas).</param>
/// <param name="Nivel">0 no primeiro nível; 1 dentro de um resumo, e assim por diante.</param>
/// <param name="Predecessoras">Como na grid: "2", "3II+2", "2; 4TT-1" (vazio sem predecessoras).</param>
/// <param name="FolgaDias">Folga total em dias úteis; nula antes do cálculo.</param>
/// <param name="Id">A tarefa no cronograma (não vai no texto): é por ele que o App acha a tarefa do Nº que o assistente citar.</param>
public sealed record TarefaNoContexto(
    int Numero,
    string Edt,
    string Nome,
    int Nivel,
    TipoNoContexto Tipo,
    int DuracaoDias,
    DateOnly Inicio,
    DateOnly Termino,
    int PercentualConcluido,
    string Predecessoras,
    int? FolgaDias,
    bool Critica,
    string? Notas,
    int Fotos,
    Guid Id = default);

/// <summary>
/// Retrato só de leitura do cronograma aberto (Bloco 23), montado pelo App a cada envio. O assistente nunca recebe o
/// <c>Cronograma</c> do domínio: só este texto, que vai nas instruções do pedido.
/// </summary>
/// <param name="Orcamento">Bloco 38: o resumo do orçamento, uma linha por item (vazio sem orçamento).</param>
public sealed record ContextoDoProjeto(string Nome, DateOnly Inicio, DateOnly Termino, DateOnly Hoje, IReadOnlyList<TarefaNoContexto> Tarefas,
    IReadOnlyList<string>? Orcamento = null)
{
    /// <summary>Linhas do orçamento no retrato, no máximo.</summary>
    public const int LinhasDoOrcamento = 200;

    /// <summary>Notas mais longas que isso são cortadas no retrato.</summary>
    public const int TamanhoMaximoDasNotas = 500;

    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>O retrato como texto: cabeçalho do projeto, legenda das colunas e uma linha por tarefa.</summary>
    public string ComoTexto()
    {
        var texto = new StringBuilder();
        texto.AppendLine($"Projeto: {Limpar(Nome)}");
        texto.AppendLine($"Início: {Data(Inicio)} · Término: {Data(Termino)} · Hoje: {Data(Hoje)}");
        texto.AppendLine("Calendário: dias úteis de segunda a sexta; durações, folgas e defasagens em dias úteis. " +
                         "Início e Término de cada tarefa são as datas calculadas do cronograma (as das barras do Gantt).");
        texto.AppendLine("Predecessoras usam o Nº da linha: TI = término-início (o padrão, só o número), II = início-início, " +
                         "TT = término-término, IT = início-término; +n/-n = defasagem em dias úteis.");
        texto.AppendLine("Crítica = folga zero (está no caminho crítico). A indentação do nome mostra o nível na EDT.");
        if (Tarefas.Count == 0)
        {
            texto.AppendLine("O projeto ainda não tem tarefas.");
            return texto.ToString().TrimEnd();
        }
        texto.AppendLine($"Tarefas ({Tarefas.Count}):");
        texto.AppendLine("Nº | EDT | Nome | Tipo | Duração | Início | Término | % concluído | Predecessoras | Folga | Crítica | Fotos | Notas");
        foreach (var t in Tarefas)
        {
            texto.AppendLine(string.Join(" | ",
                t.Numero.ToString(Cultura),
                t.Edt,
                new string(' ', 2 * Math.Max(0, t.Nivel)) + Limpar(t.Nome),
                t.Tipo switch { TipoNoContexto.Resumo => "resumo", TipoNoContexto.Marco => "marco", _ => "tarefa" },
                t.DuracaoDias == 1 ? "1 dia" : $"{t.DuracaoDias} dias",
                Data(t.Inicio),
                Data(t.Termino),
                $"{t.PercentualConcluido}%",
                string.IsNullOrWhiteSpace(t.Predecessoras) ? "-" : t.Predecessoras,
                t.FolgaDias is { } folga ? $"{folga} d" : "-",
                t.Critica ? "sim" : "não",
                t.Fotos.ToString(Cultura),
                Notas(t.Notas)));
        }
        if (Orcamento is { Count: > 0 } orcamento)
        {
            texto.AppendLine("Orçamento:");
            foreach (var linha in orcamento.Take(LinhasDoOrcamento))
                texto.AppendLine("- " + Limpar(linha));
            if (orcamento.Count > LinhasDoOrcamento)
                texto.AppendLine($"- (mais {orcamento.Count - LinhasDoOrcamento} serviços)");
        }
        return texto.ToString().TrimEnd();
    }

    private static string Data(DateOnly data) => data.ToString("dd/MM/yyyy", Cultura);

    // O "|" separa as colunas e as quebras de linha separam as tarefas: dentro de um campo viram "/" e " / ".
    private static string Limpar(string texto) =>
        texto.Replace("\r\n", " / ").Replace('\n', ' ').Replace('\r', ' ').Replace('|', '/').Trim();

    private static string Notas(string? notas)
    {
        if (string.IsNullOrWhiteSpace(notas))
            return "-";
        var limpas = Limpar(notas);
        return limpas.Length <= TamanhoMaximoDasNotas ? limpas : limpas[..TamanhoMaximoDasNotas] + "…";
    }
}
