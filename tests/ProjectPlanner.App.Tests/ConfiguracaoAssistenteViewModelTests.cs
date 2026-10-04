using ProjectPlanner.App.Services;
using ProjectPlanner.App.Services.Cofre;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Tests;

// Bloco 23: a tela Configurações do assistente.
public class ConfiguracaoAssistenteViewModelTests
{
    private const string Chave = "AIzaChaveColadaNaTela00000000000009876";

    private readonly CofreEmMemoria _cofre = new();
    private readonly PreferenciasAssistenteEmMemoria _preferencias = new();
    private readonly ProvedorFalso _provedor = new();
    private readonly DialogosFalsos _dialogos = new();
    private readonly Dictionary<string, string?> _variaveis = [];
    private readonly ConfiguracaoDoAssistente _configuracao;

    public ConfiguracaoAssistenteViewModelTests() =>
        _configuracao = new ConfiguracaoDoAssistente(_cofre, _preferencias, _provedor, nome => _variaveis.GetValueOrDefault(nome));

    private ConfiguracaoAssistenteViewModel Tela() => new(_configuracao, _dialogos);

    [Fact]
    public void SemChave_SituacaoEBotoes()
    {
        var tela = Tela();

        (tela.Situacao, tela.TemChave, tela.Modelo, tela.NomeDoProvedor, tela.ModeloPadrao).Should().Be(
            ("Nenhuma chave configurada: o assistente dá respostas simuladas.", false, "gemini-2.5-flash", "Gemini (Google)", "gemini-2.5-flash"));
        tela.DicaDaChave.Should().Contain("cofre do sistema (Chaves do macOS)").And.Contain("nunca vai no arquivo do projeto");
        tela.TestarCommand.CanExecute(null).Should().BeFalse("não há chave para testar");
        tela.RemoverChaveCommand.CanExecute(null).Should().BeFalse();
        tela.SalvarCommand.CanExecute(null).Should().BeTrue();
        ConfiguracaoAssistenteViewModel.AvisoDePrivacidade.Should().Contain("enviados ao Google (Gemini)");
    }

    [Fact]
    public void Salvar_ChaveVaiParaOCofre_ECampoEsvazia()
    {
        var tela = Tela();
        var avisos = new List<string?>();
        tela.PropertyChanged += (_, e) => avisos.Add(e.PropertyName);
        tela.ChaveDigitada = Chave;
        tela.TestarCommand.CanExecute(null).Should().BeTrue();

        tela.SalvarCommand.Execute(null);

        _cofre.Segredos[ConfiguracaoDoAssistente.NomeNoCofre].Should().Be(Chave);
        (tela.ChaveDigitada, tela.Mensagem, tela.MensagemEhErro).Should().Be(("", "Chave salva no cofre do sistema.", false));
        tela.Situacao.Should().Be("Chave guardada no cofre do sistema (Chaves do macOS), terminada em …9876.");
        tela.Situacao.Should().NotContain(Chave[..20], "a tela nunca mostra a chave de volta");
        avisos.Should().Contain(nameof(ConfiguracaoAssistenteViewModel.Situacao));
        tela.RemoverChaveCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Salvar_ModeloEChave_Juntos_ENadaParaSalvar()
    {
        var tela = Tela();

        tela.SalvarCommand.Execute(null);
        tela.Mensagem.Should().Be("Nada para salvar: cole uma chave ou mude o modelo.");

        (tela.ChaveDigitada, tela.Modelo) = (Chave, " models/gemini-2.5-pro ");
        tela.SalvarCommand.Execute(null);

        (tela.Mensagem, tela.Modelo, _configuracao.Modelo).Should().Be(("Modelo e chave salvos.", "gemini-2.5-pro", "gemini-2.5-pro"));

        tela.Modelo = "gemini-2.5-flash";
        tela.SalvarCommand.Execute(null);
        tela.Mensagem.Should().Be("Modelo salvo: gemini-2.5-flash.");
    }

    [Fact]
    public void Salvar_Invalido_MostraOErro_ENaoSalvaNada()
    {
        var tela = Tela();
        (tela.ChaveDigitada, tela.Modelo) = ("chave com espaço", "gemini-2.5-flash");

        tela.SalvarCommand.Execute(null);

        (tela.Mensagem, tela.MensagemEhErro).Should().Be(("A chave não pode ter espaços nem quebras de linha.", true));
        _cofre.Segredos.Should().BeEmpty();
        tela.ChaveDigitada.Should().Be("chave com espaço", "o usuário corrige o que colou");

        (tela.ChaveDigitada, tela.Modelo) = ("", "gemini 2.5");
        tela.SalvarCommand.Execute(null);
        (tela.MensagemEhErro, _preferencias.Salvas).Should().Be((true, null));
    }

    [Fact]
    public void Salvar_CofreRecusa_MostraOMotivo()
    {
        _cofre.Falha = new CofreException("Não foi possível salvar a chave: o acesso às Chaves do macOS foi negado.");
        var tela = Tela();
        tela.ChaveDigitada = Chave;

        tela.SalvarCommand.Execute(null);

        (tela.Mensagem, tela.MensagemEhErro, tela.TemChave).Should().Be(
            ("Não foi possível salvar a chave: o acesso às Chaves do macOS foi negado.", true, false));
    }

    [Fact]
    public async Task Testar_ChaveDigitada_Ok()
    {
        var tela = Tela();
        tela.ChaveDigitada = Chave;

        await tela.TestarCommand.ExecuteAsync(null);

        (tela.Mensagem, tela.MensagemEhErro, tela.Ocupado).Should().Be(("Conexão OK: a chave funciona com o modelo gemini-2.5-flash.", false, false));
        _provedor.Testes.Should().Equal((Chave, "gemini-2.5-flash"));
        _cofre.Segredos.Should().BeEmpty("testar não salva");
    }

    [Fact]
    public async Task Testar_Falha_MostraOErroDoProvedor()
    {
        _cofre.Segredos[ConfiguracaoDoAssistente.NomeNoCofre] = Chave;
        _provedor.ErroNoTeste = new ErroDoAssistenteException("A chave do Gemini não é válida. Confira a chave nas configurações do assistente.");
        var tela = Tela();

        await tela.TestarCommand.ExecuteAsync(null);

        (tela.Mensagem, tela.MensagemEhErro).Should().Be(("A chave do Gemini não é válida. Confira a chave nas configurações do assistente.", true));
    }

    [Fact]
    public async Task Testar_EmAndamento_TravaOsBotoes_EFecharCancela()
    {
        _provedor.TesteSegurado = new TaskCompletionSource();
        var tela = Tela();
        tela.ChaveDigitada = Chave;

        var teste = tela.TestarCommand.ExecuteAsync(null);

        (tela.Ocupado, tela.Mensagem).Should().Be((true, "Testando a conexão…"));
        tela.SalvarCommand.CanExecute(null).Should().BeFalse();
        tela.TestarCommand.CanExecute(null).Should().BeFalse();

        tela.Fechando();
        await teste;

        (tela.Ocupado, tela.Mensagem, tela.MensagemEhErro).Should().Be((false, "Teste cancelado.", true));
    }

    [Fact]
    public async Task RemoverChave_ComConfirmacao()
    {
        _cofre.Segredos[ConfiguracaoDoAssistente.NomeNoCofre] = Chave;
        var tela = Tela();
        _dialogos.Confirmar = false;

        await tela.RemoverChaveCommand.ExecuteAsync(null);
        _cofre.Segredos.Should().ContainKey(ConfiguracaoDoAssistente.NomeNoCofre, "cancelou");
        _dialogos.Confirmacoes.Last().Should().StartWith("Remover a chave do Gemini do cofre do sistema?");

        _dialogos.Confirmar = true;
        await tela.RemoverChaveCommand.ExecuteAsync(null);

        _cofre.Segredos.Should().BeEmpty();
        (tela.Mensagem, tela.TemChave, tela.RemoverChaveCommand.CanExecute(null)).Should().Be(("Chave removida do cofre do sistema.", false, false));
    }

    [Fact]
    public void ChaveDaVariavel_SituacaoExplica_ENaoDaParaRemover()
    {
        _variaveis[ConfiguracaoDoAssistente.VariavelDeAmbiente] = "AIzaChaveDaVariavelDeAmbiente000005678";

        var tela = Tela();

        tela.Situacao.Should().Be("Usando a chave da variável de ambiente GEMINI_API_KEY (terminada em …5678), só para desenvolvimento. " +
                                  "Salve uma chave aqui para usar no programa publicado.");
        tela.RemoverChaveCommand.CanExecute(null).Should().BeFalse("a variável não está no cofre");
        tela.TestarCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void CofreBloqueado_MostraOProblema()
    {
        _cofre.Falha = new CofreException("Não foi possível ler a chave: as Chaves do macOS estão bloqueadas ou recusaram o acesso.");

        var tela = Tela();
        _ = tela.Situacao;

        (tela.TemProblemaNoCofre, tela.ProblemaNoCofre).Should().Be(
            (true, "Não foi possível ler a chave: as Chaves do macOS estão bloqueadas ou recusaram o acesso."));
    }

    [Fact]
    public void RestaurarModeloPadrao_EArgumentosNulos()
    {
        var tela = Tela();
        tela.Modelo = "gemini-2.5-pro";

        tela.RestaurarModeloPadraoCommand.Execute(null);

        tela.Modelo.Should().Be("gemini-2.5-flash");
        var semConfiguracao = () => new ConfiguracaoAssistenteViewModel(null!, _dialogos);
        var semDialogos = () => new ConfiguracaoAssistenteViewModel(_configuracao, null!);
        semConfiguracao.Should().Throw<ArgumentNullException>();
        semDialogos.Should().Throw<ArgumentNullException>();
    }
}
