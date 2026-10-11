using System.Globalization;

namespace ProjectPlanner.Core.Fotos;

/// <summary>O que comparar no antes e depois: um local da obra ou uma tarefa.</summary>
public sealed record AssuntoDaComparacao(string Nome, string? Local, Guid? TarefaId)
{
    public static AssuntoDaComparacao DoLocal(string local) => new(local, local, null);

    public static AssuntoDaComparacao DaTarefa(string nome, Guid tarefaId) => new(nome, null, tarefaId);

    public override string ToString() => Nome;
}

/// <summary>
/// Bloco 29 (RF3): duas fotos do mesmo local (ou da mesma tarefa) em dias diferentes, e a série do primeiro ao último dia.
/// </summary>
/// <param name="Serie">Uma foto por dia (a primeira do dia), do mais antigo ao mais recente, no máximo <see cref="AntesEDepois.FotosNaSerie"/>.</param>
public sealed record ComparacaoAntesEDepois(AssuntoDaComparacao Assunto, FotoTarefa Antes, FotoTarefa Depois, IReadOnlyList<FotoTarefa> Serie)
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Dias corridos entre as duas fotos.</summary>
    public int Dias => (Depois.DataFoto.Date - Antes.DataFoto.Date).Days;

    /// <summary>"De 20% para 80% concluído em 26 dias" (com o % das duas, da mesma tarefa), ou "26 dias entre as fotos".</summary>
    public string Evolucao
    {
        get
        {
            var dias = Dias == 1 ? "1 dia" : $"{Dias} dias";
            return Antes.TarefaId == Depois.TarefaId && Antes.PercentualConcluidoNaData is { } de && Depois.PercentualConcluidoNaData is { } para
                ? $"De {de}% para {para}% concluído em {dias}"
                : $"{dias} entre as fotos";
        }
    }

    /// <summary>"Antes · 15/09/2026 08:00".</summary>
    public static string Rotulo(string qual, FotoTarefa foto) => $"{qual} · {foto.DataFoto.ToString("dd/MM/yyyy HH:mm", Brasil)}";
}

/// <summary>Bloco 29 (RF3): monta o antes e depois a partir das fotos do projeto.</summary>
public static class AntesEDepois
{
    public const int FotosNaSerie = 8;

    private static readonly StringComparer Alfabetica = StringComparer.Create(CultureInfo.GetCultureInfo("pt-BR"), ignoreCase: true);

    /// <summary>
    /// Os assuntos que dá para comparar: os locais e as tarefas com fotos em pelo menos dois dias diferentes. Os locais
    /// vêm primeiro (em ordem alfabética; o mesmo nome com maiúsculas ou acentos diferentes é um local só), depois as
    /// tarefas na ordem da EDT.
    /// </summary>
    public static IReadOnlyList<AssuntoDaComparacao> Assuntos(Cronograma cronograma)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        var fotos = cronograma.Tarefas.SelectMany(t => t.FotosNaOrdemDeCadastro).ToList();
        var locais = fotos.Where(f => f.Local is not null)
            .GroupBy(f => FiltroDeFotos.Normalizar(f.Local))
            .Where(g => g.Select(f => f.DataFoto.Date).Distinct().Count() >= 2)
            .Select(g => AssuntoDaComparacao.DoLocal(g.First().Local!))
            .OrderBy(a => a.Nome, Alfabetica);
        var edt = cronograma.NumeracaoEdt();
        var tarefas = cronograma.Tarefas
            .Where(t => t.FotosNaOrdemDeCadastro.Select(f => f.DataFoto.Date).Distinct().Count() >= 2)
            .Select(t => AssuntoDaComparacao.DaTarefa($"{edt[t.Id]} {t.Nome}", t.Id));
        return [.. locais, .. tarefas];
    }

    /// <summary>As fotos do assunto, da mais antiga para a mais recente (só até <paramref name="ate"/>, se informado).</summary>
    public static IReadOnlyList<FotoTarefa> FotosDe(Cronograma cronograma, AssuntoDaComparacao assunto, DateOnly? ate = null)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(assunto);
        var local = FiltroDeFotos.Normalizar(assunto.Local);
        return cronograma.Tarefas
            .Where(t => assunto.TarefaId is not { } id || t.Id == id)
            .SelectMany(t => t.FotosNaOrdemDeCadastro)
            .Where(f => assunto.Local is null || FiltroDeFotos.Normalizar(f.Local) == local)
            .Where(f => ate is not { } limite || DateOnly.FromDateTime(f.DataFoto) <= limite)
            .OrderBy(f => f.DataFoto).ThenBy(f => f.DataCadastro)
            .ToList();
    }

    /// <summary>
    /// A comparação com o antes e o depois escolhidos, ou, sem eles, a primeira e a última foto. Nula com fotos em menos
    /// de dois dias.
    /// </summary>
    public static ComparacaoAntesEDepois? Comparar(AssuntoDaComparacao assunto, IReadOnlyList<FotoTarefa> fotos,
        FotoTarefa? antes = null, FotoTarefa? depois = null)
    {
        ArgumentNullException.ThrowIfNull(assunto);
        ArgumentNullException.ThrowIfNull(fotos);
        var ordenadas = fotos.OrderBy(f => f.DataFoto).ThenBy(f => f.DataCadastro).ToList();
        if (ordenadas.Select(f => f.DataFoto.Date).Distinct().Count() < 2)
            return null;
        antes ??= ordenadas[0];
        depois ??= ordenadas[^1];
        if (antes.DataFoto > depois.DataFoto)
            (antes, depois) = (depois, antes);
        var porDia = ordenadas.Where(f => f.DataFoto >= antes.DataFoto && f.DataFoto <= depois.DataFoto)
            .GroupBy(f => f.DataFoto.Date).Select(g => g.First()).ToList();
        return new ComparacaoAntesEDepois(assunto, antes, depois, Espacar(porDia, FotosNaSerie));
    }

    // No máximo "quantas", sempre com a primeira e a última, espalhadas por igual no meio.
    private static List<FotoTarefa> Espacar(List<FotoTarefa> fotos, int quantas)
    {
        if (fotos.Count <= quantas)
            return fotos;
        return Enumerable.Range(0, quantas).Select(i => fotos[(int)Math.Round(i * (fotos.Count - 1) / (double)(quantas - 1))]).ToList();
    }
}
