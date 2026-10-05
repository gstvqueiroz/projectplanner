using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Data;

namespace ProjectPlanner.App.Tests;

// Bloco 25 (parte D): o .pplan gravado pela 0.24.0 (ver ProjectPlanner.Data.Tests/ArquivoDaVersao0240Tests) aberto
// pelo programa, com o repositório SQLite de verdade: grid, Gantt, situação, fotos e salvar de novo. "Hoje" é 28/09/2026.
public sealed class ArquivoDaVersao0240NaTelaTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-0240-" + Guid.NewGuid().ToString("N"));
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;

    public ArquivoDaVersao0240NaTelaTests()
    {
        Directory.CreateDirectory(_pasta);
        var arquivo = Path.Combine(_pasta, "obra-0.24.0.pplan");
        File.Copy(Path.Combine(PastaDoRepositorio.Raiz, "tests", "ProjectPlanner.Data.Tests", "Arquivos", "obra-0.24.0.pplan"), arquivo);
        _ambiente.Dialogos.ArquivoParaAbrir = arquivo;
        _vm = new MainWindowViewModel(new SqliteProjetoRepository(_ambiente.Relogio), new CalculadoraCronogramaServico(),
            _ambiente.Dialogos, _ambiente.Relogio, _ambiente.Preferencias, _ambiente.Assistente, _ambiente.PreferenciasInterface,
            _ambiente.Processador, _ambiente.ConfiguracaoAssistente);
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private void Abrir()
    {
        _vm.AbrirProjetoCommand.Execute(null);
        _vm.Tarefas.Should().NotBeEmpty("o arquivo da 0.24 abriu");
    }

    [Fact]
    public void Abre_NaGridENoGantt_ComAsDatasDoCalculo()
    {
        Abrir();

        (_vm.NomeProjeto, _vm.InicioProjeto, _vm.TerminoProjeto, _vm.PossuiAlteracoesNaoSalvas)
            .Should().Be(("Residência Silva — referência 0.24", new DateTime(2026, 9, 7), new DateTime(2026, 10, 13), false));
        _vm.Tarefas.Select(t => $"{t.Edt} {t.Nome} {t.Duracao} {t.Predecessoras}").Should().Equal(
            "1 Fundação 9 ", "1.1 Escavação 3 ", "1.2 Sapatas 5 2", "1.3 Fundação concluída 0 3TI+1",
            "2 Estrutura 15 ", "2.1 Pilares 6 4", "2.2 Alvenaria 12 6II+2", "2.3 Cinta e laje 3 ",
            "2.3.1 Formas 2 7TT", "2.3.2 Concretagem 1 9IT+3", "3 Revestimento — cerâmica 60×60 «A» 4 10TI-1",
            "4 Cobertura 2 7", "5 Entrega da obra 0 12; 11");
        _vm.Gantt.Layout!.Barras.Should().HaveCount(13);
        _vm.TextoTarefasNaBarra.Should().Be("13 tarefas (3 resumos, 2 marcos)");
    }

    [Fact]
    public void Abre_ComASituacaoEAsFotos()
    {
        Abrir();

        var sapatas = _vm.Tarefas.Single(t => t.Nome == "Sapatas");
        (sapatas.Situacao, sapatas.QuantidadeDeFotos).Should().Be(("Atrasada", 1), "terminava em 16/09 e está em 60%");
        _vm.Tarefas.Single(t => t.Nome == "Escavação").Should().Match<TarefaViewModel>(t => t.Situacao == "Concluída" && t.QuantidadeDeFotos == 2);
        _vm.Tarefas.Single(t => t.Nome == "Cobertura").Situacao.Should().Be("Não iniciada");
    }

    [Fact]
    public void SalvarNaVersaoAtual_EAbrirDeNovo_Igual()
    {
        Abrir();
        var antes = _vm.Tarefas.Select(t => $"{t.Edt} {t.Nome} {t.Duracao} {t.PercentualConcluido} {t.Predecessoras} {t.QuantidadeDeFotos} {t.Notas}").ToList();
        _ambiente.Dialogos.ArquivoParaSalvar = Path.Combine(_pasta, "obra-0.25.pplan");

        _vm.SalvarComoProjetoCommand.Execute(null);
        _ambiente.Dialogos.ArquivoParaAbrir = _ambiente.Dialogos.ArquivoParaSalvar;
        _vm.AbrirProjetoCommand.Execute(null);

        _vm.Tarefas.Select(t => $"{t.Edt} {t.Nome} {t.Duracao} {t.PercentualConcluido} {t.Predecessoras} {t.QuantidadeDeFotos} {t.Notas}")
            .Should().Equal(antes);
        _vm.Mensagem.Should().Contain("obra-0.25.pplan");
    }
}
