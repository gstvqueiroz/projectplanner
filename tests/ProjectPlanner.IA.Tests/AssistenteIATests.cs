using System.Reflection;

namespace ProjectPlanner.IA.Tests;

// Bloco 19: a camada do assistente sem integração real (sem rede, sem leitura de arquivos).
public class AssistenteIATests
{
    private static readonly PedidoIA Pedido = new("Qual é o caminho crítico?", [], []);

    [Fact]
    public async Task Indisponivel_RespondeQueNaoEstaConfigurado()
    {
        var assistente = new AssistenteIAIndisponivel(TimeSpan.Zero);

        var resposta = await assistente.EnviarAsync(Pedido, CancellationToken.None);

        assistente.EstaConfigurado.Should().BeFalse();
        resposta.Texto.Should().StartWith(AssistenteIAIndisponivel.Resposta).And.Contain("chave de API");
    }

    [Theory]
    [InlineData(1, "(nem o anexo, que não foi lido)")]
    [InlineData(3, "(nem os 3 anexos, que não foram lidos)")]
    public async Task Indisponivel_ComAnexos_DizQueElesNaoForamLidos(int quantos, string trecho)
    {
        var anexos = Enumerable.Range(1, quantos).Select(i => new AnexoIA($"/nao/existe/doc{i}.pdf", 100)).ToList();

        var resposta = await new AssistenteIAIndisponivel(TimeSpan.Zero).EnviarAsync(Pedido with { Anexos = anexos }, CancellationToken.None);

        resposta.Texto.Should().Contain(trecho, "o anexo nem precisa existir: nada é aberto");
    }

    [Fact]
    public async Task Indisponivel_Cancelado_DuranteAEspera()
    {
        using var cancelamento = new CancellationTokenSource();
        var envio = new AssistenteIAIndisponivel(TimeSpan.FromSeconds(30)).EnviarAsync(Pedido, cancelamento.Token);

        cancelamento.Cancel();

        await envio.Invoking(e => e).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Indisponivel_PedidoNulo_Recusado()
    {
        var enviar = () => new AssistenteIAIndisponivel(TimeSpan.Zero).EnviarAsync(null!, CancellationToken.None);

        await enviar.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Indisponivel_EsperaPadrao_MeioSegundo()
    {
        AssistenteIAIndisponivel.EsperaPadrao.Should().Be(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void CamadaDeIA_NaoReferenciaNadaDeRede()
    {
        var assembly = typeof(IAssistenteIA).Assembly;

        assembly.GetReferencedAssemblies().Select(a => a.Name)
            .Should().NotContain(n => n!.StartsWith("System.Net"), "nenhuma chamada de rede nesta fase");
        var tiposUsados = assembly.GetTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(f => f.FieldType))
            .Select(t => t.FullName ?? "");
        tiposUsados.Should().NotContain(n => n.StartsWith("System.Net"));
    }

    [Fact]
    public void Fabrica_SemChave_UsaOIndisponivel()
    {
        FabricaAssistenteIA.Criar(new SemChaveDeApi()).Should().BeOfType<AssistenteIAIndisponivel>();
        new SemChaveDeApi().ChaveApi.Should().BeNull();
    }

    [Fact]
    public void Fabrica_ComChave_AindaSemImplementacaoReal_ContinuaIndisponivel()
    {
        FabricaAssistenteIA.Criar(new ChaveFixa("sk-teste")).EstaConfigurado.Should().BeFalse();
    }

    [Fact]
    public void Fabrica_ConfiguracaoNula_Recusada()
    {
        var criar = () => FabricaAssistenteIA.Criar(null!);

        criar.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Anexo_DoArquivo_PegaNomeExtensaoETamanhoSemAbrir()
    {
        var caminho = Path.Combine(Path.GetTempPath(), $"Planta Baixa {Guid.NewGuid():N}.PDF");
        File.WriteAllBytes(caminho, new byte[2_500_000]);
        try
        {
            var anexo = AnexoIA.DoArquivo(caminho);

            (anexo.Nome, anexo.Extensao, anexo.TamanhoEmBytes, anexo.TamanhoLegivel)
                .Should().Be((Path.GetFileName(caminho), "pdf", 2_500_000L, "2,4 MB"));
        }
        finally
        {
            File.Delete(caminho);
        }
    }

    [Theory]
    [InlineData(830, "830 bytes")]
    [InlineData(12_300, "12 KB")]
    [InlineData(0, "0 bytes")]
    public void Anexo_TamanhoLegivel(long bytes, string esperado)
    {
        new AnexoIA("/x/nota.txt", bytes).TamanhoLegivel.Should().Be(esperado);
    }

    [Fact]
    public void Anexo_SemExtensao_ExtensaoVazia()
    {
        new AnexoIA("/x/LEIAME", 1).Extensao.Should().BeEmpty();
    }

    [Fact]
    public void Anexo_ArquivoQueNaoExiste_Recusado()
    {
        var anexar = () => AnexoIA.DoArquivo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "sumiu.pdf"));
        var semCaminho = () => AnexoIA.DoArquivo("  ");

        anexar.Should().Throw<FileNotFoundException>();
        semCaminho.Should().Throw<ArgumentException>();
    }

    private sealed class ChaveFixa(string chave) : IConfiguracaoIA
    {
        public string? ChaveApi => chave;
    }
}
