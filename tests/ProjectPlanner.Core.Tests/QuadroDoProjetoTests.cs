using ProjectPlanner.Core;
using ProjectPlanner.Core.Kanban;

namespace ProjectPlanner.Core.Tests;

// Bloco 75: o quadro Kanban (o plano "Delete, arrastar e Kanban", K1 a K7), com cada linha da tabela K3 num teste. Estrutura:
// Fundação (Escavação 0%, Sapatas 40%), Alvenaria 100% e o marco Entrega 0%.
public class QuadroDoProjetoTests
{
    private readonly Cronograma _c = new("Obra", Datas.Segunda);
    private readonly Tarefa _fundacao = new("Fundação", Datas.Segunda, 1);
    private readonly Tarefa _escavacao = new("Escavação", Datas.Segunda, 3);
    private readonly Tarefa _sapatas = new("Sapatas", Datas.Segunda, 4);
    private readonly Tarefa _alvenaria = new("Alvenaria", Datas.Segunda, 5);
    private readonly Tarefa _entrega = new("Entrega", Datas.Segunda, 0);

    public QuadroDoProjetoTests()
    {
        _c.AdicionarTarefa(_fundacao);
        _c.AdicionarTarefa(_escavacao, _fundacao.Id);
        _c.AdicionarTarefa(_sapatas, _fundacao.Id);
        _c.AdicionarTarefa(_alvenaria);
        _c.AdicionarTarefa(_entrega);
        _sapatas.AtualizarPercentualConcluido(40);
        _alvenaria.AtualizarPercentualConcluido(100);
        _c.Quadro.Sincronizar();
    }

    private QuadroDoProjeto Quadro => _c.Quadro;

    private ColunaDoQuadro Coluna(string nome) => Quadro.Colunas.Single(c => c.Nome == nome);

    // "A fazer: Escavação, Entrega | Em andamento: Sapatas | Impedida: | Concluída: Alvenaria"
    private string Retrato()
    {
        var porColuna = Quadro.CardsPorColuna();
        var nomes = _c.Tarefas.ToDictionary(t => t.Id, t => t.Nome);
        return string.Join(" | ", Quadro.Colunas.Select(c => $"{c.Nome}: {string.Join(", ", porColuna[c.Id].Select(k => nomes[k.TarefaId]))}"));
    }

    private ResultadoDoQuadro Mover(Tarefa tarefa, string coluna, int lacuna = int.MaxValue) =>
        RegrasDoQuadro.Aplicar(_c, RegrasDoQuadro.Planejar(_c, [tarefa.Id], Coluna(coluna).Id, lacuna));

    [Fact]
    public void ArquivoSemQuadro_AbreComAsColunasPadrao_DistribuindoPeloPercentual()
    {
        Quadro.Colunas.Select(c => (c.Nome, c.Tipo)).Should().Equal(
            ("A fazer", TipoDeColuna.AFazer), ("Em andamento", TipoDeColuna.EmAndamento),
            ("Impedida", TipoDeColuna.EmAndamento), ("Concluída", TipoDeColuna.Concluida));

        Retrato().Should().Be("A fazer: Escavação, Entrega | Em andamento: Sapatas | Impedida:  | Concluída: Alvenaria",
            "0% em A fazer, 1–99% em Em andamento, 100% em Concluída (K7); o resumo Fundação não vira card (K1)");
    }

    [Fact]
    public void K3_CardEntraEmConcluida_OPercentualVaiA100()
    {
        var plano = RegrasDoQuadro.Planejar(_c, [_sapatas.Id], Coluna("Concluída").Id, 0);
        plano.Dica.Should().Be("Mover para \"Concluída\" · 100%");

        RegrasDoQuadro.Aplicar(_c, plano).Texto.Should().Be("\"Sapatas\" foi para \"Concluída\": 100% concluída.");

        _sapatas.PercentualConcluido.Should().Be(100);
        Retrato().Should().Be("A fazer: Escavação, Entrega | Em andamento:  | Impedida:  | Concluída: Sapatas, Alvenaria");
    }

    [Fact]
    public void K3_CardSaiDeConcluida_OPercentualVoltaAoAnterior_Ou0SeNaoHouver()
    {
        Mover(_sapatas, "Concluída");
        Mover(_sapatas, "Em andamento").Texto.Should().Be("\"Sapatas\" foi para \"Em andamento\": 40% (era 100%).");
        _sapatas.PercentualConcluido.Should().Be(40);

        // Alvenaria chegou ao quadro já com 100%: não há % anterior.
        Mover(_alvenaria, "Impedida");
        _alvenaria.PercentualConcluido.Should().Be(0);
        Retrato().Should().Be("A fazer: Escavação, Entrega | Em andamento: Sapatas | Impedida: Alvenaria | Concluída: ");
    }

    [Fact]
    public void K3_CardEntraEmAFazer_OPercentualVaiA0_EOPlanoAvisaParaOferecerODesfazer()
    {
        var plano = RegrasDoQuadro.Planejar(_c, [_sapatas.Id], Coluna("A fazer").Id, 0);
        (plano.ZeraProgresso, plano.Dica).Should().Be((true, "Mover para \"A fazer\" · o % volta a 0"));

        RegrasDoQuadro.Aplicar(_c, plano).Texto.Should().Be("\"Sapatas\" foi para \"A fazer\": o % voltou a 0 (era 40%).");

        _sapatas.PercentualConcluido.Should().Be(0);
        Retrato().Should().StartWith("A fazer: Sapatas, Escavação, Entrega |");
    }

    [Fact]
    public void K3_CardEntraNumaColunaEmAndamento_OPercentualNaoMuda()
    {
        var plano = RegrasDoQuadro.Planejar(_c, [_sapatas.Id], Coluna("Impedida").Id, 0);
        (plano.SoOQuadro, plano.Dica).Should().Be((true, "Mover para \"Impedida\""));
        RegrasDoQuadro.Aplicar(_c, plano).Texto.Should().Be("\"Sapatas\" foi para \"Impedida\".");
        _sapatas.PercentualConcluido.Should().Be(40);

        Mover(_escavacao, "Em andamento");
        _escavacao.PercentualConcluido.Should().Be(0, "uma tarefa pode estar em andamento com 0%");
        Retrato().Should().Be("A fazer: Entrega | Em andamento: Escavação | Impedida: Sapatas | Concluída: Alvenaria");
    }

    [Fact]
    public void K3_PercentualSobeAcimaDe0NaGrade_CardEmAFazerVaiParaAPrimeiraEmAndamento()
    {
        _escavacao.AtualizarPercentualConcluido(10);

        Quadro.Sincronizar().Should().BeTrue();

        Retrato().Should().Be("A fazer: Entrega | Em andamento: Sapatas, Escavação | Impedida:  | Concluída: Alvenaria");
    }

    [Fact]
    public void K3_PercentualChegaA100NaGrade_CardVaiParaAPrimeiraConcluida_EGuardaOPercentualDeAntes()
    {
        _sapatas.AtualizarPercentualConcluido(100);
        Quadro.Sincronizar();
        Retrato().Should().EndWith("Concluída: Alvenaria, Sapatas");

        Mover(_sapatas, "Em andamento");
        _sapatas.PercentualConcluido.Should().Be(40, "o último % abaixo de 100 que ela teve");
    }

    [Fact]
    public void K3_PercentualCaiAbaixoDe100NaGrade_CardEmConcluidaVoltaParaEmAndamento_OuAFazerCom0()
    {
        _alvenaria.AtualizarPercentualConcluido(70);
        Quadro.Sincronizar();
        Retrato().Should().Be("A fazer: Escavação, Entrega | Em andamento: Sapatas, Alvenaria | Impedida:  | Concluída: ");

        _alvenaria.AtualizarPercentualConcluido(100);
        Quadro.Sincronizar();
        _alvenaria.AtualizarPercentualConcluido(0);
        Quadro.Sincronizar();
        Retrato().Should().StartWith("A fazer: Escavação, Entrega, Alvenaria |");
    }

    [Fact]
    public void K3_CardReordenadoDentroDaColuna_NadaMudaNoCronograma()
    {
        var ordem = _c.Tarefas.Select(t => t.Nome).ToList();
        var plano = RegrasDoQuadro.Planejar(_c, [_entrega.Id], Coluna("A fazer").Id, 0);
        (plano.SoOQuadro, plano.MudaAlgo, plano.Dica).Should().Be((true, true, "Reordenar em \"A fazer\""));

        RegrasDoQuadro.Aplicar(_c, plano).Should().Be(new ResultadoDoQuadro("\"Entrega\" agora é o 1º card de \"A fazer\".", "Reordenar \"Entrega\""));

        Retrato().Should().StartWith("A fazer: Entrega, Escavação |");
        _c.Tarefas.Select(t => t.Nome).Should().Equal(ordem, "a ordem dos cards é independente da do cronograma");
        (_entrega.PercentualConcluido, _escavacao.PercentualConcluido).Should().Be((0, 0));
    }

    [Fact]
    public void K3_TarefaCriadaRenomeadaOuExcluida_OQuadroAcompanha()
    {
        var pintura = new Tarefa("Pintura", Datas.Segunda, 2);
        _c.AdicionarTarefa(pintura);
        _c.RemoverTarefa(_entrega.Id);
        _escavacao.Renomear("Escavação manual");

        Quadro.Sincronizar();

        Retrato().Should().StartWith("A fazer: Escavação manual, Pintura |");
        Quadro.CardDe(_entrega.Id).Should().BeNull();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(9, true)]
    public void SoltarNoMesmoLugar_NaoMudaNada(int lacuna, bool muda)
    {
        // A fazer: Escavação, Entrega. Escavação nas lacunas 0 e 1 (antes e depois dela) fica onde está; a 2 é depois de Entrega.
        RegrasDoQuadro.Planejar(_c, [_escavacao.Id], Coluna("A fazer").Id, lacuna).MudaAlgo.Should().Be(muda);
    }

    [Fact]
    public void VariosCards_VaoJuntos_NaOrdemDoQuadro()
    {
        var plano = RegrasDoQuadro.Planejar(_c, [_alvenaria.Id, _escavacao.Id, _fundacao.Id], Coluna("Impedida").Id, 0);

        plano.Tarefas.Should().Equal([_escavacao.Id, _alvenaria.Id], "o resumo não tem card; vão na ordem do quadro");
        plano.Percentuais.Should().Equal(new MudancaDoPercentual(_alvenaria.Id, "Alvenaria", 100, 0));
        RegrasDoQuadro.Aplicar(_c, plano).Texto.Should().Be("2 cards foram para \"Impedida\": 1 voltou ao % de antes de concluir.");
        Retrato().Should().Be("A fazer: Entrega | Em andamento: Sapatas | Impedida: Escavação, Alvenaria | Concluída: ");
    }

    [Fact]
    public void K1_TarefaQueViraResumoSaiDoQuadro_EVoltaComOsDadosSeFicarSemSubtarefas()
    {
        Mover(_sapatas, "Impedida");
        var cardAntes = Quadro.CardDe(_sapatas.Id);
        var armacao = new Tarefa("Armação", Datas.Segunda, 2);
        _c.AdicionarTarefa(armacao, _sapatas.Id);
        Quadro.Sincronizar();
        Retrato().Should().Be("A fazer: Escavação, Entrega, Armação | Em andamento:  | Impedida:  | Concluída: Alvenaria");

        _c.RemoverTarefa(armacao.Id);
        Quadro.Sincronizar();

        Retrato().Should().Contain("Impedida: Sapatas", "o card guardado volta para a coluna em que estava");
        Quadro.CardDe(_sapatas.Id).Should().Be(cardAntes);
        Quadro.CardDe(armacao.Id).Should().BeNull();
    }

    [Fact]
    public void Copia_EIndependente_ParaODesfazer()
    {
        var copia = _c.Copiar();

        Mover(_sapatas, "Concluída");

        copia.Quadro.CardsDa(copia.Quadro.Colunas[1].Id).Select(k => k.TarefaId).Should().Equal(_sapatas.Id);
        copia.Tarefas.Single(t => t.Id == _sapatas.Id).PercentualConcluido.Should().Be(40);
    }

    [Fact]
    public void Colunas_PrecisamDeUmaDeCadaTipo_NomesDiferentes_LimiteECor()
    {
        var padrao = QuadroDoProjeto.ColunasPadrao();
        var semConcluida = padrao.Where(c => c.Tipo != TipoDeColuna.Concluida);
        var repetida = padrao.Append(new ColunaDoQuadro(Guid.NewGuid(), "impedida", TipoDeColuna.EmAndamento));

        FluentActions.Invoking(() => QuadroDoProjeto.ValidarColunas(semConcluida)).Should().Throw<DomainValidationException>()
            .WithMessage("O quadro precisa de pelo menos uma coluna do tipo Concluída: é para onde vão as tarefas com 100%.");
        FluentActions.Invoking(() => QuadroDoProjeto.ValidarColunas(repetida)).Should().Throw<DomainValidationException>()
            .WithMessage("Já existe a coluna \"Impedida\" no quadro.");
        FluentActions.Invoking(() => QuadroDoProjeto.ValidarColunas(padrao.Select(c => c with { Limite = 0 })))
            .Should().Throw<DomainValidationException>().WithMessage("*pelo menos 1 (vazio: sem limite).");
        FluentActions.Invoking(() => QuadroDoProjeto.ValidarColunas(padrao.Select(c => c with { Cor = "azul" })))
            .Should().Throw<DomainValidationException>().WithMessage("*não é uma cor (#RRGGBB).");
        QuadroDoProjeto.ValidarColunas(padrao.Select(c => c with { Nome = $"  {c.Nome} " })).Select(c => c.Nome)
            .Should().Equal("A fazer", "Em andamento", "Impedida", "Concluída");
    }

    [Fact]
    public void Restaurar_DeixaDeForaCardsDeTarefasQueNaoExistem_EASincronizacaoReposicionaOsDeColunasQueNaoExistem()
    {
        var colunas = QuadroDoProjeto.ColunasPadrao();
        Quadro.Restaurar(colunas, [], [
            new CardDoQuadro(_sapatas.Id, colunas[2].Id, 0) { PercentualAnterior = 40 },
            new CardDoQuadro(_escavacao.Id, Guid.NewGuid(), 0),
            new CardDoQuadro(Guid.NewGuid(), colunas[0].Id, 1),
        ], ModoDasRaias.Nenhuma);

        Quadro.Cards.Should().HaveCount(2);
        Quadro.Sincronizar();

        Retrato().Should().Be("A fazer: Escavação, Entrega | Em andamento:  | Impedida: Sapatas | Concluída: Alvenaria");
        Quadro.Vazio.Should().BeFalse();
        new Cronograma("Novo", Datas.Segunda).Quadro.Vazio.Should().BeTrue();
    }
}
