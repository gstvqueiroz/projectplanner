using ProjectPlanner.Core;
using ProjectPlanner.Core.Indicadores;

namespace ProjectPlanner.Data.Tests;

// Bloco 37: os custos realizados no .pplan.
public sealed class CustosNoArquivoTests : TesteComArquivo
{
    private static (Cronograma C, Tarefa A) Obra()
    {
        var c = new Cronograma("Obra", Segunda);
        var a = new Tarefa("Alvenaria", Segunda, 5);
        c.AdicionarTarefa(a);
        a.DefinirValor(3000);
        return (c, a);
    }

    [Fact]
    public void Custos_VoltamIguais_ESemCustosNaoGrava()
    {
        var (c, a) = Obra();
        Repositorio.Salvar(c, Caminho);
        Contar("CustosDoProjeto").Should().Be(0);

        c.Custos.Adicionar(new LancamentoDeCusto(Guid.NewGuid(), new DateOnly(2026, 10, 1), a.Id, CategoriaDeCusto.MaoDeObra, "Pedreiros", "FP 09", 1234.56m));
        c.Custos.Adicionar(new LancamentoDeCusto(Guid.NewGuid(), new DateOnly(2026, 9, 30), null, CategoriaDeCusto.Terceiros, "Topografia", null, 800));
        Repositorio.Salvar(c, Caminho);
        var aberto = Repositorio.Abrir(Caminho);

        aberto.Custos.Lancamentos.Should().Equal(c.Custos.Lancamentos);
        aberto.Custos.Total.Should().Be(2034.56m);
    }

    [Theory]
    [InlineData(1, "{ ruim")]
    [InlineData(1, """{"lancamentos":[{"id":"00000000-0000-0000-0000-000000000000","data":"2026-10-01","categoria":"Material","descricao":"x","valor":1}]}""")]
    [InlineData(1, """{"lancamentos":[{"id":"6f9619ff-8b86-d011-b42d-00cf4fc964ff","data":"2026-10-01","categoria":"Material","descricao":"x","valor":-5}]}""")]
    [InlineData(2, """{"lancamentos":[]}""")]
    public void CustosComDefeito_ArquivoRecusado(int versao, string dados)
    {
        var (c, _) = Obra();
        c.Custos.Adicionar(new LancamentoDeCusto(Guid.NewGuid(), new DateOnly(2026, 10, 1), null, CategoriaDeCusto.Outros, "x", null, 1));
        Repositorio.Salvar(c, Caminho);
        Executar("UPDATE CustosDoProjeto SET Versao = $p0, Dados = $p1", versao, dados);

        var abrir = () => Repositorio.Abrir(Caminho);

        abrir.Should().Throw<InvalidDataException>();
    }
}
