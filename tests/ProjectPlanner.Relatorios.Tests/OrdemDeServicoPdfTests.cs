using System.Text;
using System.Text.RegularExpressions;
using ProjectPlanner.Core.OrdensDeServico;
using ProjectPlanner.Core.Relatorios;

namespace ProjectPlanner.Relatorios.Tests;

// Bloco 58: a OS em PDF para assinar (uma página, ou mais com muitos itens).
public sealed class OrdemDeServicoPdfTests
{
    private static readonly DateTime Agora = new(2026, 10, 25, 9, 0, 0);

    internal static OrdemDeServico Ordem(int itens)
    {
        var tarefa = Guid.NewGuid();
        var dados = new DadosDaOrdem("Alvenaria de vedação do 1º pavimento", TipoDeExecutante.Terceiro, "Construtora Silva & Filhos", null, "(11) 99999-0000",
            [tarefa], Enumerable.Range(1, itens).Select(i => new ItemDaOrdem(i == 1 ? tarefa : null, $"Serviço {i}: alvenaria de blocos cerâmicos 9x19x19", "m²", 12.5m * i, 32.4m)),
            new DateOnly(2026, 10, 26), new DateOnly(2026, 11, 13), FormaDePagamentoDaOrdem.PorMedicao,
            "Usar capacete, luvas e cinto acima de 2 m.\nO material (blocos, cimento e areia) é fornecido pela obra.",
            [new ItemDeVerificacao("Prumo e nível conferidos"), new ItemDeVerificacao("Limpeza do local", true)]);
        return OrdemDeServico.Nova(Guid.NewGuid(), 7, dados, Agora).Emitir(Agora.AddHours(1)).Aceitar(Agora.AddHours(3), "João (pelo Telegram)");
    }

    internal static CabecalhoDaOrdem Cabecalho(OrdemDeServico o) => new("Residência Silva",
        new DadosDaObra("Maria Silva", "Contrato 12/2026", "Rua das Flores, 100 - São Paulo/SP"),
        new Dictionary<Guid, string> { [o.Dados.Tarefas[0]] = "2.3 Alvenaria" }, "25/10/2026 09:00");

    private static byte[] Pdf(OrdemDeServico o)
    {
        using var destino = new MemoryStream();
        new GeradorOrdemDeServicoPdf().Gerar(o, Cabecalho(o), destino);
        return destino.ToArray();
    }

    private static int Paginas(byte[] pdf) => Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page\b").Count;

    [Theory]
    [InlineData(3, 1)]
    [InlineData(60, 3)]
    public void Pdf_UmaPaginaOuMais(int itens, int paginas)
    {
        var pdf = Pdf(Ordem(itens));

        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        Paginas(pdf).Should().Be(paginas);
    }

    [Fact]
    public void Pdf_ComMedicoesEPagamentos_CabeNumaPagina()
    {
        var o = Ordem(3);
        o = o.Medir(Guid.NewGuid(), new DateOnly(2026, 10, 30), [12.5m, 10, 0], Agora).Medir(Guid.NewGuid(), new DateOnly(2026, 11, 6), [0, 15, 20], Agora);
        o = o.RegistrarPagamento(o.Medicoes[0].Id, new PagamentoDaMedicao(new DateOnly(2026, 11, 1), "NF 77", []), Agora);

        var pdf = Pdf(o);

        Paginas(pdf).Should().Be(1);
    }
}
