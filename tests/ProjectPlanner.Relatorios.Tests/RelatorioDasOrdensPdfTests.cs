using System.Text;
using System.Text.RegularExpressions;
using ProjectPlanner.Core.OrdensDeServico;
using ProjectPlanner.Core.Relatorios;

namespace ProjectPlanner.Relatorios.Tests;

// Bloco 64: o relatório das OS em PDF.
public sealed class RelatorioDasOrdensPdfTests
{
    private static readonly DateTime Agora = new(2026, 10, 1, 9, 0, 0);

    internal static List<OrdemDeServico> Ordens(int quantas)
    {
        var lista = new List<OrdemDeServico>();
        for (var n = 1; n <= quantas; n++)
        {
            var dados = new DadosDaOrdem($"Serviço {n}: alvenaria do pavimento {n}", TipoDeExecutante.Terceiro, n % 3 == 0 ? "Equipe do João" : "Construtora Silva",
                null, null, null, [new ItemDaOrdem(null, "Alvenaria", "m²", 100, 30)], new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1).AddDays(n),
                FormaDePagamentoDaOrdem.PorMedicao, null, null, retencao: n % 2 == 0 ? 5 : 0);
            var os = OrdemDeServico.Nova(Guid.NewGuid(), n, dados, Agora);
            if (n % 4 != 0)
                os = os.Emitir(Agora).Aceitar(Agora);
            if (n % 4 == 1)
                os = os.Concluir(Agora.AddDays(n + 2)).MedirOQueFalta(Guid.NewGuid(), new DateOnly(2026, 10, 20), Agora).Conferir(Agora);
            lista.Add(os);
        }
        return lista;
    }

    private static byte[] Pdf(RelatorioDasOrdens r)
    {
        using var destino = new MemoryStream();
        new GeradorRelatorioDasOrdensPdf().Gerar(r, "Residência Silva", new DadosDaObra("Maria Silva", null, null), "01/11/2026 10:00", destino);
        return destino.ToArray();
    }

    private static int Paginas(byte[] pdf) => Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page\b").Count;

    [Theory]
    [InlineData(0, 1)]
    [InlineData(6, 1)]
    [InlineData(60, 4)]
    public void Pdf_UmaPaginaOuMais(int ordens, int paginas)
    {
        var pdf = Pdf(RelatorioDasOrdens.Montar(Ordens(ordens), new FiltroDoRelatorioDasOrdens(), new DateOnly(2026, 11, 1)));

        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        Paginas(pdf).Should().Be(paginas);
    }
}
