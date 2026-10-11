using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Indicadores;
using ProjectPlanner.Core.Planejamento;
using ProjectPlanner.Relatorios;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Um custo na lista da tela.</summary>
public sealed record CustoNaLista(LancamentoDeCusto Lancamento, string Tarefa)
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    public string Data => Lancamento.Data.ToString("dd/MM/yyyy", Brasil);

    public string Categoria => Categorias.Texto(Lancamento.Categoria);

    public string Descricao => Lancamento.Descricao;

    public string Documento => Lancamento.Documento ?? "";

    public string Valor => Moeda.Formatar(Lancamento.Valor);
}

/// <summary>
/// Bloco 37: Projeto > Custos realizados…: as notas, as folhas e as locações da obra (data, tarefa ou custo geral,
/// categoria, descrição, documento e valor), com os totais por categoria e por mês, e a importação de uma planilha. Cada
/// mudança é um passo do Desfazer.
/// </summary>
public sealed partial class CustosViewModel : ViewModelBase
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly Escolha<Guid?> CustoGeral = new(null, "— Custo geral da obra —");

    private readonly Cronograma _cronograma;
    private readonly IDialogosService _dialogos;
    private readonly TimeProvider _relogio;
    private readonly Action<string> _mudou;
    private bool _carregando;

    /// <param name="mudou">Os custos mudaram, com o nome da mudança para o Desfazer.</param>
    public CustosViewModel(Cronograma cronograma, IDialogosService dialogos, TimeProvider relogio, Action<string> mudou)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(dialogos);
        ArgumentNullException.ThrowIfNull(relogio);
        ArgumentNullException.ThrowIfNull(mudou);
        (_cronograma, _dialogos, _relogio, _mudou) = (cronograma, dialogos, relogio, mudou);
        var edt = cronograma.NumeracaoEdt();
        Tarefas = [CustoGeral, .. cronograma.Tarefas.Select(t => new Escolha<Guid?>(t.Id, $"{edt[t.Id]} {t.Nome}"))];
        Categorias = [.. Core.Indicadores.Categorias.Todas.Select(c => new Escolha<CategoriaDeCusto>(c, Core.Indicadores.Categorias.Texto(c)))];
        Novo();
        Atualizar();
    }

    public string Titulo => $"Custos realizados — {_cronograma.Nome}";

    public ObservableCollection<CustoNaLista> Lista { get; } = [];

    public bool SemCustos => Lista.Count == 0;

    public const string TextoSemCustos =
        "Nenhum custo ainda. Lance as notas, as folhas e as locações da obra (ou importe a planilha do financeiro) para ver o custo real e o IDC nos indicadores.";

    [ObservableProperty] public partial string Mensagem { get; private set; } = "";

    // ---- O formulário ----

    public IReadOnlyList<Escolha<Guid?>> Tarefas { get; }

    public IReadOnlyList<Escolha<CategoriaDeCusto>> Categorias { get; }

    [ObservableProperty] public partial CustoNaLista? Selecionado { get; set; }

    [ObservableProperty] public partial DateTime? Data { get; set; }

    [ObservableProperty] public partial Escolha<Guid?> TarefaEscolhida { get; set; } = null!;

    [ObservableProperty] public partial Escolha<CategoriaDeCusto> CategoriaEscolhida { get; set; } = null!;

    [ObservableProperty] public partial string Descricao { get; set; } = "";

    [ObservableProperty] public partial string Documento { get; set; } = "";

    [ObservableProperty] public partial string Valor { get; set; } = "";

    /// <summary>"Salvar alteração" com um custo escolhido; senão, "Adicionar".</summary>
    public string TextoDoSalvar => Selecionado is null ? "Adicionar" : "Salvar alteração";

    partial void OnSelecionadoChanged(CustoNaLista? value)
    {
        OnPropertyChanged(nameof(TextoDoSalvar));
        RemoverCommand.NotifyCanExecuteChanged();
        if (_carregando || value is not { Lancamento: var l })
            return;
        Data = l.Data.ToDateTime(TimeOnly.MinValue);
        TarefaEscolhida = Tarefas.FirstOrDefault(t => t.Valor == l.TarefaId) ?? CustoGeral;
        CategoriaEscolhida = Categorias.First(c => c.Valor == l.Categoria);
        (Descricao, Documento, Valor) = (l.Descricao, l.Documento ?? "", Moeda.Formatar(l.Valor));
    }

    /// <summary>Limpa o formulário para um custo novo (a data de hoje, a mesma categoria).</summary>
    [RelayCommand]
    private void Novo()
    {
        Selecionado = null;
        Data = _relogio.GetLocalNow().Date;
        TarefaEscolhida = CustoGeral;
        CategoriaEscolhida ??= Categorias.First(c => c.Valor == CategoriaDeCusto.Material);
        (Descricao, Documento, Valor) = ("", "", "");
    }

    /// <summary>Adiciona o custo do formulário, ou salva a alteração do escolhido.</summary>
    [RelayCommand]
    private void Salvar()
    {
        LancamentoDeCusto lancamento;
        try
        {
            if (Data is not { } data)
                throw new DomainValidationException(nameof(Data), "Informe a data do custo.");
            lancamento = new LancamentoDeCusto(Selecionado?.Lancamento.Id ?? Guid.NewGuid(), DateOnly.FromDateTime(data), TarefaEscolhida.Valor,
                CategoriaEscolhida.Valor, Descricao, Documento, Moeda.Ler(Valor) ?? 0);
        }
        catch (Exception ex) when (ex is DomainValidationException or FormatException)
        {
            Mensagem = ex.Message;
            return;
        }
        var alterando = Selecionado is not null;
        if (alterando)
            _cronograma.Custos.Substituir(lancamento);
        else
            _cronograma.Custos.Adicionar(lancamento);
        _mudou(alterando ? $"Alterar o custo \"{lancamento.Descricao}\"" : $"Custo \"{lancamento.Descricao}\"");
        Atualizar();
        Mensagem = $"{(alterando ? "Alterado" : "Lançado")}: {lancamento.Descricao} ({Moeda.ComSimbolo(lancamento.Valor)}).";
        Novo();
    }

    private bool TemSelecionado() => Selecionado is not null;

    [RelayCommand(CanExecute = nameof(TemSelecionado))]
    private async Task Remover()
    {
        var custo = Selecionado!.Lancamento;
        if (!await _dialogos.ConfirmarAsync("Remover custo", $"Remover \"{custo.Descricao}\" ({Moeda.ComSimbolo(custo.Valor)})? Dá para desfazer com Editar > Desfazer.", "Remover"))
            return;
        _cronograma.Custos.Remover(custo.Id);
        _mudou($"Remover o custo \"{custo.Descricao}\"");
        Atualizar();
        Novo();
        Mensagem = $"Removido: {custo.Descricao}.";
    }

    /// <summary>Acrescenta os custos de uma planilha (.xlsx ou .csv) com as colunas Data e Valor (e Descrição, EDT, Categoria e Documento).</summary>
    [RelayCommand]
    private async Task ImportarPlanilha()
    {
        if (await _dialogos.EscolherPlanilhaAsync() is not { } caminho)
            return;
        try
        {
            var linhas = await Task.Run(() => LeitorDePlanilha.Ler(caminho));
            var resultado = ImportacaoDeCustos.Aplicar(_cronograma, linhas);
            if (resultado.Importados > 0)
                _mudou($"Importar custos de {Path.GetFileName(caminho)}");
            Atualizar();
            Mensagem = resultado.Resumo;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Mensagem = $"Não foi possível ler a planilha {Path.GetFileName(caminho)}: {ex.Message}";
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
        }
    }

    // ---- Lista e totais ----

    public string Total => Moeda.ComSimbolo(_cronograma.Custos.Total);

    public IReadOnlyList<LinhaDoResumoNaTela> PorCategoria { get; private set; } = [];

    public IReadOnlyList<LinhaDoResumoNaTela> PorMes { get; private set; } = [];

    private void Atualizar()
    {
        var edt = _cronograma.NumeracaoEdt();
        var nomes = _cronograma.Tarefas.ToDictionary(t => t.Id, t => $"{edt[t.Id]} {t.Nome}");
        _carregando = true;
        try
        {
            Lista.Clear();
            foreach (var l in _cronograma.Custos.Lancamentos)
                Lista.Add(new CustoNaLista(l, l.TarefaId is { } id ? nomes.GetValueOrDefault(id, "(tarefa removida)") : "Custo geral"));
        }
        finally
        {
            _carregando = false;
        }
        var lancamentos = _cronograma.Custos.Lancamentos;
        PorCategoria = [.. lancamentos.GroupBy(l => l.Categoria).OrderBy(g => g.Key)
            .Select(g => new LinhaDoResumoNaTela(Core.Indicadores.Categorias.Texto(g.Key), Moeda.ComSimbolo(g.Sum(l => l.Valor))))];
        PorMes = [.. lancamentos.GroupBy(l => new DateOnly(l.Data.Year, l.Data.Month, 1)).OrderBy(g => g.Key)
            .Select(g => new LinhaDoResumoNaTela(g.Key.ToString("MMM/yy", Brasil).Replace(".", ""), Moeda.ComSimbolo(g.Sum(l => l.Valor))))];
        foreach (var nome in new[] { nameof(SemCustos), nameof(Total), nameof(PorCategoria), nameof(PorMes) })
            OnPropertyChanged(nome);
    }
}
