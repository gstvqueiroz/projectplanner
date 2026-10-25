using System.Globalization;
using System.IO.Compression;
using ProjectPlanner.Core.Orcamento;
using ProjectPlanner.Relatorios;

namespace ProjectPlanner.App.Services;

/// <summary>Bloco 40: um arquivo mensal da SINAPI no site da Caixa (o ZIP com as planilhas de todos os estados).</summary>
public sealed record ArquivoDoSinapi(Uri Endereco, int Ano, int Mes)
{
    /// <summary>"09/2026".</summary>
    public string Referencia => $"{Mes:00}/{Ano}";

    public string Nome => Path.GetFileName(Endereco.LocalPath);
}

/// <summary>Bloco 40: acha e baixa a SINAPI no site da Caixa.</summary>
public interface ISinapiDaCaixa
{
    /// <summary>O arquivo mais recente publicado (a Caixa publica o mês por volta do dia 10 do mês seguinte); nulo se não achou.</summary>
    Task<ArquivoDoSinapi?> ProcurarAsync(DateOnly hoje, CancellationToken c);

    /// <summary>Baixa o ZIP e tira dele só a planilha de referência (insumos e composições), na pasta; devolve o caminho dela.</summary>
    /// <exception cref="HttpRequestException">Sem conexão, ou a Caixa recusou.</exception>
    /// <exception cref="InvalidDataException">O arquivo não é o que se esperava.</exception>
    Task<string> BaixarAsync(ArquivoDoSinapi arquivo, string pasta, CancellationToken c);
}

/// <summary>
/// Bloco 40 (decisão do usuário: pode baixar). A Caixa publica, desde 2025, um ZIP por mês com as planilhas de todos os
/// estados, em links diretos (<see cref="Pasta"/>SINAPI-AAAA-MM-formato-xlsx.zip, com "_Retificacao01" quando corrigido). O
/// programa procura do mês atual para trás, baixa o ZIP (uns 16 MB) e tira só a planilha "SINAPI_Referência".
/// </summary>
public sealed class SinapiDaCaixa(HttpClient http) : ISinapiDaCaixa
{
    public const string Pasta = "https://www.caixa.gov.br/Downloads/sinapi-relatorios-mensais/";
    public const int MesesParaTras = 6;
    public const long TamanhoMaximo = 200L * 1024 * 1024;

    /// <summary>Retificações primeiro (a mais nova vale), depois o arquivo original.</summary>
    public static IReadOnlyList<string> Sufixos { get; } = ["_Retificacao03", "_Retificacao02", "_Retificacao01", ""];

    private static readonly Lazy<SinapiDaCaixa> OPadrao = new(() =>
    {
        var cliente = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        cliente.DefaultRequestHeaders.UserAgent.ParseAdd("ProjectPlanner");
        return new SinapiDaCaixa(cliente);
    });

    public static SinapiDaCaixa Padrao => OPadrao.Value;

    /// <summary>Os endereços a tentar, do mês de <paramref name="hoje"/> para trás.</summary>
    public static IEnumerable<ArquivoDoSinapi> Candidatos(DateOnly hoje)
    {
        for (var i = 0; i < MesesParaTras; i++)
        {
            var mes = hoje.AddMonths(-i);
            foreach (var sufixo in Sufixos)
                yield return new ArquivoDoSinapi(new Uri($"{Pasta}SINAPI-{mes.Year:0000}-{mes.Month:00}-formato-xlsx{sufixo}.zip"), mes.Year, mes.Month);
        }
    }

    public async Task<ArquivoDoSinapi?> ProcurarAsync(DateOnly hoje, CancellationToken c)
    {
        foreach (var candidato in Candidatos(hoje))
        {
            using var pedido = new HttpRequestMessage(HttpMethod.Head, candidato.Endereco);
            using var resposta = await http.SendAsync(pedido, c);
            if (resposta.IsSuccessStatusCode)
                return candidato;
        }
        return null;
    }

    public async Task<string> BaixarAsync(ArquivoDoSinapi arquivo, string pasta, CancellationToken c)
    {
        Directory.CreateDirectory(pasta);
        var zip = Path.Combine(pasta, "sinapi.zip");
        using (var resposta = await http.GetAsync(arquivo.Endereco, HttpCompletionOption.ResponseHeadersRead, c))
        {
            resposta.EnsureSuccessStatusCode();
            if (resposta.Content.Headers.ContentLength > TamanhoMaximo)
                throw new InvalidDataException("O arquivo da Caixa é maior que o esperado.");
            await using var origem = await resposta.Content.ReadAsStreamAsync(c);
            await using var destino = File.Create(zip);
            var buffer = new byte[81920];
            long total = 0;
            int lidos;
            while ((lidos = await origem.ReadAsync(buffer, c)) > 0)
            {
                total += lidos;
                if (total > TamanhoMaximo)
                    throw new InvalidDataException("O arquivo da Caixa é maior que o esperado.");
                await destino.WriteAsync(buffer.AsMemory(0, lidos), c);
            }
        }
        ZipArchive pacote;
        try
        {
            pacote = ZipFile.OpenRead(zip);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException("O arquivo da Caixa veio corrompido. Tente de novo.", ex);
        }
        using (pacote)
        {
            var entrada = pacote.Entries.FirstOrDefault(e => EhAReferencia(e.Name))
                          ?? throw new InvalidDataException("O arquivo da Caixa não tem a planilha de referência (SINAPI_Referência).");
            if (entrada.Length > TamanhoMaximo)
                throw new InvalidDataException("A planilha da Caixa é maior que o esperado.");
            var planilha = Path.Combine(pasta, "SINAPI_Referencia.xlsx"); // o nome é nosso: nada do ZIP decide o caminho
            entrada.ExtractToFile(planilha, overwrite: true);
            return planilha;
        }
    }

    // "SINAPI_Referência_2026_09.xlsx" (com ou sem acento, como o ZIP gravar o nome).
    private static bool EhAReferencia(string nome)
    {
        var simples = new string(nome.Normalize(System.Text.NormalizationForm.FormD)
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();
        return simples.StartsWith("sinapi_refer", StringComparison.Ordinal) && simples.EndsWith(".xlsx", StringComparison.Ordinal);
    }
}

/// <summary>
/// Bloco 40: a base de um estado a partir da planilha de referência da SINAPI (formato de 2025 em diante): os insumos da aba
/// do regime (ISD sem desoneração, ICD com desoneração, ISE sem encargos), com o preço da coluna do estado, e as composições da
/// aba Analítico. As composições que a própria Caixa marca "SEM CUSTO" (um insumo sem preço) ficam na base, mas não abrem.
/// </summary>
public static class ImportacaoDoSinapi
{
    /// <exception cref="InvalidDataException">A planilha não é a de referência da SINAPI.</exception>
    /// <exception cref="Core.DomainValidationException">As abas não têm as colunas esperadas (ou o estado).</exception>
    public static (TabelaDeReferencia Tabela, int Avisos) Importar(string planilha, BaseDeReferencia @base)
    {
        ArgumentNullException.ThrowIfNull(@base);
        if (@base.Uf is not { } uf || !BaseDeReferencia.Ufs.Contains(uf))
            throw new Core.DomainValidationException(nameof(@base.Uf), "Informe a UF do estado (por exemplo, SP).");
        var aba = @base.Desonerado switch
        {
            true => "ICD",
            false => "ISD",
            null => "ISE",
        };
        var insumos = ImportacaoDeReferencia.Ler(LeitorDePlanilha.LerAba(planilha, aba), uf);
        var composicoes = ImportacaoDeReferencia.Ler(LeitorDePlanilha.LerAba(planilha, "Analítico"));
        if (insumos.Insumos.Count == 0 || composicoes.Composicoes.Count == 0)
            throw new InvalidDataException("A planilha da Caixa não trouxe os insumos ou as composições.");
        var porCodigo = composicoes.Insumos.ToDictionary(i => i.Codigo);
        foreach (var i in insumos.Insumos)
            porCodigo[i.Codigo] = i;
        return (new TabelaDeReferencia(@base, porCodigo.Values, composicoes.Composicoes), insumos.Avisos.Count + composicoes.Avisos.Count);
    }
}
