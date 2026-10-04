using System.Security.Cryptography;
using System.Text;

namespace ProjectPlanner.Core.Tests;

/// <summary>Fotos para os testes do domínio, sem imagem de verdade (o domínio só guarda os metadados).</summary>
public static class FotosDeTeste
{
    /// <summary>Hash de mentira, diferente para cada nome.</summary>
    public static string Hash(string semente) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(semente)));

    public static FotoTarefa Nova(Guid tarefaId, DateTime data, string? descricao = null, string? titulo = null,
        string? autor = null, string? local = null, IEnumerable<string>? tags = null, string? arquivo = null) =>
        new(Guid.NewGuid(), tarefaId,
            new DadosDaImagem(arquivo ?? $"IMG_{Guid.NewGuid():N}.jpg", 1600, 1200, 350_000, Hash(arquivo ?? Guid.NewGuid().ToString())),
            new MetadadosFoto(data, titulo, descricao, autor, local, tags: tags),
            new DateTime(2026, 10, 1, 9, 0, 0));
}
