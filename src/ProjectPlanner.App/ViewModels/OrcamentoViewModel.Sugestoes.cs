using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.Core.Orcamento;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Bloco 38: uma sugestão da IA na lista, para aceitar ou não.</summary>
public sealed partial class SugestaoNaTela : ObservableObject
{
    public SugestaoNaTela(SugestaoDeOrcamento sugestao, string grupo, string alvo, string texto, string? motivo)
    {
        (Sugestao, Grupo, Alvo, Texto, Motivo) = (sugestao, grupo, alvo, texto, motivo);
        Aceita = PodeAceitar;
    }

    public SugestaoDeOrcamento Sugestao { get; }

    /// <summary>"Composição", "Estimativa IA", "Quantidade", "Duração", "Equipe", "BDI", "Alerta".</summary>
    public string Grupo { get; }

    /// <summary>"2.1 Alvenaria", o recurso ou "Orçamento".</summary>
    public string Alvo { get; }

    public string Texto { get; }

    public string Justificativa => Sugestao.Justificativa;

    /// <summary>Por que não dá para aplicar (um alerta, uma tarefa que não existe mais…); nulo se dá.</summary>
    public string? Motivo { get; }

    public bool PodeAceitar => Motivo is null;

    public bool EhAlerta => Sugestao.EhAlerta;

    [ObservableProperty] public partial bool Aceita { get; set; }

    partial void OnAceitaChanged(bool value)
    {
        if (value && !PodeAceitar)
            Aceita = false;
    }
}

// Bloco 38: a aba Sugestões da IA: pede ao consultor (com a base escolhida na aba Composições), mostra a lista e aplica
// as aceitas num passo do Desfazer.
public sealed partial class OrcamentoViewModel
{
    public const int AbaDasSugestoes = 3;

    private readonly IConsultorDeOrcamento _consultor;
    private CancellationTokenSource? _consulta;

    [ObservableProperty] public partial int AbaEscolhida { get; set; }

    [ObservableProperty] public partial string Foco { get; set; } = "";

    [ObservableProperty] public partial bool PedirComposicoes { get; set; } = true;

    [ObservableProperty] public partial bool PedirQuantidades { get; set; } = true;

    [ObservableProperty] public partial bool PedirRevisao { get; set; } = true;

    [ObservableProperty] public partial bool PedirEquipes { get; set; } = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PedirSugestoesCommand), nameof(CancelarConsultaCommand), nameof(AplicarSugestoesCommand))]
    public partial bool Consultando { get; private set; }

    /// <summary>O andamento da consulta, o resumo da IA ou o erro.</summary>
    [ObservableProperty] public partial string TextoDaConsulta { get; private set; } = "";

    public ObservableCollection<SugestaoNaTela> Sugestoes { get; } = [];

    public bool SemSugestoes => Sugestoes.Count == 0;

    /// <summary>"Composições da base SINAPI SP 09/2026 (troque na aba Composições)".</summary>
    public string BaseDasSugestoes => BaseEscolhida is { } b
        ? $"As composições vêm da base {b.Texto} (troque na aba Composições); os preços são sempre os da base."
        : "Nenhuma base importada: a IA só pode estimar preços (marcados como \"Estimativa IA\"). Importe a SINAPI na aba Bases para ela escolher composições.";

    public bool IaConfigurada => _consultor.EstaConfigurado;

    private GruposDeSugestao Grupos =>
        (PedirComposicoes ? GruposDeSugestao.Composicoes : 0) | (PedirQuantidades ? GruposDeSugestao.Quantidades : 0) |
        (PedirRevisao ? GruposDeSugestao.Revisao : 0) | (PedirEquipes ? GruposDeSugestao.Equipes : 0);

    private bool PodePedir() => !Consultando;

    [RelayCommand(CanExecute = nameof(PodePedir))]
    private async Task PedirSugestoes()
    {
        if (!_consultor.EstaConfigurado)
        {
            TextoDaConsulta = AnalistaIndisponivel.Mensagem;
            return;
        }
        if (Grupos == GruposDeSugestao.Nenhum)
        {
            TextoDaConsulta = "Marque o que a IA deve sugerir.";
            return;
        }
        if (!_cronograma.Tarefas.Any(t => !t.EhResumo))
        {
            TextoDaConsulta = "O projeto ainda não tem tarefas para orçar.";
            return;
        }
        var tabela = TabelaEscolhida;
        // Bloco 54: numa obra grande, a consulta vai em partes (uma resposta com tudo sairia cortada), e as sugestões se juntam.
        var lotes = ConsultaDeOrcamento.Lotes(_cronograma);
        using var cancelamento = _consulta = new CancellationTokenSource();
        Consultando = true;
        var todas = new List<SugestaoDeOrcamento>();
        var resumos = new List<string>();
        var falhas = new List<string>();
        try
        {
            for (var i = 0; i < lotes.Count; i++)
            {
                var consulta = ConsultaDeOrcamento.Montar(_cronograma, tabela, Foco, Grupos, lotes.Count == 1 ? null : lotes[i]);
                TextoDaConsulta = lotes.Count == 1
                    ? $"Pedindo sugestões à IA ({consulta.Tamanho})… isso pode levar um minuto."
                    : $"Pedindo sugestões à IA: parte {i + 1} de {lotes.Count} ({consulta.Tamanho})… cada parte leva cerca de um minuto.";
                try
                {
                    var resposta = await _consultor.SugerirAsync(consulta.Pedido, cancelamento.Token);
                    Juntar(todas, consulta.Traduzir(resposta));
                    if (resposta.Resumo.Length > 0)
                        resumos.Add(resposta.Resumo);
                    if (lotes.Count > 1)
                        Mostrar(todas, tabela); // o que já chegou aparece enquanto as outras partes vêm
                }
                catch (ErroDoAssistenteException ex) when (lotes.Count > 1)
                {
                    falhas.Add($"A parte {i + 1} de {lotes.Count} não veio ({ex.Message}).");
                }
            }
            Mostrar(todas, tabela);
            TextoDaConsulta = (resumos.Count == 0 ? "" : (lotes.Count == 1 ? resumos[0] : $"Sugestões em {lotes.Count} partes. {resumos[0]}") + " ") +
                              (falhas.Count == 0 ? "" : string.Join(" ", falhas) + " Peça de novo para completar. ") + ContarSugestoes();
        }
        catch (OperationCanceledException)
        {
            TextoDaConsulta = "Consulta cancelada.";
        }
        catch (ErroDoAssistenteException ex)
        {
            TextoDaConsulta = ex.Message;
        }
        finally
        {
            _consulta = null;
            Consultando = false;
        }
    }

    [RelayCommand(CanExecute = nameof(Consultando))]
    private void CancelarConsulta() => _consulta?.Cancel();

    /// <summary>A janela fechou: a consulta em andamento é cancelada.</summary>
    public void Fechando() => _consulta?.Cancel();

    private void Mostrar(IReadOnlyList<SugestaoDeOrcamento> sugestoes, TabelaDeReferencia? tabela)
    {
        foreach (var s in Sugestoes)
            s.PropertyChanged -= SugestaoMudou;
        Sugestoes.Clear();
        var edt = _cronograma.NumeracaoEdt();
        foreach (var s in sugestoes.OrderBy(s => s.TarefaId is { } id ? _cronograma.Tarefas.ToList().FindIndex(t => t.Id == id) : int.MaxValue)
                     .ThenBy(s => s.Tipo))
        {
            var tarefa = s.TarefaId is { } id ? _cronograma.Tarefas.FirstOrDefault(t => t.Id == id) : null;
            var alvo = tarefa is not null ? $"{edt[tarefa.Id]} {tarefa.Nome}"
                : s.RecursoId is { } r && _cronograma.Recursos.Obter(r) is { } recurso ? recurso.Nome
                : "Orçamento";
            var grupo = s.Tipo switch
            {
                TipoDeSugestao.Composicao => "Composição",
                TipoDeSugestao.Estimativa => "Estimativa IA",
                TipoDeSugestao.Quantidade => "Quantidade",
                TipoDeSugestao.Duracao => "Duração",
                TipoDeSugestao.Disponivel => "Equipe",
                TipoDeSugestao.Bdi => "BDI",
                _ => "Alerta",
            };
            var item = new SugestaoNaTela(s, grupo, alvo, AplicacaoDeSugestoes.Texto(s, tabela, _cronograma),
                s.EhAlerta ? null : AplicacaoDeSugestoes.Motivo(_cronograma, tabela, s));
            item.PropertyChanged += SugestaoMudou;
            Sugestoes.Add(item);
        }
        OnPropertyChanged(nameof(SemSugestoes));
        AplicarSugestoesCommand.NotifyCanExecuteChanged();
    }

    private void SugestaoMudou(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SugestaoNaTela.Aceita))
            AplicarSugestoesCommand.NotifyCanExecuteChanged();
    }

    // Bloco 54: as sugestões de uma parte entram nas das anteriores (um BDI só; alertas iguais, uma vez).
    private static void Juntar(List<SugestaoDeOrcamento> todas, IReadOnlyList<SugestaoDeOrcamento> novas)
    {
        foreach (var s in novas)
        {
            if (s.Tipo == TipoDeSugestao.Bdi && todas.Any(t => t.Tipo == TipoDeSugestao.Bdi))
                continue;
            if (s.Tipo == TipoDeSugestao.Alerta && todas.Any(t => t.Tipo == TipoDeSugestao.Alerta && t.TarefaId == s.TarefaId && t.Descricao == s.Descricao))
                continue;
            todas.Add(s);
        }
    }

    private string ContarSugestoes()
    {
        var alertas = Sugestoes.Count(s => s.EhAlerta);
        var outras = Sugestoes.Count - alertas;
        if (Sugestoes.Count == 0)
            return "Nenhuma sugestão.";
        return (outras == 1 ? "1 sugestão" : $"{outras} sugestões") + (alertas == 0 ? "" : alertas == 1 ? " e 1 alerta" : $" e {alertas} alertas") +
               ": marque as que quer e clique em Aplicar marcadas.";
    }

    [RelayCommand]
    private void MarcarTodas()
    {
        foreach (var s in Sugestoes.Where(s => s.PodeAceitar && !s.EhAlerta))
            s.Aceita = true;
    }

    [RelayCommand]
    private void DesmarcarTodas()
    {
        foreach (var s in Sugestoes)
            s.Aceita = false;
    }

    private bool PodeAplicarSugestoes() => !Consultando && Sugestoes.Any(s => s.Aceita && !s.EhAlerta);

    /// <summary>Aplica as marcadas num passo do Desfazer; as aplicadas saem da lista e as recusadas ficam, com o motivo.</summary>
    [RelayCommand(CanExecute = nameof(PodeAplicarSugestoes))]
    private void AplicarSugestoes()
    {
        var tabela = TabelaEscolhida;
        var marcadas = Sugestoes.Where(s => s.Aceita && !s.EhAlerta).ToList();
        var resultado = AplicacaoDeSugestoes.Aplicar(_cronograma, tabela, marcadas.Select(s => s.Sugestao));
        if (resultado.Aplicadas > 0)
            _mudou("Sugestões da IA no orçamento");
        var feitas = resultado.Feitas.ToHashSet(ReferenceEqualityComparer.Instance);
        var restantes = Sugestoes.Where(s => !feitas.Contains(s.Sugestao)).Select(s => s.Sugestao).ToList();
        Mostrar(restantes, tabela);
        (Bdi, BdiDeMateriais) = (Numero(Orcamento.Configuracao.Bdi), Orcamento.Configuracao.BdiDeMateriais is { } m ? Numero(m) : "");
        AtualizarPlanilha();
        AtualizarTarefas();
        AtualizarComposicaoDaTarefa();
        Mensagem = resultado.Resumo + (resultado.Aplicadas > 0 ? " Confira na aba Planilha; Editar > Desfazer volta tudo." : "");
    }

    partial void OnBaseEscolhidaChanged(Escolha<BaseDeReferencia>? oldValue, Escolha<BaseDeReferencia>? newValue) =>
        OnPropertyChanged(nameof(BaseDasSugestoes));
}
