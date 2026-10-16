using ProjectPlanner.Core;
using ProjectPlanner.Core.Orcamento;

namespace ProjectPlanner.Data.Tests;

// Bloco 33: o orçamento no .pplan.
public sealed class OrcamentoNoArquivoTests : TesteComArquivo
{
    private static Cronograma Obra()
    {
        var c = new Cronograma("Obra", Segunda);
        var a = new Tarefa("Alvenaria", Segunda, 5);
        c.AdicionarTarefa(a);
        a.DefinirQuantidade(100);
        c.Orcamento.DefinirConfiguracao(new ConfiguracaoDoOrcamento { Bdi = 25.5m, BdiDeMateriais = 15 });
        c.Orcamento.Associar(a, new ComposicaoAberta(new BaseDeReferencia("SINAPI", "SP", "09/2026", false), "87495", "ALVENARIA", "M2",
            [new InsumoNaComposicao("88309", "PEDREIRO", "H", TipoDeInsumo.MaoDeObra, 0.8m, 28), new InsumoNaComposicao("7271", "BLOCO", "UN", TipoDeInsumo.Material, 13.5m, 1.2m)]));
        return c;
    }

    [Fact]
    public void Orcamento_VoltaIgual()
    {
        var c = Obra();

        Repositorio.Salvar(c, Caminho);
        var aberto = Repositorio.Abrir(Caminho);

        aberto.Orcamento.Configuracao.Should().Be(c.Orcamento.Configuracao);
        var (antes, depois) = (c.Orcamento.Servicos.Single().Value, aberto.Orcamento.Servicos.Single().Value);
        (depois.Base, depois.Codigo, depois.Unidade, depois.CustoUnitario).Should().Be((antes.Base, "87495", "M2", antes.CustoUnitario));
        depois.Insumos.Should().Equal(antes.Insumos);
        aberto.Orcamento.Planilha().Total.Should().Be(c.Orcamento.Planilha().Total);
    }

    [Theory]
    [InlineData(1, "{ ruim")]
    [InlineData(1, """{"bdi":150}""")]
    [InlineData(2, """{}""")]
    public void OrcamentoComDefeito_ArquivoRecusado(int versao, string dados)
    {
        Repositorio.Salvar(Obra(), Caminho);
        Executar("UPDATE OrcamentoDoProjeto SET Versao = $p0, Dados = $p1", versao, dados);

        ((Action)(() => Repositorio.Abrir(Caminho))).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void SemOrcamento_NadaGravado()
    {
        var c = new Cronograma("Vazio", Segunda);
        Repositorio.Salvar(c, Caminho);
        Contar("OrcamentoDoProjeto").Should().Be(0);
    }
}
