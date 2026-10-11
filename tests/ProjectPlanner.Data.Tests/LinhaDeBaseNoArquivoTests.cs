using ProjectPlanner.Core;
using ProjectPlanner.Core.Planejamento;

namespace ProjectPlanner.Data.Tests;

// Bloco 31: restrição, prazo, valor e linhas de base no .pplan.
public sealed class LinhaDeBaseNoArquivoTests : TesteComArquivo
{
    private static readonly DateTime Agora = new(2026, 9, 28, 10, 0, 0);

    private static (Cronograma C, Tarefa A, Tarefa Fase) Obra()
    {
        var c = new Cronograma("Obra", Segunda);
        var fase = new Tarefa("Fase", Segunda, 1);
        var a = new Tarefa("A", Segunda, 3);
        var b = new Tarefa("B", Segunda, 2);
        c.AdicionarTarefa(fase);
        c.AdicionarTarefa(a, fase.Id);
        c.AdicionarTarefa(b, fase.Id);
        a.DefinirRestricaoDeInicio(Segunda.AddDays(7));
        a.DefinirPrazo(Segunda.AddDays(11));
        a.DefinirValor(1234.56m);
        b.DefinirValor(100m);
        fase.DefinirPrazo(Segunda.AddDays(30));
        CalculadoraCronograma.Calcular(c);
        return (c, a, fase);
    }

    [Fact]
    public void RestricaoPrazoEValor_VoltamIguais_EOValorDoResumoESoma()
    {
        var (c, a, fase) = Obra();

        Repositorio.Salvar(c, Caminho);
        var aberto = Repositorio.Abrir(Caminho);
        CalculadoraCronograma.Calcular(aberto);

        var (aa, ff) = (aberto.Tarefas.Single(t => t.Id == a.Id), aberto.Tarefas.Single(t => t.Id == fase.Id));
        (aa.NaoIniciarAntesDe, aa.Prazo, aa.Valor, aa.InicioMaisCedo).Should().Be((a.NaoIniciarAntesDe, a.Prazo, 1234.56m, Segunda.AddDays(7)));
        (ff.Prazo, ff.NaoIniciarAntesDe, ff.Valor).Should().Be((fase.Prazo, null, 1334.56m));
    }

    [Fact]
    public void LinhasDeBase_VoltamIguais_ComAEmUso_ESemNenhumaNaoGrava()
    {
        var (c, a, _) = Obra();
        Repositorio.Salvar(c, Caminho);
        Contar("LinhasDeBaseDoProjeto").Should().Be(0);

        c.SalvarLinhaDeBase("Contrato", Agora);
        c.SalvarLinhaDeBase(null, Agora.AddDays(5));
        c.UsarLinhaDeBase(0);
        Repositorio.Salvar(c, Caminho);
        var aberto = Repositorio.Abrir(Caminho);

        aberto.LinhasDeBase.Select(l => l.Descricao).Should().Equal(c.LinhasDeBase.Select(l => l.Descricao));
        aberto.LinhaDeBaseEmUso!.Numero.Should().Be(0);
        aberto.LinhasDeBase[0].Tarefas[a.Id].Should().Be(c.LinhasDeBase[0].Tarefas[a.Id]);
        aberto.LinhasDeBase[0].Tarefas.Should().HaveCount(3);
    }

    [Fact]
    public void LinhasDeBase_SemNenhumaEmUso()
    {
        var (c, _, _) = Obra();
        c.SalvarLinhaDeBase(null, Agora);
        c.UsarLinhaDeBase(null);

        Repositorio.Salvar(c, Caminho);

        Repositorio.Abrir(Caminho).Should().Match<Cronograma>(x => x.LinhasDeBase.Count == 1 && x.LinhaDeBaseEmUso == null);
    }

    [Theory]
    [InlineData(1, "{ ruim")]
    [InlineData(1, """{"emUso":0,"linhas":[{"numero":-1,"salvaEm":"2026-09-28T10:00:00","tarefas":[]}]}""")]
    [InlineData(3, """{"linhas":[]}""")]
    public void LinhasDeBaseComDefeito_ArquivoRecusado(int versao, string dados)
    {
        var (c, _, _) = Obra();
        c.SalvarLinhaDeBase(null, Agora);
        Repositorio.Salvar(c, Caminho);
        Executar("UPDATE LinhasDeBaseDoProjeto SET Versao = $p0, Dados = $p1", versao, dados);

        var abrir = () => Repositorio.Abrir(Caminho);

        abrir.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ArquivoDa0240_AbreSemRestricoesNemLinhaDeBase_EAsDatasNaoMudam()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Arquivos", "obra-0.24.0.pplan"), Caminho);

        var aberto = Repositorio.Abrir(Caminho);

        aberto.Tarefas.Should().OnlyContain(t => t.NaoIniciarAntesDe == null && t.Prazo == null && (t.EhResumo || t.Valor == null));
        (aberto.LinhasDeBase.Count, aberto.LinhaDeBaseEmUso).Should().Be((0, null));
    }
}
