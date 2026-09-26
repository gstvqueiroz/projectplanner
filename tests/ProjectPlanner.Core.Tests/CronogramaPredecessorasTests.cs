using static ProjectPlanner.Core.Tests.Datas;

namespace ProjectPlanner.Core.Tests;

public class CronogramaPredecessorasTests
{
    private readonly Cronograma _cronograma = new("Projeto", Segunda);
    private readonly Tarefa _a, _b, _c, _d, _e;

    public CronogramaPredecessorasTests()
    {
        _a = NovaTarefa("A");
        _b = NovaTarefa("B");
        _c = NovaTarefa("C");
        _d = NovaTarefa("D");
        _e = NovaTarefa("E");
    }

    private Tarefa NovaTarefa(string nome)
    {
        var tarefa = new Tarefa(nome, Segunda, 1);
        _cronograma.AdicionarTarefa(tarefa);
        return tarefa;
    }

    private void Vincular(Tarefa origem, Tarefa destino,
        TipoDependencia tipo = TipoDependencia.FS, int lag = 0) =>
        _cronograma.AdicionarPredecessora(new Predecessora(origem.Id, destino.Id, tipo, lag));

    // Adicionar e listar

    [Fact]
    public void AdicionarPredecessora_FS_ApareceNaListaDaSucessora()
    {
        Vincular(_a, _b);

        _cronograma.PredecessorasDe(_b.Id).Should().Equal(new Predecessora(_a.Id, _b.Id, TipoDependencia.FS, 0));
        _cronograma.Predecessoras.Should().ContainSingle();
    }

    [Theory]
    [InlineData(TipoDependencia.SS, 2)]
    [InlineData(TipoDependencia.FF, -1)]
    [InlineData(TipoDependencia.SF, 0)]
    [InlineData(TipoDependencia.FS, -3)]
    public void AdicionarPredecessora_DeCadaTipoComLag_PreservaTipoELag(TipoDependencia tipo, int lag)
    {
        Vincular(_a, _b, tipo, lag);

        var predecessora = _cronograma.PredecessorasDe(_b.Id).Should().ContainSingle().Which;
        predecessora.Tipo.Should().Be(tipo);
        predecessora.Lag.Should().Be(lag);
    }

    [Fact]
    public void PredecessorasDe_TarefaSemPredecessoras_RetornaListaVazia()
    {
        Vincular(_a, _b);

        _cronograma.PredecessorasDe(_a.Id).Should().BeEmpty();
        _cronograma.PredecessorasDe(_e.Id).Should().BeEmpty();
    }

    [Fact]
    public void PredecessorasDe_ComVariasDependencias_RetornaSoAsDaTarefaNaOrdemDeInclusao()
    {
        Vincular(_a, _c);
        Vincular(_a, _d);
        Vincular(_b, _c, TipoDependencia.SS, 2);
        Vincular(_d, _c, TipoDependencia.FF, -1);

        _cronograma.PredecessorasDe(_c.Id).Should().Equal(
            new Predecessora(_a.Id, _c.Id),
            new Predecessora(_b.Id, _c.Id, TipoDependencia.SS, 2),
            new Predecessora(_d.Id, _c.Id, TipoDependencia.FF, -1));
    }

    [Fact]
    public void PredecessorasDe_TarefaForaDoCronograma_LancaExcecao()
    {
        var listar = () => _cronograma.PredecessorasDe(Guid.NewGuid());

        listar.Should().Throw<DomainValidationException>()
            .Which.Propriedade.Should().Be(nameof(Cronograma.Tarefas));
    }

    // Validações ao adicionar

    [Fact]
    public void AdicionarPredecessora_DaTarefaParaElaMesma_LancaExcecao()
    {
        var adicionar = () => Vincular(_a, _a);

        adicionar.Should().Throw<DomainValidationException>();
        _cronograma.Predecessoras.Should().BeEmpty();
    }

    [Fact]
    public void AdicionarPredecessora_ComOrigemForaDoCronograma_LancaExcecao()
    {
        var avulsa = new Tarefa("Avulsa", Segunda, 1);

        var adicionar = () => Vincular(avulsa, _a);

        adicionar.Should().Throw<DomainValidationException>()
            .Which.Propriedade.Should().Be(nameof(Predecessora.TarefaOrigemId));
        _cronograma.Predecessoras.Should().BeEmpty();
    }

    [Fact]
    public void AdicionarPredecessora_ComDestinoForaDoCronograma_LancaExcecao()
    {
        var avulsa = new Tarefa("Avulsa", Segunda, 1);

        var adicionar = () => Vincular(_a, avulsa);

        adicionar.Should().Throw<DomainValidationException>()
            .Which.Propriedade.Should().Be(nameof(Predecessora.TarefaDestinoId));
        _cronograma.Predecessoras.Should().BeEmpty();
    }

    [Fact]
    public void AdicionarPredecessora_ParJaVinculado_LancaExcecaoEMantemOVinculoOriginal()
    {
        Vincular(_a, _b);

        var adicionar = () => Vincular(_a, _b, TipoDependencia.SS, 3);

        adicionar.Should().Throw<DomainValidationException>()
            .Which.Propriedade.Should().Be(nameof(Cronograma.Predecessoras));
        _cronograma.Predecessoras.Should().Equal(new Predecessora(_a.Id, _b.Id));
    }

    // Validar sem adicionar (usado na prévia do arraste de vínculos, Bloco 12)

    [Fact]
    public void ValidarPredecessora_Valida_NaoAdicionaNada()
    {
        _cronograma.ValidarPredecessora(new Predecessora(_a.Id, _b.Id));

        _cronograma.Predecessoras.Should().BeEmpty();
    }

    [Fact]
    public void ValidarPredecessora_RecusaComAsMesmasMensagensDeAdicionar_SemMudarNada()
    {
        Vincular(_a, _b);
        var fase = new Tarefa("Fase", Segunda, 1);
        _cronograma.AdicionarTarefa(fase);
        _cronograma.AdicionarTarefa(new Tarefa("Sub", Segunda, 1), fase.Id);

        var repetido = () => _cronograma.ValidarPredecessora(new Predecessora(_a.Id, _b.Id, TipoDependencia.SS));
        var ciclo = () => _cronograma.ValidarPredecessora(new Predecessora(_b.Id, _a.Id));
        var resumo = () => _cronograma.ValidarPredecessora(new Predecessora(_c.Id, fase.Id));

        repetido.Should().Throw<DomainValidationException>().WithMessage("Já existe um vínculo de \"A\" para \"B\".");
        ciclo.Should().Throw<CircularDependencyException>().WithMessage("*dependência circular B → A → B.");
        resumo.Should().Throw<DomainValidationException>().WithMessage("\"Fase\" é um resumo*");
        _cronograma.Predecessoras.Should().Equal(new Predecessora(_a.Id, _b.Id));
    }

    [Fact]
    public void AdicionarPredecessora_Nula_LancaArgumentNullException()
    {
        var adicionar = () => _cronograma.AdicionarPredecessora(null!);

        adicionar.Should().Throw<ArgumentNullException>();
    }

    // Ciclos

    [Fact]
    public void AdicionarPredecessora_QueFechaCicloSimples_LancaCircularDependencyException()
    {
        Vincular(_a, _b);

        var adicionar = () => Vincular(_b, _a);

        var excecao = adicionar.Should().Throw<CircularDependencyException>().Which;
        excecao.Ciclo.Should().Equal(_b.Id, _a.Id, _b.Id);
        excecao.Should().BeAssignableTo<DomainValidationException>()
            .Which.Propriedade.Should().Be(nameof(Cronograma.Predecessoras));
        _cronograma.Predecessoras.Should().Equal(new Predecessora(_a.Id, _b.Id));
    }

    [Fact]
    public void AdicionarPredecessora_QueFechaCicloIndireto_LancaExcecaoComOCaminhoDoCiclo()
    {
        Vincular(_a, _b);
        Vincular(_b, _c);

        var adicionar = () => Vincular(_c, _a);

        adicionar.Should().Throw<CircularDependencyException>()
            .WithMessage("*C → A → B → C*")
            .Which.Ciclo.Should().Equal(_c.Id, _a.Id, _b.Id, _c.Id);
        _cronograma.Predecessoras.Should().HaveCount(2);
    }

    [Fact]
    public void AdicionarPredecessora_QueFechaCicloLongoComRamificacoes_LancaExcecao()
    {
        Vincular(_a, _b);
        Vincular(_b, _c);
        Vincular(_c, _d);
        Vincular(_d, _e);
        Vincular(_a, _e);

        var adicionar = () => Vincular(_e, _b, TipoDependencia.SS);

        adicionar.Should().Throw<CircularDependencyException>()
            .Which.Ciclo.Should().Equal(_e.Id, _b.Id, _c.Id, _d.Id, _e.Id);
    }

    [Fact]
    public void AdicionarPredecessora_CaminhosQueConvergem_NaoSaoCiclo()
    {
        // Losango: A → B → D e A → C → D, mais o atalho A → D.
        Vincular(_a, _b);
        Vincular(_a, _c);
        Vincular(_b, _d);
        Vincular(_c, _d);

        var adicionar = () => Vincular(_a, _d);

        adicionar.Should().NotThrow();
        _cronograma.PredecessorasDe(_d.Id).Should().HaveCount(3);
    }

    // Remoção

    [Fact]
    public void RemoverPredecessora_Existente_RemoveSoAquelaVinculacao()
    {
        Vincular(_a, _c);
        Vincular(_b, _c);

        _cronograma.RemoverPredecessora(_a.Id, _c.Id);

        _cronograma.PredecessorasDe(_c.Id).Should().Equal(new Predecessora(_b.Id, _c.Id));
    }

    [Fact]
    public void RemoverPredecessora_Inexistente_LancaExcecao()
    {
        Vincular(_a, _b);

        var remover = () => _cronograma.RemoverPredecessora(_b.Id, _a.Id);

        remover.Should().Throw<DomainValidationException>()
            .Which.Propriedade.Should().Be(nameof(Cronograma.Predecessoras));
        _cronograma.Predecessoras.Should().ContainSingle();
    }

    [Fact]
    public void RemoverPredecessora_PermiteReadicionarComOutroTipo()
    {
        Vincular(_a, _b);
        _cronograma.RemoverPredecessora(_a.Id, _b.Id);

        Vincular(_a, _b, TipoDependencia.SS, 1);

        _cronograma.PredecessorasDe(_b.Id).Should().Equal(new Predecessora(_a.Id, _b.Id, TipoDependencia.SS, 1));
    }

    [Fact]
    public void RemoverPredecessora_DesfazOCiclo_PermiteVinculoInverso()
    {
        Vincular(_a, _b);
        _cronograma.RemoverPredecessora(_a.Id, _b.Id);

        var adicionar = () => Vincular(_b, _a);

        adicionar.Should().NotThrow();
    }
}
