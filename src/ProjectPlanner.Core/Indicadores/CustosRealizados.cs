using System.Globalization;
using System.Text;

namespace ProjectPlanner.Core.Indicadores;

/// <summary>Bloco 37: de que é um custo realizado.</summary>
public enum CategoriaDeCusto
{
    MaoDeObra,
    Material,
    Equipamento,
    Terceiros,
    Administracao,
    Outros,
}

/// <summary>Os nomes das categorias, como aparecem e como são lidos de uma planilha.</summary>
public static class Categorias
{
    public static IReadOnlyList<CategoriaDeCusto> Todas { get; } = Enum.GetValues<CategoriaDeCusto>();

    public static string Texto(CategoriaDeCusto categoria) => categoria switch
    {
        CategoriaDeCusto.MaoDeObra => "Mão de obra",
        CategoriaDeCusto.Material => "Material",
        CategoriaDeCusto.Equipamento => "Equipamento",
        CategoriaDeCusto.Terceiros => "Serviços de terceiros",
        CategoriaDeCusto.Administracao => "Administração",
        _ => "Outros",
    };

    /// <summary>A categoria de um texto de planilha ("Folha", "materiais", "Locação"…); nula se não reconhece.</summary>
    public static CategoriaDeCusto? Ler(string? texto)
    {
        var t = Normalizar(texto);
        if (t.Length == 0)
            return null;
        if (t.StartsWith("mao de obra") || t.StartsWith("folha") || t.StartsWith("pessoal") || t.StartsWith("salario") || t == "mo")
            return CategoriaDeCusto.MaoDeObra;
        if (t.StartsWith("materia"))
            return CategoriaDeCusto.Material;
        if (t.StartsWith("equipamento") || t.StartsWith("locacao") || t.StartsWith("aluguel"))
            return CategoriaDeCusto.Equipamento;
        if (t.Contains("terceiro") || t.StartsWith("servico") || t.StartsWith("empreit") || t.StartsWith("subempreit"))
            return CategoriaDeCusto.Terceiros;
        if (t.StartsWith("administra") || t == "adm" || t.StartsWith("despesa"))
            return CategoriaDeCusto.Administracao;
        if (t.StartsWith("outro"))
            return CategoriaDeCusto.Outros;
        return null;
    }

    internal static string Normalizar(string? texto)
    {
        var decomposto = (texto ?? "").Trim().ToLowerInvariant().Replace("º", "").Replace("°", "").Normalize(NormalizationForm.FormD);
        var limpo = new StringBuilder();
        foreach (var c in decomposto)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                limpo.Append(c);
        return limpo.ToString().TrimEnd('.', ':').Trim();
    }
}

/// <summary>
/// Bloco 37: um custo realizado (uma nota fiscal, uma folha, a locação de um equipamento): a data, a tarefa (nula: custo
/// geral da obra), a categoria, a descrição, o documento e o valor.
/// </summary>
public sealed record LancamentoDeCusto
{
    public const int TamanhoMaximoDaDescricao = 200;
    public const int TamanhoMaximoDoDocumento = 60;

    /// <exception cref="DomainValidationException">Id vazio, sem descrição, textos longos demais, ou valor zero, negativo ou grande demais.</exception>
    public LancamentoDeCusto(Guid id, DateOnly data, Guid? tarefaId, CategoriaDeCusto categoria, string? descricao, string? documento, decimal valor)
    {
        if (id == Guid.Empty)
            throw new DomainValidationException(nameof(Id), "O id do lançamento é obrigatório.");
        var texto = descricao?.Trim();
        if (string.IsNullOrEmpty(texto))
            throw new DomainValidationException(nameof(Descricao), "Escreva a descrição do custo.");
        if (texto.Length > TamanhoMaximoDaDescricao)
            throw new DomainValidationException(nameof(Descricao), $"A descrição tem {texto.Length} caracteres; o máximo é {TamanhoMaximoDaDescricao}.");
        var doc = string.IsNullOrWhiteSpace(documento) ? null : documento.Trim();
        if (doc?.Length > TamanhoMaximoDoDocumento)
            throw new DomainValidationException(nameof(Documento), $"O documento tem {doc.Length} caracteres; o máximo é {TamanhoMaximoDoDocumento}.");
        if (valor <= 0)
            throw new DomainValidationException(nameof(Valor), "O valor do custo precisa ser maior que zero.");
        if (valor > Tarefa.ValorMaximo)
            throw new DomainValidationException(nameof(Valor), "O valor passa do máximo aceito (R$ 999 bilhões).");
        if (!Enum.IsDefined(categoria))
            throw new DomainValidationException(nameof(Categoria), "Categoria de custo desconhecida.");
        (Id, Data, TarefaId, Categoria, Descricao, Documento, Valor) =
            (id, data, tarefaId, categoria, texto, doc, Math.Round(valor, 2, MidpointRounding.AwayFromZero));
    }

    public Guid Id { get; }

    public DateOnly Data { get; }

    public Guid? TarefaId { get; }

    public CategoriaDeCusto Categoria { get; }

    public string Descricao { get; }

    /// <summary>"NF 1234", "Folha 10/2026"; nulo sem documento.</summary>
    public string? Documento { get; }

    public decimal Valor { get; }
}

/// <summary>
/// Bloco 37: os custos realizados da obra, na ordem da data (os do mesmo dia, na ordem em que entraram). Volta no Desfazer,
/// como as tarefas.
/// </summary>
public sealed class CustosRealizados
{
    private readonly List<LancamentoDeCusto> _lancamentos = [];

    public IReadOnlyList<LancamentoDeCusto> Lancamentos => [.. _lancamentos.OrderBy(l => l.Data)];

    public bool Vazio => _lancamentos.Count == 0;

    public decimal Total => _lancamentos.Sum(l => l.Valor);

    /// <summary>O custo realizado até a data (inclusive).</summary>
    public decimal Ate(DateOnly data) => _lancamentos.Where(l => l.Data <= data).Sum(l => l.Valor);

    /// <exception cref="DomainValidationException">O lançamento já está na lista.</exception>
    public void Adicionar(LancamentoDeCusto lancamento)
    {
        ArgumentNullException.ThrowIfNull(lancamento);
        if (_lancamentos.Exists(l => l.Id == lancamento.Id))
            throw new DomainValidationException(nameof(Lancamentos), "O lançamento já está na lista.");
        _lancamentos.Add(lancamento);
    }

    /// <summary>Troca o lançamento de mesmo Id pela versão alterada.</summary>
    /// <exception cref="DomainValidationException">O lançamento não está na lista.</exception>
    public void Substituir(LancamentoDeCusto lancamento)
    {
        ArgumentNullException.ThrowIfNull(lancamento);
        var indice = _lancamentos.FindIndex(l => l.Id == lancamento.Id);
        if (indice < 0)
            throw new DomainValidationException(nameof(Lancamentos), "O lançamento não está na lista.");
        _lancamentos[indice] = lancamento;
    }

    /// <exception cref="DomainValidationException">O lançamento não está na lista.</exception>
    public void Remover(Guid id)
    {
        if (_lancamentos.RemoveAll(l => l.Id == id) == 0)
            throw new DomainValidationException(nameof(Lancamentos), "O lançamento não está na lista.");
    }

    internal CustosRealizados Copiar()
    {
        var copia = new CustosRealizados();
        copia._lancamentos.AddRange(_lancamentos); // lançamentos são imutáveis
        return copia;
    }

    /// <summary>Os lançamentos lidos do arquivo.</summary>
    /// <exception cref="DomainValidationException">Um Id repetido.</exception>
    internal void Restaurar(IEnumerable<LancamentoDeCusto> lancamentos)
    {
        _lancamentos.Clear();
        foreach (var lancamento in lancamentos)
            Adicionar(lancamento);
    }
}
