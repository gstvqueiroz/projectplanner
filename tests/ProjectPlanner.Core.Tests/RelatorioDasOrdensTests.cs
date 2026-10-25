using ProjectPlanner.Core.OrdensDeServico;

namespace ProjectPlanner.Core.Tests;

// Bloco 64: o relatório das OS: o filtro (período, situações, executante), os totais sem as canceladas, por executante e o que
// pede atenção.
public class RelatorioDasOrdensTests
{
    private static readonly DateTime Agora = new(2026, 10, 1, 9, 0, 0);
    private static readonly DateOnly Hoje = new(2026, 10, 20);

    private static OrdemDeServico Os(int n, string executante, DateOnly prazo) => OrdemDeServico.Nova(Guid.NewGuid(), n,
        new DadosDaOrdem($"Serviço {n}", TipoDeExecutante.Terceiro, executante, null, null, null, [new ItemDaOrdem(null, "x", "vb", 1, 1000 * n)],
            new DateOnly(2026, 10, 1), prazo, FormaDePagamentoDaOrdem.Empreitada, null, null), Agora);

    private static List<OrdemDeServico> Ordens() =>
    [
        Os(1, "Construtora Silva", new DateOnly(2026, 10, 10)).Emitir(Agora), // vencida
        Os(2, "Construtora Silva", new DateOnly(2026, 10, 30)).Emitir(new DateTime(2026, 10, 15)).Iniciar(Agora).Concluir(Agora), // para conferir
        Os(3, "Equipe do João", new DateOnly(2026, 10, 30)).Emitir(Agora).Cancelar(Agora, "x"),
        Os(4, "Equipe do João", new DateOnly(2026, 10, 30)), // rascunho
    ];

    [Fact]
    public void Todas_TotaisSemAsCanceladas_PorExecutante_EAtencao()
    {
        var r = RelatorioDasOrdens.Montar(Ordens(), new FiltroDoRelatorioDasOrdens(), Hoje);

        (r.Ordens.Count, r.Valor, r.PorMedir).Should().Be((4, 7000m, 3000m));
        r.PorSituacao.Should().Equal((SituacaoDaOrdem.Rascunho, 1), (SituacaoDaOrdem.Emitida, 1), (SituacaoDaOrdem.Concluida, 1), (SituacaoDaOrdem.Cancelada, 1));
        r.PorExecutante.Select(e => (e.Nome, e.Ordens, e.Valor)).Should().Equal(("Construtora Silva", 2, 3000m));
        (r.Vencidas.Single().Numero, r.ParaConferir.Single().Numero, r.DiasDeAtraso(r.Vencidas[0])).Should().Be((1, 2, 10));
        r.Filtro.Texto.Should().Be("Todas as OS");
    }

    [Fact]
    public void Filtro_PeloPeriodoDaEmissao_SituacoesEExecutante()
    {
        var filtro = new FiltroDoRelatorioDasOrdens(new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 31), SituacoesDoRelatorio.Abertas, " construtora silva ");

        var r = RelatorioDasOrdens.Montar(Ordens(), filtro, Hoje);

        r.Ordens.Select(o => o.Numero).Should().Equal(2);
        filtro.Texto.Should().Be("OS de 10/10/2026 a 31/10/2026 · abertas · construtora silva");
        RelatorioDasOrdens.Montar(Ordens(), new FiltroDoRelatorioDasOrdens(Situacoes: SituacoesDoRelatorio.Vencidas), Hoje).Ordens.Select(o => o.Numero).Should().Equal(1);
        RelatorioDasOrdens.Montar(Ordens(), new FiltroDoRelatorioDasOrdens(Situacoes: SituacoesDoRelatorio.Encerradas), Hoje).Ordens.Select(o => o.Numero).Should().Equal(3);
    }
}
