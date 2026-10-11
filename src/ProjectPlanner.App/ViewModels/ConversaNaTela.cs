using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using ProjectPlanner.Core.Assistente;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.ViewModels;

// Bloco 26: as partes novas da conversa no painel: perguntas com respostas para clicar, o plano e a ficha do projeto.

/// <summary>Uma pergunta do assistente: as respostas sugeridas (mais "Não sei") e um campo para escrever outra.</summary>
public sealed partial class PerguntaNaTela(PerguntaIA pergunta) : ObservableObject
{
    public const string NaoSei = "Não sei (use o comum)";
    public const string SemResposta = "(sem resposta)";

    public string Texto => pergunta.Texto;

    public IReadOnlyList<string> Opcoes { get; } = [.. pergunta.Opcoes, NaoSei];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Resposta), nameof(Respondida))]
    public partial string? Escolhida { get; set; }

    /// <summary>A resposta escrita; quando há, vale no lugar da escolhida.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Resposta), nameof(Respondida))]
    public partial string Outra { get; set; } = "";

    public string? Resposta => !string.IsNullOrWhiteSpace(Outra) ? Outra.Trim() : Escolhida;

    public bool Respondida => Resposta is not null;
}

public sealed record EtapaNaTela(string Nome, string Duracao, string? Observacao)
{
    public bool TemObservacao => !string.IsNullOrWhiteSpace(Observacao);
}

/// <summary>O plano para aprovar, como o painel o mostra.</summary>
public sealed class PlanoNaTela(PlanoIA plano)
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    public IReadOnlyList<EtapaNaTela> Etapas { get; } =
        [.. plano.Etapas.Select(e => new EtapaNaTela(e.Nome, e.Duracao == 1 ? "1 dia" : $"{e.Duracao} dias", e.Observacao))];

    /// <summary>"Término estimado: 26/02/2027" (a data como veio, se não for AAAA-MM-DD); vazio sem data.</summary>
    public string Termino { get; } = plano.Termino is not { } termino ? ""
        : "Término estimado: " + (DateOnly.TryParseExact(termino, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data)
            ? data.ToString("dd/MM/yyyy", Brasil)
            : termino);

    public bool TemTermino => Termino.Length > 0;

    public IReadOnlyList<string> Premissas { get; } = plano.Premissas;

    public bool TemPremissas => Premissas.Count > 0;
}

/// <summary>Um campo da ficha do projeto no painel, com o valor editável.</summary>
public sealed partial class CampoDaFichaNaTela : ObservableObject
{
    private readonly Action<CampoDaFichaNaTela> _mudou;
    private readonly bool _pronto;

    public CampoDaFichaNaTela(ItemDaFicha item, Action<CampoDaFichaNaTela> mudou)
    {
        (Campo, _mudou) = (item.Campo, mudou);
        Valor = item.Valor;
        _pronto = true;
    }

    public string Campo { get; }

    [ObservableProperty]
    public partial string Valor { get; set; }

    partial void OnValorChanged(string value)
    {
        if (_pronto)
            _mudou(this);
    }
}
