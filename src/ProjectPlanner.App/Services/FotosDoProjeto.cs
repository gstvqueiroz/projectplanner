using ProjectPlanner.Core;
using ProjectPlanner.Core.Fotos;

namespace ProjectPlanner.App.Services;

/// <summary>O que aconteceu ao adicionar fotos: as que entraram, as repetidas e as recusadas (com o motivo).</summary>
public sealed record ResultadoDaImportacao(IReadOnlyList<FotoTarefa> Adicionadas, IReadOnlyList<string> JaAnexadas, IReadOnlyList<string> Recusadas)
{
    /// <summary>"2 fotos adicionadas. Já anexada: a.jpg. Recusada: x.heic: …".</summary>
    public string Resumo
    {
        get
        {
            var partes = new List<string>
            {
                Adicionadas.Count switch { 0 => "Nenhuma foto adicionada.", 1 => "1 foto adicionada.", var n => $"{n} fotos adicionadas." },
            };
            if (JaAnexadas.Count > 0)
                partes.Add((JaAnexadas.Count == 1 ? "Já anexada: " : "Já anexadas: ") + string.Join(", ", JaAnexadas) + ".");
            if (Recusadas.Count > 0)
                partes.Add((Recusadas.Count == 1 ? "Recusada: " : "Recusadas: ") + string.Join(" ", Recusadas));
            return string.Join(" ", partes);
        }
    }
}

/// <summary>
/// As imagens das fotos do projeto aberto (Bloco 22, D7). As fotos (metadados) ficam nas tarefas; as imagens ficam
/// no .pplan e são lidas sob demanda: as miniaturas com cache da sessão, a imagem completa só quando pedida. Fotos
/// adicionadas ou giradas ainda não salvas ficam aqui, na memória, e vão para o arquivo no próximo salvamento (as
/// demais são copiadas do arquivo de origem, ver <see cref="ImagensParaSalvar"/>).
/// </summary>
public sealed class FotosDoProjeto(IProjetoRepository repositorio, IProcessadorDeImagens processador, TimeProvider relogio)
{
    private readonly Dictionary<Guid, ConteudoDeFoto> _novas = [];
    private readonly Dictionary<Guid, byte[]> _miniaturas = [];

    /// <summary>O .pplan de onde vêm as imagens que não estão na memória (nulo num projeto novo).</summary>
    public string? ArquivoDeOrigem { get; private set; }

    /// <summary>Quantas imagens só existem na memória (novas ou giradas, ainda não salvas).</summary>
    public int Pendentes => _novas.Count;

    /// <summary>Outro projeto foi aberto (ou um novo, com <paramref name="arquivo"/> nulo): esquece as imagens do anterior.</summary>
    public void Recomecar(string? arquivo)
    {
        ArquivoDeOrigem = arquivo;
        _novas.Clear();
        _miniaturas.Clear();
    }

    /// <summary>O projeto foi salvo em <paramref name="arquivo"/>: as imagens da memória agora estão lá.</summary>
    public void Salvou(string arquivo)
    {
        ArquivoDeOrigem = arquivo;
        _novas.Clear();
    }

    /// <summary>O que gravar no próximo salvamento.</summary>
    public ImagensParaSalvar ParaSalvar() => new(ArquivoDeOrigem, new Dictionary<Guid, ConteudoDeFoto>(_novas));

    /// <summary>As miniaturas disponíveis das fotos pedidas (as que não der para ler ficam de fora).</summary>
    public IReadOnlyDictionary<Guid, byte[]> Miniaturas(IEnumerable<Guid> fotos)
    {
        var pedidas = fotos.ToList();
        var faltando = pedidas.Where(id => !_miniaturas.ContainsKey(id)).ToList();
        if (faltando.Count > 0 && ArquivoDeOrigem is { } origem)
        {
            try
            {
                foreach (var (id, miniatura) in repositorio.LerMiniaturas(origem, faltando))
                    _miniaturas[id] = miniatura;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                           or Microsoft.Data.Sqlite.SqliteException)
            {
                // Arquivo movido ou em uso: a galeria mostra as fotos sem miniatura.
            }
        }
        return pedidas.Where(_miniaturas.ContainsKey).ToDictionary(id => id, id => _miniaturas[id]);
    }

    /// <summary>A imagem completa (versão reduzida) da foto, ou nula se não der para ler.</summary>
    public byte[]? Imagem(Guid foto)
    {
        if (_novas.TryGetValue(foto, out var conteudo))
            return conteudo.Imagem;
        if (ArquivoDeOrigem is not { } origem)
            return null;
        try
        {
            return repositorio.LerImagem(origem, foto);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or Microsoft.Data.Sqlite.SqliteException)
        {
            return null;
        }
    }

    /// <summary>
    /// Lê os arquivos (fora da thread da tela) e põe na tarefa as fotos aceitas, com a data do EXIF (ou do arquivo)
    /// e o % atual da tarefa. A mesma imagem que já está na tarefa (ou repetida na seleção) é pulada.
    /// </summary>
    public async Task<ResultadoDaImportacao> ImportarAsync(Tarefa tarefa, IReadOnlyList<string> caminhos, IProgress<int>? progresso = null)
    {
        ArgumentNullException.ThrowIfNull(tarefa);
        var (adicionadas, repetidas, recusadas) = (new List<FotoTarefa>(), new List<string>(), new List<string>());
        for (var i = 0; i < caminhos.Count; i++)
        {
            progresso?.Report(i);
            var caminho = caminhos[i];
            ImagemImportada importada;
            try
            {
                importada = await Task.Run(() => processador.Importar(caminho));
            }
            catch (ImagemRecusadaException ex)
            {
                recusadas.Add(ex.Message);
                continue;
            }
            catch (Exception ex)
            {
                recusadas.Add($"{Path.GetFileName(caminho)}: não foi possível ler a imagem ({ex.Message}).");
                continue;
            }

            if (tarefa.TemFotoDaMesmaImagem(importada.HashOriginal))
            {
                repetidas.Add(importada.NomeArquivoOriginal);
                continue;
            }
            var conteudo = importada.Conteudo;
            var foto = new FotoTarefa(Guid.NewGuid(), tarefa.Id,
                new DadosDaImagem(importada.NomeArquivoOriginal, conteudo.Largura, conteudo.Altura, conteudo.Imagem.Length, importada.HashOriginal),
                new MetadadosFoto(importada.DataFoto, percentualConcluidoNaData: tarefa.PercentualConcluido),
                relogio.GetLocalNow().DateTime);
            tarefa.AdicionarFoto(foto);
            _novas[foto.Id] = conteudo;
            _miniaturas[foto.Id] = conteudo.Miniatura;
            adicionadas.Add(foto);
        }
        progresso?.Report(caminhos.Count);
        return new ResultadoDaImportacao(adicionadas, repetidas, recusadas);
    }

    /// <summary>Troca as informações editáveis da foto.</summary>
    /// <exception cref="DomainValidationException">A foto não é da tarefa.</exception>
    public FotoTarefa Editar(Tarefa tarefa, Guid fotoId, MetadadosFoto metadados)
    {
        var foto = (tarefa.Foto(fotoId) ?? throw new DomainValidationException(nameof(Tarefa.Fotos), "A foto não é desta tarefa."))
            .ComMetadados(metadados);
        tarefa.SubstituirFoto(foto);
        return foto;
    }

    /// <summary>Bloco 27: se a foto entra no relatório e se é destaque.</summary>
    /// <exception cref="DomainValidationException">A foto não é da tarefa.</exception>
    public FotoTarefa DefinirCuradoria(Tarefa tarefa, Guid fotoId, bool noRelatorio, bool destaque)
    {
        var foto = (tarefa.Foto(fotoId) ?? throw new DomainValidationException(nameof(Tarefa.Fotos), "A foto não é desta tarefa."))
            .ComCuradoria(noRelatorio, destaque);
        tarefa.SubstituirFoto(foto);
        return foto;
    }

    /// <summary>Gira a imagem guardada 90° (o resultado fica na memória até salvar).</summary>
    /// <exception cref="ImagemRecusadaException">A imagem não está disponível ou está corrompida.</exception>
    public async Task<FotoTarefa> GirarAsync(Tarefa tarefa, Guid fotoId, bool sentidoHorario)
    {
        var foto = tarefa.Foto(fotoId) ?? throw new DomainValidationException(nameof(Tarefa.Fotos), "A foto não é desta tarefa.");
        var imagem = Imagem(fotoId) ?? throw new ImagemRecusadaException(
            $"{foto.Imagem.NomeArquivoOriginal}: a imagem desta foto não está disponível (o arquivo do projeto foi movido?).");
        var girada = await Task.Run(() => processador.Girar(imagem, sentidoHorario));
        var nova = foto.ComImagem(new DadosDaImagem(foto.Imagem.NomeArquivoOriginal, girada.Largura, girada.Altura,
            girada.Imagem.Length, foto.Imagem.HashOriginal));
        tarefa.SubstituirFoto(nova);
        _novas[fotoId] = girada;
        _miniaturas[fotoId] = girada.Miniatura;
        return nova;
    }

    /// <summary>Tira as fotos da tarefa (e as imagens delas da memória).</summary>
    public void Excluir(Tarefa tarefa, IEnumerable<Guid> fotos)
    {
        foreach (var id in fotos.ToList())
        {
            tarefa.RemoverFoto(id);
            _novas.Remove(id);
            _miniaturas.Remove(id);
        }
    }
}
