using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Planejamento;
using ProjectPlanner.Core.Recursos;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Um recurso na tarefa: a quantidade (editável), o total e o custo.</summary>
public sealed partial class AtribuicaoNaTela(Recurso recurso, AtribuicaoDeRecurso atribuicao, decimal total, decimal custo, Action<AtribuicaoNaTela, string> mudar)
    : ViewModelBase
{
    public Recurso Recurso { get; } = recurso;

    public AtribuicaoDeRecurso Atribuicao { get; } = atribuicao;

    public string Nome => Recurso.Nome;

    public string Tipo => TiposDeRecurso.Texto(Recurso.Tipo);

    /// <summary>"por dia" (profissional, equipamento) ou a unidade do material ("sc").</summary>
    public string ComoMede => Recurso.PorDia ? "por dia" : Recurso.Unidade;

    public string Quantidade
    {
        get => RecursosDoProjetoTexto.Numero(Atribuicao.Quantidade);
        set => mudar(this, value ?? "");
    }

    /// <summary>"10 dias" ou "120 sc".</summary>
    public string Total => $"{RecursosDoProjetoTexto.Numero(total)} {Recurso.Unidade}{(Recurso.PorDia && total != 1 ? "s" : "")}";

    public string Custo => Moeda.Formatar(custo);

    /// <summary>Volta a mostrar o valor em vigor (depois de uma recusa).</summary>
    public void Desfazer() => OnPropertyChanged(nameof(Quantidade));
}

/// <summary>
/// Bloco 32: Tarefa > Recursos da tarefa…: os recursos de uma tarefa (quantos por dia, nos profissionais e equipamentos; o
/// total, nos materiais), com o total, o custo, o custo orçado da tarefa e a margem sobre o Valor. Cada mudança é um passo
/// do Desfazer.
/// </summary>
public sealed partial class RecursosDaTarefaViewModel : ViewModelBase
{
    private readonly Cronograma _cronograma;
    private readonly Tarefa _tarefa;
    private readonly Action<string> _mudou;

    public RecursosDaTarefaViewModel(Cronograma cronograma, Tarefa tarefa, Action<string> mudou)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(tarefa);
        ArgumentNullException.ThrowIfNull(mudou);
        (_cronograma, _tarefa, _mudou) = (cronograma, tarefa, mudou);
        Atualizar();
    }

    private RecursosDoProjeto Recursos => _cronograma.Recursos;

    public string Titulo => $"Recursos de \"{_tarefa.Nome}\"";

    /// <summary>"5 dias úteis · Valor R$ 5.000,00".</summary>
    public string Detalhe => (_tarefa.Duracao == 1 ? "1 dia útil" : $"{_tarefa.Duracao} dias úteis") +
                             (_tarefa.Valor is { } v ? $" · Valor {Moeda.ComSimbolo(v)}" : " · sem Valor") +
                             (_tarefa.EhMarco ? " · num marco, os recursos por dia não somam (0 dias)" : "");

    public ObservableCollection<AtribuicaoNaTela> Linhas { get; } = [];

    public bool SemLinhas => Linhas.Count == 0;

    public bool SemRecursosNoProjeto => Recursos.Vazio;

    public const string TextoSemRecursosNoProjeto = "O projeto ainda não tem recursos: cadastre-os em Projeto > Recursos….";

    /// <summary>Os recursos do projeto que ainda não estão na tarefa.</summary>
    public IReadOnlyList<Escolha<Guid>> Disponiveis { get; private set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AdicionarCommand))]
    [NotifyPropertyChangedFor(nameof(RotuloDaQuantidade))]
    public partial Escolha<Guid>? RecursoEscolhido { get; set; }

    [ObservableProperty] public partial string QuantidadeNova { get; set; } = "";

    /// <summary>"Quantos por dia" ou "Quantidade (sc)".</summary>
    public string RotuloDaQuantidade => RecursoEscolhido is { } e && Recursos.Obter(e.Valor) is { } r && !r.PorDia ? $"Quantidade ({r.Unidade})" : "Quantos por dia";

    public string CustoOrcado => Recursos.CustoOrcado(_tarefa) is { } c ? Moeda.ComSimbolo(c) : "—";

    public string Margem => _tarefa.Valor is { } v && Recursos.CustoOrcado(_tarefa) is { } c ? Moeda.ComSimbolo(v - c) : "—";

    public bool MargemNegativa => _tarefa.Valor is { } v && Recursos.CustoOrcado(_tarefa) is { } c && c > v;

    [ObservableProperty] public partial string Mensagem { get; private set; } = "";

    private void Atualizar()
    {
        Linhas.Clear();
        foreach (var a in _tarefa.Atribuicoes)
            if (Recursos.Obter(a.RecursoId) is { } r)
                Linhas.Add(new AtribuicaoNaTela(r, a, Recursos.Total(_tarefa, a), Recursos.Custo(_tarefa, a), Mudar));
        var naTarefa = _tarefa.Atribuicoes.Select(a => a.RecursoId).ToHashSet();
        Disponiveis = [.. Recursos.Lista.Where(r => !naTarefa.Contains(r.Id))
            .Select(r => new Escolha<Guid>(r.Id, $"{r.Nome} ({TiposDeRecurso.Texto(r.Tipo).ToLowerInvariant()}, {Moeda.ComSimbolo(r.CustoUnitario)}/{r.Unidade})"))];
        RecursoEscolhido = Disponiveis.FirstOrDefault();
        foreach (var nome in new[] { nameof(Disponiveis), nameof(SemLinhas), nameof(CustoOrcado), nameof(Margem), nameof(MargemNegativa), nameof(SemRecursosNoProjeto) })
            OnPropertyChanged(nome);
    }

    private bool Aplicar(IEnumerable<AtribuicaoDeRecurso> atribuicoes)
    {
        try
        {
            Recursos.Atribuir(_tarefa, atribuicoes);
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return false;
        }
        _mudou($"Recursos de \"{_tarefa.Nome}\"");
        Mensagem = "";
        Atualizar();
        return true;
    }

    private bool PodeAdicionar() => RecursoEscolhido is not null;

    [RelayCommand(CanExecute = nameof(PodeAdicionar))]
    private void Adicionar()
    {
        AtribuicaoDeRecurso nova;
        try
        {
            nova = new AtribuicaoDeRecurso(RecursoEscolhido!.Valor, Moeda.LerNumero(QuantidadeNova) ?? 0);
        }
        catch (Exception ex) when (ex is DomainValidationException or FormatException)
        {
            Mensagem = ex.Message;
            return;
        }
        if (Aplicar([.. _tarefa.Atribuicoes, nova]))
            QuantidadeNova = "";
    }

    [RelayCommand]
    private void Remover(AtribuicaoNaTela? linha)
    {
        if (linha is not null)
            Aplicar(_tarefa.Atribuicoes.Where(a => a.RecursoId != linha.Recurso.Id));
    }

    private void Mudar(AtribuicaoNaTela linha, string texto)
    {
        decimal? quantidade;
        try
        {
            quantidade = Moeda.LerNumero(texto);
        }
        catch (FormatException ex)
        {
            Mensagem = ex.Message;
            linha.Desfazer();
            return;
        }
        if (quantidade == linha.Atribuicao.Quantidade)
            return;
        if (quantidade is null or 0)
        {
            Remover(linha);
            return;
        }
        try
        {
            var nova = new AtribuicaoDeRecurso(linha.Recurso.Id, quantidade.Value);
            if (!Aplicar(_tarefa.Atribuicoes.Select(a => a.RecursoId == linha.Recurso.Id ? nova : a)))
                linha.Desfazer();
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            linha.Desfazer();
        }
    }
}
