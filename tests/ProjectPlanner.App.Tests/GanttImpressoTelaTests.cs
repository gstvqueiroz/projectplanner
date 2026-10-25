using System.Text;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Relatorios;

namespace ProjectPlanner.App.Tests;

// Bloco 57, na tela: Arquivo > Imprimir o Gantt…: as escolhas, a prévia por página, Salvar PDF… e Imprimir….
// "Hoje" é seg 28/09/2026, o início do projeto: Etapa (resumo) com A (5 d) → B (10 d, predecessora 2).
public sealed class GanttImpressoTelaTests : IDisposable
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly TarefaViewModel _etapa;
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-gantt-" + Guid.NewGuid().ToString("N"));

    public GanttImpressoTelaTests()
    {
        Directory.CreateDirectory(_pasta);
        _vm = _ambiente.CriarViewModel();
        _etapa = Ambiente.AdicionarTarefa(_vm, "Etapa", 1);
        var a = Ambiente.AdicionarTarefa(_vm, "A", 5);
        var b = Ambiente.AdicionarTarefa(_vm, "B", 10, "2");
        _vm.TarefaSelecionada = a;
        _vm.RebaixarCommand.Execute(null);
        _vm.TarefaSelecionada = b;
        _vm.RebaixarCommand.Execute(null);
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private GanttImpressoViewModel Abrir()
    {
        _vm.ImprimirGanttCommand.Execute(null);
        return _ambiente.Dialogos.ImpressoesDoGantt.Last();
    }

    [Fact]
    public void Abre_NaA3Paisagem_AjustadaALargura_ComAPrevia_EAsPredecessorasDaGrid()
    {
        var tela = Abrir();

        (tela.FolhaEscolhida.Valor, tela.OrientacaoEscolhida.Valor, tela.EscalaEscolhida.Valor, tela.NivelEscolhido.Valor)
            .Should().Be((TamanhoDaFolha.A3, true, EscalaDaImpressao.AjustarALargura, (int?)null));
        tela.Folhas.Select(f => f.Texto).Should().Equal("A4 (210 × 297 mm)", "A3 (297 × 420 mm)", "A2 (420 × 594 mm)", "A1 (594 × 841 mm)",
            "A0 (841 × 1189 mm)", "Carta (216 × 279 mm)", "Ofício (216 × 330 mm)");
        (tela.Resumo, tela.TextoDaPagina, tela.Erro).Should().Be(("1 página · A3 paisagem", "Página 1 de 1", ""));
        tela.Previa.Should().NotBeNull();
        Encoding.ASCII.GetString(tela.Previa!, 1, 3).Should().Be("PNG");
        tela.LinhaDeBase.Should().BeFalse("ainda não há linha de base");
        tela.TemLinhaDeBase.Should().BeFalse();

        var opcoes = tela.Opcoes();
        opcoes.Predecessoras.Values.Should().Contain("2");
        (opcoes.Titulo, opcoes.Subtitulo).Should().Be((_vm.NomeProjeto, "gerado em 28/09/2026 12:00"));
    }

    [Fact]
    public void MudarAFolhaEAEscala_RefazAsPaginas_EANavegacaoAnda()
    {
        var tela = Abrir();

        tela.FolhaEscolhida = tela.Folhas.Single(f => f.Valor == TamanhoDaFolha.A4);
        tela.OrientacaoEscolhida = tela.Orientacoes.Single(o => !o.Valor);
        tela.EscalaEscolhida = tela.Escalas.Single(e => e.Valor == EscalaDaImpressao.Dias);

        tela.Impressao!.FaixasDeTempo.Should().BeGreaterThan(1, "em dias, 3 semanas não cabem na A4 em pé");
        tela.Resumo.Should().EndWith("· A4 retrato");
        tela.PaginaAnteriorCommand.CanExecute(null).Should().BeFalse();
        var primeira = tela.Previa;

        tela.ProximaPaginaCommand.Execute(null);

        (tela.Pagina, tela.TextoDaPagina).Should().Be((1, $"Página 2 de {tela.Impressao.Paginas}"));
        tela.Previa.Should().NotBeSameAs(primeira);
        tela.PaginaAnteriorCommand.CanExecute(null).Should().BeTrue();

        tela.Legenda = false;
        tela.Pagina.Should().Be(0, "mudar uma opção volta à primeira página");
    }

    [Fact]
    public void Niveis_SoAsEtapas_OuComoNaTela()
    {
        _etapa.Expandido = false;
        var tela = Abrir();

        tela.NivelEscolhido.Valor.Should().Be(GanttImpressoViewModel.ComoNaTela, "há um resumo recolhido na tela");
        tela.Impressao!.Datas.Should().Contain("1 tarefa");

        tela.NivelEscolhido = tela.Niveis[0];
        tela.Impressao!.Datas.Should().Contain("3 tarefas");

        tela.NivelEscolhido = tela.Niveis.Single(n => n.Valor == 1);
        (tela.Opcoes().NivelMaximo, tela.Impressao!.Datas).Should().Match<(int?, string)>(x => x.Item1 == 1 && x.Item2.Contains("1 tarefa"));
    }

    [Fact]
    public async Task SalvarPdf_GravaNoCaminhoEscolhido()
    {
        var tela = Abrir();
        _ambiente.Dialogos.ArquivoExportado = Path.Combine(_pasta, "gantt");

        await tela.SalvarPdfCommand.ExecuteAsync(null);

        _ambiente.Dialogos.Exportacoes.Should().ContainSingle().Which.Should().Be(("Salvar o Gantt em PDF", $"Gantt - {_vm.NomeProjeto}", "pdf"));
        var pdf = await File.ReadAllBytesAsync(Path.Combine(_pasta, "gantt.pdf"));
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        tela.Mensagem.Should().Be("Salvo em gantt.pdf.");
    }

    [Fact]
    public async Task Imprimir_AbreOPdfNoLeitor_EDizComoImprimir()
    {
        var tela = Abrir();

        await tela.ImprimirCommand.ExecuteAsync(null);

        var caminho = _ambiente.Dialogos.ArquivosAbertos.Should().ContainSingle().Subject;
        try
        {
            (Path.GetExtension(caminho), File.Exists(caminho)).Should().Be((".pdf", true));
            tela.Mensagem.Should().StartWith("O PDF abriu no leitor do sistema: imprima com ").And.EndWith("escolhendo a folha A3 na impressora.");
        }
        finally
        {
            File.Delete(caminho);
        }
    }
}
