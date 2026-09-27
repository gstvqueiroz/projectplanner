namespace ProjectPlanner.IA;

/// <summary>
/// Um documento anexado pelo usuário no painel do assistente (planta, contrato, memorial...). Guarda só a
/// referência e os metadados do arquivo: o conteúdo não é lido aqui. Quem ler e enviar o conteúdo é a
/// implementação real de <see cref="IAssistenteIA"/>, no bloco da integração.
/// </summary>
/// <param name="CaminhoLocal">Caminho completo do arquivo no computador.</param>
/// <param name="TamanhoEmBytes">Tamanho do arquivo quando foi anexado (dos metadados, sem abrir o arquivo).</param>
public sealed record AnexoIA(string CaminhoLocal, long TamanhoEmBytes)
{
    /// <summary>Nome do arquivo, como "planta-baixa.pdf".</summary>
    public string Nome => Path.GetFileName(CaminhoLocal);

    /// <summary>Extensão em minúsculas, sem o ponto ("pdf"); vazia se o arquivo não tem.</summary>
    public string Extensao => Path.GetExtension(CaminhoLocal).TrimStart('.').ToLowerInvariant();

    /// <summary>Tamanho para exibir: "830 bytes", "12 KB", "2,3 MB".</summary>
    public string TamanhoLegivel => TamanhoEmBytes switch
    {
        < 1024 => $"{TamanhoEmBytes} bytes",
        < 1024 * 1024 => $"{TamanhoEmBytes / 1024.0:0} KB",
        _ => (TamanhoEmBytes / (1024.0 * 1024)).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")) + " MB",
    };

    /// <summary>O anexo de um arquivo que existe, com o tamanho dos metadados (o conteúdo não é aberto).</summary>
    /// <exception cref="ArgumentException">Caminho vazio.</exception>
    /// <exception cref="FileNotFoundException">O arquivo não existe.</exception>
    public static AnexoIA DoArquivo(string caminho)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caminho);
        var arquivo = new FileInfo(caminho);
        if (!arquivo.Exists)
            throw new FileNotFoundException("O arquivo não existe.", caminho);
        return new AnexoIA(arquivo.FullName, arquivo.Length);
    }
}
