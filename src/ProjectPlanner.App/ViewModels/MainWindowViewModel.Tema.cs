using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;

namespace ProjectPlanner.App.ViewModels;

// Bloco 25 (V3): Exibir > Tema. A escolha fica em interface.json, junto com o painel do assistente; a janela aplica o
// tema ao programa inteiro (ver MainWindow.AplicarTema).
public sealed partial class MainWindowViewModel
{
    private readonly IPreferenciasInterface _preferenciasInterface;

    /// <summary>O tema em uso: o do sistema (padrão), claro ou escuro.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EhTemaDoSistema), nameof(EhTemaClaro), nameof(EhTemaEscuro))]
    public partial TemaDaInterface Tema { get; private set; }

    public bool EhTemaDoSistema => Tema == TemaDaInterface.Sistema;
    public bool EhTemaClaro => Tema == TemaDaInterface.Claro;
    public bool EhTemaEscuro => Tema == TemaDaInterface.Escuro;

    /// <summary>Troca o tema e guarda a escolha para as próximas sessões.</summary>
    [RelayCommand]
    private void DefinirTema(TemaDaInterface tema)
    {
        if (!Enum.IsDefined(tema))
            throw new ArgumentOutOfRangeException(nameof(tema), tema, "Tema desconhecido.");
        Tema = tema;
        _preferenciasInterface.Salvar(new PreferenciasInterface(Assistente.Visivel, tema));
    }
}
