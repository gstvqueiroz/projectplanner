using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectPlanner.Core;

namespace ProjectPlanner.Data.Tests;

public sealed class FotosTests : TesteComArquivo
{
    private (Cronograma Projeto, Tarefa ComFotos, Tarefa SemFotos) Projeto()
    {
        var projeto = new Cronograma("Obra", Segunda);
        var comFotos = new Tarefa("Fundação", Segunda, 3);
        var semFotos = new Tarefa("Estrutura", Segunda, 2);
        projeto.AdicionarTarefa(comFotos);
        projeto.AdicionarTarefa(semFotos);
        comFotos.AdicionarFoto(new FotoTarefa(comFotos.Id, Segunda.AddDays(2), "exemplo://2", "Armadura"));
        comFotos.AdicionarFoto(new FotoTarefa(comFotos.Id, Segunda, "exemplo://1", "Escavação"));
        comFotos.AdicionarFoto(new FotoTarefa(comFotos.Id, Segunda.AddDays(4), "exemplo://3"));
        return (projeto, comFotos, semFotos);
    }

    [Fact]
    public void SalvarEAbrir_RecuperaAsFotosDeCadaTarefaEmOrdemDeData()
    {
        var (projeto, comFotos, _) = Projeto();
        Repositorio.Salvar(projeto, Caminho);

        var aberto = Repositorio.Abrir(Caminho);

        aberto.Tarefas[0].Fotos.Should().Equal(comFotos.Fotos);
        aberto.Tarefas[0].Fotos.Select(f => f.Descricao).Should().Equal("Escavação", "Armadura", null);
        aberto.Tarefas[1].Fotos.Should().BeEmpty();
    }

    [Fact]
    public void RemoverUmaFotoESalvar_ElaSaiDoArquivo()
    {
        var (projeto, comFotos, _) = Projeto();
        Repositorio.Salvar(projeto, Caminho);

        comFotos.RemoverFoto(comFotos.Fotos[0].Id);
        Repositorio.Salvar(projeto, Caminho);

        Repositorio.Abrir(Caminho).Tarefas[0].Fotos.Select(f => f.CaminhoArquivo).Should().Equal("exemplo://2", "exemplo://3");
    }

    [Fact]
    public void RemoverATarefaESalvar_AsFotosDelaSomem()
    {
        var (projeto, comFotos, _) = Projeto();
        projeto.RemoverTarefa(comFotos.Id);

        Repositorio.Salvar(projeto, Caminho);

        Contar("FotosTarefa").Should().Be(0);
    }

    [Fact]
    public void Banco_ApagarATarefa_ApagaAsFotosEmCascata()
    {
        var (projeto, comFotos, _) = Projeto();
        Repositorio.Salvar(projeto, Caminho);

        Executar("DELETE FROM Tarefas WHERE Id = $p0", comFotos.Id.ToString().ToUpperInvariant());

        Contar("FotosTarefa").Should().Be(0);
    }

    [Fact]
    public void Banco_RecusaFotoSemCaminho()
    {
        var (projeto, _, _) = Projeto();
        Repositorio.Salvar(projeto, Caminho);

        var semCaminho = () => Executar("UPDATE FotosTarefa SET CaminhoArquivo = '  '");

        semCaminho.Should().Throw<SqliteException>().WithMessage("*CK_FotosTarefa_CaminhoArquivo*");
    }

    [Fact]
    public void ArquivoDaVersaoAnterior_AbreSemFotosEGanhaATabela()
    {
        using (var contexto = ProjectPlannerDbContext.Criar(Caminho, criarSeNaoExistir: true))
            contexto.GetService<IMigrator>().Migrate("20260925222432_HierarquiaEMarcos");
        Executar("INSERT INTO Projetos (Id, Nome, DataInicio, DataCriacao, DataUltimaModificacao) VALUES ($p0, 'Antigo', $p1, $p1, $p1)",
            Guid.NewGuid().ToString().ToUpperInvariant(), "2026-09-28 00:00:00");

        var aberto = Repositorio.Abrir(Caminho);

        aberto.Tarefas.Should().BeEmpty();
        Tabelas().Should().Contain("FotosTarefa");
    }
}
