using ProjectPlanner.App.Services;
using ProjectPlanner.App.Services.Cofre;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Tests;

// Bloco 23: a chave do assistente no cofre (com a variável de ambiente para desenvolvimento) e o modelo.
public class ConfiguracaoDoAssistenteTests
{
    private const string Chave = "AIzaChaveDoCofreParaTestes0000000001234";
    private const string ChaveDaVariavel = "AIzaChaveDaVariavelDeAmbiente000005678";

    private readonly CofreEmMemoria _cofre = new();
    private readonly PreferenciasAssistenteEmMemoria _preferencias = new();
    private readonly ProvedorFalso _provedor = new();
    private readonly Dictionary<string, string?> _variaveis = [];

    private ConfiguracaoDoAssistente Nova() => new(_cofre, _preferencias, _provedor, nome => _variaveis.GetValueOrDefault(nome));

    [Fact]
    public void SemChave_NenhumaOrigem()
    {
        var configuracao = Nova();

        (configuracao.ChaveApi, configuracao.Origem, configuracao.FinalDaChave, configuracao.ProblemaNoCofre)
            .Should().Be((null, OrigemDaChave.Nenhuma, null, null));
        (configuracao.NomeDoProvedor, configuracao.NomeDoCofre, configuracao.CofreDisponivel, configuracao.Modelo)
            .Should().Be(("Gemini (Google)", "Chaves do macOS", true, "gemini-2.5-flash"));
    }

    [Fact]
    public void ChaveDoCofre_TemPrioridadeSobreAVariavel_ECofreELidoUmaVezSo()
    {
        _cofre.Segredos[ConfiguracaoDoAssistente.NomeNoCofre] = Chave;
        _variaveis[ConfiguracaoDoAssistente.VariavelDeAmbiente] = ChaveDaVariavel;
        var configuracao = Nova();

        (configuracao.ChaveApi, configuracao.Origem, configuracao.FinalDaChave).Should().Be((Chave, OrigemDaChave.Cofre, "…1234"));
        _ = configuracao.ChaveApi;
        _cofre.Leituras.Should().Be(1);
    }

    [Fact]
    public void SoAVariavelDeAmbiente_ServeParaDesenvolvimento()
    {
        _variaveis[ConfiguracaoDoAssistente.VariavelDeAmbiente] = "  " + ChaveDaVariavel + "\n";

        var configuracao = Nova();

        (configuracao.ChaveApi, configuracao.Origem, configuracao.FinalDaChave)
            .Should().Be((ChaveDaVariavel, OrigemDaChave.VariavelDeAmbiente, "…5678"));
    }

    [Fact]
    public void SalvarChave_VaiParaOCofre_SemEspacosDasPontas_EValeNaHora()
    {
        var configuracao = Nova();

        configuracao.SalvarChave("  " + Chave + " \n");

        _cofre.Segredos.Should().Equal(new Dictionary<string, string> { [ConfiguracaoDoAssistente.NomeNoCofre] = Chave });
        (configuracao.ChaveApi, configuracao.Origem).Should().Be((Chave, OrigemDaChave.Cofre));
        _preferencias.Salvas.Should().BeNull("a chave nunca vai para o assistente.json");
    }

    [Theory]
    [InlineData("", "Cole a chave de API.")]
    [InlineData("   ", "Cole a chave de API.")]
    [InlineData("AIza com espaco", "A chave não pode ter espaços nem quebras de linha.")]
    [InlineData("AIza\nquebrada", "A chave não pode ter espaços nem quebras de linha.")]
    public void SalvarChave_Invalida_NaoMexeNoCofre(string chave, string mensagem)
    {
        var salvar = () => Nova().SalvarChave(chave);

        salvar.Should().Throw<ArgumentException>().Which.Message.Should().Be(mensagem);
        _cofre.Segredos.Should().BeEmpty();
    }

    [Fact]
    public void SalvarChave_LongaDemais_Recusada()
    {
        var salvar = () => Nova().SalvarChave(new string('a', 513));

        salvar.Should().Throw<ArgumentException>().WithMessage("A chave tem 513 caracteres; o máximo é 512.");
    }

    [Fact]
    public void RemoverChave_VoltaParaAVariavel_OuParaNenhuma()
    {
        _cofre.Segredos[ConfiguracaoDoAssistente.NomeNoCofre] = Chave;
        var configuracao = Nova();
        _ = configuracao.ChaveApi;

        configuracao.RemoverChave();
        configuracao.Origem.Should().Be(OrigemDaChave.Nenhuma);
        _cofre.Segredos.Should().BeEmpty();

        _variaveis[ConfiguracaoDoAssistente.VariavelDeAmbiente] = ChaveDaVariavel;
        configuracao.Origem.Should().Be(OrigemDaChave.VariavelDeAmbiente);
    }

    [Fact]
    public void CofreBloqueado_AoLer_UsaAVariavel_EGuardaOProblema()
    {
        _cofre.Falha = new CofreException("Não foi possível ler a chave: as Chaves do macOS estão bloqueadas ou recusaram o acesso.");
        _variaveis[ConfiguracaoDoAssistente.VariavelDeAmbiente] = ChaveDaVariavel;

        var configuracao = Nova();

        (configuracao.ChaveApi, configuracao.Origem).Should().Be((ChaveDaVariavel, OrigemDaChave.VariavelDeAmbiente));
        configuracao.ProblemaNoCofre.Should().StartWith("Não foi possível ler a chave");
    }

    [Fact]
    public void CofreRecusaGravar_AChaveNaoMuda()
    {
        var configuracao = Nova();
        _cofre.Falha = new CofreException("Não foi possível salvar a chave nas Chaves do macOS (erro -25293).");

        var salvar = () => configuracao.SalvarChave(Chave);

        salvar.Should().Throw<CofreException>();
        configuracao.Origem.Should().Be(OrigemDaChave.Nenhuma);
    }

    [Fact]
    public void Modelo_SalvoNormalizado_EArquivoMexidoVoltaAoPadrao()
    {
        var configuracao = Nova();

        configuracao.SalvarModelo(" models/gemini-2.5-pro ");

        (configuracao.Modelo, _preferencias.Salvas).Should().Be(("gemini-2.5-pro", new PreferenciasAssistente("gemini-2.5-pro")));
        Nova().Modelo.Should().Be("gemini-2.5-pro", "fica salvo entre as sessões");

        _preferencias.Salvas = new PreferenciasAssistente("nome inválido!");
        Nova().Modelo.Should().Be("gemini-2.5-flash");

        var invalido = () => configuracao.SalvarModelo("gemini 2.5");
        invalido.Should().Throw<ArgumentException>();
        configuracao.Modelo.Should().Be("gemini-2.5-pro");
    }

    [Fact]
    public async Task Testar_UsaAChaveDigitada_OuAEmUso()
    {
        _cofre.Segredos[ConfiguracaoDoAssistente.NomeNoCofre] = Chave;
        var configuracao = Nova();

        await configuracao.TestarAsync(" AIzaDigitadaNaTela ", "gemini-2.5-flash", CancellationToken.None);
        await configuracao.TestarAsync(null, "models/gemini-2.5-pro", CancellationToken.None);

        _provedor.Testes.Should().Equal(("AIzaDigitadaNaTela", "gemini-2.5-flash"), (Chave, "gemini-2.5-pro"));
    }

    [Fact]
    public async Task Testar_SemNenhumaChave_Recusado()
    {
        var testar = () => Nova().TestarAsync("", "gemini-2.5-flash", CancellationToken.None);

        await testar.Should().ThrowAsync<ArgumentException>().WithMessage("Cole a chave de API para testar (ou salve uma antes).");
        _provedor.Testes.Should().BeEmpty();
    }

    [Fact]
    public void Construtor_ArgumentosNulos_Recusados()
    {
        var acoes = new Action[]
        {
            () => _ = new ConfiguracaoDoAssistente(null!, _preferencias, _provedor, _ => null),
            () => _ = new ConfiguracaoDoAssistente(_cofre, null!, _provedor, _ => null),
            () => _ = new ConfiguracaoDoAssistente(_cofre, _preferencias, null!, _ => null),
            () => _ = new ConfiguracaoDoAssistente(_cofre, _preferencias, _provedor, null!),
        };

        foreach (var acao in acoes)
            acao.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ComOAssistenteDeVerdade_SalvarAChaveLigaOProvedorNaHora()
    {
        var configuracao = Nova();
        var assistente = FabricaAssistenteIA.Criar(configuracao, _provedor);
        assistente.EstaConfigurado.Should().BeFalse();

        configuracao.SalvarChave(Chave);
        configuracao.SalvarModelo("gemini-2.5-pro");

        assistente.EstaConfigurado.Should().BeTrue();
        (await assistente.EnviarAsync(new PedidoIA("Oi", [], []), CancellationToken.None)).Texto.Should().Be("[gemini-2.5-pro] Oi");
        _provedor.Criados.Should().Equal((Chave, "gemini-2.5-pro"));
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
