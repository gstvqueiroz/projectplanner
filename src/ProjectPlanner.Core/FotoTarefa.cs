namespace ProjectPlanner.Core;

/// <summary>
/// Um registro fotográfico de uma tarefa: quando a foto foi tirada, onde está a imagem e uma descrição.
/// </summary>
/// <remarks>
/// Base definitiva do relatório fotográfico (Bloco 14): a modelagem e a persistência. O que
/// <see cref="CaminhoArquivo"/> aponta ainda é provisório: não há envio de imagens, então os registros
/// de exemplo usam identificadores fictícios (<c>exemplo://…</c>). Quando houver imagens de verdade,
/// decide-se aqui se é um caminho de arquivo, um blob no próprio .pplan ou uma referência externa.
/// </remarks>
public sealed record FotoTarefa
{
    public FotoTarefa(Guid tarefaId, DateTime dataRegistro, string caminhoArquivo, string? descricao = null)
        : this(Guid.NewGuid(), tarefaId, dataRegistro, caminhoArquivo, descricao)
    {
    }

    /// <summary>Reconstrói um registro existente, mantendo o Id (por exemplo, ao abrir um arquivo).</summary>
    public FotoTarefa(Guid id, Guid tarefaId, DateTime dataRegistro, string caminhoArquivo, string? descricao = null)
    {
        if (id == Guid.Empty)
            throw new DomainValidationException(nameof(Id), "O id da foto é obrigatório.");
        if (tarefaId == Guid.Empty)
            throw new DomainValidationException(nameof(TarefaId), "A tarefa da foto é obrigatória.");
        if (string.IsNullOrWhiteSpace(caminhoArquivo))
            throw new DomainValidationException(nameof(CaminhoArquivo), "O caminho da imagem é obrigatório.");

        Id = id;
        TarefaId = tarefaId;
        DataRegistro = dataRegistro;
        CaminhoArquivo = caminhoArquivo.Trim();
        Descricao = string.IsNullOrWhiteSpace(descricao) ? null : descricao.Trim();
    }

    public Guid Id { get; }

    public Guid TarefaId { get; }

    /// <summary>Quando a foto foi tirada ou registrada.</summary>
    public DateTime DataRegistro { get; }

    /// <summary>Onde está a imagem. Provisório: por enquanto só identificadores fictícios (ver remarks).</summary>
    public string CaminhoArquivo { get; }

    public string? Descricao { get; }
}
