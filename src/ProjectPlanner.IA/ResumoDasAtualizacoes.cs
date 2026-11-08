using System.Globalization;

namespace ProjectPlanner.IA;

/// <summary>
/// Bloco 83: as atualizações que a IA propôs, uma por linha curta ("2.1 Alvenaria: 40% → 60% concluído"), para o Telegram mostrar
/// o que ficou anotado na caixa sem depender do texto da IA. As tarefas vêm pelo Nº do retrato daquele momento. É só a leitura do
/// que foi pedido: quem confere se dá para aplicar é o programa, no cartão.
/// </summary>
public static class ResumoDasAtualizacoes
{
    /// <summary>Linhas no máximo; o resto vira "e mais N".</summary>
    public const int MaximoDeLinhas = 12;

    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    public static IReadOnlyList<string> Linhas(IReadOnlyList<AtualizacaoDaObra> atualizacoes, ContextoDoProjeto? retrato)
    {
        ArgumentNullException.ThrowIfNull(atualizacoes);
        var linhas = atualizacoes.Select(a => Linha(a, retrato)).ToList();
        return linhas.Count <= MaximoDeLinhas ? linhas : [.. linhas.Take(MaximoDeLinhas - 1), $"e mais {linhas.Count - MaximoDeLinhas + 1}"];
    }

    public static string Linha(AtualizacaoDaObra a, ContextoDoProjeto? retrato)
    {
        ArgumentNullException.ThrowIfNull(a);
        var tarefa = Tarefa(a.Tarefa, retrato);
        var nome = tarefa is not null ? $"{tarefa.Edt} {tarefa.Nome}".Trim() : string.IsNullOrWhiteSpace(a.Tarefa) ? "" : $"tarefa nº {a.Tarefa.Trim()}";
        var acao = a.Acao?.Trim().ToLowerInvariant();
        return a.Tipo switch
        {
            TiposDeAtualizacao.Percentual => Percentual(a, tarefa, nome),
            TiposDeAtualizacao.Producao => Producao(a, tarefa, nome, acao == "total"),
            TiposDeAtualizacao.Medicao => $"Medição{Numero(a.Medicao)}: {Com(nome)}" +
                                          (a.Quantidade is { } q ? $", {N(q)} {a.Unidade}".TrimEnd() : a.Percentual is { } p ? $", {N(p)}%" : ""),
            TiposDeAtualizacao.Custo => $"Custo: {(a.Valor is { } v ? Reais(v) : "?")}" + Junto(a.Descricao) +
                                        (string.IsNullOrWhiteSpace(a.Categoria) ? "" : $" ({a.Categoria.Trim()})") + (nome.Length > 0 ? $" · {nome}" : ""),
            TiposDeAtualizacao.Inicio => $"{Com(nome)}: não iniciar antes de {Dia(a.Data)}",
            TiposDeAtualizacao.LinhaDeBase => "Salvar uma linha de base" + (string.IsNullOrWhiteSpace(a.Nome) ? "" : $" (\"{a.Nome.Trim()}\")"),
            TiposDeAtualizacao.Os => a.Os is { } os ? $"OS-{os:000}: alterar" : $"Nova OS: {a.Titulo ?? a.NovaTarefa ?? "?"}" + Junto(a.Executante),
            TiposDeAtualizacao.SituacaoOs => $"OS-{a.Os ?? 0:000}: {acao ?? "?"}" + (acao == "medir" && a.Percentual is { } pm ? $" até {N(pm)}%" : "") +
                                             (string.IsNullOrWhiteSpace(a.Descricao) ? "" : $" ({a.Descricao.Trim()})"),
            TiposDeAtualizacao.Tarefa => $"{Com(nome)}: " + string.Join(", ", new[]
            {
                a.Quantidade is { } tq ? $"quantidade {N(tq)} {a.Unidade}".TrimEnd() : a.Unidade is { } u ? $"unidade {u}" : null,
                a.Valor is { } tv ? $"valor {Reais(tv)}" : null,
                a.Prazo is { } pz ? $"prazo {Dia(pz)}" : null,
            }.OfType<string>().DefaultIfEmpty("dados")),
            TiposDeAtualizacao.Obra => "Dados da obra" + Junto(string.Join(", ", new[] { a.Contratante, a.Contrato, a.Endereco }.Where(t => !string.IsNullOrWhiteSpace(t)))),
            TiposDeAtualizacao.Recurso => $"Recurso: {a.Nome ?? "?"}" + Junto(a.TipoDoRecurso),
            TiposDeAtualizacao.Atribuir => $"{Com(nome)}: " + (a.Quantidade is 0 ? $"tirar {a.Recurso}" : $"{N(a.Quantidade ?? 1)} × {a.Recurso}"),
            TiposDeAtualizacao.MedicaoDoContrato => $"Medição {a.Medicao ?? 0:00} do contrato: {(acao == "pagar" ? "paga" : "aprovar")}",
            TiposDeAtualizacao.Calendario => Calendario(a, acao),
            TiposDeAtualizacao.Quadro => Quadro(a, acao, Com(nome)),
            TiposDeAtualizacao.Diario => Diario(a, acao, tarefa, nome),
            _ => a.Tipo,
        };
    }

    private static string Percentual(AtualizacaoDaObra a, TarefaNoContexto? tarefa, string nome)
    {
        if (a.Percentual is not { } p)
            return $"{Com(nome)}: % concluído";
        var novo = N(Math.Round(p));
        return tarefa is null ? $"{Com(nome)}: {novo}% concluído" : $"{nome}: {tarefa.PercentualConcluido}% → {novo}% concluído";
    }

    // Bloco 84: "+20 m² executados (10% de 200 m²): 30% → 40% concluído", pela quantidade e pelo executado da tarefa no retrato.
    private static string Producao(AtualizacaoDaObra a, TarefaNoContexto? tarefa, string nome, bool total)
    {
        var unidade = string.IsNullOrWhiteSpace(a.Unidade) ? tarefa?.Unidade : a.Unidade.Trim();
        var quanto = a.Quantidade is { } q ? $"{N(q)} {unidade}".TrimEnd() : "?";
        var texto = total ? $"{Com(nome)}: {quanto} executados no total" : $"{Com(nome)}: +{quanto} executados";
        if (tarefa?.Quantidade is not { } totalDaTarefa || totalDaTarefa <= 0 || a.Quantidade is not { } quantidade)
            return texto;
        if (!string.IsNullOrWhiteSpace(a.Unidade) && !string.IsNullOrWhiteSpace(tarefa.Unidade) && Simples(a.Unidade) != Simples(tarefa.Unidade))
            return $"{texto} (a tarefa é medida em {tarefa.Unidade}: confira no programa)";
        var antes = tarefa.Executado ?? totalDaTarefa * tarefa.PercentualConcluido / 100m;
        var depois = total ? quantidade : antes + quantidade;
        return $"{texto} ({N(Math.Round(quantidade / totalDaTarefa * 100, 1))}% de {N(totalDaTarefa)} {tarefa.Unidade}".TrimEnd() +
               $"): {tarefa.PercentualConcluido}% → {PercentualPeloExecutado(depois, totalDaTarefa)}% concluído";
    }

    /// <summary>A mesma regra da tarefa no programa: 100% só no total; entre 0 e o total, de 1 a 99.</summary>
    private static int PercentualPeloExecutado(decimal executado, decimal total) =>
        executado <= 0 ? 0 : executado >= total ? 100 : (int)Math.Clamp(Math.Round(executado / total * 100, MidpointRounding.AwayFromZero), 1, 99);

    private static string Simples(string unidade) =>
        unidade.Trim().ToLowerInvariant().Replace("²", "2").Replace("³", "3").Replace(" ", "").Replace(".", "");

    private static string Calendario(AtualizacaoDaObra a, string? acao) => acao switch
    {
        "feriado" or "feriadoAnual" => $"Calendário: feriado em {Dia(a.Data ?? a.De)}" + (string.IsNullOrWhiteSpace(a.Nome) ? "" : $" ({a.Nome.Trim()})") +
                                       (acao == "feriadoAnual" ? ", todo ano" : ""),
        "parar" => $"Calendário: sem trabalho {Periodo(a)}" + (string.IsNullOrWhiteSpace(a.Descricao) ? "" : $" ({a.Descricao.Trim()})"),
        "trabalhar" => $"Calendário: trabalho {Periodo(a)}",
        "dias" => $"Calendário: dias de trabalho {a.Dias ?? "?"}",
        _ => "Calendário",
    };

    private static string Quadro(AtualizacaoDaObra a, string? acao, string nome) => acao switch
    {
        "mover" => $"Kanban: {nome} → {a.Coluna ?? "?"}",
        "item" => $"Kanban: no checklist de {nome}: {a.Descricao}",
        "marcarItem" => $"Kanban: feito em {nome}: {a.Descricao}",
        "etiqueta" => $"Kanban: etiqueta {a.Nome} em {nome}",
        "tirarEtiqueta" => $"Kanban: tirar a etiqueta {a.Nome} de {nome}",
        "arquivar" => $"Kanban: arquivar {nome}",
        "desarquivar" => $"Kanban: voltar {nome} ao quadro",
        "coluna" => $"Kanban: coluna nova {a.Nome}",
        "raias" => $"Kanban: raias por {a.Nome}",
        _ => "Kanban",
    };

    private static string Diario(AtualizacaoDaObra a, string? acao, TarefaNoContexto? tarefa, string nome)
    {
        var dia = string.IsNullOrWhiteSpace(a.Data) ? "RDO de hoje" : $"RDO de {Dia(a.Data)}";
        return dia + ": " + acao switch
        {
            "clima" => $"{a.Periodo switch { "manha" => "manhã", "tarde" => "tarde", _ => "dia" }} {a.Nome ?? "?"}" +
                       (a.Praticavel switch { "nao" => ", sem trabalho", "sim" => ", praticável", _ => "" }),
            "efetivo" => a.Quantidade is 0 ? $"tirar {a.Nome}" : $"{N(a.Quantidade ?? 1)} {a.Nome}" + (string.IsNullOrWhiteSpace(a.Fornecedor) ? "" : $" ({a.Fornecedor.Trim()})"),
            "equipamento" => a.Quantidade is 0 ? $"tirar {a.Nome}" : $"{N(a.Quantidade ?? 1)} {a.Nome}",
            "servico" => "serviço " + (tarefa is not null ? nome : a.Descricao ?? nome) +
                         (a.Quantidade is { } qs ? $", {N(qs)} {(string.IsNullOrWhiteSpace(a.Unidade) ? tarefa?.Unidade : a.Unidade.Trim())}".TrimEnd() : "") +
                         (a.Percentual is { } p ? $" ({N(p)}%)" : ""),
            "ocorrencia" => $"ocorrência: {a.Descricao}",
            _ => "diário",
        };
    }

    // A tarefa do retrato pelo Nº ("3" ou "#3").
    private static TarefaNoContexto? Tarefa(string? referencia, ContextoDoProjeto? retrato) =>
        retrato is null || referencia?.Trim().TrimStart('#') is not { Length: > 0 } numero ? null
            : retrato.Tarefas.FirstOrDefault(t => t.Numero.ToString(CultureInfo.InvariantCulture) == numero);

    private static string Com(string nome) => nome.Length == 0 ? "?" : nome;

    private static string Junto(string? texto) => string.IsNullOrWhiteSpace(texto) ? "" : $" · {texto.Trim()}";

    private static string Numero(int? medicao) => medicao is { } n ? $" {n:00}" : "";

    private static string N(decimal valor) => valor.ToString("#,##0.##", Brasil);

    private static string Reais(decimal valor) => "R$ " + valor.ToString("#,##0.00", Brasil);

    private static string Periodo(AtualizacaoDaObra a)
    {
        var (de, ate) = (a.De ?? a.Data, a.Ate ?? a.De ?? a.Data);
        return de == ate || ate is null ? $"em {Dia(de)}" : $"de {Dia(de)} a {Dia(ate)}";
    }

    /// <summary>"2026-11-08" → "08/11"; o que não for data fica como veio.</summary>
    private static string Dia(string? texto) =>
        DateOnly.TryParseExact(texto?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd/MM", CultureInfo.InvariantCulture)
            : texto ?? "?";
}
