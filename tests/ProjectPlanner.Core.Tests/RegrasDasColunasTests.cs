using ProjectPlanner.Core;
using ProjectPlanner.Core.Kanban;

namespace ProjectPlanner.Core.Tests;

// Bloco 76: gerenciar as colunas do quadro (o plano "Delete, arrastar e Kanban", K2). Tarefas: Escavação 0%, Sapatas 40% (em
// Impedida), Alvenaria 100%.
public class RegrasDasColunasTests
{
    private readonly Cronograma _c = new("Obra", Datas.Segunda);
    private readonly Tarefa _escavacao = new("Escavação", Datas.Segunda, 3);
    private readonly Tarefa _sapatas = new("Sapatas", Datas.Segunda, 4);
    private readonly Tarefa _alvenaria = new("Alvenaria", Datas.Segunda, 5);

    public RegrasDasColunasTests()
    {
        _c.AdicionarTarefa(_escavacao);
        _c.AdicionarTarefa(_sapatas);
        _c.AdicionarTarefa(_alvenaria);
        _sapatas.AtualizarPercentualConcluido(40);
        _alvenaria.AtualizarPercentualConcluido(100);
        _c.Quadro.Sincronizar();
        RegrasDoQuadro.Aplicar(_c, RegrasDoQuadro.Planejar(_c, [_sapatas.Id], Id("Impedida"), 0));
    }

    private Guid Id(string coluna) => _c.Quadro.Colunas.Single(c => c.Nome == coluna).Id;

    private string Quadro()
    {
        var porColuna = _c.Quadro.CardsPorColuna();
        var nomes = _c.Tarefas.ToDictionary(t => t.Id, t => t.Nome);
        return string.Join(" | ", _c.Quadro.Colunas.Select(c => $"{c.Nome}: {string.Join(", ", porColuna[c.Id].Select(k => nomes[k.TarefaId]))}"));
    }

    [Fact]
    public void Adicionar_EntraAntesDeConcluida_EmAndamento_ComUmaCorAindaSemUso()
    {
        var revisao = RegrasDasColunas.Adicionar(_c, "  Em revisão ");

        (revisao.Nome, revisao.Tipo, revisao.Cor).Should().Be(("Em revisão", TipoDeColuna.EmAndamento, "#F5A524"),
            "cinza, azul, verde e vermelho já são das colunas padrão");
        _c.Quadro.Colunas.Select(c => c.Nome).Should().Equal("A fazer", "Em andamento", "Impedida", "Em revisão", "Concluída");
        FluentActions.Invoking(() => RegrasDasColunas.Adicionar(_c, "em revisão")).Should().Throw<DomainValidationException>()
            .WithMessage("Já existe a coluna \"Em revisão\" no quadro.");
        FluentActions.Invoking(() => RegrasDasColunas.Adicionar(_c, " ")).Should().Throw<DomainValidationException>()
            .WithMessage("Toda coluna do quadro precisa de um nome.");
        _c.Quadro.Colunas.Should().HaveCount(5, "recusada, nada muda");
    }

    [Fact]
    public void RenomearColorirLimiteEMover()
    {
        RegrasDasColunas.Renomear(_c, Id("Impedida"), "Parada");
        RegrasDasColunas.Colorir(_c, Id("Parada"), "#8E4EC6");
        RegrasDasColunas.DefinirLimite(_c, Id("Em andamento"), 3);
        RegrasDasColunas.Mover(_c, Id("Parada"), -1).Should().BeTrue();
        RegrasDasColunas.Mover(_c, Id("A fazer"), -1).Should().BeFalse("já é a primeira");

        _c.Quadro.Colunas.Select(c => (c.Nome, c.Cor, c.Limite)).Should().Equal(
            ("A fazer", "#8A8F98", null), ("Parada", "#8E4EC6", null), ("Em andamento", "#2F80ED", 3), ("Concluída", "#30A46C", null));
        Quadro().Should().Be("A fazer: Escavação | Parada: Sapatas | Em andamento:  | Concluída: Alvenaria", "os cards vão com a coluna");
        FluentActions.Invoking(() => RegrasDasColunas.DefinirLimite(_c, Id("Parada"), 0)).Should().Throw<DomainValidationException>();
        FluentActions.Invoking(() => RegrasDasColunas.Colorir(_c, Id("Parada"), "roxo")).Should().Throw<DomainValidationException>();
        RegrasDasColunas.DefinirLimite(_c, Id("Em andamento"), null);
        _c.Quadro.Coluna(Id("Em andamento"))!.Limite.Should().BeNull();
    }

    [Fact]
    public void MudarTipo_SoSeOsCardsCombinam_ESemMudarONenhumPercentual()
    {
        FluentActions.Invoking(() => RegrasDasColunas.MudarTipo(_c, Id("Impedida"), TipoDeColuna.Concluida))
            .Should().Throw<DomainValidationException>()
            .WithMessage("A coluna \"Impedida\" tem cards que não combinam com o tipo Concluída (\"Sapatas\" está com 40%). Mova esses cards antes de mudar o tipo.");
        FluentActions.Invoking(() => RegrasDasColunas.MudarTipo(_c, Id("Concluída"), TipoDeColuna.EmAndamento))
            .Should().Throw<DomainValidationException>().WithMessage("*tem cards que não combinam*");

        RegrasDasColunas.MudarTipo(_c, Id("Em andamento"), TipoDeColuna.Concluida); // vazia
        _c.Quadro.Coluna(Id("Em andamento"))!.Tipo.Should().Be(TipoDeColuna.Concluida);
        _sapatas.PercentualConcluido.Should().Be(40);

        // Os cards de "A fazer" (0%) combinam com Em andamento, mas ela é a única A fazer.
        FluentActions.Invoking(() => RegrasDasColunas.MudarTipo(_c, Id("A fazer"), TipoDeColuna.EmAndamento))
            .Should().Throw<DomainValidationException>().WithMessage("*pelo menos uma coluna do tipo A fazer*");
    }

    [Fact]
    public void Excluir_ComCards_PedeODestino_EAplicaARegraDoPercentualDeLa()
    {
        var plano = RegrasDasColunas.PlanejarExclusao(_c, Id("Impedida"));
        (plano.Cards, plano.Motivo).Should().Be((1, null));
        plano.Destinos.Select(c => c.Nome).Should().Equal(["Em andamento", "A fazer", "Concluída"], "primeiro as do mesmo tipo");
        FluentActions.Invoking(() => RegrasDasColunas.Excluir(_c, Id("Impedida"), null)).Should().Throw<DomainValidationException>()
            .WithMessage("Escolha para onde vai o card da coluna \"Impedida\".");

        var resultado = RegrasDasColunas.Excluir(_c, Id("Impedida"), Id("Concluída"));

        resultado.Should().Be(new ResultadoDaColuna("Coluna \"Impedida\" excluída. \"Sapatas\" foi para \"Concluída\": 100% concluída.", true));
        _sapatas.PercentualConcluido.Should().Be(100);
        Quadro().Should().Be("A fazer: Escavação | Em andamento:  | Concluída: Alvenaria, Sapatas");
    }

    [Fact]
    public void Excluir_Vazia_OuAUltimaDoTipo()
    {
        RegrasDasColunas.Excluir(_c, Id("Em andamento"), null).Should().Be(new ResultadoDaColuna("Coluna \"Em andamento\" excluída.", false));
        _c.Quadro.Colunas.Select(c => c.Nome).Should().Equal("A fazer", "Impedida", "Concluída");

        var plano = RegrasDasColunas.PlanejarExclusao(_c, Id("Concluída"));
        plano.Motivo.Should().Be("\"Concluída\" é a única coluna do tipo Concluída, e o quadro precisa de uma: é para onde vão as tarefas com 100%.");
        FluentActions.Invoking(() => RegrasDasColunas.Excluir(_c, Id("Concluída"), Id("A fazer")))
            .Should().Throw<DomainValidationException>().WithMessage(plano.Motivo);
        _c.Quadro.Colunas.Should().HaveCount(3);
    }

    [Fact]
    public void Excluir_LevaJuntoOsCardsQueNaoAparecem()
    {
        var armacao = new Tarefa("Armação", Datas.Segunda, 2);
        _c.AdicionarTarefa(armacao, _sapatas.Id); // Sapatas vira resumo: o card dela fica guardado em Impedida
        _c.Quadro.Sincronizar();
        _c.Quadro.CardDe(_sapatas.Id)!.ColunaId.Should().Be(Id("Impedida"));

        RegrasDasColunas.Excluir(_c, Id("Impedida"), null);

        _c.Quadro.CardDe(_sapatas.Id)!.ColunaId.Should().Be(Id("Em andamento"), "foi para a primeira do mesmo tipo");
    }
}
