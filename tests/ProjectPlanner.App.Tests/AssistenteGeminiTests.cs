using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Tests;

// Bloco 23: o painel com o assistente de verdade (retrato do cronograma, configurações, anexos que ele lê).
public sealed class AssistenteGeminiTests : IDisposable
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-assistente23-" + Guid.NewGuid().ToString("N"));

    public AssistenteGeminiTests()
    {
        _vm = _ambiente.CriarViewModel();
        Directory.CreateDirectory(_pasta);
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private AssistenteViewModel Painel => _vm.Assistente;

    private string Arquivo(string nome, long bytes = 1000)
    {
        var caminho = Path.Combine(_pasta, nome);
        using var arquivo = File.Create(caminho);
        arquivo.SetLength(bytes);
        return caminho;
    }

    // 1 Fundação (resumo) > 1.1 Escavar (2 d, 100%), 1.2 Armar (3 d, pred 2); 2 Estrutura (4 d, pred 3).
    private void MontarObra()
    {
        var fundacao = Ambiente.AdicionarTarefa(_vm, "Fundação", 1);
        _vm.TarefaSelecionada = fundacao;
        _vm.AdicionarSubtarefaCommand.Execute(null);
        var escavar = _vm.TarefaSelecionada!;
        (escavar.Nome, escavar.Duracao, escavar.PercentualConcluido, escavar.Notas) = ("Escavar", 2, 100, "Solo argiloso");
        _vm.TarefaSelecionada = fundacao;
        _vm.AdicionarSubtarefaCommand.Execute(null);
        var armar = _vm.TarefaSelecionada!;
        (armar.Nome, armar.Duracao, armar.Predecessoras) = ("Armar", 3, "2");
        Ambiente.AdicionarTarefa(_vm, "Estrutura", 4, "3");
    }

    [Fact]
    public void Retrato_TemTodasAsLinhas_ComEdtDatasVinculosEFolga()
    {
        MontarObra();

        var retrato = _vm.RetratoParaOAssistente();

        (retrato.Nome, retrato.Inicio, retrato.Termino, retrato.Hoje)
            .Should().Be((_vm.NomeProjeto, new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 8), new DateOnly(2026, 9, 28)));
        retrato.Tarefas.Select(t => (t.Numero, t.Edt, t.Nome, t.Nivel, t.Tipo, t.DuracaoDias, t.Predecessoras)).Should().Equal(
            (1, "1", "Fundação", 0, TipoNoContexto.Resumo, 5, ""),
            (2, "1.1", "Escavar", 1, TipoNoContexto.Tarefa, 2, ""),
            (3, "1.2", "Armar", 1, TipoNoContexto.Tarefa, 3, "2"),
            (4, "2", "Estrutura", 0, TipoNoContexto.Tarefa, 4, "3"));
        var armar = retrato.Tarefas[2];
        (armar.Inicio, armar.Termino, armar.FolgaDias, armar.Critica).Should().Be((new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 2), 0, true));
        (retrato.Tarefas[1].PercentualConcluido, retrato.Tarefas[1].Notas).Should().Be((100, "Solo argiloso"));
    }

    [Fact]
    public void Retrato_IncluiAsLinhasDentroDeResumoRecolhido()
    {
        MontarObra();
        _vm.Tarefas[0].Expandido = false;

        _vm.LinhasVisiveis.Should().HaveCount(2);
        _vm.RetratoParaOAssistente().Tarefas.Should().HaveCount(4);
    }

    [Fact]
    public void Retrato_ProjetoNovo_SemTarefas()
    {
        var retrato = _vm.RetratoParaOAssistente();

        (retrato.Tarefas.Count, retrato.Termino).Should().Be((0, retrato.Inicio));
    }

    [Fact]
    public void Enviar_LevaORetratoDeAgora()
    {
        MontarObra();
        Painel.TextoDigitado = "Qual é o caminho crítico?";
        Painel.EnviarCommand.Execute(null);

        _vm.Tarefas[3].Nome = "Estrutura e lajes";
        Painel.TextoDigitado = "E agora?";
        Painel.EnviarCommand.Execute(null);

        var pedidos = _ambiente.Assistente.Pedidos;
        pedidos[0].Contexto!.Tarefas[3].Nome.Should().Be("Estrutura");
        pedidos[1].Contexto!.Tarefas[3].Nome.Should().Be("Estrutura e lajes", "o retrato é montado a cada envio");
    }

    [Fact]
    public void Anexar_FormatoQueOAssistenteNaoLe_EArquivoGrandeDemais_FicamDeForaComOMotivo()
    {
        _ambiente.Dialogos.DocumentosParaAnexar.AddRange(
            [Arquivo("planta.pdf"), Arquivo("contrato.docx"), Arquivo("enorme.pdf", 20L * 1024 * 1024), Path.Combine(_pasta, "sumiu.pdf")]);

        Painel.AnexarCommand.Execute(null);

        Painel.AnexosPendentes.Select(a => a.Nome).Should().Equal("planta.pdf");
        Painel.AvisoDeAnexo.Split('\n').Should().Equal(
            "Não deu para anexar: sumiu.pdf.",
            "contrato.docx: o assistente ainda não lê arquivos .docx. Use PDF, imagens (PNG, JPG, WebP, HEIC) e texto (TXT, MD, CSV, JSON); " +
            "um documento do Word ou do Excel pode ser exportado como PDF.",
            "enorme.pdf: o arquivo tem 20 MB; o limite é 14 MB por envio.");
    }

    [Fact]
    public void Configurar_AbreATela_EOPainelMostraOModelo()
    {
        var avisos = new List<string?>();
        Painel.PropertyChanged += (_, e) => avisos.Add(e.PropertyName);
        Painel.DescricaoDoModelo.Should().BeEmpty("sem chave, nada no cabeçalho");
        _ambiente.Dialogos.AoConfigurar = tela =>
        {
            tela.ChaveDigitada = "AIzaChaveColadaNoPainel0000000000001111";
            tela.SalvarCommand.Execute(null);
            _ambiente.Assistente.EstaConfigurado = true;
        };

        Painel.ConfigurarCommand.Execute(null);

        _ambiente.Dialogos.ConfiguracoesDoAssistente.Should().ContainSingle();
        _ambiente.ConfiguracaoAssistente.Chave.Should().Be("AIzaChaveColadaNoPainel0000000000001111");
        avisos.Should().Contain([nameof(AssistenteViewModel.EstaConfigurado), nameof(AssistenteViewModel.DescricaoDoModelo)]);
        (Painel.EstaConfigurado, Painel.DescricaoDoModelo).Should().Be((true, "Gemini (Google) · gemini-2.5-flash"));
    }

    [Fact]
    public void ComOAssistenteDeVerdade_SalvarAChaveNaTela_PassaAResponderPeloProvedor()
    {
        var cofre = new CofreEmMemoria();
        var provedor = new ProvedorFalso();
        var configuracao = new ConfiguracaoDoAssistente(cofre, new PreferenciasAssistenteEmMemoria(), provedor, _ => null);
        var vm = new MainWindowViewModel(_ambiente.Repositorio, _ambiente.Calculadora, _ambiente.Dialogos, _ambiente.Relogio,
            _ambiente.Preferencias, FabricaAssistenteIA.Criar(configuracao, provedor), _ambiente.PreferenciasInterface,
            _ambiente.Processador, configuracao);
        vm.Assistente.EstaConfigurado.Should().BeFalse();
        _ambiente.Dialogos.AoConfigurar = tela =>
        {
            tela.ChaveDigitada = "AIzaChaveDeVerdadeNoTeste000000000002222";
            tela.SalvarCommand.Execute(null);
        };

        vm.Assistente.ConfigurarCommand.Execute(null);
        vm.Assistente.TextoDigitado = "Olá";
        vm.Assistente.EnviarCommand.Execute(null);

        (vm.Assistente.EstaConfigurado, vm.Assistente.DescricaoDoModelo).Should().Be((true, "Gemini (Google) · gemini-2.5-flash"));
        vm.Assistente.Mensagens[^1].Texto.Should().Be("[gemini-2.5-flash] Olá");
        cofre.Segredos.Values.Should().Equal("AIzaChaveDeVerdadeNoTeste000000000002222");
    }

    [Fact]
    public void ErroDoAssistente_ApareceComoAvisoNaConversa()
    {
        _ambiente.Assistente.Erro = new ErroDoAssistenteException("O limite de uso do Gemini foi atingido. Espere um pouco e tente de novo.");
        Painel.TextoDigitado = "Oi";

        Painel.EnviarCommand.Execute(null);

        (Painel.Mensagens[^1].Texto, Painel.Mensagens[^1].EhErro)
            .Should().Be(("Não foi possível obter a resposta: O limite de uso do Gemini foi atingido. Espere um pouco e tente de novo.", true));
    }

    [Fact]
    public void Construtor_SemConfiguracaoOuRetrato_Recusado()
    {
        var semConfiguracao = () => new AssistenteViewModel(_ambiente.Assistente, _ambiente.Dialogos, _ambiente.Relogio,
            _ambiente.PreferenciasInterface, null!, _vm);
        var semRetrato = () => new AssistenteViewModel(_ambiente.Assistente, _ambiente.Dialogos, _ambiente.Relogio,
            _ambiente.PreferenciasInterface, _ambiente.ConfiguracaoAssistente, null!);

        semConfiguracao.Should().Throw<ArgumentNullException>();
        semRetrato.Should().Throw<ArgumentNullException>();
    }
}
