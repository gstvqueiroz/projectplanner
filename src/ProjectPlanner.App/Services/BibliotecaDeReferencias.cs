using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Orcamento;

namespace ProjectPlanner.App.Services;

/// <summary>Uma base guardada na biblioteca: a descrição e quantos insumos e composições ela tem.</summary>
public sealed record BaseNaBiblioteca(BaseDeReferencia Base, int Insumos, int Composicoes);

/// <summary>
/// Bloco 33: as bases de preços importadas (SINAPI de cada estado e mês; outras fontes no futuro), fora dos projetos: o
/// orçamento de cada projeto guarda as composições que usa, então trocar ou remover uma base não muda orçamentos prontos.
/// </summary>
public interface IBibliotecaDeReferencias
{
    IReadOnlyList<BaseNaBiblioteca> Listar();

    /// <summary>A base inteira; nula se não está na biblioteca.</summary>
    TabelaDeReferencia? Carregar(BaseDeReferencia @base);

    /// <exception cref="IOException">Não deu para gravar.</exception>
    void Salvar(TabelaDeReferencia tabela);

    void Remover(BaseDeReferencia @base);
}

/// <summary>Na memória (testes).</summary>
public sealed class BibliotecaDeReferenciasEmMemoria : IBibliotecaDeReferencias
{
    private readonly Dictionary<string, TabelaDeReferencia> _tabelas = [];

    public IReadOnlyList<BaseNaBiblioteca> Listar() => [.. _tabelas.Values.Select(t => new BaseNaBiblioteca(t.Base, t.Insumos.Count, t.Composicoes.Count))];

    public TabelaDeReferencia? Carregar(BaseDeReferencia @base) => _tabelas.GetValueOrDefault(@base.Chave);

    public void Salvar(TabelaDeReferencia tabela) => _tabelas[tabela.Base.Chave] = tabela;

    public void Remover(BaseDeReferencia @base) => _tabelas.Remove(@base.Chave);
}

/// <summary>Cada base num arquivo JSON da pasta referencias (ao lado de colunas.json).</summary>
public sealed class ArquivoBibliotecaDeReferencias(string pasta) : IBibliotecaDeReferencias
{
    private const int VersaoDoFormato = 1;

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public ArquivoBibliotecaDeReferencias() : this(PastaPadrao())
    {
    }

    public static string PastaPadrao() => Path.Combine(Path.GetDirectoryName(ArquivoPreferenciasColunas.CaminhoPadrao())!, "referencias");

    public IReadOnlyList<BaseNaBiblioteca> Listar()
    {
        if (!Directory.Exists(pasta))
            return [];
        var lista = new List<BaseNaBiblioteca>();
        foreach (var arquivo in Directory.GetFiles(pasta, "*.json").Order())
            if (Ler(arquivo) is { } t)
                lista.Add(new BaseNaBiblioteca(t.Base, t.Insumos.Count, t.Composicoes.Count));
        return lista;
    }

    public TabelaDeReferencia? Carregar(BaseDeReferencia @base) => Ler(Caminho(@base));

    public void Salvar(TabelaDeReferencia tabela)
    {
        var destino = Caminho(tabela.Base);
        var temporario = destino + ".tmp";
        try
        {
            Directory.CreateDirectory(pasta);
            var b = tabela.Base;
            using (var fluxo = File.Create(temporario))
                JsonSerializer.Serialize(fluxo, new Arquivo(VersaoDoFormato, b.Fonte, b.Uf, b.Referencia, b.Desonerado, [.. tabela.Insumos],
                    [.. tabela.Composicoes.Select(c => new ComposicaoGravada(c.Codigo, c.Descricao, c.Unidade, [.. c.Itens]))]), Opcoes);
            File.Move(temporario, destino, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporario); } catch (Exception) { /* nada a fazer */ }
            throw new IOException($"Não foi possível guardar a base {tabela.Base.Descricao}: {ex.Message}", ex);
        }
    }

    public void Remover(BaseDeReferencia @base)
    {
        try
        {
            File.Delete(Caminho(@base));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fica para a próxima.
        }
    }

    private string Caminho(BaseDeReferencia @base) =>
        Path.Combine(pasta, string.Concat(@base.Chave.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + ".json");

    private static TabelaDeReferencia? Ler(string arquivo)
    {
        try
        {
            if (!File.Exists(arquivo))
                return null;
            using var fluxo = File.OpenRead(arquivo);
            var a = JsonSerializer.Deserialize<Arquivo>(fluxo, Opcoes);
            if (a is not { Versao: VersaoDoFormato })
                return null;
            return new TabelaDeReferencia(new BaseDeReferencia(a.Fonte, a.Uf, a.Referencia, a.Desonerado), a.Insumos ?? [],
                (a.Composicoes ?? []).Select(c => new Composicao(c.Codigo, c.Descricao, c.Unidade, c.Itens ?? [])));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or DomainValidationException)
        {
            return null;
        }
    }

    private sealed record Arquivo(int Versao, string? Fonte, string? Uf, string? Referencia, bool? Desonerado, List<Insumo>? Insumos, List<ComposicaoGravada>? Composicoes);

    private sealed record ComposicaoGravada(string Codigo, string Descricao, string Unidade, List<ItemDeComposicao>? Itens);
}
