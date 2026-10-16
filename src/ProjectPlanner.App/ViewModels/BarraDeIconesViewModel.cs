using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Bloco 39: os grupos da barra de ícones, na ordem em que aparecem.</summary>
public enum GrupoDaBarra
{
    Arquivo,
    Edicao,
    Tarefa,
    Planejamento,
    Obra,
    Exibicao,
    Assistente,
}

/// <summary>Bloco 39: um botão da barra de ícones: o id (o que vai nas preferências), o nome e o grupo.</summary>
public sealed partial class BotaoDaBarra(string id, string nome, GrupoDaBarra grupo, Action mudou) : ObservableObject
{
    public string Id { get; } = id;

    /// <summary>O nome do botão: a dica, o leitor de tela, o menu "»" e Exibir > Barra de ícones.</summary>
    public string Nome { get; } = nome;

    public GrupoDaBarra Grupo { get; } = grupo;

    [ObservableProperty] public partial bool Visivel { get; set; } = true;

    partial void OnVisivelChanged(bool value) => mudou();
}

/// <summary>
/// Bloco 39: quais botões a barra de ícones mostra (Exibir > Barra de ícones), guardado em interface.json. A ordem e os
/// grupos são os do catálogo abaixo; a janela mostra os escolhidos e manda para o "»" os que não cabem na largura.
/// </summary>
public sealed partial class BarraDeIconesViewModel
{
    private readonly IPreferenciasInterface _preferencias;
    private bool _carregando;

    public BarraDeIconesViewModel(IPreferenciasInterface preferencias)
    {
        ArgumentNullException.ThrowIfNull(preferencias);
        _preferencias = preferencias;
        BotaoDaBarra B(string id, string nome, GrupoDaBarra grupo) => new(id, nome, grupo, BotaoMudou);
        Botoes =
        [
            B("novo", "Novo projeto", GrupoDaBarra.Arquivo),
            B("abrir", "Abrir projeto", GrupoDaBarra.Arquivo),
            B("salvar", "Salvar", GrupoDaBarra.Arquivo),
            B("desfazer", "Desfazer", GrupoDaBarra.Edicao),
            B("refazer", "Refazer", GrupoDaBarra.Edicao),
            B("adicionar", "Adicionar tarefa", GrupoDaBarra.Tarefa),
            B("remover", "Remover tarefa", GrupoDaBarra.Tarefa),
            B("promover", "Promover", GrupoDaBarra.Tarefa),
            B("rebaixar", "Rebaixar", GrupoDaBarra.Tarefa),
            B("recalcular", "Recalcular cronograma", GrupoDaBarra.Tarefa),
            B("fotos", "Fotos da tarefa", GrupoDaBarra.Tarefa),
            B("recursosDaTarefa", "Recursos da tarefa", GrupoDaBarra.Tarefa),
            B("irParaTarefa", "Ir para a tarefa", GrupoDaBarra.Planejamento),
            B("linhaDeBase", "Salvar linha de base", GrupoDaBarra.Planejamento),
            B("relatorio", "Relatório fotográfico", GrupoDaBarra.Obra),
            B("fotosDoProjeto", "Fotos do projeto", GrupoDaBarra.Obra),
            B("medicoes", "Medições", GrupoDaBarra.Obra),
            B("orcamento", "Orçamento", GrupoDaBarra.Obra),
            B("indicadores", "Indicadores", GrupoDaBarra.Obra),
            B("dividido", "Dividido", GrupoDaBarra.Exibicao),
            B("somenteTabela", "Somente Tabela", GrupoDaBarra.Exibicao),
            B("somenteGantt", "Somente Gantt", GrupoDaBarra.Exibicao),
            B("caminhoCritico", "Caminho crítico", GrupoDaBarra.Exibicao),
            B("linhaDeBaseNoGantt", "Linha de base no Gantt", GrupoDaBarra.Exibicao),
            B("linhaHoje", "Linha de hoje", GrupoDaBarra.Exibicao),
            B("assistente", "Assistente", GrupoDaBarra.Assistente),
        ];
        Carregar();
    }

    public IReadOnlyList<BotaoDaBarra> Botoes { get; }

    /// <summary>Um botão foi mostrado ou escondido (a janela refaz a barra).</summary>
    public event Action? Mudou;

    public BotaoDaBarra Botao(string id) => Botoes.First(b => b.Id == id);

    /// <summary>O nome de cada grupo, para Exibir > Barra de ícones.</summary>
    public static string NomeDoGrupo(GrupoDaBarra grupo) => grupo switch
    {
        GrupoDaBarra.Arquivo => "Arquivo",
        GrupoDaBarra.Edicao => "Edição",
        GrupoDaBarra.Tarefa => "Tarefa",
        GrupoDaBarra.Planejamento => "Planejamento",
        GrupoDaBarra.Obra => "Obra",
        GrupoDaBarra.Exibicao => "Exibição",
        _ => "Assistente",
    };

    public bool TodosVisiveis => Botoes.All(b => b.Visivel);

    private void Carregar()
    {
        var ocultos = (_preferencias.Carregar()?.OcultosNaBarra ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _carregando = true;
        foreach (var b in Botoes)
            b.Visivel = !ocultos.Contains(b.Id);
        _carregando = false;
    }

    /// <summary>Volta a mostrar todos os botões.</summary>
    [RelayCommand]
    private void Restaurar()
    {
        _carregando = true;
        foreach (var b in Botoes)
            b.Visivel = true;
        _carregando = false;
        Guardar();
    }

    private void BotaoMudou()
    {
        if (!_carregando)
            Guardar();
    }

    private void Guardar()
    {
        var ocultos = Botoes.Where(b => !b.Visivel).Select(b => b.Id).ToList();
        _preferencias.Salvar((_preferencias.Carregar() ?? new PreferenciasInterface(false)) with { OcultosNaBarra = ocultos.Count == 0 ? null : ocultos });
        Mudou?.Invoke();
    }
}
