using System.Globalization;
using System.Text;

namespace ProjectPlanner.Core.Fotos;

/// <summary>Atalhos de período da galeria (D3).</summary>
public enum AtalhoDePeriodo
{
    Tudo,
    Hoje,
    Ultimos7Dias,
    EsteMes,
}

/// <summary>
/// Os critérios da galeria (D3). Os que estão preenchidos se combinam (todos precisam valer).
/// </summary>
public sealed record CriteriosDeFotos
{
    /// <summary>Só as fotos deste dia.</summary>
    public DateOnly? Dia { get; init; }

    /// <summary>Início do intervalo (inclusive).</summary>
    public DateOnly? De { get; init; }

    /// <summary>Fim do intervalo (inclusive).</summary>
    public DateOnly? Ate { get; init; }

    public AtalhoDePeriodo Atalho { get; init; } = AtalhoDePeriodo.Tudo;

    /// <summary>Procura no título, na legenda, no local e no autor, sem diferenciar maiúsculas nem acentos.</summary>
    public string? Texto { get; init; }

    /// <summary>Só as fotos com esta tag (sem diferenciar maiúsculas).</summary>
    public string? Tag { get; init; }

    /// <summary>Só as fotos desta tarefa (para a futura visão de todas as fotos do projeto).</summary>
    public Guid? TarefaId { get; init; }

    public bool MaisRecentesPrimeiro { get; init; }

    public static readonly CriteriosDeFotos Nenhum = new();
}

/// <summary>As fotos de um dia, para o cabeçalho de data da galeria.</summary>
public sealed record GrupoDeFotosDoDia(DateOnly Dia, IReadOnlyList<FotoTarefa> Fotos);

/// <summary>
/// Filtra, ordena e agrupa fotos por dia (D3), sem depender da tela. Fotos do mesmo instante ficam na ordem em
/// que foram recebidas (a de cadastro).
/// </summary>
public static class FiltroDeFotos
{
    private static readonly CompareInfo Comparacao = CultureInfo.GetCultureInfo("pt-BR").CompareInfo;

    /// <param name="hoje">O dia de hoje, para os atalhos (passado de fora para o cálculo ser testável).</param>
    /// <exception cref="DomainValidationException">O início do intervalo é depois do fim.</exception>
    public static IReadOnlyList<GrupoDeFotosDoDia> Agrupar(IEnumerable<FotoTarefa> fotos, CriteriosDeFotos criterios, DateOnly hoje) =>
        Filtrar(fotos, criterios, hoje)
            .GroupBy(f => DateOnly.FromDateTime(f.DataFoto))
            .Select(g => new GrupoDeFotosDoDia(g.Key, g.ToList()))
            .ToList();

    /// <summary>As fotos que passam pelos critérios, já na ordem da galeria (a mesma do visualizador).</summary>
    /// <exception cref="DomainValidationException">O início do intervalo é depois do fim.</exception>
    public static IReadOnlyList<FotoTarefa> Filtrar(IEnumerable<FotoTarefa> fotos, CriteriosDeFotos criterios, DateOnly hoje)
    {
        ArgumentNullException.ThrowIfNull(fotos);
        ArgumentNullException.ThrowIfNull(criterios);
        if (criterios.De is { } de && criterios.Ate is { } ate && de > ate)
            throw new DomainValidationException(nameof(CriteriosDeFotos.De),
                $"O início do período ({de:dd/MM/yyyy}) é depois do fim ({ate:dd/MM/yyyy}).");

        var (inicioAtalho, fimAtalho) = Periodo(criterios.Atalho, hoje);
        var texto = Normalizar(criterios.Texto);
        var tag = criterios.Tag?.Trim();

        var filtradas = fotos.Select((foto, ordem) => (foto, ordem)).Where(x =>
        {
            var dia = DateOnly.FromDateTime(x.foto.DataFoto);
            return (criterios.TarefaId is not { } tarefa || x.foto.TarefaId == tarefa)
                && (criterios.Dia is not { } exato || dia == exato)
                && (criterios.De is not { } inicio || dia >= inicio)
                && (criterios.Ate is not { } fim || dia <= fim)
                && dia >= inicioAtalho && dia <= fimAtalho
                && (texto.Length == 0 || ContemTexto(x.foto, texto))
                && (string.IsNullOrEmpty(tag) || x.foto.Tags.Contains(tag, StringComparer.CurrentCultureIgnoreCase));
        });

        var ordenadas = criterios.MaisRecentesPrimeiro
            ? filtradas.OrderByDescending(x => x.foto.DataFoto).ThenByDescending(x => x.ordem)
            : filtradas.OrderBy(x => x.foto.DataFoto).ThenBy(x => x.ordem);
        return ordenadas.Select(x => x.foto).ToList();
    }

    /// <summary>As tags usadas nas fotos, sem repetir, em ordem alfabética (para a lista do filtro).</summary>
    public static IReadOnlyList<string> Tags(IEnumerable<FotoTarefa> fotos) =>
        fotos.SelectMany(f => f.Tags)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Order(StringComparer.Create(CultureInfo.GetCultureInfo("pt-BR"), ignoreCase: true))
            .ToList();

    /// <summary>O intervalo de dias de um atalho, relativo a <paramref name="hoje"/>.</summary>
    public static (DateOnly Inicio, DateOnly Fim) Periodo(AtalhoDePeriodo atalho, DateOnly hoje) => atalho switch
    {
        AtalhoDePeriodo.Hoje => (hoje, hoje),
        AtalhoDePeriodo.Ultimos7Dias => (hoje.AddDays(-6), hoje),
        AtalhoDePeriodo.EsteMes => (new DateOnly(hoje.Year, hoje.Month, 1), new DateOnly(hoje.Year, hoje.Month, 1).AddMonths(1).AddDays(-1)),
        _ => (DateOnly.MinValue, DateOnly.MaxValue),
    };

    private static bool ContemTexto(FotoTarefa foto, string texto) =>
        new[] { foto.Titulo, foto.Descricao, foto.Local, foto.Autor }
            .Any(campo => campo is not null && Normalizar(campo).Contains(texto, StringComparison.Ordinal));

    // Sem acentos e em minúsculas: "concretagem" encontra "Concretagem", e "fundacao" encontra "fundação".
    private static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return "";
        var decomposto = texto.Trim().Normalize(NormalizationForm.FormD);
        var semAcentos = new StringBuilder(decomposto.Length);
        foreach (var c in decomposto)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                semAcentos.Append(c);
        return semAcentos.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }
}
