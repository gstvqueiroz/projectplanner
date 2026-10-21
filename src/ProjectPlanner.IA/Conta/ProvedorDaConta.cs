using ProjectPlanner.IA.Gemini;

namespace ProjectPlanner.IA.Conta;

/// <summary>
/// Bloco 45: a IA pela conta ProjectPlanner. As mesmas classes do Gemini, pelo servidor (<see cref="AcessoPelaConta"/>): o
/// usuário não precisa de chave, e o servidor confere a cota do plano. A "chave" que os outros provedores recebem aqui é só
/// a marca <see cref="Credencial"/> (o token vem da sessão).
/// </summary>
public sealed class ProvedorDaConta : IProvedorIA
{
    /// <summary>A credencial que a configuração usa para dizer "pela conta".</summary>
    public const string Credencial = "conta-projectplanner";

    private readonly HttpClient _http;
    private readonly AcessoPelaConta _acesso;
    private readonly ProvedorGemini _regras;

    public ProvedorDaConta(HttpClient http, Uri servidor, ISessaoDaConta sessao)
    {
        ArgumentNullException.ThrowIfNull(http);
        (_http, _acesso, _regras) = (http, new AcessoPelaConta(servidor, sessao), new ProvedorGemini(http));
    }

    public string Nome => "Conta ProjectPlanner";

    public string ModeloPadrao => ProvedorGemini.ModeloRecomendado;

    public string NormalizarModelo(string modelo) => _regras.NormalizarModelo(modelo);

    public IAssistenteIA CriarAssistente(string chave, string modelo) => new AssistenteGemini(_http, _acesso, NormalizarModelo(modelo));

    public IAnalistaDeFotos CriarAnalista(string chave, string modelo) => new AnalistaGemini(_http, _acesso, NormalizarModelo(modelo));

    public IConsultorDeOrcamento CriarConsultor(string chave, string modelo) => new ConsultorGemini(_http, _acesso, NormalizarModelo(modelo));

    public IAnalistaDaObra CriarAnalistaDaObra(string chave, string modelo) => new AnalistaDaObraGemini(_http, _acesso, NormalizarModelo(modelo));

    /// <summary>Confere a conta e o modelo no servidor (sem ir ao Gemini nem gastar a cota).</summary>
    public async Task TestarAsync(string chave, string modelo, CancellationToken cancelamento)
    {
        var nome = NormalizarModelo(modelo);
        using var _ = await ChamadaGemini.ExecutarAsync(_http, _acesso, HttpMethod.Get, $"models/{nome}", null, nome, null, cancelamento);
    }
}

/// <summary>
/// Bloco 45: escolhe o provedor pela credencial: a marca <see cref="ProvedorDaConta.Credencial"/> vai pela conta; qualquer
/// outra é uma chave do Gemini. Assim o assistente, as fotos, o orçamento e os indicadores trocam juntos.
/// </summary>
public sealed class ProvedorConformeCredencial(IProvedorIA chavePropria, IProvedorIA conta) : IProvedorIA
{
    private IProvedorIA Para(string chave) => chave == ProvedorDaConta.Credencial ? conta : chavePropria;

    public string Nome => chavePropria.Nome;

    public string ModeloPadrao => chavePropria.ModeloPadrao;

    public string NormalizarModelo(string modelo) => chavePropria.NormalizarModelo(modelo);

    public IAssistenteIA CriarAssistente(string chave, string modelo) => Para(chave).CriarAssistente(chave, modelo);

    public IAnalistaDeFotos CriarAnalista(string chave, string modelo) => Para(chave).CriarAnalista(chave, modelo);

    public IConsultorDeOrcamento CriarConsultor(string chave, string modelo) => Para(chave).CriarConsultor(chave, modelo);

    public IAnalistaDaObra CriarAnalistaDaObra(string chave, string modelo) => Para(chave).CriarAnalistaDaObra(chave, modelo);

    public Task TestarAsync(string chave, string modelo, CancellationToken cancelamento) => Para(chave).TestarAsync(chave, modelo, cancelamento);
}
