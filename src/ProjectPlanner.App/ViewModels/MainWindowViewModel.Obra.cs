using System.Globalization;
using ProjectPlanner.App.Services;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Indicadores;
using ProjectPlanner.Core.Planejamento;
using ProjectPlanner.Core.Recursos;
using ProjectPlanner.Core.Relatorios;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.ViewModels;

// Bloco 41: o chat como central da obra: a situação da obra no retrato, as atualizações que o assistente propõe (o usuário
// confirma num cartão) e as telas que ele abre.
public sealed partial class MainWindowViewModel
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// A situação da obra para o assistente: os indicadores e o semáforo (Bloco 43), as medições, os custos, os recursos e as
    /// fotos, em linhas de texto. Vazia se o cronograma ainda não pôde ser calculado.
    /// </summary>
    internal IReadOnlyList<string> SituacaoDaObra()
    {
        var c = _cronograma;
        var hoje = DateOnly.FromDateTime(_relogio.GetLocalNow().DateTime);
        var linhas = new List<string>();
        // 0.41.2: o que dá para medir (as tarefas com Valor são os itens do contrato) e as quantidades.
        var edtDasTarefas = c.NumeracaoEdt();
        var folhas = c.Tarefas.Where(t => !t.EhResumo).ToList();
        if (!folhas.Any(t => t.Valor > 0))
            linhas.Add("Itens do contrato: nenhuma tarefa tem Valor, então não há o que medir (o usuário precisa preencher a coluna Valor, " +
                       "aplicar o orçamento ou importar os valores).");
        foreach (var t in folhas.Where(t => t.Valor > 0 || t.Quantidade is not null))
            linhas.Add($"Tarefa {edtDasTarefas[t.Id]} {t.Nome}: " + (t.Valor > 0 ? $"valor {Moeda.ComSimbolo(t.Valor!.Value)} (item do contrato)" : "sem valor (não se mede)") +
                       (t.Quantidade is { } q ? $"; quantidade {q.ToString("0.####", Brasil)} {t.Unidade}".TrimEnd() : "; medida em %") + ".");
        try
        {
            var painel = PainelDeIndicadores.Montar(c, hoje, Periodicidade.Mensal);
            linhas.AddRange(Relatorios.TextosDaAnalise.ParaAIA(painel, AnaliseFinanceira.Montar(c, painel, Periodicidade.Mensal)));
        }
        catch (DomainValidationException)
        {
            // sem cálculo, sem indicadores; o resto vai assim mesmo
        }
        foreach (var m in c.Contrato.Medicoes)
        {
            var b = c.Contrato.Boletim(m.Numero);
            linhas.Add($"Medição {m.Numero:00} ({m.De:dd/MM/yyyy} a {m.Ate:dd/MM/yyyy}): {(m.Travada ? "aprovada" : "em rascunho")}; " +
                       $"medido no período {Moeda.ComSimbolo(b.Resumo.MedidoNoPeriodo)}; acumulado {Moeda.ComSimbolo(b.Resumo.MedidoAcumulado)}; " +
                       $"líquido {Moeda.ComSimbolo(b.Resumo.Liquido)}.");
        }
        if (c.Custos.Lancamentos.Count > 0)
        {
            linhas.Add("Custos realizados por categoria: " + string.Join("; ", c.Custos.Lancamentos.GroupBy(l => l.Categoria)
                .Select(g => $"{Categorias.Texto(g.Key)} {Moeda.ComSimbolo(g.Sum(l => l.Valor))}")) + ".");
            var edt = c.NumeracaoEdt();
            foreach (var l in c.Custos.Lancamentos.OrderByDescending(l => l.Data).Take(10))
                linhas.Add($"Custo {l.Data:dd/MM/yyyy}: {l.Descricao} ({Categorias.Texto(l.Categoria)}) {Moeda.ComSimbolo(l.Valor)}" +
                           (l.TarefaId is { } id && edt.TryGetValue(id, out var e) ? $" · tarefa {e}" : " · custo geral") + ".");
        }
        foreach (var r in c.Recursos.Lista)
        {
            var (quantidade, custo) = c.Recursos.Uso(r.Id);
            linhas.Add($"Recurso {r.Nome} ({TiposDeRecurso.Texto(r.Tipo)}): {Moeda.ComSimbolo(r.CustoUnitario)} por {r.Unidade}" +
                       (r.Disponivel is { } d ? $"; disponível {d.ToString("0.##", Brasil)} por dia" : "") +
                       $"; no projeto {quantidade.ToString("0.##", Brasil)} {r.Unidade}, {Moeda.ComSimbolo(custo)}.");
        }
        foreach (var s in Core.Recursos.UsoDeRecursos.Superalocacoes(c).Take(10))
            linhas.Add($"Superalocação: {s.Recurso.Nome} de {s.De:dd/MM} a {s.Ate:dd/MM/yyyy}, pico {s.Pico.ToString("0.##", Brasil)} por dia " +
                       $"(disponível {s.Limite.ToString("0.##", Brasil)}).");
        var fotos = c.Tarefas.Sum(t => t.Fotos.Count);
        if (fotos > 0)
            linhas.Add($"Fotos: {fotos} no projeto; a mais recente de {c.Tarefas.SelectMany(t => t.Fotos).Max(f => f.DataFoto):dd/MM/yyyy}.");
        if (c.Relatorios.Count > 0)
            linhas.Add($"Relatórios fotográficos: {c.Relatorios.Count}; o último até {c.Relatorios.Max(r => r.Ate):dd/MM/yyyy}.");
        linhas.AddRange(OrdensParaOAssistente()); // Bloco 61
        return linhas;
    }

    /// <summary>Confere as atualizações propostas (os Nº pelo retrato mandado com o pedido), para o cartão.</summary>
    public IReadOnlyList<ItemDoCartao> ConferirAtualizacoes(IReadOnlyList<AtualizacaoDaObra> atualizacoes, ContextoDoProjeto retrato) =>
        AtualizacoesDoAssistente.Conferir(_cronograma, atualizacoes, retrato, _relogio.GetLocalNow().DateTime);

    /// <summary>
    /// Aplica as atualizações do cartão numa cópia e, se todas valem, põe a cópia no lugar (um passo do Desfazer,
    /// "Atualizações do assistente"). Se alguma falha, nada muda.
    /// </summary>
    public Task<ResultadoDaAplicacao> AplicarAtualizacoesAsync(IReadOnlyList<OperacaoDaObra> operacoes, string resumo)
    {
        ArgumentNullException.ThrowIfNull(operacoes);
        var copia = _cronograma.Copiar();
        var agora = _relogio.GetLocalNow().DateTime;
        try
        {
            foreach (var operacao in operacoes)
                operacao.Aplicar(copia, agora);
            _calculadora.Calcular(copia);
        }
        catch (DomainValidationException ex)
        {
            return Task.FromResult(new ResultadoDaAplicacao(SituacaoDaAplicacao.Recusada, ex.Message));
        }
        var passo = TrocarCronograma(copia, registrarComo: "Atualizações do assistente");
        Mensagem = $"Assistente: {resumo}";
        return Task.FromResult(new ResultadoDaAplicacao(SituacaoDaAplicacao.Aplicada, resumo, passo));
    }

    /// <summary>Abre a tela pedida pelo assistente, na aba e com o que mostrar (medição, período).</summary>
    public async Task AbrirTelaAsync(TelaParaAbrir tela)
    {
        ArgumentNullException.ThrowIfNull(tela);
        static int Aba(TelaParaAbrir t) => t.Aba is { } a && TelasDoPrograma.Abas.TryGetValue(t.Tela, out var abas) ? Math.Max(0, abas.ToList().IndexOf(a)) : 0;
        static DateOnly? Data(string? texto) =>
            DateOnly.TryParseExact(texto?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        switch (tela.Tela)
        {
            case TelasDoPrograma.Indicadores:
                Recalcular();
                await _dialogos.MostrarIndicadoresAsync(new IndicadoresViewModel(_cronograma, _dialogos, _relogio, _analistaDaObra) { AbaEscolhida = Aba(tela) });
                break;
            case TelasDoPrograma.Orcamento:
                var orcamento = NovoOrcamento();
                orcamento.AbaEscolhida = Aba(tela);
                await _dialogos.MostrarOrcamentoAsync(orcamento);
                break;
            case TelasDoPrograma.Medicoes:
                Recalcular();
                var medicoes = new MedicoesViewModel(_cronograma, _dialogos, _relogio, _preferenciasRelatorio, ContratoMudou, RelatorioDoPeriodoAsync)
                {
                    AbaEscolhida = Aba(tela),
                };
                if (tela.Medicao is { } numero && medicoes.Lista.FirstOrDefault(m => m.Numero == numero) is { } escolhida)
                    medicoes.Selecionada = escolhida;
                await _dialogos.MostrarMedicoesAsync(medicoes);
                break;
            case TelasDoPrograma.RelatorioFotografico:
                Recalcular();
                var relatorio = new RelatorioFotograficoViewModel(_cronograma, _fotos, _preferenciasRelatorio, _dialogos, _relogio, MarcarSemRecalcular, _analista);
                if (Data(tela.De) is { } de)
                {
                    (relatorio.De, relatorio.Ate) = (de.ToDateTime(TimeOnly.MinValue),
                        (Data(tela.Ate) is { } ate && ate >= de ? ate : DateOnly.FromDateTime(_relogio.GetLocalNow().DateTime)).ToDateTime(TimeOnly.MinValue));
                    relatorio.TarefasEscolhidas = relatorio.OpcoesDeTarefas.First(o => o.Valor == Core.Relatorios.TarefasDoRelatorio.EmExecucaoNoPeriodo);
                }
                // Bloco 42: "gera o relatório desta semana": a IA já escreve o texto do andamento.
                var texto = tela.Texto && relatorio.SugerirTextoCommand.CanExecute(null) ? relatorio.SugerirTextoCommand.ExecuteAsync(null) : Task.CompletedTask;
                await _dialogos.MostrarRelatorioFotograficoAsync(relatorio);
                await texto;
                break;
            case TelasDoPrograma.Custos:
                await CustosRealizadosCommand.ExecuteAsync(null);
                break;
            case TelasDoPrograma.Recursos:
                await RecursosDoProjetoCommand.ExecuteAsync(null);
                break;
            case TelasDoPrograma.UsoDeRecursos:
                await UsoDeRecursosCommand.ExecuteAsync(null);
                break;
            case TelasDoPrograma.FisicoFinanceiro:
                await FisicoFinanceiroCommand.ExecuteAsync(null);
                break;
            case TelasDoPrograma.FotosDoProjeto:
                await FotosDoProjetoCommand.ExecuteAsync(null);
                break;
            case TelasDoPrograma.Resumo:
                await ResumoDoProjetoCommand.ExecuteAsync(null);
                break;
            case TelasDoPrograma.LinhasDeBase:
                await GerenciarLinhasDeBaseCommand.ExecuteAsync(null);
                break;
            case TelasDoPrograma.OrdensDeServico: // Bloco 64
                await OrdensDeServicoCommand.ExecuteAsync(null);
                break;
            case TelasDoPrograma.RelatorioDasOrdens:
                await RelatorioDasOrdensCommand.ExecuteAsync(null);
                break;
        }
    }
}
