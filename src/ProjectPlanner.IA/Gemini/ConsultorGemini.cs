using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static ProjectPlanner.IA.Gemini.AnalistaGemini;

namespace ProjectPlanner.IA.Gemini;

/// <summary>
/// Bloco 38: o consultor de orçamento pelo Gemini. Uma chamada com as instruções, a obra (ficha, base, BDI), as tarefas (com
/// as composições candidatas que o App achou na base e as horas de equipe) e os recursos; o Gemini responde no formato JSON
/// do esquema. A resposta é conferida antes de virar sugestão: tipos conhecidos e pedidos, tarefas e recursos do pedido,
/// códigos só entre as candidatas da tarefa, números válidos, uma sugestão de cada tipo por tarefa.
/// </summary>
public sealed class ConsultorGemini : IConsultorDeOrcamento
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    private readonly HttpClient _http;
    private readonly AcessoAoGemini _acesso;

    public ConsultorGemini(HttpClient http, string chave, string modelo) : this(http, new AcessoPorChave(chave), modelo)
    {
    }

    /// <summary>Bloco 45: pelo acesso escolhido (a chave própria ou a conta ProjectPlanner).</summary>
    public ConsultorGemini(HttpClient http, AcessoAoGemini acesso, string modelo)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(acesso);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelo);
        (_http, _acesso, Modelo) = (http, acesso, modelo);
    }

    public string Modelo { get; }

    public bool EstaConfigurado => true;

    internal const string Instrucoes =
        "Você é um engenheiro civil orçamentista experiente em obras de edificações no Brasil, acostumado com a SINAPI. " +
        "Escreva em português do Brasil, com linguagem técnica simples. Use os dados recebidos e premissas comuns de obra, " +
        "sempre explicadas na justificativa; não invente códigos de composição.\n" +
        "Cada sugestão é um objeto com \"tipo\" e \"justificativa\" (até 300 caracteres, dizendo o porquê ou a conta) e os campos do " +
        "tipo. As tarefas e os recursos são indicados pelos ids exatamente como recebidos (T1, R1). Números vão como números JSON, " +
        "com ponto decimal. Mande só sugestões dos grupos pedidos e que mudem algo; se o pedido tiver um foco, fique nele. " +
        "Em \"resumo\", de 1 a 3 frases com o que você sugeriu e as premissas principais.\n" +
        "Tipos:\n" +
        "- composicao (grupo composições): tarefa e codigo. Escolha a composição mais adequada ao serviço da tarefa ENTRE AS " +
        "CANDIDATAS DAQUELA TAREFA (o código exatamente como listado), com a unidade coerente com o serviço e com a unidade da " +
        "tarefa. Não sugira para tarefas que já têm uma composição adequada; se a atual parecer errada, sugira a troca e explique. " +
        "Não sugira para marcos nem para tarefas que não são serviços orçáveis (projetos, aprovações, mobilização sem custo).\n" +
        "- estimativa (grupo composições): tarefa, descricao (o serviço, como numa planilha orçamentária), unidade, custoMateriais e " +
        "custoMaoDeObra (custo direto por unidade, em reais, sem BDI; mão de obra já com encargos; equipamentos junto da mão de " +
        "obra). Use só quando nenhuma candidata serve, ou não há base, e o serviço precisa de preço; seja coerente com a base " +
        "(estado e mês) quando houver. Na justificativa, diga em que se baseia (serviço parecido, faixa usual). Tarefas que já têm " +
        "Valor informado (preço de empreiteiro) ficam como estão, a menos que o foco peça.\n" +
        "- quantidade (grupo quantidades): tarefa, valor e unidade (a da composição escolhida ou sugerida). Para tarefas sem " +
        "quantidade ou com quantidade estimada antes, a partir da ficha da obra (área construída, pavimentos, pé-direito, " +
        "padrão), das notas e dos nomes; mostre a conta na justificativa (por exemplo: paredes ≈ 1,2 m por m² de área × 120 m² × " +
        "2,8 m de pé-direito − 15% de vãos ≈ 343 m²). Sem dados para estimar, mande um alerta pedindo a informação que falta.\n" +
        "- duracao (grupo equipes): tarefa e valor (dias úteis, inteiro). Cada tarefa traz as horas de mão de obra e equipamento da " +
        "composição e quantos por dia isso dá na duração atual; se for irreal para o serviço (equipe grande demais para o local, " +
        "ou fração muito pequena de uma pessoa), sugira a duração com uma equipe razoável e diga qual.\n" +
        "- disponivel (grupo equipes): recurso e valor (quantos por dia: o tamanho da equipe ou de equipamentos), para " +
        "profissionais e equipamentos sem disponível ou com o pico acima dele, coerente com as durações.\n" +
        "- bdi (grupo revisão): valor em %. Quando o BDI é 0 ou está fora da faixa usual para edificações (cerca de 20% a 30%; " +
        "referência do Acórdão 2622/2013 do TCU).\n" +
        "- alerta (grupo revisão, ou quando faltar informação): descricao com o ponto (até 300 caracteres) e, se for de uma " +
        "tarefa, a tarefa. Serviços que costumam faltar (alvenaria sem chapisco, reboco ou verga; concreto sem forma ou armação; " +
        "sem limpeza final), unidades incoerentes entre a tarefa e a composição, quantidades fora do padrão para a obra, " +
        "tarefas sem preço.";

    public async Task<RespostaDoConsultor> SugerirAsync(PedidoDeOrcamento pedido, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        var resposta = await ChamarJsonAsync(_http, _acesso, Modelo, Montar(pedido),
            "A resposta do Gemini ficou grande demais e veio cortada. Peça uma parte do orçamento por vez (use o foco).", RotinasDeIa.Orcamento, cancelamento);
        return Interpretar(resposta, pedido);
    }

    internal static JsonObject Montar(PedidoDeOrcamento pedido)
    {
        var dados = new StringBuilder();
        dados.AppendLine($"Obra: {pedido.Obra}.");
        dados.AppendLine("Grupos pedidos: " + string.Join(", ", NomesDosGrupos(pedido.Grupos)) + ".");
        if (!string.IsNullOrWhiteSpace(pedido.Foco))
            dados.AppendLine($"Foco do pedido: {pedido.Foco.Trim()}");
        dados.AppendLine(pedido.Ficha.Count == 0 ? "Ficha da obra: (vazia)" : "Ficha da obra:");
        foreach (var item in pedido.Ficha)
            dados.AppendLine("- " + item);
        dados.AppendLine(pedido.Base is null ? "Base de preços: nenhuma importada (não há candidatas)." : $"Base de preços: {pedido.Base}.");
        dados.AppendLine($"BDI: {Numero(pedido.Bdi)}%" + (pedido.BdiDeMateriais is { } m ? $" (materiais {Numero(m)}%)" : "") + ".");
        dados.AppendLine();
        dados.AppendLine($"Tarefas ({pedido.Tarefas.Count}; só as que não são resumos):");
        foreach (var t in pedido.Tarefas)
        {
            dados.Append($"{t.Id} | {t.Edt} {t.Nome}");
            if (t.Caminho.Length > 0)
                dados.Append($" | em: {t.Caminho}");
            dados.Append(t.Dias == 0 ? " | marco" : t.Dias == 1 ? " | 1 dia útil" : $" | {t.Dias} dias úteis");
            dados.Append(t.Quantidade is { } q ? $" | quantidade {Numero(q)}{(t.Unidade is { } un ? " " + un : "")}{(t.QuantidadeEstimada ? " (estimada)" : "")}"
                : t.Unidade is { } u ? $" | unidade {u}, sem quantidade" : " | sem quantidade");
            if (t.Valor is { } v)
                dados.Append($" | valor {Numero(v)}");
            dados.Append(t.Composicao is { } c ? $" | composição atual: {c}" : " | sem composição");
            if (!string.IsNullOrWhiteSpace(t.Notas))
                dados.Append($" | notas: {t.Notas.Replace('\n', ' ').Trim()}");
            dados.AppendLine();
            foreach (var e in t.Equipe)
                dados.AppendLine($"    equipe: {e.Insumo}: {Numero(e.Horas)} h no total, {Numero(e.PorDia)} por dia");
            foreach (var candidata in t.Candidatas)
                dados.AppendLine($"    candidata {candidata.Codigo}: {candidata.Descricao} ({candidata.Unidade}) · custo {Numero(candidata.Custo)}");
        }
        if (pedido.Recursos.Count > 0)
        {
            dados.AppendLine();
            dados.AppendLine("Recursos:");
            foreach (var r in pedido.Recursos)
                dados.AppendLine($"{r.Id} | {r.Nome} | {r.Tipo} | disponível {(r.Disponivel is { } d ? Numero(d) + " por dia" : "não informado")} | " +
                                 $"pico {Numero(r.Pico)} por dia");
        }
        return new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = Instrucoes }) },
            ["contents"] = new JsonArray(new JsonObject { ["role"] = "user", ["parts"] = new JsonArray(Nota(dados.ToString())) }),
            ["generationConfig"] = new JsonObject { ["responseMimeType"] = "application/json", ["responseSchema"] = Esquema() },
        };
    }

    internal static JsonObject Esquema() => new()
    {
        ["type"] = "OBJECT",
        ["properties"] = new JsonObject
        {
            ["resumo"] = Tipo("STRING"),
            ["sugestoes"] = new JsonObject
            {
                ["type"] = "ARRAY",
                ["items"] = new JsonObject
                {
                    ["type"] = "OBJECT",
                    ["properties"] = new JsonObject
                    {
                        ["tipo"] = new JsonObject { ["type"] = "STRING", ["enum"] = new JsonArray([.. TiposDeSugestaoIA.Todos.Select(t => (JsonNode)t)]) },
                        ["tarefa"] = Tipo("STRING"),
                        ["recurso"] = Tipo("STRING"),
                        ["codigo"] = Tipo("STRING"),
                        ["descricao"] = Tipo("STRING"),
                        ["unidade"] = Tipo("STRING"),
                        ["valor"] = Tipo("NUMBER"),
                        ["custoMateriais"] = Tipo("NUMBER"),
                        ["custoMaoDeObra"] = Tipo("NUMBER"),
                        ["justificativa"] = Tipo("STRING"),
                    },
                    ["required"] = new JsonArray("tipo", "justificativa"),
                    ["propertyOrdering"] = new JsonArray("tipo", "tarefa", "recurso", "codigo", "descricao", "unidade", "valor", "custoMateriais",
                        "custoMaoDeObra", "justificativa"),
                },
            },
        },
        ["required"] = new JsonArray("resumo", "sugestoes"),
        ["propertyOrdering"] = new JsonArray("resumo", "sugestoes"),
    };

    /// <summary>Confere a resposta: o que não vale para o pedido fica de fora.</summary>
    internal static RespostaDoConsultor Interpretar(JsonElement raiz, PedidoDeOrcamento pedido)
    {
        var tarefas = pedido.Tarefas.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
        var recursos = pedido.Recursos.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
        var vistas = new HashSet<string>();
        var sugestoes = new List<SugestaoDaIA>();
        foreach (var item in Lista(raiz, "sugestoes"))
        {
            if (sugestoes.Count == LimitesDoConsultor.Sugestoes)
                break;
            var tipo = (Texto(item, "tipo") ?? "").Trim().ToLowerInvariant();
            var justificativa = Limitar(Texto(item, "justificativa"), LimitesDoConsultor.TamanhoDaJustificativa) ?? "";
            var tarefa = Texto(item, "tarefa")?.Trim() is { Length: > 0 } t && tarefas.TryGetValue(t, out var achada) ? achada : null;
            var descricao = Limitar(Texto(item, "descricao"), LimitesDoConsultor.TamanhoDaJustificativa);
            var unidade = Limitar(Texto(item, "unidade"), 12);
            var valor = Decimal(item, "valor");
            if (!Pedido(tipo, pedido.Grupos))
                continue;
            SugestaoDaIA? s = tipo switch
            {
                TiposDeSugestaoIA.Composicao when tarefa is not null && Texto(item, "codigo")?.Trim() is { } codigo
                    && tarefa.Candidatas.FirstOrDefault(c => c.Codigo == codigo) is { } candidata =>
                    new(tipo, justificativa, tarefa.Id, Codigo: candidata.Codigo),
                TiposDeSugestaoIA.Estimativa when tarefa is not null && Decimal(item, "custoMateriais") is var m && Decimal(item, "custoMaoDeObra") is var mo
                    && m is null or >= 0 and <= 10_000_000 && mo is null or >= 0 and <= 10_000_000 && (m ?? 0) + (mo ?? 0) > 0
                    && (unidade ?? tarefa.Unidade) is { Length: > 0 } und =>
                    new(tipo, justificativa, tarefa.Id, Descricao: descricao ?? tarefa.Nome, Unidade: und, CustoDeMateriais: m ?? 0, CustoDeMaoDeObra: mo ?? 0),
                TiposDeSugestaoIA.Quantidade when tarefa is not null && valor is > 0 and <= 999_999_999 =>
                    new(tipo, justificativa, tarefa.Id, Unidade: unidade, Valor: Math.Round(valor.Value, 4)),
                TiposDeSugestaoIA.Duracao when tarefa is not null && tarefa.Dias > 0 && valor is >= 1 and <= 2000 && Math.Round(valor.Value) != tarefa.Dias =>
                    new(tipo, justificativa, tarefa.Id, Valor: Math.Round(valor.Value)),
                TiposDeSugestaoIA.Disponivel when Texto(item, "recurso")?.Trim() is { Length: > 0 } r && recursos.TryGetValue(r, out var recurso)
                    && valor is > 0 and <= 10_000 =>
                    new(tipo, justificativa, Recurso: recurso.Id, Valor: Math.Round(valor.Value, 2)),
                TiposDeSugestaoIA.Bdi when valor is >= 0 and <= 100 && valor != pedido.Bdi => new(tipo, justificativa, Valor: Math.Round(valor.Value, 2)),
                TiposDeSugestaoIA.Alerta when (descricao ?? Limitar(justificativa, LimitesDoConsultor.TamanhoDaJustificativa)) is { } texto =>
                    new(tipo, descricao is null ? "" : justificativa, tarefa?.Id, Descricao: texto),
                _ => null,
            };
            if (s is null)
                continue;
            // Uma de cada por tarefa (composição e estimativa contam juntas) e por recurso; um BDI; alertas repetidos saem.
            var chave = s.Tipo switch
            {
                TiposDeSugestaoIA.Composicao or TiposDeSugestaoIA.Estimativa => "preco:" + s.Tarefa,
                TiposDeSugestaoIA.Disponivel => "disponivel:" + s.Recurso,
                TiposDeSugestaoIA.Bdi => "bdi",
                TiposDeSugestaoIA.Alerta => "alerta:" + s.Tarefa + ":" + s.Descricao,
                _ => s.Tipo + ":" + s.Tarefa,
            };
            if (vistas.Add(chave))
                sugestoes.Add(s);
        }
        return new RespostaDoConsultor(Limitar(Texto(raiz, "resumo"), 1000) ?? "", sugestoes);
    }

    private static bool Pedido(string tipo, GruposDeSugestao grupos) => tipo switch
    {
        TiposDeSugestaoIA.Composicao or TiposDeSugestaoIA.Estimativa => grupos.HasFlag(GruposDeSugestao.Composicoes),
        TiposDeSugestaoIA.Quantidade => grupos.HasFlag(GruposDeSugestao.Quantidades),
        TiposDeSugestaoIA.Duracao or TiposDeSugestaoIA.Disponivel => grupos.HasFlag(GruposDeSugestao.Equipes),
        TiposDeSugestaoIA.Bdi => grupos.HasFlag(GruposDeSugestao.Revisao),
        TiposDeSugestaoIA.Alerta => true,
        _ => false,
    };

    private static IEnumerable<string> NomesDosGrupos(GruposDeSugestao grupos)
    {
        if (grupos.HasFlag(GruposDeSugestao.Composicoes)) yield return "composições (e estimativas)";
        if (grupos.HasFlag(GruposDeSugestao.Quantidades)) yield return "quantidades";
        if (grupos.HasFlag(GruposDeSugestao.Revisao)) yield return "revisão (alertas e BDI)";
        if (grupos.HasFlag(GruposDeSugestao.Equipes)) yield return "equipes (durações e disponível)";
    }

    // Números aceitos dos dois jeitos (12.5 ou "12,5"), porque os modelos misturam.
    private static decimal? Decimal(JsonElement objeto, string nome)
    {
        if (!objeto.TryGetProperty(nome, out var valor))
            return null;
        if (valor.ValueKind == JsonValueKind.Number && valor.TryGetDecimal(out var numero))
            return numero;
        if (valor.ValueKind == JsonValueKind.String)
        {
            var texto = valor.GetString()!.Trim();
            if (decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture, out var lido) && !texto.Contains(','))
                return lido;
            if (decimal.TryParse(texto, NumberStyles.Number, Brasil, out lido))
                return lido;
        }
        return null;
    }

    private static string Numero(decimal valor) => valor.ToString("0.####", CultureInfo.InvariantCulture);
}
