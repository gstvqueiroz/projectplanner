using System.ComponentModel;
using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

// Bloco 25 (V1): a barra de status com o projeto, a selecionada e a versão ("hoje" é seg 28/09/2026).
public class BarraDeStatusTests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;

    public BarraDeStatusTests() => _vm = _ambiente.CriarViewModel();

    [Fact]
    public void ProjetoVazio()
    {
        (_vm.TextoTarefasNaBarra, _vm.TextoPeriodoNaBarra, _vm.TextoSelecionadaNaBarra).Should().Be(("Nenhuma tarefa", "", ""));
        _vm.TextoVersaoNaBarra.Should().Be($"v{VersaoDoProduto.Atual}");
    }

    [Fact]
    public void TarefasPeriodoESelecionada_ComAsDatasDoCalculo()
    {
        var a = Ambiente.AdicionarTarefa(_vm, "Fundação", 3);
        var b = Ambiente.AdicionarTarefa(_vm, "Estrutura", 4, "1");

        _vm.TextoTarefasNaBarra.Should().Be("2 tarefas");
        _vm.TextoPeriodoNaBarra.Should().Be("28/09/2026 → 06/10/2026 · 7 dias úteis");

        _vm.TarefaSelecionada = b;
        _vm.TextoSelecionadaNaBarra.Should().Be("2 Estrutura: 01/10/2026 → 06/10/2026", "as datas do cálculo, como no Gantt");
        _vm.TarefaSelecionada = null;
        _vm.TextoSelecionadaNaBarra.Should().BeEmpty();
    }

    [Fact]
    public void ResumosEMarcos_NaContagem()
    {
        var fase = Ambiente.AdicionarTarefa(_vm, "Fase", 1);
        _vm.TarefaSelecionada = fase;
        _vm.AdicionarSubtarefaCommand.Execute(null);
        _vm.AdicionarMarcoCommand.Execute(null);

        _vm.TextoTarefasNaBarra.Should().Be("3 tarefas (1 resumo, 1 marco)");
        _vm.TarefaSelecionada = _vm.Tarefas.Single(t => t.EhMarco);
        _vm.TextoSelecionadaNaBarra.Should().MatchRegex(@"^\d(\.\d)* .+: \d\d/\d\d/\d{4}$", "marco: um dia só");
    }

    [Fact]
    public void MudancasNoCronograma_AvisamABarra()
    {
        var a = Ambiente.AdicionarTarefa(_vm, "Fundação", 3);
        _vm.TarefaSelecionada = a;
        var avisos = new List<string?>();
        ((INotifyPropertyChanged)_vm).PropertyChanged += (_, e) => avisos.Add(e.PropertyName);

        a.Duracao = 5;

        avisos.Should().Contain([nameof(MainWindowViewModel.TextoPeriodoNaBarra), nameof(MainWindowViewModel.TextoSelecionadaNaBarra),
            nameof(MainWindowViewModel.TextoTarefasNaBarra)]);
        _vm.TextoSelecionadaNaBarra.Should().Be("1 Fundação: 28/09/2026 → 02/10/2026");
    }
}

// Bloco 25 (B6): a barra mostra o nome do arquivo, não o caminho inteiro (que fica na dica).
public class MensagemNaBarraTests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;

    public MensagemNaBarraTests() => _vm = _ambiente.CriarViewModel();

    [Fact]
    public void Abrir_NomeDoArquivoNaBarra_CaminhoNaMensagem()
    {
        _ambiente.Repositorio.AoAbrir = _ => new ProjectPlanner.Core.Cronograma("Obra", Ambiente.Segunda);
        _ambiente.Dialogos.ArquivoParaAbrir = "/Users/eu/Obras em andamento/Residência Silva/obra.pplan";

        _vm.AbrirProjetoCommand.Execute(null);

        _vm.Mensagem.Should().Be("Projeto aberto: /Users/eu/Obras em andamento/Residência Silva/obra.pplan");
        _vm.MensagemNaBarra.Should().Be("Projeto aberto: obra.pplan");
    }

    [Fact]
    public void Salvar_EOAvisoQueVemDepoisDoCaminho_Aparece()
    {
        _ambiente.Repositorio.AoAbrir = _ =>
        {
            var cronograma = new ProjectPlanner.Core.Cronograma("Obra", Ambiente.Segunda);
            cronograma.RegistrarFotosDescartadas(2);
            return cronograma;
        };
        _ambiente.Dialogos.ArquivoParaAbrir = "/muito/longo/caminho/de/pastas/antigo.pplan";
        _vm.AbrirProjetoCommand.Execute(null);
        _vm.MensagemNaBarra.Should().Be("Projeto aberto: antigo.pplan 2 registros de exemplo de fotos (sem imagem) foram descartados; eles somem do arquivo ao salvar.");

        _ambiente.Dialogos.ArquivoParaSalvar = "/outra/pasta/novo.pplan";
        _vm.SalvarComoProjetoCommand.Execute(null);
        (_vm.Mensagem, _vm.MensagemNaBarra).Should().Be(("Projeto salvo em /outra/pasta/novo.pplan", "Projeto salvo em novo.pplan"));
    }

    [Fact]
    public void MensagemSemCaminho_FicaComoEsta()
    {
        Ambiente.AdicionarTarefa(_vm, "Fundação", 3);
        _vm.RecalcularCronogramaCommand.Execute(null);

        _vm.MensagemNaBarra.Should().Be(_vm.Mensagem).And.Be("Cronograma recalculado.");
    }
}
