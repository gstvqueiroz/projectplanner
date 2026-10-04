using System.Globalization;

namespace ProjectPlanner.IA;

/// <summary>As ações que o assistente pode pedir no cronograma (Bloco 24).</summary>
public static class AcoesNoCronograma
{
    public const string Criar = "criar";
    public const string Alterar = "alterar";
    public const string Remover = "remover";
    public const string Vincular = "vincular";
    public const string Desvincular = "desvincular";
    public const string Mover = "mover";
    public const string Projeto = "projeto";
    public const string Limpar = "limpar";

    public static IReadOnlyList<string> Todas { get; } = [Criar, Alterar, Remover, Vincular, Desvincular, Mover, Projeto, Limpar];
}

/// <summary>
/// Uma alteração do cronograma pedida pelo assistente (Bloco 24), como ele a mandou: uma ação e os campos dela. Quem
/// valida e aplica é o App, com as regras do domínio, numa cópia do cronograma (todas as alterações ou nenhuma).
/// </summary>
/// <remarks>
/// As tarefas são indicadas por referências: o Nº da linha no retrato mandado com o pedido ("3") para as que já
/// existiam, ou a referência que o próprio assistente deu ao criar uma tarefa nesta resposta ("n1").
/// </remarks>
/// <param name="Acao">Uma de <see cref="AcoesNoCronograma"/>.</param>
/// <param name="Ref">criar: a referência da tarefa nova.</param>
/// <param name="Tarefa">alterar, remover, mover: a tarefa.</param>
/// <param name="Duracao">criar, alterar: dias úteis (0 = marco).</param>
/// <param name="Pai">criar, mover: o resumo onde a tarefa fica; vazio = primeiro nível.</param>
/// <param name="De">vincular, desvincular: a predecessora.</param>
/// <param name="Para">vincular, desvincular: a sucessora.</param>
/// <param name="Tipo">vincular: TI (padrão), II, TT ou IT.</param>
/// <param name="Defasagem">vincular: dias úteis, pode ser negativa.</param>
/// <param name="Inicio">projeto: a data de início do projeto (AAAA-MM-DD).</param>
public sealed record AlteracaoDoCronograma(
    string Acao,
    string? Ref = null,
    string? Tarefa = null,
    string? Nome = null,
    int? Duracao = null,
    string? Pai = null,
    int? Percentual = null,
    string? Notas = null,
    string? De = null,
    string? Para = null,
    string? Tipo = null,
    int? Defasagem = null,
    string? Inicio = null)
{
    /// <summary>Para mensagens: "criar \"Alvenaria\"", "vincular 3 → n2", "remover 5".</summary>
    public string Descricao => Acao switch
    {
        AcoesNoCronograma.Criar => $"criar \"{Nome}\"" + (string.IsNullOrWhiteSpace(Ref) ? "" : $" ({Ref})"),
        AcoesNoCronograma.Vincular or AcoesNoCronograma.Desvincular => $"{Acao} {De} → {Para}",
        AcoesNoCronograma.Projeto or AcoesNoCronograma.Limpar => Acao,
        _ => $"{Acao} {Tarefa}",
    };

    /// <summary>"vincular 3 → n2 (II+2)": o tipo e a defasagem, quando há.</summary>
    public override string ToString() =>
        Acao == AcoesNoCronograma.Vincular && (Tipo is not null || Defasagem is not null)
            ? $"{Descricao} ({Tipo ?? "TI"}{(Defasagem is { } d && d != 0 ? d.ToString("+0;-0", CultureInfo.InvariantCulture) : "")})"
            : Descricao;
}
