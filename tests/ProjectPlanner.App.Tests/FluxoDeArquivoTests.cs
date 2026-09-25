using Microsoft.Data.Sqlite;
using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

public class FluxoDeArquivoTests
{
    private const string Obra = "/projetos/obra.pplan";

    private readonly Ambiente _ambiente = new();
    private readonly RepositorioEmMemoria _repositorio = new();

    private MainWindowViewModel NovaJanela() =>
        new(_repositorio, _ambiente.Calculadora, _ambiente.Dialogos, _ambiente.Relogio, _ambiente.Preferencias);

    // Tudo o que a tela mostra do projeto: cabeçalho, cada linha da grid (editáveis e calculadas) e o Gantt.
    private static object Retrato(MainWindowViewModel vm) => new
    {
        vm.NomeProjeto,
        vm.InicioProjeto,
        vm.TerminoProjeto,
        Linhas = vm.Tarefas.Select(t => new
        {
            t.Numero, t.Nome, t.Duracao, t.DataInicio, t.DataTermino, t.PercentualConcluido, t.Predecessoras,
            t.InicioMaisCedo, t.TerminoMaisCedo, t.InicioMaisTarde, t.TerminoMaisTarde, t.Folga, t.EhCritica,
        }).ToList(),
        Barras = vm.Gantt.Layout!.Barras.Select(b => new { b.Nome, b.X, b.Largura, b.LarguraConcluida, b.FimDaFolga, b.EhCritica }).ToList(),
        Setas = vm.Gantt.Layout.Setas.Select(s => new { s.Tipo, s.Saida, s.Chegada, s.EhCritica }).ToList(),
    };

    [Fact]
    public async Task NovoEditarSalvarFecharEAbrir_DevolveOsMesmosDados()
    {
        var vm = NovaJanela();
        await vm.NovoProjetoCommand.ExecuteAsync(null);
        vm.NomeProjeto = "Obra da sede";
        Ambiente.AdicionarTarefa(vm, "Fundação", 5);
        Ambiente.AdicionarTarefa(vm, "Estrutura", 8, "1");
        Ambiente.AdicionarTarefa(vm, "Instalações", 4, "2II+2");
        Ambiente.AdicionarTarefa(vm, "Acabamento", 3, "2; 3TT-1");
        Ambiente.AdicionarTarefa(vm, "Vigilância", 2, "4IT");
        vm.Tarefas[0].PercentualConcluido = 100;
        vm.Tarefas[4].DataInicio = new DateTime(2026, 11, 2);
        var antes = Retrato(vm);
        _ambiente.Dialogos.ArquivoParaSalvar = Obra;

        await vm.SalvarProjetoCommand.ExecuteAsync(null);
        // "Fechar": uma janela nova, do zero, abrindo o arquivo pelo mesmo repositório.
        var reaberta = NovaJanela();
        _ambiente.Dialogos.ArquivoParaAbrir = Obra;
        await reaberta.AbrirProjetoCommand.ExecuteAsync(null);

        Retrato(reaberta).Should().BeEquivalentTo(antes, o => o.WithStrictOrdering());
        reaberta.PossuiAlteracoesNaoSalvas.Should().BeFalse();
        reaberta.Titulo.Should().Be("obra — ProjectPlanner");
    }

    [Fact]
    public async Task PossuiAlteracoesNaoSalvas_FicaVerdadeiraAoEditarEFalsaAoSalvar()
    {
        var vm = NovaJanela();
        _ambiente.Dialogos.ArquivoParaSalvar = Obra;
        vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();

        Ambiente.AdicionarTarefa(vm, "A", 2);
        vm.PossuiAlteracoesNaoSalvas.Should().BeTrue();
        vm.Titulo.Should().Be("*Projeto sem título — ProjectPlanner");

        await vm.SalvarProjetoCommand.ExecuteAsync(null);
        vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();
        vm.Titulo.Should().Be("obra — ProjectPlanner");

        vm.Tarefas[0].Duracao = 3;
        vm.PossuiAlteracoesNaoSalvas.Should().BeTrue();
        vm.Titulo.Should().Be("*obra — ProjectPlanner");
    }

    [Fact]
    public async Task RecalcularOuMudarOZoom_NaoSaoAlteracoesDoProjeto()
    {
        var vm = NovaJanela();
        _ambiente.Dialogos.ArquivoParaSalvar = Obra;
        Ambiente.AdicionarTarefa(vm, "A", 2);
        await vm.SalvarProjetoCommand.ExecuteAsync(null);

        vm.RecalcularCronogramaCommand.Execute(null);
        vm.Gantt.PixelsPorDia = 40;

        vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();
    }

    [Fact]
    public async Task SalvarComo_PerguntaDeNovoSugereOArquivoAtualEPassaAUsarONovo()
    {
        var vm = NovaJanela();
        Ambiente.AdicionarTarefa(vm, "A", 2);
        _ambiente.Dialogos.ArquivoParaSalvar = Obra;
        await vm.SalvarProjetoCommand.ExecuteAsync(null);
        _ambiente.Dialogos.ArquivoParaSalvar = "/projetos/obra-v2.pplan";

        await vm.SalvarComoProjetoCommand.ExecuteAsync(null);
        vm.Tarefas[0].Duracao = 4;
        await vm.SalvarProjetoCommand.ExecuteAsync(null);

        _ambiente.Dialogos.VezesQuePerguntouOndeSalvar.Should().Be(2, "o segundo Salvar vai direto para o arquivo novo");
        _ambiente.Dialogos.NomeSugerido.Should().Be("obra");
        vm.CaminhoArquivo.Should().Be("/projetos/obra-v2.pplan");
        vm.Titulo.Should().Be("obra-v2 — ProjectPlanner");
        _repositorio.Arquivos.Should().BeEquivalentTo([Obra, "/projetos/obra-v2.pplan"]);
    }

    [Fact]
    public async Task SalvarComo_Cancelado_MantemOArquivoEAsAlteracoes()
    {
        var vm = NovaJanela();
        Ambiente.AdicionarTarefa(vm, "A", 2);
        _ambiente.Dialogos.ArquivoParaSalvar = Obra;
        await vm.SalvarProjetoCommand.ExecuteAsync(null);
        vm.Tarefas[0].Duracao = 4;
        _ambiente.Dialogos.ArquivoParaSalvar = null;

        await vm.SalvarComoProjetoCommand.ExecuteAsync(null);

        vm.CaminhoArquivo.Should().Be(Obra);
        vm.PossuiAlteracoesNaoSalvas.Should().BeTrue();
    }

    [Fact]
    public async Task AbrirComAlteracoes_Salvar_SalvaOAtualEDepoisAbre()
    {
        _repositorio.Salvar(new Core.Cronograma("Outro", Ambiente.Segunda), "/projetos/outro.pplan");
        var vm = NovaJanela();
        Ambiente.AdicionarTarefa(vm, "A", 2);
        _ambiente.Dialogos.RespostaAlteracoesPendentes = RespostaAlteracoesPendentes.Salvar;
        _ambiente.Dialogos.ArquivoParaSalvar = Obra;
        _ambiente.Dialogos.ArquivoParaAbrir = "/projetos/outro.pplan";

        await vm.AbrirProjetoCommand.ExecuteAsync(null);

        _ambiente.Dialogos.VezesQuePerguntouSobreAlteracoes.Should().Be(1);
        _repositorio.Arquivos.Should().Contain(Obra);
        vm.NomeProjeto.Should().Be("Outro");
    }

    [Fact]
    public async Task AbrirComAlteracoes_NaoSalvar_AbreSemSalvar()
    {
        _repositorio.Salvar(new Core.Cronograma("Outro", Ambiente.Segunda), "/projetos/outro.pplan");
        var vm = NovaJanela();
        Ambiente.AdicionarTarefa(vm, "A", 2);
        _ambiente.Dialogos.RespostaAlteracoesPendentes = RespostaAlteracoesPendentes.NaoSalvar;
        _ambiente.Dialogos.ArquivoParaAbrir = "/projetos/outro.pplan";

        await vm.AbrirProjetoCommand.ExecuteAsync(null);

        _repositorio.Arquivos.Should().Equal("/projetos/outro.pplan");
        vm.NomeProjeto.Should().Be("Outro");
        vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();
    }

    public static TheoryData<Exception, string> ErrosAoSalvar => new()
    {
        { new UnauthorizedAccessException("Access denied"), "Sem permissão para gravar ou ler neste local*" },
        { new IOException("No space left on device", 28), "Não há espaço suficiente no disco." },
        { new SqliteException("SQLite Error 13: 'database or disk is full'", 13), "Não há espaço suficiente no disco." },
        { new Microsoft.EntityFrameworkCore.DbUpdateException("falhou", new SqliteException("readonly", 8)), "Sem permissão*" },
        { new DirectoryNotFoundException("sumiu"), "A pasta do arquivo não existe mais." },
        { new InvalidOperationException("bug"), "Erro inesperado: bug" },
    };

    [Theory]
    [MemberData(nameof(ErrosAoSalvar))]
    public async Task ErroAoSalvar_ViraMensagemAmigavelSemLancarEMantemAsAlteracoes(Exception erro, string mensagem)
    {
        var repositorio = new RepositorioFalso { ErroAoSalvar = erro };
        var vm = new MainWindowViewModel(repositorio, _ambiente.Calculadora, _ambiente.Dialogos, _ambiente.Relogio, _ambiente.Preferencias);
        Ambiente.AdicionarTarefa(vm, "A", 2);
        _ambiente.Dialogos.ArquivoParaSalvar = Obra;

        var salvar = () => vm.SalvarProjetoCommand.ExecuteAsync(null);

        await salvar.Should().NotThrowAsync();
        vm.Mensagem.Should().Match($"Não foi possível salvar o projeto. {mensagem}");
        vm.PossuiAlteracoesNaoSalvas.Should().BeTrue();
        vm.CaminhoArquivo.Should().BeNull();
    }

    [Fact]
    public async Task ArquivoCorrompidoAoAbrir_ViraMensagemEMantemOProjetoAtual()
    {
        var repositorio = new RepositorioFalso { AoAbrir = _ => throw new SqliteException("file is not a database", 26) };
        var vm = new MainWindowViewModel(repositorio, _ambiente.Calculadora, _ambiente.Dialogos, _ambiente.Relogio, _ambiente.Preferencias);
        _ambiente.Dialogos.ArquivoParaAbrir = Obra;

        var abrir = () => vm.AbrirProjetoCommand.ExecuteAsync(null);

        await abrir.Should().NotThrowAsync();
        vm.Mensagem.Should().Be("Não foi possível abrir o projeto. O arquivo está corrompido ou não é um projeto do ProjectPlanner.");
        vm.NomeProjeto.Should().Be("Projeto sem título");
    }
}
