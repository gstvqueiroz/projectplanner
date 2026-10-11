using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Fotos;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Uma foto nas listas do antes e depois: "15/09/2026 08:00 · 1 Fundação".</summary>
public sealed record FotoNaComparacao(FotoTarefa Foto, string Texto)
{
    public override string ToString() => Texto;
}

/// <summary>Uma foto da série (uma por dia), com a miniatura e a data.</summary>
public sealed record FotoDaSerie(FotoTarefa Foto, byte[]? Miniatura, string Data);

// Bloco 29 (RF3): a aba Antes e depois: o mesmo local (ou a mesma tarefa) em dois dias, lado a lado, e a série do
// primeiro ao último dia.
public sealed partial class FotosDoProjetoViewModel
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");
    private bool _montandoComparacao;

    /// <summary>Os locais e as tarefas com fotos em pelo menos dois dias.</summary>
    [ObservableProperty] public partial IReadOnlyList<AssuntoDaComparacao> AssuntosDaComparacao { get; private set; } = [];

    [ObservableProperty] public partial AssuntoDaComparacao? AssuntoEscolhido { get; set; }

    /// <summary>As fotos do assunto, da mais antiga para a mais recente (as opções do antes e do depois).</summary>
    [ObservableProperty] public partial IReadOnlyList<FotoNaComparacao> FotosDoAssunto { get; private set; } = [];

    [ObservableProperty] public partial FotoNaComparacao? FotoAntes { get; set; }

    [ObservableProperty] public partial FotoNaComparacao? FotoDepois { get; set; }

    [ObservableProperty] public partial byte[]? ImagemAntes { get; private set; }

    [ObservableProperty] public partial byte[]? ImagemDepois { get; private set; }

    /// <summary>"De 20% para 80% concluído em 26 dias".</summary>
    [ObservableProperty] public partial string Evolucao { get; private set; } = "";

    [ObservableProperty] public partial IReadOnlyList<FotoDaSerie> Serie { get; private set; } = [];

    public bool SemComparacao => AssuntosDaComparacao.Count == 0;

    public const string SemAssuntos =
        "Ainda não há o que comparar: é preciso ter fotos do mesmo local (ou da mesma tarefa) em pelo menos dois dias diferentes.";

    partial void OnAssuntoEscolhidoChanged(AssuntoDaComparacao? value) => MontarFotosDoAssunto();

    partial void OnFotoAntesChanged(FotoNaComparacao? value) => MontarComparacao();

    partial void OnFotoDepoisChanged(FotoNaComparacao? value) => MontarComparacao();

    /// <summary>Refaz a lista de assuntos (depois de importar ou editar fotos), mantendo o escolhido se ainda existe.</summary>
    private void AtualizarAntesEDepois()
    {
        var escolhido = AssuntoEscolhido;
        AssuntosDaComparacao = AntesEDepois.Assuntos(_cronograma);
        OnPropertyChanged(nameof(SemComparacao));
        AssuntoEscolhido = AssuntosDaComparacao.FirstOrDefault(a => a == escolhido) ?? AssuntosDaComparacao.FirstOrDefault();
        if (AssuntoEscolhido == escolhido)
            MontarFotosDoAssunto();
    }

    // A primeira e a última foto do assunto, como ponto de partida.
    private void MontarFotosDoAssunto()
    {
        _montandoComparacao = true;
        try
        {
            FotosDoAssunto = AssuntoEscolhido is { } assunto
                ? AntesEDepois.FotosDe(_cronograma, assunto).Select(f => new FotoNaComparacao(f,
                    $"{f.DataFoto.ToString("dd/MM/yyyy HH:mm", Brasil)} · {_nomes.GetValueOrDefault(f.TarefaId)}")).ToList()
                : [];
            FotoAntes = FotosDoAssunto.FirstOrDefault();
            FotoDepois = FotosDoAssunto.LastOrDefault();
        }
        finally
        {
            _montandoComparacao = false;
        }
        MontarComparacao();
    }

    private void MontarComparacao()
    {
        if (_montandoComparacao)
            return;
        var comparacao = AssuntoEscolhido is { } assunto && FotoAntes is { } antes && FotoDepois is { } depois
            ? AntesEDepois.Comparar(assunto, FotosDoAssunto.Select(f => f.Foto).ToList(), antes.Foto, depois.Foto)
            : null;
        ImagemAntes = comparacao is null ? null : _fotos.Imagem(comparacao.Antes.Id);
        ImagemDepois = comparacao is null ? null : _fotos.Imagem(comparacao.Depois.Id);
        Evolucao = comparacao?.Evolucao ?? "";
        var miniaturas = comparacao is null ? new Dictionary<Guid, byte[]>() : _fotos.Miniaturas(comparacao.Serie.Select(f => f.Id));
        Serie = comparacao?.Serie.Select(f => new FotoDaSerie(f, miniaturas.GetValueOrDefault(f.Id), f.DataFoto.ToString("dd/MM", Brasil))).ToList() ?? [];
    }
}
