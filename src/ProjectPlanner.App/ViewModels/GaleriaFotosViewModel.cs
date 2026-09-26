using System.Globalization;
using ProjectPlanner.Core;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Uma foto na galeria: o rótulo de data (sobre a miniatura) e a descrição.</summary>
public sealed record FotoNaGaleria(FotoTarefa Foto, string Data, string Descricao);

/// <summary>
/// A galeria de fotos de uma tarefa (Bloco 14): os registros em ordem cronológica, com data e descrição.
/// Não edita nada. A miniatura ainda é um marcador (não há imagens de verdade, ver <see cref="FotoTarefa"/>).
/// </summary>
public sealed class GaleriaFotosViewModel
{
    public GaleriaFotosViewModel(Tarefa tarefa)
    {
        ArgumentNullException.ThrowIfNull(tarefa);
        var cultura = CultureInfo.GetCultureInfo("pt-BR");
        Titulo = $"Fotos — {tarefa.Nome}";
        Fotos = tarefa.Fotos
            .Select(f => new FotoNaGaleria(f, f.DataRegistro.ToString("dd/MM/yyyy", cultura), f.Descricao ?? "(sem descrição)"))
            .ToList();
        Resumo = Fotos.Count switch
        {
            0 => "Esta tarefa ainda não tem fotos.",
            1 => "1 foto",
            var n => $"{n} fotos, da mais antiga para a mais recente",
        };
    }

    public string Titulo { get; }

    public string Resumo { get; }

    /// <summary>Da mais antiga para a mais recente.</summary>
    public IReadOnlyList<FotoNaGaleria> Fotos { get; }
}
