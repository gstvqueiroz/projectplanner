using ProjectPlanner.Core;
using ProjectPlanner.Core.Edicao;

namespace ProjectPlanner.Core.Tests;

// Bloco 73: as regras para mover arrastando (o plano "Delete, arrastar e Kanban", M3 a M6), com a estrutura e a tabela de
// exemplos do plano: Fundação (Escavação, Sapatas), Estrutura (Pilares) e o marco Entrega. Linhas visíveis:
// 0 Fundação · 1 Escavação · 2 Sapatas · 3 Estrutura · 4 Pilares · 5 Entrega; a lacuna g é o espaço antes da linha g.
public class RegrasDeMovimentoTests
{
    private readonly Cronograma _c = new("Obra", Datas.Segunda);
    private readonly Tarefa _fundacao = new("Fundação", Datas.Segunda, 1);
    private readonly Tarefa _escavacao = new("Escavação", Datas.Segunda, 3);
    private readonly Tarefa _sapatas = new("Sapatas", Datas.Segunda, 4);
    private readonly Tarefa _estrutura = new("Estrutura", Datas.Segunda, 1);
    private readonly Tarefa _pilares = new("Pilares", Datas.Segunda, 5);
    private readonly Tarefa _entrega = new("Entrega", Datas.Segunda, 0);

    public RegrasDeMovimentoTests()
    {
        _c.AdicionarTarefa(_fundacao);
        _c.AdicionarTarefa(_escavacao, _fundacao.Id);
        _c.AdicionarTarefa(_sapatas, _fundacao.Id);
        _c.AdicionarTarefa(_estrutura);
        _c.AdicionarTarefa(_pilares, _estrutura.Id);
        _c.AdicionarTarefa(_entrega);
        CalculadoraCronograma.Calcular(_c);
    }

    private IReadOnlyList<Guid> Visiveis => [.. _c.Tarefas.Select(t => t.Id)];

    private PlanoDeMovimento Planejar(Tarefa arrastada, int lacuna, int puxao = 0) =>
        RegrasDeMovimento.Planejar(_c, Visiveis, [arrastada.Id], lacuna, puxao);

    private string Estrutura() => string.Join(" · ", _c.Tarefas.Select(t => $"{new string('-', _c.NivelDe(t.Id))}{t.Nome}"));

    private ResultadoDoMovimento Mover(Tarefa arrastada, int lacuna, int puxao = 0) => RegrasDeMovimento.Aplicar(_c, Planejar(arrastada, lacuna, puxao));

    [Fact]
    public void Sapatas_EntrePilaresEEntrega_ViraSubtarefaDeEstrutura_MantendoONivel()
    {
        var plano = Planejar(_sapatas, 5);
        (plano.Valido, plano.Nivel, plano.Dica).Should().Be((true, 1, "Mover \"Sapatas\" para \"Estrutura\""));

        Mover(_sapatas, 5).Texto.Should().Be("\"Sapatas\" agora é a 2.2.");
        Estrutura().Should().Be("Fundação · -Escavação · Estrutura · -Pilares · -Sapatas · Entrega");
    }

    [Fact]
    public void OMesmo_PuxandoParaAEsquerda_FicaNoPrimeiroNivel_EntreEstruturaEEntrega()
    {
        Planejar(_sapatas, 5, puxao: -1).Nivel.Should().Be(0);

        Mover(_sapatas, 5, puxao: -1);

        Estrutura().Should().Be("Fundação · -Escavação · Estrutura · -Pilares · Sapatas · Entrega");
    }

    [Fact]
    public void Entrega_EntreEscavacaoESapatas_EntraEmFundacao_PorqueAliONivel1NaoCabe()
    {
        Planejar(_entrega, 2).Nivel.Should().Be(1);

        Mover(_entrega, 2);

        Estrutura().Should().Be("Fundação · -Escavação · -Entrega · -Sapatas · Estrutura · -Pilares");
    }

    [Fact]
    public void Sapatas_PuxadaParaADireita_LogoAbaixoDeEscavacao_EscavacaoViraResumo()
    {
        var plano = Planejar(_sapatas, 2, puxao: 1);
        (plano.Nivel, plano.Dica, plano.ViraResumo).Should().Be((2, "Tornar subtarefa de \"Escavação\"", "Escavação"));

        Mover(_sapatas, 2, puxao: 1).Texto.Should().Be("\"Sapatas\" agora é a 1.1.1. \"Escavação\" virou resumo.");

        Estrutura().Should().Be("Fundação · -Escavação · --Sapatas · Estrutura · -Pilares · Entrega");
    }

    [Fact]
    public void Fundacao_EntrePilaresEEntrega_LevaAsSubtarefas_EFicaNoPrimeiroNivel()
    {
        Mover(_fundacao, 5);

        Estrutura().Should().Be("Estrutura · -Pilares · Fundação · -Escavação · -Sapatas · Entrega");
    }

    [Fact]
    public void Bloqueados_ResumoDentroDeleMesmo_SubtarefaNumMarcoOuNumaTarefaComVinculos()
    {
        Planejar(_fundacao, 2).Should().Match<PlanoDeMovimento>(p => !p.Valido && p.Motivo == RegrasDeMovimento.DentroDeSiMesmo);
        Planejar(_pilares, 6, puxao: 1).Motivo.Should().Be("\"Entrega\" é um marco, e marcos não têm subtarefas.");
        _c.AdicionarPredecessora(new Predecessora(_escavacao.Id, _pilares.Id));
        Planejar(_sapatas, 2, puxao: 1).Motivo.Should().StartWith("\"Escavação\" tem vínculos");
    }

    [Fact]
    public void NoMesmoLugar_NaoMudaNada_EResumoQueEsvaziaViraTarefa()
    {
        Planejar(_sapatas, 2).MudaAlgo.Should().BeFalse("antes ou depois da própria linha");
        Planejar(_sapatas, 3).MudaAlgo.Should().BeFalse();

        var r = Mover(_pilares, 6, puxao: -1);

        Estrutura().Should().Be("Fundação · -Escavação · -Sapatas · Estrutura · Entrega · Pilares");
        r.Texto.Should().Be("\"Pilares\" agora é a 4. \"Estrutura\" virou tarefa comum.");
        _estrutura.EhResumo.Should().BeFalse();
    }

    [Fact]
    public void Varias_VaoJuntas_NaOrdemEmQueEstavam()
    {
        // Soltas depois de Entrega (o fim): lá só cabe o primeiro nível.
        var plano = RegrasDeMovimento.Planejar(_c, Visiveis, [_pilares.Id, _escavacao.Id], 6);

        (plano.Tarefas.First(), plano.Tarefas.Last(), plano.Nivel).Should().Be((_escavacao.Id, _pilares.Id, 0));
        RegrasDeMovimento.Aplicar(_c, plano).Texto.Should().Be("2 tarefas movidas (a partir da 4). \"Estrutura\" virou tarefa comum.");
        Estrutura().Should().Be("Fundação · -Sapatas · Estrutura · Entrega · Escavação · Pilares");
    }
}
