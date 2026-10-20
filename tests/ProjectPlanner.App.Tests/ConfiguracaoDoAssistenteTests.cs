using ProjectPlanner.App.Services;
using ProjectPlanner.App.Services.Cofre;
using ProjectPlanner.IA;
using ProjectPlanner.IA.Conta;

namespace ProjectPlanner.App.Tests;

// Blocos 23 e 46: a configuração do assistente. Desde o Bloco 46, a IA vai pela conta ProjectPlanner; a chave do Gemini só
// pela variável de ambiente, para quem compila o código. O modelo (o nível) fica no assistente.json.
public class ConfiguracaoDoAssistenteTests
{
    private const string ChaveDaVariavel = "AIzaChaveDaVariavelDeAmbiente000005678";

    private readonly PreferenciasAssistenteEmMemoria _preferencias = new();
    private readonly ProvedorFalso _provedor = new();
    private readonly ContaFalsa _conta = new();
    private readonly Dictionary<string, string?> _variaveis = [];

    private ConfiguracaoDoAssistente Nova() => new(_preferencias, _provedor, nome => _variaveis.GetValueOrDefault(nome), _conta);

    [Fact]
    public void SemContaNemVariavel_SemCredencial()
    {
        var configuracao = Nova();

        (configuracao.ChaveApi, configuracao.ChaveDeDesenvolvimento, configuracao.Modelo, configuracao.ModeloPadrao)
            .Should().Be((null, false, "gemini-2.5-flash", "gemini-2.5-flash"));
    }

    [Fact]
    public void ComAConta_ACredencialEADaConta_ESairDesliga()
    {
        var configuracao = Nova();
        _conta.Conectar();
        configuracao.ChaveApi.Should().Be(ProvedorDaConta.Credencial);

        _conta.Perfil = null;
        configuracao.ChaveApi.Should().BeNull();
    }

    [Theory]
    [InlineData("  " + ChaveDaVariavel + "  ", ChaveDaVariavel)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void VariavelDeAmbiente_ParaDesenvolvimento_TemPrioridadeSobreAConta(string? variavel, string? esperada)
    {
        _variaveis[ConfiguracaoDoAssistente.VariavelDeAmbiente] = variavel;
        _conta.Conectar();
        var configuracao = Nova();

        (configuracao.ChaveApi, configuracao.ChaveDeDesenvolvimento).Should().Be((esperada ?? ProvedorDaConta.Credencial, esperada is not null));
    }

    [Fact]
    public void Modelo_SalvoNormalizado_EArquivoMexidoVoltaAoPadrao()
    {
        var configuracao = Nova();

        configuracao.SalvarModelo(" models/claude-opus-5-5 ");

        (configuracao.Modelo, _preferencias.Salvas).Should().Be(("claude-opus-5-5", new PreferenciasAssistente("claude-opus-5-5")));
        Nova().Modelo.Should().Be("claude-opus-5-5", "fica salvo entre as sessões");

        _preferencias.Salvas = new PreferenciasAssistente("nome inválido!");
        Nova().Modelo.Should().Be("gemini-2.5-flash");

        var invalido = () => configuracao.SalvarModelo("gemini 2.5");
        invalido.Should().Throw<ArgumentException>();
        configuracao.Modelo.Should().Be("claude-opus-5-5");
    }

    [Fact]
    public async Task Testar_PelaContaOuPelaVariavel_ESemNenhumaRecusa()
    {
        var testar = () => Nova().TestarAsync("gemini-2.5-flash", CancellationToken.None);
        await testar.Should().ThrowAsync<ArgumentException>().WithMessage("Entre na sua conta ProjectPlanner (menu Conta) para testar.");
        _provedor.Testes.Should().BeEmpty();

        _conta.Conectar();
        await Nova().TestarAsync("models/gemini-2.5-pro", CancellationToken.None);
        _variaveis[ConfiguracaoDoAssistente.VariavelDeAmbiente] = ChaveDaVariavel;
        await Nova().TestarAsync("gemini-2.5-flash", CancellationToken.None);

        _provedor.Testes.Should().Equal((ProvedorDaConta.Credencial, "gemini-2.5-pro"), (ChaveDaVariavel, "gemini-2.5-flash"));
    }

    [Fact]
    public void Construtor_ArgumentosNulos_Recusados()
    {
        var acoes = new Action[]
        {
            () => _ = new ConfiguracaoDoAssistente(null!, _provedor, _ => null),
            () => _ = new ConfiguracaoDoAssistente(_preferencias, null!, _ => null),
            () => _ = new ConfiguracaoDoAssistente(_preferencias, _provedor, null!),
        };

        foreach (var acao in acoes)
            acao.Should().Throw<ArgumentNullException>();
        new ConfiguracaoDoAssistente(_preferencias, _provedor, _ => null).ChaveApi.Should().BeNull("sem conta passada, nada conectado");
    }

    [Fact]
    public async Task ComOAssistenteDeVerdade_EntrarNaContaLigaOProvedorNaHora()
    {
        var configuracao = Nova();
        var assistente = FabricaAssistenteIA.Criar(configuracao, _provedor);
        assistente.EstaConfigurado.Should().BeFalse();

        _conta.Conectar();
        configuracao.SalvarModelo("claude-opus-5-5");

        assistente.EstaConfigurado.Should().BeTrue();
        (await assistente.EnviarAsync(new PedidoIA("Oi", [], []), CancellationToken.None)).Texto.Should().Be("[claude-opus-5-5] Oi");
        _provedor.Criados.Should().Equal((ProvedorDaConta.Credencial, "claude-opus-5-5"));
    }
}

// O assistente.json de verdade, numa pasta temporária.
public sealed class ArquivoPreferenciasAssistenteTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-assistente-json-" + Guid.NewGuid().ToString("N"));
    private string Caminho => Path.Combine(_pasta, "sub", "assistente.json");

    public void Dispose()
    {
        if (Directory.Exists(_pasta))
            Directory.Delete(_pasta, recursive: true);
    }

    [Fact]
    public void SemArquivo_Nulo()
    {
        new ArquivoPreferenciasAssistente(Caminho).Carregar().Should().BeNull();
    }

    [Fact]
    public void SalvarECarregar_SoOModelo()
    {
        new ArquivoPreferenciasAssistente(Caminho).Salvar(new PreferenciasAssistente("gemini-2.5-pro"));

        new ArquivoPreferenciasAssistente(Caminho).Carregar().Should().Be(new PreferenciasAssistente("gemini-2.5-pro"));
        File.ReadAllText(Caminho).Should().Contain("\"modelo\": \"gemini-2.5-pro\"").And.Contain("\"versao\": 1").And.NotContainAny("chave", "key");
    }

    [Theory]
    [InlineData("isto não é json")]
    [InlineData("{\"versao\": 99, \"modelo\": \"gemini-2.5-pro\"}")]
    [InlineData("{\"versao\": 1, \"modelo\": \"\"}")]
    [InlineData("{\"versao\": 1}")]
    public void ArquivoInvalidoOuDeOutraVersao_Nulo(string conteudo)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Caminho)!);
        File.WriteAllText(Caminho, conteudo);

        new ArquivoPreferenciasAssistente(Caminho).Carregar().Should().BeNull();
    }

    [Fact]
    public void CaminhoPadrao_AoLadoDoColunasJson()
    {
        Path.GetDirectoryName(ArquivoPreferenciasAssistente.CaminhoPadrao())
            .Should().Be(Path.GetDirectoryName(ArquivoPreferenciasColunas.CaminhoPadrao()));
        Path.GetFileName(ArquivoPreferenciasAssistente.CaminhoPadrao()).Should().Be("assistente.json");
    }
}

// As Chaves do macOS de verdade, com um serviço só do teste (apagado no fim). Nos outros sistemas, o teste não faz nada.
public sealed class CofreMacOSTests : IDisposable
{
    private readonly string _servico = "ProjectPlanner.Testes." + Guid.NewGuid().ToString("N");

    public void Dispose()
    {
        if (OperatingSystem.IsMacOS())
            new CofreMacOS(_servico).Remover("chave");
    }

    [Fact]
    public void GravarLerTrocarERemover()
    {
        if (!OperatingSystem.IsMacOS())
            return;
        var cofre = new CofreMacOS(_servico);

        cofre.Ler("chave").Should().BeNull();
        cofre.Gravar("chave", "segredo-1 ção");
        cofre.Ler("chave").Should().Be("segredo-1 ção");
        cofre.Gravar("chave", "segredo-2");
        cofre.Ler("chave").Should().Be("segredo-2", "gravar de novo substitui");
        new CofreMacOS(_servico).Ler("outra").Should().BeNull("cada nome é um item");

        cofre.Remover("chave");
        cofre.Ler("chave").Should().BeNull();
        cofre.Remover("chave"); // sem erro se não existe
        (cofre.Nome, cofre.Disponivel).Should().Be(("Chaves do macOS", true));
    }

    [Fact]
    public void CofreDoSistema_NoMac_EAsChavesDoMacOS()
    {
        if (!OperatingSystem.IsMacOS())
            return;
        CofreDoSistema.Criar().Should().BeOfType<CofreMacOS>();
        ArgumentException? recusado = null;
        try
        {
            _ = new CofreMacOS(" ");
        }
        catch (ArgumentException ex)
        {
            recusado = ex;
        }
        recusado.Should().NotBeNull("o serviço não pode ser vazio");
    }

    [Fact]
    public void CofreIndisponivel_NaoGuardaNada()
    {
        var cofre = new CofreIndisponivel();

        (cofre.Disponivel, cofre.Ler("chave")).Should().Be((false, null));
        var gravar = () => cofre.Gravar("chave", "x");
        gravar.Should().Throw<CofreException>().WithMessage("*GEMINI_API_KEY*");
        cofre.Remover("chave");
    }
}
