using System.Reflection;

namespace ProjectPlanner.IA.Tests;

// Blocos 19 e 23: a camada do assistente (o indisponível, a escolha conforme a configuração, os anexos).
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

    // Bloco 23: a rede entrou, mas só no provedor do Gemini; o resto da camada continua sem saber de HTTP.
    [Fact]
    public void CamadaDeIA_SoOGeminiUsaRede_ENadaDoDominio()
    {
        var assembly = typeof(IAssistenteIA).Assembly;

        var comRede = assembly.GetTypes()
            .Where(t => t.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(f => (f.FieldType.FullName ?? "").StartsWith("System.Net")))
            .Select(t => t.Namespace);
        comRede.Should().NotBeEmpty().And.OnlyContain(n => n == "ProjectPlanner.IA.Gemini");
        assembly.GetReferencedAssemblies().Select(a => a.Name).Should().NotContain(n => n!.StartsWith("ProjectPlanner"),
            "a camada de IA não depende do domínio: recebe o retrato do cronograma pronto");
    }

    [Fact]
    public async Task Fabrica_SemChave_RespondeComoIndisponivel()
    {
        var provedor = new ProvedorDeTeste();
        var assistente = new AssistenteConformeConfiguracao(new SemChaveDeApi(), provedor, new AssistenteIAIndisponivel(TimeSpan.Zero));

        var resposta = await assistente.EnviarAsync(Pedido, CancellationToken.None);

        assistente.EstaConfigurado.Should().BeFalse();
        resposta.Texto.Should().StartWith(AssistenteIAIndisponivel.Resposta);
        provedor.Criados.Should().BeEmpty();
        (new SemChaveDeApi().ChaveApi, new SemChaveDeApi().Modelo).Should().Be((null, "gemini-2.5-flash"));
        FabricaAssistenteIA.Criar(new SemChaveDeApi(), provedor).EstaConfigurado.Should().BeFalse();
    }

    [Fact]
    public async Task Fabrica_ComChave_UsaOProvedor_ETrocaQuandoAChaveOuOModeloMudam()
    {
        var configuracao = new ConfiguracaoFixa("chave-1");
        var provedor = new ProvedorDeTeste();
        var assistente = FabricaAssistenteIA.Criar(configuracao, provedor);

        assistente.EstaConfigurado.Should().BeTrue();
        (await assistente.EnviarAsync(Pedido, CancellationToken.None)).Texto.Should().Be("chave-1/gemini-2.5-flash: Qual é o caminho crítico?");
        await assistente.EnviarAsync(Pedido, CancellationToken.None);
        provedor.Criados.Should().ContainSingle("a mesma configuração reaproveita o assistente");

        configuracao.Modelo = "gemini-2.5-pro";
        (await assistente.EnviarAsync(Pedido, CancellationToken.None)).Texto.Should().StartWith("chave-1/gemini-2.5-pro");
        configuracao.ChaveApi = "chave-2";
        (await assistente.EnviarAsync(Pedido, CancellationToken.None)).Texto.Should().StartWith("chave-2/");
        provedor.Criados.Should().HaveCount(3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Fabrica_ChaveEmBranco_NaoEstaConfigurado(string? chave)
    {
        FabricaAssistenteIA.Criar(new ConfiguracaoFixa(chave), new ProvedorDeTeste()).EstaConfigurado.Should().BeFalse();
    }

    [Fact]
    public async Task Fabrica_ChaveRemovida_VoltaAoIndisponivel()
    {
        var configuracao = new ConfiguracaoFixa("chave-1");
        var assistente = new AssistenteConformeConfiguracao(configuracao, new ProvedorDeTeste(), new AssistenteIAIndisponivel(TimeSpan.Zero));

        configuracao.ChaveApi = null;

        assistente.EstaConfigurado.Should().BeFalse();
        (await assistente.EnviarAsync(Pedido, CancellationToken.None)).Texto.Should().StartWith(AssistenteIAIndisponivel.Resposta);
    }

    [Fact]
    public async Task Fabrica_ArgumentosNulos_Recusados()
    {
        var semConfiguracao = () => FabricaAssistenteIA.Criar(null!, new ProvedorDeTeste());
        var semProvedor = () => FabricaAssistenteIA.Criar(new SemChaveDeApi(), null!);
        var semPedido = () => FabricaAssistenteIA.Criar(new ConfiguracaoFixa("x"), new ProvedorDeTeste()).EnviarAsync(null!, CancellationToken.None);

        semConfiguracao.Should().Throw<ArgumentNullException>();
        semProvedor.Should().Throw<ArgumentNullException>();
        await semPedido.Should().ThrowAsync<ArgumentNullException>();
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
}
