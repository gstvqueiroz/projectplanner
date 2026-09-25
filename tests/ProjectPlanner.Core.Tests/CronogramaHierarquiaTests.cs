using static ProjectPlanner.Core.Tests.Datas;

namespace ProjectPlanner.Core.Tests;

public class CronogramaHierarquiaTests
{
    private readonly Cronograma _cronograma = new("Obra", Segunda);

    private Tarefa Nova(string nome, Tarefa? pai = null, int duracao = 1)
    {
        var tarefa = new Tarefa(nome, Segunda, duracao);
        _cronograma.AdicionarTarefa(tarefa, pai?.Id);
        return tarefa;
    }

    private void Ligar(Tarefa de, Tarefa para) => _cronograma.AdicionarPredecessora(new Predecessora(de.Id, para.Id));

    // A lista do cronograma com dois espaços de recuo por nível, para comparar estruturas de relance.
    private List<string> Estrutura() =>
        _cronograma.Tarefas.Select(t => new string(' ', 2 * _cronograma.NivelDe(t.Id)) + t.Nome).ToList();

    [Fact]
    public void DescendentesDe_TodasAsSubtarefasEmQualquerNivel()
    {
        var obra = Nova("Obra");
        var fase = Nova("Fase", obra);
        var a = Nova("A", fase);
        var b = Nova("B", obra);
        Nova("X");

        _cronograma.DescendentesDe(obra.Id).Should().Equal(fase, a, b);
        _cronograma.DescendentesDe(a.Id).Should().BeEmpty();
    }

    // --- Adicionar subtarefa ---

    [Fact]
    public void AdicionarSubtarefa_EntraDepoisDasOutrasSubtarefasEOPaiViraResumo()
    {
        var fase1 = Nova("Fase 1");
        var fase2 = Nova("Fase 2");
        var a = Nova("A", fase1);
        var b = Nova("B", fase1);

        Estrutura().Should().Equal("Fase 1", "  A", "  B", "Fase 2");
        fase1.Tipo.Should().Be(TipoTarefa.Resumo);
        fase2.Tipo.Should().Be(TipoTarefa.Normal);
        b.TarefaPaiId.Should().Be(fase1.Id);
        _cronograma.FilhasDe(fase1.Id).Should().Equal(a, b);
    }

    [Fact]
    public void VariosNiveis_CadaSubtarefaEntraNoFimDoProprioRamo()
    {
        var obra = Nova("Obra");
        var fase1 = Nova("Fase 1", obra);
        var fase2 = Nova("Fase 2", obra);
        Nova("A", fase1);
        Nova("C", fase2);
        Nova("B", fase1);
        Nova("Entrega");

        Estrutura().Should().Equal("Obra", "  Fase 1", "    A", "    B", "  Fase 2", "    C", "Entrega");
        _cronograma.NivelDe(obra.Id).Should().Be(0);
        _cronograma.NivelDe(fase1.Id).Should().Be(1);
        _cronograma.FilhasDe(obra.Id).Should().Equal(fase1, fase2);
    }

    [Fact]
    public void AdicionarSubtarefaEmMarco_Recusada()
    {
        var marco = Nova("Marco", duracao: 0);

        var adicionar = () => _cronograma.AdicionarTarefa(new Tarefa("A", Segunda, 1), marco.Id);

        adicionar.Should().Throw<DomainValidationException>()
            .WithMessage("\"Marco\" é um marco, e marcos não têm subtarefas.");
        Estrutura().Should().Equal("Marco");
        marco.Tipo.Should().Be(TipoTarefa.Marco);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AdicionarSubtarefaEmTarefaComVinculos_Recusada(bool comoPredecessora)
    {
        var x = Nova("X");
        var y = Nova("Y");
        if (comoPredecessora)
            Ligar(x, y);
        else
            Ligar(y, x);

        var adicionar = () => _cronograma.AdicionarTarefa(new Tarefa("Z", Segunda, 1), x.Id);

        adicionar.Should().Throw<DomainValidationException>()
            .WithMessage("\"X\" tem vínculos (predecessoras ou sucessoras) e não pode virar resumo*Remova os vínculos dela primeiro.");
        Estrutura().Should().Equal("X", "Y");
        x.EhResumo.Should().BeFalse();
    }

    [Fact]
    public void VinculosNasSubtarefas_NaoImpedemOResumoDeGanharOutras()
    {
        var fase = Nova("Fase");
        var a = Nova("A", fase);
        var b = Nova("B", fase);
        Ligar(a, b);

        Nova("C", fase);

        Estrutura().Should().Equal("Fase", "  A", "  B", "  C");
    }

    [Fact]
    public void AdicionarComPaiInexistenteOuElaMesmaComoPai_Recusada()
    {
        var tarefa = new Tarefa("A", Segunda, 1);

        var paiInexistente = () => _cronograma.AdicionarTarefa(tarefa, Guid.NewGuid());
        var elaMesma = () => _cronograma.AdicionarTarefa(tarefa, tarefa.Id);

        paiInexistente.Should().Throw<DomainValidationException>().Which.Propriedade.Should().Be(nameof(Tarefa.TarefaPaiId));
        elaMesma.Should().Throw<DomainValidationException>().WithMessage("Uma tarefa não pode ser subtarefa de si mesma.");
        _cronograma.Tarefas.Should().BeEmpty();
    }

    // --- Rebaixar ---

    [Fact]
    public void Rebaixar_ViraAUltimaSubtarefaDaTarefaDeCimaNoMesmoNivel()
    {
        var a = Nova("A");
        var b = Nova("B");
        var c = Nova("C");

        _cronograma.Rebaixar(b.Id);
        _cronograma.Rebaixar(c.Id);

        Estrutura().Should().Equal("A", "  B", "  C");
        a.Tipo.Should().Be(TipoTarefa.Resumo);
        c.TarefaPaiId.Should().Be(a.Id);
    }

    [Fact]
    public void Rebaixar_LevaAsSubtarefasJuntoSemMudarAOrdem()
    {
        Nova("X");
        var y = Nova("Y");
        Nova("Y1", y);

        _cronograma.Rebaixar(y.Id);

        Estrutura().Should().Equal("X", "  Y", "    Y1");
    }

    [Fact]
    public void Rebaixar_ComUmResumoAcima_ViraAUltimaSubtarefaDele()
    {
        var fase = Nova("Fase");
        Nova("A", fase);
        var x = Nova("X");

        _cronograma.Rebaixar(x.Id);

        Estrutura().Should().Equal("Fase", "  A", "  X");
    }

    [Fact]
    public void Rebaixar_APrimeiraDoNivel_Recusada()
    {
        var fase = Nova("Fase");
        var a = Nova("A", fase);

        var primeiraDoResumo = () => _cronograma.Rebaixar(a.Id);
        var primeiraDoProjeto = () => _cronograma.Rebaixar(fase.Id);

        primeiraDoResumo.Should().Throw<DomainValidationException>()
            .WithMessage("Não há tarefa acima de \"A\" no mesmo nível para ser o resumo dela.");
        primeiraDoProjeto.Should().Throw<DomainValidationException>();
        Estrutura().Should().Equal("Fase", "  A");
    }

    [Fact]
    public void Rebaixar_AbaixoDeUmMarco_Recusada()
    {
        Nova("Marco", duracao: 0);
        var a = Nova("A");

        var rebaixar = () => _cronograma.Rebaixar(a.Id);

        rebaixar.Should().Throw<DomainValidationException>().WithMessage("*marcos não têm subtarefas.");
        Estrutura().Should().Equal("Marco", "A");
    }

    [Fact]
    public void Rebaixar_AbaixoDeTarefaComVinculos_Recusada()
    {
        var x = Nova("X");
        var y = Nova("Y");
        var z = Nova("Z");
        Ligar(x, y);

        var rebaixar = () => _cronograma.Rebaixar(z.Id);

        rebaixar.Should().Throw<DomainValidationException>().WithMessage("\"Y\" tem vínculos*");
        Estrutura().Should().Equal("X", "Y", "Z");
    }

    // --- Promover ---

    [Fact]
    public void Promover_SaiDoResumoEFicaLogoDepoisDele_AsDeBaixoContinuamNoResumo()
    {
        var fase1 = Nova("Fase 1");
        Nova("A", fase1);
        var b = Nova("B", fase1);
        Nova("C", fase1);
        Nova("Fase 2");

        _cronograma.Promover(b.Id);

        Estrutura().Should().Equal("Fase 1", "  A", "  C", "B", "Fase 2");
        b.TarefaPaiId.Should().BeNull();
        fase1.Tipo.Should().Be(TipoTarefa.Resumo);
    }

    [Fact]
    public void Promover_LevaAsSubtarefasJunto()
    {
        var fase = Nova("Fase");
        var a = Nova("A", fase);
        Nova("A1", a);
        Nova("A2", a);
        Nova("B", fase);

        _cronograma.Promover(a.Id);

        Estrutura().Should().Equal("Fase", "  B", "A", "  A1", "  A2");
    }

    [Fact]
    public void Promover_AUltimaSubtarefa_OResumoVoltaASerTarefaComum()
    {
        var fase = Nova("Fase");
        var a = Nova("A", fase);

        _cronograma.Promover(a.Id);

        Estrutura().Should().Equal("Fase", "A");
        fase.Tipo.Should().Be(TipoTarefa.Normal);
    }

    [Fact]
    public void Promover_EmVariosNiveis_SobeUmNivelSo()
    {
        var obra = Nova("Obra");
        var fase1 = Nova("Fase 1", obra);
        var a = Nova("A", fase1);
        Nova("B", fase1);
        Nova("Fase 2", obra);

        _cronograma.Promover(a.Id);

        Estrutura().Should().Equal("Obra", "  Fase 1", "    B", "  A", "  Fase 2");
        a.TarefaPaiId.Should().Be(obra.Id);
    }

    [Fact]
    public void Promover_NoPrimeiroNivel_Recusada()
    {
        var a = Nova("A");

        var promover = () => _cronograma.Promover(a.Id);

        promover.Should().Throw<DomainValidationException>().WithMessage("\"A\" já está no primeiro nível.");
    }

    // --- Definir pai (movimento geral) ---

    [Fact]
    public void DefinirPai_ElaMesma_Recusada()
    {
        var a = Nova("A");

        var definir = () => _cronograma.DefinirPai(a.Id, a.Id);

        definir.Should().Throw<DomainValidationException>().WithMessage("Uma tarefa não pode ser subtarefa de si mesma.");
        a.TarefaPaiId.Should().BeNull();
    }

    [Fact]
    public void DefinirPai_DentroDeUmaSubtarefaDelaMesma_RecusadaPorCiclo()
    {
        var a = Nova("A");
        var b = Nova("B", a);
        var c = Nova("C", b);

        var filha = () => _cronograma.DefinirPai(a.Id, b.Id);
        var neta = () => _cronograma.DefinirPai(a.Id, c.Id);

        filha.Should().Throw<DomainValidationException>()
            .WithMessage("\"B\" é subtarefa de \"A\"; colocar \"A\" dentro dela criaria um ciclo na estrutura.");
        neta.Should().Throw<DomainValidationException>().WithMessage("*ciclo*");
        Estrutura().Should().Equal("A", "  B", "    C");
    }

    [Fact]
    public void DefinirPai_ParaUmMarco_Recusada()
    {
        var marco = Nova("Marco", duracao: 0);
        var a = Nova("A");

        var definir = () => _cronograma.DefinirPai(a.Id, marco.Id);

        definir.Should().Throw<DomainValidationException>().WithMessage("*marcos não têm subtarefas.");
    }

    [Fact]
    public void DefinirPai_MoveComAsSubtarefasParaOFimDoNovoResumo()
    {
        var fase1 = Nova("Fase 1");
        var a = Nova("A", fase1);
        Nova("A1", a);
        var fase2 = Nova("Fase 2");
        Nova("C", fase2);

        _cronograma.DefinirPai(a.Id, fase2.Id);

        Estrutura().Should().Equal("Fase 1", "Fase 2", "  C", "  A", "    A1");
        fase1.Tipo.Should().Be(TipoTarefa.Normal);
    }

    [Fact]
    public void DefinirPaiNulo_VaiParaOFimNoPrimeiroNivel()
    {
        var fase = Nova("Fase");
        var a = Nova("A", fase);
        Nova("B", fase);
        Nova("X");

        _cronograma.DefinirPai(a.Id, null);

        Estrutura().Should().Equal("Fase", "  B", "X", "A");
    }

    // --- Remover ---

    [Fact]
    public void RemoverResumo_RemoveAsSubtarefasEOsVinculosDelas()
    {
        var fase = Nova("Fase");
        var a = Nova("A", fase);
        var sub = Nova("Sub", fase);
        var b = Nova("B", sub);
        var x = Nova("X");
        Ligar(a, x);
        Ligar(x, b);

        _cronograma.RemoverTarefa(fase.Id);

        Estrutura().Should().Equal("X");
        _cronograma.Predecessoras.Should().BeEmpty();
    }

    [Fact]
    public void RemoverAUltimaSubtarefa_OResumoVoltaASerTarefaComum()
    {
        var fase = Nova("Fase");
        var a = Nova("A", fase);

        _cronograma.RemoverTarefa(a.Id);

        fase.Tipo.Should().Be(TipoTarefa.Normal);
        _cronograma.FilhasDe(fase.Id).Should().BeEmpty();
    }

    // --- Vínculos com resumos ---

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void VinculoComResumo_Recusado(bool resumoComoPredecessora)
    {
        var fase = Nova("Fase");
        Nova("A", fase);
        var x = Nova("X");

        var ligar = () => _cronograma.AdicionarPredecessora(resumoComoPredecessora
            ? new Predecessora(fase.Id, x.Id)
            : new Predecessora(x.Id, fase.Id));

        ligar.Should().Throw<DomainValidationException>()
            .WithMessage("\"Fase\" é um resumo, e resumos não têm predecessoras nem sucessoras. Vincule as subtarefas dele.");
        _cronograma.Predecessoras.Should().BeEmpty();
    }

    [Fact]
    public void Resumo_NaoAceitaEdicaoDeDuracaoDatasNemPercentual()
    {
        var fase = Nova("Fase");
        Nova("A", fase);

        var acoes = new Action[]
        {
            () => fase.AlterarDuracao(3),
            () => fase.AlterarDataInicio(Terca),
            () => fase.AlterarDataTermino(Sexta),
            () => fase.AtualizarPercentualConcluido(50),
        };

        foreach (var acao in acoes)
            acao.Should().Throw<DomainValidationException>().WithMessage("*de um resumo é calculad* a partir das subtarefas.");
        fase.Renomear("Fase 1");
        fase.Nome.Should().Be("Fase 1");
    }
}
