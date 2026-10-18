using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Indicadores;
using ProjectPlanner.Core.Orcamento;
using ProjectPlanner.Core.Planejamento;

namespace ProjectPlanner.Relatorios.Tests;

// Bloco 43: os indicadores ampliados nos textos, no PDF e no Word. Fundação (Escavação R$ 5.000, custo orçado R$ 2.000,
// 100% feita, custo real R$ 4.000) e Estrutura (R$ 20.000, sem custo orçado), em 09/10/2026.
public sealed class AnaliseExportadaTests
{
    private static readonly DateTime Seg = new(2026, 9, 28);

    private static (PainelDeIndicadores Painel, AnaliseFinanceira Analise) Montar()
    {
        var c = new Cronograma("Residência", Seg);
        var fundacao = new Tarefa("Fundação", Seg, 1);
        var escavacao = new Tarefa("Escavação", Seg, 5);
        var estrutura = new Tarefa("Estrutura", Seg, 10);
        c.AdicionarTarefa(fundacao);
        c.AdicionarTarefa(escavacao, fundacao.Id);
        c.AdicionarTarefa(estrutura);
        c.AdicionarPredecessora(new Predecessora(escavacao.Id, estrutura.Id));
        escavacao.DefinirValor(5000);
        estrutura.DefinirValor(20000);
        escavacao.DefinirQuantidade(100);
        c.Orcamento.Associar(escavacao, new ComposicaoAberta(new BaseDeReferencia("SINAPI"), "1", "ESCAVAÇÃO", "M3",
            [new InsumoNaComposicao("1", "ESCAVAÇÃO", "M3", TipoDeInsumo.Servico, 1, 20)]));
        CalculadoraCronograma.Calcular(c);
        escavacao.AtualizarPercentualConcluido(100);
        c.Custos.Adicionar(new LancamentoDeCusto(Guid.NewGuid(), new DateOnly(2026, 10, 1), escavacao.Id, CategoriaDeCusto.Material, "Diesel", null, 4000));
        c.Custos.Adicionar(new LancamentoDeCusto(Guid.NewGuid(), new DateOnly(2026, 10, 2), null, CategoriaDeCusto.Outros, "Canteiro", null, 500));
        var painel = PainelDeIndicadores.Montar(c, new DateOnly(2026, 10, 9), Periodicidade.Mensal);
        return (painel, AnaliseFinanceira.Montar(c, painel, Periodicidade.Mensal));
    }

    private static int Paginas(byte[] pdf) => Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page\b").Count;

    private static readonly AnaliseEscrita Escrita = new("A fundação gastou o dobro do orçado.", ["Custo da escavação."], ["Rever o diesel."]);

    [Fact]
    public void Textos_CustosMedicoesCaixaEParaAIA()
    {
        var (painel, a) = Montar();

        TextosDaAnalise.Custos(a).Select(l => l[0]).Should().Equal("1 Fundação", "2 Estrutura", "Total", "Custos gerais (sem tarefa)");
        TextosDaAnalise.Custos(a)[2][5].Should().Be("4.000,00", "o total é o das etapas; os custos gerais vêm à parte");
        TextosDaAnalise.Cartoes(a).Single(c => c.Rotulo == "Custo real").Detalhe.Should().Be("com R$ 500,00 de custos gerais");
        TextosDaAnalise.Cartoes(a).Single(c => c.Rotulo == "Contratado").Detalhe.Should().Be("sem aditivos");
        TextosDaAnalise.Custos(a)[0].Should().Equal("1 Fundação", "5.000,00", "2.000,00", "3.000,00 (60,0%)", "2.000,00", "4.000,00", "-2.000,00",
            "4.000,00", "1.000,00");
        TextosDaAnalise.Custos(a)[1][2].Should().Be("—", "sem custo orçado");
        TextosDaAnalise.Cartoes(a).Single(c => c.Rotulo == "Margem prevista").Detalhe.Should().Be("60,0% de R$ 5.000,00 com custo orçado");
        TextosDaAnalise.Medicoes(a)[^1].Should().Equal("Total", "25.000,00", "20,0%", "0,00 (0,0%)", "−20,0 pontos", "5.000,00");
        TextosDaAnalise.Caixa(a).Should().HaveCount(a.Caixa.Count);
        TextosDaAnalise.Contrato(a).Select(c => c.Rotulo).Should().Contain(["Valor original", "Saldo do contrato"]);
        var linhas = TextosDaAnalise.ParaAIA(painel, a);
        linhas.Should().Contain(l => l.StartsWith("Prazo: físico previsto"))
            .And.Contain(l => l.StartsWith("Etapa 1 Fundação: contratado 5.000,00; custo orçado 2.000,00"))
            .And.Contain(l => l.StartsWith("Alerta Custo (Crítico)"));
        (TextosDaAnalise.Cor(NivelDoAlerta.Critico), TextosDaAnalise.Nivel(NivelDoAlerta.SemDados)).Should().Be(("#C62828", "Sem dados"));
        new AnaliseEscrita(" ", [], []).Vazia.Should().BeTrue();
    }

    [Fact]
    public void Pdf_ComAAnalise_MaisPaginas_SemElaUmaSo()
    {
        var (painel, a) = Montar();
        using var so = new MemoryStream();
        new GeradorIndicadoresPdf().Gerar(painel, "Residência", Seg, so);
        Paginas(so.ToArray()).Should().Be(1, "como antes do Bloco 43");

        using var com = new MemoryStream();
        new GeradorIndicadoresPdf().Gerar(painel, "Residência", Seg, com, a, Escrita);

        Encoding.ASCII.GetString(com.ToArray(), 0, 5).Should().Be("%PDF-");
        Paginas(com.ToArray()).Should().BeGreaterThanOrEqualTo(4, "o painel, custos e margem, medições e caixa, alertas e análise");
    }

    [Fact]
    public void Word_ComAsSecoesEAAnalise()
    {
        var (painel, a) = Montar();
        using var destino = new MemoryStream();

        new GeradorIndicadoresWord().Gerar(painel, "Residência", Seg, destino, a, Escrita);

        destino.Position = 0;
        using var documento = WordprocessingDocument.Open(destino, false);
        var texto = documento.MainDocumentPart!.Document!.Body!.InnerText;
        texto.Should().Contain("Custos e margem: do orçado ao realizado").And.Contain("Medições e fluxo de caixa").And.Contain("Alertas e análise")
            .And.Contain("A fundação gastou o dobro do orçado.").And.Contain("• Rever o diesel.");
        documento.MainDocumentPart.ImageParts.Should().HaveCountGreaterThanOrEqualTo(2, "a Curva S e o fluxo de caixa");
    }

    [Fact]
    public void ImagemDoCaixa_UmPng()
    {
        var (_, a) = Montar();
        var png = ImagemDoCaixa.Png(a, 600, 200);
        png.Take(4).Should().Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
    }
}
