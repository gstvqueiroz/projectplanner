using System.Globalization;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Orcamento;
using ProjectPlanner.Core.Recursos;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Services;

/// <summary>
/// Bloco 38: monta o pedido ao consultor de orçamento a partir do projeto (as tarefas com as composições candidatas da base,
/// as horas de equipe das composições, os recursos e a ficha do assistente) e traduz as sugestões de volta para o domínio
/// (os ids "T3" e "R2" viram as tarefas e os recursos).
/// </summary>
public sealed class ConsultaDeOrcamento
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    private readonly Dictionary<string, Guid> _tarefas = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Guid> _recursos = new(StringComparer.OrdinalIgnoreCase);

    private ConsultaDeOrcamento(PedidoDeOrcamento pedido) => Pedido = pedido;

    public PedidoDeOrcamento Pedido { get; private set; }

    /// <param name="tabela">A base escolhida; nula sem base (só estimativas, quantidades, revisão e equipes).</param>
    /// <param name="lote">Bloco 54: só estas tarefas (um lote do orçamento da obra inteira); nulo, as que mais precisam, até o limite.</param>
    public static ConsultaDeOrcamento Montar(Cronograma cronograma, TabelaDeReferencia? tabela, string? foco, GruposDeSugestao grupos,
        IReadOnlyCollection<Guid>? lote = null)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        var consulta = new ConsultaDeOrcamento(null!);
        var orcamento = cronograma.Orcamento;
        var edt = cronograma.NumeracaoEdt();
        var porId = cronograma.Tarefas.ToDictionary(t => t.Id);
        string Caminho(Tarefa t)
        {
            var nomes = new List<string>();
            for (var pai = t.TarefaPaiId; pai is { } id; pai = porId[id].TarefaPaiId)
                nomes.Insert(0, porId[id].Nome);
            return string.Join(" > ", nomes);
        }

        // As que mais precisam primeiro (sem preço, sem quantidade), até o limite; depois na ordem da EDT.
        var folhas = cronograma.Tarefas.Where(t => !t.EhResumo && (lote is null || lote.Contains(t.Id))).ToList();
        var escolhidas = folhas
            .OrderBy(t => orcamento.ComposicaoDe(t.Id) is null && t.Valor is null ? 0 : t.Quantidade is null ? 1 : 2)
            .Take(LimitesDoConsultor.Tarefas).ToHashSet();
        var tarefas = new List<TarefaParaOrcamento>();
        foreach (var t in folhas.Where(escolhidas.Contains))
        {
            var id = $"T{tarefas.Count + 1}";
            consulta._tarefas[id] = t.Id;
            var c = orcamento.ComposicaoDe(t.Id);
            var caminho = Caminho(t);
            var candidatas = new List<CandidataParaIA>();
            if (tabela is not null && t.Duracao > 0)
                foreach (var candidata in tabela.Candidatas(t.Nome, caminho, LimitesDoConsultor.CandidatasPorTarefa))
                {
                    try
                    {
                        candidatas.Add(new CandidataParaIA(candidata.Codigo, candidata.Descricao, candidata.Unidade, tabela.Abrir(candidata.Codigo).CustoUnitario));
                    }
                    catch (DomainValidationException)
                    {
                        // uma composição que não abre (falta insumo na base) não é candidata
                    }
                }
            var equipe = new List<HorasNaTarefa>();
            if (c is { EhEstimativa: false } && t.Quantidade is { } q && t.Duracao > 0)
                foreach (var insumo in c.Insumos.Where(i => i.Tipo is TipoDeInsumo.MaoDeObra or TipoDeInsumo.Equipamento))
                {
                    var horas = Math.Round(insumo.Coeficiente * q, 2);
                    equipe.Add(new HorasNaTarefa(insumo.Descricao, horas, Math.Round(horas / OrcamentoDoProjeto.HorasPorDia / t.Duracao, 2)));
                }
            tarefas.Add(new TarefaParaOrcamento(id, edt[t.Id], t.Nome, caminho, t.Duracao, t.Unidade, t.Quantidade, orcamento.QuantidadeEhEstimada(t),
                c is null ? t.Valor : null, c is null ? null : Descrever(c, orcamento), t.Notas, candidatas, equipe));
        }

        var usos = UsoDeRecursos.Calcular(cronograma).ToDictionary(u => u.Recurso.Id, u => u.Pico);
        var recursos = new List<RecursoParaOrcamento>();
        foreach (var r in cronograma.Recursos.Lista.Where(r => r.PorDia))
        {
            var id = $"R{recursos.Count + 1}";
            consulta._recursos[id] = r.Id;
            recursos.Add(new RecursoParaOrcamento(id, r.Nome, TiposDeRecurso.Texto(r.Tipo), r.Disponivel, usos.GetValueOrDefault(r.Id)));
        }

        consulta.Pedido = new PedidoDeOrcamento(cronograma.Nome, string.IsNullOrWhiteSpace(foco) ? null : foco.Trim(), grupos,
            [.. cronograma.Assistente.Ficha.Itens.Select(i => $"{i.Campo}: {i.Valor}")], tabela?.Base.Descricao,
            orcamento.Configuracao.Bdi, orcamento.Configuracao.BdiDeMateriais, tarefas, recursos);
        return consulta;
    }

    private static string Descrever(ComposicaoAberta c, OrcamentoDoProjeto orcamento) =>
        (c.EhEstimativa ? $"estimativa da IA: {c.Descricao}" : $"{c.Base.Fonte} {c.Codigo} {c.Descricao}") +
        $" ({c.Unidade}) · custo {c.CustoUnitario.ToString("0.00", CultureInfo.InvariantCulture)}";

    /// <summary>As sugestões no domínio (as de tarefas ou recursos fora do pedido ficam de fora).</summary>
    public IReadOnlyList<SugestaoDeOrcamento> Traduzir(RespostaDoConsultor resposta)
    {
        ArgumentNullException.ThrowIfNull(resposta);
        var lista = new List<SugestaoDeOrcamento>();
        foreach (var s in resposta.Sugestoes)
        {
            Guid? tarefa = s.Tarefa is { } t && _tarefas.TryGetValue(t, out var idDaTarefa) ? idDaTarefa : null;
            Guid? recurso = s.Recurso is { } r && _recursos.TryGetValue(r, out var idDoRecurso) ? idDoRecurso : null;
            TipoDeSugestao? tipo = s.Tipo switch
            {
                TiposDeSugestaoIA.Composicao => TipoDeSugestao.Composicao,
                TiposDeSugestaoIA.Estimativa => TipoDeSugestao.Estimativa,
                TiposDeSugestaoIA.Quantidade => TipoDeSugestao.Quantidade,
                TiposDeSugestaoIA.Duracao => TipoDeSugestao.Duracao,
                TiposDeSugestaoIA.Disponivel => TipoDeSugestao.Disponivel,
                TiposDeSugestaoIA.Bdi => TipoDeSugestao.Bdi,
                TiposDeSugestaoIA.Alerta => TipoDeSugestao.Alerta,
                _ => null,
            };
            if (tipo is not { } tipoDaSugestao
                || (tipoDaSugestao is TipoDeSugestao.Composicao or TipoDeSugestao.Estimativa or TipoDeSugestao.Quantidade or TipoDeSugestao.Duracao && tarefa is null)
                || (tipoDaSugestao == TipoDeSugestao.Disponivel && recurso is null))
                continue;
            lista.Add(new SugestaoDeOrcamento(tipoDaSugestao, s.Justificativa, tarefa, recurso, s.Codigo, s.Descricao, s.Unidade, s.Valor,
                s.CustoDeMateriais, s.CustoDeMaoDeObra)
            {
                Equipe = [.. (s.Equipe ?? []).Select(p => new MembroDaEquipe(p.Funcao, p.Quantidade, p.CustoPorDia))], // Bloco 54
            });
        }
        return lista;
    }

    /// <summary>
    /// Bloco 54: as tarefas da obra (não resumos) em lotes de <see cref="LimitesDoConsultor.TarefasPorLote"/>, na ordem do
    /// cronograma, para o orçamento da obra inteira não sair cortado. Até o limite de uma consulta, um lote só.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<Guid>> Lotes(Cronograma cronograma)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        var folhas = cronograma.Tarefas.Where(t => !t.EhResumo).Select(t => t.Id).ToList();
        return [.. folhas.Chunk(LimitesDoConsultor.TarefasPorLote).Select(l => (IReadOnlyList<Guid>)l)];
    }

    /// <summary>"3 tarefas, 1 recurso" para a tela.</summary>
    public string Tamanho =>
        (Pedido.Tarefas.Count == 1 ? "1 tarefa" : $"{Pedido.Tarefas.Count.ToString("#,##0", Brasil)} tarefas") +
        (Pedido.Recursos.Count == 0 ? "" : Pedido.Recursos.Count == 1 ? ", 1 recurso" : $", {Pedido.Recursos.Count} recursos");
}
