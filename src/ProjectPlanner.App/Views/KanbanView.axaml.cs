using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core.Kanban;

namespace ProjectPlanner.App.Views;

/// <summary>
/// Bloco 75 (o plano "Delete, arrastar e Kanban", K5): o quadro Kanban na tela. Clique num card seleciona a tarefa (Ctrl/⌘ e
/// Shift como na grade); fora dos cards, limpa a seleção. Arrastar um card (depois de 5 px; no toque, segurando 0,4 s) mostra
/// o card junto do cursor, a guia azul onde ele vai cair e a dica do que acontece com o %; perto das bordas o quadro e a coluna
/// rolam sozinhos; Esc ou soltar fora do quadro cancela. Apertar num dos selecionados leva todos; sem arrastar, fica só ele. As
/// regras são do <see cref="KanbanViewModel"/> (que pede ao programa), não daqui.
/// </summary>
public partial class KanbanView : UserControl
{
    /// <summary>A largura de cada coluna.</summary>
    public const double LarguraDaColuna = 272;

    /// <summary>Bloco 77: a largura do painel do card.</summary>
    public const double LarguraDoPainel = 360;

    private const double LimiarDoArraste = 5;
    private const double BordaDaRolagem = 40;
    private static readonly TimeSpan SegurarNoToque = TimeSpan.FromMilliseconds(400);
    private static readonly IBrush CorDoBloqueio = new SolidColorBrush(Color.Parse("#C62828"));
    private static readonly Cursor CursorArrastando = new(StandardCursorType.DragMove);
    private static readonly Cursor CursorProibido = new(StandardCursorType.No);

    private CardNoKanban? _apertado;
    private Point _pontoDoClique;
    private Point _ultimoPonto;
    private IPointer? _ponteiro;
    private bool _selecionarAoSoltar;
    private bool _abrirAoSoltar;
    private IReadOnlyList<Guid>? _arrastadas;
    private readonly DispatcherTimer _rolagemDoArraste = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private readonly DispatcherTimer _seguraDoToque = new() { Interval = SegurarNoToque };

    public KanbanView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, Apertado, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, Movido, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, Solto, RoutingStrategies.Tunnel);
        AddHandler(PointerCaptureLostEvent, (_, _) => Encerrar(), RoutingStrategies.Bubble);
        AddHandler(KeyDownEvent, TeclaApertada, RoutingStrategies.Tunnel);
        _rolagemDoArraste.Tick += (_, _) => RolarDuranteOArraste();
        // Bloco 87: os cabeçalhos fixos acompanham a rolagem horizontal das raias (e o contrário, rolando sobre eles).
        RolagemDasRaias.ScrollChanged += (_, _) => AcompanharRolagemDasRaias();
        CabecalhoDasRaias.ScrollChanged += (_, _) =>
        {
            if (Math.Abs(CabecalhoDasRaias.Offset.X - RolagemDasRaias.Offset.X) > 0.5)
                RolagemDasRaias.Offset = RolagemDasRaias.Offset.WithX(CabecalhoDasRaias.Offset.X);
        };
        NovoItemDoChecklist.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Vm?.Painel is { } painel)
            {
                painel.AdicionarItemCommand.Execute(null);
                e.Handled = true;
            }
        };
        _seguraDoToque.Tick += (_, _) =>
        {
            _seguraDoToque.Stop();
            if (_apertado is not null && _ponteiro is { } ponteiro)
            {
                ComecarArraste(ponteiro);
                AtualizarArraste();
            }
        };
    }

    private KanbanViewModel? Vm => DataContext as KanbanViewModel;

    /// <summary>
    /// Bloco 87: com as raias, os cabeçalhos das colunas ficam fora da rolagem vertical e seguem a horizontal; a raia cujo nome já
    /// passou do alto aparece na etiqueta do canto (o nome e quantos cards).
    /// </summary>
    internal void AcompanharRolagemDasRaias()
    {
        if (Math.Abs(CabecalhoDasRaias.Offset.X - RolagemDasRaias.Offset.X) > 0.5)
            CabecalhoDasRaias.Offset = CabecalhoDasRaias.Offset.WithX(RolagemDasRaias.Offset.X);
        RaiaNoAltoAgora = null;
        if (Vm is { ComRaias: true } vm)
            for (var i = 0; i < vm.RaiasNaTela.Count; i++)
            {
                if (ListaDeRaias.ContainerFromIndex(i) is not { } raia || raia.TranslatePoint(new Point(0, 0), RolagemDasRaias) is not { } topo)
                    continue;
                if (topo.Y > -4)
                    break; // esta e as de baixo ainda mostram o próprio nome
                if (topo.Y + raia.Bounds.Height > 24)
                    RaiaNoAltoAgora = vm.RaiasNaTela[i];
            }
        RaiaNoAlto.IsVisible = RaiaNoAltoAgora is not null;
        TextoDaRaiaNoAlto.Text = RaiaNoAltoAgora is { } r ? $"{r.Nome} · {r.Contagem}" : "";
    }

    /// <summary>Bloco 87: a raia que está no alto com o nome dela já fora da tela (nula se nenhuma).</summary>
    internal RaiaNoKanban? RaiaNoAltoAgora { get; private set; }

    /// <summary>Se há um arraste de cards em curso.</summary>
    public bool Arrastando => _arrastadas is not null;

    private void Apertado(object? sender, PointerPressedEventArgs e)
    {
        Encerrar();
        if (Vm is not { } vm)
            return;
        var origem = e.Source as Visual;
        // Só a área dos cards (as colunas ou as raias): a barra dos filtros e o painel do card cuidam de si (o foco fica neles).
        if (origem is null || !NaAreaDosCards(origem))
            return;
        if (origem.GetSelfAndVisualAncestors().Any(v => v is ScrollBar or Button))
            return; // nas barras de rolagem é só rolar; os botões (⋯, + Adicionar coluna, + Adicionar card) cuidam de si
        var propriedades = e.GetCurrentPoint(this).Properties;
        Focus(); // o Delete vale no quadro (E1)
        var card = CardSob(origem);
        if (propriedades.IsRightButtonPressed)
        {
            if (card is not null)
            {
                if (!card.Selecionado)
                    vm.ClicarNoCard(card.TarefaId, adicionar: false, intervalo: false);
                AbrirMenu(MenuDoCard(vm, card), (Control)origem); // Bloco 77
            }
            else if (origem.GetSelfAndVisualAncestors().OfType<DockPanel>().FirstOrDefault(d => d.Classes.Contains("cabecalho")) is
                     { DataContext: ColunaNoKanban coluna } cabecalho)
                AbrirMenu(coluna, cabecalho); // Bloco 76: o menu da coluna também no botão direito
            return;
        }
        if (!propriedades.IsLeftButtonPressed)
            return;
        if (card is null)
        {
            vm.ClicarNoVazio();
            return;
        }

        // A tecla de acrescentar à seleção é a da plataforma (⌘ no Mac, Ctrl no Windows), a mesma da grade e do Gantt.
        var comando = this.GetPlatformSettings()?.HotkeyConfiguration.CommandModifiers
                      ?? (Services.AtalhosDeTeclado.EhMac ? KeyModifiers.Meta : KeyModifiers.Control);
        var teclas = e.KeyModifiers;
        var semTeclas = (teclas & (comando | KeyModifiers.Shift)) == 0;
        // Apertar num card já selecionado (de vários) deixa a seleção para o arraste; sem arrastar, fica só ele.
        _selecionarAoSoltar = semTeclas && card.Selecionado && vm.Colunas.Sum(c => c.Cards.Count(k => k.Selecionado)) > 1;
        if (!_selecionarAoSoltar)
            vm.ClicarNoCard(card.TarefaId, adicionar: (teclas & comando) != 0, intervalo: teclas.HasFlag(KeyModifiers.Shift));
        e.Handled = true;
        if (!semTeclas)
            return;
        // Bloco 77 (K4): duplo clique abre o painel do card, ao soltar sem ter arrastado (o segundo aperto ainda pode arrastar).
        _abrirAoSoltar = e.ClickCount >= 2;
        _apertado = card;
        _pontoDoClique = _ultimoPonto = e.GetPosition(this);
        _ponteiro = e.Pointer;
        if (e.Pointer.Type is PointerType.Touch or PointerType.Pen)
        {
            e.Handled = false; // no toque, andar antes de segurar 0,4 s é rolar
            _seguraDoToque.Start();
            return;
        }
        e.Pointer.Capture(this);
    }

    private void Movido(object? sender, PointerEventArgs e)
    {
        if (_apertado is null)
            return;
        var ponto = e.GetPosition(this);
        _ultimoPonto = ponto;
        if (_arrastadas is null)
        {
            if (Distancia(ponto, _pontoDoClique) < LimiarDoArraste)
                return;
            if (_seguraDoToque.IsEnabled)
            {
                _seguraDoToque.Stop(); // andou antes de segurar: é rolagem
                _apertado = null;
                return;
            }
            ComecarArraste(e.Pointer);
        }
        AtualizarArraste();
        e.Handled = true;
    }

    private void Solto(object? sender, PointerReleasedEventArgs e)
    {
        _seguraDoToque.Stop();
        if (_apertado is not { } card)
            return;
        var vm = Vm;
        if (_arrastadas is { } arrastadas)
        {
            _ultimoPonto = e.GetPosition(this);
            var destino = DestinoEm(_ultimoPonto);
            Encerrar();
            if (destino?.Celula is { } celula)
                vm?.SoltarNaCelula(arrastadas, celula, destino.Lacuna); // Bloco 78: numa célula das raias
            else if (destino is not null)
                vm?.Soltar(arrastadas, destino.Coluna.Id, destino.Lacuna);
        }
        else
        {
            var (soEle, abrir) = (_selecionarAoSoltar, _abrirAoSoltar);
            Encerrar();
            if (soEle)
                vm?.ClicarNoCard(card.TarefaId, adicionar: false, intervalo: false);
            if (abrir)
                vm?.AbrirCard(card.TarefaId);
        }
        e.Handled = true;
    }

    /// <summary>Bloco 76: o último menu de coluna aberto (os testes o leem).</summary>
    internal ContextMenu? MenuAberto { get; private set; }

    private void AbrirMenuDaColuna(object? sender, RoutedEventArgs e)
    {
        if (sender is Control botao && botao.DataContext is ColunaNoKanban coluna)
            AbrirMenu(coluna, botao);
    }

    private void AbrirMenu(ColunaNoKanban coluna, Control alvo) => AbrirMenu(MenuDaColuna(coluna), alvo);

    private void AbrirMenu(ContextMenu menu, Control alvo)
    {
        MenuAberto?.Close();
        MenuAberto = menu;
        MenuAberto.Open(alvo);
    }

    /// <summary>Bloco 77: com o foco no quadro (num card), o Delete exclui; no painel do card e nos filtros, não (E1).</summary>
    internal bool NaAreaDosCards(Visual origem) =>
        ReferenceEquals(origem, this) || origem.GetSelfAndVisualAncestors().Any(v => ReferenceEquals(v, Rolagem) || ReferenceEquals(v, RolagemDasRaias)
                                                                               || ReferenceEquals(v, CabecalhoDasRaias));

    /// <summary>
    /// Bloco 77: o menu do card (botão direito): Abrir card, Mover para (as colunas), Arquivar e Excluir tarefa, para os
    /// selecionados se o card é um deles.
    /// </summary>
    internal static ContextMenu MenuDoCard(KanbanViewModel vm, CardNoKanban card)
    {
        var selecionados = vm.OrdemDosCards.Where(id => vm.CardDe(id)?.Selecionado == true).ToList();
        IReadOnlyList<Guid> ids = selecionados.Contains(card.TarefaId) ? selecionados : [card.TarefaId];
        var varios = ids.Count > 1;
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Abrir card", Command = new RelayCommand(() => vm.AbrirCard(card.TarefaId)) });
        var mover = new MenuItem { Header = varios ? $"Mover {ids.Count} cards para" : "Mover para" };
        foreach (var coluna in vm.Colunas)
            mover.Items.Add(new MenuItem
            {
                Header = coluna.Nome,
                Command = new RelayCommand(() => vm.Mover?.Invoke(ids, coluna.Id, int.MaxValue)),
                IsEnabled = ids.Any(id => vm.ColunaDe(id) != coluna),
            });
        menu.Items.Add(mover);
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem
        {
            Header = varios ? $"Arquivar {ids.Count} cards" : "Arquivar",
            Command = new RelayCommand(() => vm.AcoesDosCards?.Arquivar(ids)),
        });
        menu.Items.Add(new MenuItem
        {
            Header = varios ? $"Excluir {ids.Count} tarefas" : "Excluir tarefa",
            Command = new AsyncRelayCommand(() => vm.AcoesDosCards?.ExcluirTarefasAsync(ids) ?? Task.CompletedTask),
        });
        return menu;
    }

    private void AbrirMenuDasEtiquetas(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && sender is Control botao)
            AbrirMenu(MenuDasEtiquetas(vm), botao);
    }

    /// <summary>Bloco 77: as etiquetas do projeto: para cada uma, Renomear…, Cor e Excluir; e Nova etiqueta….</summary>
    internal static ContextMenu MenuDasEtiquetas(KanbanViewModel vm)
    {
        var menu = new ContextMenu();
        foreach (var etiqueta in vm.EtiquetasDoProjeto)
        {
            var item = new MenuItem
            {
                Header = etiqueta.Nome,
                Icon = new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(Color.Parse(etiqueta.Cor)) },
            };
            item.Items.Add(new MenuItem { Header = "Renomear…", Command = new AsyncRelayCommand(() => vm.AcoesDosCards?.RenomearEtiquetaAsync(etiqueta.Id) ?? Task.CompletedTask) });
            var cores = new MenuItem { Header = "Cor" };
            foreach (var (nome, cor) in RegrasDasColunas.Paleta)
                cores.Items.Add(new MenuItem
                {
                    Header = nome,
                    ToggleType = MenuItemToggleType.Radio,
                    GroupName = "cor",
                    IsChecked = string.Equals(etiqueta.Cor, cor, StringComparison.OrdinalIgnoreCase),
                    Icon = new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(Color.Parse(cor)) },
                    Command = new RelayCommand(() => vm.AcoesDosCards?.ColorirEtiqueta(etiqueta.Id, cor)),
                });
            item.Items.Add(cores);
            item.Items.Add(new MenuItem { Header = "Excluir", Command = new RelayCommand(() => vm.AcoesDosCards?.ExcluirEtiqueta(etiqueta.Id)) });
            menu.Items.Add(item);
        }
        if (vm.EtiquetasDoProjeto.Count > 0)
            menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Nova etiqueta…", Command = new AsyncRelayCommand(() => vm.AcoesDosCards?.NovaEtiquetaAsync(null) ?? Task.CompletedTask) });
        return menu;
    }

    private void AbrirMenuDosArquivados(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && sender is Control botao)
            AbrirMenu(MenuDosArquivados(vm), botao);
    }

    /// <summary>Bloco 77: os cards arquivados: abrir (no painel) ou trazer de volta ao quadro.</summary>
    internal static ContextMenu MenuDosArquivados(KanbanViewModel vm)
    {
        var menu = new ContextMenu();
        if (vm.Arquivados.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "Nenhum card arquivado", IsEnabled = false });
            return menu;
        }
        foreach (var arquivado in vm.Arquivados)
        {
            var item = new MenuItem { Header = arquivado.Nome };
            item.Items.Add(new MenuItem { Header = "Abrir card", Command = new RelayCommand(() => vm.AbrirCard(arquivado.TarefaId)) });
            item.Items.Add(new MenuItem { Header = "Desarquivar", Command = new RelayCommand(() => vm.AcoesDosCards?.Desarquivar([arquivado.TarefaId])) });
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var todos = vm.Arquivados.Select(a => a.TarefaId).ToList();
        menu.Items.Add(new MenuItem { Header = "Desarquivar todos", Command = new RelayCommand(() => vm.AcoesDosCards?.Desarquivar(todos)) });
        return menu;
    }

    /// <summary>
    /// Bloco 76 (K2): Renomear…, Cor, Tipo, Limite de cards…, Mover para a esquerda/direita e Excluir coluna…. Montado na hora (a cor
    /// e o tipo marcados são os de agora).
    /// </summary>
    internal static ContextMenu MenuDaColuna(ColunaNoKanban coluna)
    {
        var cores = new MenuItem { Header = "Cor" };
        foreach (var (nome, cor) in RegrasDasColunas.Paleta)
            cores.Items.Add(new MenuItem
            {
                Header = nome,
                Command = coluna.ColorirCommand,
                CommandParameter = cor,
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "cor",
                IsChecked = string.Equals(coluna.Cor, cor, StringComparison.OrdinalIgnoreCase),
                Icon = new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(Color.Parse(cor)) },
            });
        var tipos = new MenuItem { Header = "Tipo" };
        foreach (var tipo in Enum.GetValues<TipoDeColuna>())
            tipos.Items.Add(new MenuItem
            {
                Header = QuadroDoProjeto.NomeDoTipo(tipo) + tipo switch
                {
                    TipoDeColuna.AFazer => " (o card vai a 0%)",
                    TipoDeColuna.Concluida => " (o card vai a 100%)",
                    _ => " (o card mantém o %)",
                },
                Command = coluna.MudarTipoCommand,
                CommandParameter = tipo,
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "tipo",
                IsChecked = coluna.Tipo == tipo,
            });
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Renomear…", Command = coluna.RenomearCommand });
        menu.Items.Add(cores);
        menu.Items.Add(tipos);
        menu.Items.Add(new MenuItem { Header = "Limite de cards…", Command = coluna.DefinirLimiteCommand });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Mover para a esquerda", Command = coluna.MoverParaAEsquerdaCommand, IsEnabled = coluna.PodeIrParaAEsquerda });
        menu.Items.Add(new MenuItem { Header = "Mover para a direita", Command = coluna.MoverParaADireitaCommand, IsEnabled = coluna.PodeIrParaADireita });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Excluir coluna…", Command = coluna.ExcluirCommand });
        return menu;
    }

    private void TeclaApertada(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _arrastadas is not null)
        {
            Encerrar();
            e.Handled = true;
        }
    }

    // Os arrastados: os selecionados, se o apertado é um deles; senão, só ele. Na ordem do quadro.
    private void ComecarArraste(IPointer ponteiro)
    {
        if (Vm is not { } vm || _apertado is not { } card)
            return;
        var selecionados = vm.OrdemDosCards.Where(id => vm.CardDe(id)?.Selecionado == true).ToList();
        _arrastadas = selecionados.Contains(card.TarefaId) ? selecionados : [card.TarefaId];
        (_selecionarAoSoltar, _abrirAoSoltar) = (false, false);
        foreach (var id in _arrastadas)
            if (vm.CardDe(id) is { } arrastado)
                arrastado.Arrastado = true;
        ponteiro.Capture(this);
        _rolagemDoArraste.Start();
    }

    // O fantasma junto do cursor, a guia onde os cards vão cair e a dica (ou o motivo de não poder).
    private void AtualizarArraste()
    {
        if (Vm is not { } vm || _arrastadas is not { } arrastadas)
            return;
        Sobreposicao.Children.Clear();
        DesenharFantasma(vm, arrastadas);
        var destino = DestinoEm(_ultimoPonto);
        if (destino is null)
        {
            vm.MovimentoEmCurso = null;
            DesenharEtiqueta("Solte numa coluna do quadro (fora dele, cancela)", CorDoBloqueio);
            Cursor = CursorProibido;
            return;
        }
        var plano = destino.Celula is { } celula
            ? vm.PreverNaCelula(arrastadas, celula, destino.Lacuna)
            : vm.PreverSoltar(arrastadas, destino.Coluna.Id, destino.Lacuna);
        vm.MovimentoEmCurso = plano is null ? null : new MovimentoNoKanban(destino.Coluna.Id, destino.Lacuna, plano);
        Cursor = CursorArrastando;
        if (plano is { Valido: true, MudaAlgo: true })
        {
            Posicionar(new Rectangle { Width = destino.Largura, Height = 3, RadiusX = 1.5, RadiusY = 1.5, Fill = GuiaDeReordenacao.Cor },
                destino.X, destino.Y - 1.5);
            DesenharEtiqueta(arrastadas.Count > 1 ? $"{arrastadas.Count} cards · {plano.Dica}" : plano.Dica, GuiaDeReordenacao.Cor);
        }
        else if (plano is { Valido: false, Motivo: { } motivo })
        {
            DesenharEtiqueta(motivo, CorDoBloqueio);
            Cursor = CursorProibido;
        }
    }

    private void DesenharFantasma(KanbanViewModel vm, IReadOnlyList<Guid> arrastadas)
    {
        var nome = vm.CardDe(arrastadas[0])?.Nome ?? "";
        var fantasma = new Border
        {
            Width = LarguraDaColuna - 40,
            Opacity = 0.92,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8),
            BorderThickness = new Thickness(2),
            BorderBrush = GuiaDeReordenacao.Cor,
            Background = Recurso("FundoDoCard") ?? Brushes.White,
            BoxShadow = BoxShadows.Parse("0 4 12 0 #33000000"),
            Child = new TextBlock
            {
                Text = arrastadas.Count > 1 ? $"{nome}  (+{arrastadas.Count - 1})" : nome,
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeight.Medium,
                Foreground = Recurso("SystemControlForegroundBaseHighBrush"),
            },
        };
        Posicionar(fantasma, _ultimoPonto.X + 14, _ultimoPonto.Y + 10);
    }

    private void DesenharEtiqueta(string texto, IBrush fundo)
    {
        var rotulo = new Border
        {
            Background = fundo,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4),
            Child = new TextBlock { Text = texto, Foreground = Brushes.White, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 },
        };
        rotulo.Measure(Size.Infinity);
        var x = Math.Max(0, Math.Min(_ultimoPonto.X + 14, Bounds.Width - rotulo.DesiredSize.Width - 4));
        Posicionar(rotulo, x, Math.Max(0, _ultimoPonto.Y - rotulo.DesiredSize.Height - 8));
    }

    private IBrush? Recurso(string chave) =>
        this.TryFindResource(chave, ActualThemeVariant, out var valor) ? valor as IBrush : null;

    private void Posicionar(Control controle, double x, double y)
    {
        Canvas.SetLeft(controle, x);
        Canvas.SetTop(controle, y);
        Sobreposicao.Children.Add(controle);
    }

    // Perto das bordas do quadro rola na horizontal; perto do alto ou do pé da coluna sob o cursor, na vertical.
    private void RolarDuranteOArraste()
    {
        if (_arrastadas is null)
            return;
        var p = _ultimoPonto;
        var rolou = false;
        if (Vm is { ComRaias: true })
        {
            // Bloco 78: com raias, uma rolagem só, nas duas direções, pelas bordas da área das raias.
            var raias = Area(RolagemDasRaias);
            var ladoX = p.X < raias.Left + BordaDaRolagem ? -Passo(p.X - raias.Left) : p.X > raias.Right - BordaDaRolagem ? Passo(raias.Right - p.X) : 0;
            var ladoY = p.Y < raias.Top + BordaDaRolagem ? -Passo(p.Y - raias.Top) : p.Y > raias.Bottom - BordaDaRolagem ? Passo(raias.Bottom - p.Y) : 0;
            var novo = new Vector(
                Math.Clamp(RolagemDasRaias.Offset.X + ladoX, 0, Math.Max(0, RolagemDasRaias.Extent.Width - RolagemDasRaias.Viewport.Width)),
                Math.Clamp(RolagemDasRaias.Offset.Y + ladoY, 0, Math.Max(0, RolagemDasRaias.Extent.Height - RolagemDasRaias.Viewport.Height)));
            if (Math.Abs(novo.X - RolagemDasRaias.Offset.X) > 0.1 || Math.Abs(novo.Y - RolagemDasRaias.Offset.Y) > 0.1)
            {
                RolagemDasRaias.Offset = novo;
                AtualizarArraste();
            }
            return;
        }
        var quadro = Area(Rolagem);
        var dx = p.X < quadro.Left + BordaDaRolagem ? -Passo(p.X - quadro.Left)
            : p.X > quadro.Right - BordaDaRolagem ? Passo(quadro.Right - p.X) : 0;
        if (dx != 0)
        {
            var x = Math.Clamp(Rolagem.Offset.X + dx, 0, Math.Max(0, Rolagem.Extent.Width - Rolagem.Viewport.Width));
            if (Math.Abs(x - Rolagem.Offset.X) > 0.1)
            {
                Rolagem.Offset = Rolagem.Offset.WithX(x);
                rolou = true;
            }
        }
        if (DestinoEm(p) is { } destino && RolagemDa(destino.Coluna) is { } coluna)
        {
            var area = Area(coluna);
            var dy = p.Y < area.Top + BordaDaRolagem ? -Passo(p.Y - area.Top)
                : p.Y > area.Bottom - BordaDaRolagem ? Passo(area.Bottom - p.Y) : 0;
            if (dy != 0)
            {
                var y = Math.Clamp(coluna.Offset.Y + dy, 0, Math.Max(0, coluna.Extent.Height - coluna.Viewport.Height));
                if (Math.Abs(y - coluna.Offset.Y) > 0.1)
                {
                    coluna.Offset = coluna.Offset.WithY(y);
                    rolou = true;
                }
            }
        }
        if (rolou)
            AtualizarArraste();
    }

    // Mais rápido quanto mais perto da borda.
    private static double Passo(double distanciaDaBorda) => 4 + 14 * Math.Clamp(1 - distanciaDaBorda / BordaDaRolagem, 0, 1);

    private void Encerrar()
    {
        _seguraDoToque.Stop();
        _rolagemDoArraste.Stop();
        var ponteiro = _ponteiro;
        _ponteiro = null;
        if (Vm is { } vm)
        {
            if (_arrastadas is { } arrastadas)
                foreach (var id in arrastadas)
                    if (vm.CardDe(id) is { } card)
                        card.Arrastado = false;
            vm.MovimentoEmCurso = null;
        }
        _arrastadas = null;
        _apertado = null;
        (_selecionarAoSoltar, _abrirAoSoltar) = (false, false);
        Sobreposicao.Children.Clear();
        Cursor = null;
        if (ponteiro?.Captured == this)
            ponteiro.Capture(null);
    }

    /// <summary>
    /// Bloco 75: onde os cards cairiam: a coluna (a sob o cursor, ou a mais perto), a lacuna entre os cards dela e a guia. Bloco 78:
    /// com raias, a célula (a lacuna é entre os cards dela).
    /// </summary>
    private sealed record Destino(ColunaNoKanban Coluna, int Lacuna, double X, double Y, double Largura, CelulaNoKanban? Celula = null);

    private Destino? DestinoEm(Point ponto)
    {
        if (Vm is { ComRaias: true })
            return DestinoNasRaias(ponto);
        if (Vm is null || !Area(Rolagem).Contains(ponto))
            return null; // fora da área dos cards (na barra dos filtros, no painel, fora do quadro): soltar cancela
        (ColunaNoKanban Coluna, Control Raiz, Rect Area)? alvo = null;
        var menor = double.MaxValue;
        foreach (var (coluna, raiz) in ColunasNaTela())
        {
            var area = Area(raiz);
            var distancia = ponto.X < area.Left ? area.Left - ponto.X : ponto.X > area.Right ? ponto.X - area.Right : 0;
            if (distancia < menor)
                (menor, alvo) = (distancia, (coluna, raiz, area));
        }
        if (alvo is not { } a)
            return null;
        var cartoes = a.Raiz.GetVisualDescendants().OfType<ItemsControl>().FirstOrDefault(c => c.Classes.Contains("cartoes"));
        var visivel = RolagemDe(a.Raiz) is { } rolagem ? Area(rolagem) : a.Area;
        var cards = Enumerable.Range(0, a.Coluna.Cards.Count).Select(i => cartoes?.ContainerFromIndex(i)).OfType<Control>().Select(Area).ToList();
        var lacuna = cards.Count(c => c.Center.Y < ponto.Y);
        var y = cards.Count == 0 ? visivel.Top + 4
            : lacuna == 0 ? cards[0].Top - 3
            : lacuna == cards.Count ? cards[^1].Bottom + 3
            : (cards[lacuna - 1].Bottom + cards[lacuna].Top) / 2;
        y = Math.Clamp(y, visivel.Top + 2, Math.Max(visivel.Top + 2, visivel.Bottom - 2));
        return new Destino(a.Coluna, lacuna, a.Area.Left + 8, y, a.Area.Width - 16);
    }

    // Bloco 78: a célula sob o cursor (ou a mais perto) e a lacuna entre os cards dela.
    private Destino? DestinoNasRaias(Point ponto)
    {
        var visivel = Area(RolagemDasRaias);
        if (!visivel.Contains(ponto))
            return null;
        (CelulaNoKanban Celula, Control Raiz, Rect Area)? alvo = null;
        var menor = double.MaxValue;
        foreach (var raiz in RolagemDasRaias.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("celula") && b.IsEffectivelyVisible))
        {
            if (raiz.DataContext is not CelulaNoKanban celula)
                continue;
            var area = Area(raiz);
            var dx = ponto.X < area.Left ? area.Left - ponto.X : ponto.X > area.Right ? ponto.X - area.Right : 0;
            var dy = ponto.Y < area.Top ? area.Top - ponto.Y : ponto.Y > area.Bottom ? ponto.Y - area.Bottom : 0;
            if (dx + dy < menor)
                (menor, alvo) = (dx + dy, (celula, raiz, area));
        }
        if (alvo is not { } a)
            return null;
        var cartoes = a.Raiz.GetVisualDescendants().OfType<ItemsControl>().FirstOrDefault(c => c.Classes.Contains("cartoes"));
        var cards = Enumerable.Range(0, a.Celula.Cards.Count).Select(i => cartoes?.ContainerFromIndex(i)).OfType<Control>().Select(Area).ToList();
        var lacuna = cards.Count(c => c.Center.Y < ponto.Y);
        var y = cards.Count == 0 ? a.Area.Top + 6
            : lacuna == 0 ? cards[0].Top - 3
            : lacuna == cards.Count ? cards[^1].Bottom + 3
            : (cards[lacuna - 1].Bottom + cards[lacuna].Top) / 2;
        y = Math.Clamp(y, visivel.Top + 2, Math.Max(visivel.Top + 2, visivel.Bottom - 2));
        return new Destino(a.Celula.Coluna, lacuna, a.Area.Left + 8, y, a.Area.Width - 16, a.Celula);
    }

    private IEnumerable<(ColunaNoKanban Coluna, Control Raiz)> ColunasNaTela()
    {
        if (Vm is not { } vm)
            yield break;
        for (var i = 0; i < vm.Colunas.Count; i++)
            if (ListaDeColunas.ContainerFromIndex(i) is { } raiz)
                yield return (vm.Colunas[i], raiz);
    }

    private ScrollViewer? RolagemDa(ColunaNoKanban coluna) =>
        ColunasNaTela().Where(c => c.Coluna == coluna).Select(c => RolagemDe(c.Raiz)).FirstOrDefault();

    private static ScrollViewer? RolagemDe(Control raiz) =>
        raiz.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(c => c.Classes.Contains("cartoes"));

    // O retângulo do controle nas coordenadas do quadro.
    private Rect Area(Control controle) =>
        controle.TranslatePoint(new Point(0, 0), this) is { } topo ? new Rect(topo, controle.Bounds.Size) : default;

    private static CardNoKanban? CardSob(Visual? origem) =>
        origem?.GetSelfAndVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("card"))?.DataContext as CardNoKanban;

    private static double Distancia(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}
