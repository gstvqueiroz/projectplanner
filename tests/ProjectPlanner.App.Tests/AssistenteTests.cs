using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Tests;

// Bloco 19: o painel do assistente de IA no ViewModel (sem integração real).
public sealed class AssistenteTests : IDisposable
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-assistente-" + Guid.NewGuid().ToString("N"));

    public AssistenteTests()
    {
        _vm = _ambiente.CriarViewModel();
        Directory.CreateDirectory(_pasta);
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private AssistenteViewModel Painel => _vm.Assistente;

    private string Arquivo(string nome, int bytes = 1000)
    {
        var caminho = Path.Combine(_pasta, nome);
        File.WriteAllBytes(caminho, new byte[bytes]);
        return caminho;
    }

    [Fact]
    public void Enviar_AcrescentaAMensagemDoUsuarioEARespostaDoAssistente()
    {
        Painel.TextoDigitado = "  Qual é o caminho crítico?  ";

        Painel.EnviarCommand.Execute(null);

        Painel.Mensagens.Select(m => (m.DoUsuario, m.Texto, m.EhErro)).Should().Equal(
            (true, "Qual é o caminho crítico?", false),
            (false, "Resposta a: Qual é o caminho crítico?", false));
        Painel.Mensagens.Should().OnlyContain(m => m.Hora == "12:00");
        (Painel.TextoDigitado, Painel.Aguardando, Painel.ConversaVazia).Should().Be(("", false, false));
    }

    [Fact]
    public void Enviar_ComOAssistenteIndisponivelDeVerdade_MostraARespostaPadrao()
    {
        var vm = new MainWindowViewModel(_ambiente.Repositorio, _ambiente.Calculadora, _ambiente.Dialogos, _ambiente.Relogio,
            _ambiente.Preferencias, new AssistenteIAIndisponivel(TimeSpan.Zero), _ambiente.PreferenciasInterface, _ambiente.Processador, _ambiente.ConfiguracaoAssistente);
        vm.Assistente.TextoDigitado = "Olá";

        vm.Assistente.EnviarCommand.Execute(null);

        vm.Assistente.EstaConfigurado.Should().BeFalse();
        vm.Assistente.Mensagens[^1].Texto.Should().StartWith(AssistenteIAIndisponivel.Resposta);
    }

    [Fact]
    public void Enviar_MandaOHistoricoAnteriorSemOsAvisos()
    {
        Painel.TextoDigitado = "Primeira";
        Painel.EnviarCommand.Execute(null);
        _ambiente.Assistente.Erro = new InvalidOperationException("sem rede");
        Painel.TextoDigitado = "Segunda";
        Painel.EnviarCommand.Execute(null);
        _ambiente.Assistente.Erro = null;
        Painel.TextoDigitado = "Terceira";

        Painel.EnviarCommand.Execute(null);

        _ambiente.Assistente.Pedidos[^1].Historico.Select(m => (m.Autor, m.Texto)).Should().Equal(
            (AutorMensagem.Usuario, "Primeira"),
            (AutorMensagem.Assistente, "Resposta a: Primeira"),
            (AutorMensagem.Usuario, "Segunda"));
    }

    [Fact]
    public void Enviar_FalhaDoAssistente_ViraAvisoNaConversa_SemQuebrar()
    {
        _ambiente.Assistente.Erro = new HttpRequestException("tempo esgotado");
        Painel.TextoDigitado = "Olá";

        Painel.EnviarCommand.Execute(null);

        var aviso = Painel.Mensagens[^1];
        (aviso.DoUsuario, aviso.EhErro, aviso.Texto).Should().Be((false, true, "Não foi possível obter a resposta: tempo esgotado"));
        Painel.Aguardando.Should().BeFalse();
    }

    [Fact]
    public void Enviar_SemTextoESemAnexos_Desabilitado_EDuranteAEsperaTambem()
    {
        Painel.EnviarCommand.CanExecute(null).Should().BeFalse();
        Painel.TextoDigitado = "   ";
        Painel.EnviarCommand.CanExecute(null).Should().BeFalse();

        Painel.TextoDigitado = "Olá";
        _ambiente.Assistente.Segurar();
        Painel.EnviarCommand.Execute(null);

        (Painel.Aguardando, Painel.CancelarCommand.CanExecute(null)).Should().Be((true, true));
        Painel.TextoDigitado = "Outra";
        Painel.EnviarCommand.CanExecute(null).Should().BeFalse("espera a resposta anterior");
        _ambiente.Assistente.Soltar();
    }

    [Fact]
    public async Task Aguardando_EnquantoARespostaNaoChega()
    {
        _ambiente.Assistente.Segurar();
        Painel.TextoDigitado = "Demora";

        var envio = Painel.EnviarCommand.ExecuteAsync(null);

        (Painel.Aguardando, Painel.Mensagens.Count).Should().Be((true, 1), "a do usuário já aparece; a resposta, não");
        _ambiente.Assistente.Soltar();
        await envio;
        (Painel.Aguardando, Painel.Mensagens.Count).Should().Be((false, 2));
    }

    [Fact]
    public async Task Cancelar_DuranteAEspera_MarcaComoCancelada()
    {
        _ambiente.Assistente.Segurar();
        Painel.TextoDigitado = "Demora";
        var envio = Painel.EnviarCommand.ExecuteAsync(null);

        Painel.CancelarCommand.Execute(null);
        await envio;

        (Painel.Mensagens[^1].Texto, Painel.Mensagens[^1].EhErro, Painel.Aguardando).Should().Be(("Resposta cancelada.", true, false));
    }

    [Fact]
    public void Anexar_PoeNaListaDePendentes_SemRepetir_EOEnvioLevaELimpa()
    {
        var planta = Arquivo("planta.pdf", 2048);
        var contrato = Arquivo("memorial.txt");
        _ambiente.Dialogos.DocumentosParaAnexar.AddRange([planta, contrato]);

        Painel.AnexarCommand.Execute(null);
        Painel.AnexarCommand.Execute(null); // os mesmos de novo

        Painel.AnexosPendentes.Select(a => (a.Nome, a.Extensao)).Should().Equal(("planta.pdf", "pdf"), ("memorial.txt", "txt"));
        Painel.EnviarCommand.CanExecute(null).Should().BeTrue("só com anexos já dá para enviar");

        Painel.EnviarCommand.Execute(null);

        _ambiente.Assistente.Pedidos.Single().Anexos.Select(a => a.Nome).Should().Equal("planta.pdf", "memorial.txt");
        Painel.Mensagens[0].Anexos.Select(a => a.Nome).Should().Equal("planta.pdf", "memorial.txt");
        Painel.AnexosPendentes.Should().BeEmpty("os anexos foram com a mensagem");
    }

    [Fact]
    public void Anexar_Cancelado_NaoMudaNada_ERemoverTiraUm()
    {
        Painel.AnexarCommand.Execute(null); // diálogo cancelado: nada escolhido
        Painel.AnexosPendentes.Should().BeEmpty();

        _ambiente.Dialogos.DocumentosParaAnexar.AddRange([Arquivo("a.pdf"), Arquivo("b.pdf")]);
        Painel.AnexarCommand.Execute(null);
        Painel.RemoverAnexoCommand.Execute(Painel.AnexosPendentes[0]);

        Painel.AnexosPendentes.Select(a => a.Nome).Should().Equal("b.pdf");
    }

    [Fact]
    public void Anexar_ArquivoQueSumiu_Avisa_EAnexaOsOutros()
    {
        _ambiente.Dialogos.DocumentosParaAnexar.AddRange([Arquivo("ok.pdf"), Path.Combine(_pasta, "sumiu.pdf")]);

        Painel.AnexarCommand.Execute(null);

        Painel.AnexosPendentes.Select(a => a.Nome).Should().Equal("ok.pdf");
        Painel.AvisoDeAnexo.Should().Be("Não deu para anexar: sumiu.pdf.");
    }

    [Fact]
    public void LimparConversa_EsvaziaOHistorico()
    {
        Painel.TextoDigitado = "Olá";
        Painel.EnviarCommand.Execute(null);

        Painel.LimparConversaCommand.Execute(null);

        (Painel.Mensagens.Count, Painel.ConversaVazia).Should().Be((0, true));
    }

    [Fact]
    public async Task LimparConversa_ComRespostaPendente_NaoTrazARespostaDeVolta()
    {
        _ambiente.Assistente.Segurar();
        Painel.TextoDigitado = "Demora";
        var envio = Painel.EnviarCommand.ExecuteAsync(null);

        Painel.LimparConversaCommand.Execute(null);
        await envio;

        Painel.Mensagens.Should().BeEmpty();
        Painel.Aguardando.Should().BeFalse();
    }

    [Fact]
    public void NovoProjeto_RecomecaAConversa_EOsPendentes()
    {
        Painel.TextoDigitado = "Olá";
        Painel.EnviarCommand.Execute(null);
        _ambiente.Dialogos.DocumentosParaAnexar.Add(Arquivo("a.pdf"));
        Painel.AnexarCommand.Execute(null);
        Painel.TextoDigitado = "rascunho";

        _vm.NovoProjetoCommand.Execute(null);

        (Painel.Mensagens.Count, Painel.AnexosPendentes.Count, Painel.TextoDigitado).Should().Be((0, 0, ""));
    }

    [Fact]
    public void Conversa_NaoMarcaOProjetoComoAlterado()
    {
        Painel.TextoDigitado = "Olá";
        Painel.EnviarCommand.Execute(null);

        _vm.PossuiAlteracoesNaoSalvas.Should().BeFalse("a conversa não vai para o .pplan");
    }

    [Fact]
    public void Painel_ComecaOcultoNaPrimeiraVez_EAlternarGravaAPreferencia()
    {
        Painel.Visivel.Should().BeFalse();
        _ambiente.PreferenciasInterface.Gravacoes.Should().Be(0, "abrir não grava nada");

        Painel.AlternarVisibilidadeCommand.Execute(null);
        (Painel.Visivel, _ambiente.PreferenciasInterface.Salvo).Should().Be((true, new PreferenciasInterface(true)));

        Painel.FecharCommand.Execute(null);
        (Painel.Visivel, _ambiente.PreferenciasInterface.Salvo).Should().Be((false, new PreferenciasInterface(false)));
    }

    [Fact]
    public void Painel_AbreComoFicouNaSessaoAnterior()
    {
        _ambiente.PreferenciasInterface.Salvo = new PreferenciasInterface(MostrarAssistente: true);

        _ambiente.CriarViewModel().Assistente.Visivel.Should().BeTrue();
    }
}

// O interface.json de verdade, numa pasta temporária.
public sealed class ArquivoPreferenciasInterfaceTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-interface-" + Guid.NewGuid().ToString("N"));
    private string Caminho => Path.Combine(_pasta, "sub", "interface.json");

    public void Dispose()
    {
        if (Directory.Exists(_pasta))
            Directory.Delete(_pasta, recursive: true);
    }

    [Fact]
    public void SemArquivo_Nulo()
    {
        new ArquivoPreferenciasInterface(Caminho).Carregar().Should().BeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SalvarECarregar_EntreSessoes(bool mostrar)
    {
        new ArquivoPreferenciasInterface(Caminho).Salvar(new PreferenciasInterface(mostrar));

        new ArquivoPreferenciasInterface(Caminho).Carregar().Should().Be(new PreferenciasInterface(mostrar));
        File.ReadAllText(Caminho).Should().Contain("\"mostrarAssistente\"").And.Contain("\"versao\": 1");
    }

    [Theory]
    [InlineData("isto não é json")]
    [InlineData("{\"versao\": 99, \"mostrarAssistente\": true}")]
    public void ArquivoInvalidoOuDeOutraVersao_Nulo(string conteudo)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Caminho)!);
        File.WriteAllText(Caminho, conteudo);

        new ArquivoPreferenciasInterface(Caminho).Carregar().Should().BeNull();
    }

    [Fact]
    public void CaminhoPadrao_AoLadoDoColunasJson()
    {
        Path.GetDirectoryName(ArquivoPreferenciasInterface.CaminhoPadrao())
            .Should().Be(Path.GetDirectoryName(ArquivoPreferenciasColunas.CaminhoPadrao()));
        Path.GetFileName(ArquivoPreferenciasInterface.CaminhoPadrao()).Should().Be("interface.json");
    }
}
