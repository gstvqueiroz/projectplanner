using ProjectPlanner.App.ViewModels;
using ProjectPlanner.IA;
using ProjectPlanner.IA.Conta;

namespace ProjectPlanner.App.Tests;

// Bloco 49: o Telegram no programa: marcar a obra (o retrato vai ao servidor e de novo a cada salvar), buscar o que chegou
// (atualizações viram cartões; fotos vão para a importação) e a tela Conectar o Telegram.
public sealed class TelegramNoProgramaTests : IDisposable
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-telegram-" + Guid.NewGuid().ToString("N"));

    public TelegramNoProgramaTests()
    {
        _vm = _ambiente.CriarViewModel();
        _vm.PastaDasFotosDoTelegram = _pasta;
        Ambiente.AdicionarTarefa(_vm, "Fundação", 5);
        Ambiente.AdicionarTarefa(_vm, "Alvenaria", 10, "1");
        _vm.Cronograma.Renomear("Casa da Ana");
    }

    public void Dispose()
    {
        if (Directory.Exists(_pasta))
            Directory.Delete(_pasta, recursive: true);
    }

    private Guid Projeto => _vm.Cronograma.Id;
    private Guid IdDe(string nome) => _vm.Cronograma.Tarefas.Single(t => t.Nome == nome).Id;

    [Fact]
    public async Task SemConta_OsComandosFicamDesligados_EComContaLigam()
    {
        (_vm.UsarObraNoTelegramCommand.CanExecute(null), _vm.ConectarTelegramCommand.CanExecute(null), _vm.BuscarDoTelegramCommand.CanExecute(null))
            .Should().Be((false, false, false));

        _ambiente.Conta.Conectar(plano: "profissional");
        await Task.Yield();

        (_vm.UsarObraNoTelegramCommand.CanExecute(null), _vm.ConectarTelegramCommand.CanExecute(null), _vm.BuscarDoTelegramCommand.CanExecute(null))
            .Should().Be((true, true, false), "buscar só numa obra marcada");
        _ambiente.Telegram.Pedidos.Should().Contain("obras", "ao entrar, o programa confere se a obra aberta está no Telegram");
    }

    [Fact]
    public async Task MarcarAObra_MandaORetrato_SalvarAtualiza_EDesmarcar()
    {
        _ambiente.Conta.Conectar(plano: "profissional");
        await _vm.UsarObraNoTelegramCommand.ExecuteAsync(null);

        var (nome, retrato) = _ambiente.Telegram.Marcadas[Projeto];
        (nome, retrato.Tarefas.Select(t => t.Nome)).Should().BeEquivalentTo(("Casa da Ana", new[] { "Fundação", "Alvenaria" }));
        (_vm.ObraNoTelegram, _vm.TextoDoMenuDaObraNoTelegram, _vm.BuscarDoTelegramCommand.CanExecute(null))
            .Should().Be((true, "Tirar esta obra do Telegram", true));
        _vm.Mensagem.Should().Be("A obra \"Casa da Ana\" está no Telegram: o retrato é atualizado a cada vez que você salvar.");

        Ambiente.AdicionarTarefa(_vm, "Cobertura", 4, "2");
        _ambiente.Dialogos.ArquivoParaSalvar = Path.Combine(_pasta, "casa.pplan");
        Directory.CreateDirectory(_pasta);
        await _vm.SalvarAsync();
        _ambiente.Telegram.Marcadas[Projeto].Retrato.Tarefas.Should().HaveCount(3, "salvar manda o retrato novo");

        await _vm.UsarObraNoTelegramCommand.ExecuteAsync(null);
        (_vm.ObraNoTelegram, _ambiente.Telegram.Marcadas.Count, _vm.Mensagem).Should().Be((false, 0, "A obra \"Casa da Ana\" saiu do Telegram."));
        var pedidos = _ambiente.Telegram.Pedidos.Count;
        await _vm.SalvarAsync();
        _ambiente.Telegram.Pedidos.Should().HaveCount(pedidos, "obra fora do Telegram: salvar não manda nada");
    }

    [Fact]
    public async Task MarcarSemSerProfissional_MostraOMotivo()
    {
        _ambiente.Conta.Conectar();
        _ambiente.Telegram.Erro = new ErroDaContaException("O assistente no Telegram é do plano Profissional. Veja em Conta > Minha conta.", 403);

        await _vm.UsarObraNoTelegramCommand.ExecuteAsync(null);

        (_vm.ObraNoTelegram, _vm.Mensagem).Should().Be((false, "O assistente no Telegram é do plano Profissional. Veja em Conta > Minha conta."));
    }

    [Fact]
    public async Task Buscar_AtualizacoesViramCartoes_PelaTarefaDoMomento_EFotosVaoParaAImportacao()
    {
        _ambiente.Conta.Conectar(plano: "profissional");
        await _vm.UsarObraNoTelegramCommand.ExecuteAsync(null);
        var foto = Guid.NewGuid();
        // A mensagem chegou quando a Alvenaria era a Nº 7 (o cronograma mudou depois): o id acha a tarefa certa.
        _ambiente.Telegram.Caixas[Projeto] =
        [
            new ItemDoTelegram(Guid.NewGuid(), ItemDoTelegram.DeAtualizacoes, new DateTime(2026, 10, 19, 17, 30, 0, DateTimeKind.Utc), "a alvenaria está em 60%", null,
                [new AtualizacaoDaObra(TiposDeAtualizacao.Percentual, "7", Percentual: 60)], [new TarefaDoTelegram(7, IdDe("Alvenaria"))]),
            new ItemDoTelegram(foto, ItemDoTelegram.DeFoto, DateTime.UtcNow, "laje", "telegram-20261019-1.jpg", null, null),
        ];
        _ambiente.Telegram.Fotos[foto] = [1, 2, 3];
        await _vm.AtualizarTelegramAsync();
        (_vm.PendentesDoTelegram, _vm.TextoDoMenuBuscarDoTelegram).Should().Be((2, "Buscar do Telegram (2)"));
        _vm.Mensagem.Should().Be("Chegaram 2 itens do Telegram para esta obra: Conta > Buscar do Telegram.");

        await _vm.BuscarDoTelegramCommand.ExecuteAsync(null);

        var mensagem = _vm.Assistente.Mensagens[^1];
        mensagem.Texto.Should().StartWith("Do Telegram, em ").And.EndWith(": \"a alvenaria está em 60%\"");
        mensagem.Cartao!.Itens.Single().Texto.Should().Be("2 Alvenaria: 0% → 60% concluído");
        _vm.Assistente.Visivel.Should().BeTrue();
        File.ReadAllBytes(Path.Combine(_pasta, Projeto.ToString("N"), "telegram-20261019-1.jpg")).Should().Equal(1, 2, 3);
        _ambiente.Dialogos.FotosDoProjeto.Should().ContainSingle("as fotos abrem a importação em lote");
        (_vm.PendentesDoTelegram, _ambiente.Telegram.Caixas[Projeto].Count).Should().Be((0, 0), "o que foi trazido some do servidor");
        _vm.Mensagem.Should().Be("1 mensagem do Telegram virou cartão no assistente: confira e aplique. 1 foto foi para a importação.");

        await _vm.Assistente.AplicarCartaoCommand.ExecuteAsync(mensagem.Cartao);
        _vm.Cronograma.Tarefas.Single(t => t.Nome == "Alvenaria").PercentualConcluido.Should().Be(60);
    }

    [Fact]
    public async Task Buscar_NadaNovo_FotoQueSumiu_ESemInternet()
    {
        _ambiente.Conta.Conectar(plano: "profissional");
        await _vm.UsarObraNoTelegramCommand.ExecuteAsync(null);
        await _vm.BuscarDoTelegramCommand.ExecuteAsync(null);
        _vm.Mensagem.Should().Be("Nada novo do Telegram para esta obra.");

        _ambiente.Telegram.Caixas[Projeto] = [new ItemDoTelegram(Guid.NewGuid(), ItemDoTelegram.DeFoto, DateTime.UtcNow, "", "x.jpg", null, null)];
        await _vm.BuscarDoTelegramCommand.ExecuteAsync(null);
        (_vm.Mensagem, _vm.PendentesDoTelegram).Should().Be(("1 item(ns) não puderam ser trazidos agora (tente de novo).", 1));
        _ambiente.Dialogos.FotosDoProjeto.Should().BeEmpty();

        _ambiente.Telegram.Erro = new ErroDaContaException("Sem conexão com o servidor do ProjectPlanner. Verifique a internet e tente de novo.", 0);
        await _vm.BuscarDoTelegramCommand.ExecuteAsync(null);
        _vm.Mensagem.Should().Be("Sem conexão com o servidor do ProjectPlanner. Verifique a internet e tente de novo.");
    }

    [Fact]
    public async Task Conectar_GeraOCodigo_AbreOLink_VerificaEDesliga()
    {
        _ambiente.Conta.Conectar(plano: "profissional");
        await _vm.ConectarTelegramCommand.ExecuteAsync(null);
        var tela = _ambiente.Dialogos.Telegrams.Single();
        (tela.TextoDaSituacao, tela.PodeDesligar, tela.TemCodigo, tela.AbrirTelegramCommand.CanExecute(null))
            .Should().Be(("Ainda não ligado. Gere o código e abra o Telegram.", false, false, false));

        await tela.GerarCodigoCommand.ExecuteAsync(null);
        (tela.Codigo!.Codigo, tela.TemCodigo, tela.AbrirTelegramCommand.CanExecute(null)).Should().Be(("ABCD2345", true, true));
        tela.InstrucaoDoCodigo.Should().Contain("/start ABCD2345").And.Contain("15 minutos");
        await tela.AbrirTelegramCommand.ExecuteAsync(null);
        _ambiente.Dialogos.EnderecosAbertos.Single().Should().Be(new Uri("https://t.me/ProjectPlannerBot?start=ABCD2345"));

        await tela.VerificarCommand.ExecuteAsync(null);
        (tela.Mensagem, tela.MensagemEhErro).Should().Be(("Ainda não ligado: no Telegram, toque em Iniciar (ou mande /start e o código) e verifique de novo.", true));
        _ambiente.Telegram.Situacao = new SituacaoDoTelegram(true, true, "Ana", "ProjectPlannerBot");
        await tela.VerificarCommand.ExecuteAsync(null);
        (tela.TextoDaSituacao, tela.TemCodigo, tela.PodeDesligar, tela.Mensagem).Should().Be(("Ligado ao Telegram de Ana.", false, true, "Pronto: o Telegram está ligado à sua conta."));

        await tela.DesligarCommand.ExecuteAsync(null);
        (tela.PodeDesligar, tela.Mensagem).Should().Be((false, "O Telegram não está mais ligado à sua conta."));
    }

    [Fact]
    public async Task Conectar_SemTelegramNoServidor_OuForaDoPlano()
    {
        _ambiente.Conta.Conectar();
        _ambiente.Telegram.Situacao = new SituacaoDoTelegram(false, false, null, null);
        await _vm.ConectarTelegramCommand.ExecuteAsync(null);
        var tela = _ambiente.Dialogos.Telegrams.Single();
        tela.TextoDaSituacao.Should().Be("O Telegram ainda não está configurado no servidor do ProjectPlanner.");

        _ambiente.Telegram.Erro = new ErroDaContaException("O assistente no Telegram é do plano Profissional. Veja em Conta > Minha conta.", 403);
        await tela.GerarCodigoCommand.ExecuteAsync(null);
        (tela.TemCodigo, tela.Mensagem, tela.MensagemEhErro).Should().Be((false, "O assistente no Telegram é do plano Profissional. Veja em Conta > Minha conta.", true));
    }
}
