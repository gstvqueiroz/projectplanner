using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.IA.Conta;

namespace ProjectPlanner.App.Tests;

// Bloco 69: o aviso de versão nova (e da que o servidor não aceita mais) e os erros inesperados mandados ao suporte, só com a
// opção ligada em Minha conta e sem as pastas do usuário.
public sealed class VersaoEErrosTests
{
    private static readonly PerfilDaConta Ana = new("ana@exemplo.com", "Ana", "gratis", true, false);

    [Theory]
    [InlineData("0.69.0", "0.69.0", null, null)]
    [InlineData("0.69.0", "0.70.0", null, "Versão nova: 0.70.0")]
    [InlineData("0.69.0", "0.70.1", "0.69.0", "Versão nova: 0.70.1")]
    [InlineData("0.59.2", "0.70.0", "0.60.0", "!Esta versão (0.59.2) não funciona mais com o servidor: atualize para a 0.70.0")]
    [InlineData("0.59.2", null, "0.60", "!Esta versão (0.59.2) não funciona mais com o servidor: atualize para a 0.60.0")]
    [InlineData("0.71.0", "0.70.0", null, null)]
    [InlineData("0.69.0", "lixo", null, null)]
    public void AvisoDeVersao_PeloQueOServidorAnuncia(string atual, string? ultima, string? minima, string? esperado)
    {
        var aviso = AvisoDeVersao.Montar(atual, new VersaoNoServidor(ultima, minima, null));

        if (esperado is null)
            aviso.Should().BeNull();
        else
            (aviso!.Texto, aviso.Urgente).Should().Be((esperado.TrimStart('!'), esperado.StartsWith('!')));
        AvisoDeVersao.Montar(atual, null).Should().BeNull("sem resposta do servidor, nada");
    }

    [Fact]
    public void AvisoDeVersao_SoBaixaDeEnderecoDaWeb()
    {
        AvisoDeVersao.Montar("0.1.0", new VersaoNoServidor("0.2.0", null, "https://exemplo.com/baixar"))!.Download.Should().Be(new Uri("https://exemplo.com/baixar"));
        AvisoDeVersao.Montar("0.1.0", new VersaoNoServidor("0.2.0", null, "file:///etc/passwd"))!.Download.Should().BeNull();
        AvisoDeVersao.Montar("0.1.0", new VersaoNoServidor("0.2.0", null, "não é endereço"))!.Download.Should().BeNull();
    }

    [Fact]
    public async Task JanelaPrincipal_MostraOAviso_EBaixa()
    {
        var ambiente = new Ambiente();
        ambiente.Conta.VersaoDoServidor = new VersaoNoServidor("99.0.0", "0.1.0", "https://exemplo.com/projectplanner");
        var vm = ambiente.CriarViewModel();

        vm.TemAvisoDeVersao.Should().BeFalse("só depois de perguntar ao servidor");
        await vm.VerificarVersaoAsync();

        (vm.TemAvisoDeVersao, vm.TextoDoAvisoDeVersao).Should().Be((true, "Versão nova: 99.0.0 · Baixar"));
        await vm.BaixarVersaoNovaCommand.ExecuteAsync(null);
        ambiente.Dialogos.EnderecosAbertos.Should().Equal(new Uri("https://exemplo.com/projectplanner"));

        ambiente.Conta.VersaoDoServidor = new VersaoNoServidor(null, "99.0.0", null);
        await vm.VerificarVersaoAsync();
        vm.Mensagem.Should().StartWith("Esta versão (").And.EndWith("não funciona mais com o servidor: atualize para a 99.0.0.");
        vm.BaixarVersaoNovaCommand.CanExecute(null).Should().BeFalse("o servidor não disse onde baixar");
    }

    [Fact]
    public void ErroParaOSuporte_SemAsPastasDoUsuario_EComOErroDeDentro()
    {
        Exception erro;
        try
        {
            throw new FileNotFoundException("Não achei /Users/ana/Documents/Obras/Casa da Rua A.pplan");
        }
        catch (Exception ex)
        {
            erro = new AggregateException(ex);
        }

        var mandado = EnvioDeErros.Montar(erro, "interface", "/Users/ana");

        (mandado.Tipo, mandado.Origem, mandado.Versao).Should().Be(("System.IO.FileNotFoundException", "interface", VersaoDoProduto.Atual));
        mandado.Mensagem.Should().Be("Não achei ~/Documents/Obras/Casa da Rua A.pplan");
        mandado.Pilha.Should().Contain(nameof(ErroParaOSuporte_SemAsPastasDoUsuario_EComOErroDeDentro))
            .And.Contain(" in tests/ProjectPlanner.App.Tests/VersaoEErrosTests.cs:line").And.NotContain("/Users/").And.NotContain("Documents/Project");
        mandado.Sistema.Should().NotBeNullOrWhiteSpace();
        EnvioDeErros.Montar(new InvalidOperationException("x"), "fatal", "").Pilha.Should().BeNull("um erro que nunca foi lançado não tem pilha");
    }

    [Fact]
    public async Task Envio_SoComAOpcaoLigada_EAContaConectada_UmaVezCadaErro()
    {
        var conta = new ContaFalsa();
        var preferencias = new PreferenciasInterfaceEmMemoria();
        var envio = new EnvioDeErros(conta, preferencias);
        var erro = new InvalidOperationException("Algo deu errado");

        await envio.EnviarAsync(erro, "interface");
        conta.Perfil = Ana;
        await envio.EnviarAsync(erro, "interface");
        conta.ErrosEnviados.Should().BeEmpty("a opção começa desligada");

        preferencias.Salvo = new PreferenciasInterface(true, EnviarErros: true);
        await envio.EnviarAsync(erro, "interface");
        await envio.EnviarAsync(erro, "interface");
        for (var i = 0; i < EnvioDeErros.MaximoPorSessao + 5; i++)
            await envio.EnviarAsync(new InvalidOperationException($"Erro {i}"), "tarefa em segundo plano");

        conta.ErrosEnviados.Should().HaveCount(EnvioDeErros.MaximoPorSessao, "o mesmo erro vai uma vez, e no máximo 10 por sessão");
        conta.ErrosEnviados.First().Mensagem.Should().Be("Algo deu errado");
    }

    [Fact]
    public void TratamentoDeErros_RegistraEDepoisManda()
    {
        var pasta = Path.Combine(Path.GetTempPath(), $"pp-erros-{Guid.NewGuid():N}");
        try
        {
            var mandados = new List<(string Tipo, string Origem)>();
            var tratamento = new TratamentoDeErros(new RegistroDeErros(pasta, TimeProvider.System), _ => { })
            {
                Enviar = (erro, origem) =>
                {
                    mandados.Add((erro.GetType().Name, origem));
                    return Task.CompletedTask;
                },
            };

            tratamento.ErroNaInterface(new InvalidOperationException("na tela"));
            tratamento.Fatal(null, new UnhandledExceptionEventArgs(new OutOfMemoryException(), true));

            mandados.Should().Equal(("InvalidOperationException", "interface"), ("OutOfMemoryException", "fatal"));
            Directory.GetFiles(pasta).Should().ContainSingle("o registro local continua");
            new TratamentoDeErros(new RegistroDeErros(pasta, TimeProvider.System), _ => { }) { Enviar = (_, _) => throw new IOException() }
                .ErroNaInterface(new InvalidOperationException("o envio que falha não derruba nada"));
        }
        finally
        {
            Directory.Delete(pasta, recursive: true);
        }
    }

    [Fact]
    public void MinhaConta_LigaOEnvio_EGuardaNasPreferencias()
    {
        var conta = new ContaFalsa { Perfil = Ana };
        var preferencias = new PreferenciasInterfaceEmMemoria { Salvo = new PreferenciasInterface(false, TemaDaInterface.Escuro) };

        new ContaViewModel(conta).MostraEnvioDeErros.Should().BeFalse("só pelo menu Conta da janela principal");
        var tela = new ContaViewModel(conta, preferencias);
        tela.MostraEnvioDeErros.Should().BeTrue();
        tela.EnviarErros.Should().BeFalse();
        tela.EnviarErros = true;

        preferencias.Salvo.Should().Be(new PreferenciasInterface(false, TemaDaInterface.Escuro, EnviarErros: true), "o resto das preferências fica");
    }

    [Fact]
    public void ArquivoDasPreferencias_GuardaOEnvio_SoQuandoLigado()
    {
        var caminho = Path.Combine(Path.GetTempPath(), $"pp-interface-{Guid.NewGuid():N}.json");
        try
        {
            var arquivo = new ArquivoPreferenciasInterface(caminho);
            arquivo.Salvar(new PreferenciasInterface(true));
            File.ReadAllText(caminho).Should().NotContain("enviarErros", "o arquivo de quem não liga fica como antes");
            arquivo.Salvar(new PreferenciasInterface(true, EnviarErros: true));
            File.ReadAllText(caminho).Should().Contain("\"enviarErros\": true");
            arquivo.Carregar()!.EnviarErros.Should().BeTrue();
        }
        finally
        {
            File.Delete(caminho);
        }
    }
}
