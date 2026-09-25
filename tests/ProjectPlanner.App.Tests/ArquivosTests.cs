using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core;

namespace ProjectPlanner.App.Tests;

public class ArquivosTests
{
    private const string Caminho = "/projetos/obra.pplan";

    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;

    public ArquivosTests()
    {
        _vm = _ambiente.CriarViewModel();
    }

    private void AlterarAlgo() => Ambiente.AdicionarTarefa(_vm, "A", 2);

    // Salvar

    [Fact]
    public async Task Salvar_ProjetoNuncaSalvo_PerguntaOndeESalvaPeloRepositorio()
    {
        AlterarAlgo();
        _ambiente.Dialogos.ArquivoParaSalvar = Caminho;

        await _vm.SalvarProjetoCommand.ExecuteAsync(null);

        _ambiente.Dialogos.NomeSugerido.Should().Be("Projeto sem título");
        _ambiente.Repositorio.Salvos.Should().ContainSingle()
            .Which.Should().Be((_vm.Cronograma, Caminho));
        _vm.CaminhoArquivo.Should().Be(Caminho);
        _vm.TemAlteracoesNaoSalvas.Should().BeFalse();
        _vm.Titulo.Should().Be("Projeto sem título — ProjectPlanner");
        _vm.Mensagem.Should().Be($"Projeto salvo em {Caminho}");
    }

    [Fact]
    public async Task Salvar_ProjetoJaSalvo_SalvaNoMesmoArquivoSemPerguntar()
    {
        _ambiente.Dialogos.ArquivoParaSalvar = Caminho;
        await _vm.SalvarProjetoCommand.ExecuteAsync(null);
        AlterarAlgo();

        await _vm.SalvarProjetoCommand.ExecuteAsync(null);

        _ambiente.Dialogos.VezesQuePerguntouOndeSalvar.Should().Be(1);
        _ambiente.Repositorio.Salvos.Select(s => s.Caminho).Should().Equal(Caminho, Caminho);
    }

    [Fact]
    public async Task Salvar_CancelandoODialogo_NaoSalva()
    {
        AlterarAlgo();

        await _vm.SalvarProjetoCommand.ExecuteAsync(null);

        _ambiente.Repositorio.Salvos.Should().BeEmpty();
        _vm.TemAlteracoesNaoSalvas.Should().BeTrue();
    }

    [Fact]
    public async Task Salvar_ComErroDeEscrita_MostraAMensagemSemLancar()
    {
        AlterarAlgo();
        _ambiente.Dialogos.ArquivoParaSalvar = Caminho;
        _ambiente.Repositorio.ErroAoSalvar = new IOException("Disco cheio.");

        var salvar = () => _vm.SalvarProjetoCommand.ExecuteAsync(null);

        await salvar.Should().NotThrowAsync();
        _vm.Mensagem.Should().Be("Não foi possível salvar o projeto. Disco cheio.");
        _vm.TemAlteracoesNaoSalvas.Should().BeTrue();
        _vm.CaminhoArquivo.Should().BeNull();
    }

    // Abrir

    [Fact]
    public async Task Abrir_CarregaOProjetoDoRepositorioECalcula()
    {
        var projeto = new Cronograma("Obra", Ambiente.Segunda);
        var (x, y) = (new Tarefa("X", Ambiente.Segunda, 3), new Tarefa("Y", Ambiente.Segunda, 2));
        projeto.AdicionarTarefa(x);
        projeto.AdicionarTarefa(y);
        projeto.AdicionarPredecessora(new Predecessora(x.Id, y.Id, TipoDependencia.SS, 1));
        _ambiente.Dialogos.ArquivoParaAbrir = Caminho;
        _ambiente.Repositorio.AoAbrir = _ => projeto;

        await _vm.AbrirProjetoCommand.ExecuteAsync(null);

        _ambiente.Repositorio.Abertos.Should().Equal(Caminho);
        _vm.NomeProjeto.Should().Be("Obra");
        _vm.CaminhoArquivo.Should().Be(Caminho);
        _vm.Tarefas.Select(t => (t.Numero, t.Nome, t.Predecessoras)).Should().Equal((1, "X", ""), (2, "Y", "1II+1"));
        _vm.Tarefas[1].InicioMaisCedo.Should().Be(Ambiente.DiaUtil(1));
        _vm.TemAlteracoesNaoSalvas.Should().BeFalse();
    }

    [Fact]
    public async Task Abrir_CancelandoODialogo_NaoChamaORepositorio()
    {
        await _vm.AbrirProjetoCommand.ExecuteAsync(null);

        _ambiente.Repositorio.Abertos.Should().BeEmpty();
    }

    [Fact]
    public async Task Abrir_ArquivoInvalido_MostraAMensagemEMantemOProjetoAtual()
    {
        _ambiente.Dialogos.ArquivoParaSalvar = Caminho;
        AlterarAlgo();
        await _vm.SalvarProjetoCommand.ExecuteAsync(null);
        _ambiente.Dialogos.ArquivoParaAbrir = "/outro.pplan";
        _ambiente.Repositorio.AoAbrir = _ => throw new InvalidDataException("O arquivo não é um projeto do ProjectPlanner.");

        var abrir = () => _vm.AbrirProjetoCommand.ExecuteAsync(null);

        await abrir.Should().NotThrowAsync();
        _vm.Mensagem.Should().Be("Não foi possível abrir o projeto. O arquivo não é um projeto do ProjectPlanner.");
        _vm.Tarefas.Select(t => t.Nome).Should().Equal("A");
        _vm.CaminhoArquivo.Should().Be(Caminho);
    }

    // Alterações pendentes

    [Fact]
    public async Task Novo_SemAlteracoes_NaoPergunta()
    {
        await _vm.NovoProjetoCommand.ExecuteAsync(null);

        _ambiente.Dialogos.VezesQuePerguntouSobreAlteracoes.Should().Be(0);
    }

    [Fact]
    public async Task Novo_ComAlteracoes_Cancelar_MantemOProjeto()
    {
        AlterarAlgo();
        _ambiente.Dialogos.RespostaAlteracoesPendentes = RespostaAlteracoesPendentes.Cancelar;

        await _vm.NovoProjetoCommand.ExecuteAsync(null);

        _ambiente.Dialogos.VezesQuePerguntouSobreAlteracoes.Should().Be(1);
        _vm.Tarefas.Should().ContainSingle();
    }

    [Fact]
    public async Task Novo_ComAlteracoes_NaoSalvar_DescartaSemSalvar()
    {
        AlterarAlgo();
        _ambiente.Dialogos.RespostaAlteracoesPendentes = RespostaAlteracoesPendentes.NaoSalvar;

        await _vm.NovoProjetoCommand.ExecuteAsync(null);

        _ambiente.Repositorio.Salvos.Should().BeEmpty();
        _vm.Tarefas.Should().BeEmpty();
        _vm.TemAlteracoesNaoSalvas.Should().BeFalse();
    }

    [Fact]
    public async Task Novo_ComAlteracoes_Salvar_SalvaAntesDeCriarONovo()
    {
        AlterarAlgo();
        var anterior = _vm.Cronograma;
        _ambiente.Dialogos.RespostaAlteracoesPendentes = RespostaAlteracoesPendentes.Salvar;
        _ambiente.Dialogos.ArquivoParaSalvar = Caminho;

        await _vm.NovoProjetoCommand.ExecuteAsync(null);

        _ambiente.Repositorio.Salvos.Should().ContainSingle().Which.Cronograma.Should().BeSameAs(anterior);
        _vm.Tarefas.Should().BeEmpty();
        _vm.CaminhoArquivo.Should().BeNull();
    }

    [Fact]
    public async Task Novo_ComAlteracoes_SalvarMasCancelarODialogoDeArquivo_MantemOProjeto()
    {
        AlterarAlgo();
        _ambiente.Dialogos.RespostaAlteracoesPendentes = RespostaAlteracoesPendentes.Salvar;

        await _vm.NovoProjetoCommand.ExecuteAsync(null);

        _vm.Tarefas.Should().ContainSingle();
        _vm.TemAlteracoesNaoSalvas.Should().BeTrue();
    }

    [Fact]
    public async Task Abrir_ComAlteracoes_Cancelar_NaoAbre()
    {
        AlterarAlgo();
        _ambiente.Dialogos.ArquivoParaAbrir = Caminho;

        await _vm.AbrirProjetoCommand.ExecuteAsync(null);

        _ambiente.Repositorio.Abertos.Should().BeEmpty();
        _vm.Tarefas.Should().ContainSingle();
    }
}
