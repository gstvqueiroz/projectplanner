using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectPlanner.Core;

namespace ProjectPlanner.Data.Tests;

public sealed class HierarquiaTests : TesteComArquivo
{
    // Árvore de referência com três níveis, marcos e vínculos entre ramos:
    //   Obra
    //     Fase 1
    //       A (3 dias, 100%)
    //       B (2 dias, depois de A)
    //       Fase 1 concluída (marco, depois de B)
    //     Fase 2
    //       C (4 dias, depois do marco)
    //   Entrega (marco, depois de C)
    private static Cronograma Referencia()
    {
        var projeto = new Cronograma("Obra da sede", Segunda);
        Tarefa Nova(string nome, int duracao = 1, Tarefa? pai = null)
        {
            var tarefa = new Tarefa(nome, Segunda, duracao);
            projeto.AdicionarTarefa(tarefa, pai?.Id);
            return tarefa;
        }
        void Ligar(Tarefa de, Tarefa para) => projeto.AdicionarPredecessora(new Predecessora(de.Id, para.Id));

        var obra = Nova("Obra");
        var fase1 = Nova("Fase 1", pai: obra);
        var a = Nova("A", 3, fase1);
        var b = Nova("B", 2, fase1);
        var marco = Nova("Fase 1 concluída", 0, fase1);
        var fase2 = Nova("Fase 2", pai: obra);
        var c = Nova("C", 4, fase2);
        var entrega = Nova("Entrega", 0);
        Ligar(a, b);
        Ligar(b, marco);
        Ligar(marco, c);
        Ligar(c, entrega);
        a.AtualizarPercentualConcluido(100);
        fase2.AlterarNotas("Depende da vistoria");
        CalculadoraCronograma.Calcular(projeto);
        return projeto;
    }

    private static List<(string Nome, TipoTarefa Tipo, string? Pai, int Nivel, string Edt)> Estrutura(Cronograma projeto)
    {
        var nomes = projeto.Tarefas.ToDictionary(t => t.Id, t => t.Nome);
        var edt = projeto.NumeracaoEdt();
        return projeto.Tarefas
            .Select(t => (t.Nome, t.Tipo, t.TarefaPaiId is { } pai ? nomes[pai] : null, projeto.NivelDe(t.Id), edt[t.Id]))
            .ToList();
    }

    private static List<(string Nome, DateTime Inicio, DateTime Termino, int Duracao, int Percentual, int? Folga)> Valores(Cronograma projeto) =>
        projeto.Tarefas.Select(t => (t.Nome, t.DataInicio, t.DataTermino, t.Duracao, t.PercentualConcluido, t.Folga)).ToList();

    [Fact]
    public void SalvarEAbrir_ReconstroiAArvoreComTiposNiveisEVinculos()
    {
        var original = Referencia();
        Repositorio.Salvar(original, Caminho);

        var aberto = Repositorio.Abrir(Caminho);

        Estrutura(aberto).Should().Equal(
            ("Obra", TipoTarefa.Resumo, null, 0, "1"),
            ("Fase 1", TipoTarefa.Resumo, "Obra", 1, "1.1"),
            ("A", TipoTarefa.Normal, "Fase 1", 2, "1.1.1"),
            ("B", TipoTarefa.Normal, "Fase 1", 2, "1.1.2"),
            ("Fase 1 concluída", TipoTarefa.Marco, "Fase 1", 2, "1.1.3"),
            ("Fase 2", TipoTarefa.Resumo, "Obra", 1, "1.2"),
            ("C", TipoTarefa.Normal, "Fase 2", 2, "1.2.1"),
            ("Entrega", TipoTarefa.Marco, null, 0, "2"));
        Estrutura(aberto).Should().Equal(Estrutura(original));
        aberto.Predecessoras.Should().Equal(original.Predecessoras);
        aberto.Tarefas.Single(t => t.Nome == "Fase 2").Notas.Should().Be("Depende da vistoria");
    }

    [Fact]
    public void SalvarEAbrir_RecalcularDaOsMesmosValoresDeAntes()
    {
        var original = Referencia();
        Repositorio.Salvar(original, Caminho);

        var aberto = Repositorio.Abrir(Caminho);
        CalculadoraCronograma.Calcular(aberto);

        Valores(aberto).Should().Equal(Valores(original));
        aberto.Tarefas.Single(t => t.Nome == "Obra").PercentualConcluido.Should().Be(33);
    }

    [Fact]
    public void Salvar_GravaTipoEPaiDeCadaTarefaEmPreOrdem()
    {
        Repositorio.Salvar(Referencia(), Caminho);

        using var contexto = ProjectPlannerDbContext.Criar(Caminho, criarSeNaoExistir: false);
        var registros = contexto.Tarefas.OrderBy(t => t.Ordem).ToList();
        var nomes = registros.ToDictionary(r => r.Id, r => r.Nome);
        registros.Select(r => (r.Nome, r.Tipo, r.TarefaPaiId is { } pai ? nomes[pai] : null, r.Duracao)).Should().Equal(
            ("Obra", TipoTarefa.Resumo, null, 9),
            ("Fase 1", TipoTarefa.Resumo, "Obra", 5),
            ("A", TipoTarefa.Normal, "Fase 1", 3),
            ("B", TipoTarefa.Normal, "Fase 1", 2),
            ("Fase 1 concluída", TipoTarefa.Marco, "Fase 1", 0),
            ("Fase 2", TipoTarefa.Resumo, "Obra", 4),
            ("C", TipoTarefa.Normal, "Fase 2", 4),
            ("Entrega", TipoTarefa.Marco, null, 0));
    }

    [Fact]
    public void ResumoSoComMarcos_TemDuracaoZeroNoArquivoEAbreNormalmente()
    {
        var projeto = new Cronograma("Marcos", Segunda);
        var resumo = new Tarefa("Marcos do mês", Segunda, 1);
        projeto.AdicionarTarefa(resumo);
        projeto.AdicionarTarefa(new Tarefa("M1", Segunda, 0), resumo.Id);
        CalculadoraCronograma.Calcular(projeto);
        Repositorio.Salvar(projeto, Caminho);

        var aberto = Repositorio.Abrir(Caminho);
        CalculadoraCronograma.Calcular(aberto);

        var lido = aberto.Tarefas[0];
        (lido.Tipo, lido.Duracao).Should().Be((TipoTarefa.Resumo, 0));
        aberto.Tarefas[1].Tipo.Should().Be(TipoTarefa.Marco);
    }

    [Fact]
    public void ArquivoDaVersaoAnterior_AbreComTudoNoPrimeiroNivelESemPerderOsVinculos()
    {
        // Um arquivo do Bloco 10: sem Tipo nem TarefaPaiId, com um vínculo entre as tarefas.
        using (var contexto = ProjectPlannerDbContext.Criar(Caminho, criarSeNaoExistir: true))
            contexto.GetService<IMigrator>().Migrate("20260925200921_NotasDaTarefa");
        var (projetoId, a, b) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        string Id(Guid id) => id.ToString().ToUpperInvariant();
        Executar("INSERT INTO Projetos (Id, Nome, DataInicio, DataCriacao, DataUltimaModificacao) VALUES ($p0, 'Antigo', $p1, $p1, $p1)",
            Id(projetoId), "2026-09-28 00:00:00");
        const string insereTarefa = "INSERT INTO Tarefas (Id, ProjetoId, Ordem, Nome, Duracao, DataInicio, DataTermino, PercentualConcluido) " +
                                    "VALUES ($p0, $p1, $p2, $p3, 2, '2026-09-28 00:00:00', '2026-09-29 00:00:00', 0)";
        Executar(insereTarefa, Id(a), Id(projetoId), 0, "A");
        Executar(insereTarefa, Id(b), Id(projetoId), 1, "B");
        Executar("INSERT INTO Predecessoras (Ordem, TarefaOrigemId, TarefaDestinoId, Tipo, Lag) VALUES (0, $p0, $p1, 0, 0)", Id(a), Id(b));

        var aberto = Repositorio.Abrir(Caminho);

        aberto.Tarefas.Select(t => (t.Nome, t.Tipo, t.TarefaPaiId)).Should().Equal(
            ("A", TipoTarefa.Normal, (Guid?)null), ("B", TipoTarefa.Normal, null));
        aberto.Predecessoras.Should().Equal(new Predecessora(a, b));
        Contar("Predecessoras").Should().Be(1);
    }

    [Fact]
    public void Banco_ApagarUmResumo_ApagaAsSubtarefasEOsVinculosDelas()
    {
        Repositorio.Salvar(Referencia(), Caminho);
        using var contexto = ProjectPlannerDbContext.Criar(Caminho, criarSeNaoExistir: false);
        var fase1 = contexto.Tarefas.Single(t => t.Nome == "Fase 1").Id;

        Executar("DELETE FROM Tarefas WHERE Id = $p0", fase1.ToString().ToUpperInvariant());

        Contar("Tarefas").Should().Be(4);       // Obra, Fase 2, C, Entrega
        Contar("Predecessoras").Should().Be(1); // C → Entrega
    }

    [Fact]
    public void Banco_RecusaTipoIncoerenteComADuracaoETarefaPaiDelaMesma()
    {
        Repositorio.Salvar(Referencia(), Caminho);
        using var contexto = ProjectPlannerDbContext.Criar(Caminho, criarSeNaoExistir: false);
        var a = contexto.Tarefas.Single(t => t.Nome == "A").Id.ToString().ToUpperInvariant();

        var marcoComDuracao = () => Executar("UPDATE Tarefas SET Tipo = 1 WHERE Id = $p0", a);
        var tipoInexistente = () => Executar("UPDATE Tarefas SET Tipo = 3 WHERE Id = $p0", a);
        var paiDelaMesma = () => Executar("UPDATE Tarefas SET TarefaPaiId = Id WHERE Id = $p0", a);

        marcoComDuracao.Should().Throw<SqliteException>().WithMessage("*CK_Tarefas_Duracao*");
        // Tipo 3 fura mais de um CHECK (nenhuma regra de duração o aceita); vale a recusa.
        tipoInexistente.Should().Throw<SqliteException>().WithMessage("*CHECK constraint failed*");
        paiDelaMesma.Should().Throw<SqliteException>().WithMessage("*CK_Tarefas_PaiDiferenteDelaMesma*");
    }

    [Fact]
    public void ArquivoComPaiDepoisDaSubtarefa_EhInvalido()
    {
        Repositorio.Salvar(Referencia(), Caminho);
        using (var contexto = ProjectPlannerDbContext.Criar(Caminho, criarSeNaoExistir: false))
        {
            var obra = contexto.Tarefas.Single(t => t.Nome == "Obra").Id.ToString().ToUpperInvariant();
            var entrega = contexto.Tarefas.Single(t => t.Nome == "Entrega").Id.ToString().ToUpperInvariant();
            Executar("UPDATE Tarefas SET TarefaPaiId = $p0 WHERE Id = $p1", entrega, obra);
        }

        var abrir = () => Repositorio.Abrir(Caminho);

        abrir.Should().Throw<InvalidDataException>().WithMessage("*dados inválidos*");
    }

    [Fact]
    public void ArquivoComSubtarefaDentroDeUmMarco_EhInvalido()
    {
        Repositorio.Salvar(Referencia(), Caminho);
        using (var contexto = ProjectPlannerDbContext.Criar(Caminho, criarSeNaoExistir: false))
        {
            var marco = contexto.Tarefas.Single(t => t.Nome == "Fase 1 concluída").Id.ToString().ToUpperInvariant();
            var b = contexto.Tarefas.Single(t => t.Nome == "B").Id.ToString().ToUpperInvariant();
            // Troca a ordem de B e do marco, e põe B dentro do marco.
            Executar("UPDATE Tarefas SET Ordem = -1 WHERE Id = $p0", b);
            Executar("UPDATE Tarefas SET Ordem = 3 WHERE Id = $p0", marco);
            Executar("UPDATE Tarefas SET Ordem = 4, TarefaPaiId = $p0 WHERE Id = $p1", marco, b);
        }

        var abrir = () => Repositorio.Abrir(Caminho);

        abrir.Should().Throw<InvalidDataException>().WithMessage("*marcos não têm subtarefas*");
    }

    [Fact]
    public void ArquivoComVinculoEmUmResumo_EhInvalido()
    {
        Repositorio.Salvar(Referencia(), Caminho);
        using (var contexto = ProjectPlannerDbContext.Criar(Caminho, criarSeNaoExistir: false))
        {
            var fase2 = contexto.Tarefas.Single(t => t.Nome == "Fase 2").Id.ToString().ToUpperInvariant();
            var entrega = contexto.Tarefas.Single(t => t.Nome == "Entrega").Id.ToString().ToUpperInvariant();
            Executar("INSERT INTO Predecessoras (Ordem, TarefaOrigemId, TarefaDestinoId, Tipo, Lag) VALUES (9, $p0, $p1, 0, 0)",
                fase2, entrega);
        }

        var abrir = () => Repositorio.Abrir(Caminho);

        abrir.Should().Throw<InvalidDataException>().WithMessage("*resumos não têm predecessoras nem sucessoras*");
    }
}
