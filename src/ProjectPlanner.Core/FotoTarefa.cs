namespace ProjectPlanner.Core;

/// <summary>
/// Uma foto de uma tarefa (Bloco 22): os dados técnicos da imagem guardada, as informações que o usuário edita
/// e quando foi cadastrada. A imagem em si (versão reduzida e miniatura) não fica no domínio: fica no arquivo
/// .pplan, lida sob demanda (ver <see cref="IProjetoRepository"/>).
/// </summary>
public sealed record FotoTarefa
{
    /// <exception cref="DomainValidationException">Id ou tarefa vazios, ou data de cadastro ausente.</exception>
    public FotoTarefa(Guid id, Guid tarefaId, DadosDaImagem imagem, MetadadosFoto metadados, DateTime dataCadastro)
    {
        ArgumentNullException.ThrowIfNull(imagem);
        ArgumentNullException.ThrowIfNull(metadados);
        if (id == Guid.Empty)
            throw new DomainValidationException(nameof(Id), "O id da foto é obrigatório.");
        if (tarefaId == Guid.Empty)
            throw new DomainValidationException(nameof(TarefaId), "A tarefa da foto é obrigatória.");
        if (dataCadastro == default)
            throw new DomainValidationException(nameof(DataCadastro), "A data de cadastro da foto é obrigatória.");

        (Id, TarefaId, Imagem, Metadados, DataCadastro) = (id, tarefaId, imagem, metadados, dataCadastro);
    }

    public Guid Id { get; }

    public Guid TarefaId { get; }

    /// <summary>Nome do arquivo original, tamanho e dimensões da imagem guardada, hash do original.</summary>
    public DadosDaImagem Imagem { get; }

    /// <summary>O que o usuário informa e edita: data, título, legenda, autor, local, % na data, tags.</summary>
    public MetadadosFoto Metadados { get; }

    /// <summary>Quando a foto entrou no projeto.</summary>
    public DateTime DataCadastro { get; }

    public DateTime DataFoto => Metadados.DataFoto;
    public string? Titulo => Metadados.Titulo;
    public string? Descricao => Metadados.Descricao;
    public string? Autor => Metadados.Autor;
    public string? Local => Metadados.Local;
    public int? PercentualConcluidoNaData => Metadados.PercentualConcluidoNaData;
    public IReadOnlyList<string> Tags => Metadados.Tags;

    /// <summary>Bloco 27: se a foto entra no relatório fotográfico (todas entram, até o usuário tirar).</summary>
    public bool NoRelatorio { get; init; } = true;

    /// <summary>Bloco 27: foto de destaque no relatório (vem primeiro no grupo e maior).</summary>
    public bool Destaque { get; init; }

    /// <summary>Bloco 28: onde a foto foi tirada, quando o EXIF trazia o GPS.</summary>
    public Fotos.Coordenadas? Gps { get; init; }

    /// <summary>A mesma foto com outras informações (edição pelo usuário).</summary>
    public FotoTarefa ComMetadados(MetadadosFoto metadados) =>
        new(Id, TarefaId, Imagem, metadados, DataCadastro) { NoRelatorio = NoRelatorio, Destaque = Destaque, Gps = Gps };

    /// <summary>A mesma foto com outra imagem guardada (girada), mantendo o hash do original.</summary>
    /// <param name="novoId">
    /// Bloco 30: o Id da versão nova. A imagem é guardada pelo Id, então a versão girada ganha outro e a de antes
    /// continua existindo para o Desfazer; nulo mantém o mesmo Id.
    /// </param>
    public FotoTarefa ComImagem(DadosDaImagem imagem, Guid? novoId = null) =>
        new(novoId ?? Id, TarefaId, imagem, Metadados, DataCadastro) { NoRelatorio = NoRelatorio, Destaque = Destaque, Gps = Gps };

    /// <summary>A mesma foto com outra escolha para o relatório (Bloco 27). Fora do relatório, não há destaque.</summary>
    public FotoTarefa ComCuradoria(bool noRelatorio, bool destaque) =>
        new(Id, TarefaId, Imagem, Metadados, DataCadastro) { NoRelatorio = noRelatorio, Destaque = noRelatorio && destaque, Gps = Gps };
}

/// <summary>Os dados técnicos da imagem guardada no projeto.</summary>
public sealed record DadosDaImagem
{
    public const int TamanhoMaximoDoNome = 255;

    /// <param name="nomeArquivoOriginal">Nome do arquivo de onde a foto veio (só o nome, sem a pasta).</param>
    /// <param name="largura">Largura da imagem guardada, em pixels.</param>
    /// <param name="altura">Altura da imagem guardada, em pixels.</param>
    /// <param name="tamanhoBytes">Tamanho da imagem guardada (a versão reduzida), em bytes.</param>
    /// <param name="hashOriginal">SHA-256 do arquivo original, em hexadecimal: identifica a mesma foto anexada de novo.</param>
    /// <exception cref="DomainValidationException">Algum valor inválido.</exception>
    public DadosDaImagem(string nomeArquivoOriginal, int largura, int altura, long tamanhoBytes, string hashOriginal)
    {
        var nome = Path.GetFileName((nomeArquivoOriginal ?? "").Trim());
        if (nome.Length == 0)
            throw new DomainValidationException(nameof(NomeArquivoOriginal), "O nome do arquivo da foto é obrigatório.");
        if (nome.Length > TamanhoMaximoDoNome)
            throw new DomainValidationException(nameof(NomeArquivoOriginal),
                $"O nome do arquivo tem {nome.Length} caracteres; o máximo é {TamanhoMaximoDoNome}.");
        if (largura <= 0 || altura <= 0)
            throw new DomainValidationException(nameof(Largura), "As dimensões da imagem precisam ser positivas.");
        if (tamanhoBytes <= 0)
            throw new DomainValidationException(nameof(TamanhoBytes), "O tamanho da imagem precisa ser positivo.");
        var hash = (hashOriginal ?? "").Trim().ToLowerInvariant();
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw new DomainValidationException(nameof(HashOriginal), "O hash do arquivo original é inválido.");

        (NomeArquivoOriginal, Largura, Altura, TamanhoBytes, HashOriginal) = (nome, largura, altura, tamanhoBytes, hash);
    }

    public string NomeArquivoOriginal { get; }
    public int Largura { get; }
    public int Altura { get; }
    public long TamanhoBytes { get; }
    public string HashOriginal { get; }
}

/// <summary>As informações de uma foto que o usuário edita.</summary>
public sealed record MetadadosFoto
{
    public static readonly DateTime DataMinima = new(1900, 1, 1);
    public static readonly DateTime DataMaxima = new(2100, 12, 31, 23, 59, 59);
    public const int TamanhoMaximoDoTitulo = 120;
    public const int TamanhoMaximoDaDescricao = 2000;
    public const int TamanhoMaximoDoAutor = 120;
    public const int TamanhoMaximoDoLocal = 200;
    public const int TamanhoMaximoDaTag = 40;
    public const int QuantidadeMaximaDeTags = 20;

    /// <param name="dataFoto">Quando a foto foi tirada (data e hora).</param>
    /// <param name="descricao">A legenda.</param>
    /// <param name="local">Local ou trecho da obra.</param>
    /// <param name="percentualConcluidoNaData">O avanço da tarefa no dia da foto, de 0 a 100.</param>
    /// <param name="tags">Marcadores livres ("antes", "depois", "problema"...); repetidos e vazios são ignorados.</param>
    /// <exception cref="DomainValidationException">Data fora do intervalo, texto longo demais, % fora de 0–100 ou tags demais.</exception>
    public MetadadosFoto(DateTime dataFoto, string? titulo = null, string? descricao = null, string? autor = null,
        string? local = null, int? percentualConcluidoNaData = null, IEnumerable<string>? tags = null)
    {
        if (dataFoto == default)
            throw new DomainValidationException(nameof(DataFoto), "A data da foto é obrigatória.");
        if (dataFoto < DataMinima || dataFoto > DataMaxima)
            throw new DomainValidationException(nameof(DataFoto),
                $"A data da foto precisa estar entre {DataMinima:dd/MM/yyyy} e {DataMaxima:dd/MM/yyyy}.");
        if (percentualConcluidoNaData is < 0 or > 100)
            throw new DomainValidationException(nameof(PercentualConcluidoNaData), "O % concluído na data precisa estar entre 0 e 100.");

        DataFoto = dataFoto;
        Titulo = Texto(titulo, TamanhoMaximoDoTitulo, nameof(Titulo), "O título");
        Descricao = Texto(descricao, TamanhoMaximoDaDescricao, nameof(Descricao), "A legenda");
        Autor = Texto(autor, TamanhoMaximoDoAutor, nameof(Autor), "O autor");
        Local = Texto(local, TamanhoMaximoDoLocal, nameof(Local), "O local");
        PercentualConcluidoNaData = percentualConcluidoNaData;
        Tags = NormalizarTags(tags ?? []);
    }

    public DateTime DataFoto { get; }
    public string? Titulo { get; }
    public string? Descricao { get; }
    public string? Autor { get; }
    public string? Local { get; }
    public int? PercentualConcluidoNaData { get; }
    public IReadOnlyList<string> Tags { get; }

    /// <summary>As tags como o usuário as digita: "antes, problema".</summary>
    public string TextoDasTags => string.Join(", ", Tags);

    /// <summary>Separa as tags de um texto digitado (por vírgula ou ponto e vírgula).</summary>
    public static IReadOnlyList<string> LerTags(string? texto) =>
        (texto ?? "").Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    public bool Equals(MetadadosFoto? outro) =>
        outro is not null && DataFoto == outro.DataFoto && Titulo == outro.Titulo && Descricao == outro.Descricao
        && Autor == outro.Autor && Local == outro.Local && PercentualConcluidoNaData == outro.PercentualConcluidoNaData
        && Tags.SequenceEqual(outro.Tags);

    public override int GetHashCode() => HashCode.Combine(DataFoto, Titulo, Descricao, Autor, Local, PercentualConcluidoNaData, TextoDasTags);

    private static string? Texto(string? valor, int maximo, string propriedade, string nome)
    {
        var texto = valor?.Trim();
        if (string.IsNullOrEmpty(texto))
            return null;
        if (texto.Length > maximo)
            throw new DomainValidationException(propriedade, $"{nome} tem {texto.Length} caracteres; o máximo é {maximo}.");
        return texto;
    }

    private static List<string> NormalizarTags(IEnumerable<string> tags)
    {
        var resultado = new List<string>();
        foreach (var tag in tags.Select(t => t?.Trim() ?? "").Where(t => t.Length > 0))
        {
            if (tag.Length > TamanhoMaximoDaTag)
                throw new DomainValidationException(nameof(Tags), $"A tag \"{tag}\" tem {tag.Length} caracteres; o máximo é {TamanhoMaximoDaTag}.");
            if (!resultado.Contains(tag, StringComparer.CurrentCultureIgnoreCase))
                resultado.Add(tag);
        }
        if (resultado.Count > QuantidadeMaximaDeTags)
            throw new DomainValidationException(nameof(Tags), $"São {resultado.Count} tags; o máximo é {QuantidadeMaximaDeTags}.");
        return resultado;
    }
}
