using ProjectPlanner.Core.Gantt;

namespace ProjectPlanner.Core.Tests.Gantt;

public class InferenciaDependenciaTests
{
    [Theory]
    [InlineData(BordaDaBarra.Termino, BordaDaBarra.Inicio, TipoDependencia.FS)]
    [InlineData(BordaDaBarra.Inicio, BordaDaBarra.Inicio, TipoDependencia.SS)]
    [InlineData(BordaDaBarra.Termino, BordaDaBarra.Termino, TipoDependencia.FF)]
    [InlineData(BordaDaBarra.Inicio, BordaDaBarra.Termino, TipoDependencia.SF)]
    public void CadaCombinacaoDeBordas_DaOTipoQueLigaOsDoisPontos(BordaDaBarra origem, BordaDaBarra destino, TipoDependencia tipo)
    {
        InferenciaDependencia.Tipo(origem, destino).Should().Be(tipo);
    }

    [Fact]
    public void AsQuatroCombinacoes_DaoOsQuatroTipos()
    {
        var tipos = from origem in Enum.GetValues<BordaDaBarra>()
                    from destino in Enum.GetValues<BordaDaBarra>()
                    select InferenciaDependencia.Tipo(origem, destino);

        tipos.Should().BeEquivalentTo(Enum.GetValues<TipoDependencia>());
    }

    [Fact]
    public void BordaInexistente_Recusada()
    {
        var inferir = () => InferenciaDependencia.Tipo((BordaDaBarra)7, BordaDaBarra.Inicio);

        inferir.Should().Throw<ArgumentOutOfRangeException>();
    }
}
