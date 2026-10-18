using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace ProjectPlanner.Servidor.Servicos;

/// <summary>Manda os e-mails do servidor (código de confirmação, recuperação de senha, aviso de conta existente).</summary>
public interface IEnviadorDeEmail
{
    Task EnviarAsync(string para, string assunto, string texto, CancellationToken cancelamento);
}

/// <summary>Desenvolvimento: o e-mail vai só para o log (com o código, para testar à mão).</summary>
public sealed class EmailNoLog(ILogger<EmailNoLog> log) : IEnviadorDeEmail
{
    public Task EnviarAsync(string para, string assunto, string texto, CancellationToken cancelamento)
    {
        log.LogInformation("E-mail (só no log, sem SMTP configurado) para {Para}: {Assunto}\n{Texto}", para, assunto, texto);
        return Task.CompletedTask;
    }
}

/// <summary>Produção: SMTP (o do provedor de e-mail escolhido), com TLS.</summary>
public sealed class EmailPorSmtp(IOptions<OpcoesDoServidor> opcoes) : IEnviadorDeEmail
{
    public async Task EnviarAsync(string para, string assunto, string texto, CancellationToken cancelamento)
    {
        var email = opcoes.Value.Email;
        using var cliente = new SmtpClient(email.Smtp, email.Porta) { EnableSsl = true, Credentials = new NetworkCredential(email.Usuario, email.Senha) };
        using var mensagem = new MailMessage(MailAddress.TryCreate(email.Remetente, out var de) ? de : new MailAddress("nao-responda@projectplanner.app"),
            new MailAddress(para)) { Subject = assunto, Body = texto };
        await cliente.SendMailAsync(mensagem, cancelamento);
    }
}

/// <summary>Os textos dos e-mails.</summary>
public static class TextosDeEmail
{
    public static (string Assunto, string Texto) Confirmacao(string nome, string codigo) =>
        ("Seu código do ProjectPlanner", $"Olá, {nome}!\n\nSeu código de confirmação é {codigo}. Ele vale por 30 minutos.\n\n" +
                                          "Se você não criou uma conta no ProjectPlanner, ignore este e-mail.");

    public static (string Assunto, string Texto) Senha(string nome, string codigo) =>
        ("Trocar a senha do ProjectPlanner", $"Olá, {nome}!\n\nPara trocar a senha, use o código {codigo}. Ele vale por 30 minutos.\n\n" +
                                              "Se não foi você quem pediu, ignore este e-mail: a senha continua a mesma.");

    public static (string Assunto, string Texto) ContaExistente(string nome, bool google) =>
        ("Você já tem uma conta no ProjectPlanner", $"Olá, {nome}!\n\nAlguém tentou criar uma conta com este e-mail, mas ele já tem uma. " +
            (google ? "Ela usa o Google: entre com \"Entrar com Google\"." : "Entre com a sua senha ou use \"Esqueci a senha\".") +
            "\n\nSe não foi você, ignore este e-mail.");
}
