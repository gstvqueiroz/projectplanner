using ProjectPlanner.IA;
using ProjectPlanner.IA.Conta;

namespace ProjectPlanner.App.Services;

/// <summary>
/// Bloco 49: o Telegram pela conta ProjectPlanner: ligar o chat (código), marcar a obra (o retrato vai ao servidor) e levar
/// o que chegou na caixa de entrada (atualizações e fotos).
/// </summary>
public interface ITelegramDaConta
{
    Task<SituacaoDoTelegram> SituacaoAsync(CancellationToken c);
    Task<CodigoParaOTelegram> CodigoAsync(CancellationToken c);
    Task DesligarAsync(CancellationToken c);
    Task<IReadOnlyList<ObraNoTelegram>> ObrasAsync(CancellationToken c);
    Task MarcarAsync(Guid projeto, string nome, ContextoDoProjeto retrato, CancellationToken c);
    Task DesmarcarAsync(Guid projeto, CancellationToken c);
    Task<IReadOnlyList<ItemDoTelegram>> CaixaAsync(Guid projeto, CancellationToken c);
    Task<byte[]> FotoAsync(Guid item, CancellationToken c);
    Task LevarAsync(Guid item, CancellationToken c);

    /// <summary>Bloco 59: põe a OS nos grupos da obra; devolve em quantos ela está.</summary>
    Task<int> PublicarOrdemAsync(Guid projeto, Guid ordem, int numero, string titulo, string texto, string situacao, CancellationToken c);
}

/// <summary>As rotas do Telegram com o token da conta (renovado uma vez se o servidor recusar).</summary>
public sealed class TelegramDaConta(ClienteDaConta cliente, IContaProjectPlanner conta) : ITelegramDaConta
{
    public Task<SituacaoDoTelegram> SituacaoAsync(CancellationToken c) => ComAcesso(a => cliente.TelegramAsync(a, c), c);
    public Task<CodigoParaOTelegram> CodigoAsync(CancellationToken c) => ComAcesso(a => cliente.CodigoDoTelegramAsync(a, c), c);
    public Task DesligarAsync(CancellationToken c) => ComAcesso(async a => { await cliente.DesligarTelegramAsync(a, c); return true; }, c);
    public Task<IReadOnlyList<ObraNoTelegram>> ObrasAsync(CancellationToken c) => ComAcesso(a => cliente.ObrasAsync(a, c), c);

    public Task MarcarAsync(Guid projeto, string nome, ContextoDoProjeto retrato, CancellationToken c) =>
        ComAcesso(async a => { await cliente.MarcarObraAsync(a, projeto, nome, retrato, c); return true; }, c);

    public Task DesmarcarAsync(Guid projeto, CancellationToken c) => ComAcesso(async a => { await cliente.DesmarcarObraAsync(a, projeto, c); return true; }, c);
    public Task<IReadOnlyList<ItemDoTelegram>> CaixaAsync(Guid projeto, CancellationToken c) => ComAcesso(a => cliente.CaixaAsync(a, projeto, c), c);
    public Task<byte[]> FotoAsync(Guid item, CancellationToken c) => ComAcesso(a => cliente.FotoDaCaixaAsync(a, item, c), c);
    public Task LevarAsync(Guid item, CancellationToken c) => ComAcesso(async a => { await cliente.LevarDaCaixaAsync(a, item, c); return true; }, c);

    public Task<int> PublicarOrdemAsync(Guid projeto, Guid ordem, int numero, string titulo, string texto, string situacao, CancellationToken c) =>
        ComAcesso(async a => (await cliente.PublicarOrdemAsync(a, projeto, ordem, numero, titulo, texto, situacao, c)).Grupos, c);

    // O token da conta; sem conta, ou com a sessão acabada, a mensagem para o usuário.
    private async Task<T> ComAcesso<T>(Func<string, Task<T>> chamada, CancellationToken c)
    {
        try
        {
            try
            {
                return await chamada(await conta.AcessoAsync(false, c));
            }
            catch (ErroDaContaException ex) when (ex.SessaoRecusada)
            {
                return await chamada(await conta.AcessoAsync(true, c));
            }
        }
        catch (ErroDoAssistenteException ex)
        {
            throw new ErroDaContaException(ex.Message, 401, ex);
        }
    }
}
