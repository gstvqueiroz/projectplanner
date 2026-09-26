using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Gantt;

namespace ProjectPlanner.App.Tests;

// Arrastar de uma barra para outra no Gantt (Bloco 12), pela lógica do GanttViewModel, sem mouse:
// PreverLigacao é o que a tela mostra durante o arraste; SoltarLigacao, o que acontece ao soltar.
// "Hoje" é seg 28/09/2026: A (3 dias) vai de seg a qua; B (2 dias) e C (1 dia) começam na segunda.
public class LigacaoPorArrasteTests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly TarefaViewModel _a, _b, _c;

    public LigacaoPorArrasteTests()
    {
        _vm = _ambiente.CriarViewModel();
        _a = Ambiente.AdicionarTarefa(_vm, "A", 3);
        _b = Ambiente.AdicionarTarefa(_vm, "B", 2);
        _c = Ambiente.AdicionarTarefa(_vm, "C", 1);
    }

    private GanttViewModel Gantt => _vm.Gantt;

    private bool Soltar(TarefaViewModel origem, BordaDaBarra bordaDeOrigem, TarefaViewModel? destino,
        BordaDaBarra bordaDeDestino = BordaDaBarra.Inicio) =>
        Gantt.SoltarLigacao(origem.Tarefa.Id, bordaDeOrigem, destino?.Tarefa.Id, bordaDeDestino);

    private PrevisaoDeLigacao? Prever(TarefaViewModel origem, BordaDaBarra bordaDeOrigem, TarefaViewModel? destino,
        BordaDaBarra bordaDeDestino = BordaDaBarra.Inicio) =>
        Gantt.PreverLigacao(origem.Tarefa.Id, bordaDeOrigem, destino?.Tarefa.Id, bordaDeDestino);

    private void Salvar()
    {
        _ambiente.Dialogos.ArquivoParaSalvar = "/obra.pplan";
        _vm.SalvarProjetoCommand.Execute(null);
    }

    // --- Arraste válido ---

    [Fact]
    public void DoTerminoParaOCorpoDoDestino_CriaTIRecalculaEAtualizaGridEGantt()
    {
        Salvar();
        var chamadas = _ambiente.Calculadora.Chamadas;

        var criou = Soltar(_a, BordaDaBarra.Termino, _b);

        criou.Should().BeTrue();
        _vm.Cronograma.Predecessoras.Should().Equal(new Predecessora(_a.Tarefa.Id, _b.Tarefa.Id, TipoDependencia.FS));
        (_b.Predecessoras, _a.Sucessoras).Should().Be(("1", "2"));
        _b.InicioMaisCedo.Should().Be(new DateTime(2026, 10, 1)); // depois de A, que termina na quarta
        _ambiente.Calculadora.Chamadas.Should().BeGreaterThan(chamadas);
        Gantt.Layout!.Setas.Should().ContainSingle().Which.Tipo.Should().Be(TipoDependencia.FS);
        _vm.PossuiAlteracoesNaoSalvas.Should().BeTrue();
        _vm.Mensagem.Should().Be("Vínculo criado: A → B (TI).");
    }

    [Theory]
    [InlineData(BordaDaBarra.Termino, BordaDaBarra.Inicio, "1", "TI")]
    [InlineData(BordaDaBarra.Inicio, BordaDaBarra.Inicio, "1II", "II")]
    [InlineData(BordaDaBarra.Termino, BordaDaBarra.Termino, "1TT", "TT")]
    [InlineData(BordaDaBarra.Inicio, BordaDaBarra.Termino, "1IT", "IT")]
    public void CadaCombinacaoDeBordas_CriaOTipoInferido(BordaDaBarra deOrigem, BordaDaBarra deDestino,
        string predecessoras, string sigla)
    {
        Soltar(_a, deOrigem, _b, deDestino).Should().BeTrue();

        _b.Predecessoras.Should().Be(predecessoras);
        _vm.Mensagem.Should().Be($"Vínculo criado: A → B ({sigla}).");
    }

    [Fact]
    public void VariosArrastesParaAMesmaTarefa_AcumulamPredecessoras()
    {
        Soltar(_a, BordaDaBarra.Termino, _c);
        Soltar(_b, BordaDaBarra.Inicio, _c);

        _c.Predecessoras.Should().Be("1; 2II");
    }

    [Fact]
    public void VariosArrastesDaMesmaTarefa_CriamVariasSucessoras()
    {
        Soltar(_a, BordaDaBarra.Termino, _b);
        Soltar(_a, BordaDaBarra.Termino, _c);

        _a.Sucessoras.Should().Be("2; 3");
        Gantt.Layout!.Setas.Should().HaveCount(2);
    }

    [Fact]
    public void MarcoComoOrigemOuDestino_Permitido()
    {
        var marco = Ambiente.AdicionarTarefa(_vm, "Concluído", 0);

        Soltar(_a, BordaDaBarra.Termino, marco).Should().BeTrue();
        Soltar(marco, BordaDaBarra.Termino, _b).Should().BeTrue();

        marco.InicioMaisCedo.Should().Be(new DateTime(2026, 9, 30)); // no fim do dia em que A termina
        _b.InicioMaisCedo.Should().Be(new DateTime(2026, 10, 1));
    }

    // --- Casos recusados ---

    [Fact]
    public void QueCriariaCiclo_RecusadoComMensagemESemMudarNada()
    {
        _b.Predecessoras = "1";
        Salvar();
        var chamadas = _ambiente.Calculadora.Chamadas;

        var criou = Soltar(_b, BordaDaBarra.Termino, _a);

        criou.Should().BeFalse();
        _vm.Mensagem.Should().Be("\"B\" não pode ser predecessora de \"A\": isso criaria a dependência circular B → A → B.");
        _vm.Cronograma.Predecessoras.Should().Equal(new Predecessora(_a.Tarefa.Id, _b.Tarefa.Id));
        _a.Predecessoras.Should().BeEmpty();
        _ambiente.Calculadora.Chamadas.Should().Be(chamadas);
        _vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();
    }

    [Fact]
    public void NaPropriaTarefa_CanceladoSemMensagem()
    {
        var chamadas = _ambiente.Calculadora.Chamadas;

        var criou = Soltar(_a, BordaDaBarra.Termino, _a);

        criou.Should().BeFalse();
        _vm.Cronograma.Predecessoras.Should().BeEmpty();
        _vm.Mensagem.Should().BeEmpty();
        _ambiente.Calculadora.Chamadas.Should().Be(chamadas);
    }

    [Fact]
    public void ForaDeQualquerBarra_CanceladoSemMensagem()
    {
        var criou = Soltar(_a, BordaDaBarra.Termino, destino: null);

        criou.Should().BeFalse();
        _vm.Cronograma.Predecessoras.Should().BeEmpty();
        _vm.Mensagem.Should().BeEmpty();
    }

    [Fact]
    public void VinculoQueJaExiste_RecusadoEOExistenteFicaComoEstava()
    {
        _b.Predecessoras = "1";

        var criou = Soltar(_a, BordaDaBarra.Inicio, _b); // seria II

        criou.Should().BeFalse();
        _vm.Mensagem.Should().Be(
            "Já existe um vínculo de \"A\" para \"B\" (TI). Para mudar o tipo, edite a coluna Predecessoras.");
        _vm.Cronograma.Predecessoras.Should().Equal(new Predecessora(_a.Tarefa.Id, _b.Tarefa.Id, TipoDependencia.FS));
    }

    [Fact]
    public void ComUmResumo_RecusadoNosDoisSentidos()
    {
        _vm.TarefaSelecionada = _c;
        _vm.AdicionarSubtarefaCommand.Execute(null); // C vira resumo de uma subtarefa nova
        var mensagemDoResumo = "\"C\" é um resumo, e resumos não têm predecessoras nem sucessoras. Vincule as subtarefas dele.";

        var paraOResumo = Soltar(_a, BordaDaBarra.Termino, _c);
        var mensagemParaOResumo = _vm.Mensagem;
        var doResumo = Soltar(_c, BordaDaBarra.Termino, _b);

        (paraOResumo, doResumo).Should().Be((false, false));
        mensagemParaOResumo.Should().Be(mensagemDoResumo);
        _vm.Mensagem.Should().Be(mensagemDoResumo);
        _vm.Cronograma.Predecessoras.Should().BeEmpty();
    }

    // --- Prévia durante o arraste ---

    [Fact]
    public void Previa_Valida_DizQueVinculoSeraCriadoSemCriarNada()
    {
        var chamadas = _ambiente.Calculadora.Chamadas;

        var previsao = Prever(_a, BordaDaBarra.Termino, _b);

        previsao.Should().Be(new PrevisaoDeLigacao(EstadoDaLigacao.Valida, "Término de \"A\" → Início de \"B\" (TI)"));
        _vm.Cronograma.Predecessoras.Should().BeEmpty();
        _ambiente.Calculadora.Chamadas.Should().Be(chamadas);
        _vm.Mensagem.Should().BeEmpty();
    }

    [Fact]
    public void Previa_NaAlcaDoTerminoDoDestino_MostraTT()
    {
        Prever(_a, BordaDaBarra.Termino, _b, BordaDaBarra.Termino)!.Texto
            .Should().Be("Término de \"A\" → Término de \"B\" (TT)");
    }

    [Fact]
    public void Previa_Recusada_TrazOMotivo()
    {
        _b.Predecessoras = "1";

        var previsao = Prever(_b, BordaDaBarra.Termino, _a);

        previsao!.Estado.Should().Be(EstadoDaLigacao.Invalida);
        previsao.Texto.Should().Contain("dependência circular B → A → B");
        _vm.Mensagem.Should().BeEmpty();
    }

    [Fact]
    public void Previa_NaPropriaTarefa_Neutra()
    {
        Prever(_a, BordaDaBarra.Termino, _a).Should().Be(new PrevisaoDeLigacao(EstadoDaLigacao.Neutra, "Solte sobre outra tarefa"));
    }

    [Fact]
    public void Previa_ForaDeQualquerBarra_Nenhuma()
    {
        Prever(_a, BordaDaBarra.Termino, destino: null).Should().BeNull();
    }
}
