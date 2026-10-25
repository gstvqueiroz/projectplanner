using Microsoft.Extensions.Options;
using ProjectPlanner.Servidor.Dados;

namespace ProjectPlanner.Servidor.Servicos;

/// <summary>
/// 0.63.1: a conta da Anthropic do servidor ficou sem crédito ("Your credit balance is too low"). Em vez de mostrar o erro a
/// cada pedido, os pedidos do Claude vão para o Gemini (o substituto do mesmo nível) por <see cref="Pausa"/>; o dono do
/// servidor fica sabendo pelo painel (/admin, erros do servidor) e por e-mail, no máximo a cada <see cref="IntervaloDoAviso"/>.
/// </summary>
public sealed class SaudeDosProvedores(TimeProvider relogio)
{
    public static readonly TimeSpan Pausa = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan IntervaloDoAviso = TimeSpan.FromHours(6);

    public const string MensagemDoAviso =
        "A conta da Anthropic do servidor está sem crédito: os pedidos do Claude (Padrão e Máximo) estão indo para o Gemini. " +
        "Adicione crédito em console.anthropic.com > Plans & Billing; o Claude volta sozinho em até 15 minutos.";

    private readonly Lock _trava = new();
    private DateTime _semCreditoAte;
    private DateTime _ultimoAviso = DateTime.MinValue;

    /// <summary>A Anthropic recusou por crédito há menos de <see cref="Pausa"/>: nem tenta, vai direto ao substituto.</summary>
    public bool AnthropicSemCredito
    {
        get
        {
            lock (_trava)
                return relogio.GetUtcNow().UtcDateTime < _semCreditoAte;
        }
    }

    /// <summary>A recusa da Anthropic é por falta de crédito na conta (e não um problema do pedido).</summary>
    public static bool EhFaltaDeCredito(int status, string? texto) =>
        status is 400 or 402 && texto is not null && texto.Contains("credit balance", StringComparison.OrdinalIgnoreCase);

    /// <returns>Se é hora de avisar o dono (a primeira vez, ou passado o intervalo).</returns>
    public bool MarcarSemCredito()
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        lock (_trava)
        {
            _semCreditoAte = agora + Pausa;
            if (agora - _ultimoAviso < IntervaloDoAviso)
                return false;
            _ultimoAviso = agora;
            return true;
        }
    }

    /// <summary>Registra no painel (erros do servidor) e manda o e-mail ao dono, sem nunca falhar por isso.</summary>
    public static async Task AvisarAsync(BancoDoServidor banco, OpcoesDoServidor opcoes, IEnviadorDeEmail email, DateTime agora, ILogger log, CancellationToken c)
    {
        log.LogError("A conta da Anthropic está sem crédito; os pedidos do Claude vão para o Gemini");
        try
        {
            banco.FalhasDoServidor.Add(new FalhaDoServidor
            {
                Codigo = Falhas.NovoCodigo(), Quando = agora, Rota = "IA → Anthropic", Tipo = "Anthropic sem crédito", Mensagem = MensagemDoAviso,
            });
            await banco.SaveChangesAsync(c);
            if (!string.IsNullOrWhiteSpace(opcoes.Admin.Email))
                await email.EnviarAsync(opcoes.Admin.Email.Trim(), "ProjectPlanner: a conta da Anthropic está sem crédito", MensagemDoAviso +
                    "\n\nEnquanto isso, ninguém fica sem resposta: o Gemini atende no lugar. Veja os erros no painel: /admin", c);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Não deu para registrar ou avisar a falta de crédito da Anthropic");
        }
    }
}
