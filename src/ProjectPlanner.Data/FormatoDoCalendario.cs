using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectPlanner.Core;

namespace ProjectPlanner.Data;

/// <summary>
/// Bloco 71: o calendário de trabalho em JSON, na tabela CalendarioDoProjeto (só quando não é o padrão de segunda a sexta sem
/// feriados: um arquivo sem a linha abre com o padrão, como antes do bloco). O mesmo formato guarda o calendário dos projetos
/// novos nas preferências do programa.
/// </summary>
public static class FormatoDoCalendario
{
    public const int Versao = 1;

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static bool Conhece(int versao) => versao == Versao;

    public static string Serializar(CalendarioDoProjeto calendario) => JsonSerializer.Serialize(new Dados(
        [.. calendario.DiasDeTrabalho], calendario.FeriadosNacionais, calendario.PontosFacultativos,
        calendario.Feriados.Count == 0 ? null : [.. calendario.Feriados.Select(f => new Feriado(f.Data, f.Nome, f.TodoAno ? true : null))],
        calendario.Excecoes.Count == 0 ? null : [.. calendario.Excecoes.Select(e => new Excecao(e.De, e.Ate, e.Motivo, e.Trabalha))]), Opcoes);

    /// <exception cref="InvalidDataException">JSON com defeito ou valores que o domínio recusa.</exception>
    public static CalendarioDoProjeto Ler(string json)
    {
        try
        {
            var dados = JsonSerializer.Deserialize<Dados>(json, Opcoes) ?? throw new InvalidDataException("O calendário está vazio.");
            return CalendarioDoProjeto.Criar(dados.Dias ?? [], dados.FeriadosNacionais, dados.PontosFacultativos,
                (dados.Feriados ?? []).Select(f => new FeriadoDoCalendario(f.Data, f.Nome ?? "", f.TodoAno == true)),
                (dados.Excecoes ?? []).Select(e => new ExcecaoDoCalendario(e.De, e.Ate, e.Motivo ?? "", e.Trabalha)));
        }
        catch (Exception ex) when (ex is JsonException or DomainValidationException or NotSupportedException)
        {
            throw new InvalidDataException($"O calendário de trabalho tem um defeito: {ex.Message}", ex);
        }
    }

    private sealed record Dados(List<DayOfWeek>? Dias, bool FeriadosNacionais, bool PontosFacultativos, List<Feriado>? Feriados, List<Excecao>? Excecoes);

    private sealed record Feriado(DateOnly Data, string? Nome, bool? TodoAno);

    private sealed record Excecao(DateOnly De, DateOnly Ate, string? Motivo, bool Trabalha);
}
