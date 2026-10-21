using System.Text.Json;

namespace ProjectPlanner.Servidor.Servicos;

/// <summary>
/// Bloco 50: os tokens de um pedido de IA, como o <c>usageMetadata</c> do Gemini conta (a resposta do Claude é traduzida para
/// o mesmo formato). A entrada inclui os tokens lidos do cache e os gravados nele, que custam diferente
/// (<see cref="ModeloDeIa.Microdolares"/>).
/// </summary>
public readonly record struct TokensDoPedido(long Entrada, long Saida, long LidosDoCache = 0, long GravadosNoCache = 0)
{
    /// <summary>O Gemini não grava cache cobrado; o tradutor do Claude põe neste campo os tokens que o Claude gravou.</summary>
    public const string CampoGravadosNoCache = "cacheCreationTokenCount";

    public TokensDoPedido Mais(TokensDoPedido outro) =>
        new(Entrada + outro.Entrada, Saida + outro.Saida, LidosDoCache + outro.LidosDoCache, GravadosNoCache + outro.GravadosNoCache);

    /// <summary>Os tokens do <c>usageMetadata</c> (a saída inclui os de raciocínio, cobrados como saída).</summary>
    public static TokensDoPedido DoUso(JsonElement uso)
    {
        if (uso.ValueKind != JsonValueKind.Object)
            return default;
        long Numero(string nome) => uso.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;
        long entrada = Numero("promptTokenCount"), saida = Numero("candidatesTokenCount") + Numero("thoughtsTokenCount");
        if (entrada + saida == 0)
            entrada = Numero("totalTokenCount");
        return new TokensDoPedido(entrada, saida, Numero("cachedContentTokenCount"), Numero(CampoGravadosNoCache));
    }

    public long Microdolares(ModeloDeIa modelo) => modelo.Microdolares(Entrada, Saida, LidosDoCache, GravadosNoCache);
}
