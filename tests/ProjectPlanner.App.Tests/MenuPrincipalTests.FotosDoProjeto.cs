using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ProjectPlanner.App.Views;

namespace ProjectPlanner.App.Tests;

// Bloco 28, na janela de verdade: Exibir > Fotos do projeto…, o aviso de tarefa sem foto na grid e no Resumo.
public partial class MenuPrincipalTests
{
    private static Button? AlertaDaLinha(Abertura a, int indice)
    {
        var linha = a.Janela.Grade.GetVisualDescendants().OfType<DataGridRow>()
            .SingleOrDefault(r => r.DataContext == a.Vm.LinhasVisiveis[indice]);
        return linha?.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Classes.Contains("alertaDeFoto"));
    }

    [Fact]
    public Task FotosDoProjeto_PeloMenu_AsTresAbas() => Tela.Executar(() =>
    {
        var a = Abrir();
        Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);

        a.Clicar("Exibir", "Fotos do projeto…");

        var fotos = a.Ambiente.Dialogos.FotosDoProjeto.Should().ContainSingle().Subject;
        var janela = new FotosDoProjetoWindow(fotos);
        janela.Show();
        Dispatcher.UIThread.RunJobs();
        janela.FindControl<TabControl>("Abas")!.ItemCount.Should().Be(3);
        janela.FindControl<TextBlock>("MensagemVazia")!.IsVisible.Should().BeTrue();
        janela.FindControl<TextBlock>("Contador")!.Text.Should().Be("0 fotos em 0 tarefas");
        janela.FindControl<ComboBox>("FiltroTarefa")!.SelectedItem!.ToString().Should().Be("(todas as tarefas)");

        Esperar(fotos.LerLoteAsync(["/fotos/a.jpg"]));
        Dispatcher.UIThread.RunJobs();
        janela.FindControl<TabControl>("Abas")!.SelectedIndex.Should().Be(1, "ler fotos leva à aba da importação");
        janela.FindControl<DataGrid>("TabelaDoLote")!.IsVisible.Should().BeTrue();
        janela.FindControl<Button>("Importar")!.IsEffectivelyEnabled.Should().BeTrue("29/09 está no período da Fundação");
        janela.FindControl<TextBlock>("ResumoDoLote")!.Text.Should().Be("1 de 1 foto vai ser importada.");

        fotos.AbaEscolhida = 2;
        Dispatcher.UIThread.RunJobs();
        janela.FindControl<NumericUpDown>("DiasSemFoto")!.Value.Should().Be(7);
        janela.FindControl<TextBlock>("TituloDoAlerta")!.Text.Should().Be("Nenhuma tarefa em andamento está sem foto há 7 dias ou mais.");
        janela.Close();
    });

    // Trocar as listas dos filtros com a janela aberta fazia o ComboBox desmarcar e remarcar a escolha, e a tela
    // se refazia sem parar (estouro de pilha).
    [Fact]
    public Task FotosDoProjeto_ImportarEFiltrarComAJanelaAberta_MantemOsFiltros() => Tela.Executar(() =>
    {
        var a = Abrir();
        Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        Ambiente.AdicionarTarefa(a.Vm, "Estrutura", 3, "1");
        a.Ambiente.Processador.Datas["e.jpg"] = new DateTime(2026, 10, 1, 9, 0, 0);
        a.Clicar("Exibir", "Fotos do projeto…");
        var fotos = a.Ambiente.Dialogos.FotosDoProjeto.Single();
        var janela = new FotosDoProjetoWindow(fotos);
        janela.Show();
        Dispatcher.UIThread.RunJobs();

        Esperar(fotos.LerLoteAsync(["/fotos/f.jpg", "/fotos/e.jpg"]));
        fotos.LocalDoLote = "Bloco A";
        fotos.ImportarLoteCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        fotos.TarefaFiltro = fotos.TarefasComFotos[2];
        fotos.LocalFiltro = "Bloco A";
        Dispatcher.UIThread.RunJobs();
        fotos.Atualizar();
        Dispatcher.UIThread.RunJobs();

        janela.FindControl<ComboBox>("FiltroTarefa")!.SelectedItem!.ToString().Should().Be("2 Estrutura");
        janela.FindControl<ComboBox>("FiltroLocal")!.SelectedItem.Should().Be("Bloco A");
        janela.FindControl<TextBlock>("Contador")!.Text.Should().Be("1 de 2 fotos");
        fotos.FotosFiltradas.Select(f => f.Imagem.NomeArquivoOriginal).Should().Equal("e.jpg");
        janela.Close();
    });

    [Fact]
    public Task Grade_AlertaDeFoto_NaTarefaEmAndamento_EAbreAGaleria() => Tela.Executar(() =>
    {
        var a = Abrir();
        a.Vm.InicioProjeto = new DateTime(2026, 9, 14);
        Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);
        var concluida = Ambiente.AdicionarTarefa(a.Vm, "Gabarito", 1);
        concluida.PercentualConcluido = 100;
        Pintar(a);

        var alerta = AlertaDaLinha(a, 0)!;
        alerta.IsVisible.Should().BeTrue();
        ToolTip.GetTip(alerta).Should().Be("Sem nenhuma foto desde o início, há 14 dias — abrir a galeria");
        AlertaDaLinha(a, 1)!.IsVisible.Should().BeFalse("concluída");

        a.ClicarComOMouse(alerta);
        a.Ambiente.Dialogos.Galerias.Last().Titulo.Should().Be("Fotos — 1 Fundação");
        a.Ambiente.Processador.Datas["hoje.jpg"] = new DateTime(2026, 9, 28, 9, 0, 0);
        Esperar(a.Ambiente.Dialogos.Galerias.Last().AdicionarArquivosAsync(["/fotos/hoje.jpg"]));
        Pintar(a);
        AlertaDaLinha(a, 0)!.IsVisible.Should().BeFalse("tem foto de hoje");
    });

    [Fact]
    public Task Resumo_ListaAsTarefasSemFotoRecente() => Tela.Executar(() =>
    {
        var a = Abrir();
        a.Vm.InicioProjeto = new DateTime(2026, 9, 14);
        Ambiente.AdicionarTarefa(a.Vm, "Fundação", 3);

        a.Clicar("Exibir", "Resumo do projeto…");

        var janela = new ResumoDoProjetoWindow(a.Ambiente.Dialogos.Resumos.Last());
        janela.Show();
        Dispatcher.UIThread.RunJobs();
        janela.FindControl<ItemsControl>("ListaSemFotoRecente")!.Items.Cast<string>()
            .Should().Equal("1 Fundação — Sem nenhuma foto desde o início, há 14 dias");
        janela.Close();
    });
}
