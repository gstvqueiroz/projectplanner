using Avalonia.Controls;
using Avalonia.Threading;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.App.Views;

namespace ProjectPlanner.App.Tests;

// Bloco 45, na janela de verdade: o menu Conta, o nome na barra de status e a tela da conta.
public partial class MenuPrincipalTests
{
    [Fact]
    public Task Conta_PeloMenu_AbreATela_EConectadoMostraNaBarra() => Tela.Executar(() =>
    {
        var a = Abrir();
        var status = a.Janela.FindControl<TextBlock>("StatusConta")!;
        (status.IsVisible, a.Item("Conta", "Sair da conta").IsEffectivelyEnabled).Should().Be((false, false));

        a.Clicar("Conta", "Entrar ou criar conta…");
        a.Ambiente.Dialogos.Contas.Should().ContainSingle().Which.Pagina.Should().Be(PaginaDaConta.Entrar);

        a.Ambiente.Conta.Conectar("Ana Souza");
        Dispatcher.UIThread.RunJobs();
        (status.IsVisible, status.Text).Should().Be((true, "Conta: Ana Souza"));
        Titulos(a.Item("Conta")).Should().Equal("Minha conta…", "Sair da conta");
        a.Item("Conta", "Sair da conta").IsEffectivelyEnabled.Should().BeTrue();

        a.Clicar("Conta", "Minha conta…");
        a.Ambiente.Dialogos.Contas[^1].Pagina.Should().Be(PaginaDaConta.Perfil);

        a.Ambiente.Dialogos.Confirmar = false;
        a.Clicar("Conta", "Sair da conta");
        a.Ambiente.Conta.Conectada.Should().BeTrue("não confirmou");
        a.Ambiente.Dialogos.Confirmacoes[^1].Should().Contain("ana@exemplo.com");
        a.Ambiente.Dialogos.Confirmar = true;
        a.Clicar("Conta", "Sair da conta");
        Dispatcher.UIThread.RunJobs();
        (a.Ambiente.Conta.Conectada, status.IsVisible, a.Vm.Mensagem).Should().Be((false, false, "Você saiu da conta ProjectPlanner."));
    });

    [Fact]
    public Task Conta_NoPainelDoAssistente_ACabecaMostraAConta() => Tela.Executar(() =>
    {
        var a = Abrir(ambiente =>
        {
            ambiente.Assistente.EstaConfigurado = true;
            ambiente.ConfiguracaoAssistente.Provedor = Services.ProvedorDoAssistente.Conta;
        });
        a.Vm.Assistente.DescricaoDoModelo.Should().Be("Conta ProjectPlanner · gemini-2.5-flash");
        var mudou = new List<string?>();
        a.Vm.Assistente.PropertyChanged += (_, e) => mudou.Add(e.PropertyName);
        a.Ambiente.Conta.Conectar();
        mudou.Should().Contain(nameof(AssistenteViewModel.DescricaoDoModelo), "entrar ou sair muda o que o assistente usa");
    });

    [Fact]
    public Task ContaWindow_CadaParteMostraSoOsSeusCampos() => Tela.Executar(() =>
    {
        var conta = new ContaFalsa();
        var tela = new ContaViewModel(conta);
        var janela = new ContaWindow(tela);
        janela.Show();
        Dispatcher.UIThread.RunJobs();
        bool Visivel(string nome) => janela.FindControl<Control>(nome)!.IsEffectivelyVisible;

        (Visivel("Email"), Visivel("Senha"), Visivel("Entrar"), Visivel("EntrarComGoogle"), Visivel("Nome"), Visivel("NomeDaConta"), Visivel("Cancelar"))
            .Should().Be((true, true, true, true, false, false, false));
        janela.Title.Should().Be("Conta ProjectPlanner: Entrar");

        tela.IrParaCriarCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        (Visivel("Nome"), Visivel("Email"), Visivel("Senha"), Visivel("Criar"), Visivel("Entrar")).Should().Be((true, true, true, true, false));

        (tela.Nome, tela.Email, tela.Senha) = ("Ana", "ana@exemplo.com", ContaFalsa.SenhaCerta);
        tela.CriarCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        (Visivel("CodigoDeConfirmacao"), Visivel("SenhaDoCadastro"), Visivel("Confirmar"), Visivel("Email")).Should().Be((true, false, true, false),
            "a senha do cadastro já está na tela: não pede de novo");

        tela.Codigo = ContaFalsa.CodigoCerto;
        tela.ConfirmarCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        (Visivel("NomeDaConta"), Visivel("Plano"), Visivel("Sair"), Visivel("Email")).Should().Be((true, true, true, false));
        janela.FindControl<TextBlock>("Plano")!.Text.Should().Be("Plano: Grátis");
        janela.Title.Should().Be("Conta ProjectPlanner: Minha conta");

        janela.Close();
    });

    [Fact]
    public Task ConfiguracoesDoAssistente_ContaEscolhida_EscondeAChave() => Tela.Executar(() =>
    {
        var configuracao = new ConfiguracaoAssistenteFalsa();
        var tela = new ConfiguracaoAssistenteViewModel(configuracao, new DialogosFalsos(), new ContaFalsa());
        var janela = new ConfiguracaoAssistenteWindow(tela);
        janela.Show();
        Dispatcher.UIThread.RunJobs();
        bool Visivel(string nome) => janela.FindControl<Control>(nome)!.IsEffectivelyVisible;

        (Visivel("Chave"), Visivel("AbrirConta"), Visivel("Modelo")).Should().Be((true, false, true));
        janela.FindControl<RadioButton>("UsarConta")!.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        (tela.UsarConta, Visivel("Chave"), Visivel("AbrirConta"), Visivel("SituacaoDaConta"), Visivel("Modelo")).Should().Be((true, false, true, true, true));
        janela.FindControl<Button>("AbrirConta")!.Content.Should().Be("Entrar ou criar conta…");
        janela.Close();
    });
}
