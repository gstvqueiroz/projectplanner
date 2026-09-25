namespace ProjectPlanner.Core.Tests;

public class PredecessoraTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    [Fact]
    public void Criar_SemTipoNemLag_UsaFSComLagZero()
    {
        var predecessora = new Predecessora(A, B);

        predecessora.TarefaOrigemId.Should().Be(A);
        predecessora.TarefaDestinoId.Should().Be(B);
        predecessora.Tipo.Should().Be(TipoDependencia.FS);
        predecessora.Lag.Should().Be(0);
    }

    [Theory]
    [InlineData(TipoDependencia.FS)]
    [InlineData(TipoDependencia.SS)]
    [InlineData(TipoDependencia.FF)]
    [InlineData(TipoDependencia.SF)]
    public void Criar_ComCadaTipo_GuardaOTipo(TipoDependencia tipo)
    {
        var predecessora = new Predecessora(A, B, tipo);

        predecessora.Tipo.Should().Be(tipo);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(-2)]
    [InlineData(0)]
    public void Criar_ComLagPositivoNegativoOuZero_GuardaOLag(int lag)
    {
        var predecessora = new Predecessora(A, B, TipoDependencia.SS, lag);

        predecessora.Lag.Should().Be(lag);
    }

    [Fact]
    public void TipoPadrao_EhFS()
    {
        default(TipoDependencia).Should().Be(TipoDependencia.FS);
    }

    [Fact]
    public void Criar_ComAMesmaTarefaNasDuasPontas_LancaExcecao()
    {
        var criar = () => new Predecessora(A, A);

        criar.Should().Throw<DomainValidationException>()
            .Which.Propriedade.Should().Be(nameof(Predecessora.TarefaOrigemId));
    }

    [Fact]
    public void Criar_ComOrigemVazia_LancaExcecao()
    {
        var criar = () => new Predecessora(Guid.Empty, B);

        criar.Should().Throw<DomainValidationException>()
            .Which.Propriedade.Should().Be(nameof(Predecessora.TarefaOrigemId));
    }

    [Fact]
    public void Criar_ComDestinoVazio_LancaExcecao()
    {
        var criar = () => new Predecessora(A, Guid.Empty);

        criar.Should().Throw<DomainValidationException>()
            .Which.Propriedade.Should().Be(nameof(Predecessora.TarefaDestinoId));
    }

    [Fact]
    public void Criar_ComTipoInexistente_LancaExcecao()
    {
        var criar = () => new Predecessora(A, B, (TipoDependencia)99);

        criar.Should().Throw<DomainValidationException>()
            .Which.Propriedade.Should().Be(nameof(Predecessora.Tipo));
    }

    [Fact]
    public void Igualdade_ComparaPorValor()
    {
        new Predecessora(A, B, TipoDependencia.FF, 1)
            .Should().Be(new Predecessora(A, B, TipoDependencia.FF, 1))
            .And.NotBe(new Predecessora(A, B, TipoDependencia.FF, 2));
    }
}
