using ProjectPlanner.App.Services;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Um componente de terceiros que vai junto com o programa, com a licença dele.</summary>
public sealed record ComponenteDeTerceiros(string Nome, string Licenca);

/// <summary>Ajuda > Sobre o ProjectPlanner (Bloco 25): a versão, as novidades dela e os componentes de terceiros.</summary>
public sealed class SobreViewModel : ViewModelBase
{
    public SobreViewModel() : this(VersaoDoProduto.Atual, NovidadesDaVersao.Changelog())
    {
    }

    public SobreViewModel(string versao, string changelog)
    {
        ArgumentNullException.ThrowIfNull(versao);
        ArgumentNullException.ThrowIfNull(changelog);
        Versao = versao;
        Novidades = NovidadesDaVersao.Ler(changelog, versao);
    }

    public string Versao { get; }

    public string Titulo => $"ProjectPlanner {Versao}";

    public string Descricao => "Planejamento de obras: cronograma com tarefas, resumos, marcos, predecessoras e caminho crítico, " +
                               "Gantt, fotos das tarefas e assistente de IA.";

    public IReadOnlyList<string> Novidades { get; }

    public bool TemNovidades => Novidades.Count > 0;

    /// <summary>O que vai dentro do programa publicado, além do .NET (licença MIT).</summary>
    public IReadOnlyList<ComponenteDeTerceiros> Componentes { get; } =
    [
        new(".NET 10 e Entity Framework Core", "MIT"),
        new("Avalonia UI (interface, DataGrid, tema Fluent, fonte Inter)", "MIT; fonte Inter: SIL Open Font License 1.1"),
        new("CommunityToolkit.Mvvm", "MIT"),
        new("SQLite (pelo Microsoft.Data.Sqlite e SQLitePCLRaw)", "domínio público; MIT/Apache-2.0"),
        new("SkiaSharp e HarfBuzzSharp", "MIT"),
        new("MetadataExtractor e XmpCore (EXIF das fotos)", "Apache-2.0"),
    ];
}
