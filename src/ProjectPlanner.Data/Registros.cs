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

    /// <summary>Bloco 31: "não iniciar antes de"; nulo sem restrição.</summary>
    public DateTime? NaoIniciarAntesDe { get; set; }

    /// <summary>Bloco 31: "não terminar depois de"; nulo sem prazo.</summary>
    public DateTime? Prazo { get; set; }

    /// <summary>Bloco 35: o valor em reais (só nas tarefas que não são resumo; o do resumo é a soma).</summary>
    public decimal? Valor { get; set; }

    /// <summary>Bloco 36: a unidade do item (m², m³…); nula sem unidade e nos resumos.</summary>
    public string? Unidade { get; set; }

    /// <summary>Bloco 36: a quantidade contratada; nula sem quantidade (medida em %) e nos resumos.</summary>
    public decimal? Quantidade { get; set; }
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
    public DateTime DataFoto { get; set; }
    public DateTime DataCadastro { get; set; }
    public string? Titulo { get; set; }
    public string? Descricao { get; set; }
    public string? Autor { get; set; }
    public string? Local { get; set; }
    public int? PercentualConcluidoNaData { get; set; }
    public required string NomeArquivoOriginal { get; set; }
    public int Largura { get; set; }
    public int Altura { get; set; }
    public long TamanhoBytes { get; set; }
    public required string HashOriginal { get; set; }

    /// <summary>As tags em JSON (["antes","problema"]); nulo sem tags.</summary>
    public string? Tags { get; set; }

    /// <summary>Bloco 27: entra no relatório fotográfico (as fotos de antes dele entram).</summary>
    public bool NoRelatorio { get; set; } = true;

    /// <summary>Bloco 27: destaque no relatório.</summary>
    public bool Destaque { get; set; }

    /// <summary>Bloco 28: o GPS do EXIF, em graus decimais (os dois nulos quando a foto não tinha).</summary>
    public double? Latitude { get; set; }

    public double? Longitude { get; set; }
}

/// <summary>A imagem (versão reduzida, JPEG) de uma foto, separada dos metadados para listar fotos sem lê-la.</summary>
internal sealed class FotoImagemRegistro
{
    public Guid FotoId { get; set; }
    public required byte[] Dados { get; set; }
}

/// <summary>A miniatura (JPEG) de uma foto, lida pela galeria sob demanda.</summary>
internal sealed class FotoMiniaturaRegistro
{
    public Guid FotoId { get; set; }
    public required byte[] Dados { get; set; }
}

/// <summary>
/// A conversa com o assistente e a ficha do projeto (Bloco 26), em JSON (ver <see cref="FormatoDaConversa"/>): só uma
/// linha por projeto, e nenhuma quando a conversa está vazia.
/// </summary>
internal sealed class AssistenteRegistro
{
    public Guid ProjetoId { get; set; }

    /// <summary>A versão do formato do JSON; uma que esta versão do programa não conhece é deixada de fora ao abrir.</summary>
    public int Versao { get; set; }

    public required string Dados { get; set; }
}

/// <summary>
/// Os dados da obra para a capa e o histórico dos relatórios fotográficos (Bloco 27), em JSON (ver
/// <see cref="FormatoDoRelatorio"/>): uma linha por projeto, nenhuma quando não há nada.
/// </summary>
/// <summary>Bloco 31: as linhas de base do projeto, em JSON (ver <see cref="FormatoDasLinhasDeBase"/>); nenhuma linha sem linha de base.</summary>
internal sealed class LinhasDeBaseRegistro
{
    public Guid ProjetoId { get; set; }

    public int Versao { get; set; }

    public required string Dados { get; set; }
}

/// <summary>Bloco 36: o contrato, os aditivos e as medições, em JSON (ver <see cref="FormatoDoContrato"/>); nenhuma linha sem contrato.</summary>
internal sealed class ContratoRegistro
{
    public Guid ProjetoId { get; set; }

    public int Versao { get; set; }

    public required string Dados { get; set; }
}

internal sealed class RelatorioRegistro
{
    public Guid ProjetoId { get; set; }

    public int Versao { get; set; }

    public required string Dados { get; set; }
}
