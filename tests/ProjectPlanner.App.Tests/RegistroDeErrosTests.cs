using Avalonia.Threading;
using ProjectPlanner.App.Services;

namespace ProjectPlanner.App.Tests;

// Bloco 25 (R1): o registro local de erros e o tratamento dos erros que ninguém tratou.
public sealed class RegistroDeErrosTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-erros-" + Guid.NewGuid().ToString("N"));
    private readonly RelogioFixo _relogio = new(new DateTimeOffset(2026, 10, 4, 15, 30, 0, TimeSpan.Zero));

    public void Dispose()
    {
        if (Directory.Exists(_pasta))
            Directory.Delete(_pasta, recursive: true);
    }

    private static Exception Lancada(string mensagem)
    {
        try
        {
            throw new InvalidOperationException(mensagem);
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    [Fact]
    public void Registrar_UmArquivoPorMes_ComDataVersaoOrigemEDetalhes()
    {
        var registro = new RegistroDeErros(_pasta, _relogio);

        registro.Registrar(Lancada("primeiro"), "interface").Should().BeTrue();
        registro.Registrar(Lancada("segundo"), "tarefa em segundo plano").Should().BeTrue();

        registro.ArquivoAtual.Should().Be(Path.Combine(_pasta, "erros-2026-10.log"));
        var texto = File.ReadAllText(registro.ArquivoAtual);
        texto.Should().StartWith($"[2026-10-04 15:30:00 +00:00] ProjectPlanner {VersaoDoProduto.Atual} — interface")
            .And.Contain("System.InvalidOperationException: primeiro").And.Contain("at ProjectPlanner.App.Tests.RegistroDeErrosTests.Lancada")
            .And.Contain("— tarefa em segundo plano").And.Contain("segundo");
    }

    [Fact]
    public void Registrar_SemConseguirGravar_NaoLanca()
    {
        File.WriteAllText(_pasta, "um arquivo no lugar da pasta");
        try
        {
            new RegistroDeErros(_pasta, _relogio).Registrar(Lancada("x"), "interface").Should().BeFalse();
        }
        finally
        {
            File.Delete(_pasta);
        }
    }

    [Fact]
    public void Argumentos_Invalidos()
    {
        var semPasta = () => new RegistroDeErros(" ", _relogio);
        var semRelogio = () => new RegistroDeErros(_pasta, null!);
        var semErro = () => new RegistroDeErros(_pasta, _relogio).Registrar(null!, "x");
        var semAviso = () => new TratamentoDeErros(new RegistroDeErros(_pasta, _relogio), null!);

        semPasta.Should().Throw<ArgumentException>();
        semRelogio.Should().Throw<ArgumentNullException>();
        semErro.Should().Throw<ArgumentNullException>();
        semAviso.Should().Throw<ArgumentNullException>();
        RegistroDeErros.PastaPadrao().Should().EndWith(Path.Combine("ProjectPlanner", "logs"));
    }

    [Fact]
    public void ErroNaInterface_VaiParaORegistro_EViraAviso()
    {
        var registro = new RegistroDeErros(_pasta, _relogio);
        var avisos = new List<string>();
        var tratamento = new TratamentoDeErros(registro, avisos.Add);

        tratamento.ErroNaInterface(Lancada("quebrou"));

        avisos.Should().ContainSingle().Which.Should().StartWith("Aconteceu um erro inesperado, e o ProjectPlanner continua aberto.")
            .And.EndWith(registro.ArquivoAtual);
        File.ReadAllText(registro.ArquivoAtual).Should().Contain("quebrou");
    }

    [Fact]
    public void ErroQueSeRepeteDuranteOAviso_NaoEmpilhaAvisos_EAvisoQueFalhaNaoLanca()
    {
        var registro = new RegistroDeErros(_pasta, _relogio);
        var avisos = 0;
        TratamentoDeErros? tratamento = null;
        tratamento = new TratamentoDeErros(registro, _ =>
        {
            avisos++;
            tratamento!.ErroNaInterface(Lancada("de novo")); // o erro volta enquanto o aviso está aberto
            throw new InvalidOperationException("o aviso também falhou");
        });

        var tratar = () => tratamento.ErroNaInterface(Lancada("primeiro"));

        tratar.Should().NotThrow();
        avisos.Should().Be(1);
        File.ReadAllText(registro.ArquivoAtual).Should().Contain("primeiro").And.Contain("de novo").And.Contain("o aviso também falhou");
    }

    [Fact]
    public void EmSegundoPlano_FicaObservado_EFatal_Registrado()
    {
        var registro = new RegistroDeErros(_pasta, _relogio);
        var tratamento = new TratamentoDeErros(registro, _ => { });
        var args = new UnobservedTaskExceptionEventArgs(new AggregateException(Lancada("em segundo plano")));

        tratamento.EmSegundoPlano(null, args);
        tratamento.Fatal(null, new UnhandledExceptionEventArgs("não é uma exceção", isTerminating: true));

        args.Observed.Should().BeTrue();
        File.ReadAllText(registro.ArquivoAtual).Should().Contain("em segundo plano").And.Contain("— fatal").And.Contain("não é uma exceção");
    }

    [Fact]
    public Task NaJanela_UmErroNaFilaDaTela_ETratado() => Tela.Executar(() =>
    {
        var registro = new RegistroDeErros(_pasta, _relogio);
        var avisos = new List<string>();
        var tratamento = new TratamentoDeErros(registro, avisos.Add);
        Dispatcher.UIThread.UnhandledException += tratamento.NaInterface;
        try
        {
            Dispatcher.UIThread.Post(() => throw new InvalidOperationException("erro num clique"));
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            Dispatcher.UIThread.UnhandledException -= tratamento.NaInterface;
        }

        avisos.Should().ContainSingle();
        File.ReadAllText(registro.ArquivoAtual).Should().Contain("erro num clique");
    });

    [Fact]
    public void ComandoAbrirRegistros_AbreAPastaPadrao()
    {
        var ambiente = new Ambiente();

        ambiente.CriarViewModel().AbrirRegistrosDeErrosCommand.Execute(null);

        ambiente.Dialogos.PastasAbertas.Should().Equal(RegistroDeErros.PastaPadrao());
    }
}
