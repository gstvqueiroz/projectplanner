using System.Globalization;

namespace ProjectPlanner.Core.Fotos;

/// <summary>Bloco 28 (FT4): onde a foto foi tirada, lido do GPS do EXIF (graus decimais, WGS 84).</summary>
public sealed record Coordenadas
{
    /// <exception cref="DomainValidationException">Latitude fora de −90 a 90, longitude fora de −180 a 180, ou não é número.</exception>
    public Coordenadas(double latitude, double longitude)
    {
        if (double.IsNaN(latitude) || latitude is < -90 or > 90)
            throw new DomainValidationException(nameof(Latitude), "A latitude precisa estar entre −90 e 90 graus.");
        if (double.IsNaN(longitude) || longitude is < -180 or > 180)
            throw new DomainValidationException(nameof(Longitude), "A longitude precisa estar entre −180 e 180 graus.");
        (Latitude, Longitude) = (latitude, longitude);
    }

    public double Latitude { get; }

    public double Longitude { get; }

    /// <summary>"-22.905810, -47.062630", como os mapas aceitam na busca.</summary>
    public override string ToString() =>
        $"{Latitude.ToString("0.000000", CultureInfo.InvariantCulture)}, {Longitude.ToString("0.000000", CultureInfo.InvariantCulture)}";

    /// <summary>O ponto no OpenStreetMap.</summary>
    public Uri Mapa()
    {
        var (lat, lon) = (Latitude.ToString("0.######", CultureInfo.InvariantCulture), Longitude.ToString("0.######", CultureInfo.InvariantCulture));
        return new Uri($"https://www.openstreetmap.org/?mlat={lat}&mlon={lon}#map=18/{lat}/{lon}");
    }
}

/// <summary>
/// Bloco 28: a lista de locais padronizados do projeto (FT4, sugeridos ao preencher o local de uma foto) e de
/// quantos dias sem foto uma tarefa em andamento passa a ser lembrada (FT5; 0 desliga o alerta).
/// </summary>
public sealed record ConfiguracaoDasFotos
{
    public const int DiasPadrao = 7;
    public const int DiasMaximos = 365;
    public const int QuantidadeMaximaDeLocais = 200;

    public static readonly ConfiguracaoDasFotos Padrao = new([], DiasPadrao);

    /// <param name="locais">Os locais da obra; vazios e repetidos (sem diferenciar maiúsculas) ficam de fora.</param>
    /// <param name="diasSemFotoParaAlerta">De 0 (sem alerta) a <see cref="DiasMaximos"/>.</param>
    /// <exception cref="DomainValidationException">Local longo demais, locais demais ou dias fora do intervalo.</exception>
    public ConfiguracaoDasFotos(IEnumerable<string> locais, int diasSemFotoParaAlerta)
    {
        ArgumentNullException.ThrowIfNull(locais);
        if (diasSemFotoParaAlerta is < 0 or > DiasMaximos)
            throw new DomainValidationException(nameof(DiasSemFotoParaAlerta),
                $"Os dias sem foto para o alerta precisam estar entre 0 (sem alerta) e {DiasMaximos}.");
        var lista = new List<string>();
        foreach (var local in locais.Select(l => l?.Trim() ?? "").Where(l => l.Length > 0))
        {
            if (local.Length > MetadadosFoto.TamanhoMaximoDoLocal)
                throw new DomainValidationException(nameof(Locais),
                    $"O local \"{local[..30]}…\" tem {local.Length} caracteres; o máximo é {MetadadosFoto.TamanhoMaximoDoLocal}.");
            if (!lista.Contains(local, StringComparer.CurrentCultureIgnoreCase))
                lista.Add(local);
        }
        if (lista.Count > QuantidadeMaximaDeLocais)
            throw new DomainValidationException(nameof(Locais), $"São {lista.Count} locais; o máximo é {QuantidadeMaximaDeLocais}.");
        (Locais, DiasSemFotoParaAlerta) = (lista, diasSemFotoParaAlerta);
    }

    public IReadOnlyList<string> Locais { get; }

    public int DiasSemFotoParaAlerta { get; }

    public bool AlertaLigado => DiasSemFotoParaAlerta > 0;

    /// <summary>Bloco 42: a pasta de onde vêm as fotos da obra (a do celular sincronizada, por exemplo); nula sem pasta.</summary>
    public string? PastaDaObra { get; init; }

    /// <summary>Bloco 42: a última vez que a pasta foi lida (só os arquivos mais novos que isso entram de novo).</summary>
    public DateTime? UltimaLeituraDaPasta { get; init; }

    public bool EhPadrao => Locais.Count == 0 && DiasSemFotoParaAlerta == DiasPadrao && PastaDaObra is null;

    public bool Equals(ConfiguracaoDasFotos? outra) =>
        outra is not null && DiasSemFotoParaAlerta == outra.DiasSemFotoParaAlerta && Locais.SequenceEqual(outra.Locais);

    public override int GetHashCode() => HashCode.Combine(DiasSemFotoParaAlerta, string.Join("\n", Locais));

    /// <summary>
    /// Os locais para sugerir ao preencher uma foto: os padronizados, na ordem da lista, e depois os já usados nas
    /// fotos que não estão nela, em ordem alfabética.
    /// </summary>
    public IReadOnlyList<string> Sugestoes(IEnumerable<FotoTarefa> fotos)
    {
        ArgumentNullException.ThrowIfNull(fotos);
        var usados = fotos.Select(f => f.Local).OfType<string>()
            .Where(l => !Locais.Contains(l, StringComparer.CurrentCultureIgnoreCase))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Order(StringComparer.Create(CultureInfo.GetCultureInfo("pt-BR"), ignoreCase: true));
        return [.. Locais, .. usados];
    }
}

/// <summary>Bloco 28 (FT2): a tarefa sugerida para uma foto importada em lote, pela data em que foi tirada.</summary>
public static class SugestaoDeTarefa
{
    /// <summary>
    /// As tarefas de trabalho (não resumos nem marcos) cujo período (o calculado, ou o informado) contém o dia da
    /// foto; a sugestão é a mais curta (a mais específica) e, no empate, a primeira na ordem da EDT. Nula quando
    /// nenhuma tarefa estava prevista naquele dia.
    /// </summary>
    public static Tarefa? Sugerir(Cronograma cronograma, DateTime dataDaFoto)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        var dia = dataDaFoto.Date;
        return cronograma.Tarefas
            .Select((tarefa, ordem) => (tarefa, ordem))
            .Where(x => !x.tarefa.EhResumo && !x.tarefa.EhMarco
                        && (x.tarefa.InicioMaisCedo ?? x.tarefa.DataInicio).Date <= dia
                        && (x.tarefa.TerminoMaisCedo ?? x.tarefa.DataTermino).Date >= dia)
            .OrderBy(x => x.tarefa.Duracao)
            .ThenBy(x => x.ordem)
            .Select(x => x.tarefa)
            .FirstOrDefault();
    }
}

/// <summary>
/// Bloco 42: a data da foto confere com a tarefa? Uma foto tirada bem antes do início ou bem depois do término previsto
/// (com uma folga de <see cref="Folga"/> dias) provavelmente é de outra tarefa, ou a data da câmera está errada.
/// </summary>
public static class CoerenciaDaData
{
    public const int Folga = 7;

    /// <summary>O aviso ("A foto é de 05/10/2026, antes do início previsto da tarefa (12/10/2026)."); nulo se confere.</summary>
    public static string? Aviso(Tarefa tarefa, DateTime dataDaFoto)
    {
        ArgumentNullException.ThrowIfNull(tarefa);
        var (inicio, termino, dia) = ((tarefa.InicioMaisCedo ?? tarefa.DataInicio).Date, (tarefa.TerminoMaisCedo ?? tarefa.DataTermino).Date, dataDaFoto.Date);
        if (dia < inicio.AddDays(-Folga))
            return $"A foto é de {dia:dd/MM/yyyy}, antes do início previsto da tarefa ({inicio:dd/MM/yyyy}).";
        if (dia > termino.AddDays(Folga) && tarefa.PercentualConcluido >= 100)
            return $"A foto é de {dia:dd/MM/yyyy}, depois do término da tarefa ({termino:dd/MM/yyyy}), que já está concluída.";
        return null;
    }
}

/// <summary>Uma tarefa em andamento sem foto recente (FT5).</summary>
/// <param name="Dias">Há quantos dias está sem foto (desde a última, ou desde o início se nunca teve).</param>
/// <param name="UltimaFoto">O dia da foto mais recente, nulo se a tarefa nunca teve foto.</param>
public sealed record TarefaSemFotoRecente(Tarefa Tarefa, string Edt, int Dias, DateOnly? UltimaFoto)
{
    /// <summary>"Sem foto há 9 dias (a última é de 02/10/2026)" ou "Sem nenhuma foto desde o início, há 12 dias".</summary>
    public string Descricao => UltimaFoto is { } ultima
        ? $"Sem foto há {Quantos} (a última é de {ultima.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)})"
        : $"Sem nenhuma foto desde o início, há {Quantos}";

    private string Quantos => Dias == 1 ? "1 dia" : $"{Dias} dias";
}

/// <summary>
/// Bloco 28 (FT5): as tarefas em andamento que estão há <see cref="ConfiguracaoDasFotos.DiasSemFotoParaAlerta"/>
/// dias ou mais sem foto. Em andamento é uma tarefa de trabalho (não resumo nem marco), não concluída, já
/// iniciada (com % maior que zero ou com o início no passado). Conta-se a partir da última foto ou, se a tarefa
/// nunca teve foto (ou só tem fotos anteriores ao início), a partir do início.
/// </summary>
public static class AlertaDeFotos
{
    /// <returns>Na ordem da EDT; vazia com o alerta desligado.</returns>
    public static IReadOnlyList<TarefaSemFotoRecente> Calcular(Cronograma cronograma, DateTime hoje)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        var dias = cronograma.ConfiguracaoDasFotos.DiasSemFotoParaAlerta;
        if (dias == 0)
            return [];
        var dia = hoje.Date;
        var edt = cronograma.NumeracaoEdt();
        var resultado = new List<TarefaSemFotoRecente>();
        foreach (var tarefa in cronograma.Tarefas.Where(t => !t.EhResumo && !t.EhMarco && t.PercentualConcluido < 100))
        {
            var inicio = (tarefa.InicioMaisCedo ?? tarefa.DataInicio).Date;
            if (tarefa.PercentualConcluido == 0 && inicio > dia)
                continue; // ainda não começou
            var ultima = tarefa.FotosNaOrdemDeCadastro.Select(f => f.DataFoto.Date).Where(d => d <= dia)
                .DefaultIfEmpty(DateTime.MinValue).Max();
            var referencia = ultima > inicio ? ultima : inicio;
            var semFoto = (int)(dia - referencia).TotalDays;
            if (semFoto >= dias)
                resultado.Add(new TarefaSemFotoRecente(tarefa, edt[tarefa.Id], semFoto, ultima == DateTime.MinValue ? null : DateOnly.FromDateTime(ultima)));
        }
        return resultado;
    }
}
