using ProjectPlanner.Core;

namespace ProjectPlanner.Data;

// Linhas das tabelas, separadas das entidades de domínio: o EF só enxerga estas classes, e o
// domínio continua sem nenhuma dependência de persistência. O mapeamento fica em SqliteProjetoRepository.

internal sealed class ProjetoRegistro
{
    public Guid Id { get; set; }
    public required string Nome { get; set; }
    public DateTime DataInicio { get; set; }
    public DateTime DataCriacao { get; set; }
    public DateTime DataUltimaModificacao { get; set; }
}

internal sealed class TarefaRegistro
{
    public Guid Id { get; set; }
    public Guid ProjetoId { get; set; }
    public int Ordem { get; set; }
    public required string Nome { get; set; }
    public int Duracao { get; set; }
    public DateTime DataInicio { get; set; }
    public DateTime DataTermino { get; set; }
    public int PercentualConcluido { get; set; }
    public string? Notas { get; set; }

    /// <summary>Gravado para quem ler o arquivo por fora; ao abrir, o tipo vem da estrutura e da duração.</summary>
    public TipoTarefa Tipo { get; set; }

    /// <summary>O resumo em que a tarefa está; nulo no primeiro nível.</summary>
    public Guid? TarefaPaiId { get; set; }
}

internal sealed class PredecessoraRegistro
{
    public int Id { get; set; }
    public int Ordem { get; set; }
    public Guid TarefaOrigemId { get; set; }
    public Guid TarefaDestinoId { get; set; }
    public TipoDependencia Tipo { get; set; }
    public int Lag { get; set; }
}

internal sealed class FotoRegistro
{
    public Guid Id { get; set; }
    public Guid TarefaId { get; set; }
    public int Ordem { get; set; }
    public DateTime DataRegistro { get; set; }

    /// <summary>Provisório: por enquanto só identificadores fictícios (ver <see cref="FotoTarefa"/>).</summary>
    public required string CaminhoArquivo { get; set; }

    public string? Descricao { get; set; }
}
