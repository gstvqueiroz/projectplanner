using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.Core.Assistente;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.ViewModels;

// Bloco 70 (saiu de AssistenteViewModel.cs): a condução da conversa (o modo, as perguntas e o plano).
public sealed partial class AssistenteViewModel
{
    // ---- Bloco 26: a condução (modo, perguntas, plano) ----

    private ConducaoDaConversa Conducao(bool planoAgora) => new(
        _conversa.Modo switch
        {
            ModoDaConversa.Direto => ModoDeTrabalho.Direto,
            ModoDaConversa.Estudo => ModoDeTrabalho.Estudo,
            _ => ModoDeTrabalho.NaoEscolhido,
        },
        _configuracao.PreferenciaDeModo,
        [.. _conversa.Ficha.Itens.Select(i => new ItemDeFicha(i.Campo, i.Valor))],
        _conversa.RodadasDePerguntas,
        _conversa.PlanoAprovado,
        planoAgora);

    // O que a resposta muda na condução: a ficha, o modo (perguntas e plano são do estudo) e o fim do estudo (o plano
    // aprovado virou cronograma).
    private void Conduzir(RespostaIA resposta, AlteracoesNaConversa? alteracoes)
    {
        var mudou = _conversa.Ficha.Mesclar(resposta.Ficha.Select(i => new ItemDaFicha(i.Campo, i.Valor))) > 0;
        if (resposta.Tipo is TipoDeResposta.Perguntas or TipoDeResposta.Plano && _conversa.Modo == ModoDaConversa.NaoEscolhido)
            mudou |= _conversa.DefinirModo(ModoDaConversa.Estudo);
        if (resposta.Tipo == TipoDeResposta.Plano)
            mudou |= _conversa.DefinirPlanoAprovado(false); // um plano novo precisa de aprovação nova
        if (_conversa.PlanoAprovado && alteracoes is { Aplicadas: true, Resultado.Marca: not null })
            mudou |= _conversa.DefinirModo(ModoDaConversa.Direto); // estudo concluído: daqui em diante, ajustes diretos
        if (mudou)
        {
            _cronograma.ConversaMudou();
            AtualizarFicha();
        }
    }

    /// <summary>A última mensagem do assistente, a única com o cartão, as perguntas ou o plano ativos.</summary>
    public MensagemNaTela? Ultima => Mensagens.LastOrDefault(m => !m.DoUsuario && !m.EhErro);

    private bool EhAUltimaAtiva(MensagemNaTela? mensagem) => !Aguardando && mensagem is not null && ReferenceEquals(mensagem, Ultima)
                                                              && ReferenceEquals(mensagem, Mensagens.LastOrDefault());

    private bool PodeEscolherModo(string? modo) => EhAUltimaAtiva(Ultima) && Ultima!.EscolheModo && modo is "direto" or "estudo";

    /// <summary>O cartão "Como você prefere?": "direto" monta agora; "estudo" estuda e conversa antes. Vale para a conversa.</summary>
    [RelayCommand(CanExecute = nameof(PodeEscolherModo))]
    private Task EscolherModo(string? modo)
    {
        var estudo = modo == "estudo";
        if (_conversa.DefinirModo(estudo ? ModoDaConversa.Estudo : ModoDaConversa.Direto))
            _cronograma.ConversaMudou();
        AtualizarEstudo();
        return EnviarAsync(estudo ? TextoEstudarAntes : TextoMontarAgora, []);
    }

    private bool PodeResponderPerguntas(MensagemNaTela? mensagem) =>
        EhAUltimaAtiva(mensagem) && mensagem!.TemPerguntas
        && (mensagem.Perguntas.Any(p => p.Respondida) || !string.IsNullOrWhiteSpace(TextoDigitado));

    /// <summary>Manda as respostas escolhidas ou escritas (com o texto da caixa, se houver, como observação).</summary>
    [RelayCommand(CanExecute = nameof(PodeResponderPerguntas))]
    private Task ResponderPerguntas(MensagemNaTela? mensagem)
    {
        var texto = "Respostas:\n" + string.Join("\n", mensagem!.Perguntas.Select(p => $"- {p.Texto} {p.Resposta ?? PerguntaNaTela.SemResposta}"));
        if (!string.IsNullOrWhiteSpace(TextoDigitado))
            texto += "\n" + TextoDigitado.Trim();
        var anexos = AnexosPendentes.ToList();
        TextoDigitado = "";
        AnexosPendentes.Clear();
        AvisoDeAnexo = "";
        return EnviarAsync(texto, anexos);
    }

    private bool PodeTratarOPlano(MensagemNaTela? mensagem) => EhAUltimaAtiva(mensagem) && mensagem!.TemPlano;

    /// <summary>Aprova o plano: o assistente monta o cronograma completo seguindo ele.</summary>
    [RelayCommand(CanExecute = nameof(PodeTratarOPlano))]
    private Task AprovarPlano(MensagemNaTela? mensagem)
    {
        _conversa.DefinirModo(ModoDaConversa.Estudo);
        if (_conversa.DefinirPlanoAprovado(true))
            _cronograma.ConversaMudou();
        AtualizarEstudo();
        return EnviarAsync(TextoPlanoAprovado, []);
    }

    /// <summary>Começa uma mensagem de ajuste do plano na caixa de texto, para o usuário completar.</summary>
    [RelayCommand(CanExecute = nameof(PodeTratarOPlano))]
    private void AjustarPlano(MensagemNaTela? mensagem)
    {
        if (!TextoDigitado.StartsWith(PrefixoDoAjuste, StringComparison.Ordinal))
            TextoDigitado = PrefixoDoAjuste + TextoDigitado;
        FocoNaEntradaPedido?.Invoke();
    }

    /// <summary>A view põe o cursor na caixa de texto (Ajustar o plano).</summary>
    public event Action? FocoNaEntradaPedido;

    private bool PodePularParaOPlano() => !Aguardando && EmEstudo && !_conversa.PlanoAprovado && Ultima?.Tipo != TipoDeResposta.Plano
                                          && Mensagens.Count > 0;

    /// <summary>Para as perguntas: o assistente apresenta o plano com o que já tem (premissas comuns no resto).</summary>
    [RelayCommand(CanExecute = nameof(PodePularParaOPlano))]
    private Task PularParaOPlano() => EnviarAsync(TextoPularParaOPlano, [], planoAgora: true);

    /// <summary>No modo de estudo (desde a escolha até o cronograma ser montado).</summary>
    public bool EmEstudo => _conversa.Modo == ModoDaConversa.Estudo;

    /// <summary>"Estudo · perguntas (2 de 4)": onde o estudo está; vazio fora dele.</summary>
    public string EtapaDoEstudo => _conversa.Modo switch
    {
        ModoDaConversa.Estudo when _conversa.PlanoAprovado => "Estudo · montando o cronograma",
        ModoDaConversa.Estudo when Ultima?.Tipo == TipoDeResposta.Plano => "Estudo · plano para aprovar",
        ModoDaConversa.Estudo when _conversa.RodadasDePerguntas > 0 =>
            $"Estudo · perguntas ({_conversa.RodadasDePerguntas} de {ConducaoDaConversa.MaximoDeRodadas})",
        ModoDaConversa.Estudo => "Estudo · lendo e entendendo",
        ModoDaConversa.Direto => "Modo: montar direto",
        _ => "",
    };

    /// <summary>O atalho "Pular para o plano" aparece só quando vale (no estudo, antes do plano).</summary>
    public bool MostrarPularParaOPlano => PodePularParaOPlano();

    public bool TemEtapaDoEstudo => EtapaDoEstudo.Length > 0;

    private void AtualizarEstudo()
    {
        OnPropertyChanged(nameof(EmEstudo));
        OnPropertyChanged(nameof(EtapaDoEstudo));
        OnPropertyChanged(nameof(TemEtapaDoEstudo));
        AtualizarInteracoes();
    }

    private MensagemNaTela? _observada;

    // Os botões do cartão, das perguntas e do plano só valem na última mensagem, e não durante a espera.
    private void AtualizarInteracoes()
    {
        if (_observada is not null)
            foreach (var pergunta in _observada.Perguntas)
                pergunta.PropertyChanged -= PerguntaRespondida;
        _observada = Ultima;
        if (_observada is not null)
            foreach (var pergunta in _observada.Perguntas)
                pergunta.PropertyChanged += PerguntaRespondida;
        OnPropertyChanged(nameof(Ultima));
        OnPropertyChanged(nameof(MostrarPularParaOPlano));
        EscolherModoCommand.NotifyCanExecuteChanged();
        ResponderPerguntasCommand.NotifyCanExecuteChanged();
        AprovarPlanoCommand.NotifyCanExecuteChanged();
        AjustarPlanoCommand.NotifyCanExecuteChanged();
        PularParaOPlanoCommand.NotifyCanExecuteChanged();
    }

    private void PerguntaRespondida(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        ResponderPerguntasCommand.NotifyCanExecuteChanged();
}
