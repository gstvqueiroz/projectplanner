using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.Core;

namespace ProjectPlanner.App.ViewModels;

// Bloco 70 (saiu de AssistenteViewModel.cs): a ficha do projeto no painel.
public sealed partial class AssistenteViewModel
{
    // ---- Bloco 26: a ficha do projeto ----

    /// <summary>O que já foi definido sobre o projeto, com os valores editáveis.</summary>
    public ObservableCollection<CampoDaFichaNaTela> Ficha { get; } = [];

    public bool TemFicha => Ficha.Count > 0;

    public string TituloDaFicha => Ficha.Count == 0 ? "Ficha do projeto" : $"Ficha do projeto ({Ficha.Count})";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AdicionarCampoCommand))]
    public partial string NovoCampo { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AdicionarCampoCommand))]
    public partial string NovoValor { get; set; } = "";

    /// <summary>Um campo ou valor que a ficha recusou (vazio, longo demais, ficha cheia).</summary>
    [ObservableProperty]
    public partial string AvisoDaFicha { get; private set; } = "";

    private bool PodeAdicionarCampo() => !string.IsNullOrWhiteSpace(NovoCampo) && !string.IsNullOrWhiteSpace(NovoValor);

    [RelayCommand(CanExecute = nameof(PodeAdicionarCampo))]
    private void AdicionarCampo()
    {
        if (!DefinirNaFicha(NovoCampo, NovoValor))
            return;
        (NovoCampo, NovoValor) = ("", "");
        AtualizarFicha();
    }

    [RelayCommand]
    private void RemoverCampo(CampoDaFichaNaTela? campo)
    {
        if (campo is not null && DefinirNaFicha(campo.Campo, null))
            AtualizarFicha();
    }

    private void ValorDaFichaMudou(CampoDaFichaNaTela campo)
    {
        if (DefinirNaFicha(campo.Campo, campo.Valor) && string.IsNullOrWhiteSpace(campo.Valor))
            AtualizarFicha(); // valor apagado: o campo sai da ficha
    }

    private bool DefinirNaFicha(string campo, string? valor)
    {
        try
        {
            AvisoDaFicha = "";
            if (_conversa.Ficha.Definir(campo, valor))
                _cronograma.ConversaMudou();
            return true;
        }
        catch (DomainValidationException ex)
        {
            AvisoDaFicha = ex.Message;
            return false;
        }
    }

    private void AtualizarFicha()
    {
        Ficha.Clear();
        foreach (var item in _conversa.Ficha.Itens)
            Ficha.Add(new CampoDaFichaNaTela(item, ValorDaFichaMudou));
    }
}
