using ProjectPlanner.App.Models;
using ProjectPlanner.Core;

namespace ProjectPlanner.App.Tests;

public class NotacaoPredecessorasTests
{
    [Theory]
    [InlineData("2", 2, TipoDependencia.FS, 0)]
    [InlineData("2TI", 2, TipoDependencia.FS, 0)]
    [InlineData("3II+2", 3, TipoDependencia.SS, 2)]
    [InlineData("4TT-1", 4, TipoDependencia.FF, -1)]
    [InlineData("5IT+3d", 5, TipoDependencia.SF, 3)]
    [InlineData(" 12 ii + 4 d ", 12, TipoDependencia.SS, 4)]
    [InlineData("2+3", 2, TipoDependencia.FS, 3)]
    [InlineData("2TI−2", 2, TipoDependencia.FS, -2)]
    public void Interpretar_UmItem(string texto, int linha, TipoDependencia tipo, int lag)
    {
        NotacaoPredecessoras.Interpretar(texto).Should().Equal(new ItemPredecessora(linha, tipo, lag));
    }

    [Theory]
    [InlineData("2; 3II+2")]
    [InlineData("2, 3II+2")]
    [InlineData("2;3ii+2;")]
    public void Interpretar_VariosItens_AceitaPontoEVirgulaOuVirgula(string texto)
    {
        NotacaoPredecessoras.Interpretar(texto).Should().Equal(
            new ItemPredecessora(2, TipoDependencia.FS, 0),
            new ItemPredecessora(3, TipoDependencia.SS, 2));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(" ; ")]
    public void Interpretar_TextoVazio_NaoTemPredecessoras(string? texto)
    {
        NotacaoPredecessoras.Interpretar(texto).Should().BeEmpty();
    }

    [Theory]
    [InlineData("A")]
    [InlineData("2FS")]
    [InlineData("2XY")]
    [InlineData("TI2")]
    [InlineData("2II+")]
    [InlineData("2II+99999999999")]
    public void Interpretar_ForaDaNotacao_LancaFormatExceptionComExemplo(string texto)
    {
        var interpretar = () => NotacaoPredecessoras.Interpretar(texto);

        interpretar.Should().Throw<FormatException>().WithMessage("*Não entendi*2, 3II, 4TT+2, 5IT-1*");
    }

    [Fact]
    public void Formatar_UsaAFormaCanonica()
    {
        NotacaoPredecessoras.Formatar([
            new ItemPredecessora(2, TipoDependencia.FS, 0),
            new ItemPredecessora(2, TipoDependencia.FS, -1),
            new ItemPredecessora(3, TipoDependencia.SS, 2),
            new ItemPredecessora(4, TipoDependencia.FF, 0),
            new ItemPredecessora(5, TipoDependencia.SF, -3),
        ]).Should().Be("2; 2TI-1; 3II+2; 4TT; 5IT-3");
    }

    [Fact]
    public void FormatarEInterpretar_SaoInversos()
    {
        var itens = new[]
        {
            new ItemPredecessora(1, TipoDependencia.FF, 5),
            new ItemPredecessora(7, TipoDependencia.SF, -2),
        };

        NotacaoPredecessoras.Interpretar(NotacaoPredecessoras.Formatar(itens)).Should().Equal(itens);
    }
}
