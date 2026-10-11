using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Fotos;

namespace ProjectPlanner.App.Tests;

// Bloco 30: Editar > Desfazer e Refazer, em vários níveis, para as edições da grid, a estrutura, os vínculos, o projeto e
// as fotos ("hoje" é seg 28/09/2026).
public class DesfazerTests : TesteDeFotos
{
    public DesfazerTests()
    {
        Ambiente.Dialogos.ArquivoParaSalvar = "/obra.pplan";
    }

    private string[] Linhas => Vm.Tarefas.Select(l => $"{l.Edt} {l.Nome}").ToArray();

    private void Desfazer() => Vm.DesfazerAlteracaoCommand.Execute(null);

    private void Refazer() => Vm.RefazerAlteracaoCommand.Execute(null);

    [Fact]
    public void ProjetoNovo_NadaParaDesfazer()
    {
        (Vm.DesfazerAlteracaoCommand.CanExecute(null), Vm.RefazerAlteracaoCommand.CanExecute(null), Vm.TextoDesfazer, Vm.TextoRefazer)
            .Should().Be((false, false, "Desfazer", "Refazer"));
    }

    [Fact]
    public void EdicoesDaGrid_DesfeitasERefeitas_ComONome()
    {
        var linha = Ambiente.AdicionarTarefa(Vm, "Escavação", 3);
        Vm.TextoDesfazer.Should().Be("Desfazer Duração de \"Escavação\"");

        linha.PercentualConcluido = 50;
        Vm.TextoDesfazer.Should().Be("Desfazer % concluído de \"Escavação\"");

        Desfazer();
        Vm.Tarefas.Single().PercentualConcluido.Should().Be(0);
        (Vm.Mensagem, Vm.TextoRefazer).Should().Be(("Desfeito: % concluído de \"Escavação\".", "Refazer % concluído de \"Escavação\""));
        Desfazer();
        Desfazer();
        Desfazer();
        Linhas.Should().BeEmpty("nome, duração e a tarefa nova também foram desfeitos");
        Vm.DesfazerAlteracaoCommand.CanExecute(null).Should().BeFalse();

        for (var i = 0; i < 4; i++)
            Refazer();
        var refeita = Vm.Tarefas.Single();
        (refeita.Nome, refeita.Duracao, refeita.PercentualConcluido, Vm.Mensagem).Should().Be(("Escavação", 3, 50, "Refeito: % concluído de \"Escavação\"."));
        Vm.TerminoProjeto.Should().Be(new DateTime(2026, 9, 30), "o cronograma é recalculado");
    }

    [Fact]
    public void Estrutura_RemoverResumoComSubtarefasEVinculos_VoltaTudo()
    {
        Ambiente.AdicionarTarefa(Vm, "Fundação", 3);
        Ambiente.AdicionarTarefa(Vm, "Escavação", 2);
        Ambiente.AdicionarTarefa(Vm, "Sapatas", 4, "2");
        Ambiente.AdicionarTarefa(Vm, "Estrutura", 5, "3");
        Vm.TarefaSelecionada = Vm.Tarefas[1];
        Vm.RebaixarCommand.Execute(null);
        Vm.TarefaSelecionada = Vm.Tarefas[2];
        Vm.RebaixarCommand.Execute(null);
        Linhas.Should().Equal("1 Fundação", "1.1 Escavação", "1.2 Sapatas", "2 Estrutura");
        Vm.TarefaSelecionada = Vm.Tarefas[0];

        Vm.RemoverTarefaCommand.Execute(null);
        Linhas.Should().Equal("1 Estrutura");
        Vm.TextoDesfazer.Should().Be("Desfazer Remover \"Fundação\"");

        Desfazer();
        Linhas.Should().Equal("1 Fundação", "1.1 Escavação", "1.2 Sapatas", "2 Estrutura");
        (Vm.Tarefas[2].Predecessoras, Vm.Tarefas[3].Predecessoras).Should().Be(("2", "3"));
        Desfazer();
        Linhas.Should().Equal(["1 Fundação", "1.1 Escavação", "2 Sapatas", "3 Estrutura"], "desfez o Rebaixar");
    }

    [Fact]
    public void MoverNaGrid_VinculoNoGantt_NomeEInicioDoProjeto()
    {
        var a = Ambiente.AdicionarTarefa(Vm, "A", 1);
        var b = Ambiente.AdicionarTarefa(Vm, "B", 1);
        Vm.SoltarTarefa(b, linhaVisivel: 0, metadeDeBaixo: false).Should().BeTrue();
        Linhas.Should().Equal("1 B", "2 A");
        ((ILigacoesNoGantt)Vm).Ligar(b.Tarefa.Id, a.Tarefa.Id, TipoDependencia.FS).Should().BeTrue();
        Vm.NomeProjeto = "Residência";
        Vm.InicioProjeto = new DateTime(2026, 10, 5);

        Vm.TextoDesfazer.Should().Be("Desfazer Início do projeto");
        Desfazer();
        Vm.InicioProjeto.Should().Be(Ambiente.Segunda);
        Desfazer();
        Vm.NomeProjeto.Should().Be("Projeto sem título");
        Vm.TextoDesfazer.Should().Be("Desfazer Vínculo B → A");
        Desfazer();
        Vm.Tarefas[1].Predecessoras.Should().BeEmpty();
        Vm.TextoDesfazer.Should().Be("Desfazer Mover \"B\"");
        Desfazer();
        Linhas.Should().Equal("1 A", "2 B");
    }

    [Fact]
    public void SelecaoERecolhidos_SobrevivemAoDesfazer()
    {
        Vm.AdicionarResumoCommand.Execute(null);
        var resumo = Vm.Tarefas[0];
        Ambiente.AdicionarTarefa(Vm, "Outra", 2);
        resumo.Expandido = false;
        Vm.TarefaSelecionada = Vm.LinhasVisiveis.Single(l => l.Nome == "Outra");

        Desfazer(); // a duração de "Outra"

        Vm.Tarefas[0].Expandido.Should().BeFalse();
        Vm.TarefaSelecionada!.Nome.Should().Be("Outra");
        Vm.LinhasVisiveis.Should().HaveCount(2);
    }

    [Fact]
    public void Salvo_DesfazerAteOSalvoTiraOAsterisco_EAlteracaoForaDoHistoricoMantem()
    {
        var linha = Ambiente.AdicionarTarefa(Vm, "A", 2);
        Vm.SalvarProjetoCommand.Execute(null);
        linha.Duracao = 5;
        Vm.Titulo.Should().StartWith("*");

        Desfazer();
        (Vm.PossuiAlteracoesNaoSalvas, Vm.Titulo.StartsWith('*')).Should().Be((false, false));
        Desfazer();
        Vm.PossuiAlteracoesNaoSalvas.Should().BeTrue("antes do salvo também é diferente do arquivo");
        Refazer();
        Vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();

        Vm.ConversaMudou(); // a ficha ou o modo do assistente mudaram: não entra no histórico
        Refazer();
        Desfazer();
        Vm.PossuiAlteracoesNaoSalvas.Should().BeTrue("a ficha mudou depois de salvar");
    }

    [Fact]
    public void DadosQueNaoVoltam_LocaisDasFotosEObra()
    {
        Ambiente.AdicionarTarefa(Vm, "A", 2);
        Vm.FotosDoProjetoCommand.Execute(null);
        var fotos = Ambiente.Dialogos.FotosDoProjeto.Last();
        fotos.TextoDosLocais = "Bloco A";
        fotos.AplicarConfiguracaoCommand.Execute(null);

        Desfazer(); // duração
        Desfazer(); // nome
        Desfazer(); // a tarefa nova

        Vm.Tarefas.Should().BeEmpty();
        Vm.ConfiguracaoDasFotos.Locais.Should().Equal("Bloco A");
        Vm.TextoDesfazer.Should().Be("Desfazer");
    }

    [Fact]
    public void OutroProjeto_HistoricoRecomeca()
    {
        Ambiente.AdicionarTarefa(Vm, "A", 2);
        Ambiente.Dialogos.RespostaAlteracoesPendentes = Services.RespostaAlteracoesPendentes.NaoSalvar;

        Vm.NovoProjetoCommand.Execute(null);

        (Vm.DesfazerAlteracaoCommand.CanExecute(null), Vm.RefazerAlteracaoCommand.CanExecute(null)).Should().Be((false, false));
    }

    // ---- Fotos ----

    [Fact]
    public async Task Fotos_Adicionar_Excluir_Girar_EditarECuradoria_TudoVolta()
    {
        var linha = Ambiente.AdicionarTarefa(Vm, "Fundação", 3);
        var galeria = await ComFotos(linha, "a.jpg", "b.jpg");
        var original = linha.Tarefa.Fotos.Single(f => f.Imagem.NomeArquivoOriginal == "a.jpg");
        galeria.Selecionar(Item(galeria, "a.jpg"), adicionar: false);
        galeria.DestaqueNoRelatorio = true;
        galeria.EdicaoTitulo = "Gabarito";
        galeria.AplicarEdicaoCommand.Execute(null);
        await galeria.GirarDireitaCommand.ExecuteAsync(null);
        galeria.Selecionar(Item(galeria, "b.jpg"), adicionar: false);
        await galeria.ExcluirCommand.ExecuteAsync(null);
        linha.QuantidadeDeFotos.Should().Be(1);

        Desfazer(); // excluir b
        Vm.Tarefas[0].QuantidadeDeFotos.Should().Be(2);
        var b = Vm.Tarefas[0].Tarefa.Fotos.Single(f => f.Imagem.NomeArquivoOriginal == "b.jpg");
        ProcessadorFalso.Descrever(Vm.Fotos.Imagem(b.Id)).Should().Be("b.jpg|1600|1200", "a imagem da foto excluída volta junto");

        Desfazer(); // girar
        var a = Vm.Tarefas[0].Tarefa.Fotos.Single(f => f.Imagem.NomeArquivoOriginal == "a.jpg");
        (a.Id, a.Imagem.Largura, a.Titulo, a.Destaque).Should().Be((original.Id, 1600, "Gabarito", true));
        ProcessadorFalso.Descrever(Vm.Fotos.Imagem(a.Id)).Should().Be("a.jpg|1600|1200");

        Desfazer(); // título
        Desfazer(); // destaque
        Vm.Tarefas[0].Tarefa.Fotos.Single(f => f.Id == original.Id).Should().Be(original);

        Desfazer(); // adicionar as duas
        (Vm.Tarefas[0].QuantidadeDeFotos, Vm.FotosDaSelecionada).Should().Be((0, 0));
        for (var i = 0; i < 5; i++)
            Refazer();
        var girada = Vm.Tarefas[0].Tarefa.Fotos.Single();
        ProcessadorFalso.Descrever(Vm.Fotos.Imagem(girada.Id)).Should().Be("a.jpg↻|1200|1600");
    }

    [Fact]
    public async Task Fotos_SalvarNoMeio_AsImagensContinuamDisponiveis_ESalvarDeNovoFunciona()
    {
        var repositorio = new RepositorioEmMemoria();
        var vm = new MainWindowViewModel(repositorio, Ambiente.Calculadora, Ambiente.Dialogos, Ambiente.Relogio, Ambiente.Preferencias,
            Ambiente.Assistente, Ambiente.PreferenciasInterface, Ambiente.Processador, Ambiente.ConfiguracaoAssistente);
        var linha = Ambiente.AdicionarTarefa(vm, "Fundação", 3);
        vm.GerenciarFotosCommand.Execute(linha);
        var galeria = Ambiente.Dialogos.Galerias.Last();
        await galeria.AdicionarArquivosAsync(["/fotos/a.jpg", "/fotos/b.jpg"]);
        await vm.SalvarProjetoCommand.ExecuteAsync(null);
        galeria.SelecionarTodasCommand.Execute(null);
        await galeria.ExcluirCommand.ExecuteAsync(null);
        await vm.SalvarProjetoCommand.ExecuteAsync(null); // o arquivo fica sem as duas imagens
        repositorio.ImagensDe("/obra.pplan").Should().BeEmpty();

        vm.DesfazerAlteracaoCommand.Execute(null);
        vm.Tarefas[0].QuantidadeDeFotos.Should().Be(2);
        vm.Tarefas[0].Tarefa.Fotos.Should().OnlyContain(f => vm.Fotos.Imagem(f.Id) != null);
        await vm.SalvarProjetoCommand.ExecuteAsync(null);

        (vm.Mensagem, repositorio.ImagensDe("/obra.pplan").Count).Should().Be(("Projeto salvo em /obra.pplan", 2));
        vm.RefazerAlteracaoCommand.Execute(null);
        vm.DesfazerAlteracaoCommand.Execute(null);
        vm.Tarefas[0].QuantidadeDeFotos.Should().Be(2);
    }

    [Fact]
    public async Task Fotos_GirarDepoisDeSalvar_DesfazerTrazAOriginalDoArquivo()
    {
        var repositorio = new RepositorioEmMemoria();
        var vm = new MainWindowViewModel(repositorio, Ambiente.Calculadora, Ambiente.Dialogos, Ambiente.Relogio, Ambiente.Preferencias,
            Ambiente.Assistente, Ambiente.PreferenciasInterface, Ambiente.Processador, Ambiente.ConfiguracaoAssistente);
        var linha = Ambiente.AdicionarTarefa(vm, "Fundação", 3);
        vm.GerenciarFotosCommand.Execute(linha);
        var galeria = Ambiente.Dialogos.Galerias.Last();
        await galeria.AdicionarArquivosAsync(["/fotos/a.jpg"]);
        await vm.SalvarProjetoCommand.ExecuteAsync(null);
        galeria.Selecionar(Item(galeria, "a.jpg"), adicionar: false);
        await galeria.GirarDireitaCommand.ExecuteAsync(null);
        await vm.SalvarProjetoCommand.ExecuteAsync(null);

        vm.DesfazerAlteracaoCommand.Execute(null);

        var foto = vm.Tarefas[0].Tarefa.Fotos.Single();
        ProcessadorFalso.Descrever(vm.Fotos.Imagem(foto.Id)).Should().Be("a.jpg|1600|1200");
        await vm.SalvarProjetoCommand.ExecuteAsync(null);
        ProcessadorFalso.Descrever(repositorio.ImagensDe("/obra.pplan")[foto.Id].Imagem).Should().Be("a.jpg|1600|1200");
    }

    // ---- Copiar, colar e duplicar ----

    [Fact]
    public void CopiarEColar_DepoisDaSelecionada_ComSubtarefasEVinculos()
    {
        Vm.AdicionarResumoCommand.Execute(null); // 1 Novo resumo > 1.1 Nova tarefa
        Vm.Tarefas[0].Nome = "Fundação";
        Vm.Tarefas[1].Nome = "Escavação";
        Vm.TarefaSelecionada = Vm.Tarefas[1];
        Vm.InserirTarefaAbaixoCommand.Execute(null);
        Vm.Tarefas[2].Nome = "Sapatas";
        Vm.Tarefas[2].Predecessoras = "2";
        Ambiente.AdicionarTarefa(Vm, "Estrutura", 5);
        (Vm.ColarTarefaCommand.CanExecute(null), Vm.TemTarefasCopiadas).Should().Be((false, false));
        Vm.TarefaSelecionada = Vm.Tarefas[0];

        Vm.CopiarTarefaCommand.Execute(null);
        Vm.Mensagem.Should().Be("Copiado: \"Fundação\" (com 2 subtarefas). Selecione onde colar (a cópia entra logo abaixo) e use Colar.");
        Vm.PossuiAlteracoesNaoSalvas.Should().BeTrue("as tarefas foram criadas antes");
        Vm.TextoDesfazer.Should().NotContain("Copiar", "copiar não altera o projeto");
        Vm.TarefaSelecionada = Vm.Tarefas[3];
        Vm.ColarTarefaCommand.Execute(null);

        Linhas.Should().Equal("1 Fundação", "1.1 Escavação", "1.2 Sapatas", "2 Estrutura", "3 Fundação", "3.1 Escavação", "3.2 Sapatas");
        (Vm.TarefaSelecionada!.Edt, Vm.Mensagem, Vm.TextoDesfazer).Should().Be(
            ("3", "Colado: \"Fundação\" (com 2 subtarefas).", "Desfazer Colar \"Fundação\" (com 2 subtarefas)"));
        Vm.Tarefas[6].Predecessoras.Should().Be("6", "o vínculo é entre as cópias");

        Desfazer();
        Linhas.Should().HaveCount(4);
    }

    [Fact]
    public void Duplicar_LogoAbaixo_SemMexerNoCopiado()
    {
        var a = Ambiente.AdicionarTarefa(Vm, "A", 2);
        Ambiente.AdicionarTarefa(Vm, "B", 3);
        a.PercentualConcluido = 30;
        Vm.TarefaSelecionada = a;

        Vm.DuplicarTarefaCommand.Execute(null);

        Linhas.Should().Equal("1 A", "2 A", "3 B");
        (Vm.Tarefas[1].Duracao, Vm.Tarefas[1].PercentualConcluido, Vm.TarefaSelecionada!.Numero, Vm.Mensagem)
            .Should().Be((2, 30, 2, "Duplicado: \"A\"."));
        Vm.TemTarefasCopiadas.Should().BeFalse();
    }

    [Fact]
    public async Task Colar_SemFotos_ESemSelecaoVaiParaOFim_EmOutroProjeto()
    {
        var linha = Ambiente.AdicionarTarefa(Vm, "Fundação", 3);
        await ComFotos(linha, "a.jpg");
        Vm.TarefaSelecionada = linha;
        Vm.CopiarTarefaCommand.Execute(null);
        Ambiente.Dialogos.RespostaAlteracoesPendentes = Services.RespostaAlteracoesPendentes.NaoSalvar;
        Vm.NovoProjetoCommand.Execute(null);
        Ambiente.AdicionarTarefa(Vm, "Outra", 1);
        Vm.TarefaSelecionada = null;

        Vm.ColarTarefaCommand.Execute(null);

        Linhas.Should().Equal("1 Outra", "2 Fundação");
        Vm.Tarefas[1].QuantidadeDeFotos.Should().Be(0, "as fotos ficam na tarefa de origem");
    }

    [Fact]
    public void Colar_EntreAsSubtarefas_EComandosConformeASelecao()
    {
        var a = Ambiente.AdicionarTarefa(Vm, "A", 2);
        Vm.TarefaSelecionada = a;
        Vm.CopiarTarefaCommand.Execute(null);
        Vm.AdicionarResumoCommand.Execute(null); // 2 Novo resumo > 2.1 Nova tarefa
        var sub = Vm.Tarefas[2];
        var antes = Vm.TextoDesfazer;
        Vm.TarefaSelecionada = sub;

        Vm.ColarTarefaCommand.Execute(null);

        Linhas.Should().Equal("1 A", "2 Novo resumo", "2.1 Nova tarefa", "2.2 A");
        Vm.TextoDesfazer.Should().NotBe(antes);
        (Vm.CopiarTarefaCommand.CanExecute(null), Vm.DuplicarTarefaCommand.CanExecute(null)).Should().Be((true, true));
        Vm.TarefaSelecionada = null;
        (Vm.CopiarTarefaCommand.CanExecute(null), Vm.DuplicarTarefaCommand.CanExecute(null), Vm.ColarTarefaCommand.CanExecute(null))
            .Should().Be((false, false, true));
    }
}
