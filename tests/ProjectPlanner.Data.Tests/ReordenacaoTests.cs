using ProjectPlanner.Core;

namespace ProjectPlanner.Data.Tests;

// Bloco 18: a ordem vem da posição na lista do cronograma e vai para a coluna Ordem (que já existia).
public sealed class ReordenacaoTests : TesteComArquivo
{
    [Fact]
    public void Reordenar_SalvarEAbrir_VoltaNaNovaOrdem()
    {
        var projeto = new Cronograma("Obra", Segunda);
        var fundacao = new Tarefa("Fundação", Segunda, 3);
        var estrutura = new Tarefa("Estrutura", Segunda, 2);
        var cobertura = new Tarefa("Cobertura", Segunda, 2);
        projeto.AdicionarTarefa(fundacao);
        projeto.AdicionarTarefa(new Tarefa("Escavar", Segunda, 1), fundacao.Id);
        projeto.AdicionarTarefa(new Tarefa("Armar", Segunda, 1), fundacao.Id);
        projeto.AdicionarTarefa(estrutura);
        projeto.AdicionarTarefa(cobertura);
        projeto.MoverEntreIrmas(cobertura.Id, 0);
        projeto.MoverEntreIrmas(projeto.FilhasDe(fundacao.Id)[1].Id, 0);

        Repositorio.Salvar(projeto, Caminho);
        var aberto = Repositorio.Abrir(Caminho);

        aberto.Tarefas.Select(t => $"{aberto.EdtDe(t.Id)} {t.Nome}")
            .Should().Equal("1 Cobertura", "2 Fundação", "2.1 Armar", "2.2 Escavar", "3 Estrutura");
    }

    [Fact]
    public void ReordenarDepoisDeSalvo_SalvarDeNovo_SobrescreveAOrdemAntiga()
    {
        var projeto = new Cronograma("Obra", Segunda);
        var (a, b) = (new Tarefa("A", Segunda, 1), new Tarefa("B", Segunda, 1));
        projeto.AdicionarTarefa(a);
        projeto.AdicionarTarefa(b);
        Repositorio.Salvar(projeto, Caminho);

        projeto.MoverEntreIrmas(b.Id, 0);
        Repositorio.Salvar(projeto, Caminho);

        Repositorio.Abrir(Caminho).Tarefas.Select(t => t.Nome).Should().Equal("B", "A");
    }
}
