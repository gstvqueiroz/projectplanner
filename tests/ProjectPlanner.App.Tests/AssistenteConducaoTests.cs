using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core.Assistente;
using ProjectPlanner.Data;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Tests;

// Bloco 26: o chat em dois modos (montar agora ou estudar e conversar antes), as perguntas com respostas para clicar, o
// plano para aprovar e a ficha do projeto, no ViewModel ("hoje" é seg 28/09/2026).
public sealed class AssistenteConducaoTests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly Queue<RespostaIA> _respostas = new();

    public AssistenteConducaoTests()
    {
        _vm = _ambiente.CriarViewModel();
        _ambiente.Assistente.ResponderComAlteracoes = p => _respostas.Count > 0 ? _respostas.Dequeue() : new RespostaIA($"Resposta a: {p.Mensagem}");
    }

    private AssistenteViewModel Painel => _vm.Assistente;
    private ConversaDoProjeto Conversa => _vm.Conversa;
    private ConducaoDaConversa UltimaConducao => _ambiente.Assistente.Pedidos[^1].Conducao!;

    private void Pedir(string texto)
    {
        Painel.TextoDigitado = texto;
        Painel.EnviarCommand.Execute(null);
    }

    private static RespostaIA Modo(string texto = "Posso montar agora ou estudar antes. Como prefere?") =>
        new(texto) { Tipo = TipoDeResposta.EscolherModo, Ficha = [new ItemDeFicha("Tipo de obra", "Residência térrea")] };

    private static RespostaIA Perguntas() => new("Li a planta. Algumas perguntas:")
    {
        Tipo = TipoDeResposta.Perguntas,
        Perguntas = [new PerguntaIA("Qual a estrutura?", ["Concreto armado", "Alvenaria estrutural"]), new PerguntaIA("Quando começa?", [])],
        Ficha = [new ItemDeFicha("Área construída", "100 m²")],
    };

    private static RespostaIA Plano() => new("O plano:")
    {
        Tipo = TipoDeResposta.Plano,
        Plano = new PlanoIA([new EtapaIA("Fundação", 15, "sapatas"), new EtapaIA("Estrutura", 25)], "2027-02-26", ["Sábado sem obra"]),
    };

    private static readonly AlteracaoDoCronograma[] Casa =
    [
        new(AcoesNoCronograma.Criar, Ref: "n1", Nome: "Fundação"),
        new(AcoesNoCronograma.Criar, Ref: "n2", Nome: "Escavação", Duracao: 3, Pai: "n1"),
        new(AcoesNoCronograma.Criar, Ref: "n3", Nome: "Sapatas", Duracao: 5, Pai: "n1"),
        new(AcoesNoCronograma.Vincular, De: "n2", Para: "n3"),
    ];

    // ---- A escolha do modo ----

    [Fact]
    public void PedidoDeCronograma_SemModo_MostraOCartao_ENaoMexeNoCronograma()
    {
        _respostas.Enqueue(Modo());

        Pedir("Monte o cronograma de uma casa de 100 m²");

        var conducao = UltimaConducao;
        (conducao.Modo, conducao.Preferencia, conducao.RodadasDePerguntas).Should().Be((ModoDeTrabalho.NaoEscolhido, PreferenciaDeModo.Perguntar, 0));
        Painel.Ultima!.EscolheModo.Should().BeTrue();
        (Painel.EscolherModoCommand.CanExecute("direto"), Painel.EscolherModoCommand.CanExecute("estudo"), Painel.EscolherModoCommand.CanExecute("outro"))
            .Should().Be((true, true, false));
        _vm.Tarefas.Should().BeEmpty();
        Conversa.Ficha["Tipo de obra"].Should().Be("Residência térrea");
        _vm.PossuiAlteracoesNaoSalvas.Should().BeTrue("a ficha mudou");
    }

    [Fact]
    public void MontarAgora_ManDaOPedidoNoModoDireto_EAplica()
    {
        _respostas.Enqueue(Modo());
        Pedir("Monte o cronograma de uma casa");
        _respostas.Enqueue(new RespostaIA("Montei.") { Alteracoes = Casa, Ficha = [new ItemDeFicha("Dias de trabalho", "Segunda a sexta")] });

        Painel.EscolherModoCommand.Execute("direto");

        _ambiente.Assistente.Pedidos[^1].Mensagem.Should().Be(AssistenteViewModel.TextoMontarAgora);
        UltimaConducao.Modo.Should().Be(ModoDeTrabalho.Direto);
        (Conversa.Modo, _vm.Tarefas.Count, Painel.Ultima!.Alteracoes!.Aplicadas).Should().Be((ModoDaConversa.Direto, 3, true));
        Painel.EtapaDoEstudo.Should().Be("Modo: montar direto");
        Painel.EscolherModoCommand.CanExecute("estudo").Should().BeFalse("o cartão só vale enquanto é a última mensagem");
    }

    [Fact]
    public void PreferenciaNasConfiguracoes_VaiComOPedido()
    {
        _ambiente.ConfiguracaoAssistente.PreferenciaDeModo = PreferenciaDeModo.Estudo;

        Pedir("Monte o cronograma");

        UltimaConducao.Preferencia.Should().Be(PreferenciaDeModo.Estudo);
    }

    // ---- O estudo: perguntas, plano e montagem ----

    [Fact]
    public void Estudo_PerguntasComRespostasParaClicar_EaFicha()
    {
        _respostas.Enqueue(Modo());
        Pedir("Monte o cronograma da casa");
        _respostas.Enqueue(Perguntas());

        Painel.EscolherModoCommand.Execute("estudo");

        var perguntas = Painel.Ultima!.Perguntas;
        (Conversa.Modo, Conversa.RodadasDePerguntas, Painel.EmEstudo).Should().Be((ModoDaConversa.Estudo, 1, true));
        perguntas.Select(p => p.Texto).Should().Equal("Qual a estrutura?", "Quando começa?");
        perguntas[0].Opcoes.Should().Equal("Concreto armado", "Alvenaria estrutural", PerguntaNaTela.NaoSei);
        perguntas[1].Opcoes.Should().Equal(PerguntaNaTela.NaoSei);
        Painel.EtapaDoEstudo.Should().Be("Estudo · perguntas (1 de 4)");
        Painel.Ficha.Select(c => $"{c.Campo}: {c.Valor}").Should().Equal("Tipo de obra: Residência térrea", "Área construída: 100 m²");
        Painel.ResponderPerguntasCommand.CanExecute(Painel.Ultima).Should().BeFalse("nada respondido ainda");

        perguntas[0].Escolhida = "Concreto armado";
        Painel.ResponderPerguntasCommand.CanExecute(Painel.Ultima).Should().BeTrue();
        perguntas[1].Outra = "  Em novembro ";
        Painel.TextoDigitado = "A casa tem 2 banheiros.";
        Painel.ResponderPerguntasCommand.Execute(Painel.Ultima);

        _ambiente.Assistente.Pedidos[^1].Mensagem.Should().Be(
            "Respostas:\n- Qual a estrutura? Concreto armado\n- Quando começa? Em novembro\nA casa tem 2 banheiros.");
        (UltimaConducao.Modo, UltimaConducao.RodadasDePerguntas, UltimaConducao.Ficha.Count).Should().Be((ModoDeTrabalho.Estudo, 1, 2));
        Painel.TextoDigitado.Should().BeEmpty();
    }

    [Fact]
    public void Estudo_PerguntaSemResposta_VaiComoSemResposta()
    {
        _respostas.Enqueue(Perguntas());
        Pedir("Estude a planta");
        Painel.Ultima!.Perguntas[1].Escolhida = PerguntaNaTela.NaoSei;

        Painel.ResponderPerguntasCommand.Execute(Painel.Ultima);

        _ambiente.Assistente.Pedidos[^1].Mensagem.Should().Be(
            $"Respostas:\n- Qual a estrutura? {PerguntaNaTela.SemResposta}\n- Quando começa? {PerguntaNaTela.NaoSei}");
        Conversa.Modo.Should().Be(ModoDaConversa.Estudo, "perguntas são do estudo, mesmo sem a escolha no cartão");
    }

    [Fact]
    public void Estudo_PlanoAprovado_MontaTudo_EEncerraOEstudo()
    {
        _respostas.Enqueue(Perguntas());
        Pedir("Estude a planta");
        _respostas.Enqueue(Plano());
        Painel.TextoDigitado = "Concreto armado, começa em novembro";
        Painel.EnviarCommand.Execute(null);

        var plano = Painel.Ultima!.Plano!;
        plano.Etapas.Select(e => $"{e.Nome} {e.Duracao}").Should().Equal("Fundação 15 dias", "Estrutura 25 dias");
        (plano.Termino, plano.Premissas.Single()).Should().Be(("Término estimado: 26/02/2027", "Sábado sem obra"));
        Painel.EtapaDoEstudo.Should().Be("Estudo · plano para aprovar");
        (Painel.PularParaOPlanoCommand.CanExecute(null), Painel.MostrarPularParaOPlano).Should().Be((false, false), "o plano já está aí");

        _respostas.Enqueue(new RespostaIA("Montei o cronograma completo.") { Alteracoes = Casa });
        Painel.AprovarPlanoCommand.Execute(Painel.Ultima);

        _ambiente.Assistente.Pedidos[^1].Mensagem.Should().Be(AssistenteViewModel.TextoPlanoAprovado);
        UltimaConducao.PlanoAprovado.Should().BeTrue();
        _vm.Tarefas.Should().HaveCount(3);
        (Conversa.Modo, Conversa.PlanoAprovado).Should().Be((ModoDaConversa.Direto, false), "o estudo acabou: daqui em diante, ajustes diretos");
    }

    [Fact]
    public void Estudo_PlanoAprovadoMasRecusado_ContinuaAprovado_ParaTentarDeNovo()
    {
        _respostas.Enqueue(Plano());
        Pedir("Estude");
        var invalida = new RespostaIA("Montei.") { Alteracoes = [new AlteracaoDoCronograma(AcoesNoCronograma.Remover, Tarefa: "99")] };
        _respostas.Enqueue(invalida);
        _respostas.Enqueue(invalida);

        Painel.AprovarPlanoCommand.Execute(Painel.Ultima);

        (Conversa.Modo, Conversa.PlanoAprovado, Painel.Ultima!.Alteracoes!.Aplicadas).Should().Be((ModoDaConversa.Estudo, true, false));
        Painel.EtapaDoEstudo.Should().Be("Estudo · montando o cronograma");
    }

    [Fact]
    public void Estudo_PlanoNovo_PedeAprovacaoDeNovo()
    {
        _respostas.Enqueue(Plano());
        Pedir("Estude");
        Painel.AjustarPlanoCommand.Execute(Painel.Ultima);
        var foco = 0;
        Painel.FocoNaEntradaPedido += () => foco++;
        Painel.AjustarPlanoCommand.Execute(Painel.Ultima);

        Painel.TextoDigitado.Should().Be(AssistenteViewModel.PrefixoDoAjuste, "o prefixo não se repete");
        foco.Should().Be(1);

        Conversa.DefinirPlanoAprovado(true);
        _respostas.Enqueue(Plano());
        Painel.TextoDigitado += "a fundação leva 20 dias";
        Painel.EnviarCommand.Execute(null);

        Conversa.PlanoAprovado.Should().BeFalse();
    }

    [Fact]
    public void Estudo_PularParaOPlano_PedeOPlanoAgora()
    {
        Painel.PularParaOPlanoCommand.CanExecute(null).Should().BeFalse("fora do estudo");
        _respostas.Enqueue(Perguntas());
        Pedir("Estude a planta");

        Painel.PularParaOPlanoCommand.Execute(null);

        (_ambiente.Assistente.Pedidos[^1].Mensagem, UltimaConducao.ApresentarPlanoAgora).Should().Be((AssistenteViewModel.TextoPularParaOPlano, true));
    }

    [Fact]
    public void BotoesDasMensagensAntigas_FicamInativos()
    {
        _respostas.Enqueue(Perguntas());
        Pedir("Estude a planta");
        var antiga = Painel.Ultima!;
        antiga.Perguntas[0].Escolhida = "Concreto armado";

        Pedir("E o prazo?");

        Painel.ResponderPerguntasCommand.CanExecute(antiga).Should().BeFalse();
        Painel.AprovarPlanoCommand.CanExecute(antiga).Should().BeFalse();
    }

    [Fact]
    public async Task DuranteAEspera_BotoesInativos()
    {
        _respostas.Enqueue(Modo());
        Pedir("Monte o cronograma");
        _ambiente.Assistente.Segurar();

        var envio = Painel.EscolherModoCommand.ExecuteAsync("estudo");

        Painel.EscolherModoCommand.CanExecute("direto").Should().BeFalse();
        _ambiente.Assistente.Soltar();
        await envio;
    }

    [Fact]
    public void ErroNaResposta_NaoMudaOModoNemAFicha()
    {
        _respostas.Enqueue(Modo());
        Pedir("Monte o cronograma");
        _ambiente.Assistente.Erro = new ErroDoAssistenteException("sem conexão");

        Painel.EscolherModoCommand.Execute("estudo");

        (Conversa.Modo, Conversa.Ficha.Itens.Count, Painel.Mensagens[^1].EhErro).Should().Be((ModoDaConversa.Estudo, 1, true));
        Conversa.Mensagens.Should().NotContain(m => m.Texto.Contains("sem conexão"), "avisos locais não ficam no projeto");
    }

    // ---- A ficha ----

    [Fact]
    public void Ficha_AcrescentarEditarApagarERemover_MarcaOProjeto()
    {
        Painel.NovoCampo = "Prazo desejado";
        Painel.NovoValor = "6 meses";
        Painel.AdicionarCampoCommand.Execute(null);
        (Conversa.Ficha["Prazo desejado"], Painel.NovoCampo, _vm.PossuiAlteracoesNaoSalvas).Should().Be(("6 meses", "", true));

        Painel.Ficha.Single().Valor = "8 meses";
        Conversa.Ficha["Prazo desejado"].Should().Be("8 meses");

        Painel.Ficha.Single().Valor = "";
        (Conversa.Ficha.Vazia, Painel.Ficha.Count, Painel.TituloDaFicha).Should().Be((true, 0, "Ficha do projeto"));

        Conversa.Ficha.Definir("Equipe", "6 pessoas");
        Painel.NovoCampo = "Local";
        Painel.NovoValor = "Campinas";
        Painel.AdicionarCampoCommand.Execute(null);
        Painel.RemoverCampoCommand.Execute(Painel.Ficha.Single(c => c.Campo == "Local"));
        Conversa.Ficha.Itens.Select(i => i.Campo).Should().Equal("Equipe");
    }

    [Fact]
    public void Ficha_CampoInvalido_AvisaENaoAcrescenta()
    {
        Painel.AdicionarCampoCommand.CanExecute(null).Should().BeFalse();
        Painel.NovoCampo = new string('c', FichaDoProjeto.TamanhoMaximoDoCampo + 1);
        Painel.NovoValor = "x";

        Painel.AdicionarCampoCommand.Execute(null);

        (Conversa.Ficha.Vazia, Painel.AvisoDaFicha).Should().Be((true, $"O nome do campo passa de {FichaDoProjeto.TamanhoMaximoDoCampo} caracteres."));
        _vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();
    }

    [Fact]
    public void LimparConversa_RecomecaOModo_MantemAFicha()
    {
        _respostas.Enqueue(Perguntas());
        Pedir("Estude a planta");

        Painel.LimparConversaCommand.Execute(null);

        (Conversa.Modo, Conversa.Mensagens.Count, Conversa.Ficha.Itens.Count, Painel.TemEtapaDoEstudo).Should().Be((ModoDaConversa.NaoEscolhido, 0, 1, false));
    }

    [Fact]
    public void ConversaSemModoNemFicha_NaoMarcaOProjeto_MasVaiNoProjeto()
    {
        Pedir("Qual é o caminho crítico?");

        _vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();
        Conversa.Mensagens.Select(m => m.Texto).Should().Equal("Qual é o caminho crítico?", "Resposta a: Qual é o caminho crítico?");
    }

    [Fact]
    public void NovoProjeto_ConversaEFichaVazias()
    {
        _respostas.Enqueue(Perguntas());
        Pedir("Estude a planta");

        _ambiente.Dialogos.RespostaAlteracoesPendentes = RespostaAlteracoesPendentes.NaoSalvar;
        _vm.NovoProjetoCommand.Execute(null);

        (Painel.Mensagens.Count, Painel.Ficha.Count, Painel.EmEstudo, Conversa.Vazia).Should().Be((0, 0, false, true));
    }
}

// Bloco 26: a conversa e a ficha vão no .pplan de verdade e voltam ao abrir, com o estudo continuando de onde parou.
public sealed class ConversaNoProjetoTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-conversa-" + Guid.NewGuid().ToString("N"));
    private readonly Ambiente _ambiente = new();
    private readonly Queue<RespostaIA> _respostas = new();

    public ConversaNoProjetoTests()
    {
        Directory.CreateDirectory(_pasta);
        _ambiente.Assistente.ResponderComAlteracoes = p => _respostas.Count > 0 ? _respostas.Dequeue() : new RespostaIA($"Resposta a: {p.Mensagem}");
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private string Arquivo => Path.Combine(_pasta, "obra.pplan");

    private MainWindowViewModel NovaVm() => new(new SqliteProjetoRepository(_ambiente.Relogio), new CalculadoraCronogramaServico(),
        _ambiente.Dialogos, _ambiente.Relogio, _ambiente.Preferencias, _ambiente.Assistente, _ambiente.PreferenciasInterface,
        _ambiente.Processador, _ambiente.ConfiguracaoAssistente);

    [Fact]
    public void SalvarEAbrir_AConversaVolta_EOEstudoContinua()
    {
        var vm = NovaVm();
        _respostas.Enqueue(new RespostaIA("Li a planta. Perguntas:")
        {
            Tipo = TipoDeResposta.Perguntas,
            Perguntas = [new PerguntaIA("Qual a estrutura?", ["Concreto armado", "Alvenaria estrutural"])],
            Ficha = [new ItemDeFicha("Área construída", "100 m²")],
        });
        File.WriteAllText(Path.Combine(_pasta, "memorial.txt"), "Casa térrea");
        vm.Assistente.AnexosPendentes.Add(AnexoIA.DoArquivo(Path.Combine(_pasta, "memorial.txt")));
        vm.Assistente.TextoDigitado = "Estude o memorial";
        vm.Assistente.EnviarCommand.Execute(null);
        _ambiente.Dialogos.ArquivoParaSalvar = Arquivo;
        vm.SalvarProjetoCommand.Execute(null);

        var reaberta = NovaVm();
        _ambiente.Dialogos.ArquivoParaAbrir = Arquivo;
        reaberta.AbrirProjetoCommand.Execute(null);

        var painel = reaberta.Assistente;
        painel.Mensagens.Select(m => m.Texto).Should().Equal("Estude o memorial", "Li a planta. Perguntas:");
        painel.Mensagens[0].Anexos.Single().Nome.Should().Be("memorial.txt");
        (painel.EmEstudo, painel.Ficha.Single().Valor, reaberta.PossuiAlteracoesNaoSalvas).Should().Be((true, "100 m²", false));
        painel.Ultima!.Perguntas.Single().Opcoes.Should().Contain("Concreto armado");

        painel.Ultima.Perguntas[0].Escolhida = "Concreto armado";
        painel.ResponderPerguntasCommand.Execute(painel.Ultima);

        var pedido = _ambiente.Assistente.Pedidos[^1];
        pedido.Historico.Select(m => m.Texto).Should().Equal("Estude o memorial", "Li a planta. Perguntas:");
        pedido.Historico[1].Resposta!.Perguntas.Single().Texto.Should().Be("Qual a estrutura?", "o assistente vê o que já perguntou");
        pedido.Historico[0].Anexos.Single().Nome.Should().Be("memorial.txt", "os anexos voltam a ir, enquanto o arquivo existir");
        (pedido.Conducao!.Modo, pedido.Conducao.RodadasDePerguntas).Should().Be((ModoDeTrabalho.Estudo, 1));
    }

    [Fact]
    public void AlteracoesDeOutraSessao_AparecemComoTexto_SemDesfazer()
    {
        var vm = NovaVm();
        _respostas.Enqueue(new RespostaIA("Montei.") { Alteracoes = [new AlteracaoDoCronograma(AcoesNoCronograma.Criar, Ref: "n1", Nome: "Fundação", Duracao: 5)] });
        vm.Assistente.TextoDigitado = "Crie a fundação";
        vm.Assistente.EnviarCommand.Execute(null);
        vm.Assistente.DesfazerCommand.Execute(vm.Assistente.Ultima!.Alteracoes);
        _respostas.Enqueue(new RespostaIA("De novo.") { Alteracoes = [new AlteracaoDoCronograma(AcoesNoCronograma.Criar, Ref: "n1", Nome: "Fundação", Duracao: 5)] });
        vm.Assistente.TextoDigitado = "Crie de novo";
        vm.Assistente.EnviarCommand.Execute(null);
        _ambiente.Dialogos.ArquivoParaSalvar = Arquivo;
        vm.SalvarProjetoCommand.Execute(null);

        var reaberta = NovaVm();
        _ambiente.Dialogos.ArquivoParaAbrir = Arquivo;
        reaberta.AbrirProjetoCommand.Execute(null);

        var alteracoes = reaberta.Assistente.Mensagens.Where(m => m.TemAlteracoes).Select(m => m.Alteracoes!).ToList();
        alteracoes.Select(a => a.Texto).Should().Equal("Desfeito: 1 tarefa criada", "Cronograma alterado: 1 tarefa criada");
        alteracoes.Should().OnlyContain(a => !a.PodeDesfazer);
        alteracoes[1].Aplicadas.Should().BeTrue();
    }

    [Fact]
    public void ConversaComDefeitoNoArquivo_OProjetoAbre_ComAviso()
    {
        var vm = NovaVm();
        vm.Assistente.TextoDigitado = "Oi";
        vm.Assistente.EnviarCommand.Execute(null);
        vm.Assistente.NovoCampo = "Área";
        vm.Assistente.NovoValor = "100 m²";
        vm.Assistente.AdicionarCampoCommand.Execute(null);
        _ambiente.Dialogos.ArquivoParaSalvar = Arquivo;
        vm.SalvarProjetoCommand.Execute(null);
        using (var conexao = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Arquivo};Pooling=False"))
        {
            conexao.Open();
            using var comando = conexao.CreateCommand();
            comando.CommandText = "UPDATE AssistenteDoProjeto SET Dados = '{ruim'";
            comando.ExecuteNonQuery();
        }

        var reaberta = NovaVm();
        _ambiente.Dialogos.ArquivoParaAbrir = Arquivo;
        reaberta.AbrirProjetoCommand.Execute(null);

        reaberta.Mensagem.Should().Contain("A conversa com o assistente guardada no arquivo não pôde ser lida e ficou de fora.");
        reaberta.Assistente.Mensagens.Should().BeEmpty();
    }
}
