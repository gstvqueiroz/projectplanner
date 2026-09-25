using System.Collections.ObjectModel;
using System.Data.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Models;
using ProjectPlanner.App.Services;
using ProjectPlanner.Core;

namespace ProjectPlanner.App.ViewModels;

/// <summary>
/// A janela principal: o projeto aberto, a grid de tarefas e os comandos da barra de ferramentas.
/// </summary>
/// <remarks>
/// Toda alteração aceita marca o projeto como alterado e recalcula o cronograma na hora, como o MS
/// Project; o comando Recalcular continua disponível para pedir o cálculo explicitamente. Alterações
/// recusadas pelo domínio aparecem como erro no campo e na barra de status, sem exceção na tela.
/// </remarks>
public sealed partial class MainWindowViewModel : ViewModelBase, IEdicaoCronograma
{
    private const string NomePadrao = "Projeto sem título";

    private readonly IProjetoRepository _repositorio;
    private readonly ICalculadoraCronograma _calculadora;
    private readonly IDialogosService _dialogos;
    private readonly TimeProvider _relogio;
    private Cronograma _cronograma;

    public MainWindowViewModel(IProjetoRepository repositorio, ICalculadoraCronograma calculadora,
        IDialogosService dialogos, TimeProvider relogio)
    {
        _repositorio = repositorio;
        _calculadora = calculadora;
        _dialogos = dialogos;
        _relogio = relogio;
        _cronograma = NovoCronograma();
        Carregar(_cronograma, caminho: null);
    }

    public ObservableCollection<TarefaViewModel> Tarefas { get; } = [];

    /// <summary>O Gantt ao lado da grid, refeito a cada recálculo.</summary>
    public GanttViewModel Gantt { get; } = new();

    /// <summary>O projeto aberto (para testes; a tela usa só as propriedades do ViewModel).</summary>
    internal Cronograma Cronograma => _cronograma;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoverTarefaCommand))]
    public partial TarefaViewModel? TarefaSelecionada { get; set; }

    /// <summary>Arquivo do projeto; nulo enquanto ele nunca foi salvo.</summary>
    [ObservableProperty]
    public partial string? CaminhoArquivo { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    public partial bool TemAlteracoesNaoSalvas { get; private set; }

    /// <summary>Texto da barra de status: o último aviso ou erro.</summary>
    [ObservableProperty]
    public partial string Mensagem { get; private set; } = "";

    /// <summary>Fim do projeto no último cálculo.</summary>
    [ObservableProperty]
    public partial DateTime? TerminoProjeto { get; private set; }

    public string Titulo => $"{_cronograma.Nome}{(TemAlteracoesNaoSalvas ? " *" : "")} — ProjectPlanner";

    public string NomeProjeto
    {
        get => ValorExibido(nameof(NomeProjeto), _cronograma.Nome);
        set
        {
            if (AlterarDominio(nameof(NomeProjeto), value, value == _cronograma.Nome, () => _cronograma.Renomear(value)))
                MarcarAlteracao();
            else if (ErroDe(nameof(NomeProjeto)) is { } erro)
                Mensagem = erro;
        }
    }

    public DateTime? InicioProjeto
    {
        get => ValorExibido<DateTime?>(nameof(InicioProjeto), _cronograma.DataInicio);
        set
        {
            var inicio = value;
            if (AlterarDominio(nameof(InicioProjeto), inicio, inicio == _cronograma.DataInicio,
                () => _cronograma.AlterarDataInicio(inicio ?? throw new DomainValidationException(
                    nameof(InicioProjeto), "Informe a data de início do projeto."))))
                MarcarAlteracao();
            else if (ErroDe(nameof(InicioProjeto)) is { } erro)
                Mensagem = erro;
        }
    }

    [RelayCommand]
    private async Task NovoProjeto()
    {
        if (await PodeDescartarAlteracoesAsync())
            Carregar(NovoCronograma(), caminho: null);
    }

    [RelayCommand]
    private async Task AbrirProjeto()
    {
        if (!await PodeDescartarAlteracoesAsync())
            return;
        var caminho = await _dialogos.EscolherArquivoParaAbrirAsync();
        if (caminho is null)
            return;

        try
        {
            Carregar(_repositorio.Abrir(caminho), caminho);
            Mensagem = $"Projeto aberto: {caminho}";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or DbException)
        {
            Mensagem = $"Não foi possível abrir o projeto. {ex.Message}";
        }
    }

    [RelayCommand]
    private Task SalvarProjeto() => SalvarAsync();

    [RelayCommand]
    private void AdicionarTarefa()
    {
        var tarefa = new Tarefa("Nova tarefa", _cronograma.DataInicio, 1);
        _cronograma.AdicionarTarefa(tarefa);
        var linha = new TarefaViewModel(tarefa, Tarefas.Count + 1, this);
        Tarefas.Add(linha);
        TarefaSelecionada = linha;
        MarcarAlteracao();
    }

    [RelayCommand(CanExecute = nameof(PodeRemoverTarefa))]
    private void RemoverTarefa()
    {
        var linha = TarefaSelecionada!;
        var indice = Tarefas.IndexOf(linha);
        _cronograma.RemoverTarefa(linha.Tarefa.Id);
        Tarefas.RemoveAt(indice);
        for (var i = indice; i < Tarefas.Count; i++)
            Tarefas[i].Numero = i + 1;
        TarefaSelecionada = Tarefas.Count == 0 ? null : Tarefas[Math.Min(indice, Tarefas.Count - 1)];
        MarcarAlteracao();
    }

    private bool PodeRemoverTarefa() => TarefaSelecionada is not null;

    [RelayCommand]
    private void RecalcularCronograma()
    {
        if (Recalcular())
            Mensagem = "Cronograma recalculado.";
    }

    /// <summary>Salva no arquivo atual ou, se o projeto nunca foi salvo, pergunta onde salvar.</summary>
    /// <returns>Se o projeto foi salvo.</returns>
    public async Task<bool> SalvarAsync()
    {
        var caminho = CaminhoArquivo ?? await _dialogos.EscolherArquivoParaSalvarAsync(_cronograma.Nome);
        if (caminho is null)
            return false;

        try
        {
            _repositorio.Salvar(_cronograma, caminho);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DbException)
        {
            Mensagem = $"Não foi possível salvar o projeto. {ex.Message}";
            return false;
        }

        CaminhoArquivo = caminho;
        TemAlteracoesNaoSalvas = false;
        Mensagem = $"Projeto salvo em {caminho}";
        return true;
    }

    /// <summary>
    /// Se houver alterações não salvas, pergunta o que fazer com elas (Salvar, Não salvar ou Cancelar).
    /// Usado antes de Novo, Abrir e ao fechar a janela.
    /// </summary>
    /// <returns>Se pode seguir e descartar o projeto atual.</returns>
    public async Task<bool> PodeDescartarAlteracoesAsync()
    {
        if (!TemAlteracoesNaoSalvas)
            return true;

        return await _dialogos.PerguntarSobreAlteracoesPendentesAsync(_cronograma.Nome) switch
        {
            RespostaAlteracoesPendentes.Salvar => await SalvarAsync(),
            RespostaAlteracoesPendentes.NaoSalvar => true,
            _ => false,
        };
    }

    string IEdicaoCronograma.TextoPredecessoras(Tarefa tarefa) =>
        NotacaoPredecessoras.Formatar(_cronograma.PredecessorasDe(tarefa.Id)
            .Select(p => new ItemPredecessora(NumeroDa(p.TarefaOrigemId), p.Tipo, p.Lag)));

    void IEdicaoCronograma.DefinirPredecessoras(Tarefa tarefa, string texto)
    {
        var novas = NotacaoPredecessoras.Interpretar(texto)
            .Select(item => new Predecessora(TarefaDaLinha(item.Linha).Id, tarefa.Id, item.Tipo, item.Lag))
            .ToList();
        var antigas = _cronograma.PredecessorasDe(tarefa.Id);

        foreach (var vinculo in antigas)
            _cronograma.RemoverPredecessora(vinculo.TarefaOrigemId, vinculo.TarefaDestinoId);
        var adicionadas = new List<Predecessora>();
        try
        {
            foreach (var vinculo in novas)
            {
                _cronograma.AdicionarPredecessora(vinculo);
                adicionadas.Add(vinculo);
            }
        }
        catch
        {
            // Desfaz tudo: a tarefa volta a ter exatamente as predecessoras de antes.
            foreach (var vinculo in adicionadas)
                _cronograma.RemoverPredecessora(vinculo.TarefaOrigemId, vinculo.TarefaDestinoId);
            foreach (var vinculo in antigas)
                _cronograma.AdicionarPredecessora(vinculo);
            throw;
        }
    }

    void IEdicaoCronograma.TarefaAlterada() => MarcarAlteracao();

    void IEdicaoCronograma.AlteracaoRecusada(string mensagem) => Mensagem = mensagem;

    void IEdicaoCronograma.EdicaoCancelada() => Mensagem = "";

    private void MarcarAlteracao()
    {
        TemAlteracoesNaoSalvas = true;
        OnPropertyChanged(nameof(Titulo));
        Mensagem = "";
        Recalcular();
    }

    /// <returns>Se o cálculo deu certo.</returns>
    private bool Recalcular()
    {
        var calculou = true;
        try
        {
            var resultado = _calculadora.Calcular(_cronograma);
            TerminoProjeto = resultado.DataTermino;
            Gantt.Atualizar(_cronograma, resultado);
        }
        catch (DomainValidationException ex)
        {
            Mensagem = $"Não foi possível calcular o cronograma. {ex.Message}";
            Gantt.Limpar();
            calculou = false;
        }

        foreach (var linha in Tarefas)
            linha.AtualizarTela();
        return calculou;
    }

    private void Carregar(Cronograma cronograma, string? caminho)
    {
        _cronograma = cronograma;
        Tarefas.Clear();
        foreach (var (tarefa, indice) in cronograma.Tarefas.Select((t, i) => (t, i)))
            Tarefas.Add(new TarefaViewModel(tarefa, indice + 1, this));

        TarefaSelecionada = null;
        CaminhoArquivo = caminho;
        TemAlteracoesNaoSalvas = false;
        Mensagem = "";
        LimparRecusas();
        OnPropertyChanged(nameof(NomeProjeto));
        OnPropertyChanged(nameof(InicioProjeto));
        OnPropertyChanged(nameof(Titulo));
        Recalcular();
    }

    private Cronograma NovoCronograma() => new(NomePadrao, _relogio.GetLocalNow().Date);

    private int NumeroDa(Guid tarefaId) => Tarefas.First(t => t.Tarefa.Id == tarefaId).Numero;

    private Tarefa TarefaDaLinha(int linha) =>
        linha >= 1 && linha <= Tarefas.Count
            ? Tarefas[linha - 1].Tarefa
            : throw new FormatException($"Não existe a linha {linha}.");
}
