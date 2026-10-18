using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Bloco 41: em que pé está um cartão de atualizações.</summary>
public enum EstadoDoCartao
{
    Pendente,
    Aplicado,
    Descartado,
    Recusado,
}

/// <summary>Um item do cartão na tela: o texto e, se não der para aplicar, o porquê.</summary>
public sealed record ItemDoCartaoNaTela(ItemDoCartao Item)
{
    public string Texto => Item.Texto;
    public string? Problema => Item.Problema is { } p ? $"Não dá: {p}." : null;
    public bool TemProblema => Item.Problema is not null || Item.Operacao is null;
}

/// <summary>
/// Bloco 41: as atualizações da obra que o assistente propôs numa resposta (%, medição, custos, início, linha de base),
/// para o usuário aplicar ou descartar. Aplicadas, viram um passo do Desfazer (com o Desfazer na própria conversa).
/// </summary>
public sealed partial class CartaoDeAtualizacoes : ObservableObject
{
    public CartaoDeAtualizacoes(IReadOnlyList<ItemDoCartao> itens) =>
        Itens = [.. itens.Select(i => new ItemDoCartaoNaTela(i))];

    public IReadOnlyList<ItemDoCartaoNaTela> Itens { get; }

    public IReadOnlyList<OperacaoDaObra> Operacoes => [.. Itens.Where(i => i.Item.Valido).Select(i => i.Item.Operacao!)];

    public int Validos => Itens.Count(i => i.Item.Valido);

    public bool TemValidos => Validos > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Pendente), nameof(TextoDoEstado), nameof(TemAplicacao))]
    public partial EstadoDoCartao Estado { get; set; }

    /// <summary>Depois de aplicado: o resultado, com o Desfazer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemAplicacao))]
    public partial AlteracoesNaConversa? Aplicacao { get; set; }

    public bool TemAplicacao => Aplicacao is not null;

    public bool Pendente => Estado == EstadoDoCartao.Pendente;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDoEstado))]
    public partial string? Motivo { get; set; }

    public string Titulo => Itens.Count == 1 ? "Atualização proposta" : $"{Itens.Count} atualizações propostas";

    public string TextoDoBotao => Validos == Itens.Count ? "Aplicar" : Validos == 1 ? "Aplicar a válida" : $"Aplicar as {Validos} válidas";

    public string TextoDoEstado => Estado switch
    {
        EstadoDoCartao.Descartado => "Descartadas: nada mudou.",
        EstadoDoCartao.Recusado => $"Nada mudou. {Motivo}",
        _ => "",
    };

    /// <summary>Como fica guardado na conversa (ao reabrir o projeto, o cartão aparece como texto).</summary>
    public string ComoTexto => Estado switch
    {
        EstadoDoCartao.Aplicado => "Atualizações aplicadas: ",
        EstadoDoCartao.Descartado => "Atualizações descartadas: ",
        EstadoDoCartao.Recusado => "Atualizações não aplicadas: ",
        _ => "Atualizações propostas (não aplicadas): ",
    } + string.Join("; ", Itens.Select(i => i.Texto)) + ".";
}

// Bloco 41: o cartão das atualizações e as telas abertas pelo assistente.
public sealed partial class AssistenteViewModel
{
    /// <summary>Aplica as atualizações válidas do cartão (todas juntas, num passo do Desfazer).</summary>
    [RelayCommand]
    private async Task AplicarCartao(CartaoDeAtualizacoes? cartao)
    {
        if (cartao is not { Pendente: true } || cartao.Validos == 0)
            return;
        var resumo = cartao.Validos == 1 ? cartao.Itens.First(i => i.Item.Valido).Texto : $"{cartao.Validos} atualizações da obra";
        var resultado = await _cronograma.AplicarAtualizacoesAsync(cartao.Operacoes, resumo);
        if (resultado.Situacao == SituacaoDaAplicacao.Aplicada)
        {
            cartao.Aplicacao = new AlteracoesNaConversa(resultado)
            {
                Prefixo = "Obra atualizada",
                PodeDesfazer = resultado.Marca is { } m && _cronograma.PodeDesfazer(m),
            };
            cartao.Estado = EstadoDoCartao.Aplicado;
        }
        else
            (cartao.Motivo, cartao.Estado) = (resultado.Texto, EstadoDoCartao.Recusado);
        GuardarCartao(cartao);
    }

    [RelayCommand]
    private void DescartarCartao(CartaoDeAtualizacoes? cartao)
    {
        if (cartao is not { Pendente: true })
            return;
        cartao.Estado = EstadoDoCartao.Descartado;
        GuardarCartao(cartao);
    }

    /// <summary>Abre de novo a tela que o assistente abriu (o botão na mensagem).</summary>
    [RelayCommand]
    private Task AbrirTela(TelaParaAbrir? tela) => tela is null ? Task.CompletedTask : _cronograma.AbrirTelaAsync(tela);

    // O estado do cartão vai para a conversa guardada (junto do resultado das alterações do cronograma, se houver).
    private void GuardarCartao(CartaoDeAtualizacoes cartao)
    {
        var mensagem = Mensagens.FirstOrDefault(m => ReferenceEquals(m.Cartao, cartao));
        if (mensagem?.IndiceNaConversa is { } i && i < _conversa.Mensagens.Count)
            _conversa.Substituir(i, _conversa.Mensagens[i] with { ResultadoDasAlteracoes = TextoGuardado(mensagem.Alteracoes, cartao) });
        _cronograma.ConversaMudou();
    }

    private static string? TextoGuardado(AlteracoesNaConversa? alteracoes, CartaoDeAtualizacoes? cartao) =>
        string.Join("\n", new[] { alteracoes?.Texto, cartao?.ComoTexto }.OfType<string>()) is { Length: > 0 } texto ? texto : null;
}
