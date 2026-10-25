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

// Bloco 65: o boletim de medição da OS.
public class BoletimDaOrdemTests
{
    private static readonly DateTime Agora = new(2026, 10, 1, 9, 0, 0);

    [Fact]
    public void Boletim_AnteriorNestaAcumuladoSaldo_RetencaoELiquido()
    {
        var dados = new DadosDaOrdem("Alvenaria", TipoDeExecutante.Terceiro, "Construtora Silva", null, null, null,
            [new ItemDaOrdem(null, "Alvenaria", "m²", 100, 30), new ItemDaOrdem(null, "Verga", "m", 10, 15)], null, null, FormaDePagamentoDaOrdem.PorMedicao,
            null, null, retencao: 5);
        var os = OrdemDeServico.Nova(Guid.NewGuid(), 3, dados, Agora).Emitir(Agora).Aceitar(Agora)
            .Medir(Guid.NewGuid(), new DateOnly(2026, 10, 5), [40, 0], Agora).Medir(Guid.NewGuid(), new DateOnly(2026, 10, 12), [20, 10], Agora);

        var b = BoletimDaOrdem.Montar(os, os.Medicoes[1].Id);

        b.Titulo.Should().Be("OS-003 · Boletim de medição M2");
        b.Linhas.Select(l => (l.Anterior, l.NestaMedicao, l.Acumulado, l.Saldo, l.Percentual, l.ValorNestaMedicao)).Should().Equal(
            (40m, 20m, 60m, 40m, 60m, 600m), (0m, 10m, 10m, 0m, 100m, 150m));
        (b.ValorNestaMedicao, b.MedidoAnterior, b.MedidoAcumulado, b.Saldo, b.Retencao, b.Liquido, b.PercentualAcumulado)
            .Should().Be((750m, 1200m, 1950m, 1200m, 37.5m, 712.5m, 61.9m));
        FluentActions.Invoking(() => BoletimDaOrdem.Montar(os, Guid.NewGuid())).Should().Throw<DomainValidationException>();
    }
}
