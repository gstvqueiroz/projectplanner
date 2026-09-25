using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ProjectPlanner.Core;

namespace ProjectPlanner.App.ViewModels;

/// <summary>O que uma <see cref="TarefaViewModel"/> precisa do projeto em que está.</summary>
public interface IEdicaoCronograma
{
    /// <summary>Predecessoras da tarefa na notação da grid (ver <see cref="Models.NotacaoPredecessoras"/>).</summary>
    string TextoPredecessoras(Tarefa tarefa);

    /// <summary>Troca as predecessoras da tarefa pelas do texto; em caso de erro, não muda nada.</summary>
    /// <exception cref="FormatException">O texto não segue a notação ou cita uma linha que não existe.</exception>
    /// <exception cref="DomainValidationException">Algum vínculo viola as regras do cronograma.</exception>
    void DefinirPredecessoras(Tarefa tarefa, string texto);

    /// <summary>Uma alteração foi aplicada: marca o projeto como alterado e recalcula.</summary>
    void TarefaAlterada();

    /// <summary>Uma alteração foi recusada: mostra a mensagem ao usuário.</summary>
    void AlteracaoRecusada(string mensagem);

    /// <summary>O usuário desistiu de uma edição recusada (Esc ou voltou ao valor em vigor): some com a mensagem.</summary>
    void EdicaoCancelada();
}

/// <summary>
/// Uma linha da grid. Os campos editáveis repassam cada mudança para a <see cref="Core.Tarefa"/>; os
/// calculados mostram o resultado do último cálculo do cronograma.
/// </summary>
/// <remarks>
/// Cada célula é confirmada no domínio ao sair dela. <see cref="IEditableObject"/> existe para o Esc
/// do DataGrid: cancelar a edição da linha descarta os valores recusados, que senão continuariam na
/// tela e prenderiam o usuário na linha inválida.
/// </remarks>
public sealed partial class TarefaViewModel : ViewModelBase, IEditableObject
{
    private static readonly string[] Propriedades =
    [
        nameof(Nome), nameof(Duracao), nameof(DataInicio), nameof(DataTermino), nameof(PercentualConcluido),
        nameof(Predecessoras), nameof(InicioMaisCedo), nameof(TerminoMaisCedo), nameof(InicioMaisTarde),
        nameof(TerminoMaisTarde), nameof(Folga), nameof(EhCritica),
    ];

    private readonly IEdicaoCronograma _edicao;

    public TarefaViewModel(Tarefa tarefa, int numero, IEdicaoCronograma edicao)
    {
        Tarefa = tarefa;
        Numero = numero;
        _edicao = edicao;
    }

    public Tarefa Tarefa { get; }

    /// <summary>Número da linha (1, 2, 3...), usado na notação de predecessoras.</summary>
    [ObservableProperty]
    public partial int Numero { get; set; }

    public string Nome
    {
        get => ValorExibido(nameof(Nome), Tarefa.Nome);
        set => Alterar(nameof(Nome), "Nome", value, value == Tarefa.Nome, () => Tarefa.Renomear(value));
    }

    public int Duracao
    {
        get => ValorExibido(nameof(Duracao), Tarefa.Duracao);
        set => Alterar(nameof(Duracao), "Duração", value, value == Tarefa.Duracao, () => Tarefa.AlterarDuracao(value));
    }

    public DateTime? DataInicio
    {
        get => ValorExibido<DateTime?>(nameof(DataInicio), Tarefa.DataInicio);
        set => Alterar(nameof(DataInicio), "Início", value, value == Tarefa.DataInicio,
            () => Tarefa.AlterarDataInicio(value ?? throw DataObrigatoria(nameof(DataInicio), "início")));
    }

    public DateTime? DataTermino
    {
        get => ValorExibido<DateTime?>(nameof(DataTermino), Tarefa.DataTermino);
        set => Alterar(nameof(DataTermino), "Término", value, value == Tarefa.DataTermino,
            () => Tarefa.AlterarDataTermino(value ?? throw DataObrigatoria(nameof(DataTermino), "término")));
    }

    public int PercentualConcluido
    {
        get => ValorExibido(nameof(PercentualConcluido), Tarefa.PercentualConcluido);
        set => Alterar(nameof(PercentualConcluido), "% concluído", value, value == Tarefa.PercentualConcluido,
            () => Tarefa.AtualizarPercentualConcluido(value));
    }

    public string Predecessoras
    {
        get => ValorExibido(nameof(Predecessoras), _edicao.TextoPredecessoras(Tarefa));
        set => Alterar(nameof(Predecessoras), "Predecessoras", value,
            (value ?? "").Trim() == _edicao.TextoPredecessoras(Tarefa),
            () => _edicao.DefinirPredecessoras(Tarefa, value ?? ""));
    }

    // Resultado do último cálculo do cronograma (somente leitura).
    public DateTime? InicioMaisCedo => Tarefa.InicioMaisCedo;
    public DateTime? TerminoMaisCedo => Tarefa.TerminoMaisCedo;
    public DateTime? InicioMaisTarde => Tarefa.InicioMaisTarde;
    public DateTime? TerminoMaisTarde => Tarefa.TerminoMaisTarde;
    public int? Folga => Tarefa.Folga;
    public bool EhCritica => Tarefa.EhCritica;

    /// <summary>Avisa a tela de que os dados da tarefa podem ter mudado (depois de um recálculo, por exemplo).</summary>
    public void AtualizarTela()
    {
        foreach (var propriedade in Propriedades)
            OnPropertyChanged(propriedade);
    }

    void IEditableObject.BeginEdit()
    {
    }

    void IEditableObject.EndEdit()
    {
    }

    /// <summary>Esquece os valores recusados e volta a mostrar os que estão em vigor.</summary>
    void IEditableObject.CancelEdit()
    {
        if (!HasErrors)
            return;
        LimparRecusas();
        _edicao.EdicaoCancelada();
    }

    private void Alterar<T>(string propriedade, string rotulo, T valor, bool igualAoAtual, Action alteracao)
    {
        var estavaRecusado = ErroDe(propriedade) is not null;
        if (AlterarDominio(propriedade, valor, igualAoAtual, alteracao))
            _edicao.TarefaAlterada();
        else if (ErroDe(propriedade) is { } erro)
            _edicao.AlteracaoRecusada($"Linha {Numero}, {rotulo}: {erro} Corrija o valor ou tecle Esc para desfazer.");
        else if (estavaRecusado)
            _edicao.EdicaoCancelada(); // voltou ao valor em vigor (é o que o DataGrid faz no Esc)
    }

    private static DomainValidationException DataObrigatoria(string propriedade, string qual) =>
        new(propriedade, $"Informe a data de {qual}.");
}
