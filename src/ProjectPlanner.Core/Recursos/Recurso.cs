using System.Globalization;
using System.Text;

namespace ProjectPlanner.Core.Recursos;

/// <summary>Bloco 32: o tipo de um recurso, que decide como ele é atribuído e custeado.</summary>
public enum TipoDeRecurso
{
    /// <summary>Pedreiro, servente, eletricista: atribuído por dia ("2 pedreiros"), custo por dia.</summary>
    Profissional,

    /// <summary>Cimento, areia, tijolo: atribuído pelo total na tarefa ("120 sc"), custo por unidade.</summary>
    Material,

    /// <summary>Betoneira, andaime: atribuído por dia ("1 betoneira"), custo por dia.</summary>
    Equipamento,
}

/// <summary>Os nomes dos tipos, como aparecem e como são lidos de uma planilha.</summary>
public static class TiposDeRecurso
{
    public static IReadOnlyList<TipoDeRecurso> Todos { get; } = Enum.GetValues<TipoDeRecurso>();

    public static string Texto(TipoDeRecurso tipo) => tipo switch
    {
        TipoDeRecurso.Profissional => "Profissional",
        TipoDeRecurso.Material => "Material",
        _ => "Equipamento",
    };

    /// <summary>Profissionais e equipamentos são atribuídos por dia; materiais, pelo total.</summary>
    public static bool PorDia(TipoDeRecurso tipo) => tipo != TipoDeRecurso.Material;

    /// <summary>"Profissional", "mão de obra", "materiais", "equip."…; nulo se não reconhece.</summary>
    public static TipoDeRecurso? Ler(string? texto)
    {
        var t = Normalizar(texto);
        if (t.StartsWith("prof") || t.StartsWith("mao de obra") || t == "mo" || t.StartsWith("pessoal"))
            return TipoDeRecurso.Profissional;
        if (t.StartsWith("materia") || t.StartsWith("insumo"))
            return TipoDeRecurso.Material;
        if (t.StartsWith("equip") || t.StartsWith("maquina"))
            return TipoDeRecurso.Equipamento;
        return null;
    }

    internal static string Normalizar(string? texto)
    {
        var decomposto = (texto ?? "").Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var limpo = new StringBuilder();
        foreach (var c in decomposto)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                limpo.Append(c);
        return limpo.ToString().TrimEnd('.', ':').Trim();
    }
}

/// <summary>
/// Bloco 32: um recurso do projeto (ou do catálogo pessoal): o nome, o tipo, a unidade, o custo unitário, quanto há por dia
/// (equipes e equipamentos, para o alerta de superalocação) e o fornecedor. Profissionais e equipamentos custam por dia; a
/// unidade deles é "dia".
/// </summary>
public sealed record Recurso
{
    public const int TamanhoMaximoDoNome = 80;
    public const int TamanhoMaximoDaUnidade = 12;
    public const int TamanhoMaximoDoFornecedor = 80;
    public const string UnidadeDosPorDia = "dia";

    /// <exception cref="DomainValidationException">Id vazio, sem nome, textos longos demais, custo negativo, disponível zero ou negativo.</exception>
    public Recurso(Guid id, string? nome, TipoDeRecurso tipo, string? unidade, decimal custoUnitario, decimal? disponivel = null, string? fornecedor = null)
    {
        if (id == Guid.Empty)
            throw new DomainValidationException(nameof(Id), "O id do recurso é obrigatório.");
        var texto = nome?.Trim();
        if (string.IsNullOrEmpty(texto))
            throw new DomainValidationException(nameof(Nome), "Escreva o nome do recurso.");
        if (texto.Length > TamanhoMaximoDoNome)
            throw new DomainValidationException(nameof(Nome), $"O nome tem {texto.Length} caracteres; o máximo é {TamanhoMaximoDoNome}.");
        if (!Enum.IsDefined(tipo))
            throw new DomainValidationException(nameof(Tipo), "Tipo de recurso desconhecido.");
        var und = TiposDeRecurso.PorDia(tipo) ? UnidadeDosPorDia : string.IsNullOrWhiteSpace(unidade) ? "un" : unidade.Trim();
        if (und.Length > TamanhoMaximoDaUnidade)
            throw new DomainValidationException(nameof(Unidade), $"A unidade tem {und.Length} caracteres; o máximo é {TamanhoMaximoDaUnidade}.");
        if (custoUnitario < 0)
            throw new DomainValidationException(nameof(CustoUnitario), "O custo unitário não pode ser negativo.");
        if (custoUnitario > Tarefa.ValorMaximo)
            throw new DomainValidationException(nameof(CustoUnitario), "O custo unitário passa do máximo aceito.");
        if (disponivel is <= 0)
            throw new DomainValidationException(nameof(Disponivel), "O disponível por dia precisa ser maior que zero (deixe vazio para sem limite).");
        var forn = string.IsNullOrWhiteSpace(fornecedor) ? null : fornecedor.Trim();
        if (forn?.Length > TamanhoMaximoDoFornecedor)
            throw new DomainValidationException(nameof(Fornecedor), $"O fornecedor tem {forn.Length} caracteres; o máximo é {TamanhoMaximoDoFornecedor}.");
        (Id, Nome, Tipo, Unidade, CustoUnitario, Disponivel, Fornecedor) =
            (id, texto, tipo, und, Math.Round(custoUnitario, 2, MidpointRounding.AwayFromZero), TiposDeRecurso.PorDia(tipo) ? disponivel : null, forn);
    }

    public Guid Id { get; }

    public string Nome { get; }

    public TipoDeRecurso Tipo { get; }

    /// <summary>"dia" (profissionais e equipamentos) ou a unidade do material ("sc", "m³").</summary>
    public string Unidade { get; }

    /// <summary>O custo de uma unidade (de um dia, num profissional ou equipamento).</summary>
    public decimal CustoUnitario { get; }

    /// <summary>Quantos há por dia (equipe ou equipamentos); nulo sem limite (e sempre nos materiais).</summary>
    public decimal? Disponivel { get; }

    public string? Fornecedor { get; }

    public bool PorDia => TiposDeRecurso.PorDia(Tipo);

    /// <summary>Uma cópia com outro Id (para trazer do catálogo sem misturar os dois).</summary>
    public Recurso ComId(Guid id) => new(id, Nome, Tipo, Unidade, CustoUnitario, Disponivel, Fornecedor);
}

/// <summary>
/// Bloco 32: um recurso numa tarefa: num profissional ou equipamento, quantos por dia (o total é × os dias úteis da
/// tarefa); num material, o total na tarefa.
/// </summary>
public sealed record AtribuicaoDeRecurso
{
    /// <exception cref="DomainValidationException">Quantidade zero, negativa ou grande demais.</exception>
    public AtribuicaoDeRecurso(Guid recursoId, decimal quantidade)
    {
        if (quantidade <= 0)
            throw new DomainValidationException(nameof(Quantidade), "A quantidade do recurso precisa ser maior que zero.");
        if (quantidade > Tarefa.QuantidadeMaxima)
            throw new DomainValidationException(nameof(Quantidade), "A quantidade do recurso passa do máximo aceito.");
        (RecursoId, Quantidade) = (recursoId, Math.Round(quantidade, 4, MidpointRounding.AwayFromZero));
    }

    public Guid RecursoId { get; }

    /// <summary>Por dia (profissional, equipamento) ou o total (material).</summary>
    public decimal Quantidade { get; }
}
