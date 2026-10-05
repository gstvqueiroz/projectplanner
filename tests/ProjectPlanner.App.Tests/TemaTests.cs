using System.Text.Json;
using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

// Bloco 25 (V3): Exibir > Tema, guardado no interface.json junto com o painel do assistente.
public class TemaTests
{
    private readonly Ambiente _ambiente = new();

    [Fact]
    public void SemPreferencia_IgualAoSistema_ENaoGravaNada()
    {
        var vm = _ambiente.CriarViewModel();

        (vm.Tema, vm.EhTemaDoSistema, vm.EhTemaClaro, vm.EhTemaEscuro).Should().Be((TemaDaInterface.Sistema, true, false, false));
        _ambiente.PreferenciasInterface.Gravacoes.Should().Be(0, "abrir não grava nada");
    }

    [Fact]
    public void AbreComOTemaDaSessaoAnterior()
    {
        _ambiente.PreferenciasInterface.Salvo = new PreferenciasInterface(MostrarAssistente: true, TemaDaInterface.Escuro);

        var vm = _ambiente.CriarViewModel();

        (vm.Tema, vm.EhTemaEscuro, vm.EhTemaDoSistema, vm.Assistente.Visivel).Should().Be((TemaDaInterface.Escuro, true, false, true));
    }

    [Theory]
    [InlineData(TemaDaInterface.Claro)]
    [InlineData(TemaDaInterface.Escuro)]
    [InlineData(TemaDaInterface.Sistema)]
    public void Definir_TrocaEGuarda_SemMexerNoPainelDoAssistente(TemaDaInterface tema)
    {
        _ambiente.PreferenciasInterface.Salvo = new PreferenciasInterface(MostrarAssistente: true);
        var vm = _ambiente.CriarViewModel();

        vm.DefinirTemaCommand.Execute(tema);

        vm.Tema.Should().Be(tema);
        (vm.EhTemaDoSistema, vm.EhTemaClaro, vm.EhTemaEscuro)
            .Should().Be((tema == TemaDaInterface.Sistema, tema == TemaDaInterface.Claro, tema == TemaDaInterface.Escuro));
        _ambiente.PreferenciasInterface.Salvo.Should().Be(new PreferenciasInterface(true, tema));
    }

    [Fact]
    public void Definir_AvisaAsMarcasDoMenu()
    {
        var vm = _ambiente.CriarViewModel();
        var avisos = new List<string?>();
        vm.PropertyChanged += (_, e) => avisos.Add(e.PropertyName);

        vm.DefinirTemaCommand.Execute(TemaDaInterface.Escuro);

        avisos.Should().Contain([nameof(vm.Tema), nameof(vm.EhTemaDoSistema), nameof(vm.EhTemaClaro), nameof(vm.EhTemaEscuro)]);
    }

    [Fact]
    public void MostrarOuOcultarOAssistente_NaoApagaOTemaEscolhido()
    {
        _ambiente.PreferenciasInterface.Salvo = new PreferenciasInterface(MostrarAssistente: false, TemaDaInterface.Escuro);
        var vm = _ambiente.CriarViewModel();

        vm.Assistente.AlternarVisibilidadeCommand.Execute(null);
        _ambiente.PreferenciasInterface.Salvo.Should().Be(new PreferenciasInterface(true, TemaDaInterface.Escuro));

        vm.Assistente.FecharCommand.Execute(null);
        _ambiente.PreferenciasInterface.Salvo.Should().Be(new PreferenciasInterface(false, TemaDaInterface.Escuro));
    }

    [Fact]
    public void TemaDesconhecido_Recusado_SemMudarNemGravar()
    {
        var vm = _ambiente.CriarViewModel();

        var definir = () => vm.DefinirTemaCommand.Execute((TemaDaInterface)42);

        definir.Should().Throw<ArgumentOutOfRangeException>();
        (vm.Tema, _ambiente.PreferenciasInterface.Gravacoes).Should().Be((TemaDaInterface.Sistema, 0));
    }

    [Fact]
    public void TrocarOTema_NaoMarcaOProjetoComoAlterado()
    {
        var vm = _ambiente.CriarViewModel();
        Ambiente.AdicionarTarefa(vm, "Fundação", 3);
        _ambiente.Dialogos.ArquivoParaSalvar = "/obra.pplan";
        vm.SalvarProjetoCommand.Execute(null);

        vm.DefinirTemaCommand.Execute(TemaDaInterface.Escuro);

        vm.PossuiAlteracoesNaoSalvas.Should().BeFalse("o tema é do programa, não do .pplan");
    }
}

// O tema no interface.json de verdade, numa pasta temporária.
public sealed class ArquivoPreferenciasInterfaceTemaTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-tema-" + Guid.NewGuid().ToString("N"));
    private string Caminho => Path.Combine(_pasta, "interface.json");

    public void Dispose()
    {
        if (Directory.Exists(_pasta))
            Directory.Delete(_pasta, recursive: true);
    }

    private void Escrever(string conteudo)
    {
        Directory.CreateDirectory(_pasta);
        File.WriteAllText(Caminho, conteudo);
    }

    [Theory]
    [InlineData(TemaDaInterface.Sistema, "sistema")]
    [InlineData(TemaDaInterface.Claro, "claro")]
    [InlineData(TemaDaInterface.Escuro, "escuro")]
    public void SalvarECarregar_ComOTema(TemaDaInterface tema, string noArquivo)
    {
        new ArquivoPreferenciasInterface(Caminho).Salvar(new PreferenciasInterface(true, tema));

        new ArquivoPreferenciasInterface(Caminho).Carregar().Should().Be(new PreferenciasInterface(true, tema));
        File.ReadAllText(Caminho).Should().Contain("\"versao\": 1").And.Contain("\"mostrarAssistente\": true")
            .And.Contain($"\"tema\": \"{noArquivo}\"");
    }

    [Fact]
    public void ArquivoDasVersoesAnteriores_SemTema_ValeComoSistema()
    {
        Escrever("{\n  \"versao\": 1,\n  \"mostrarAssistente\": true\n}");

        new ArquivoPreferenciasInterface(Caminho).Carregar().Should().Be(new PreferenciasInterface(true, TemaDaInterface.Sistema));
    }

    [Theory]
    [InlineData("\"roxo\"", TemaDaInterface.Sistema)]
    [InlineData("\"\"", TemaDaInterface.Sistema)]
    [InlineData("null", TemaDaInterface.Sistema)]
    [InlineData("\"ESCURO\"", TemaDaInterface.Escuro)]
    [InlineData("\" claro \"", TemaDaInterface.Claro)]
    public void TemaDesconhecidoOuVazio_ValeComoSistema_SemPerderOPainel(string tema, TemaDaInterface esperado)
    {
        Escrever($"{{\"versao\": 1, \"mostrarAssistente\": true, \"tema\": {tema}}}");

        new ArquivoPreferenciasInterface(Caminho).Carregar().Should().Be(new PreferenciasInterface(true, esperado));
    }

    // As versões anteriores (até a 0.24) leem o arquivo novo como antes: o campo a mais é ignorado.
    [Fact]
    public void ArquivoNovo_LidoPeloFormatoDasVersoesAnteriores()
    {
        new ArquivoPreferenciasInterface(Caminho).Salvar(new PreferenciasInterface(true, TemaDaInterface.Escuro));

        var lido = JsonSerializer.Deserialize<FormatoAte024>(File.ReadAllText(Caminho),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        lido.Should().Be(new FormatoAte024(1, true));
    }

    private sealed record FormatoAte024(int Versao, bool MostrarAssistente);
}
