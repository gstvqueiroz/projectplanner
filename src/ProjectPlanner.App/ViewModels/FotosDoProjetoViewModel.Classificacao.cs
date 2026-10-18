using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.Core.Fotos;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.ViewModels;

// Bloco 42: a IA diz onde cada foto do lote se encaixa (tarefa, legenda, local, %), e as fotos novas da pasta da obra.
public sealed partial class FotosDoProjetoViewModel
{
    /// <summary>As extensões que a pasta da obra traz (as mesmas que a importação aceita).</summary>
    public static readonly string[] ExtensoesDeFoto = [".jpg", ".jpeg", ".png", ".heic", ".heif", ".webp"];

    /// <summary>Quantos arquivos da pasta, no máximo, numa leitura (os mais novos).</summary>
    public const int ArquivosPorLeituraDaPasta = 200;

    private CancellationTokenSource? _classificando;

    /// <summary>Ao importar, sobe o % concluído das tarefas para o que as fotos mostram (segundo a IA).</summary>
    [ObservableProperty] public partial bool AtualizarPercentualPelasFotos { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClassificarComIaCommand), nameof(CancelarClassificacaoCommand))]
    public partial bool Classificando { get; private set; }

    /// <summary>"Pasta da obra: ~/Fotos/Obra · lida em 18/10 10:00" ou o convite para escolher.</summary>
    public string TextoDaPasta => _cronograma.ConfiguracaoDasFotos.PastaDaObra is { } pasta
        ? $"Pasta da obra: {pasta}" + (_cronograma.ConfiguracaoDasFotos.UltimaLeituraDaPasta is { } lida ? $" · lida em {lida:dd/MM/yyyy HH:mm}" : "")
        : "Nenhuma pasta da obra escolhida: em Buscar na pasta da obra…, escolha a pasta onde as fotos chegam (a do celular, por exemplo).";

    private bool PodeClassificar() => !Classificando && !Ocupado && Lote.Any(l => l.Lida.Importada is not null);

    /// <summary>A IA olha cada foto do lote e escolhe a tarefa, a legenda, o local e o % (o usuário confere antes de importar).</summary>
    [RelayCommand(CanExecute = nameof(PodeClassificar))]
    private async Task ClassificarComIa()
    {
        if (!_analista.EstaConfigurado)
        {
            AvisoDoLote = AnalistaIndisponivel.Mensagem;
            return;
        }
        var linhas = Lote.Where(l => l.Lida.Importada is not null).ToList();
        var datas = linhas.Select(l => DateOnly.FromDateTime(l.Lida.Importada!.DataFoto)).ToList();
        var (de, ate) = (datas.Min().AddDays(-CoerenciaDaData.Folga), datas.Max().AddDays(CoerenciaDaData.Folga));
        var edt = _cronograma.NumeracaoEdt();
        var porId = _cronograma.Tarefas.ToDictionary(t => t.Id);
        string Caminho(Core.Tarefa t)
        {
            var nomes = new List<string>();
            for (var pai = t.TarefaPaiId; pai is { } id; pai = porId[id].TarefaPaiId)
                nomes.Insert(0, porId[id].Nome);
            return string.Join(" > ", nomes);
        }
        var candidatas = _cronograma.Tarefas.Where(t => !t.EhResumo && !t.EhMarco)
            .Select(t => (Tarefa: t, Inicio: DateOnly.FromDateTime(t.InicioMaisCedo ?? t.DataInicio), Termino: DateOnly.FromDateTime(t.TerminoMaisCedo ?? t.DataTermino)))
            .Select(x => (x.Tarefa, x.Inicio, x.Termino, EmExecucao: x.Inicio <= ate && x.Termino >= de))
            .OrderBy(x => x.EmExecucao ? 0 : 1)
            .Take(LimitesDaAnalise.TarefasNaClassificacao)
            .OrderBy(x => _cronograma.Tarefas.ToList().IndexOf(x.Tarefa))
            .ToList();
        var ids = candidatas.Select((x, i) => (Id: $"T{i + 1}", x.Tarefa.Id)).ToDictionary(x => x.Item1, x => x.Item2, StringComparer.OrdinalIgnoreCase);
        var tarefas = candidatas.Select((x, i) => new TarefaParaClassificar($"T{i + 1}", edt[x.Tarefa.Id], x.Tarefa.Nome, Caminho(x.Tarefa), x.Inicio, x.Termino,
            x.Tarefa.PercentualConcluido, x.EmExecucao)).ToList();

        using var cancelamento = _classificando = new CancellationTokenSource();
        Classificando = true;
        var (classificadas, duvidosas) = (0, 0);
        try
        {
            foreach (var grupo in linhas.Chunk(LimitesDaAnalise.FotosPorClassificacao))
            {
                AvisoDoLote = $"A IA está olhando as fotos ({classificadas + 1}–{classificadas + grupo.Length} de {linhas.Count})…";
                var fotos = grupo.Select((l, i) => new FotoParaIA($"F{classificadas + i + 1}", l.Lida.Importada!.Conteudo.Imagem,
                    l.TarefaEscolhida.Id is { } t ? $"sugerida pela data: {edt[t]} {porId[t].Nome}" : "sem tarefa pela data",
                    DateOnly.FromDateTime(l.Lida.Importada.DataFoto))).ToList();
                var respostas = await _analista.ClassificarFotosAsync(
                    new PedidoDeClassificacao(_cronograma.Nome, DateOnly.FromDateTime(_relogio.GetLocalNow().DateTime), tarefas, fotos,
                        _cronograma.ConfiguracaoDasFotos.Locais), cancelamento.Token);
                for (var i = 0; i < grupo.Length; i++)
                {
                    if (respostas.FirstOrDefault(r => r.Id == fotos[i].Id) is not { } r)
                        continue;
                    var linha = grupo[i];
                    if (r.Tarefa is { } t && ids.TryGetValue(t, out var tarefaId) && linha.Tarefas.FirstOrDefault(o => o.Id == tarefaId) is { } opcao)
                    {
                        linha.Situacao = opcao == linha.TarefaEscolhida ? "A IA confirmou a tarefa da data." : "Escolhida pela IA pelo que aparece na foto.";
                        linha.TarefaEscolhida = opcao;
                    }
                    linha.Legenda = r.Legenda ?? linha.Legenda;
                    linha.Local = r.Local ?? linha.Local;
                    linha.PercentualSugerido = r.Percentual;
                    linha.Confianca = r.Tarefa is null ? "IA: não soube a tarefa" : r.Confianca switch
                    {
                        ConfiancaDaIA.Alta => "IA: certeza alta",
                        ConfiancaDaIA.Media => "IA: provável",
                        _ => "IA: palpite, confira",
                    };
                    linha.Duvidosa = r.Tarefa is null || r.Confianca == ConfiancaDaIA.Baixa;
                    linha.Justificativa = r.Justificativa;
                    duvidosas += linha.Duvidosa ? 1 : 0;
                }
                classificadas += grupo.Length;
            }
            AvisoDoLote = $"{(classificadas == 1 ? "1 foto classificada" : $"{classificadas} fotos classificadas")} pela IA" +
                          (duvidosas == 0 ? "" : $"; {duvidosas} com dúvida (marcadas)") + ". Confira as tarefas e as legendas e clique em Importar.";
        }
        catch (OperationCanceledException)
        {
            AvisoDoLote = $"Classificação cancelada ({classificadas} de {linhas.Count} fotos classificadas).";
        }
        catch (ErroDoAssistenteException ex)
        {
            AvisoDoLote = ex.Message;
        }
        finally
        {
            _classificando = null;
            Classificando = false;
        }
    }

    [RelayCommand(CanExecute = nameof(Classificando))]
    private void CancelarClassificacao() => _classificando?.Cancel();

    /// <summary>
    /// Traz para o lote as fotos novas da pasta da obra (as modificadas depois da última leitura e que ainda não estão no
    /// projeto) e, com a IA configurada, já as classifica. Sem pasta escolhida, pede a pasta.
    /// </summary>
    [RelayCommand(CanExecute = nameof(Livre))]
    private async Task BuscarNaPastaDaObra()
    {
        var config = _cronograma.ConfiguracaoDasFotos;
        var pasta = config.PastaDaObra;
        if (pasta is null || !Directory.Exists(pasta))
        {
            if (pasta is not null)
                AvisoDoLote = $"A pasta {pasta} não existe mais: escolha outra.";
            if (await _dialogos.EscolherPastaAsync("A pasta onde chegam as fotos da obra") is not { } escolhida)
                return;
            pasta = escolhida;
            config = config with { PastaDaObra = pasta, UltimaLeituraDaPasta = null };
        }
        var desde = config.UltimaLeituraDaPasta;
        var agora = _relogio.GetLocalNow().DateTime;
        List<string> arquivos;
        try
        {
            arquivos = Directory.EnumerateFiles(pasta, "*", SearchOption.AllDirectories)
                .Where(a => ExtensoesDeFoto.Contains(Path.GetExtension(a).ToLowerInvariant()))
                .Select(a => (Arquivo: a, Data: File.GetLastWriteTime(a)))
                .Where(a => desde is null || a.Data > desde)
                .OrderByDescending(a => a.Data)
                .Take(ArquivosPorLeituraDaPasta)
                .Select(a => a.Arquivo)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AvisoDoLote = $"Não foi possível ler a pasta {pasta}: {ex.Message}";
            return;
        }
        _cronograma.DefinirConfiguracaoDasFotos(config with { UltimaLeituraDaPasta = agora });
        _configuracaoMudou();
        OnPropertyChanged(nameof(TextoDaPasta));
        AbaEscolhida = AbaImportacao;
        if (arquivos.Count == 0)
        {
            AvisoDoLote = desde is null ? $"Nenhuma foto na pasta {pasta}." : $"Nenhuma foto nova na pasta desde {desde:dd/MM/yyyy HH:mm}.";
            return;
        }
        var antes = Lote.Count;
        await LerLoteAsync(arquivos, soNovas: true);
        var novas = Lote.Count - antes;
        AvisoDoLote = novas == 0 ? "As fotos da pasta já estão no projeto." : $"{(novas == 1 ? "1 foto nova" : $"{novas} fotos novas")} da pasta da obra.";
        if (novas > 0 && _analista.EstaConfigurado)
            await ClassificarComIa();
    }

    /// <summary>Escolhe outra pasta da obra (a próxima busca lê a pasta inteira).</summary>
    [RelayCommand(CanExecute = nameof(Livre))]
    private async Task TrocarPastaDaObra()
    {
        if (await _dialogos.EscolherPastaAsync("A pasta onde chegam as fotos da obra") is not { } pasta)
            return;
        _cronograma.DefinirConfiguracaoDasFotos(_cronograma.ConfiguracaoDasFotos with { PastaDaObra = pasta, UltimaLeituraDaPasta = null });
        _configuracaoMudou();
        OnPropertyChanged(nameof(TextoDaPasta));
    }

    /// <summary>Bloco 42 (o chat): lê estas fotos para o lote e, com a IA configurada, já as classifica.</summary>
    public async Task ImportarComIaAsync(IReadOnlyList<string> caminhos)
    {
        await LerLoteAsync(caminhos);
        if (_analista.EstaConfigurado && Lote.Any(l => l.Lida.Importada is not null))
            await ClassificarComIa();
    }
}
