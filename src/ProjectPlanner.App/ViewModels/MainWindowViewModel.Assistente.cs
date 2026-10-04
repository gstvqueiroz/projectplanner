using ProjectPlanner.App.Services;
using ProjectPlanner.Core;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.ViewModels;

// Bloco 24: o assistente altera o cronograma. As alterações de uma resposta são aplicadas numa cópia do cronograma; se
// todas valem (e o cálculo fecha), a cópia entra no lugar do aberto, e o de antes fica guardado para o Desfazer.
public sealed partial class MainWindowViewModel : ICronogramaDoAssistente
{
    /// <summary>A última aplicação do assistente: o cronograma de antes dela e a versão do projeto logo depois.</summary>
    private sealed record AplicacaoDoAssistente(Cronograma Anterior, int Versao);

    private AplicacaoDoAssistente? _ultimaAplicacao;

    /// <summary>Muda a cada alteração do projeto: diz se o cronograma mudou depois de uma aplicação do assistente.</summary>
    private int _versao;

    public event Action? DesfazerMudou;

    ContextoDoProjeto ICronogramaDoAssistente.Retrato() => RetratoParaOAssistente();

    public async Task<ResultadoDaAplicacao> AplicarAsync(IReadOnlyList<AlteracaoDoCronograma> alteracoes, ContextoDoProjeto retrato)
    {
        ArgumentNullException.ThrowIfNull(alteracoes);
        ArgumentNullException.ThrowIfNull(retrato);
        var copia = _cronograma.Copiar();
        ResumoDasAlteracoes resumo;
        try
        {
            resumo = AplicadorDeAlteracoes.Aplicar(copia, alteracoes, retrato);
            _calculadora.Calcular(copia); // o cronograma novo precisa fechar no cálculo
        }
        catch (AlteracaoRecusadaException ex)
        {
            return new ResultadoDaAplicacao(SituacaoDaAplicacao.Recusada, ex.Message);
        }
        catch (DomainValidationException ex)
        {
            return new ResultadoDaAplicacao(SituacaoDaAplicacao.Recusada, $"O cronograma resultante não pôde ser calculado: {ex.Message}");
        }

        // Fotos não voltam sozinhas: remover tarefas com fotos pede confirmação (o Desfazer as traz de volta).
        var ficam = copia.Tarefas.Select(t => t.Id).ToHashSet();
        var comFotos = _cronograma.Tarefas.Where(t => t.Fotos.Count > 0 && !ficam.Contains(t.Id)).ToList();
        if (comFotos.Count > 0)
        {
            var fotos = comFotos.Sum(t => t.Fotos.Count);
            var pergunta = comFotos.Count == 1
                ? $"As alterações do assistente removem a tarefa \"{comFotos[0].Nome}\", que tem " +
                  (fotos == 1 ? "1 foto" : $"{fotos} fotos") + ". Aplicar mesmo assim?"
                : $"As alterações do assistente removem {comFotos.Count} tarefas com fotos ({fotos} fotos no total). Aplicar mesmo assim?";
            if (!await _dialogos.ConfirmarAsync("Alterações do assistente", pergunta, "Aplicar"))
                return new ResultadoDaAplicacao(SituacaoDaAplicacao.Cancelada,
                    "Alterações não aplicadas: elas removeriam tarefas com fotos.");
        }

        if (resumo.Vazio)
            return new ResultadoDaAplicacao(SituacaoDaAplicacao.Aplicada, resumo.Texto);
        var anterior = _cronograma;
        TrocarCronograma(copia);
        _ultimaAplicacao = new AplicacaoDoAssistente(anterior, _versao);
        Mensagem = $"Assistente: {resumo.Texto}.";
        DesfazerMudou?.Invoke();
        return new ResultadoDaAplicacao(SituacaoDaAplicacao.Aplicada, resumo.Texto, _ultimaAplicacao);
    }

    public bool PodeDesfazer(object marca) => _ultimaAplicacao is not null && ReferenceEquals(marca, _ultimaAplicacao);

    public async Task<bool> DesfazerAsync(object marca)
    {
        if (!PodeDesfazer(marca))
            return false;
        var aplicacao = _ultimaAplicacao!;
        if (_versao != aplicacao.Versao && !await _dialogos.ConfirmarAsync("Desfazer",
                "O cronograma mudou depois desta resposta do assistente. Desfazer volta para como ele estava antes dela, " +
                "e essas mudanças também se perdem. Desfazer mesmo assim?", "Desfazer"))
            return false;
        _ultimaAplicacao = null;
        TrocarCronograma(aplicacao.Anterior);
        Mensagem = "Alterações do assistente desfeitas.";
        DesfazerMudou?.Invoke();
        return true;
    }

    /// <summary>Outro projeto (Novo, Abrir): não há mais o que desfazer.</summary>
    private void EsquecerAplicacaoDoAssistente()
    {
        if (_ultimaAplicacao is null)
            return;
        _ultimaAplicacao = null;
        DesfazerMudou?.Invoke();
    }

    // Põe outro cronograma (do mesmo projeto) no lugar do aberto: as linhas são refeitas, mantendo recolhidos os resumos
    // que estavam recolhidos e a seleção, se a tarefa continua lá. A conversa e as imagens das fotos ficam.
    private void TrocarCronograma(Cronograma novo)
    {
        var recolhidos = Tarefas.Where(l => !l.Expandido).Select(l => l.Tarefa.Id).ToHashSet();
        var selecionada = TarefaSelecionada?.Tarefa.Id;
        _cronograma = novo;
        _mudandoEstrutura = true;
        try
        {
            Tarefas.Clear();
            SincronizarLinhas();
            foreach (var linha in Tarefas.Where(l => recolhidos.Contains(l.Tarefa.Id) && l.EhResumo))
                linha.Expandido = false;
        }
        finally
        {
            _mudandoEstrutura = false;
        }
        AtualizarLinhasVisiveis(avisarOGantt: true);
        TarefaSelecionada = selecionada is { } id ? LinhasVisiveis.FirstOrDefault(l => l.Tarefa.Id == id) : null;
        LimparRecusas();
        OnPropertyChanged(nameof(NomeProjeto));
        OnPropertyChanged(nameof(InicioProjeto));
        MarcarAlteracao();
    }
}
