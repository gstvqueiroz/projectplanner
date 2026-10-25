using System.Text;
using System.Text.RegularExpressions;
using ProjectPlanner.Core;
using SkiaSharp;

namespace ProjectPlanner.Relatorios.Tests;

// Bloco 57: o Gantt para imprimir: a folha (A4…A0, Carta, Ofício; retrato ou paisagem), a escala, a divisão em páginas, os
// níveis da EDT e a prévia.
public sealed class GanttImpressoTests
{
    private static readonly DateTime Seg = new(2026, 9, 28);

    /// <summary>Etapas (resumos) com <paramref name="porEtapa"/> tarefas de 5 dias em sequência e um marco no fim.</summary>
    private static (Cronograma Cronograma, ResultadoCronograma Resultado) Obra(int etapas, int porEtapa = 3)
    {
        var c = new Cronograma("Residência Silva", Seg);
        Tarefa? anterior = null;
        for (var e = 0; e < etapas; e++)
        {
            var etapa = new Tarefa($"Etapa {e + 1}", Seg, 1);
            c.AdicionarTarefa(etapa);
            for (var i = 0; i < porEtapa; i++)
            {
                var tarefa = new Tarefa($"Serviço {e + 1}.{i + 1}", Seg, 5);
                c.AdicionarTarefa(tarefa, etapa.Id);
                if (anterior is not null)
                    c.AdicionarPredecessora(new Predecessora(anterior.Id, tarefa.Id));
                anterior = tarefa;
            }
        }
        var entrega = new Tarefa("Entrega da obra", Seg, 0);
        c.AdicionarTarefa(entrega);
        if (anterior is not null)
            c.AdicionarPredecessora(new Predecessora(anterior.Id, entrega.Id));
        var resultado = CalculadoraCronograma.Calcular(c);
        return (c, resultado);
    }

    private static GanttParaImprimir Montar((Cronograma C, ResultadoCronograma R) obra, OpcoesDaImpressaoDoGantt? opcoes = null) =>
        GanttParaImprimir.Montar(obra.C, obra.R, opcoes ?? new OpcoesDaImpressaoDoGantt { Titulo = "Residência Silva" }, Seg.AddDays(10));

    private static int Paginas(byte[] pdf) => Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page\b").Count;

    private static byte[] Pdf(GanttParaImprimir gantt)
    {
        using var destino = new MemoryStream();
        gantt.GerarPdf(destino);
        return destino.ToArray();
    }

    [Theory]
    [InlineData(TamanhoDaFolha.A4, 595.28f, 841.89f)]
    [InlineData(TamanhoDaFolha.A3, 841.89f, 1190.55f)]
    [InlineData(TamanhoDaFolha.A0, 2383.94f, 3370.39f)]
    [InlineData(TamanhoDaFolha.Carta, 612f, 792f)]
    [InlineData(TamanhoDaFolha.Oficio, 612.28f, 935.43f)]
    public void Folha_TamanhoEmPontos_PaisagemDeitaAFolha(TamanhoDaFolha folha, float largura, float altura)
    {
        var obra = Obra(2);

        var retrato = Montar(obra, new OpcoesDaImpressaoDoGantt { Folha = folha, Paisagem = false });
        var paisagem = Montar(obra, new OpcoesDaImpressaoDoGantt { Folha = folha, Paisagem = true });

        (retrato.LarguraDaFolha, retrato.AlturaDaFolha).Should().Be((largura, altura));
        (paisagem.LarguraDaFolha, paisagem.AlturaDaFolha).Should().Be((altura, largura));
    }

    [Fact]
    public void ObraPequena_AjustadaALargura_CabeNumaPagina_EOPdfSai()
    {
        var gantt = Montar(Obra(2));

        (gantt.Paginas, gantt.FaixasDeTempo, gantt.Resumo).Should().Be((1, 1, "1 página · A3 paisagem"));
        var pdf = Pdf(gantt);
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        Paginas(pdf).Should().Be(1);
    }

    [Fact]
    public void MuitasTarefas_DividemEmPaginasPorLinhas_EAFolhaMaiorPrecisaDeMenos()
    {
        var obra = Obra(10, porEtapa: 8); // 91 linhas

        var a4 = Montar(obra, new OpcoesDaImpressaoDoGantt { Folha = TamanhoDaFolha.A4 });
        var a1 = Montar(obra, new OpcoesDaImpressaoDoGantt { Folha = TamanhoDaFolha.A1 });

        a4.BlocosDeLinhas.Should().Be((int)Math.Ceiling(91.0 / a4.LinhasPorPagina)).And.BeGreaterThan(2);
        a1.Paginas.Should().Be(1);
        Paginas(Pdf(a4)).Should().Be(a4.Paginas);
        a4.Resumo.Should().Be($"{a4.Paginas} páginas ({a4.LinhasPorPagina} tarefas por folha) · A4 paisagem");
    }

    [Fact]
    public void EscalaFixa_ObraLonga_DivideOTempoEmFolhasLadoALado()
    {
        var obra = Obra(6, porEtapa: 5); // 30 serviços de 5 dias úteis: uns 6 meses

        var ajustada = Montar(obra, new OpcoesDaImpressaoDoGantt { Folha = TamanhoDaFolha.A4 });
        var emDias = Montar(obra, new OpcoesDaImpressaoDoGantt { Folha = TamanhoDaFolha.A4, Escala = EscalaDaImpressao.Dias });
        var emMeses = Montar(obra, new OpcoesDaImpressaoDoGantt { Folha = TamanhoDaFolha.A3, Escala = EscalaDaImpressao.Meses });

        ajustada.FaixasDeTempo.Should().Be(1);
        emDias.PontosPorDia.Should().Be(GanttParaImprimir.PontosPorDiaNosDias);
        emDias.FaixasDeTempo.Should().BeGreaterThan(3);
        emDias.Paginas.Should().Be(emDias.FaixasDeTempo * emDias.BlocosDeLinhas);
        emMeses.FaixasDeTempo.Should().Be(1, "a dois pontos por dia, os 7 meses cabem na A3");
        Paginas(Pdf(emDias)).Should().Be(emDias.Paginas);
        emDias.Resumo.Should().StartWith($"{emDias.Paginas} páginas (o tempo em {emDias.FaixasDeTempo} folhas lado a lado");
    }

    [Fact]
    public void NivelMaximo_DeixaSoAsEtapas_EComoNaTelaTiraAsOcultas()
    {
        var obra = Obra(3);

        var etapas = Montar(obra, new OpcoesDaImpressaoDoGantt { NivelMaximo = 1, Folha = TamanhoDaFolha.A4 });
        var ocultas = obra.Cronograma.Tarefas.Where(t => t.TarefaPaiId == obra.Cronograma.Tarefas[0].Id).Select(t => t.Id).ToHashSet();
        var comoNaTela = Montar(obra, new OpcoesDaImpressaoDoGantt { TarefasOcultas = ocultas });

        etapas.Resumo.Should().Be("1 página · A4 paisagem");
        Encoding.Latin1.GetString(Pdf(etapas)).Should().NotBeEmpty();
        // O cabeçalho conta as tarefas impressas: 3 etapas e o marco; na tela, 13 menos as 3 recolhidas.
        etapas.Datas.Should().Contain("4 tarefas");
        comoNaTela.Datas.Should().Contain("10 tarefas");
    }

    [Fact]
    public void Previa_DesenhaAPaginaNaLarguraPedida()
    {
        var gantt = Montar(Obra(2), new OpcoesDaImpressaoDoGantt
        {
            Folha = TamanhoDaFolha.A4,
            Paisagem = false,
            Colunas = Enum.GetValues<ColunaDoGanttImpresso>().ToHashSet(),
        });

        using var imagem = SKBitmap.Decode(gantt.Previa(0, 600));

        (imagem.Width, imagem.Height).Should().Be((600, (int)Math.Round(841.89 * 600 / 595.28)));
        var naoBrancos = 0;
        for (var y = 0; y < imagem.Height; y += 4)
            for (var x = 0; x < imagem.Width; x += 4)
                if (imagem.GetPixel(x, y) != SKColors.White)
                    naoBrancos++;
        naoBrancos.Should().BeGreaterThan(500, "a tabela, as barras e a legenda aparecem");
        gantt.Invoking(g => g.Previa(1, 600)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ObraSemTarefas_SaiUmaPaginaVazia()
    {
        var c = new Cronograma("Nova", Seg);
        var gantt = GanttParaImprimir.Montar(c, CalculadoraCronograma.Calcular(c), new OpcoesDaImpressaoDoGantt(), Seg);

        gantt.Paginas.Should().Be(1);
        Paginas(Pdf(gantt)).Should().Be(1);
    }
}
