using System.Text.Json;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Fotos;
using ProjectPlanner.Core.Relatorios;

namespace ProjectPlanner.Data;

/// <summary>
/// Os dados da obra e o histórico dos relatórios fotográficos em JSON, na tabela RelatorioDoProjeto (Bloco 27). O
/// Bloco 28 acrescentou os locais padronizados e os dias do alerta de foto: campos novos e opcionais, sem mudar a
/// versão (um arquivo da 0.27, sem eles, abre com os valores padrão). Bloco 42: a pasta das fotos da obra e a última
/// leitura dela, do mesmo jeito.
/// </summary>
internal static class FormatoDoRelatorio
{
    public const int Versao = 1;

    private static readonly JsonSerializerOptions Opcoes = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string Serializar(Cronograma cronograma) => JsonSerializer.Serialize(new Dados(
        cronograma.Obra.Contratante, cronograma.Obra.Contrato, cronograma.Obra.Endereco,
        [.. cronograma.Relatorios.Select(r => new Relatorio(r.Numero, r.GeradoEm, r.De, r.Ate, r.Fotos))],
        [.. cronograma.ConfiguracaoDasFotos.Locais], cronograma.ConfiguracaoDasFotos.DiasSemFotoParaAlerta,
        cronograma.ConfiguracaoDasFotos.PastaDaObra, cronograma.ConfiguracaoDasFotos.UltimaLeituraDaPasta), Opcoes);

    /// <summary>Há algo para gravar (sem nada, o arquivo fica sem a linha).</summary>
    public static bool TemDados(Cronograma cronograma) =>
        !cronograma.Obra.Vazio || cronograma.Relatorios.Count > 0 || !cronograma.ConfiguracaoDasFotos.EhPadrao;

    /// <exception cref="InvalidDataException">JSON com defeito ou valores que o domínio recusa.</exception>
    public static void Ler(string json, Cronograma cronograma)
    {
        try
        {
            var dados = JsonSerializer.Deserialize<Dados>(json, Opcoes) ?? throw new InvalidDataException("Os dados do relatório estão vazios.");
            cronograma.DefinirObra(new DadosDaObra(dados.Contratante, dados.Contrato, dados.Endereco));
            foreach (var r in dados.Relatorios ?? [])
                cronograma.RegistrarRelatorio(new RelatorioGerado(r.Numero, r.GeradoEm, r.De, r.Ate, r.Fotos));
            cronograma.DefinirConfiguracaoDasFotos(new ConfiguracaoDasFotos(dados.Locais ?? [],
                dados.DiasSemFoto ?? ConfiguracaoDasFotos.DiasPadrao)
            {
                PastaDaObra = string.IsNullOrWhiteSpace(dados.PastaDaObra) ? null : dados.PastaDaObra,
                UltimaLeituraDaPasta = dados.UltimaLeituraDaPasta,
            });
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or DomainValidationException)
        {
            throw new InvalidDataException($"Os dados do relatório fotográfico não puderam ser lidos: {ex.Message}", ex);
        }
    }

    private sealed record Dados(string? Contratante, string? Contrato, string? Endereco, List<Relatorio>? Relatorios,
        List<string>? Locais = null, int? DiasSemFoto = null, string? PastaDaObra = null, DateTime? UltimaLeituraDaPasta = null);

    private sealed record Relatorio(int Numero, DateTime GeradoEm, DateOnly De, DateOnly Ate, int Fotos);
}
