using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectPlanner.Core;

namespace ProjectPlanner.Data;

/// <summary>
/// Guarda cada projeto num arquivo SQLite próprio.
/// </summary>
/// <remarks>
/// <para>
/// Salvar monta o arquivo inteiro do zero num arquivo temporário da mesma pasta e só então o põe no
/// lugar do destino. Assim, salvar sempre sobrescreve tudo (vence quem salvar por último, sem
/// controle de concorrência), e uma falha no meio nunca deixa o arquivo original pela metade.
/// </para>
/// <para>
/// Abrir aplica as migrations pendentes (arquivos de versões anteriores são atualizados no lugar) e
/// reconstrói o domínio pelos construtores e métodos normais, revalidando todas as regras. A coluna
/// DataTermino é gravada para quem ler o arquivo por fora; ao abrir, ela é recalculada a partir de
/// DataInicio e Duracao.
/// </para>
/// </remarks>
public sealed class SqliteProjetoRepository(TimeProvider? relogio = null) : IProjetoRepository
{
    private const int SqliteNaoEhBanco = 26; // SQLITE_NOTADB

    private readonly TimeProvider _relogio = relogio ?? TimeProvider.System;

    public void Salvar(Cronograma cronograma, string caminhoArquivo)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentException.ThrowIfNullOrWhiteSpace(caminhoArquivo);

        var destino = Path.GetFullPath(caminhoArquivo);
        var pasta = Path.GetDirectoryName(destino)!;
        if (!Directory.Exists(pasta))
            throw new DirectoryNotFoundException($"A pasta \"{pasta}\" não existe.");

        var agora = _relogio.GetUtcNow().UtcDateTime;
        var criacao = cronograma.DataCriacao ?? agora;
        var temporario = Path.Combine(pasta, $".{Path.GetFileName(destino)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var contexto = ProjectPlannerDbContext.Criar(temporario, criarSeNaoExistir: true))
            {
                contexto.Database.Migrate();
                Gravar(contexto, cronograma, criacao, agora);
                contexto.SaveChanges();
            }
            File.Move(temporario, destino, overwrite: true);
        }
        catch
        {
            ApagarSemFalhar(temporario);
            throw;
        }

        cronograma.DefinirDatasDoArquivo(criacao, agora);
    }

    public Cronograma Abrir(string caminhoArquivo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caminhoArquivo);
        if (!File.Exists(caminhoArquivo))
            throw new FileNotFoundException($"O arquivo de projeto \"{caminhoArquivo}\" não existe.", caminhoArquivo);

        try
        {
            using var contexto = ProjectPlannerDbContext.Criar(caminhoArquivo, criarSeNaoExistir: false);
            AtualizarSchemaSePreciso(contexto, caminhoArquivo);
            return Ler(contexto, caminhoArquivo);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteNaoEhBanco)
        {
            throw NaoEhProjeto(caminhoArquivo, ex);
        }
        catch (DomainValidationException ex)
        {
            throw new InvalidDataException(
                $"O arquivo \"{caminhoArquivo}\" tem dados inválidos: {ex.Message}", ex);
        }
    }

    private static void Gravar(ProjectPlannerDbContext contexto, Cronograma cronograma, DateTime criacao, DateTime agora)
    {
        contexto.Projetos.Add(new ProjetoRegistro
        {
            Id = cronograma.Id,
            Nome = cronograma.Nome,
            DataInicio = cronograma.DataInicio,
            DataCriacao = criacao,
            DataUltimaModificacao = agora,
        });

        contexto.Tarefas.AddRange(cronograma.Tarefas.Select((t, ordem) => new TarefaRegistro
        {
            Id = t.Id,
            ProjetoId = cronograma.Id,
            Ordem = ordem,
            Nome = t.Nome,
            Duracao = t.Duracao,
            DataInicio = t.DataInicio,
            DataTermino = t.DataTermino,
            PercentualConcluido = t.PercentualConcluido,
            Notas = t.Notas,
        }));

        contexto.Predecessoras.AddRange(cronograma.Predecessoras.Select((p, ordem) => new PredecessoraRegistro
        {
            Ordem = ordem,
            TarefaOrigemId = p.TarefaOrigemId,
            TarefaDestinoId = p.TarefaDestinoId,
            Tipo = p.Tipo,
            Lag = p.Lag,
        }));
    }

    // Um arquivo de projeto tem a migration inicial aplicada; se tiver alguma que esta versão não
    // conhece, foi salvo por uma versão mais nova e não pode ser aberto (nem atualizado) por esta.
    // Só migra quando falta alguma migration, para que abrir um arquivo atualizado não o altere.
    private static void AtualizarSchemaSePreciso(ProjectPlannerDbContext contexto, string caminhoArquivo)
    {
        var conhecidas = contexto.Database.GetMigrations().ToList();
        var aplicadas = contexto.Database.GetAppliedMigrations().ToList();

        if (!aplicadas.Contains(conhecidas[0]))
            throw NaoEhProjeto(caminhoArquivo);
        if (aplicadas.Except(conhecidas).Any())
            throw new InvalidDataException(
                $"O arquivo \"{caminhoArquivo}\" foi salvo por uma versão mais nova do ProjectPlanner.");
        if (conhecidas.Except(aplicadas).Any())
            contexto.Database.Migrate();
    }

    private static Cronograma Ler(ProjectPlannerDbContext contexto, string caminhoArquivo)
    {
        var projeto = contexto.Projetos.AsNoTracking().ToList() switch
        {
            [var unico] => unico,
            var projetos => throw new InvalidDataException(
                $"O arquivo \"{caminhoArquivo}\" deveria ter um projeto, mas tem {projetos.Count}."),
        };

        var cronograma = new Cronograma(projeto.Id, projeto.Nome, projeto.DataInicio);
        cronograma.DefinirDatasDoArquivo(
            DateTime.SpecifyKind(projeto.DataCriacao, DateTimeKind.Utc),
            DateTime.SpecifyKind(projeto.DataUltimaModificacao, DateTimeKind.Utc));

        foreach (var registro in contexto.Tarefas.AsNoTracking().OrderBy(t => t.Ordem))
        {
            var tarefa = new Tarefa(registro.Id, registro.Nome, registro.DataInicio, registro.Duracao);
            tarefa.AtualizarPercentualConcluido(registro.PercentualConcluido);
            tarefa.AlterarNotas(registro.Notas);
            cronograma.AdicionarTarefa(tarefa);
        }

        foreach (var registro in contexto.Predecessoras.AsNoTracking().OrderBy(p => p.Ordem))
            cronograma.AdicionarPredecessora(new Predecessora(
                registro.TarefaOrigemId, registro.TarefaDestinoId, registro.Tipo, registro.Lag));

        return cronograma;
    }

    private static InvalidDataException NaoEhProjeto(string caminhoArquivo, Exception? causa = null) =>
        new($"O arquivo \"{caminhoArquivo}\" não é um projeto do ProjectPlanner.", causa);

    private static void ApagarSemFalhar(string caminho)
    {
        try
        {
            File.Delete(caminho);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // O erro original é o que importa; um temporário esquecido não atrapalha.
        }
    }
}
