using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Models;
using ProjectPlanner.App.Services;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Uma coluna da grid no menu "Colunas": se aparece, com que largura e em que posição.</summary>
public sealed partial class ColunaViewModel(string id, string titulo, bool podeOcultar) : ViewModelBase
{
    /// <summary>Identifica a coluna (é o Tag dela na MainWindow.axaml e o id no colunas.json).</summary>
    public string Id { get; } = id;

    /// <summary>Nome da coluna no menu.</summary>
    public string Titulo { get; } = titulo;

    /// <summary>Falso para a coluna Nome, que sempre aparece (sem ela a grid não diria de que tarefa é cada linha).</summary>
    public bool PodeOcultar { get; } = podeOcultar;

    [ObservableProperty]
    public partial bool Visivel { get; set; } = true;

    /// <summary>Largura em pixels.</summary>
    [ObservableProperty]
    public partial double Largura { get; set; }

    /// <summary>Posição na grid (0 = primeira), entre todas as colunas, visíveis ou não.</summary>
    [ObservableProperty]
    public partial int Posicao { get; set; }

    partial void OnVisivelChanged(bool value)
    {
        if (!value && !PodeOcultar)
            Visivel = true;
    }
}

/// <summary>
/// As colunas da grid: quais aparecem, em que ordem e com que largura. O layout é do usuário, não do
/// projeto: é lido das preferências ao abrir o programa e gravado a cada mudança (ver
/// <see cref="IPreferenciasColunas"/>). A tela aplica este estado nas colunas do DataGrid e devolve
/// para cá o que o usuário muda arrastando (<see cref="RegistrarLayoutDaGrade"/>).
/// </summary>
public sealed partial class ColunasViewModel : ViewModelBase
{
    /// <summary>Largura mínima aceita (a mesma do DataGrid): abaixo disso a coluna sumiria da vista.</summary>
    public const double LarguraMinima = 20;

    /// <summary>
    /// O layout padrão, na ordem padrão: as colunas do Bloco 5, com as larguras de lá, e as do Bloco 10
    /// (EDT ao lado do Nº, Sucessoras ao lado das Predecessoras e Notas no fim), todas visíveis.
    /// </summary>
    public static readonly IReadOnlyList<(string Id, string Titulo, double Largura)> Padrao =
    [
        ("Numero", "Nº", 65),
        ("Edt", "EDT", 75),
        ("Nome", "Nome", 130),
        ("Duracao", "Duração (dias)", 104),
        ("Inicio", "Início", 104),
        ("Termino", "Término", 104),
        ("PercentualConcluido", "% Concluído", 80),
        ("Predecessoras", "Predecessoras", 130),
        ("Sucessoras", "Sucessoras", 130),
        ("InicioMaisCedo", "Início cedo", 104),
        ("TerminoMaisCedo", "Término cedo", 104),
        ("InicioMaisTarde", "Início tarde", 104),
        ("TerminoMaisTarde", "Término tarde", 104),
        ("Folga", "Folga (dias)", 85),
        ("Critica", "Crítica", 90),
        ("Notas", "Notas", 200),
    ];

    private readonly IPreferenciasColunas _preferencias;
    private bool _aplicando;

    public ColunasViewModel(IPreferenciasColunas preferencias)
    {
        _preferencias = preferencias;
        Colunas = new ReadOnlyCollection<ColunaViewModel>(
            Padrao.Select(p => new ColunaViewModel(p.Id, p.Titulo, podeOcultar: p.Id != "Nome")).ToList());
        foreach (var coluna in Colunas)
            coluna.PropertyChanged += ColunaMudou;
        Aplicar(preferencias.Carregar());
    }

    /// <summary>Todas as colunas, na ordem do menu (a padrão), cada uma com sua posição atual na grid.</summary>
    public IReadOnlyList<ColunaViewModel> Colunas { get; }

    /// <summary>O layout mudou por inteiro (restaurado ao padrão): a tela reaplica tudo nas colunas.</summary>
    public event EventHandler? LayoutRestaurado;

    public ColunaViewModel this[string id] => Colunas.First(c => c.Id == id);

    /// <summary>O que o usuário deixou na grid ao arrastar cabeçalhos ou bordas: posição e largura de cada coluna.</summary>
    public void RegistrarLayoutDaGrade(IEnumerable<(string Id, int Posicao, double Largura)> colunas)
    {
        var mudou = false;
        _aplicando = true;
        try
        {
            foreach (var (id, posicao, largura) in colunas)
            {
                if (Colunas.FirstOrDefault(c => c.Id == id) is not { } coluna)
                    continue;
                if (coluna.Posicao != posicao || (LarguraValida(largura) && coluna.Largura != largura))
                    mudou = true;
                coluna.Posicao = posicao;
                if (LarguraValida(largura))
                    coluna.Largura = largura;
            }
            Normalizar();
        }
        finally
        {
            _aplicando = false;
        }
        if (mudou)
            Salvar();
    }

    /// <summary>Volta tudo ao layout padrão: todas visíveis, ordem e larguras originais.</summary>
    [RelayCommand]
    private void RestaurarPadrao()
    {
        Aplicar(null);
        Salvar();
        LayoutRestaurado?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Aplica um layout salvo por cima do padrão. Colunas que o arquivo não conhece ficam no padrão, na
    /// posição padrão; ids desconhecidos e larguras inválidas são ignorados.
    /// </summary>
    private void Aplicar(LayoutColunas? salvo)
    {
        _aplicando = true;
        try
        {
            var estados = (salvo?.Colunas ?? [])
                .GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
            for (var i = 0; i < Colunas.Count; i++)
            {
                var coluna = Colunas[i];
                var padrao = Padrao[i];
                if (estados.TryGetValue(coluna.Id, out var estado))
                {
                    coluna.Visivel = estado.Visivel || !coluna.PodeOcultar;
                    coluna.Largura = LarguraValida(estado.Largura) ? estado.Largura : padrao.Largura;
                    coluna.Posicao = estado.Posicao;
                }
                else
                {
                    coluna.Visivel = true;
                    coluna.Largura = padrao.Largura;
                    // Sem posição salva: fica logo depois da coluna que a antecede no padrão.
                    coluna.Posicao = i == 0 ? -1 : Colunas[i - 1].Posicao;
                }
            }
            Normalizar();
        }
        finally
        {
            _aplicando = false;
        }
    }

    // Deixa as posições em 0..n−1, sem repetição; empates ficam na ordem padrão.
    private void Normalizar()
    {
        var ordem = Colunas.Select((c, i) => (Coluna: c, Padrao: i))
            .OrderBy(x => x.Coluna.Posicao).ThenBy(x => x.Padrao)
            .Select(x => x.Coluna).ToList();
        for (var i = 0; i < ordem.Count; i++)
            ordem[i].Posicao = i;
    }

    private void ColunaMudou(object? sender, PropertyChangedEventArgs e)
    {
        if (!_aplicando && e.PropertyName == nameof(ColunaViewModel.Visivel))
            Salvar();
    }

    private void Salvar() => _preferencias.Salvar(new LayoutColunas(
        Colunas.Select(c => new EstadoColuna(c.Id, c.Visivel, c.Largura, c.Posicao)).ToList()));

    private static bool LarguraValida(double largura) => double.IsFinite(largura) && largura >= LarguraMinima;
}
