using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core;

namespace ProjectPlanner.App.Tests;

// Abrir um projeto pedido pelo sistema (Bloco 13): duplo clique ou "Abrir com" num .pplan, argumentos da
// linha de comando, ou arquivos entregues pelo Finder no macOS.
public class ArquivosPedidosTests
{
    private static readonly string Pasta = Path.Combine(Path.GetTempPath(), "projectplanner-testes");

    [Fact]
    public void SemArgumentos_NenhumProjeto()
    {
        ArquivosPedidos.Interpretar([]).Should().Be(new ProjetoPedido(null, 0));
    }

    [Fact]
    public void UmProjeto_DevolveOCaminhoCompleto()
    {
        var caminho = Path.Combine(Pasta, "obra.pplan");

        ArquivosPedidos.Interpretar([caminho]).Should().Be(new ProjetoPedido(caminho, 0));
    }

    [Fact]
    public void CaminhoRelativo_ViraCompletoAPartirDaPastaAtual()
    {
        ArquivosPedidos.Interpretar(["obra.pplan"]).Caminho
            .Should().Be(Path.Combine(Directory.GetCurrentDirectory(), "obra.pplan"));
    }

    [Theory]
    [InlineData("OBRA.PPLAN")]
    [InlineData("  obra.pplan  ")]
    public void ExtensaoEmMaiusculasOuComEspacos_Aceita(string argumento)
    {
        ArquivosPedidos.Interpretar([argumento]).Caminho.Should().NotBeNull();
    }

    [Fact]
    public void UriDeArquivo_ViraCaminhoLocal()
    {
        var caminho = Path.Combine(Pasta, "obra com espaço.pplan");

        ArquivosPedidos.Interpretar([new Uri(caminho).AbsoluteUri]).Caminho.Should().Be(caminho);
    }

    [Theory]
    [InlineData("planilha.xlsx")]
    [InlineData("projeto.mpp")]
    [InlineData("obra.pplan.bak")]
    [InlineData("-psn_0_12345")]
    [InlineData("--verbose")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void OutrosArgumentos_IgnoradosEmSilencio(string? argumento)
    {
        ArquivosPedidos.Interpretar([argumento]).Should().Be(new ProjetoPedido(null, 0));
    }

    [Fact]
    public void VariosProjetos_ValeOPrimeiroEOsOutrosSaoContados()
    {
        var (a, b, c) = (Path.Combine(Pasta, "a.pplan"), Path.Combine(Pasta, "b.pplan"), Path.Combine(Pasta, "c.pplan"));

        ArquivosPedidos.Interpretar(["-psn_0_1", a, "notas.txt", b, c]).Should().Be(new ProjetoPedido(a, 2));
    }

    [Fact]
    public void OMesmoProjetoRepetido_ContaUmaVez()
    {
        var caminho = Path.Combine(Pasta, "obra.pplan");

        ArquivosPedidos.Interpretar([caminho, caminho]).Should().Be(new ProjetoPedido(caminho, 0));
    }

    [Fact]
    public void ArquivoQueNaoExiste_AindaEDevolvido_QuemAbreAvisa()
    {
        var caminho = Path.Combine(Pasta, Guid.NewGuid().ToString("N"), "sumiu.pplan");

        ArquivosPedidos.Interpretar([caminho]).Caminho.Should().Be(caminho);
    }
}

public class AberturaPeloSistemaTests
{
    private readonly Ambiente _ambiente = new();

    private static Cronograma Obra()
    {
        var projeto = new Cronograma("Obra da sede", Ambiente.Segunda);
        var fundacao = new Tarefa("Fundação", Ambiente.Segunda, 3);
        var estrutura = new Tarefa("Estrutura", Ambiente.Segunda, 2);
        projeto.AdicionarTarefa(fundacao);
        projeto.AdicionarTarefa(estrutura);
        projeto.AdicionarPredecessora(new Predecessora(fundacao.Id, estrutura.Id));
        return projeto;
    }

    [Fact]
    public async Task SemArgumentos_FicaOProjetoNovoSemMensagem()
    {
        var vm = _ambiente.CriarViewModel();

        await vm.AbrirPedidoDoSistemaAsync([]);

        _ambiente.Repositorio.Abertos.Should().BeEmpty();
        (vm.NomeProjeto, vm.CaminhoArquivo, vm.Mensagem).Should().Be(("Projeto sem título", (string?)null, ""));
    }

    [Fact]
    public async Task UmProjetoValido_AbreComoOAbrirECalculaOCronograma()
    {
        _ambiente.Repositorio.AoAbrir = _ => Obra();
        var vm = _ambiente.CriarViewModel();
        var chamadas = _ambiente.Calculadora.Chamadas;
        var caminho = Path.Combine(Path.GetTempPath(), "obra.pplan");

        await vm.AbrirPedidoDoSistemaAsync([caminho]);

        _ambiente.Repositorio.Abertos.Should().Equal(caminho);
        vm.Tarefas.Select(t => t.Nome).Should().Equal("Fundação", "Estrutura");
        (vm.CaminhoArquivo, vm.Titulo).Should().Be((caminho, "obra — ProjectPlanner"));
        _ambiente.Calculadora.Chamadas.Should().BeGreaterThan(chamadas);
        vm.TerminoProjeto.Should().Be(new DateTime(2026, 10, 2));
        vm.Mensagem.Should().Be($"Projeto aberto: {caminho}");
        vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();
    }

    [Fact]
    public async Task ArquivoQueNaoExiste_AvisaSemTravarEMantemOProjetoNovo()
    {
        var vm = _ambiente.CriarViewModel(); // o repositório falso lança FileNotFoundException, como o real

        await vm.AbrirPedidoDoSistemaAsync(["/nao/existe/obra.pplan"]);

        vm.Mensagem.Should().Be("Não foi possível abrir o projeto. O arquivo não foi encontrado. Ele pode ter sido movido ou apagado.");
        (vm.NomeProjeto, vm.CaminhoArquivo).Should().Be(("Projeto sem título", (string?)null));
    }

    [Fact]
    public async Task ArquivoCorrompido_AvisaComAMensagemDoAbrir()
    {
        _ambiente.Repositorio.AoAbrir = caminho =>
            throw new InvalidDataException($"O arquivo \"{caminho}\" não é um projeto do ProjectPlanner.");
        var vm = _ambiente.CriarViewModel();

        await vm.AbrirPedidoDoSistemaAsync(["/tmp/estragado.pplan"]);

        vm.Mensagem.Should().Be("Não foi possível abrir o projeto. O arquivo \"/tmp/estragado.pplan\" não é um projeto do ProjectPlanner.");
        vm.Tarefas.Should().BeEmpty();
    }

    [Fact]
    public async Task OutraExtensao_NaoAbreNadaNemAvisa()
    {
        var vm = _ambiente.CriarViewModel();

        await vm.AbrirPedidoDoSistemaAsync(["/tmp/planilha.xlsx"]);

        _ambiente.Repositorio.Abertos.Should().BeEmpty();
        vm.Mensagem.Should().BeEmpty();
    }

    [Theory]
    [InlineData(2, "1 outro arquivo não foi aberto.")]
    [InlineData(3, "2 outros arquivos não foram abertos.")]
    public async Task VariosProjetos_AbreOPrimeiroEAvisaDosOutros(int quantos, string aviso)
    {
        _ambiente.Repositorio.AoAbrir = _ => Obra();
        var vm = _ambiente.CriarViewModel();
        var caminhos = Enumerable.Range(1, quantos).Select(i => Path.Combine(Path.GetTempPath(), $"obra{i}.pplan")).ToList();

        await vm.AbrirPedidoDoSistemaAsync(caminhos);

        _ambiente.Repositorio.Abertos.Should().Equal(caminhos[0]);
        vm.Mensagem.Should().Be($"O ProjectPlanner abre um projeto por janela: {aviso} Projeto aberto: {caminhos[0]}");
    }

    [Fact]
    public async Task VariosProjetosEOPrimeiroFalha_AvisaDosDois()
    {
        var vm = _ambiente.CriarViewModel();

        await vm.AbrirPedidoDoSistemaAsync(["/nao/existe/a.pplan", "/tmp/b.pplan"]);

        vm.Mensagem.Should().Be("O ProjectPlanner abre um projeto por janela: 1 outro arquivo não foi aberto. " +
                                "Não foi possível abrir o projeto. O arquivo não foi encontrado. Ele pode ter sido movido ou apagado.");
    }

    [Fact]
    public async Task ComAlteracoesPendentes_PerguntaAntes_ECancelarMantemOProjetoAtual()
    {
        _ambiente.Repositorio.AoAbrir = _ => Obra();
        var vm = _ambiente.CriarViewModel();
        Ambiente.AdicionarTarefa(vm, "Rascunho", 1);
        _ambiente.Dialogos.RespostaAlteracoesPendentes = RespostaAlteracoesPendentes.Cancelar;

        await vm.AbrirPedidoDoSistemaAsync(["/tmp/obra.pplan"]);

        _ambiente.Dialogos.VezesQuePerguntouSobreAlteracoes.Should().Be(1);
        _ambiente.Repositorio.Abertos.Should().BeEmpty();
        vm.Tarefas.Select(t => t.Nome).Should().Equal("Rascunho");
    }

    [Fact]
    public async Task ComAlteracoesPendentes_NaoSalvarAbreOPedido()
    {
        _ambiente.Repositorio.AoAbrir = _ => Obra();
        var vm = _ambiente.CriarViewModel();
        Ambiente.AdicionarTarefa(vm, "Rascunho", 1);
        _ambiente.Dialogos.RespostaAlteracoesPendentes = RespostaAlteracoesPendentes.NaoSalvar;

        await vm.AbrirPedidoDoSistemaAsync(["/tmp/obra.pplan"]);

        vm.Tarefas.Select(t => t.Nome).Should().Equal("Fundação", "Estrutura");
    }
}
