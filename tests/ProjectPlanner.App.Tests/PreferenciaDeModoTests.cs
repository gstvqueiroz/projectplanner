using System.Text.Json;
using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Tests;

// Bloco 26: "Ao pedir um cronograma" (perguntar, montar direto, estudar antes), em Configurações do assistente.
public class PreferenciaDeModoTests
{
    private readonly PreferenciasAssistenteEmMemoria _preferencias = new();
    private readonly ConfiguracaoDoAssistente _configuracao;

    public PreferenciaDeModoTests() =>
        _configuracao = new ConfiguracaoDoAssistente(_preferencias, new ProvedorFalso(), _ => null);

    [Fact]
    public void SemPreferencia_Perguntar()
    {
        _configuracao.PreferenciaDeModo.Should().Be(PreferenciaDeModo.Perguntar);
    }

    [Fact]
    public void SalvarAPreferencia_MantemOModelo_ESalvarOModelo_MantemAPreferencia()
    {
        _configuracao.SalvarModelo("gemini-2.5-pro");
        _configuracao.SalvarPreferenciaDeModo(PreferenciaDeModo.Estudo);

        _preferencias.Salvas.Should().Be(new PreferenciasAssistente("gemini-2.5-pro", PreferenciaDeModo.Estudo));

        _configuracao.SalvarModelo("gemini-2.5-flash");
        (_configuracao.PreferenciaDeModo, _configuracao.Modelo).Should().Be((PreferenciaDeModo.Estudo, "gemini-2.5-flash"));
    }

    [Fact]
    public void SemArquivoAinda_APreferenciaLevaOModeloEmUso()
    {
        _configuracao.SalvarPreferenciaDeModo(PreferenciaDeModo.Direto);

        _preferencias.Salvas.Should().Be(new PreferenciasAssistente("gemini-2.5-flash", PreferenciaDeModo.Direto));
    }

    [Fact]
    public void PreferenciaDesconhecida_Recusada()
    {
        var salvar = () => _configuracao.SalvarPreferenciaDeModo((PreferenciaDeModo)7);

        salvar.Should().Throw<ArgumentOutOfRangeException>();
        _preferencias.Salvas.Should().BeNull();
    }

    [Fact]
    public void Tela_MostraAPreferencia_ETrocarSalvaNaHora()
    {
        _configuracao.SalvarPreferenciaDeModo(PreferenciaDeModo.Direto);
        var salvasAntes = _preferencias.Salvas;
        var tela = new ConfiguracaoAssistenteViewModel(_configuracao, new DialogosFalsos());

        (tela.ModoEscolhido.Valor, tela.Mensagem).Should().Be((PreferenciaDeModo.Direto, ""), "abrir a tela não salva nada");
        _preferencias.Salvas.Should().BeSameAs(salvasAntes);
        ConfiguracaoAssistenteViewModel.OpcoesDeModo.Select(o => o.Valor)
            .Should().Equal(PreferenciaDeModo.Perguntar, PreferenciaDeModo.Direto, PreferenciaDeModo.Estudo);

        tela.ModoEscolhido = ConfiguracaoAssistenteViewModel.OpcoesDeModo[2];

        (_configuracao.PreferenciaDeModo, tela.Mensagem, tela.MensagemEhErro).Should().Be(
            (PreferenciaDeModo.Estudo, "Ao pedir um cronograma: estudar os documentos e conversar antes.", false));
    }
}

// O modo no assistente.json de verdade.
public sealed class ArquivoPreferenciasAssistenteModoTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-assistente-modo-" + Guid.NewGuid().ToString("N"));
    private string Caminho => Path.Combine(_pasta, "assistente.json");

    public void Dispose()
    {
        if (Directory.Exists(_pasta))
            Directory.Delete(_pasta, recursive: true);
    }

    private void Escrever(string conteudo)
    {
        Directory.CreateDirectory(_pasta);
        File.WriteAllText(Caminho, conteudo);
    }

    [Theory]
    [InlineData(PreferenciaDeModo.Perguntar, "perguntar")]
    [InlineData(PreferenciaDeModo.Direto, "direto")]
    [InlineData(PreferenciaDeModo.Estudo, "estudo")]
    public void SalvarECarregar_ComOModo(PreferenciaDeModo modo, string noArquivo)
    {
        new ArquivoPreferenciasAssistente(Caminho).Salvar(new PreferenciasAssistente("gemini-2.5-flash", modo));

        new ArquivoPreferenciasAssistente(Caminho).Carregar().Should().Be(new PreferenciasAssistente("gemini-2.5-flash", modo));
        File.ReadAllText(Caminho).Should().Contain($"\"modo\": \"{noArquivo}\"").And.Contain("\"versao\": 1");
    }

    [Theory]
    [InlineData("{\"versao\": 1, \"modelo\": \"gemini-2.5-pro\"}", PreferenciaDeModo.Perguntar)]
    [InlineData("{\"versao\": 1, \"modelo\": \"gemini-2.5-pro\", \"modo\": \"voando\"}", PreferenciaDeModo.Perguntar)]
    [InlineData("{\"versao\": 1, \"modelo\": \"gemini-2.5-pro\", \"modo\": \"ESTUDO\"}", PreferenciaDeModo.Estudo)]
    public void ArquivoAntigoOuModoDesconhecido_ValeComoPerguntar(string conteudo, PreferenciaDeModo esperado)
    {
        Escrever(conteudo);

        new ArquivoPreferenciasAssistente(Caminho).Carregar().Should().Be(new PreferenciasAssistente("gemini-2.5-pro", esperado));
    }

    // As versões anteriores (até a 0.25) leem o arquivo novo como antes: o campo a mais é ignorado.
    [Fact]
    public void ArquivoNovo_LidoPeloFormatoDasVersoesAnteriores()
    {
        new ArquivoPreferenciasAssistente(Caminho).Salvar(new PreferenciasAssistente("gemini-2.5-pro", PreferenciaDeModo.Estudo));

        JsonSerializer.Deserialize<FormatoAte025>(File.ReadAllText(Caminho), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
            .Should().Be(new FormatoAte025(1, "gemini-2.5-pro"));
    }

    private sealed record FormatoAte025(int Versao, string? Modelo);
}
