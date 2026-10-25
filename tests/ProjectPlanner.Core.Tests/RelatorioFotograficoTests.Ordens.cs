using ProjectPlanner.Core.OrdensDeServico;
using ProjectPlanner.Core.Relatorios;

namespace ProjectPlanner.Core.Tests;

// Bloco 66: o relatório fotográfico de uma OS: as fotos das tarefas dela (uma etapa traz as de dentro), no período dela, com a
// OS na capa e o medido de cada tarefa.
public partial class RelatorioFotograficoTests
{
    private static OrdemDeServico OsDaFundacao(Obra obra, DateTime emissao)
    {
        var dados = new DadosDaOrdem("Fundação", TipoDeExecutante.Terceiro, "Construtora Silva", null, null, [obra.Fundacao.Id],
            [new ItemDaOrdem(obra.Escavacao.Id, "Escavação", "m³", 10, 50), new ItemDaOrdem(obra.Sapatas.Id, "Sapatas", "un", 4, 250)],
            null, new DateOnly(2026, 10, 9), FormaDePagamentoDaOrdem.PorMedicao, null, null);
        var os = OrdemDeServico.Nova(Guid.NewGuid(), 7, dados, emissao).Emitir(emissao).Aceitar(emissao)
            .Medir(Guid.NewGuid(), new DateOnly(2026, 10, 5), [10, 2], emissao);
        obra.Cronograma.OrdensDeServico.Adicionar(os);
        return os;
    }

    [Fact]
    public void DeUmaOrdem_SoAsTarefasDela_NoPeriodo_ComAOSNaCapaEOMedido()
    {
        var obra = new Obra();
        var os = OsDaFundacao(obra, new DateTime(2026, 9, 29));
        obra.Foto(obra.Escavacao, new DateTime(2026, 9, 30, 9, 0, 0));
        obra.Foto(obra.Sapatas, new DateTime(2026, 10, 6, 9, 0, 0));
        obra.Foto(obra.Cobertura, new DateTime(2026, 10, 6, 10, 0, 0)); // fora da OS
        var (de, ate) = MontadorDoRelatorio.PeriodoDaOrdem(os, new DateOnly(2026, 10, 11));

        var r = obra.Montar(new OpcoesDoRelatorio(de, ate) { Tarefas = TarefasDoRelatorio.DeUmaOrdem, Ordem = os.Id });

        (de, ate).Should().Be((new DateOnly(2026, 9, 29), new DateOnly(2026, 10, 11)));
        r.Grupos.Select(g => g.Titulo).Should().Equal("1.1 Escavação", "1.2 Sapatas");
        r.Grupos[0].Detalhe.Should().EndWith("· 100% medido na OS-007");
        r.Grupos[1].Detalhe.Should().EndWith("· 50% medido na OS-007");
        r.Titulo.Should().Be("Relatório fotográfico nº 1 da OS-007 · 29/09/2026 a 11/10/2026");
        r.NomeSugerido.Should().Be("Relatório fotográfico 01 - OS-007 - Residência Silva");
        r.Ordem.Should().Be(os);
    }

    [Fact]
    public void DeUmaOrdem_SemAOS_EOPeriodoDaConferida()
    {
        FluentActions.Invoking(() => new OpcoesDoRelatorio(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2)) { Tarefas = TarefasDoRelatorio.DeUmaOrdem }.Validar())
            .Should().Throw<DomainValidationException>().WithMessage("Escolha a ordem de serviço.");
        var obra = new Obra();
        var os = OsDaFundacao(obra, new DateTime(2026, 9, 29)).Concluir(new DateTime(2026, 10, 7)).Conferir(new DateTime(2026, 10, 8, 16, 0, 0));

        MontadorDoRelatorio.PeriodoDaOrdem(os, new DateOnly(2026, 10, 30)).Should().Be((new DateOnly(2026, 9, 29), new DateOnly(2026, 10, 8)));
    }
}
