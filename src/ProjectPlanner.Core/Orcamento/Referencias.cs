using System.Globalization;
using System.Text;
using ProjectPlanner.Core.Recursos;

namespace ProjectPlanner.Core.Orcamento;

/// <summary>Bloco 33: o tipo de um insumo de uma base de preços.</summary>
public enum TipoDeInsumo
{
    Material,
    MaoDeObra,
    Equipamento,

    /// <summary>Serviço de terceiros, taxa ou outro insumo que não é material, mão de obra nem equipamento.</summary>
    Servico,
}

/// <summary>Os nomes dos tipos de insumo e como eles viram recursos.</summary>
public static class TiposDeInsumo
{
    public static string Texto(TipoDeInsumo tipo) => tipo switch
    {
        TipoDeInsumo.MaoDeObra => "Mão de obra",
        TipoDeInsumo.Equipamento => "Equipamento",
        TipoDeInsumo.Servico => "Serviço",
        _ => "Material",
    };

    /// <summary>Mão de obra vira profissional; equipamento, equipamento; material e serviço, material (pelo total).</summary>
    public static TipoDeRecurso Recurso(TipoDeInsumo tipo) => tipo switch
    {
        TipoDeInsumo.MaoDeObra => TipoDeRecurso.Profissional,
        TipoDeInsumo.Equipamento => TipoDeRecurso.Equipamento,
        _ => TipoDeRecurso.Material,
    };

    /// <summary>
    /// O tipo pela classe da base ("MÃO DE OBRA", "EQUIPAMENTO", "MATERIAL", "SERVIÇOS") ou, sem ela, pela unidade: "H" é mão de
    /// obra, "CHP"/"CHI" (custo horário produtivo/improdutivo) é equipamento; o resto, material.
    /// </summary>
    public static TipoDeInsumo Inferir(string? classe, string? unidade)
    {
        var c = Normalizar(classe);
        if (c.Contains("mao de obra") || c.StartsWith("mo"))
            return TipoDeInsumo.MaoDeObra;
        if (c.Contains("equipamento"))
            return TipoDeInsumo.Equipamento;
        if (c.Contains("servico") || c.Contains("taxa"))
            return TipoDeInsumo.Servico;
        if (c.Contains("material"))
            return TipoDeInsumo.Material;
        return Normalizar(unidade) switch
        {
            "h" => TipoDeInsumo.MaoDeObra,
            "chp" or "chi" => TipoDeInsumo.Equipamento,
            _ => TipoDeInsumo.Material,
        };
    }

    internal static string Normalizar(string? texto)
    {
        var decomposto = (texto ?? "").Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var limpo = new StringBuilder();
        foreach (var ch in decomposto)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                limpo.Append(ch);
        return limpo.ToString().Trim();
    }
}

/// <summary>
/// Bloco 33: de onde vêm os preços: a fonte (SINAPI hoje; SICRO, ORSE ou uma base própria depois), o estado, o mês de
/// referência e se a mão de obra é desonerada.
/// </summary>
public sealed record BaseDeReferencia
{
    public const int TamanhoMaximoDaFonte = 40;

    /// <exception cref="DomainValidationException">Sem fonte, ou fonte longa demais.</exception>
    public BaseDeReferencia(string? fonte, string? uf = null, string? referencia = null, bool? desonerado = null)
    {
        var f = fonte?.Trim();
        if (string.IsNullOrEmpty(f))
            throw new DomainValidationException(nameof(Fonte), "Informe a fonte da base (por exemplo, SINAPI).");
        if (f.Length > TamanhoMaximoDaFonte)
            throw new DomainValidationException(nameof(Fonte), $"A fonte tem {f.Length} caracteres; o máximo é {TamanhoMaximoDaFonte}.");
        (Fonte, Uf, Referencia, Desonerado) = (f, string.IsNullOrWhiteSpace(uf) ? null : uf.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(referencia) ? null : referencia.Trim(), desonerado);
    }

    public string Fonte { get; }

    public string? Uf { get; }

    /// <summary>"09/2026".</summary>
    public string? Referencia { get; }

    public bool? Desonerado { get; }

    /// <summary>"SINAPI SP 09/2026 não desonerado".</summary>
    public string Descricao => string.Join(" ", new[]
    {
        Fonte, Uf, Referencia, Desonerado switch { true => "desonerado", false => "não desonerado", _ => null },
    }.OfType<string>());

    /// <summary>Um nome de arquivo para a base ("SINAPI-SP-09-2026-ND").</summary>
    public string Chave => string.Join("-", new[] { Fonte, Uf, Referencia?.Replace('/', '-'), Desonerado switch { true => "D", false => "ND", _ => null } }
        .OfType<string>()).Replace(' ', '_');

    /// <summary>Lê o estado, o mês e a desoneração do nome de um arquivo da Caixa ("SINAPI_..._SP_202609_NaoDesonerado.xlsx").</summary>
    public static BaseDeReferencia DoNomeDoArquivo(string arquivo)
    {
        var nome = Path.GetFileNameWithoutExtension(arquivo ?? "");
        var partes = nome.Split(['_', '-', ' ', '.'], StringSplitOptions.RemoveEmptyEntries);
        string? uf = null, referencia = null;
        bool? desonerado = null;
        foreach (var parte in partes)
        {
            var p = TiposDeInsumo.Normalizar(parte);
            if (parte.Length == 2 && Ufs.Contains(parte.ToUpperInvariant()))
                uf = parte.ToUpperInvariant();
            else if (parte.Length == 6 && parte.All(char.IsDigit) && int.Parse(parte[4..], CultureInfo.InvariantCulture) is >= 1 and <= 12)
                referencia = $"{parte[4..]}/{parte[..4]}";
            else if (p.Contains("naodesonerad") || p == "nd")
                desonerado = false;
            else if (p.Contains("desonerad"))
                desonerado = true;
        }
        var fonte = partes.FirstOrDefault(p => TiposDeInsumo.Normalizar(p) is "sinapi" or "sicro" or "orse")?.ToUpperInvariant() ?? "SINAPI";
        return new BaseDeReferencia(fonte, uf, referencia, desonerado);
    }

    private static readonly HashSet<string> Ufs =
        ["AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA", "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO"];
}

/// <summary>Bloco 33: um insumo de uma base (o preço é o da base: com os encargos, na mão de obra).</summary>
public sealed record Insumo(string Codigo, string Descricao, string Unidade, TipoDeInsumo Tipo, decimal Preco);

/// <summary>Bloco 33: um item de uma composição: um insumo ou outra composição (auxiliar), com o coeficiente por unidade.</summary>
public sealed record ItemDeComposicao(bool EhComposicao, string Codigo, decimal Coeficiente);

/// <summary>Bloco 33: uma composição de custo unitário de uma base ("1 m² de alvenaria = 0,8 h de pedreiro + …").</summary>
public sealed record Composicao(string Codigo, string Descricao, string Unidade, IReadOnlyList<ItemDeComposicao> Itens);

/// <summary>Bloco 33: um insumo numa composição aberta: o coeficiente total por unidade do serviço (somando as auxiliares) e o preço.</summary>
public sealed record InsumoNaComposicao(string Codigo, string Descricao, string Unidade, TipoDeInsumo Tipo, decimal Coeficiente, decimal Preco)
{
    public decimal Parcial => Coeficiente * Preco;
}

/// <summary>
/// Bloco 33: uma composição aberta até os insumos (as auxiliares multiplicadas pelos coeficientes), com os preços da base:
/// é o que o orçamento guarda em cada tarefa (mudar a base depois não muda o orçamento até atualizar os preços).
/// </summary>
public sealed record ComposicaoAberta(BaseDeReferencia Base, string Codigo, string Descricao, string Unidade, IReadOnlyList<InsumoNaComposicao> Insumos)
{
    /// <summary>Bloco 38: a fonte dos preços estimados pela IA (sem composição numa base).</summary>
    public const string FonteDaEstimativa = "Estimativa IA";

    public const int TamanhoMaximoDaDescricao = 300;

    /// <summary>Bloco 38: um preço estimado pela IA, e não de uma base: não vira recursos nem muda com outra base.</summary>
    public bool EhEstimativa => Base.Fonte == FonteDaEstimativa;

    /// <summary>
    /// Bloco 38: um serviço com o custo unitário estimado pela IA, em duas partes (materiais; mão de obra e equipamentos)
    /// para o BDI diferenciado valer.
    /// </summary>
    /// <exception cref="DomainValidationException">Sem descrição ou unidade, custo negativo ou zero.</exception>
    public static ComposicaoAberta Estimativa(string? descricao, string? unidade, decimal custoDeMateriais, decimal custoDeMaoDeObra)
    {
        var d = descricao?.Trim();
        var u = unidade?.Trim();
        if (string.IsNullOrEmpty(d))
            throw new DomainValidationException(nameof(Descricao), "A estimativa precisa de uma descrição do serviço.");
        if (string.IsNullOrEmpty(u))
            throw new DomainValidationException(nameof(Unidade), "A estimativa precisa da unidade do serviço.");
        if (custoDeMateriais < 0 || custoDeMaoDeObra < 0 || custoDeMateriais + custoDeMaoDeObra <= 0)
            throw new DomainValidationException(nameof(Insumos), "O custo estimado precisa ser maior que zero (e nenhuma parte negativa).");
        if (d.Length > TamanhoMaximoDaDescricao)
            d = d[..(TamanhoMaximoDaDescricao - 1)].TrimEnd() + "…";
        var insumos = new List<InsumoNaComposicao>();
        if (custoDeMateriais > 0)
            insumos.Add(new InsumoNaComposicao("IA-MAT", "Materiais (estimativa da IA)", u, TipoDeInsumo.Material, 1, Math.Round(custoDeMateriais, 2, MidpointRounding.AwayFromZero)));
        if (custoDeMaoDeObra > 0)
            insumos.Add(new InsumoNaComposicao("IA-MO", "Mão de obra e equipamentos (estimativa da IA)", u, TipoDeInsumo.MaoDeObra, 1,
                Math.Round(custoDeMaoDeObra, 2, MidpointRounding.AwayFromZero)));
        return new ComposicaoAberta(new BaseDeReferencia(FonteDaEstimativa), "IA", d, u, insumos);
    }

    /// <summary>O custo direto de uma unidade do serviço.</summary>
    public decimal CustoUnitario => Math.Round(Insumos.Sum(i => i.Parcial), 2, MidpointRounding.AwayFromZero);

    /// <summary>A parte de materiais e serviços (para o BDI de materiais).</summary>
    public decimal CustoDeMateriais => Insumos.Where(i => i.Tipo is TipoDeInsumo.Material or TipoDeInsumo.Servico).Sum(i => i.Parcial);

    /// <summary>A parte de mão de obra e equipamentos.</summary>
    public decimal CustoDeMaoDeObraEEquipamentos => Insumos.Where(i => i.Tipo is TipoDeInsumo.MaoDeObra or TipoDeInsumo.Equipamento).Sum(i => i.Parcial);
}

/// <summary>
/// Bloco 33: uma base de preços carregada: os insumos e as composições, por código. Abre uma composição até os insumos
/// (recusa um ciclo entre auxiliares e um item que não está na base) e busca composições pelas palavras da descrição.
/// </summary>
public sealed class TabelaDeReferencia
{
    private readonly Dictionary<string, Insumo> _insumos;
    private readonly Dictionary<string, Composicao> _composicoes;

    public TabelaDeReferencia(BaseDeReferencia @base, IEnumerable<Insumo> insumos, IEnumerable<Composicao> composicoes)
    {
        ArgumentNullException.ThrowIfNull(@base);
        Base = @base;
        _insumos = [];
        foreach (var insumo in insumos)
            _insumos[insumo.Codigo] = insumo;
        _composicoes = [];
        foreach (var composicao in composicoes)
            _composicoes[composicao.Codigo] = composicao;
    }

    public BaseDeReferencia Base { get; }

    public IReadOnlyCollection<Insumo> Insumos => _insumos.Values;

    public IReadOnlyCollection<Composicao> Composicoes => _composicoes.Values;

    public Insumo? Insumo(string codigo) => _insumos.GetValueOrDefault(codigo);

    public Composicao? Composicao(string codigo) => _composicoes.GetValueOrDefault(codigo);

    /// <summary>A composição aberta até os insumos.</summary>
    /// <exception cref="DomainValidationException">Composição que não está na base, item faltando ou ciclo entre auxiliares.</exception>
    public ComposicaoAberta Abrir(string codigo)
    {
        var composicao = Composicao(codigo) ?? throw new DomainValidationException(nameof(codigo), $"A composição {codigo} não está na base {Base.Descricao}.");
        var soma = new Dictionary<string, decimal>();
        Acumular(composicao, 1, soma, []);
        return new ComposicaoAberta(Base, composicao.Codigo, composicao.Descricao, composicao.Unidade,
            [.. soma.Select(p => _insumos[p.Key]).Select(i => new InsumoNaComposicao(i.Codigo, i.Descricao, i.Unidade, i.Tipo, Math.Round(soma[i.Codigo], 7), i.Preco))
                .OrderBy(i => i.Tipo).ThenByDescending(i => i.Parcial)]);
    }

    private void Acumular(Composicao composicao, decimal fator, Dictionary<string, decimal> soma, HashSet<string> caminho)
    {
        if (!caminho.Add(composicao.Codigo))
            throw new DomainValidationException(nameof(Composicoes), $"A composição {composicao.Codigo} usa a si mesma (ciclo entre composições auxiliares).");
        foreach (var item in composicao.Itens)
        {
            if (item.EhComposicao)
            {
                var auxiliar = Composicao(item.Codigo)
                               ?? throw new DomainValidationException(nameof(Composicoes), $"A composição {composicao.Codigo} usa a {item.Codigo}, que não está na base.");
                Acumular(auxiliar, fator * item.Coeficiente, soma, caminho);
            }
            else
            {
                if (!_insumos.ContainsKey(item.Codigo))
                    throw new DomainValidationException(nameof(Insumos), $"A composição {composicao.Codigo} usa o insumo {item.Codigo}, que não está na base.");
                soma[item.Codigo] = soma.GetValueOrDefault(item.Codigo) + fator * item.Coeficiente;
            }
        }
        caminho.Remove(composicao.Codigo);
    }

    /// <summary>As composições com todas as palavras (ou o código) do texto, as mais curtas primeiro.</summary>
    private static readonly HashSet<string> PalavrasVazias =
    [
        "de", "do", "da", "dos", "das", "e", "em", "no", "na", "nos", "nas", "com", "para", "por", "a", "o", "as", "os", "um", "uma",
        "ao", "aos", "ou", "sem", "sobre", "entre", "ate", "etapa", "servico", "servicos", "geral", "obra",
    ];

    // As palavras de um nome de tarefa para buscar: sem acentos, sem as vazias, com 3 letras ou mais; as longas pelo radical
    // ("alvenarias" acha "alvenaria").
    private static IReadOnlyList<string> Radicais(string? texto) =>
    [
        .. new string([.. TiposDeInsumo.Normalizar(texto).Select(c => char.IsLetterOrDigit(c) ? c : ' ')])
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.Length >= 3 && !PalavrasVazias.Contains(p))
            .Select(p => p.Length > 6 ? p[..6] : p)
            .Distinct(),
    ];

    /// <summary>
    /// Bloco 38: as composições candidatas para uma tarefa, para a IA escolher: as que mais têm as palavras do nome da tarefa
    /// (as do resumo onde ela está valem a metade; a que começa pela primeira palavra ganha um ponto), das mais parecidas
    /// para as menos.
    /// </summary>
    public IReadOnlyList<Composicao> Candidatas(string? nome, string? contexto = null, int maximo = 12)
    {
        var proprias = Radicais(nome);
        var doContexto = Radicais(contexto).Except(proprias).ToList();
        if (proprias.Count == 0 && doContexto.Count == 0)
            return [];
        return [.. _composicoes.Values
            .Select(c =>
            {
                var descricao = " " + new string([.. TiposDeInsumo.Normalizar(c.Descricao).Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ')]);
                var pontos = proprias.Count(p => descricao.Contains(" " + p, StringComparison.Ordinal)) * 2 +
                             doContexto.Count(p => descricao.Contains(" " + p, StringComparison.Ordinal));
                if (pontos >= 2 && proprias.Count > 0 && descricao.StartsWith(" " + proprias[0], StringComparison.Ordinal))
                    pontos++; // o serviço principal da composição é a primeira palavra da tarefa ("Alvenaria…")
                return (Composicao: c, Pontos: pontos);
            })
            .Where(c => c.Pontos >= 2)
            .OrderByDescending(c => c.Pontos).ThenBy(c => c.Composicao.Descricao.Length).ThenBy(c => c.Composicao.Codigo, StringComparer.Ordinal)
            .Take(maximo)
            .Select(c => c.Composicao)];
    }

    public IReadOnlyList<Composicao> Buscar(string? texto, int maximo = 100)
    {
        var palavras = TiposDeInsumo.Normalizar(texto).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (palavras.Length == 0)
            return [];
        return [.. _composicoes.Values
            .Where(c => c.Codigo == texto!.Trim() || palavras.All(p => TiposDeInsumo.Normalizar(c.Descricao).Contains(p)))
            .OrderBy(c => c.Codigo == texto!.Trim() ? 0 : 1).ThenBy(c => c.Descricao.Length).Take(maximo)];
    }
}
