using System.Text.Json;
using ProjectPlanner.Core.Relatorios;

namespace ProjectPlanner.App.Services;

/// <summary>
/// O que o relatório fotográfico guarda fora do projeto (Bloco 27): os dados da empresa para a capa (responsável técnico,
/// registro e logotipo), que valem para todos os projetos, e a última escolha de fotos por página e agrupamento.
/// </summary>
/// <param name="Logotipo">O arquivo do logotipo, numa cópia na pasta das preferências; nulo sem logotipo.</param>
public sealed record PreferenciasRelatorio(
    string? Responsavel = null,
    string? Registro = null,
    string? Logotipo = null,
    int FotosPorPagina = 2,
    AgrupamentoDoRelatorio Agrupamento = AgrupamentoDoRelatorio.PorTarefa);

public interface IPreferenciasRelatorio
{
    PreferenciasRelatorio Carregar();

    void Salvar(PreferenciasRelatorio preferencias);

    /// <summary>Copia o logotipo escolhido para a pasta das preferências e devolve o caminho da cópia.</summary>
    /// <exception cref="IOException">Não deu para copiar.</exception>
    string GuardarLogotipo(string arquivo);
}

/// <summary>Sem arquivo: as preferências ficam só na memória (testes e quando nada foi configurado).</summary>
public sealed class PreferenciasRelatorioEmMemoria : IPreferenciasRelatorio
{
    public PreferenciasRelatorio Atuais { get; set; } = new();

    public PreferenciasRelatorio Carregar() => Atuais;

    public void Salvar(PreferenciasRelatorio preferencias) => Atuais = preferencias;

    public string GuardarLogotipo(string arquivo) => arquivo;
}

/// <summary>
/// <c>relatorio.json</c> ao lado das outras preferências, e o logotipo copiado para lá (<c>logotipo.png</c> ou
/// <c>.jpg</c>), para continuar valendo se o arquivo original mudar de lugar. Arquivo ausente ou ilegível vale como
/// preferências vazias; falhas ao gravar são ignoradas, como nos outros arquivos de preferências.
/// </summary>
public sealed class ArquivoPreferenciasRelatorio(string caminho) : IPreferenciasRelatorio
{
    private const int VersaoDoFormato = 1;

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ArquivoPreferenciasRelatorio() : this(CaminhoPadrao())
    {
    }

    public string Caminho { get; } = caminho;

    public static string CaminhoPadrao() =>
        Path.Combine(Path.GetDirectoryName(ArquivoPreferenciasColunas.CaminhoPadrao())!, "relatorio.json");

    public PreferenciasRelatorio Carregar()
    {
        try
        {
            if (!File.Exists(Caminho))
                return new();
            var arquivo = JsonSerializer.Deserialize<Arquivo>(File.ReadAllText(Caminho), Opcoes);
            if (arquivo is not { Versao: VersaoDoFormato })
                return new();
            return new PreferenciasRelatorio(
                Limpo(arquivo.Responsavel), Limpo(arquivo.Registro),
                arquivo.Logotipo is { Length: > 0 } logo && File.Exists(logo) ? logo : null,
                OpcoesDoRelatorio.FotosPorPaginaAceitas.Contains(arquivo.FotosPorPagina) ? arquivo.FotosPorPagina : 2,
                Enum.TryParse<AgrupamentoDoRelatorio>(arquivo.Agrupamento, ignoreCase: true, out var agrupamento) && Enum.IsDefined(agrupamento)
                    ? agrupamento : AgrupamentoDoRelatorio.PorTarefa);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return new();
        }
    }

    public void Salvar(PreferenciasRelatorio preferencias)
    {
        var temporario = Caminho + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Caminho)!);
            File.WriteAllText(temporario, JsonSerializer.Serialize(new Arquivo(VersaoDoFormato, preferencias.Responsavel, preferencias.Registro,
                preferencias.Logotipo, preferencias.FotosPorPagina, preferencias.Agrupamento.ToString()), Opcoes));
            File.Move(temporario, Caminho, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporario); } catch (Exception) { /* nada a fazer */ }
        }
    }

    public string GuardarLogotipo(string arquivo)
    {
        var pasta = Path.GetDirectoryName(Caminho)!;
        Directory.CreateDirectory(pasta);
        var extensao = Path.GetExtension(arquivo).ToLowerInvariant() is ".png" ? ".png" : ".jpg";
        foreach (var antigo in Directory.GetFiles(pasta, "logotipo.*"))
            File.Delete(antigo);
        var destino = Path.Combine(pasta, "logotipo" + extensao);
        File.Copy(arquivo, destino, overwrite: true);
        return destino;
    }

    private static string? Limpo(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    private sealed record Arquivo(int Versao, string? Responsavel, string? Registro, string? Logotipo, int FotosPorPagina, string? Agrupamento);
}
