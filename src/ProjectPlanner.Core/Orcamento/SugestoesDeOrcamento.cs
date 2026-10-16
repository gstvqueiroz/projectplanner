using System.Globalization;
using ProjectPlanner.Core.Recursos;

namespace ProjectPlanner.Core.Orcamento;

/// <summary>Bloco 38: o que uma sugestão da IA para o orçamento propõe.</summary>
public enum TipoDeSugestao
{
    /// <summary>Uma composição da base para a tarefa.</summary>
    Composicao,

    /// <summary>Um custo unitário estimado pela IA (quando a base não tem um serviço adequado).</summary>
    Estimativa,

    /// <summary>A quantidade da tarefa, estimada.</summary>
    Quantidade,

    /// <summary>A duração da tarefa (dias úteis), pelo tamanho da equipe que ela pede.</summary>
    Duracao,

    /// <summary>Quantos de um recurso há por dia (o tamanho da equipe).</summary>
    Disponivel,

    /// <summary>O BDI do orçamento.</summary>
    Bdi,

    /// <summary>Um ponto da revisão, só para ler (serviço que falta, unidade incoerente…).</summary>
    Alerta,
}

/// <summary>
/// Bloco 38: uma sugestão da IA para o orçamento, já com as tarefas e os recursos do projeto. Nada muda até o usuário
/// aceitar: <see cref="AplicacaoDeSugestoes"/> confere e aplica as aceitas.
/// </summary>
/// <param name="Valor">Quantidade: a quantidade; Duração: os dias; Disponível: quantos por dia; BDI: o %.</param>
/// <param name="Descricao">Estimativa: o serviço; Alerta: o texto.</param>
public sealed record SugestaoDeOrcamento(
    TipoDeSugestao Tipo,
    string Justificativa,
    Guid? TarefaId = null,
    Guid? RecursoId = null,
    string? Codigo = null,
    string? Descricao = null,
    string? Unidade = null,
    decimal? Valor = null,
    decimal? CustoDeMateriais = null,
    decimal? CustoDeMaoDeObra = null)
{
    public bool EhAlerta => Tipo == TipoDeSugestao.Alerta;
}

/// <summary>O que aplicar as sugestões aceitas fez.</summary>
/// <param name="Feitas">As sugestões que entraram no projeto.</param>
/// <param name="Recusadas">As que não entraram, com o motivo ("2.1 Alvenaria: a tarefa virou resumo").</param>
public sealed record ResultadoDasSugestoes(IReadOnlyList<SugestaoDeOrcamento> Feitas, IReadOnlyList<string> Recusadas)
{
    public int Aplicadas => Feitas.Count;

    public string Resumo => (Aplicadas == 1 ? "1 sugestão aplicada" : $"{Aplicadas} sugestões aplicadas") +
                            (Recusadas.Count == 0 ? "." : $"; {Recusadas.Count} não: {string.Join("; ", Recusadas)}.");
}

/// <summary>
/// Bloco 38: confere e aplica as sugestões aceitas no cronograma. As composições precisam existir na base escolhida (os
/// preços vêm sempre da base); as estimativas entram como "Estimativa IA"; as quantidades ficam marcadas como estimadas.
/// Uma sugestão que não vale mais (tarefa removida, contrato travado…) é recusada com o motivo e não impede as outras.
/// </summary>
public static class AplicacaoDeSugestoes
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>A duração máxima aceita numa sugestão (dias úteis).</summary>
    public const int DuracaoMaxima = 2000;

    /// <summary>Por que a sugestão não pode ser aplicada agora; nulo se pode.</summary>
    public static string? Motivo(Cronograma cronograma, TabelaDeReferencia? tabela, SugestaoDeOrcamento s)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(s);
        if (s.EhAlerta)
            return "é só um aviso";
        var tarefa = s.TarefaId is { } id ? cronograma.Tarefas.FirstOrDefault(t => t.Id == id) : null;
        switch (s.Tipo)
        {
            case TipoDeSugestao.Composicao or TipoDeSugestao.Estimativa or TipoDeSugestao.Quantidade or TipoDeSugestao.Duracao when tarefa is null:
                return "a tarefa não existe mais";
            case TipoDeSugestao.Composicao or TipoDeSugestao.Estimativa or TipoDeSugestao.Quantidade or TipoDeSugestao.Duracao when tarefa!.EhResumo:
                return "a tarefa virou resumo";
            case TipoDeSugestao.Composicao when tabela is null:
                return "nenhuma base escolhida";
            case TipoDeSugestao.Composicao when tabela!.Composicao(s.Codigo ?? "") is null:
                return $"a composição {s.Codigo} não está na base {tabela.Base.Descricao}";
            case TipoDeSugestao.Estimativa when s.CustoDeMateriais is null && s.CustoDeMaoDeObra is null:
                return "sem custo estimado";
            case TipoDeSugestao.Quantidade when s.Valor is not > 0 || s.Valor > Tarefa.QuantidadeMaxima:
                return "quantidade inválida";
            case TipoDeSugestao.Quantidade when cronograma.Contrato.Travado:
                return "o contrato já tem medição aprovada (mude a quantidade por aditivo)";
            case TipoDeSugestao.Duracao when s.Valor is not { } dias || dias < 1 || dias > DuracaoMaxima || dias != Math.Round(dias):
                return "duração inválida";
            case TipoDeSugestao.Duracao when tarefa!.Duracao == 0:
                return "a tarefa é um marco";
            case TipoDeSugestao.Disponivel when s.RecursoId is not { } r || cronograma.Recursos.Obter(r) is null:
                return "o recurso não existe mais";
            case TipoDeSugestao.Disponivel when s.Valor is not > 0 || !TiposDeRecurso.PorDia(cronograma.Recursos.Obter(s.RecursoId!.Value)!.Tipo):
                return "só profissionais e equipamentos têm quantidade por dia";
            case TipoDeSugestao.Bdi when s.Valor is not { } bdi || bdi < 0 || bdi > 100:
                return "BDI fora de 0% a 100%";
        }
        return null;
    }

    /// <summary>Aplica as sugestões aceitas, na ordem: quantidades e durações, preços, recursos e o BDI.</summary>
    public static ResultadoDasSugestoes Aplicar(Cronograma cronograma, TabelaDeReferencia? tabela, IEnumerable<SugestaoDeOrcamento> aceitas)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(aceitas);
        var ordem = aceitas.Where(s => !s.EhAlerta).OrderBy(s => s.Tipo switch
        {
            TipoDeSugestao.Quantidade or TipoDeSugestao.Duracao => 0,
            TipoDeSugestao.Composicao or TipoDeSugestao.Estimativa => 1,
            TipoDeSugestao.Disponivel => 2,
            _ => 3,
        }).ToList();
        var edt = cronograma.NumeracaoEdt();
        var (feitas, recusadas) = (new List<SugestaoDeOrcamento>(), new List<string>());
        foreach (var s in ordem)
        {
            var tarefa = s.TarefaId is { } id ? cronograma.Tarefas.FirstOrDefault(t => t.Id == id) : null;
            var alvo = tarefa is null ? s.Tipo.ToString().ToLowerInvariant() : $"{edt.GetValueOrDefault(tarefa.Id)} {tarefa.Nome}";
            if (Motivo(cronograma, tabela, s) is { } motivo)
            {
                recusadas.Add($"{alvo}: {motivo}");
                continue;
            }
            try
            {
                switch (s.Tipo)
                {
                    case TipoDeSugestao.Composicao:
                        cronograma.Orcamento.Associar(tarefa!, tabela!.Abrir(s.Codigo!));
                        break;
                    case TipoDeSugestao.Estimativa:
                        cronograma.Orcamento.Associar(tarefa!, ComposicaoAberta.Estimativa(s.Descricao ?? tarefa!.Nome, s.Unidade ?? tarefa!.Unidade,
                            s.CustoDeMateriais ?? 0, s.CustoDeMaoDeObra ?? 0));
                        break;
                    case TipoDeSugestao.Quantidade:
                        cronograma.Orcamento.EstimarQuantidade(tarefa!, s.Valor!.Value, s.Unidade);
                        break;
                    case TipoDeSugestao.Duracao:
                        tarefa!.AlterarDuracao((int)s.Valor!.Value);
                        break;
                    case TipoDeSugestao.Disponivel:
                        var r = cronograma.Recursos.Obter(s.RecursoId!.Value)!;
                        cronograma.Recursos.Substituir(new Recurso(r.Id, r.Nome, r.Tipo, r.Unidade, r.CustoUnitario, s.Valor, r.Fornecedor));
                        break;
                    case TipoDeSugestao.Bdi:
                        cronograma.Orcamento.DefinirConfiguracao(cronograma.Orcamento.Configuracao with { Bdi = s.Valor!.Value });
                        break;
                }
                feitas.Add(s);
            }
            catch (DomainValidationException ex)
            {
                recusadas.Add($"{alvo}: {ex.Message}");
            }
        }
        return new ResultadoDasSugestoes(feitas, recusadas);
    }

    /// <summary>A sugestão em uma linha para a tela: "Composição 87495 — ALVENARIA… (M2) · custo 52,53".</summary>
    public static string Texto(SugestaoDeOrcamento s, TabelaDeReferencia? tabela, Cronograma cronograma)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(cronograma);
        string N(decimal v) => v.ToString("#,##0.##", Brasil);
        return s.Tipo switch
        {
            TipoDeSugestao.Composicao when tabela?.Composicao(s.Codigo ?? "") is { } c =>
                $"{tabela.Base.Fonte} {c.Codigo} — {c.Descricao} ({c.Unidade}) · custo {Custo(tabela, c.Codigo)}",
            TipoDeSugestao.Composicao => $"composição {s.Codigo}",
            TipoDeSugestao.Estimativa =>
                $"{s.Descricao} ({s.Unidade}) · custo estimado {N((s.CustoDeMateriais ?? 0) + (s.CustoDeMaoDeObra ?? 0))}/{s.Unidade} " +
                $"(materiais {N(s.CustoDeMateriais ?? 0)}; mão de obra {N(s.CustoDeMaoDeObra ?? 0)})",
            TipoDeSugestao.Quantidade => $"quantidade {N(s.Valor ?? 0)} {s.Unidade}".TrimEnd(),
            TipoDeSugestao.Duracao => s.Valor == 1 ? "duração de 1 dia útil" : $"duração de {N(s.Valor ?? 0)} dias úteis",
            TipoDeSugestao.Disponivel => $"{N(s.Valor ?? 0)} por dia",
            TipoDeSugestao.Bdi => $"BDI de {N(s.Valor ?? 0)}%",
            _ => s.Descricao ?? "",
        };
    }

    private static string Custo(TabelaDeReferencia tabela, string codigo)
    {
        try
        {
            return tabela.Abrir(codigo).CustoUnitario.ToString("#,##0.00", Brasil);
        }
        catch (DomainValidationException)
        {
            return "—";
        }
    }
}
