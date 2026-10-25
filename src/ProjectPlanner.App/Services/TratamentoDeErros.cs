using Avalonia.Threading;

namespace ProjectPlanner.App.Services;

/// <summary>
/// O que acontece com um erro que nenhum código tratou (Bloco 25, R1). Na interface: ele vai para o registro, o usuário
/// recebe um aviso e o programa continua aberto, para salvar o trabalho. Em segundo plano: só o registro. Fatal (fora da
/// interface): o registro, antes de o programa fechar. Instalado só pelo Program.Main, nunca nos testes.
/// </summary>
public sealed class TratamentoDeErros
{
    private readonly RegistroDeErros _registro;
    private readonly Action<string> _avisar;
    private bool _avisando;

    public TratamentoDeErros(RegistroDeErros registro, Action<string> avisar)
    {
        ArgumentNullException.ThrowIfNull(registro);
        ArgumentNullException.ThrowIfNull(avisar);
        (_registro, _avisar) = (registro, avisar);
    }

    /// <summary>
    /// Bloco 69: depois do registro local, o envio ao suporte (o <see cref="EnvioDeErros"/>, ligado pelo App quando os serviços
    /// sobem; ele mesmo confere se o usuário ligou a opção). Num erro fatal, espera até <see cref="EsperaNoFatal"/> o envio.
    /// </summary>
    public Func<Exception, string, Task>? Enviar { get; set; }

    public static readonly TimeSpan EsperaNoFatal = TimeSpan.FromSeconds(3);

    /// <summary>O aviso ao usuário, com o arquivo onde ficaram os detalhes.</summary>
    public string Aviso =>
        "Aconteceu um erro inesperado, e o ProjectPlanner continua aberto. Salve o seu trabalho (de preferência com " +
        "Salvar como, num arquivo novo) e, se o erro voltar, feche e abra o programa de novo.\n\n" +
        $"Os detalhes ficaram no registro de erros: {_registro.ArquivoAtual}";

    public void Instalar()
    {
        AppDomain.CurrentDomain.UnhandledException += Fatal;
        TaskScheduler.UnobservedTaskException += EmSegundoPlano;
        Dispatcher.UIThread.UnhandledException += NaInterface;
    }

    internal void NaInterface(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ErroNaInterface(e.Exception);
        e.Handled = true;
    }

    /// <summary>Um erro na interface: vai para o registro e vira um aviso (um de cada vez).</summary>
    internal void ErroNaInterface(Exception erro)
    {
        Registrar(erro, "interface");
        if (_avisando)
            return; // um erro que se repete não empilha avisos
        _avisando = true;
        try
        {
            _avisar(Aviso);
        }
        catch (Exception ex)
        {
            _registro.Registrar(ex, "aviso de erro");
        }
        finally
        {
            _avisando = false;
        }
    }

    internal void EmSegundoPlano(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Registrar(e.Exception, "tarefa em segundo plano");
        e.SetObserved();
    }

    internal void Fatal(object? sender, UnhandledExceptionEventArgs e) =>
        Registrar(e.ExceptionObject as Exception ?? new InvalidOperationException($"Erro sem detalhes: {e.ExceptionObject}"), "fatal");

    // O registro local e, se ligado, o envio ao suporte (que nunca derruba nada).
    private void Registrar(Exception erro, string origem)
    {
        _registro.Registrar(erro, origem);
        try
        {
            var envio = Enviar?.Invoke(erro, origem);
            if (origem == "fatal")
                envio?.Wait(EsperaNoFatal);
        }
        catch (Exception)
        {
        }
    }
}
