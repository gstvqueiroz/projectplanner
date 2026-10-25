using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Tests.Avaliacao;

/// <summary>Bloco 68: um pedido da avaliação: o que o usuário escreve, o canal, o que se espera e a resposta de referência.</summary>
public sealed record CasoDaAvaliacao(string Id, string Pedido, string? Canal, JsonObject Esperado, JsonObject Referencia)
{
    public override string ToString() => Id;
}

/// <summary>Bloco 68: o resultado de um pedido: o que falhou (a resposta não serve) e os avisos (serve, mas veio algo a mais).</summary>
public sealed record ResultadoDoCaso(string Id, IReadOnlyList<string> Falhas, IReadOnlyList<string> Avisos)
{
    public bool Passou => Falhas.Count == 0;
}

/// <summary>
/// Bloco 68: confere uma resposta do assistente contra o esperado de um pedido. O esperado é parcial: cada atualização, alteração
/// e tela esperada precisa aparecer na resposta com os campos dados (texto começando com "~" é "contém", sem acento nem
/// maiúscula; datas valem nos dois formatos); o que vier a mais é aviso. Depois, as atualizações passam pelo cartão de verdade
/// (<see cref="AtualizacoesDoAssistente.Conferir"/>) e as alterações pelo aplicador, na obra da avaliação: um item com problema
/// é falha.
/// </summary>
public static class AvaliadorDaIa
{
    public static IReadOnlyList<CasoDaAvaliacao> Casos()
    {
        var caminho = Path.Combine(AppContext.BaseDirectory, "Avaliacao", "pedidos.json");
        var raiz = JsonNode.Parse(File.ReadAllText(caminho))!.AsObject();
        return [.. raiz["casos"]!.AsArray().Select(c => new CasoDaAvaliacao(
            c!["id"]!.GetValue<string>(), c["pedido"]!.GetValue<string>(), c["canal"]?.GetValue<string>(),
            c["esperado"]!.AsObject(), c["referencia"]!.AsObject()))];
    }

    public static ResultadoDoCaso Avaliar(CasoDaAvaliacao caso, RespostaIA resposta, MainWindowViewModel obra, ContextoDoProjeto retrato)
    {
        var falhas = new List<string>();
        var avisos = new List<string>();
        var esperado = caso.Esperado;

        // As atualizações: cada esperada casa com uma da resposta (sem repetir).
        var sobrando = resposta.Atualizacoes.ToList();
        foreach (var item in Lista(esperado, "atualizacoes"))
        {
            var achada = sobrando.FirstOrDefault(a => Casa(item, campo => Valor(a, campo)));
            if (achada is null)
                falhas.Add($"faltou a atualização {item.ToJsonString()}" + (resposta.Atualizacoes.Count == 0 ? " (não veio nenhuma)"
                    : $"; vieram: {string.Join(" | ", resposta.Atualizacoes.Select(Descrever))}"));
            else
                sobrando.Remove(achada);
        }
        if (esperado.ContainsKey("atualizacoes"))
            avisos.AddRange(sobrando.Select(a => $"atualização a mais: {Descrever(a)}"));

        var alteracoes = resposta.Alteracoes.ToList();
        foreach (var item in Lista(esperado, "alteracoes"))
        {
            var achada = alteracoes.FirstOrDefault(a => Casa(item, campo => Valor(a, campo)));
            if (achada is null)
                falhas.Add($"faltou a alteração {item.ToJsonString()}" + (resposta.Alteracoes.Count == 0 ? " (não veio nenhuma)"
                    : $"; vieram: {string.Join(" | ", resposta.Alteracoes.Select(a => a.Descricao))}"));
            else
                alteracoes.Remove(achada);
        }

        switch (esperado["abrir"])
        {
            case JsonValue v when v.GetValue<string>() == "nenhum":
                if (resposta.Abrir is { } aberta)
                    falhas.Add($"não era para abrir tela (abriu {aberta.Tela})");
                break;
            case JsonObject tela:
                if (resposta.Abrir is not { } abrir)
                    falhas.Add($"faltou abrir {tela.ToJsonString()}");
                else if (!Casa(tela, campo => Valor(abrir, campo)))
                    falhas.Add($"abriu {abrir.Tela}{(abrir.Aba is { } aba ? $" ({aba})" : "")}, e não {tela.ToJsonString()}");
                break;
        }

        if (esperado["orcamento"]?.GetValue<bool>() == true && string.IsNullOrWhiteSpace(resposta.PedidoDeOrcamento))
            falhas.Add("faltou o pedido de orçamento");
        if (esperado["semMudancas"]?.GetValue<bool>() == true)
        {
            if (resposta.Atualizacoes.Count > 0)
                falhas.Add($"era só para responder, e veio {resposta.Atualizacoes.Count} atualização(ões): {string.Join(" | ", resposta.Atualizacoes.Select(Descrever))}");
            if (resposta.Alteracoes.Count > 0)
                falhas.Add($"era só para responder, e veio {resposta.Alteracoes.Count} alteração(ões) do cronograma");
        }
        if (string.IsNullOrWhiteSpace(resposta.Texto))
            falhas.Add("a mensagem veio vazia");

        // O cartão e o aplicador de verdade, na obra da avaliação.
        if (resposta.Atualizacoes.Count > 0)
            foreach (var item in obra.ConferirAtualizacoes(resposta.Atualizacoes, retrato).Where(i => !i.Valido))
                falhas.Add($"o cartão recusa \"{item.Texto}\": {item.Problema}");
        if (resposta.Alteracoes.Count > 0)
            try
            {
                AplicadorDeAlteracoes.Aplicar(obra.Cronograma.Copiar(), resposta.Alteracoes, retrato);
            }
            catch (Exception ex) when (ex is AlteracaoRecusadaException or Core.DomainValidationException)
            {
                falhas.Add($"as alterações não se aplicam: {ex.Message}");
            }
        return new ResultadoDoCaso(caso.Id, falhas, avisos);
    }

    private static IEnumerable<JsonObject> Lista(JsonObject objeto, string nome) =>
        objeto[nome] is JsonArray lista ? lista.OfType<JsonObject>() : [];

    public static string Descrever(AtualizacaoDaObra a) =>
        string.Join(", ", new[] { $"tipo={a.Tipo}" }.Concat(CatalogoDaIa.Campos
            .Select(c => (c.Nome, Valor: Valor(a, c.Nome))).Where(c => c.Valor is not null).Select(c => $"{c.Nome}={c.Valor}")));

    // Os campos pelo nome do JSON ("tipoDoRecurso"), na propriedade de mesmo nome ("TipoDoRecurso").
    private static object? Valor(object registro, string campo) =>
        registro.GetType().GetProperty(char.ToUpperInvariant(campo[0]) + campo[1..])?.GetValue(registro);

    private static bool Casa(JsonObject esperado, Func<string, object?> valor) =>
        esperado.All(par => Igual(par.Key, par.Value, valor(par.Key)));

    private static bool Igual(string campo, JsonNode? esperado, object? real)
    {
        if (esperado is null)
            return real is null;
        var e = (JsonValue)esperado;
        if (e.GetValueKind() == JsonValueKind.Number)
            return real switch
            {
                int i => e.GetValue<decimal>() == i,
                decimal d => e.GetValue<decimal>() == d,
                _ => false,
            };
        if (e.GetValueKind() == JsonValueKind.True || e.GetValueKind() == JsonValueKind.False)
            return real is bool b && b == e.GetValue<bool>();
        var texto = e.GetValue<string>();
        var r = real switch
        {
            null => null,
            string s => s,
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => real.ToString(),
        };
        if (r is null)
            return false;
        if (texto.StartsWith('~'))
            return Normal(r).Contains(Normal(texto[1..]), StringComparison.Ordinal);
        if (campo is "tarefa" or "de" or "para" or "apos" or "pai" && !EhData(texto))
            return Normal(r).TrimStart('#') == Normal(texto);
        if (campo == "tarefas")
            return Referencias(r).SetEquals(Referencias(texto));
        if (EhData(texto))
            return Data(r) is { } dr && dr == Data(texto);
        return Normal(r) == Normal(texto);
    }

    private static HashSet<string> Referencias(string texto) =>
        [.. texto.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim().TrimStart('#'))];

    private static bool EhData(string texto) => Data(texto) is not null;

    private static DateOnly? Data(string texto) =>
        DateOnly.TryParseExact(texto.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d
        : DateOnly.TryParseExact(texto.Trim(), ["dd/MM/yyyy", "d/M/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d
        : null;

    // Sem acento, sem maiúscula e sem espaços nas pontas: "Mão de Obra" = "mao de obra".
    private static string Normal(string texto)
    {
        var decomposto = texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        return new string([.. decomposto.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)]);
    }
}
