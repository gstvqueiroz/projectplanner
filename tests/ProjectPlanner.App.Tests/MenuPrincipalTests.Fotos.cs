using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ProjectPlanner.App.Views;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Gantt;

namespace ProjectPlanner.App.Tests;

// Bloco 17, na janela de verdade: menu de contexto da grid e do Gantt, câmera na coluna Nome e a galeria.
public partial class MenuPrincipalTests
{
    private static Button? CameraDaLinha(Abertura a, int indice)
    {
        var linha = a.Janela.Grade.GetVisualDescendants().OfType<DataGridRow>()
            .SingleOrDefault(r => r.DataContext == a.Vm.LinhasVisiveis[indice]);
        return linha?.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Classes.Contains("indicadorDeFotos"));
    }

    [Fact]
    public Task Grade_IndicadorDeFotos_SoNasTarefasComFotos_EAbreAGaleria() => Tela.Executar(() =>
    {
        var a = Abrir();
        var semFotos = Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        var comFotos = Ambiente.AdicionarTarefa(a.Vm, "Estrutura", 2);
        Pintar(a);
        CameraDaLinha(a, 0)!.IsVisible.Should().BeFalse();
        CameraDaLinha(a, 1)!.IsVisible.Should().BeFalse();

        a.Vm.GerenciarFotosCommand.Execute(comFotos);
        Esperar(a.Ambiente.Dialogos.Galerias.Last().AdicionarArquivosAsync(["/fotos/a.jpg"]));
        Pintar(a);

        CameraDaLinha(a, 0)!.IsVisible.Should().BeFalse();
        var camera = CameraDaLinha(a, 1)!;
        camera.IsVisible.Should().BeTrue();
        ToolTip.GetTip(camera).Should().Be("1 foto — abrir a galeria");
        camera.GetLogicalDescendants().OfType<TextBlock>().Single().Text.Should().Be("1");

        a.Vm.TarefaSelecionada = semFotos;
        a.ClicarComOMouse(camera);
        a.Ambiente.Dialogos.Galerias.Last().Titulo.Should().Be("Fotos — 2 Estrutura");
    });

    [Fact]
    public Task Grade_CliqueDireitoNumaLinha_SelecionaEOMenuAbreAGaleria() => Tela.Executar(() =>
    {
        var a = Abrir();
        Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        var segunda = Ambiente.AdicionarTarefa(a.Vm, "Estrutura", 2);
        a.Vm.TarefaSelecionada = a.Vm.Tarefas[0];
        Pintar(a);
        var linha = a.Janela.Grade.GetVisualDescendants().OfType<DataGridRow>().Single(r => r.DataContext == segunda);
        var celula = linha.GetVisualDescendants().OfType<DataGridCell>().First();

        var pedido = new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent, Source = celula };
        celula.RaiseEvent(pedido);

        a.Vm.TarefaSelecionada.Should().Be(segunda);
        pedido.Handled.Should().BeTrue("o menu de contexto abriu");
        a.Janela.MenuDaGrade.Items.OfType<MenuItem>().Select(i => i.Header)
            .Should().Equal("Inserir tarefa acima", "Inserir tarefa abaixo", "Copiar tarefa", "Colar tarefa", "Duplicar tarefa", "Gerenciar fotos…");
        var item = a.Janela.MenuDaGrade.Items.OfType<MenuItem>().Last();
        item.Command!.Execute(item.CommandParameter);
        a.Ambiente.Dialogos.Galerias.Last().Titulo.Should().Be("Fotos — 2 Estrutura");
        var acima = a.Janela.MenuDaGrade.Items.OfType<MenuItem>().First();
        acima.Command!.Execute(acima.CommandParameter); // Bloco 21: tarefa nova acima da linha clicada
        a.Vm.Tarefas.Select(t => t.Nome).Should().Equal("Fundação", "Nova tarefa", "Estrutura");
        a.Janela.MenuDaGrade.Close();
    });

    [Fact]
    public Task Grade_CliqueDireitoForaDasLinhas_SemMenu() => Tela.Executar(() =>
    {
        var a = Abrir();
        Pintar(a);
        var pedido = new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent, Source = a.Janela.Grade };

        a.Janela.Grade.RaiseEvent(pedido);

        (pedido.Handled, a.Janela.MenuDaGrade.IsOpen).Should().Be((true, false));
    });

    [Fact]
    public Task Gantt_CliqueDireitoNaBarra_SelecionaEOMenuAbreAGaleria_ForaDaBarraSemMenu() => Tela.Executar(() =>
    {
        var a = Abrir();
        Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        var segunda = Ambiente.AdicionarTarefa(a.Vm, "Estrutura", 2);
        a.Vm.TarefaSelecionada = a.Vm.Tarefas[0];
        Pintar(a);
        var superficie = a.Janela.Gantt.FindControl<Panel>("Superficie")!;
        var barra = a.Vm.Gantt.Layout!.Barras.Single(b => b.TarefaId == segunda.Tarefa.Id);
        var menu = superficie.ContextMenu!;

        // Clique direito de verdade sobre a barra da Estrutura.
        var ponto = superficie.TranslatePoint(new Point(barra.X + barra.Largura / 2, barra.CentroY), a.Janela)!.Value;
        a.Janela.MouseDown(ponto, MouseButton.Right);
        a.Janela.MouseUp(ponto, MouseButton.Right);
        Pintar(a);

        a.Vm.TarefaSelecionada.Should().Be(segunda);
        menu.IsOpen.Should().BeTrue();
        var item = menu.Items.OfType<MenuItem>().Single();
        item.Header.Should().Be("Gerenciar fotos…");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        a.Ambiente.Dialogos.Galerias.Last().Titulo.Should().Be("Fotos — 2 Estrutura");
        menu.Close();
        Pintar(a);

        // Fora das barras: nada.
        var vazio = superficie.TranslatePoint(new Point(5, barra.CentroY + 5 * GanttViewModelAltura), a.Janela)!.Value;
        a.Janela.MouseDown(vazio, MouseButton.Right);
        a.Janela.MouseUp(vazio, MouseButton.Right);
        Pintar(a);
        menu.IsOpen.Should().BeFalse();
    });

    [Fact]
    public Task Gantt_PedidoDeMenuSemPosicao_UsaOMouseSobreABarra() => Tela.Executar(() =>
    {
        var a = Abrir();
        Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        var segunda = Ambiente.AdicionarTarefa(a.Vm, "Estrutura", 2);
        a.Vm.TarefaSelecionada = a.Vm.Tarefas[0];
        Pintar(a);
        var superficie = a.Janela.Gantt.FindControl<Panel>("Superficie")!;
        var barra = a.Vm.Gantt.Layout!.Barras.Single(b => b.TarefaId == segunda.Tarefa.Id);
        a.Janela.MouseMove(superficie.TranslatePoint(new Point(barra.X + barra.Largura / 2, barra.CentroY), a.Janela)!.Value);

        // Pedido do menu sem a posição do ponteiro.
        var pedido = new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent, Source = superficie };
        superficie.RaiseEvent(pedido);
        Pintar(a);

        (a.Vm.TarefaSelecionada, superficie.ContextMenu!.IsOpen).Should().Be((segunda, true));
        superficie.ContextMenu.Close();

        // Com o mouse fora do Gantt, um pedido sem posição (teclado) não abre nada.
        a.Janela.MouseMove(new Point(5, 5));
        var outro = new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent, Source = superficie };
        superficie.RaiseEvent(outro);
        Pintar(a);
        superficie.ContextMenu.IsOpen.Should().BeFalse();
    });

    private const double GanttViewModelAltura = ViewModels.GanttViewModel.AlturaLinha;

    /// <summary>Roda a fila da tela até a operação assíncrona terminar (a importação lê os arquivos fora dela).</summary>
    private static void Esperar(Task tarefa)
    {
        var limite = DateTime.UtcNow.AddSeconds(10);
        while (!tarefa.IsCompleted && DateTime.UtcNow < limite)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }
        tarefa.GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
    }

    private static (GaleriaFotosWindow Janela, ViewModels.GaleriaFotosViewModel Galeria) AbrirGaleria(Abertura a, ViewModels.TarefaViewModel linha)
    {
        a.Vm.GerenciarFotosCommand.Execute(linha);
        var galeria = a.Ambiente.Dialogos.Galerias.Last();
        var janela = new GaleriaFotosWindow(galeria);
        janela.Show();
        Dispatcher.UIThread.RunJobs();
        return (janela, galeria);
    }

    private static List<Border> Miniaturas(GaleriaFotosWindow janela) =>
        janela.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("miniatura")).ToList();

    private static void ClicarEm(GaleriaFotosWindow janela, Control alvo, RawInputModifiers modificadores = RawInputModifiers.None)
    {
        var ponto = alvo.TranslatePoint(new Point(alvo.Bounds.Width / 2, alvo.Bounds.Height / 2), janela)!.Value;
        janela.MouseDown(ponto, MouseButton.Left, modificadores);
        janela.MouseUp(ponto, MouseButton.Left, modificadores);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task Galeria_Vazia_MostraAMensagem_EAdicionarPeloDialogoMostraAsMiniaturas() => Tela.Executar(() =>
    {
        var a = Abrir();
        var tarefa = Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        var (janela, galeria) = AbrirGaleria(a, tarefa);

        var adicionar = janela.FindControl<Button>("Adicionar")!;
        var vazia = janela.FindControl<TextBlock>("MensagemVazia")!;
        (adicionar.IsEffectivelyVisible, adicionar.IsEffectivelyEnabled, adicionar.Content).Should().Be((true, true, "Adicionar fotos…"));
        (vazia.IsVisible, vazia.Text).Should().Be((true, "Nenhuma foto nesta tarefa. Clique em Adicionar fotos (ou arraste fotos para cá)."));
        janela.FindControl<TextBlock>("InfoDaTarefa")!.Text.Should().StartWith("EDT 1 · Fundação · Início 28/09/2026");
        janela.FindControl<Control>("Detalhes")!.IsVisible.Should().BeFalse("sem foto escolhida, sem detalhes");

        a.Ambiente.Dialogos.FotosParaAdicionar.AddRange(["/fotos/a.jpg", "/fotos/b.jpg", "/fotos/c.heic"]);
        ClicarEm(janela, adicionar);
        Esperar(Task.Run(() => SpinWait.SpinUntil(() => !galeria.Ocupado && galeria.Contador == "2 fotos", 5000)));

        tarefa.Tarefa.Fotos.Should().HaveCount(2);
        vazia.IsVisible.Should().BeFalse();
        Miniaturas(janela).Should().HaveCount(2);
        janela.FindControl<TextBlock>("Contador")!.Text.Should().Be("2 fotos");
        janela.FindControl<TextBlock>("Aviso")!.Text.Should().StartWith("2 fotos adicionadas. Recusada: c.heic: fotos HEIC ainda não são aceitas.");
        janela.FindControl<TextBox>("EdicaoTitulo")!.IsEffectivelyVisible.Should().BeTrue("a última adicionada fica escolhida");
        janela.Close();
    });

    [Fact]
    public Task Galeria_CliqueSeleciona_CmdCliqueAcrescenta_DeleteExcluiAsSelecionadas() => Tela.Executar(() =>
    {
        var a = Abrir();
        var tarefa = Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        var (janela, galeria) = AbrirGaleria(a, tarefa);
        Esperar(galeria.AdicionarArquivosAsync(["/fotos/a.jpg", "/fotos/b.jpg", "/fotos/c.jpg"]));
        var miniaturas = Miniaturas(janela);

        ClicarEm(janela, miniaturas[0]);
        ClicarEm(janela, miniaturas[2], RawInputModifiers.Meta);

        galeria.QuantidadeSelecionada.Should().Be(2);
        Miniaturas(janela).Select(m => m.Classes.Contains("selecionada")).Should().Equal(true, false, true);
        galeria.FotoAtual!.Foto.Imagem.NomeArquivoOriginal.Should().Be("c.jpg");

        ClicarEm(janela, miniaturas[1]);
        galeria.QuantidadeSelecionada.Should().Be(1, "sem Cmd, o clique deixa só ela");

        janela.KeyPress(Key.A, RawInputModifiers.Meta, PhysicalKey.A, "a");
        Dispatcher.UIThread.RunJobs();
        galeria.QuantidadeSelecionada.Should().Be(3);

        janela.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        Dispatcher.UIThread.RunJobs();

        a.Ambiente.Dialogos.Confirmacoes.Last().Should().Be("Excluir as 3 fotos selecionadas? Não dá para desfazer.");
        tarefa.Tarefa.Fotos.Should().BeEmpty();
        janela.FindControl<TextBlock>("MensagemVazia")!.IsVisible.Should().BeTrue();
        janela.Close();
    });

    [Fact]
    public Task Galeria_SoltarArquivos_AdicionaAsFotos_EOutrosDadosSaoIgnorados() => Tela.Executar(() =>
    {
        var a = Abrir();
        var tarefa = Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        var (janela, galeria) = AbrirGaleria(a, tarefa);
        var meio = new Point(janela.Bounds.Width / 2, janela.Bounds.Height / 2);

        var texto = new DataTransfer();
        texto.Add(DataTransferItem.CreateText("não é arquivo"));
        janela.DragDrop(meio, RawDragEventType.DragEnter, texto, DragDropEffects.Copy);
        janela.DragDrop(meio, RawDragEventType.Drop, texto, DragDropEffects.Copy);
        Dispatcher.UIThread.RunJobs();
        tarefa.Tarefa.Fotos.Should().BeEmpty();

        var arquivos = new DataTransfer();
        arquivos.Add(DataTransferItem.CreateFile(ArquivoArrastado("/fotos/a.jpg")));
        arquivos.Add(DataTransferItem.CreateFile(ArquivoArrastado("/fotos/b.webp")));
        janela.DragDrop(meio, RawDragEventType.DragEnter, arquivos, DragDropEffects.Copy);
        janela.DragDrop(meio, RawDragEventType.Drop, arquivos, DragDropEffects.Copy);
        Esperar(Task.Run(() => SpinWait.SpinUntil(() => !galeria.Ocupado && tarefa.Tarefa.Fotos.Count == 2, 5000)));

        tarefa.Tarefa.Fotos.Select(f => f.Imagem.NomeArquivoOriginal).Should().BeEquivalentTo(["a.jpg", "b.webp"]);
        Miniaturas(janela).Should().HaveCount(2);
        janela.Close();
    });

    [Fact]
    public Task Galeria_DuploCliqueNaMiniatura_AbreOVisualizador_SetasNavegam_EscFecha() => Tela.Executar(() =>
    {
        var a = Abrir();
        var tarefa = Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        var (janela, galeria) = AbrirGaleria(a, tarefa);
        Esperar(galeria.AdicionarArquivosAsync(["/fotos/a.jpg", "/fotos/b.jpg", "/fotos/c.jpg"]));
        var segunda = Miniaturas(janela)[1];
        var ponto = segunda.TranslatePoint(new Point(segunda.Bounds.Width / 2, segunda.Bounds.Height / 2), janela)!.Value;

        janela.MouseDown(ponto, MouseButton.Left);
        janela.MouseUp(ponto, MouseButton.Left);
        janela.MouseDown(ponto, MouseButton.Left);
        janela.MouseUp(ponto, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var vm = a.Ambiente.Dialogos.Visualizadores.Should().ContainSingle().Subject;
        var visualizador = new VisualizadorFotosWindow(vm);
        visualizador.Show();
        Dispatcher.UIThread.RunJobs();
        var posicao = visualizador.FindControl<TextBlock>("Posicao")!;
        (posicao.Text, vm.Atual.Imagem.NomeArquivoOriginal).Should().Be(("2 de 3", "b.jpg"));

        visualizador.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        visualizador.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        Dispatcher.UIThread.RunJobs();
        (posicao.Text, visualizador.FindControl<Button>("Proxima")!.IsEffectivelyEnabled).Should().Be(("3 de 3", false));

        visualizador.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
        Dispatcher.UIThread.RunJobs();
        posicao.Text.Should().Be("2 de 3");

        visualizador.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        visualizador.IsVisible.Should().BeFalse("Esc fecha o visualizador");
        janela.Close();
    });

    // A decodificação de verdade não roda aqui (o desenho sem tela aceita quaisquer bytes); ela é conferida no app publicado.
    [Fact]
    public void ConversorDeImagem_SemBytes_ViraNada()
    {
        var cultura = System.Globalization.CultureInfo.InvariantCulture;

        Conversores.Imagem.Convert(Array.Empty<byte>(), typeof(object), null, cultura).Should().BeNull();
        Conversores.Imagem.Convert(null, typeof(object), null, cultura).Should().BeNull();
    }

    /// <summary>
    /// Um arquivo arrastado do Finder. O IStorageFile do Avalonia não pode ser implementado fora dele, então usa o
    /// arquivo de disco do próprio Avalonia (o mesmo que a área de transferência entrega); só o caminho importa.
    /// </summary>
    private static IStorageFile ArquivoArrastado(string caminho)
    {
        var tipo = typeof(IStorageFile).Assembly.GetTypes().Single(t => t.Name == "BclStorageFile");
        return (IStorageFile)Activator.CreateInstance(tipo, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic, null, [new FileInfo(caminho)], null)!;
    }
}
