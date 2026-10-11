using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Medicoes;
using ProjectPlanner.Core.Planejamento;
using ProjectPlanner.Core.Relatorios;
using ProjectPlanner.Relatorios;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Uma medição na lista da tela: o número, o período, a situação e o valor a faturar.</summary>
public sealed record MedicaoNaLista(int Numero, string Rotulo, string Periodo, string Situacao, string Liquido, bool Travada);

/// <summary>Uma glosa da medição na tela.</summary>
public sealed record GlosaNaTela(GlosaDaMedicao Glosa)
{
    public string Descricao => Glosa.Descricao;

    public string Valor => Moeda.ComSimbolo(Glosa.Valor);
}

/// <summary>Uma linha do resumo financeiro na tela.</summary>
public sealed record LinhaDoResumoNaTela(string Rotulo, string Valor, bool Destaque = false);

/// <summary>
/// Uma linha do boletim na tela (um grupo ou um item). Só a coluna "No período" de um item muda, e só no rascunho; o
/// resto vem do boletim calculado.
/// </summary>
public sealed partial class ItemDaMedicaoNaTela : ViewModelBase
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");
    private readonly Action<ItemDaMedicaoNaTela, string> _medir;

    public ItemDaMedicaoNaTela(LinhaDoBoletim linha, bool rascunho, Action<ItemDaMedicaoNaTela, string> medir)
    {
        (Linha, _medir) = (linha, medir);
        PodeMedir = rascunho && !linha.EhGrupo && !linha.Removida && linha.TarefaId is not null;
    }

    public LinhaDoBoletim Linha { get; private set; }

    /// <summary>Um item do rascunho (o medido dele pode ser digitado).</summary>
    public bool PodeMedir { get; private set; }

    public bool EhGrupo => Linha.EhGrupo;

    public string Edt => Linha.Edt;

    public string Descricao => new string(' ', Math.Min(Linha.Nivel, 6) * 3) + Linha.Descricao + (Linha.Removida && !Linha.EhGrupo ? " (fora do cronograma)" : "");

    public string Unidade => EhGrupo ? "" : Linha.Unidade;

    public string Contratado => EhGrupo ? "" : Moeda.FormatarQuantidade(Linha.QuantidadeContratada);

    public string PrecoUnitario => EhGrupo ? "" : Moeda.Formatar(Linha.PrecoUnitario);

    public string ValorContratado => Moeda.Formatar(Linha.ValorContratado);

    public string Anterior => EhGrupo ? "" : Moeda.FormatarQuantidade(Linha.QuantidadeAnterior);

    /// <summary>O medido no período (na unidade do item, ou em %); vazio sem nada medido.</summary>
    public string NoPeriodo
    {
        get => EhGrupo || Linha.QuantidadeNoPeriodo == 0 ? "" : Moeda.FormatarQuantidade(Linha.QuantidadeNoPeriodo);
        set
        {
            if (PodeMedir)
                _medir(this, value ?? "");
        }
    }

    public string Acumulado => EhGrupo ? "" : Moeda.FormatarQuantidade(Linha.QuantidadeAcumulada);

    public string Saldo => EhGrupo ? "" : Moeda.FormatarQuantidade(Linha.QuantidadeSaldo);

    /// <summary>O medido passa do contratado (saldo negativo): a linha aparece em vermelho.</summary>
    public bool SaldoNegativo => !EhGrupo && (Linha.QuantidadeSaldo < 0 || Linha.ValorSaldo < 0);

    public string ValorNoPeriodo => Moeda.Formatar(Linha.ValorNoPeriodo);

    public string ValorAcumulado => Moeda.Formatar(Linha.ValorAcumulado);

    public string PercentualAcumulado => Linha.PercentualAcumulado.ToString("0.0", Brasil) + "%";

    /// <summary>Mostra a linha recalculada (o boletim mudou), mantendo o objeto na grid.</summary>
    public void Atualizar(LinhaDoBoletim linha, bool rascunho)
    {
        Linha = linha;
        PodeMedir = rascunho && !linha.EhGrupo && !linha.Removida && linha.TarefaId is not null;
        OnPropertyChanged(string.Empty);
    }
}

/// <summary>
/// Bloco 36: Projeto > Medições…: as medições (boletins nº 1, 2, 3…) com o medido de cada item, as glosas, o reajuste e a
/// retenção; os aditivos do contrato; as regras do contrato e o plano de medições; e o boletim em PDF, Word e Excel. Cada
/// mudança é um passo do Desfazer.
/// </summary>
public sealed partial class MedicoesViewModel : ViewModelBase
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    private readonly Cronograma _cronograma;
    private readonly IDialogosService _dialogos;
    private readonly TimeProvider _relogio;
    private readonly IPreferenciasRelatorio _preferencias;
    private readonly Action<string, bool> _mudou;
    private readonly Func<DateOnly, DateOnly, Task> _relatorioDoPeriodo;
    private bool _carregando;

    /// <param name="mudou">Uma mudança do contrato, com o nome para o Desfazer e se o cronograma precisa recalcular (um aditivo muda os valores das tarefas).</param>
    /// <param name="relatorioDoPeriodo">Abre o relatório fotográfico com o período da medição.</param>
    public MedicoesViewModel(Cronograma cronograma, IDialogosService dialogos, TimeProvider relogio, IPreferenciasRelatorio preferencias,
        Action<string, bool> mudou, Func<DateOnly, DateOnly, Task> relatorioDoPeriodo)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(dialogos);
        ArgumentNullException.ThrowIfNull(relogio);
        ArgumentNullException.ThrowIfNull(preferencias);
        ArgumentNullException.ThrowIfNull(mudou);
        ArgumentNullException.ThrowIfNull(relatorioDoPeriodo);
        (_cronograma, _dialogos, _relogio, _preferencias, _mudou, _relatorioDoPeriodo) = (cronograma, dialogos, relogio, preferencias, mudou, relatorioDoPeriodo);
        DataDoAditivo = Hoje.ToDateTime(TimeOnly.MinValue);
        CarregarConfiguracao();
        AtualizarLista();
        Selecionada = Lista.LastOrDefault();
        AtualizarAditivos();
        AtualizarContrato();
    }

    private ContratoDoProjeto Contrato => _cronograma.Contrato;

    private DateOnly Hoje => DateOnly.FromDateTime(_relogio.GetLocalNow().DateTime);

    public string Titulo => $"Medições — {_cronograma.Nome}";

    [ObservableProperty] public partial string Mensagem { get; private set; } = "";

    // ---- A lista ----

    public ObservableCollection<MedicaoNaLista> Lista { get; } = [];

    public bool SemMedicoes => Lista.Count == 0;

    public const string TextoSemMedicoes =
        "Nenhuma medição ainda. Use Nova medição para abrir a nº 01: o período vem do plano de medições (aba Contrato).";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemSelecionada))]
    public partial MedicaoNaLista? Selecionada { get; set; }

    public bool TemSelecionada => Selecionada is not null;

    partial void OnSelecionadaChanged(MedicaoNaLista? value)
    {
        if (!_carregando)
            CarregarEditor();
    }

    private void AtualizarLista()
    {
        var numero = Selecionada?.Numero;
        _carregando = true;
        try
        {
            Lista.Clear();
            foreach (var medicao in Contrato.Medicoes)
            {
                var boletim = Contrato.Boletim(medicao.Numero);
                Lista.Add(new MedicaoNaLista(medicao.Numero, medicao.Rotulo, medicao.Periodo, medicao.TextoDaSituacao,
                    Moeda.ComSimbolo(boletim.Resumo.Liquido), medicao.Travada));
            }
            Selecionada = Lista.FirstOrDefault(m => m.Numero == numero) ?? Lista.LastOrDefault();
        }
        finally
        {
            _carregando = false;
        }
        OnPropertyChanged(nameof(SemMedicoes));
        CarregarEditor();
    }

    // ---- A medição escolhida ----

    private Medicao? Atual => Selecionada is { } s ? Contrato.ObterMedicao(s.Numero) : null;

    public bool EhRascunho => Atual is { Travada: false };

    public bool EhAprovada => Atual?.Situacao == SituacaoDaMedicao.Aprovada;

    public bool EhPaga => Atual?.Situacao == SituacaoDaMedicao.Paga;

    /// <summary>"Medição nº 01 · Rascunho".</summary>
    public string TituloDaMedicao => Atual is { } m ? $"{m.Rotulo} · {m.TextoDaSituacao}" : "";

    [ObservableProperty] public partial DateTime? De { get; set; }

    [ObservableProperty] public partial DateTime? Ate { get; set; }

    public ObservableCollection<ItemDaMedicaoNaTela> Itens { get; } = [];

    public ObservableCollection<GlosaNaTela> Glosas { get; } = [];

    [ObservableProperty] public partial string NovaGlosaDescricao { get; set; } = "";

    [ObservableProperty] public partial string NovaGlosaValor { get; set; } = "";

    /// <summary>O índice de reajuste do período (I1), como digitado; vazio sem reajuste.</summary>
    [ObservableProperty] public partial string Indice { get; set; } = "";

    [ObservableProperty] public partial bool DevolverRetencao { get; set; }

    [ObservableProperty] public partial string Observacoes { get; set; } = "";

    public bool TemReajuste => Contrato.Configuracao.TemReajuste;

    /// <summary>"Índice do período (INCC-DI; base 1.000)".</summary>
    public string RotuloDoIndice => Contrato.Configuracao is { IndiceBase: { } baseDoIndice } c
        ? $"Índice do período ({c.NomeDoIndice ?? "índice"}; base {baseDoIndice.ToString("#,##0.####", Brasil)})"
        : "";

    public IReadOnlyList<LinhaDoResumoNaTela> Resumo { get; private set; } = [];

    private void CarregarEditor()
    {
        _carregando = true;
        try
        {
            Itens.Clear();
            Glosas.Clear();
            if (Atual is not { } medicao)
            {
                (De, Ate, Indice, DevolverRetencao, Observacoes, Resumo) = (null, null, "", false, "", []);
                return;
            }
            var boletim = Contrato.Boletim(medicao.Numero);
            foreach (var linha in boletim.Linhas)
                Itens.Add(new ItemDaMedicaoNaTela(linha, !medicao.Travada, Medir));
            foreach (var glosa in medicao.Glosas)
                Glosas.Add(new GlosaNaTela(glosa));
            (De, Ate) = (medicao.De.ToDateTime(TimeOnly.MinValue), medicao.Ate.ToDateTime(TimeOnly.MinValue));
            Indice = medicao.Indice is { } i ? i.ToString("#,##0.####", Brasil) : "";
            DevolverRetencao = medicao.DevolverRetencao;
            Observacoes = medicao.Observacoes ?? "";
            Resumo = [.. TextosDoBoletim.Resumo(boletim.Resumo).Select(r => new LinhaDoResumoNaTela(r.Rotulo, r.Valor, r.Destaque))];
        }
        finally
        {
            _carregando = false;
            NotificarEditor();
        }
    }

    // Depois de medir: refaz as linhas no lugar (a grid não perde a posição) e o resumo.
    private void AtualizarBoletim()
    {
        if (Atual is not { } medicao)
            return;
        var boletim = Contrato.Boletim(medicao.Numero);
        if (boletim.Linhas.Count != Itens.Count || boletim.Linhas.Where((l, i) => l.TarefaId != Itens[i].Linha.TarefaId || l.EhGrupo != Itens[i].EhGrupo).Any())
        {
            CarregarEditor();
            return;
        }
        for (var i = 0; i < Itens.Count; i++)
            Itens[i].Atualizar(boletim.Linhas[i], !medicao.Travada);
        Resumo = [.. TextosDoBoletim.Resumo(boletim.Resumo).Select(r => new LinhaDoResumoNaTela(r.Rotulo, r.Valor, r.Destaque))];
        OnPropertyChanged(nameof(Resumo));
        AtualizarItemDaLista(medicao, boletim);
    }

    private void AtualizarItemDaLista(Medicao medicao, BoletimDeMedicao boletim)
    {
        var indice = Lista.ToList().FindIndex(m => m.Numero == medicao.Numero);
        if (indice < 0)
            return;
        _carregando = true;
        try
        {
            Lista[indice] = new MedicaoNaLista(medicao.Numero, medicao.Rotulo, medicao.Periodo, medicao.TextoDaSituacao,
                Moeda.ComSimbolo(boletim.Resumo.Liquido), medicao.Travada);
            Selecionada = Lista[indice];
        }
        finally
        {
            _carregando = false;
        }
    }

    private void NotificarEditor()
    {
        foreach (var nome in new[] { nameof(EhRascunho), nameof(EhAprovada), nameof(EhPaga), nameof(TituloDaMedicao), nameof(Resumo), nameof(TemReajuste), nameof(RotuloDoIndice) })
            OnPropertyChanged(nome);
        foreach (var comando in new IRelayCommand[]
                 {
                     RemoverMedicaoCommand, SugerirPeloPercentualConcluidoCommand, AprovarCommand, ReabrirCommand, MarcarPagaCommand, DesmarcarPagaCommand,
                     AdicionarGlosaCommand, RemoverGlosaCommand, ExportarPdfCommand, ExportarWordCommand, ExportarExcelCommand, RelatorioFotograficoDoPeriodoCommand,
                 })
            comando.NotifyCanExecuteChanged();
    }

    // Uma mudança aceita: entra no Desfazer, e a tela mostra o boletim novo.
    private bool Tentar(Action mudanca, string descricao, bool recalcular = false)
    {
        try
        {
            mudanca();
        }
        catch (Exception ex) when (ex is DomainValidationException or FormatException)
        {
            Mensagem = ex.Message;
            return false;
        }
        _mudou(descricao, recalcular);
        Mensagem = "";
        return true;
    }

    private void Medir(ItemDaMedicaoNaTela item, string texto)
    {
        if (Atual is not { } medicao || item.Linha.TarefaId is not { } tarefa)
            return;
        decimal? quantidade;
        try
        {
            quantidade = Moeda.LerNumero(texto.Replace("%", ""));
        }
        catch (FormatException ex)
        {
            Mensagem = ex.Message;
            item.Atualizar(item.Linha, rascunho: true);
            return;
        }
        if ((quantidade ?? 0) == item.Linha.QuantidadeNoPeriodo)
            return;
        if (!Tentar(() => Contrato.Medir(medicao.Numero, tarefa, quantidade), $"{medicao.Rotulo}: {item.Linha.Descricao}"))
            item.Atualizar(item.Linha, rascunho: true);
        AtualizarBoletim();
    }

    partial void OnDeChanged(DateTime? value) => MudarPeriodo();

    partial void OnAteChanged(DateTime? value) => MudarPeriodo();

    private void MudarPeriodo()
    {
        if (_carregando || Atual is not { Travada: false } medicao || De is not { } de || Ate is not { } ate)
            return;
        var (inicio, fim) = (DateOnly.FromDateTime(de), DateOnly.FromDateTime(ate));
        if (inicio == medicao.De && fim == medicao.Ate)
            return;
        if (!Tentar(() => Contrato.DefinirPeriodo(medicao.Numero, inicio, fim), $"Período da {medicao.Rotulo.ToLower(Brasil)}"))
        {
            CarregarEditor();
            return;
        }
        AtualizarLista();
    }

    partial void OnIndiceChanged(string value)
    {
        if (_carregando || Atual is not { Travada: false } medicao)
            return;
        Tentar(() => Contrato.DefinirIndice(medicao.Numero, Moeda.LerNumero(value)), $"Índice da {medicao.Rotulo.ToLower(Brasil)}");
        AtualizarBoletim();
    }

    partial void OnDevolverRetencaoChanged(bool value)
    {
        if (_carregando || Atual is not { Travada: false } medicao)
            return;
        Tentar(() => Contrato.DefinirDevolucaoDaRetencao(medicao.Numero, value), $"Devolução da retenção na {medicao.Rotulo.ToLower(Brasil)}");
        AtualizarBoletim();
    }

    partial void OnObservacoesChanged(string value)
    {
        if (_carregando || Atual is not { Travada: false } medicao || (string.IsNullOrWhiteSpace(value) ? null : value.Trim()) == medicao.Observacoes)
            return;
        Tentar(() => Contrato.DefinirObservacoes(medicao.Numero, value), $"Observações da {medicao.Rotulo.ToLower(Brasil)}");
    }

    // ---- Ações da lista e da medição ----

    /// <summary>Abre a próxima medição, com o período do plano (dá para mudar no rascunho).</summary>
    [RelayCommand]
    private void NovaMedicao()
    {
        var (de, ate) = Contrato.ProximoPeriodo();
        Medicao? nova = null;
        if (!Tentar(() => nova = Contrato.NovaMedicao(de, ate), "Nova medição"))
            return;
        AtualizarLista();
        Selecionada = Lista.First(m => m.Numero == nova!.Numero);
        Mensagem = $"{nova!.Rotulo} aberta ({nova.Periodo}). Digite o medido de cada item ou use Sugerir pelo % concluído.";
    }

    private bool PodeRemover() => Atual is { Travada: false } m && m.Numero == Contrato.Medicoes[^1].Numero;

    [RelayCommand(CanExecute = nameof(PodeRemover))]
    private async Task RemoverMedicao()
    {
        var medicao = Atual!;
        if (!await _dialogos.ConfirmarAsync("Remover medição", $"Remover a {medicao.Rotulo.ToLower(Brasil)} ({medicao.Periodo})? Dá para desfazer com Editar > Desfazer.", "Remover"))
            return;
        if (Tentar(() => Contrato.RemoverMedicao(medicao.Numero), $"Remover a {medicao.Rotulo.ToLower(Brasil)}"))
        {
            Selecionada = null;
            AtualizarLista();
        }
    }

    [RelayCommand(CanExecute = nameof(EhRascunho))]
    private void SugerirPeloPercentualConcluido()
    {
        var medicao = Atual!;
        var itens = 0;
        if (!Tentar(() => itens = Contrato.SugerirPeloPercentualConcluido(medicao.Numero), $"Sugerir a {medicao.Rotulo.ToLower(Brasil)} pelo % concluído"))
            return;
        AtualizarBoletim();
        Mensagem = itens == 0
            ? "Nada a sugerir: o % concluído das tarefas não passa do que já foi medido."
            : $"{(itens == 1 ? "1 item medido" : $"{itens} itens medidos")} pelo % concluído (o que falta para chegar a ele). Confira e ajuste antes de aprovar.";
    }

    [RelayCommand(CanExecute = nameof(EhRascunho))]
    private async Task Aprovar()
    {
        var medicao = Atual!;
        var liquido = Contrato.Boletim(medicao.Numero).Resumo.Liquido;
        if (!await _dialogos.ConfirmarAsync("Aprovar medição",
                $"Aprovar a {medicao.Rotulo.ToLower(Brasil)} ({Moeda.ComSimbolo(liquido)} a faturar)? O boletim fica travado como está, e o contrato passa a mudar só por aditivo. " +
                "Dá para reabrir a última medição se ela não estiver paga.", "Aprovar"))
            return;
        if (Tentar(() => Contrato.Aprovar(medicao.Numero, _relogio.GetLocalNow().DateTime), $"Aprovar a {medicao.Rotulo.ToLower(Brasil)}"))
        {
            AtualizarLista();
            AtualizarContrato();
            Mensagem = $"{medicao.Rotulo} aprovada. Exporte o boletim em PDF, Word ou Excel.";
        }
    }

    private bool PodeReabrir() => Atual is { Situacao: SituacaoDaMedicao.Aprovada } m && m.Numero == Contrato.Medicoes[^1].Numero;

    [RelayCommand(CanExecute = nameof(PodeReabrir))]
    private async Task Reabrir()
    {
        var medicao = Atual!;
        if (!await _dialogos.ConfirmarAsync("Reabrir medição",
                $"Voltar a {medicao.Rotulo.ToLower(Brasil)} a rascunho? O boletim aprovado deixa de valer até ela ser aprovada de novo.", "Reabrir"))
            return;
        if (Tentar(() => Contrato.Reabrir(medicao.Numero), $"Reabrir a {medicao.Rotulo.ToLower(Brasil)}"))
        {
            AtualizarLista();
            AtualizarContrato();
        }
    }

    [RelayCommand(CanExecute = nameof(EhAprovada))]
    private async Task MarcarPaga()
    {
        var medicao = Atual!;
        if (await _dialogos.PedirTextoAsync("Marcar como paga", $"Data do pagamento da {medicao.Rotulo.ToLower(Brasil)} (dd/mm/aaaa):",
                Hoje.ToString("dd/MM/yyyy", Brasil)) is not { } texto)
            return;
        if (!DateOnly.TryParseExact(texto.Trim(), ["dd/MM/yyyy", "d/M/yyyy"], Brasil, DateTimeStyles.None, out var data))
        {
            Mensagem = $"\"{texto.Trim()}\" não é uma data (use dd/mm/aaaa).";
            return;
        }
        if (Tentar(() => Contrato.MarcarPaga(medicao.Numero, data), $"Pagamento da {medicao.Rotulo.ToLower(Brasil)}"))
            AtualizarLista();
    }

    [RelayCommand(CanExecute = nameof(EhPaga))]
    private void DesmarcarPaga()
    {
        var medicao = Atual!;
        if (Tentar(() => Contrato.DesmarcarPaga(medicao.Numero), $"Desmarcar o pagamento da {medicao.Rotulo.ToLower(Brasil)}"))
            AtualizarLista();
    }

    [RelayCommand(CanExecute = nameof(EhRascunho))]
    private void AdicionarGlosa()
    {
        var medicao = Atual!;
        try
        {
            var glosa = new GlosaDaMedicao(NovaGlosaDescricao, Moeda.Ler(NovaGlosaValor) ?? 0);
            if (Tentar(() => Contrato.DefinirGlosas(medicao.Numero, [.. medicao.Glosas, glosa]), $"Glosa na {medicao.Rotulo.ToLower(Brasil)}"))
                (NovaGlosaDescricao, NovaGlosaValor) = ("", "");
        }
        catch (Exception ex) when (ex is DomainValidationException or FormatException)
        {
            Mensagem = ex.Message;
            return;
        }
        CarregarEditor();
        AtualizarItemDaLista(Atual!, Contrato.Boletim(medicao.Numero));
    }

    [RelayCommand(CanExecute = nameof(EhRascunho))]
    private void RemoverGlosa(GlosaNaTela? glosa)
    {
        if (glosa is null || Atual is not { } medicao)
            return;
        if (Tentar(() => Contrato.DefinirGlosas(medicao.Numero, medicao.Glosas.Where(g => g != glosa.Glosa)), $"Tirar a glosa da {medicao.Rotulo.ToLower(Brasil)}"))
        {
            CarregarEditor();
            AtualizarItemDaLista(Atual!, Contrato.Boletim(medicao.Numero));
        }
    }

    [RelayCommand(CanExecute = nameof(TemSelecionada))]
    private Task RelatorioFotograficoDoPeriodo() => _relatorioDoPeriodo(Atual!.De, Atual.Ate);

    // ---- Exportar o boletim ----

    [RelayCommand(CanExecute = nameof(TemSelecionada))]
    private Task ExportarPdf() => Exportar("pdf", "PDF", (b, c, agora, destino) => new GeradorBoletimPdf().Gerar(b, c, agora, destino));

    [RelayCommand(CanExecute = nameof(TemSelecionada))]
    private Task ExportarWord() => Exportar("docx", "Word", (b, c, agora, destino) => new GeradorBoletimWord().Gerar(b, c, agora, destino));

    [RelayCommand(CanExecute = nameof(TemSelecionada))]
    private Task ExportarExcel() => Exportar("xlsx", "Excel", (b, c, agora, destino) => new GeradorBoletimExcel().Gerar(b, c, agora, destino));

    /// <summary>O cabeçalho do boletim: a obra (do projeto) e quem emite (as preferências do relatório fotográfico).</summary>
    internal CabecalhoDoRelatorio Cabecalho()
    {
        var preferencias = _preferencias.Carregar();
        byte[]? logotipo = null;
        try
        {
            if (preferencias.Logotipo is { } arquivo && File.Exists(arquivo))
                logotipo = File.ReadAllBytes(arquivo);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sem o logotipo, o boletim sai do mesmo jeito.
        }
        return new CabecalhoDoRelatorio(_cronograma.Nome, _cronograma.Obra, preferencias.Responsavel, preferencias.Registro, logotipo);
    }

    private async Task Exportar(string extensao, string formato, Action<BoletimDeMedicao, CabecalhoDoRelatorio, DateTime, Stream> gerar)
    {
        var boletim = Contrato.Boletim(Atual!.Numero);
        var cabecalho = Cabecalho();
        if (await _dialogos.SalvarComoAsync($"Exportar o boletim em {formato}", TextosDoBoletim.NomeDoArquivo(boletim, _cronograma.Nome), extensao) is not { } caminho)
            return;
        var agora = _relogio.GetLocalNow().DateTime;
        var temporario = caminho + ".tmp";
        try
        {
            await Task.Run(() =>
            {
                using (var destino = File.Create(temporario))
                    gerar(boletim, cabecalho, agora, destino);
                File.Move(temporario, caminho, overwrite: true);
            });
            Mensagem = $"Salvo em {Path.GetFileName(caminho)}" + (boletim.Medicao.Travada ? "." : " (rascunho: os números ainda podem mudar).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporario); } catch (Exception) { /* nada a fazer */ }
            Mensagem = $"Não foi possível salvar: {ex.Message}";
        }
    }
}
