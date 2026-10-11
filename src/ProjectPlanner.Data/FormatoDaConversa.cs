using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Assistente;

namespace ProjectPlanner.Data;

/// <summary>
/// A conversa com o assistente e a ficha do projeto em JSON, na tabela AssistenteDoProjeto (Bloco 26). Um JSON (e não
/// tabelas) porque as mensagens têm partes que variam (anexos, perguntas com opções, o plano), e ninguém precisa
/// consultá-las por SQL. Os nomes são os mesmos do domínio, em camelCase, e os enums vão como texto.
/// </summary>
internal static class FormatoDaConversa
{
    public const int Versao = 1;

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static string Serializar(ConversaDoProjeto conversa) => JsonSerializer.Serialize(new Conversa(
        conversa.Modo,
        conversa.PlanoAprovado,
        [.. conversa.Ficha.Itens.Select(i => new Item(i.Campo, i.Valor))],
        [.. conversa.Mensagens.Select(m => new Mensagem(
            m.Autor, m.Texto, m.DataHora, m.Tipo, m.ResultadoDasAlteracoes, m.AlteracoesAplicadas,
            m.Anexos.Count == 0 ? null : [.. m.Anexos.Select(a => new Anexo(a.Caminho, a.Tamanho))],
            m.Perguntas.Count == 0 ? null : [.. m.Perguntas.Select(p => new Pergunta(p.Texto, [.. p.Opcoes]))],
            m.Plano is null ? null : new Plano(
                [.. m.Plano.Etapas.Select(e => new Etapa(e.Nome, e.DuracaoEmDias, e.Observacao))], m.Plano.TerminoEstimado, [.. m.Plano.Premissas])))]),
        Opcoes);

    /// <summary>A conversa do JSON, validada pelas regras do domínio.</summary>
    /// <exception cref="InvalidDataException">JSON com defeito ou com valores que o domínio recusa.</exception>
    public static ConversaDoProjeto Ler(string json)
    {
        try
        {
            var lido = JsonSerializer.Deserialize<Conversa>(json, Opcoes) ?? throw new InvalidDataException("A conversa está vazia.");
            var conversa = new ConversaDoProjeto();
            foreach (var item in lido.Ficha ?? [])
                conversa.Ficha.Definir(item.Campo, item.Valor);
            foreach (var m in lido.Mensagens ?? [])
                conversa.Acrescentar(new MensagemDaConversa(m.Autor, m.Texto ?? "", m.DataHora, m.Tipo, m.Resultado, m.Aplicadas)
                {
                    Anexos = [.. (m.Anexos ?? []).Select(a => new AnexoNaConversa(a.Caminho, a.Tamanho))],
                    Perguntas = [.. (m.Perguntas ?? []).Select(p => new PerguntaNaConversa(p.Texto, p.Opcoes ?? []))],
                    Plano = m.Plano is null ? null : new PlanoNaConversa(
                        [.. (m.Plano.Etapas ?? []).Select(e => new EtapaDoPlano(e.Nome, e.Duracao, e.Observacao))],
                        m.Plano.Termino, m.Plano.Premissas ?? []),
                });
            conversa.DefinirModo(lido.Modo);
            if (lido.PlanoAprovado && lido.Modo == ModoDaConversa.Estudo)
                conversa.DefinirPlanoAprovado(true);
            return conversa;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or DomainValidationException
                                       or ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException($"A conversa com o assistente não pôde ser lida: {ex.Message}", ex);
        }
    }

    private sealed record Conversa(ModoDaConversa Modo, bool PlanoAprovado, List<Item>? Ficha, List<Mensagem>? Mensagens);

    private sealed record Item(string Campo, string Valor);

    private sealed record Mensagem(
        AutorNaConversa Autor,
        string? Texto,
        DateTimeOffset DataHora,
        TipoDaMensagem Tipo,
        string? Resultado,
        bool Aplicadas,
        List<Anexo>? Anexos,
        List<Pergunta>? Perguntas,
        Plano? Plano);

    private sealed record Anexo(string Caminho, long Tamanho);

    private sealed record Pergunta(string Texto, List<string>? Opcoes);

    private sealed record Plano(List<Etapa>? Etapas, string? Termino, List<string>? Premissas);

    private sealed record Etapa(string Nome, int Duracao, string? Observacao);
}
