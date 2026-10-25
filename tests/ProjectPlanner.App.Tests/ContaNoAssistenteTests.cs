using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.IA;
using ProjectPlanner.IA.Conta;

namespace ProjectPlanner.App.Tests;

// Blocos 45 e 46: a IA pela conta (o provedor escolhido pela credencial), os níveis como as telas mostram e o assistente.json.
public sealed class ContaNoAssistenteTests
{
    private const string Chave = "AIzaChaveDeDesenvolvimento0000000001234";

    [Fact]
    public void Arquivo_OCampoProvedorDa044_EIgnorado()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "pp-provedor-" + Guid.NewGuid().ToString("N"));
        try
        {
            var arquivo = new ArquivoPreferenciasAssistente(Path.Combine(pasta, "assistente.json"));
            Directory.CreateDirectory(pasta);
            File.WriteAllText(arquivo.Caminho, """{"versao":1,"modelo":"gemini-2.5-pro","modo":"estudo","provedor":"chave"}""");
            arquivo.Carregar().Should().Be(new PreferenciasAssistente("gemini-2.5-pro", PreferenciaDeModo.Estudo));
            arquivo.Salvar(new PreferenciasAssistente("claude-opus-5-5"));
            File.ReadAllText(arquivo.Caminho).Should().NotContain("provedor");
        }
        finally
        {
            if (Directory.Exists(pasta))
                Directory.Delete(pasta, recursive: true);
        }
    }

    [Fact]
    public void Automatico_OferecidoUmaVez_FicaNoArquivo_EAConfiguracaoTroca()
    {
        // Bloco 51
        var pasta = Path.Combine(Path.GetTempPath(), "pp-automatico-" + Guid.NewGuid().ToString("N"));
        try
        {
            var arquivo = new ArquivoPreferenciasAssistente(Path.Combine(pasta, "assistente.json"));
            arquivo.Salvar(new PreferenciasAssistente("automatico", PreferenciaDeModo.Direto, AutomaticoOferecido: true));
            File.ReadAllText(arquivo.Caminho).Should().Contain("\"automaticoOferecido\": true");
            arquivo.Carregar().Should().Be(new PreferenciasAssistente("automatico", PreferenciaDeModo.Direto, true));
            arquivo.Salvar(new PreferenciasAssistente("claude-haiku-5-5"));
            File.ReadAllText(arquivo.Caminho).Should().NotContain("automaticoOferecido", "sem o campo, as versões anteriores leem igual");
        }
        finally
        {
            if (Directory.Exists(pasta))
                Directory.Delete(pasta, recursive: true);
        }

        var preferencias = new PreferenciasAssistenteEmMemoria { Salvas = new PreferenciasAssistente("gemini-2.5-flash", PreferenciaDeModo.Estudo) };
        var configuracao = new ConfiguracaoDoAssistente(preferencias, new ProvedorFalso(), _ => null);
        IConfiguracaoAssistente comInterface = configuracao;
        comInterface.AutomaticoOferecido.Should().BeFalse();
        comInterface.OferecerAutomatico(trocar: true);
        (configuracao.Modelo, preferencias.Salvas).Should().Be(("automatico", new PreferenciasAssistente("automatico", PreferenciaDeModo.Estudo, true)));
        comInterface.OferecerAutomatico(trocar: false);
        configuracao.Modelo.Should().Be("automatico", "sem trocar, fica o que estava");
    }

    [Fact]
    public void ProvedorConformeCredencial_AMarcaVaiPelaConta_OResto_PelaChave()
    {
        var chave = new ProvedorFalso();
        var conta = new ProvedorFalso();
        var provedor = new ProvedorConformeCredencial(chave, conta);

        provedor.CriarAssistente(ProvedorDaConta.Credencial, "gemini-2.5-flash");
        provedor.CriarAssistente(Chave, "gemini-2.5-pro");
        _ = provedor.TestarAsync(ProvedorDaConta.Credencial, "gemini-2.5-flash", default);

        (conta.Criados.Single(), chave.Criados.Single(), conta.Testes.Single().Chave)
            .Should().Be(((ProvedorDaConta.Credencial, "gemini-2.5-flash"), (Chave, "gemini-2.5-pro"), ProvedorDaConta.Credencial));
        (provedor.Nome, provedor.ModeloPadrao, provedor.NormalizarModelo("models/gemini-2.5-pro")).Should().Be(("Gemini (Google)", "gemini-2.5-flash", "gemini-2.5-pro"));
        provedor.CriarAnalista(ProvedorDaConta.Credencial, "m").Should().NotBeNull();
        provedor.CriarConsultor(Chave, "m").Should().NotBeNull();
        provedor.CriarAnalistaDaObra(Chave, "m").Should().NotBeNull();
    }

    [Fact]
    public void ProvedorDaConta_NomeEModelos_InclusiveOClaude()
    {
        var provedor = new ProvedorDaConta(new HttpClient(), new Uri("https://servidor.teste"), new ContaFalsa());
        (provedor.Nome, provedor.ModeloPadrao, provedor.NormalizarModelo(" models/claude-opus-5-5 ")).Should().Be(("Conta ProjectPlanner", "gemini-2.5-flash", "claude-opus-5-5"));
        FluentActions.Invoking(() => provedor.CriarAssistente(ProvedorDaConta.Credencial, "Modelo Inválido!")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void OpcaoDeNivel_TextosEAEscolha()
    {
        var opcoes = OpcaoDeNivel.De([.. ContaFalsa.ModelosDoGratis, new ModeloDaConta("gemini-x", "padrao", "Gemini X", true, false, 9)]);

        opcoes.Select(o => (o.TextoCompleto, o.Opacidade)).Should().Equal(
            ("Padrão: Gemini 2.5 Flash (cerca de 14 créditos por pedido)", 1.0),
            ("Avançado: Gemini 2.5 Pro (do plano Profissional)", 0.5),
            ("Máximo: Claude Opus 5.5 (do plano Profissional)", 0.5),
            ("Padrão: Gemini X (indisponível no momento)", 0.5));
        (OpcaoDeNivel.Escolhida(opcoes, "gemini-2.5-flash")!.Id, OpcaoDeNivel.Escolhida(opcoes, "claude-opus-5-5")!.Id,
            OpcaoDeNivel.Escolhida(opcoes, "gemini-x")!.Id, OpcaoDeNivel.Escolhida([], "gemini-2.5-flash")).Should().Be(
            ("gemini-2.5-flash", "gemini-2.5-flash", "gemini-2.5-flash", null));
        new ModeloDaConta("a", "outro", "A", true, true, 1).NomeDoNivel.Should().Be("Padrão");
        new OpcaoDeNivel(new ModeloDaConta("gemini-3-flash-preview", "intermediario", "Gemini 3 Flash", true, true, 19)).TextoCompleto
            .Should().Be("Intermediário: Gemini 3 Flash (cerca de 19 créditos por pedido)", "Bloco 50: o nível entre o Padrão e o Avançado");
    }
}
