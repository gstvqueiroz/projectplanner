using ProjectPlanner.Core;

namespace ProjectPlanner.Data.Tests;

public class SalvarEAbrirTests : TesteComArquivo
{
    [Fact]
    public void Salvar_ProjetoNovoVazio_CriaOArquivoEAbreIgual()
    {
        var projeto = new Cronograma("Projeto vazio", Segunda);

        Repositorio.Salvar(projeto, Caminho);
        var aberto = Repositorio.Abrir(Caminho);

        File.Exists(Caminho).Should().BeTrue();
        aberto.Id.Should().Be(projeto.Id);
        aberto.Nome.Should().Be("Projeto vazio");
        aberto.DataInicio.Should().Be(Segunda);
        aberto.Tarefas.Should().BeEmpty();
        aberto.Predecessoras.Should().BeEmpty();
    }

    [Fact]
    public void Salvar_RegistraCriacaoUmaVezEModificacaoACadaSalvamento()
    {
        var projeto = new Cronograma("Projeto", Segunda);
        var primeiroSalvamento = Relogio.Agora.UtcDateTime;

        Repositorio.Salvar(projeto, Caminho);
        Relogio.Avancar(TimeSpan.FromHours(2));
        Repositorio.Salvar(projeto, Caminho);
        var aberto = Repositorio.Abrir(Caminho);

        var segundoSalvamento = primeiroSalvamento.AddHours(2);
        projeto.DataCriacao.Should().Be(primeiroSalvamento);
        projeto.DataUltimaModificacao.Should().Be(segundoSalvamento);
        aberto.DataCriacao.Should().Be(primeiroSalvamento);
        aberto.DataUltimaModificacao.Should().Be(segundoSalvamento);
        aberto.DataUltimaModificacao!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void SalvarEAbrir_ComTarefas_PreservaCadaCampoEAOrdemDoCronograma()
    {
        // Adicionadas fora da ordem cronológica, para provar que a ordem vem do cronograma.
        var projeto = new Cronograma("Obra", Segunda);
        var estrutura = new Tarefa("Estrutura", new DateTime(2026, 10, 5), 12);
        var acabamento = new Tarefa("Acabamento", new DateTime(2026, 10, 21), 1);
        var fundacao = new Tarefa("Fundação", Segunda, 5);
        estrutura.AtualizarPercentualConcluido(35);
        fundacao.AtualizarPercentualConcluido(100);
        foreach (var tarefa in new[] { estrutura, acabamento, fundacao })
            projeto.AdicionarTarefa(tarefa);

        Repositorio.Salvar(projeto, Caminho);
        var aberto = Repositorio.Abrir(Caminho);

        aberto.Tarefas.Select(t => t.Nome).Should().Equal("Estrutura", "Acabamento", "Fundação");
        aberto.Tarefas.Should().BeEquivalentTo(projeto.Tarefas, o => o.WithStrictOrdering());
    }

    [Fact]
    public void SalvarEAbrir_ComOsQuatroTiposDeVinculoELagELead_ReconstroiOGrafo()
    {
        var projeto = new Cronograma("Projeto", Segunda);
        var (a, b, c, d, e) = (new Tarefa("A", Segunda, 3), new Tarefa("B", Segunda, 2),
            new Tarefa("C", Segunda, 4), new Tarefa("D", Segunda, 1), new Tarefa("E", Segunda, 2));
        foreach (var tarefa in new[] { a, b, c, d, e })
            projeto.AdicionarTarefa(tarefa);
        projeto.AdicionarPredecessora(new Predecessora(a.Id, b.Id));
        projeto.AdicionarPredecessora(new Predecessora(a.Id, c.Id, TipoDependencia.SS, 2));
        projeto.AdicionarPredecessora(new Predecessora(b.Id, d.Id, TipoDependencia.FF, -1));
        projeto.AdicionarPredecessora(new Predecessora(c.Id, d.Id, TipoDependencia.SF, 3));
        projeto.AdicionarPredecessora(new Predecessora(d.Id, e.Id, TipoDependencia.FS, -2));

        Repositorio.Salvar(projeto, Caminho);
        var aberto = Repositorio.Abrir(Caminho);

        aberto.Predecessoras.Should().Equal(projeto.Predecessoras);
        aberto.PredecessorasDe(d.Id).Should().Equal(
            new Predecessora(b.Id, d.Id, TipoDependencia.FF, -1),
            new Predecessora(c.Id, d.Id, TipoDependencia.SF, 3));
        aberto.PredecessorasDe(a.Id).Should().BeEmpty();

        // O grafo reconstruído continua sob as regras do domínio.
        var fecharCiclo = () => aberto.AdicionarPredecessora(new Predecessora(e.Id, a.Id));
        fecharCiclo.Should().Throw<CircularDependencyException>();
    }

    [Fact]
    public void Abrir_NaoCalculaOCaminhoCritico()
    {
        var projeto = new Cronograma("Projeto", Segunda);
        projeto.AdicionarTarefa(new Tarefa("A", Segunda, 3));
        CalculadoraCronograma.Calcular(projeto);

        Repositorio.Salvar(projeto, Caminho);
        var tarefa = Repositorio.Abrir(Caminho).Tarefas.Single();

        tarefa.InicioMaisCedo.Should().BeNull();
        tarefa.Folga.Should().BeNull();
    }

    [Fact]
    public void Abrir_ArquivoJaAtualizado_NaoAlteraOArquivo()
    {
        Repositorio.Salvar(new Cronograma("Projeto", Segunda), Caminho);
        var antes = File.ReadAllBytes(Caminho);

        Repositorio.Abrir(Caminho);

        File.ReadAllBytes(Caminho).Should().Equal(antes);
    }

    [Fact]
    public void Salvar_SobreOutroProjeto_SubstituiTodoOConteudo()
    {
        var primeiro = new Cronograma("Primeiro", Segunda);
        var (a, b) = (new Tarefa("A", Segunda, 1), new Tarefa("B", Segunda, 1));
        primeiro.AdicionarTarefa(a);
        primeiro.AdicionarTarefa(b);
        primeiro.AdicionarPredecessora(new Predecessora(a.Id, b.Id));
        Repositorio.Salvar(primeiro, Caminho);
        var segundo = new Cronograma("Segundo", Segunda);
        segundo.AdicionarTarefa(new Tarefa("X", Segunda, 1));

        Repositorio.Salvar(segundo, Caminho);
        var aberto = Repositorio.Abrir(Caminho);

        aberto.Id.Should().Be(segundo.Id);
        aberto.Tarefas.Select(t => t.Nome).Should().Equal("X");
        Contar("Projetos").Should().Be(1);
        Contar("Tarefas").Should().Be(1);
        Contar("Predecessoras").Should().Be(0);
    }

    [Fact]
    public void Salvar_DeNovoDepoisDeAlterar_GravaOEstadoAtual()
    {
        var projeto = new Cronograma("Projeto", Segunda);
        var tarefa = new Tarefa("A", Segunda, 3);
        projeto.AdicionarTarefa(tarefa);
        Repositorio.Salvar(projeto, Caminho);

        tarefa.AlterarDuracao(8);
        tarefa.AtualizarPercentualConcluido(50);
        Repositorio.Salvar(projeto, Caminho);
        var aberta = Repositorio.Abrir(Caminho).Tarefas.Single();

        aberta.Duracao.Should().Be(8);
        aberta.DataTermino.Should().Be(tarefa.DataTermino);
        aberta.PercentualConcluido.Should().Be(50);
    }

    [Fact]
    public void Salvar_SobreArquivoQueNaoEhProjeto_Sobrescreve()
    {
        File.WriteAllText(Caminho, "isto não é um banco de dados");

        Repositorio.Salvar(new Cronograma("Projeto", Segunda), Caminho);

        Repositorio.Abrir(Caminho).Nome.Should().Be("Projeto");
    }

    [Fact]
    public void Salvar_NaoDeixaArquivosTemporariosNaPasta()
    {
        Repositorio.Salvar(new Cronograma("Projeto", Segunda), Caminho);
        Repositorio.Salvar(new Cronograma("Projeto", Segunda), Caminho);

        Directory.GetFiles(Pasta).Should().Equal(Caminho);
    }

    [Fact]
    public void Salvar_EmPastaInexistente_LancaDirectoryNotFoundException()
    {
        var caminho = Path.Combine(Pasta, "nao-existe", "projeto.sqlite");

        var salvar = () => Repositorio.Salvar(new Cronograma("Projeto", Segunda), caminho);

        salvar.Should().Throw<DirectoryNotFoundException>();
    }
}
