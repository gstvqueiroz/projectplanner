using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.IA;
using ProjectPlanner.IA.Conta;

namespace ProjectPlanner.App.ViewModels;

// Bloco 49: o Telegram no programa. Conta > Usar esta obra no Telegram manda o retrato da obra ao servidor (e de novo a cada
// vez que ela é salva); Conta > Buscar do Telegram traz o que chegou: as atualizações viram cartões no chat, para conferir
// e aplicar, e as fotos vão para a importação em lote (com a IA classificando); Conta > Conectar o Telegram… liga o chat.
public sealed partial class MainWindowViewModel
{
    private readonly ITelegramDaConta? _telegram;

    /// <summary>Onde as fotos do Telegram ficam até a importação (uma pasta por obra).</summary>
    public string PastaDasFotosDoTelegram { get; set; } =
        Path.Combine(Path.GetDirectoryName(ArquivoPreferenciasColunas.CaminhoPadrao())!, "telegram");

    /// <summary>A obra aberta está marcada para o Telegram (o que o servidor disse por último).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDoMenuDaObraNoTelegram))]
    public partial bool ObraNoTelegram { get; private set; }

    /// <summary>Quantos itens do Telegram esperam esta obra.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDoMenuBuscarDoTelegram))]
    [NotifyCanExecuteChangedFor(nameof(BuscarDoTelegramCommand))]
    public partial int PendentesDoTelegram { get; private set; }

    public string TextoDoMenuDaObraNoTelegram => ObraNoTelegram ? "Tirar esta obra do Telegram" : "Usar esta obra no Telegram";

    public string TextoDoMenuBuscarDoTelegram => PendentesDoTelegram > 0 ? $"Buscar do Telegram ({PendentesDoTelegram})" : "Buscar do Telegram";

    private bool TelegramDisponivel => _telegram is not null && _conta.Conectada;

    [RelayCommand(CanExecute = nameof(TelegramDisponivel))]
    private Task ConectarTelegram() => _telegram is null ? Task.CompletedTask : _dialogos.MostrarTelegramAsync(new TelegramViewModel(_telegram, _dialogos));

    [RelayCommand(CanExecute = nameof(TelegramDisponivel))]
    private async Task UsarObraNoTelegram()
    {
        try
        {
            if (ObraNoTelegram)
            {
                await _telegram!.DesmarcarAsync(_cronograma.Id, CancellationToken.None);
                (ObraNoTelegram, PendentesDoTelegram) = (false, 0);
                Mensagem = $"A obra \"{_cronograma.Nome}\" saiu do Telegram.";
            }
            else
            {
                await _telegram!.MarcarAsync(_cronograma.Id, _cronograma.Nome, RetratoParaOAssistente(), CancellationToken.None);
                ObraNoTelegram = true;
                Mensagem = $"A obra \"{_cronograma.Nome}\" está no Telegram: o retrato é atualizado a cada vez que você salvar.";
            }
        }
        catch (ErroDaContaException ex)
        {
            Mensagem = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(PodeBuscarDoTelegram))]
    private async Task BuscarDoTelegram()
    {
        IReadOnlyList<ItemDoTelegram> itens;
        try
        {
            itens = await _telegram!.CaixaAsync(_cronograma.Id, CancellationToken.None);
        }
        catch (ErroDaContaException ex)
        {
            Mensagem = ex.Message;
            return;
        }
        if (itens.Count == 0)
        {
            (PendentesDoTelegram, Mensagem) = (0, "Nada novo do Telegram para esta obra.");
            return;
        }

        var (cartoes, fotos, falhas) = (0, new List<string>(), 0);
        foreach (var item in itens.Where(i => i.Tipo == ItemDoTelegram.DeAtualizacoes && i.Atualizacoes is { Count: > 0 }))
        {
            Assistente.MostrarDoTelegram(item.Texto, item.CriadoEm, ConferirAtualizacoes(item.Atualizacoes!, RetratoDoTelegram(item.Tarefas)));
            cartoes++;
            falhas += await Levar(item) ? 0 : 1;
        }
        var pasta = Path.Combine(PastaDasFotosDoTelegram, _cronograma.Id.ToString("N"));
        foreach (var item in itens.Where(i => i.Tipo == ItemDoTelegram.DeFoto))
        {
            try
            {
                var bytes = await _telegram!.FotoAsync(item.Id, CancellationToken.None);
                Directory.CreateDirectory(pasta);
                var caminho = Path.Combine(pasta, Path.GetFileName(item.NomeDaFoto ?? $"telegram-{item.Id:N}.jpg"));
                await File.WriteAllBytesAsync(caminho, bytes);
                fotos.Add(caminho);
                falhas += await Levar(item) ? 0 : 1;
            }
            catch (Exception ex) when (ex is ErroDaContaException or IOException or UnauthorizedAccessException)
            {
                falhas++;
            }
        }
        PendentesDoTelegram = falhas;
        if (cartoes > 0)
            Assistente.Visivel = true;
        Mensagem = string.Join(" ", new[]
        {
            cartoes == 0 ? null : cartoes == 1 ? "1 mensagem do Telegram virou cartão no assistente: confira e aplique." : $"{cartoes} mensagens do Telegram viraram cartões no assistente: confira e aplique.",
            fotos.Count == 0 ? null : fotos.Count == 1 ? "1 foto foi para a importação." : $"{fotos.Count} fotos foram para a importação.",
            falhas == 0 ? null : $"{falhas} item(ns) não puderam ser trazidos agora (tente de novo).",
        }.Where(t => t is not null));
        if (fotos.Count > 0)
            await ImportarFotosAsync(OrigensDasFotos.Anexos, fotos);
    }

    private bool PodeBuscarDoTelegram() => TelegramDisponivel && ObraNoTelegram;

    private async Task<bool> Levar(ItemDoTelegram item)
    {
        try
        {
            await _telegram!.LevarAsync(item.Id, CancellationToken.None);
            return true;
        }
        catch (ErroDaContaException)
        {
            return false; // fica no servidor: vem de novo na próxima busca
        }
    }

    // As tarefas como estavam quando a mensagem chegou (o Nº que a IA citou e o id), para achar a tarefa certa.
    private static ContextoDoProjeto RetratoDoTelegram(IReadOnlyList<TarefaDoTelegram>? tarefas) =>
        new("", default, default, default, [.. (tarefas ?? []).Select(t =>
            new TarefaNoContexto(t.Numero, "", "", 0, TipoNoContexto.Tarefa, 0, default, default, 0, "", null, false, null, 0, t.Id))]);

    /// <summary>Ao abrir uma obra (ou ao entrar na conta): ela está no Telegram? Chegou alguma coisa?</summary>
    internal async Task AtualizarTelegramAsync()
    {
        if (!TelegramDisponivel)
        {
            (ObraNoTelegram, PendentesDoTelegram) = (false, 0);
            AtualizarComandosDoTelegram();
            return;
        }
        var projeto = _cronograma.Id;
        try
        {
            var obra = (await _telegram!.ObrasAsync(CancellationToken.None)).FirstOrDefault(o => o.Projeto == projeto);
            if (projeto != _cronograma.Id)
                return; // outra obra foi aberta enquanto isso
            (ObraNoTelegram, PendentesDoTelegram) = (obra is not null, obra?.Pendentes ?? 0);
            if (PendentesDoTelegram > 0)
                Mensagem = PendentesDoTelegram == 1
                    ? "Chegou 1 item do Telegram para esta obra: Conta > Buscar do Telegram."
                    : $"Chegaram {PendentesDoTelegram} itens do Telegram para esta obra: Conta > Buscar do Telegram.";
        }
        catch (ErroDaContaException)
        {
            // sem internet agora: o menu continua como estava
        }
        AtualizarComandosDoTelegram();
    }

    // Depois de salvar: a obra marcada manda o retrato novo (sem atrapalhar o salvar se o servidor não responder).
    private async Task AtualizarRetratoNoTelegramAsync()
    {
        if (!TelegramDisponivel || !ObraNoTelegram)
            return;
        try
        {
            await _telegram!.MarcarAsync(_cronograma.Id, _cronograma.Nome, RetratoParaOAssistente(), CancellationToken.None);
        }
        catch (ErroDaContaException ex)
        {
            Mensagem += $" O retrato no Telegram não foi atualizado agora ({ex.Message})";
        }
    }

    private void AtualizarComandosDoTelegram()
    {
        ConectarTelegramCommand.NotifyCanExecuteChanged();
        UsarObraNoTelegramCommand.NotifyCanExecuteChanged();
        BuscarDoTelegramCommand.NotifyCanExecuteChanged();
    }
}
