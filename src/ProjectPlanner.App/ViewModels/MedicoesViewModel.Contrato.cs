using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Medicoes;
using ProjectPlanner.Core.Planejamento;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Um aditivo registrado na lista: o rótulo e o que ele mudou.</summary>
public sealed record AditivoNaLista(Aditivo Aditivo, string Detalhe)
{
    public string Rotulo => Aditivo.Rotulo;
}

/// <summary>
/// Uma tarefa (não resumo) no editor do aditivo: o contratado agora e o acréscimo (+) ou a supressão (−) digitados. Sem o
/// valor digitado, ele vem da quantidade × o preço unitário.
/// </summary>
public sealed partial class ItemDoAditivoNaTela(Tarefa tarefa, string edt, bool jaMedida) : ViewModelBase
{
    public Tarefa Tarefa { get; } = tarefa;

    public string Edt { get; } = edt;

    public string Nome => Tarefa.Nome;

    public string Unidade => Tarefa.Quantidade is null ? (Tarefa.Valor is > 0 ? "%" : "") : Tarefa.Unidade ?? "";

    public string QuantidadeAtual => Tarefa.Quantidade is { } q ? Moeda.FormatarQuantidade(q) : "";

    public string ValorAtual => Tarefa.Valor is { } v ? Moeda.Formatar(v) : "";

    /// <summary>Medida em % (sem quantidade) e já medida: o aditivo só muda o valor.</summary>
    public bool SoValor { get; } = jaMedida && tarefa.Quantidade is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NovaQuantidade), nameof(NovoValor))]
    public partial string Quantidade { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NovoValor))]
    public partial string Valor { get; set; } = "";

    /// <summary>O que vai mudar (zeros sem nada digitado).</summary>
    /// <exception cref="FormatException">Um número que não dá para ler.</exception>
    public (decimal Quantidade, decimal Valor) Mudanca()
    {
        var quantidade = Moeda.LerNumero(Quantidade) ?? 0;
        var valor = Moeda.Ler(Valor) ?? (Tarefa.PrecoUnitario is { } preco ? Math.Round(quantidade * preco, 2, MidpointRounding.AwayFromZero) : 0);
        return (quantidade, valor);
    }

    public string NovaQuantidade => Tentar(m => (Tarefa.Quantidade ?? 0) + m.Quantidade is var q && q != 0 ? Moeda.FormatarQuantidade(q) : "");

    public string NovoValor => Tentar(m => Moeda.Formatar((Tarefa.Valor ?? 0) + m.Valor));

    private string Tentar(Func<(decimal Quantidade, decimal Valor), string> texto)
    {
        try
        {
            return texto(Mudanca());
        }
        catch (FormatException)
        {
            return "?";
        }
    }
}

/// <summary>Um período do plano de medições na tela.</summary>
public sealed record LinhaDoPlanoNaTela(string Periodo, string Previsto, string PrevistoAcumulado, string Medicoes, string Medido, string MedidoAcumulado,
    string Diferenca, bool Atrasado);

// Bloco 36: as abas Aditivos, Contrato e Plano da tela de medições.
public sealed partial class MedicoesViewModel
{
    // ---- Aditivos ----

    public ObservableCollection<AditivoNaLista> Aditivos { get; } = [];

    public bool SemAditivos => Aditivos.Count == 0;

    public ObservableCollection<ItemDoAditivoNaTela> ItensDoAditivo { get; } = [];

    [ObservableProperty] public partial DateTime? DataDoAditivo { get; set; }

    [ObservableProperty] public partial string MotivoDoAditivo { get; set; } = "";

    private void AtualizarAditivos()
    {
        var edt = _cronograma.NumeracaoEdt();
        Aditivos.Clear();
        foreach (var aditivo in Contrato.Aditivos.Reverse())
            Aditivos.Add(new AditivoNaLista(aditivo, string.Join("; ", aditivo.Itens.Take(6).Select(i => Detalhe(i, edt))) +
                (aditivo.Itens.Count > 6 ? $" e mais {aditivo.Itens.Count - 6}" : "")));
        var medidas = Contrato.Medicoes.Where(m => m.Travada).SelectMany(m => m.Linhas!).Where(l => !l.EhGrupo && l.QuantidadeNoPeriodo > 0)
            .Select(l => l.TarefaId).ToHashSet();
        ItensDoAditivo.Clear();
        foreach (var tarefa in _cronograma.Tarefas.Where(t => !t.EhResumo))
            ItensDoAditivo.Add(new ItemDoAditivoNaTela(tarefa, edt[tarefa.Id], medidas.Contains(tarefa.Id)));
        OnPropertyChanged(nameof(SemAditivos));
        RemoverUltimoAditivoCommand.NotifyCanExecuteChanged();
    }

    private string Detalhe(AlteracaoDoAditivo item, IReadOnlyDictionary<Guid, string> edt)
    {
        var codigo = edt.GetValueOrDefault(item.TarefaId, "—");
        var unidade = _cronograma.Tarefas.FirstOrDefault(t => t.Id == item.TarefaId)?.Unidade;
        var quantidade = item.Quantidade == 0 ? "" : $"{(item.Quantidade > 0 ? "+" : "−")}{Moeda.FormatarQuantidade(Math.Abs(item.Quantidade))} {unidade} ".Replace("  ", " ");
        return $"{codigo} {quantidade}({Relatorios.TextosDoBoletim.ComSinal(item.Valor)})";
    }

    /// <summary>Registra o aditivo com o que foi digitado: muda a quantidade e o valor das tarefas.</summary>
    [RelayCommand]
    private void RegistrarAditivo()
    {
        var itens = new List<AlteracaoDoAditivo>();
        foreach (var item in ItensDoAditivo)
        {
            try
            {
                var (quantidade, valor) = item.Mudanca();
                if (quantidade != 0 || valor != 0)
                    itens.Add(new AlteracaoDoAditivo(item.Tarefa.Id, quantidade, valor));
            }
            catch (FormatException ex)
            {
                Mensagem = $"{item.Edt} {item.Nome}: {ex.Message}";
                return;
            }
        }
        if (DataDoAditivo is not { } data)
        {
            Mensagem = "Informe a data do aditivo.";
            return;
        }
        Aditivo? aditivo = null;
        if (!Tentar(() => aditivo = Contrato.RegistrarAditivo(DateOnly.FromDateTime(data), MotivoDoAditivo, itens), "Registrar aditivo", recalcular: true))
            return;
        MotivoDoAditivo = "";
        AtualizarAditivos();
        AtualizarLista();
        AtualizarContrato();
        Mensagem = $"{aditivo!.Rotulo.Split(" · ")[0]} registrado: o contrato passou a {Moeda.ComSimbolo(Contrato.ValorContratado)}.";
    }

    /// <summary>Tentou mudar a quantidade de um item medido em %.</summary>
    public void AvisarSoValor() => Mensagem = "Este item é medido em % (sem quantidade) e já foi medido: o aditivo só muda o valor dele.";

    private bool TemAditivo() => Contrato.Aditivos.Count > 0;

    [RelayCommand(CanExecute = nameof(TemAditivo))]
    private async Task RemoverUltimoAditivo()
    {
        var aditivo = Contrato.Aditivos[^1];
        if (!await _dialogos.ConfirmarAsync("Remover aditivo",
                $"Remover o aditivo nº {aditivo.Numero} e desfazer as mudanças dele nas tarefas? Dá para desfazer com Editar > Desfazer.", "Remover"))
            return;
        if (!Tentar(() => Contrato.RemoverAditivo(aditivo.Numero), $"Remover o aditivo nº {aditivo.Numero}", recalcular: true))
            return;
        AtualizarAditivos();
        AtualizarLista();
        AtualizarContrato();
    }

    // ---- Contrato: as regras ----

    public IReadOnlyList<Escolha<Periodicidade>> Periodicidades { get; } =
    [
        new(Periodicidade.Semanal, "Semanal"),
        new(Periodicidade.Quinzenal, "Quinzenal"),
        new(Periodicidade.Mensal, "Mensal"),
        new(Periodicidade.DatasDeCorte, "Datas de corte"),
    ];

    [ObservableProperty] public partial string Retencao { get; set; } = "";

    [ObservableProperty] public partial string NomeDoIndice { get; set; } = "";

    [ObservableProperty] public partial string IndiceBase { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostraCortes))]
    public partial Escolha<Periodicidade> PeriodicidadeEscolhida { get; set; } = null!;

    [ObservableProperty] public partial string TextoDosCortes { get; set; } = "";

    public bool MostraCortes => PeriodicidadeEscolhida.Valor == Periodicidade.DatasDeCorte;

    private void CarregarConfiguracao()
    {
        var c = Contrato.Configuracao;
        Retencao = c.PercentualDeRetencao == 0 ? "" : c.PercentualDeRetencao.ToString("0.##", Brasil);
        NomeDoIndice = c.NomeDoIndice ?? "";
        IndiceBase = c.IndiceBase is { } i ? i.ToString("#,##0.####", Brasil) : "";
        PeriodicidadeEscolhida = Periodicidades.First(p => p.Valor == c.Periodicidade);
        TextoDosCortes = string.Join(", ", c.Cortes.Select(d => d.ToString("dd/MM/yyyy", Brasil)));
    }

    /// <summary>Guarda a retenção, o índice e o plano de medições (vale para as medições em rascunho e as próximas).</summary>
    [RelayCommand]
    private void SalvarConfiguracao()
    {
        ConfiguracaoDoContrato configuracao;
        try
        {
            configuracao = new ConfiguracaoDoContrato
            {
                PercentualDeRetencao = Moeda.LerNumero(Retencao.Replace("%", "")) ?? 0,
                NomeDoIndice = NomeDoIndice,
                IndiceBase = Moeda.LerNumero(IndiceBase),
                Periodicidade = PeriodicidadeEscolhida.Valor,
                Cortes = MostraCortes ? PeriodosFinanceiros.LerCortes(TextoDosCortes) : [],
            };
        }
        catch (FormatException ex)
        {
            Mensagem = ex.Message;
            return;
        }
        if (!Tentar(() => Contrato.DefinirConfiguracao(configuracao), "Regras do contrato"))
            return;
        CarregarConfiguracao();
        AtualizarLista();
        AtualizarContrato();
        Mensagem = "Regras do contrato guardadas: valem para as medições em rascunho e as próximas (as aprovadas não mudam).";
    }

    // ---- Contrato: os números e o plano ----

    public IReadOnlyList<LinhaDoResumoNaTela> ResumoDoContrato { get; private set; } = [];

    public string BaseDoPlano { get; private set; } = "";

    public IReadOnlyList<LinhaDoPlanoNaTela> Plano { get; private set; } = [];

    /// <summary>O plano não deu para montar (cronograma sem cálculo, períodos demais).</summary>
    public string ErroDoPlano { get; private set; } = "";

    private void AtualizarContrato()
    {
        var aprovadas = Contrato.Medicoes.Where(m => m.Travada).ToList();
        var contratado = Contrato.ValorContratado;
        var medido = Contrato.MedidoAprovado;
        var retido = aprovadas.Sum(m => m.Resumo!.Retencao - m.Resumo.Devolucao);
        ResumoDoContrato =
        [
            new("Valor original", Moeda.ComSimbolo(contratado - Contrato.ValorDosAditivos)),
            new($"Aditivos ({Contrato.Aditivos.Count})", Relatorios.TextosDoBoletim.ComSinal(Contrato.ValorDosAditivos)),
            new("Valor atual do contrato", Moeda.ComSimbolo(contratado), true),
            new("Medido (aprovadas)", $"{Moeda.ComSimbolo(medido)} ({(contratado == 0 ? 0 : (double)(medido / contratado) * 100).ToString("0.0", Brasil)}%)"),
            new("Saldo do contrato", Moeda.ComSimbolo(contratado - medido)),
            new("Retido acumulado", Moeda.ComSimbolo(retido)),
            new("Medições", $"{Contrato.Medicoes.Count} ({aprovadas.Count(m => m.Situacao == SituacaoDaMedicao.Paga)} paga(s), " +
                            $"{aprovadas.Count(m => m.Situacao == SituacaoDaMedicao.Aprovada)} aprovada(s), {Contrato.Medicoes.Count(m => !m.Travada)} em rascunho)"),
        ];
        try
        {
            var plano = PlanoDeMedicoes.Montar(_cronograma);
            BaseDoPlano = $"Previsto pelo físico-financeiro de: {plano.Base}";
            Plano = [.. plano.Periodos.Select(p => new LinhaDoPlanoNaTela(p.Periodo.Rotulo, Moeda.Formatar(p.Previsto), Moeda.Formatar(p.PrevistoAcumulado),
                string.Join(", ", p.Medicoes.Select(n => $"nº {n:00}")), p.Medido == 0 ? "" : Moeda.Formatar(p.Medido), Moeda.Formatar(p.MedidoAcumulado),
                Moeda.Formatar(p.Diferenca), p.Diferenca < 0 && p.Periodo.Fim < Hoje))];
            ErroDoPlano = "";
        }
        catch (DomainValidationException ex)
        {
            (BaseDoPlano, Plano, ErroDoPlano) = ("", [], ex.Message);
        }
        OnPropertyChanged(nameof(ResumoDoContrato));
        OnPropertyChanged(nameof(BaseDoPlano));
        OnPropertyChanged(nameof(Plano));
        OnPropertyChanged(nameof(ErroDoPlano));
    }
}
