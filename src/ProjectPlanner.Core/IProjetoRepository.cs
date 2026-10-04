namespace ProjectPlanner.Core;

/// <summary>
/// Guarda e recupera projetos, um arquivo por projeto (como o "Abrir" e o "Salvar como" do MS Project).
/// </summary>
/// <remarks>
/// As datas do caminho crítico não são gravadas: depois de abrir, chame
/// <see cref="CalculadoraCronograma.Calcular"/>. Os métodos são síncronos porque o SQLite não
/// tem E/S assíncrona de verdade; a interface pode chamá-los fora da thread de UI.
/// </remarks>
public interface IProjetoRepository
{
    /// <summary>
    /// Grava o projeto inteiro no arquivo, criando-o ou substituindo todo o conteúdo anterior.
    /// Em caso de sucesso, atualiza <see cref="Cronograma.DataUltimaModificacao"/> (e
    /// <see cref="Cronograma.DataCriacao"/>, no primeiro salvamento).
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">A pasta do arquivo não existe.</exception>
    void Salvar(Cronograma cronograma, string caminhoArquivo) => Salvar(cronograma, caminhoArquivo, Fotos.ImagensParaSalvar.Nenhuma);

    /// <summary>
    /// Como <see cref="Salvar(Cronograma, string)"/>, gravando também as imagens das fotos: as de
    /// <see cref="Fotos.ImagensParaSalvar.Novas"/> e, para as demais fotos do projeto, as copiadas de
    /// <see cref="Fotos.ImagensParaSalvar.ArquivoDeOrigem"/> (pode ser o próprio destino).
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">A pasta do arquivo não existe.</exception>
    /// <exception cref="IOException">Alguma foto ficaria sem imagem (o arquivo de origem sumiu, por exemplo).</exception>
    void Salvar(Cronograma cronograma, string caminhoArquivo, Fotos.ImagensParaSalvar imagens);

    /// <summary>As miniaturas das fotos pedidas que estão no arquivo (as que não estão ficam de fora).</summary>
    IReadOnlyDictionary<Guid, byte[]> LerMiniaturas(string caminhoArquivo, IReadOnlyCollection<Guid> fotos);

    /// <summary>A imagem (versão reduzida) de uma foto do arquivo, ou nula se ela não estiver lá.</summary>
    byte[]? LerImagem(string caminhoArquivo, Guid foto);

    /// <summary>Lê o projeto do arquivo, reconstruindo tarefas e vínculos.</summary>
    /// <exception cref="FileNotFoundException">O arquivo não existe.</exception>
    /// <exception cref="InvalidDataException">
    /// O arquivo não é um projeto, foi salvo por uma versão mais nova ou tem dados inválidos.
    /// </exception>
    Cronograma Abrir(string caminhoArquivo);
}
