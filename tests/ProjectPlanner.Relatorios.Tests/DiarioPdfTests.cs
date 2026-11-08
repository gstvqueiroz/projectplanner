using System.Text;
using System.Text.RegularExpressions;
using ProjectPlanner.Core.Diario;
using ProjectPlanner.Core.Relatorios;
using SkiaSharp;

namespace ProjectPlanner.Relatorios.Tests;

// Bloco 81: o Diário de Obra (RDO) em PDF: um dia numa página; vários dias, uma página cada; um dia com muita coisa continua na
// seguinte; as fotos entram (até oito, quatro por linha).
public sealed class DiarioPdfTests
{
    private static readonly CabecalhoDoRelatorio Cabecalho =
        new("Residência Silva", new DadosDaObra("Maria Silva", "Contrato 12/2026", "Rua das Flores, 100"), "Eng. Ana Souza", "CREA 123", null);

    private static readonly PrazoNoDiario Prazo = new(new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 18));

    private static DiaDaObra Dia(DateOnly data, int linhas = 3) => new(data)
    {
        Manha = ClimaDoPeriodo.Bom,
        Tarde = new ClimaDoPeriodo(TempoNoPeriodo.Chuva, Praticavel: false),
        Efetivo = [.. Enumerable.Range(1, linhas).Select(i => new EfetivoNoDia($"Função {i}", i, i % 2 == 0 ? "Empreiteira X" : null))],
        Equipamentos = [new EquipamentoNoDia("Betoneira 400 L", 1)],
        Atividades = [.. Enumerable.Range(1, linhas).Select(i => new AtividadeNoDia(null, $"2.{i} Alvenaria do bloco {i}", 10 * i % 100))],
        Ocorrencias = "Chuva forte a partir das 13h; serviços externos parados.\nEntrega de 200 sacos de cimento (NF 4521).",
        Fechamento = new FechamentoDoDia(new DateTime(2026, 9, 28, 18, 0, 0), "Eng. Ana Souza"),
    };

    private static byte[] Foto()
    {
        using var bitmap = new SKBitmap(64, 48);
        bitmap.Erase(SKColors.SteelBlue);
        using var imagem = SKImage.FromBitmap(bitmap);
        return imagem.Encode(SKEncodedImageFormat.Jpeg, 80).ToArray();
    }

    private static byte[] Pdf(params DiaNoPdf[] dias)
    {
        using var destino = new MemoryStream();
        new GeradorDiarioPdf().Gerar(dias, Cabecalho, Prazo, "28/09/2026 18:05", destino);
        return destino.ToArray();
    }

    private static int Paginas(byte[] pdf) => Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page\b").Count;

    private static DiaNoPdf ComFotos(int fotos) =>
        new(Dia(new DateOnly(2026, 9, 28)), 12, [.. Enumerable.Range(1, fotos).Select(i => new FotoDoDiaNoPdf(Foto(), $"Foto {i}"))]);

    [Fact]
    public void UmDia_ComQuatroFotos_CabeNumaPagina_ComMais_AsFotosContinuam()
    {
        var pdf = Pdf(ComFotos(4));

        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        Paginas(pdf).Should().Be(1);
        Paginas(Pdf(ComFotos(12))).Should().Be(2, "entram até oito fotos, duas linhas de quatro");
    }

    [Fact]
    public void VariosDias_UmaPaginaCada_EMuitaCoisaContinuaNaSeguinte()
    {
        Paginas(Pdf(new DiaNoPdf(Dia(new DateOnly(2026, 9, 28)), 1, []), new DiaNoPdf(Dia(new DateOnly(2026, 9, 29)), 2, []))).Should().Be(2);
        Paginas(Pdf(new DiaNoPdf(Dia(new DateOnly(2026, 9, 28), linhas: 40), 1, []))).Should().Be(3);
        FluentActions.Invoking(() => Pdf()).Should().Throw<ArgumentException>();
    }
}
