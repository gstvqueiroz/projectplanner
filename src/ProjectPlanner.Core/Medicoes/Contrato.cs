using System.Globalization;
using ProjectPlanner.Core.Planejamento;

namespace ProjectPlanner.Core.Medicoes;

/// <summary>
/// Bloco 36: as regras do contrato para as medições: a retenção (caução) de cada medição, o índice de reajuste (o nome e o
/// valor na data-base) e o plano de medições (de quanto em quanto tempo se mede).
/// </summary>
public sealed record ConfiguracaoDoContrato
{
    /// <summary>A maior retenção aceita, em %.</summary>
    public const decimal RetencaoMaxima = 30;

    public const int TamanhoMaximoDoNomeDoIndice = 40;

    public static ConfiguracaoDoContrato Padrao { get; } = new();

    /// <summary>Quanto de cada medição fica retido (caução), de 0 a 30%.</summary>
    public decimal PercentualDeRetencao { get; init; }

    /// <summary>"INCC-DI", "IPCA"…; nulo sem nome.</summary>
    public string? NomeDoIndice { get; init; }

    /// <summary>O índice na data-base do contrato (I0); nulo sem reajuste.</summary>
    public decimal? IndiceBase { get; init; }

    /// <summary>De quanto em quanto tempo se mede (sugere o período da próxima medição e monta o plano).</summary>
    public Periodicidade Periodicidade { get; init; } = Periodicidade.Mensal;

    /// <summary>As datas de corte (o último dia de cada medição), com <see cref="Periodicidade.DatasDeCorte"/>.</summary>
    public IReadOnlyList<DateOnly> Cortes { get; init; } = [];

    public bool TemReajuste => IndiceBase is not null;

    /// <exception cref="DomainValidationException">Retenção fora de 0 a 30%, índice base zero ou negativo, nome longo demais.</exception>
    public void Validar()
    {
        if (PercentualDeRetencao is < 0 or > RetencaoMaxima)
            throw new DomainValidationException(nameof(PercentualDeRetencao), $"A retenção vai de 0% a {RetencaoMaxima:0}%.");
        if (IndiceBase is <= 0)
            throw new DomainValidationException(nameof(IndiceBase), "O índice base precisa ser maior que zero.");
        if (NomeDoIndice?.Length > TamanhoMaximoDoNomeDoIndice)
            throw new DomainValidationException(nameof(NomeDoIndice),
                $"O nome do índice tem {NomeDoIndice.Length} caracteres; o máximo é {TamanhoMaximoDoNomeDoIndice}.");
    }
}

/// <summary>Bloco 36: o que um aditivo muda num item: o acréscimo (+) ou a supressão (−) de quantidade e de valor.</summary>
/// <param name="Quantidade">Na unidade do item; zero num item medido em % (sem quantidade).</param>
public sealed record AlteracaoDoAditivo(Guid TarefaId, decimal Quantidade, decimal Valor);

/// <summary>
/// Bloco 36: um aditivo de contrato registrado: o número, a data, o motivo e o que mudou em cada item (acréscimos,
/// supressões e itens novos). Ao ser registrado, ele muda a quantidade e o valor das tarefas; o valor original do contrato
/// é o atual menos os aditivos.
/// </summary>
public sealed class Aditivo
{
    public const int TamanhoMaximoDaDescricao = 200;

    /// <exception cref="DomainValidationException">Número menor que 1, sem motivo, sem itens, item repetido ou sem mudança.</exception>
    public Aditivo(int numero, DateOnly data, string? descricao, IEnumerable<AlteracaoDoAditivo> itens)
    {
        ArgumentNullException.ThrowIfNull(itens);
        if (numero < 1)
            throw new DomainValidationException(nameof(Numero), "O número do aditivo começa em 1.");
        var texto = descricao?.Trim();
        if (string.IsNullOrEmpty(texto))
            throw new DomainValidationException(nameof(Descricao), "Escreva o motivo do aditivo.");
        if (texto.Length > TamanhoMaximoDaDescricao)
            throw new DomainValidationException(nameof(Descricao), $"O motivo tem {texto.Length} caracteres; o máximo é {TamanhoMaximoDaDescricao}.");
        var lista = itens.Where(i => i.Quantidade != 0 || i.Valor != 0).ToList();
        if (lista.Count == 0)
            throw new DomainValidationException(nameof(Itens), "O aditivo não muda nenhum item: informe o acréscimo ou a supressão.");
        if (lista.GroupBy(i => i.TarefaId).Any(g => g.Count() > 1))
            throw new DomainValidationException(nameof(Itens), "Um item aparece duas vezes no aditivo.");
        (Numero, Data, Descricao, Itens) = (numero, data, texto, lista);
    }

    public int Numero { get; }

    public DateOnly Data { get; }

    public string Descricao { get; }

    public IReadOnlyList<AlteracaoDoAditivo> Itens { get; }

    /// <summary>O quanto o aditivo muda o valor do contrato (negativo numa supressão maior que os acréscimos).</summary>
    public decimal Valor => Itens.Sum(i => i.Valor);

    /// <summary>"Aditivo nº 1 · 15/11/2026 · Ampliação da garagem · + R$ 12.000,00".</summary>
    public string Rotulo => $"Aditivo nº {Numero} · {Data:dd/MM/yyyy} · {Descricao} · {(Valor < 0 ? "−" : "+")} R$ {Moeda.Formatar(Math.Abs(Valor))}";
}

/// <summary>Em que ponto está uma medição.</summary>
public enum SituacaoDaMedicao
{
    /// <summary>Em preparação: as quantidades e o período ainda mudam.</summary>
    Rascunho,

    /// <summary>Aceita pelo contratante: travada (os valores ficam como estavam na aprovação).</summary>
    Aprovada,

    /// <summary>Aprovada e recebida.</summary>
    Paga,
}

/// <summary>Bloco 36: o que foi medido de um item numa medição em rascunho (na unidade do item, ou em % sem quantidade).</summary>
public sealed record ItemMedido(Guid TarefaId, decimal Quantidade);

/// <summary>Bloco 36: um corte do contratante no valor da medição, com o motivo.</summary>
public sealed record GlosaDaMedicao
{
    public const int TamanhoMaximoDaDescricao = 200;

    /// <exception cref="DomainValidationException">Sem motivo, motivo longo demais, ou valor zero ou negativo.</exception>
    public GlosaDaMedicao(string? descricao, decimal valor)
    {
        var texto = descricao?.Trim();
        if (string.IsNullOrEmpty(texto))
            throw new DomainValidationException(nameof(Descricao), "Escreva o motivo da glosa.");
        if (texto.Length > TamanhoMaximoDaDescricao)
            throw new DomainValidationException(nameof(Descricao), $"O motivo da glosa tem {texto.Length} caracteres; o máximo é {TamanhoMaximoDaDescricao}.");
        if (valor <= 0)
            throw new DomainValidationException(nameof(Valor), "O valor da glosa precisa ser maior que zero.");
        (Descricao, Valor) = (texto, Math.Round(valor, 2, MidpointRounding.AwayFromZero));
    }

    public string Descricao { get; }

    public decimal Valor { get; }
}

/// <summary>
/// Bloco 36: uma linha do boletim de medição: um item do contrato (uma tarefa com valor) ou um grupo (um resumo, com a
/// soma dos valores). Num item medido em %, a unidade é "%" e a quantidade contratada é 100.
/// </summary>
/// <param name="Removida">Um item medido antes que não está mais no cronograma.</param>
public sealed record LinhaDoBoletim(
    Guid? TarefaId,
    string Edt,
    string Descricao,
    int Nivel,
    bool EhGrupo,
    bool PorPercentual,
    string Unidade,
    decimal QuantidadeContratada,
    decimal PrecoUnitario,
    decimal ValorContratado,
    decimal QuantidadeAnterior,
    decimal QuantidadeNoPeriodo,
    decimal ValorAnterior,
    decimal ValorNoPeriodo,
    bool Removida = false)
{
    public decimal QuantidadeAcumulada => QuantidadeAnterior + QuantidadeNoPeriodo;

    public decimal QuantidadeSaldo => QuantidadeContratada - QuantidadeAcumulada;

    public decimal ValorAcumulado => ValorAnterior + ValorNoPeriodo;

    public decimal ValorSaldo => ValorContratado - ValorAcumulado;

    /// <summary>O % do valor do item já medido (0 a 100).</summary>
    public double PercentualAcumulado => ValorContratado == 0 ? 0 : (double)(ValorAcumulado / ValorContratado) * 100;
}

/// <summary>Bloco 36: os números do boletim: o contrato (original, aditivos e atual), o medido e o valor a faturar.</summary>
/// <param name="Fisico">O avanço físico do cronograma (pelo % concluído) quando o boletim foi feito; nulo sem tarefas.</param>
public sealed record ResumoDoBoletim(
    decimal ValorDosAditivos,
    int QuantidadeDeAditivos,
    decimal ValorContratado,
    decimal MedidoAnterior,
    decimal MedidoNoPeriodo,
    decimal Reajuste,
    string? DescricaoDoReajuste,
    decimal Glosas,
    decimal PercentualDeRetencao,
    decimal Retencao,
    decimal Devolucao,
    decimal RetidoAnterior,
    double? Fisico)
{
    public decimal ValorOriginal => ValorContratado - ValorDosAditivos;

    public decimal MedidoAcumulado => MedidoAnterior + MedidoNoPeriodo;

    public decimal Saldo => ValorContratado - MedidoAcumulado;

    /// <summary>O % do contrato já medido.</summary>
    public double PercentualMedido => ValorContratado == 0 ? 0 : (double)(MedidoAcumulado / ValorContratado) * 100;

    /// <summary>O medido no período com o reajuste e menos as glosas.</summary>
    public decimal Bruto => MedidoNoPeriodo + Reajuste - Glosas;

    /// <summary>O valor a faturar: o bruto menos a retenção, mais a retenção devolvida.</summary>
    public decimal Liquido => Bruto - Retencao + Devolucao;

    /// <summary>O total retido depois desta medição.</summary>
    public decimal RetidoAcumulado => RetidoAnterior + Retencao - Devolucao;
}

/// <summary>
/// Bloco 36: uma medição (boletim de medição nº 1, 2, 3…): o período, o que foi medido de cada item, as glosas, o índice
/// de reajuste do período e a devolução da retenção. Ao ser aprovada, as linhas e os números do boletim ficam guardados
/// como estavam (<see cref="Linhas"/> e <see cref="Resumo"/>): mudar o contrato depois não muda uma medição aprovada.
/// </summary>
public sealed record Medicao
{
    public const int TamanhoMaximoDasObservacoes = 4000;

    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    public int Numero { get; internal init; }

    public DateOnly De { get; internal init; }

    public DateOnly Ate { get; internal init; }

    public SituacaoDaMedicao Situacao { get; internal init; }

    /// <summary>O medido de cada item (na unidade dele, ou em %); nas aprovadas, o que valeu na aprovação.</summary>
    public IReadOnlyList<ItemMedido> Itens { get; internal init; } = [];

    public IReadOnlyList<GlosaDaMedicao> Glosas { get; internal init; } = [];

    /// <summary>O índice de reajuste do período (I1); nulo sem reajuste.</summary>
    public decimal? Indice { get; internal init; }

    /// <summary>Se esta medição devolve a retenção acumulada (e não retém nada).</summary>
    public bool DevolverRetencao { get; internal init; }

    public string? Observacoes { get; internal init; }

    public DateTime? AprovadaEm { get; internal init; }

    public DateOnly? PagaEm { get; internal init; }

    /// <summary>As linhas do boletim na aprovação; nulas no rascunho (calculadas na hora).</summary>
    public IReadOnlyList<LinhaDoBoletim>? Linhas { get; internal init; }

    /// <summary>Os números do boletim na aprovação; nulos no rascunho.</summary>
    public ResumoDoBoletim? Resumo { get; internal init; }

    public bool Travada => Situacao != SituacaoDaMedicao.Rascunho;

    /// <summary>"Medição nº 01".</summary>
    public string Rotulo => $"Medição nº {Numero:00}";

    /// <summary>"01/10/2026 a 31/10/2026".</summary>
    public string Periodo => $"{De:dd/MM/yyyy} a {Ate:dd/MM/yyyy}";

    /// <summary>"Rascunho", "Aprovada em 05/11/2026", "Paga em 20/11/2026".</summary>
    public string TextoDaSituacao => Situacao switch
    {
        SituacaoDaMedicao.Rascunho => "Rascunho",
        SituacaoDaMedicao.Aprovada => AprovadaEm is { } data ? $"Aprovada em {data.ToString("dd/MM/yyyy", Brasil)}" : "Aprovada",
        _ => PagaEm is { } paga ? $"Paga em {paga.ToString("dd/MM/yyyy", Brasil)}" : "Paga",
    };

    /// <summary>Uma medição lida do arquivo, com as mesmas regras de uma criada no programa.</summary>
    /// <exception cref="DomainValidationException">Número menor que 1, período invertido, aprovada sem os números guardados, observações longas.</exception>
    public static Medicao Restaurar(int numero, DateOnly de, DateOnly ate, SituacaoDaMedicao situacao, IEnumerable<ItemMedido> itens,
        IEnumerable<GlosaDaMedicao> glosas, decimal? indice, bool devolverRetencao, string? observacoes, DateTime? aprovadaEm, DateOnly? pagaEm,
        IReadOnlyList<LinhaDoBoletim>? linhas, ResumoDoBoletim? resumo)
    {
        var medicao = new Medicao
        {
            Numero = numero, De = de, Ate = ate, Situacao = situacao, Itens = [.. itens], Glosas = [.. glosas], Indice = indice,
            DevolverRetencao = devolverRetencao, Observacoes = LimparObservacoes(observacoes), AprovadaEm = aprovadaEm,
            PagaEm = situacao == SituacaoDaMedicao.Paga ? pagaEm : null,
            Linhas = situacao == SituacaoDaMedicao.Rascunho ? null : linhas, Resumo = situacao == SituacaoDaMedicao.Rascunho ? null : resumo,
        };
        medicao.Validar();
        return medicao;
    }

    internal void Validar()
    {
        if (Numero < 1)
            throw new DomainValidationException(nameof(Numero), "O número da medição começa em 1.");
        if (Ate < De)
            throw new DomainValidationException(nameof(Ate), "O período da medição termina antes de começar.");
        if (Indice is <= 0)
            throw new DomainValidationException(nameof(Indice), "O índice do período precisa ser maior que zero.");
        if (Travada && (Linhas is null || Resumo is null))
            throw new DomainValidationException(nameof(Situacao), $"A {Rotulo} está aprovada, mas sem o boletim guardado.");
        if (Itens.Any(i => i.Quantidade <= 0) || Itens.GroupBy(i => i.TarefaId).Any(g => g.Count() > 1))
            throw new DomainValidationException(nameof(Itens), $"A {Rotulo} tem um item medido duas vezes, ou com quantidade zero.");
    }

    internal static string? LimparObservacoes(string? observacoes)
    {
        var texto = string.IsNullOrWhiteSpace(observacoes) ? null : observacoes.Trim();
        return texto?.Length > TamanhoMaximoDasObservacoes
            ? throw new DomainValidationException(nameof(Observacoes), $"As observações têm {texto.Length} caracteres; o máximo é {TamanhoMaximoDasObservacoes}.")
            : texto;
    }
}

/// <summary>Bloco 36: o boletim de uma medição: as linhas (grupos e itens, na ordem da EDT) e os números.</summary>
public sealed record BoletimDeMedicao(Medicao Medicao, IReadOnlyList<LinhaDoBoletim> Linhas, ResumoDoBoletim Resumo)
{
    /// <summary>Só as linhas de itens (sem os grupos).</summary>
    public IEnumerable<LinhaDoBoletim> Itens => Linhas.Where(l => !l.EhGrupo);
}
