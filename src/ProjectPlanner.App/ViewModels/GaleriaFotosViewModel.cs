using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.Core;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Uma foto na galeria: o rótulo de data (sobre a miniatura) e a descrição.</summary>
public sealed record FotoNaGaleria(FotoTarefa Foto, string Data, string Descricao);

/// <summary>
/// A galeria de fotos de uma tarefa (Bloco 14): os registros em ordem cronológica, com data e descrição, e,
/// desde o Bloco 17, o botão "Adicionar foto", sempre visível (também com a galeria vazia). A miniatura ainda
/// é um marcador e a foto adicionada é um registro de exemplo: não há envio de imagens de verdade (ver
/// <see cref="FotoTarefa"/>).
/// </summary>
public sealed partial class GaleriaFotosViewModel : ViewModelBase
{
    /// <summary>Aviso discreto na galeria enquanto o envio de imagens não existe.</summary>
    public const string AvisoProvisorio =
        "O envio de imagens reais ainda será implementado. Por enquanto, Adicionar foto cria um registro de exemplo.";

    public const string SemFotos = "Nenhuma foto adicionada ainda.";

    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("pt-BR");
    private readonly Tarefa _tarefa;
    private readonly Action<FotoTarefa> _aoAdicionar;
    private readonly Func<DateTime> _hoje;

    /// <param name="tarefa">A tarefa das fotos.</param>
    /// <param name="aoAdicionar">Grava a foto nova na tarefa (a janela principal marca o projeto como alterado).</param>
    /// <param name="hoje">A data de hoje, para a foto de exemplo.</param>
    public GaleriaFotosViewModel(Tarefa tarefa, Action<FotoTarefa> aoAdicionar, Func<DateTime> hoje)
    {
        ArgumentNullException.ThrowIfNull(tarefa);
        ArgumentNullException.ThrowIfNull(aoAdicionar);
        ArgumentNullException.ThrowIfNull(hoje);
        (_tarefa, _aoAdicionar, _hoje) = (tarefa, aoAdicionar, hoje);
        Titulo = $"Fotos — {tarefa.Nome}";
        Recarregar();
    }

    public string Titulo { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Vazia))]
    public partial string Resumo { get; private set; } = "";

    /// <summary>Da mais antiga para a mais recente.</summary>
    public ObservableCollection<FotoNaGaleria> Fotos { get; } = [];

    /// <summary>Sem fotos: a galeria mostra <see cref="SemFotos"/> no lugar das miniaturas.</summary>
    public bool Vazia => Fotos.Count == 0;

    /// <summary>
    /// PROVISÓRIO até existir o envio de imagens: cria na tarefa uma foto de exemplo com a data de hoje.
    /// </summary>
    [RelayCommand]
    private void AdicionarFoto()
    {
        var foto = new FotoTarefa(_tarefa.Id, _hoje().Date, $"exemplo://{Guid.NewGuid():N}", "Foto de exemplo");
        _aoAdicionar(foto);
        Recarregar();
    }

    private void Recarregar()
    {
        Fotos.Clear();
        foreach (var f in _tarefa.Fotos)
            Fotos.Add(new FotoNaGaleria(f, f.DataRegistro.ToString("dd/MM/yyyy", Cultura), f.Descricao ?? "(sem descrição)"));
        Resumo = Fotos.Count switch
        {
            0 => SemFotos,
            1 => "1 foto",
            var n => $"{n} fotos, da mais antiga para a mais recente",
        };
    }
}
