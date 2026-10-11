namespace ProjectPlanner.Core.Relatorios;

/// <summary>Os dados da obra para a capa do relatório fotográfico (Bloco 27); ficam no projeto.</summary>
public sealed record DadosDaObra
{
    public const int TamanhoMaximo = 200;

    public static readonly DadosDaObra Vazios = new(null, null, null);

    /// <exception cref="DomainValidationException">Algum texto passa de <see cref="TamanhoMaximo"/> caracteres.</exception>
    public DadosDaObra(string? contratante, string? contrato, string? endereco)
    {
        Contratante = Texto(contratante, "O contratante");
        Contrato = Texto(contrato, "O contrato");
        Endereco = Texto(endereco, "O endereço");
    }

    public string? Contratante { get; }
    public string? Contrato { get; }
    public string? Endereco { get; }

    public bool Vazio => Contratante is null && Contrato is null && Endereco is null;

    private static string? Texto(string? valor, string nome)
    {
        var texto = valor?.Trim();
        if (string.IsNullOrEmpty(texto))
            return null;
        return texto.Length > TamanhoMaximo
            ? throw new DomainValidationException(nameof(DadosDaObra), $"{nome} tem {texto.Length} caracteres; o máximo é {TamanhoMaximo}.")
            : texto;
    }
}

/// <summary>Um relatório fotográfico já gerado: o número, quando, o período e quantas fotos (Bloco 27).</summary>
public sealed record RelatorioGerado
{
    /// <exception cref="DomainValidationException">Número menor que 1, período invertido ou quantidade negativa.</exception>
    public RelatorioGerado(int numero, DateTime geradoEm, DateOnly de, DateOnly ate, int fotos)
    {
        if (numero < 1)
            throw new DomainValidationException(nameof(Numero), "O número do relatório começa em 1.");
        if (ate < de)
            throw new DomainValidationException(nameof(Ate), "O período do relatório termina antes de começar.");
        if (fotos < 0)
            throw new DomainValidationException(nameof(Fotos), "A quantidade de fotos não pode ser negativa.");
        (Numero, GeradoEm, De, Ate, Fotos) = (numero, geradoEm, de, ate, fotos);
    }

    public int Numero { get; }
    public DateTime GeradoEm { get; }
    public DateOnly De { get; }
    public DateOnly Ate { get; }
    public int Fotos { get; }
}
