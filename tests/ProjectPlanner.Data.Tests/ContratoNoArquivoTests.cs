using ProjectPlanner.Core;
using ProjectPlanner.Core.Medicoes;
using ProjectPlanner.Core.Planejamento;

namespace ProjectPlanner.Data.Tests;

// Bloco 36: unidade e quantidade das tarefas, e o contrato (configuração, aditivos e medições) no .pplan.
public sealed class ContratoNoArquivoTests : TesteComArquivo
{
    private static readonly DateTime Agora = new(2026, 10, 20, 9, 30, 0);

    private static DateOnly D(int dia, int mes = 10) => new(2026, mes, dia);

    private static (Cronograma C, Tarefa A, Tarefa B) Obra()
    {
        var c = new Cronograma("Obra", Segunda);
        var fase = new Tarefa("Fase", Segunda, 1);
        var a = new Tarefa("Alvenaria", Segunda, 5);
        var b = new Tarefa("Pintura", Segunda, 5);
        c.AdicionarTarefa(fase);
        c.AdicionarTarefa(a, fase.Id);
        c.AdicionarTarefa(b);
        a.DefinirValor(3000);
        a.DefinirUnidade("m²");
        a.DefinirQuantidade(120.125m);
        b.DefinirValor(1000);
        CalculadoraCronograma.Calcular(c);
        return (c, a, b);
    }

    [Fact]
    public void UnidadeEQuantidade_VoltamIguais()
    {
        var (c, a, b) = Obra();

        Repositorio.Salvar(c, Caminho);
        var aberto = Repositorio.Abrir(Caminho);

        var (aa, bb) = (aberto.Tarefas.Single(t => t.Id == a.Id), aberto.Tarefas.Single(t => t.Id == b.Id));
        (aa.Unidade, aa.Quantidade, bb.Unidade, bb.Quantidade).Should().Be(("m²", 120.125m, null, null));
        Contar("ContratoDoProjeto").Should().Be(0, "sem contrato, nada a gravar");
    }

    [Fact]
    public void Contrato_AditivosEMedicoes_VoltamIguais_ComOBoletimAprovado()
    {
        var (c, a, b) = Obra();
        c.Contrato.DefinirConfiguracao(new ConfiguracaoDoContrato
        {
            PercentualDeRetencao = 5, NomeDoIndice = "INCC-DI", IndiceBase = 1000.5m, Periodicidade = Periodicidade.DatasDeCorte, Cortes = [D(16), D(30)],
        });
        c.Contrato.RegistrarAditivo(D(1), "Ampliação", [new AlteracaoDoAditivo(a.Id, 10, 250)]);
        c.Contrato.NovaMedicao(D(28, 9), D(16));
        c.Contrato.Medir(1, a.Id, 40);
        c.Contrato.Medir(1, b.Id, 25.5m);
        c.Contrato.DefinirIndice(1, 1100);
        c.Contrato.DefinirGlosas(1, [new GlosaDaMedicao("Retrabalho", 50)]);
        c.Contrato.DefinirObservacoes(1, "Conferida em campo.");
        var aprovado = c.Contrato.Aprovar(1, Agora);
        c.Contrato.MarcarPaga(1, D(25));
        c.Contrato.NovaMedicao(D(17), D(30));
        c.Contrato.Medir(2, a.Id, 1);

        Repositorio.Salvar(c, Caminho);
        var aberto = Repositorio.Abrir(Caminho).Contrato;

        aberto.Configuracao.Should().BeEquivalentTo(c.Contrato.Configuracao);
        aberto.Aditivos.Select(x => x.Rotulo).Should().Equal(c.Contrato.Aditivos.Select(x => x.Rotulo));
        aberto.Aditivos[0].Itens.Should().Equal(c.Contrato.Aditivos[0].Itens);
        var paga = aberto.ObterMedicao(1);
        (paga.Situacao, paga.TextoDaSituacao, paga.AprovadaEm, paga.Indice, paga.Observacoes).Should()
            .Be((SituacaoDaMedicao.Paga, "Paga em 25/10/2026", Agora, 1100m, "Conferida em campo."));
        paga.Glosas.Should().Equal(c.Contrato.ObterMedicao(1).Glosas);
        paga.Linhas.Should().Equal(aprovado.Linhas);
        paga.Resumo.Should().Be(aprovado.Resumo);
        aberto.ObterMedicao(2).Itens.Should().Equal(new ItemMedido(a.Id, 1));
        aberto.Boletim(2).Resumo.MedidoAnterior.Should().Be(aprovado.Resumo.MedidoNoPeriodo);
    }

    [Theory]
    [InlineData(1, "{ ruim")]
    [InlineData(1, """{"medicoes":[{"numero":1,"de":"2026-10-01","ate":"2026-10-10","situacao":"Aprovada"}]}""")]
    [InlineData(1, """{"configuracao":{"retencao":40,"periodicidade":"Mensal"}}""")]
    [InlineData(1, """{"medicoes":[{"numero":1,"de":"2026-10-01","ate":"2026-10-10","situacao":"Rascunho"},{"numero":2,"de":"2026-10-11","ate":"2026-10-20","situacao":"Rascunho"}]}""")]
    [InlineData(2, """{}""")]
    public void ContratoComDefeito_ArquivoRecusado(int versao, string dados)
    {
        var (c, _, _) = Obra();
        c.Contrato.DefinirConfiguracao(new ConfiguracaoDoContrato { PercentualDeRetencao = 5 });
        Repositorio.Salvar(c, Caminho);
        Executar("UPDATE ContratoDoProjeto SET Versao = $p0, Dados = $p1", versao, dados);

        var abrir = () => Repositorio.Abrir(Caminho);

        abrir.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ArquivoDa0240_AbreSemUnidadeNemContrato()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Arquivos", "obra-0.24.0.pplan"), Caminho);

        var aberto = Repositorio.Abrir(Caminho);

        aberto.Tarefas.Should().OnlyContain(t => t.Unidade == null && t.Quantidade == null);
        aberto.Contrato.Vazio.Should().BeTrue();
    }
}
