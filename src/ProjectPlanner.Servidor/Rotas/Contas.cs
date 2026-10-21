using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectPlanner.Servidor.Dados;
using ProjectPlanner.Servidor.Servicos;

namespace ProjectPlanner.Servidor.Rotas;

public sealed record PedidoDeCadastro(string? Email, string? Senha, string? Nome);
public sealed record PedidoDeConfirmacao(string? Email, string? Codigo, string? Senha);
public sealed record PedidoDeEmail(string? Email);
public sealed record PedidoDeLogin(string? Email, string? Senha);
public sealed record PedidoDoGoogle(string? IdToken);
public sealed record PedidoDoCodigoDoGoogle(string? Codigo, string? Verificador, string? Redirecionamento);
public sealed record PedidoDeRenovacao(string? Renovacao);
public sealed record PedidoDeNovaSenha(string? Email, string? Codigo, string? NovaSenha);
public sealed record Resposta(string Mensagem);
public sealed record Erro([property: System.Text.Json.Serialization.JsonPropertyName("erro")] string Mensagem);

/// <summary>
/// Bloco 44: cadastro (com código por e-mail), login com senha ou Google, renovação e saída, e troca de senha. As respostas
/// não dizem se um e-mail tem conta (cadastro e "esqueci a senha" respondem igual nos dois casos), as senhas ficam só como
/// hash (PBKDF2) e várias senhas erradas seguidas bloqueiam a conta por um tempo.
/// </summary>
public static class Contas
{
    public const int TamanhoMinimoDaSenha = 10;
    public const int TamanhoMaximoDaSenha = 200;
    public const int MinutosDoCodigo = 30;
    public const int TentativasDoCodigo = 5;

    /// <summary>Códigos de um tipo por conta, por hora: além disso não se manda mais e-mail (nem se dá mais chance de adivinhar).</summary>
    public const int CodigosPorHora = 5;

    public const string MensagemDoCadastro = "Se o e-mail puder ser usado, mandamos um código para ele. Digite o código para confirmar a conta.";
    public const string MensagemDaSenha = "Se houver uma conta com esse e-mail, mandamos um código para trocar a senha.";
    public const string LoginInvalido = "E-mail ou senha incorretos.";
    public const string CodigoInvalido = "Código inválido ou vencido. Peça outro.";

    private static readonly PasswordHasher<Usuario> Hasher = new();

    public static IResult Falha(int status, string mensagem) => Results.Json(new Erro(mensagem), statusCode: status);

    public static void Mapear(WebApplication app)
    {
        var contas = app.MapGroup("/v1").RequireRateLimiting(LimitesDeUso.Contas);
        contas.MapPost("/contas", Cadastrar);
        contas.MapPost("/contas/confirmar", Confirmar);
        contas.MapPost("/contas/reenviar", Reenviar);
        contas.MapPost("/sessoes", Entrar);
        contas.MapPost("/sessoes/google", EntrarComGoogle);
        contas.MapPost("/sessoes/google/codigo", EntrarComCodigoDoGoogle);
        contas.MapGet("/google", ClienteDoGoogle);
        contas.MapPost("/sessoes/renovar", Renovar);
        contas.MapPost("/sessoes/sair", Sair);
        contas.MapPost("/senha/esqueci", EsqueciASenha);
        contas.MapPost("/senha/redefinir", RedefinirSenha);
        app.MapGet("/v1/eu", Eu).RequireAuthorization();
    }

    private static string? ProblemaNoEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) || email.Trim().Length > 254 || !MailAddress.TryCreate(email.Trim(), out var m) || !m.Host.Contains('.')
            ? "Informe um e-mail válido." : null;

    private static string? ProblemaNaSenha(string? senha) => senha switch
    {
        null or { Length: < TamanhoMinimoDaSenha } => $"A senha precisa ter pelo menos {TamanhoMinimoDaSenha} caracteres.",
        { Length: > TamanhoMaximoDaSenha } => $"A senha pode ter até {TamanhoMaximoDaSenha} caracteres.",
        _ => null,
    };

    private static async Task<Usuario?> PorEmail(BancoDoServidor banco, string? email)
    {
        var normalizado = Segredos.NormalizarEmail(email);
        return await banco.Usuarios.FirstOrDefaultAsync(u => u.EmailNormalizado == normalizado);
    }

    // Um código novo (os antigos do mesmo tipo deixam de valer) e o e-mail com ele; o de confirmação leva a senha e o nome do
    // cadastro. Passado o limite por hora, nada acontece (a resposta é a mesma).
    private static async Task MandarCodigo(BancoDoServidor banco, IEnviadorDeEmail email, TimeProvider relogio, Usuario usuario, string tipo,
        string? senhaHash, string? nome, CancellationToken c)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var umaHora = agora.AddHours(-1);
        if (await banco.CodigosDeEmail.CountAsync(x => x.UsuarioId == usuario.Id && x.Tipo == tipo && x.CriadoEm > umaHora, c) >= CodigosPorHora)
        {
            await banco.SaveChangesAsync(c);
            return;
        }
        foreach (var antigo in banco.CodigosDeEmail.Where(x => x.UsuarioId == usuario.Id && x.Tipo == tipo && x.UsadoEm == null))
            antigo.UsadoEm = agora;
        var codigo = Segredos.Codigo();
        banco.CodigosDeEmail.Add(new CodigoDeEmail
        {
            Id = Guid.NewGuid(), UsuarioId = usuario.Id, Tipo = tipo, Hash = Segredos.Hash(codigo), CriadoEm = agora,
            ExpiraEm = agora.AddMinutes(MinutosDoCodigo), SenhaHash = senhaHash, Nome = nome,
        });
        await banco.SaveChangesAsync(c);
        var (assunto, texto) = tipo == CodigoDeEmail.Senha ? TextosDeEmail.Senha(usuario.Nome, codigo) : TextosDeEmail.Confirmacao(nome ?? usuario.Nome, codigo);
        await email.EnviarAsync(usuario.Email, assunto, texto, c);
    }

    private static Task<CodigoDeEmail?> CodigoPendente(BancoDoServidor banco, Usuario usuario, string tipo) =>
        banco.CodigosDeEmail.Where(x => x.UsuarioId == usuario.Id && x.Tipo == tipo && x.UsadoEm == null).OrderByDescending(x => x.CriadoEm)
            .FirstOrDefaultAsync();

    // Confere o código (prazo, uso único, tentativas e, na confirmação, a senha do cadastro) e o marca como usado.
    private static async Task<CodigoDeEmail?> UsarCodigo(BancoDoServidor banco, TimeProvider relogio, Usuario usuario, string tipo, string? codigo,
        string? senha = null)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var registro = await CodigoPendente(banco, usuario, tipo);
        if (registro is null || registro.ExpiraEm < agora || registro.Tentativas >= TentativasDoCodigo)
            return null;
        var certo = Segredos.Iguais(registro.Hash, Segredos.Hash((codigo ?? "").Trim()))
                    && (registro.SenhaHash is null || Hasher.VerifyHashedPassword(usuario, registro.SenhaHash, senha ?? "") != PasswordVerificationResult.Failed);
        if (!certo)
        {
            registro.Tentativas++;
            await banco.SaveChangesAsync();
            return null;
        }
        registro.UsadoEm = agora;
        return registro;
    }

    private static async Task<IResult> Cadastrar(PedidoDeCadastro pedido, BancoDoServidor banco, IEnviadorDeEmail email, TimeProvider relogio, CancellationToken c)
    {
        if (ProblemaNoEmail(pedido.Email) is { } problemaNoEmail)
            return Falha(400, problemaNoEmail);
        if (ProblemaNaSenha(pedido.Senha) is { } problemaNaSenha)
            return Falha(400, problemaNaSenha);
        var nome = (pedido.Nome ?? "").Trim();
        if (nome.Length is 0 or > 120)
            return Falha(400, "Informe o nome (até 120 caracteres).");
        var existente = await PorEmail(banco, pedido.Email);
        if (existente is { EmailConfirmado: true })
        {
            // Não revela que a conta existe: avisa o dono, por e-mail.
            var (assunto, texto) = TextosDeEmail.ContaExistente(existente.Nome, existente.SenhaHash is null && existente.GoogleId is not null);
            await email.EnviarAsync(existente.Email, assunto, texto, c);
            return Results.Accepted(value: new Resposta(MensagemDoCadastro));
        }
        var usuario = existente;
        if (usuario is null)
        {
            usuario = new Usuario
            {
                Id = Guid.NewGuid(), Email = pedido.Email!.Trim(), EmailNormalizado = Segredos.NormalizarEmail(pedido.Email), Nome = nome,
                CriadoEm = relogio.GetUtcNow().UtcDateTime,
            };
            banco.Usuarios.Add(usuario);
        }
        // A senha e o nome só valem quando o dono do e-mail confirmar o código deste cadastro (com esta senha).
        await MandarCodigo(banco, email, relogio, usuario, CodigoDeEmail.Confirmacao, Hasher.HashPassword(usuario, pedido.Senha!), nome, c);
        return Results.Accepted(value: new Resposta(MensagemDoCadastro));
    }

    private static async Task<IResult> Confirmar(PedidoDeConfirmacao pedido, BancoDoServidor banco, EmissorDeTokens tokens, TimeProvider relogio)
    {
        if (await PorEmail(banco, pedido.Email) is not { EmailConfirmado: false } usuario
            || await UsarCodigo(banco, relogio, usuario, CodigoDeEmail.Confirmacao, pedido.Codigo, pedido.Senha) is not { } codigo)
            return Falha(400, CodigoInvalido);
        (usuario.SenhaHash, usuario.Nome, usuario.EmailConfirmado) = (codigo.SenhaHash, codigo.Nome ?? usuario.Nome, true);
        var sessao = tokens.Emitir(banco, usuario);
        await banco.SaveChangesAsync();
        return Results.Ok(sessao);
    }

    private static async Task<IResult> Reenviar(PedidoDeEmail pedido, BancoDoServidor banco, IEnviadorDeEmail email, TimeProvider relogio, CancellationToken c)
    {
        // O código novo leva a senha e o nome do último cadastro.
        if (await PorEmail(banco, pedido.Email) is { EmailConfirmado: false } usuario
            && await banco.CodigosDeEmail.Where(x => x.UsuarioId == usuario.Id && x.Tipo == CodigoDeEmail.Confirmacao).OrderByDescending(x => x.CriadoEm)
                .FirstOrDefaultAsync(c) is { } ultimo)
            await MandarCodigo(banco, email, relogio, usuario, CodigoDeEmail.Confirmacao, ultimo.SenhaHash, ultimo.Nome, c);
        return Results.Accepted(value: new Resposta(MensagemDoCadastro));
    }

    private static async Task<IResult> Entrar(PedidoDeLogin pedido, BancoDoServidor banco, EmissorDeTokens tokens, TimeProvider relogio,
        IOptions<OpcoesDoServidor> opcoes)
    {
        var limites = opcoes.Value.Limites;
        var agora = relogio.GetUtcNow().UtcDateTime;
        var usuario = await PorEmail(banco, pedido.Email);
        if (usuario?.BloqueadoAte is { } ate && ate > agora)
            return Falha(429, $"Muitas tentativas erradas. Tente de novo em {Math.Max(1, (int)Math.Ceiling((ate - agora).TotalMinutes))} minuto(s) ou troque a senha.");
        if (usuario is { EmailConfirmado: false })
        {
            // Ainda sem senha: se for a do cadastro pendente, lembra de confirmar; senão, a mensagem de sempre.
            var pendente = await CodigoPendente(banco, usuario, CodigoDeEmail.Confirmacao);
            return pendente?.SenhaHash is { } h && Hasher.VerifyHashedPassword(usuario, h, pedido.Senha ?? "") != PasswordVerificationResult.Failed
                ? Falha(403, "Confirme o e-mail antes de entrar: digite o código que mandamos (ou peça outro).")
                : Falha(401, LoginInvalido);
        }
        if (usuario?.SenhaHash is null || pedido.Senha is null)
        {
            Hasher.VerifyHashedPassword(new Usuario(), HashFalso, pedido.Senha ?? ""); // o mesmo tempo com ou sem conta
            return Falha(401, LoginInvalido);
        }
        var resultado = Hasher.VerifyHashedPassword(usuario, usuario.SenhaHash, pedido.Senha);
        if (resultado == PasswordVerificationResult.Failed)
        {
            usuario.TentativasFalhas++;
            if (usuario.TentativasFalhas >= limites.TentativasAteBloquear)
                (usuario.BloqueadoAte, usuario.TentativasFalhas) = (agora.AddMinutes(limites.MinutosDeBloqueio), 0);
            await banco.SaveChangesAsync();
            return Falha(401, LoginInvalido);
        }
        if (resultado == PasswordVerificationResult.SuccessRehashNeeded)
            usuario.SenhaHash = Hasher.HashPassword(usuario, pedido.Senha);
        (usuario.TentativasFalhas, usuario.BloqueadoAte) = (0, null);
        var sessao = tokens.Emitir(banco, usuario);
        await banco.SaveChangesAsync();
        return Results.Ok(sessao);
    }

    private static readonly string HashFalso = Hasher.HashPassword(new Usuario(), "senha-que-ninguem-usa-" + Guid.NewGuid());

    public const string GoogleRecusado = "Não foi possível confirmar a conta Google. Tente de novo.";

    private static async Task<IResult> EntrarComGoogle(PedidoDoGoogle pedido, BancoDoServidor banco, EmissorDeTokens tokens, IValidadorDoGoogle google,
        TimeProvider relogio)
    {
        if (string.IsNullOrWhiteSpace(pedido.IdToken) || await google.ValidarAsync(pedido.IdToken) is not { } conta)
            return Falha(401, GoogleRecusado);
        return await SessaoDoGoogle(conta, banco, tokens, relogio);
    }

    /// <summary>Bloco 45: o client ID do app para o login no navegador (404 se o Google não está configurado no servidor).</summary>
    private static IResult ClienteDoGoogle(IOptions<OpcoesDoServidor> opcoes) => opcoes.Value.Google.LoginDoAppLigado
        ? Results.Ok(new { clientId = opcoes.Value.Google.ClientIdDoApp })
        : Falha(404, "O login com Google ainda não está disponível. Use e-mail e senha.");

    /// <summary>
    /// Bloco 45: o login com Google do programa. O navegador volta ao app com um código (PKCE, redirecionamento para o
    /// próprio computador); o app manda o código e o verificador, e o servidor, que tem o segredo, troca pelo idToken.
    /// </summary>
    private static async Task<IResult> EntrarComCodigoDoGoogle(PedidoDoCodigoDoGoogle pedido, BancoDoServidor banco, EmissorDeTokens tokens,
        IValidadorDoGoogle google, ITrocaDoCodigoDoGoogle troca, TimeProvider relogio, IOptions<OpcoesDoServidor> opcoes, CancellationToken c)
    {
        if (!opcoes.Value.Google.LoginDoAppLigado)
            return Falha(404, "O login com Google ainda não está disponível. Use e-mail e senha.");
        if (string.IsNullOrWhiteSpace(pedido.Codigo) || string.IsNullOrWhiteSpace(pedido.Verificador)
            || !Uri.TryCreate(pedido.Redirecionamento, UriKind.Absolute, out var volta) || volta.Scheme != Uri.UriSchemeHttp || !volta.IsLoopback)
            return Falha(400, "Pedido de login com Google incompleto. Tente de novo.");
        if (await troca.TrocarAsync(pedido.Codigo, pedido.Verificador, pedido.Redirecionamento!, c) is not { } idToken
            || await google.ValidarAsync(idToken) is not { } conta)
            return Falha(401, GoogleRecusado);
        return await SessaoDoGoogle(conta, banco, tokens, relogio);
    }

    // Acha a conta pelo Google (ou pelo e-mail verificado, vinculando), cria se não há, e abre a sessão.
    private static async Task<IResult> SessaoDoGoogle(ContaGoogle conta, BancoDoServidor banco, EmissorDeTokens tokens, TimeProvider relogio)
    {
        var usuario = await banco.Usuarios.FirstOrDefaultAsync(u => u.GoogleId == conta.Id);
        if (usuario is null)
        {
            if (!conta.EmailVerificado)
                return Falha(403, "O e-mail da conta Google não está verificado.");
            usuario = await PorEmail(banco, conta.Email);
            if (usuario is null)
            {
                usuario = new Usuario
                {
                    Id = Guid.NewGuid(), Email = conta.Email, EmailNormalizado = Segredos.NormalizarEmail(conta.Email),
                    Nome = conta.Nome.Length > 120 ? conta.Nome[..120] : conta.Nome, CriadoEm = relogio.GetUtcNow().UtcDateTime,
                };
                banco.Usuarios.Add(usuario);
            }
            usuario.GoogleId = conta.Id;
            usuario.EmailConfirmado = true; // o Google verificou o e-mail
        }
        var sessao = tokens.Emitir(banco, usuario);
        await banco.SaveChangesAsync();
        return Results.Ok(sessao);
    }

    private static async Task<IResult> Renovar(PedidoDeRenovacao pedido, BancoDoServidor banco, EmissorDeTokens tokens, TimeProvider relogio)
    {
        var hash = Segredos.Hash(pedido.Renovacao ?? "");
        var registro = await banco.TokensDeRenovacao.FirstOrDefaultAsync(t => t.Hash == hash);
        if (registro is null)
            return Falha(401, "Sessão vencida: entre de novo.");
        if (registro.RevogadoEm is not null)
        {
            // Um token já trocado voltou: pode ter sido copiado. A família inteira deixa de valer.
            tokens.RevogarFamilia(banco, registro.Familia);
            await banco.SaveChangesAsync();
            return Falha(401, "Sessão vencida: entre de novo.");
        }
        if (registro.ExpiraEm < relogio.GetUtcNow().UtcDateTime || await banco.Usuarios.FindAsync(registro.UsuarioId) is not { } usuario)
            return Falha(401, "Sessão vencida: entre de novo.");
        registro.RevogadoEm = relogio.GetUtcNow().UtcDateTime;
        var sessao = tokens.Emitir(banco, usuario, registro.Familia);
        await banco.SaveChangesAsync();
        return Results.Ok(sessao);
    }

    private static async Task<IResult> Sair(PedidoDeRenovacao pedido, BancoDoServidor banco, EmissorDeTokens tokens)
    {
        var hash = Segredos.Hash(pedido.Renovacao ?? "");
        if (await banco.TokensDeRenovacao.FirstOrDefaultAsync(t => t.Hash == hash) is { } registro)
        {
            tokens.RevogarFamilia(banco, registro.Familia);
            await banco.SaveChangesAsync();
        }
        return Results.NoContent();
    }

    private static async Task<IResult> EsqueciASenha(PedidoDeEmail pedido, BancoDoServidor banco, IEnviadorDeEmail email, TimeProvider relogio, CancellationToken c)
    {
        if (await PorEmail(banco, pedido.Email) is { } usuario)
            await MandarCodigo(banco, email, relogio, usuario, CodigoDeEmail.Senha, null, null, c);
        return Results.Accepted(value: new Resposta(MensagemDaSenha));
    }

    private static async Task<IResult> RedefinirSenha(PedidoDeNovaSenha pedido, BancoDoServidor banco, EmissorDeTokens tokens, TimeProvider relogio)
    {
        if (ProblemaNaSenha(pedido.NovaSenha) is { } problema)
            return Falha(400, problema);
        if (await PorEmail(banco, pedido.Email) is not { } usuario || await UsarCodigo(banco, relogio, usuario, CodigoDeEmail.Senha, pedido.Codigo) is null)
            return Falha(400, CodigoInvalido);
        usuario.SenhaHash = Hasher.HashPassword(usuario, pedido.NovaSenha!);
        (usuario.EmailConfirmado, usuario.TentativasFalhas, usuario.BloqueadoAte) = (true, 0, null);
        tokens.RevogarTudo(banco, usuario.Id); // as outras sessões (de quem sabia a senha velha) caem
        var sessao = tokens.Emitir(banco, usuario);
        await banco.SaveChangesAsync();
        return Results.Ok(sessao);
    }

    /// <summary>Quem sou eu: o perfil, o plano e o uso do mês.</summary>
    private static async Task<IResult> Eu(ClaimsPrincipal quem, BancoDoServidor banco, TimeProvider relogio, IOptions<OpcoesDoServidor> opcoes)
    {
        if (await Usuarios.Atual(quem, banco) is not { } usuario)
            return Falha(401, "Sessão vencida: entre de novo.");
        var (tokens, custo) = await Cotas.NoMes(banco, usuario.Id, relogio);
        var planos = opcoes.Value.Planos;
        return Results.Ok(new
        {
            perfil = EmissorDeTokens.Perfil(usuario),
            tokensUsadosNoMes = tokens,
            creditosUsadosNoMes = Math.Round(planos.Creditos(custo), 1, MidpointRounding.AwayFromZero),
            creditosDoPlano = planos.CreditosPorMes(usuario.Plano),
            creditosAvulsos = Math.Round(planos.Creditos(usuario.MicrodolaresAvulsos), 1, MidpointRounding.AwayFromZero), // Bloco 47
            profissionalAte = usuario.ProfissionalAte,
        });
    }
}

public static class Usuarios
{
    /// <summary>O usuário do token de acesso (nulo se a conta não existe mais).</summary>
    public static async Task<Usuario?> Atual(ClaimsPrincipal quem, BancoDoServidor banco) =>
        Guid.TryParse(quem.FindFirstValue(ClaimTypes.NameIdentifier) ?? quem.FindFirstValue("sub"), out var id) ? await banco.Usuarios.FindAsync(id) : null;
}

/// <summary>
/// Bloco 47: o saldo de uma conta, em milionésimos de dólar: o que já gastou no mês, os créditos do plano e os avulsos
/// (pacotes). Os avulsos só são gastos depois que os do plano acabam, e não vencem no fim do mês.
/// </summary>
public sealed record SaldoDaConta(long GastosDoMes, long DoPlano, long Avulsos)
{
    public long RestanteDoPlano => Math.Max(0, DoPlano - GastosDoMes);
    public long Restante => RestanteDoPlano + Avulsos;
    public bool Esgotado => Restante <= 0;
}

public static class Cotas
{
    public static async Task<SaldoDaConta> SaldoAsync(BancoDoServidor banco, Usuario usuario, OpcoesDosPlanos planos, TimeProvider relogio)
    {
        var (_, gastos) = await NoMes(banco, usuario.Id, relogio);
        return new SaldoDaConta(gastos, planos.Microdolares(planos.CreditosPorMes(usuario.Plano)), usuario.MicrodolaresAvulsos);
    }

    /// <summary>Tira dos avulsos a parte do custo que passou dos créditos do plano; devolve o que resta depois do pedido.</summary>
    public static long Debitar(Usuario usuario, SaldoDaConta antes, long custo)
    {
        var dosAvulsos = custo - Math.Min(custo, antes.RestanteDoPlano);
        usuario.MicrodolaresAvulsos = Math.Max(0, usuario.MicrodolaresAvulsos - dosAvulsos);
        return Math.Max(0, antes.Restante - custo);
    }

    /// <summary>Os tokens e o custo (em milionésimos de dólar) do mês corrente (UTC).</summary>
    public static async Task<(long Tokens, long Microdolares)> NoMes(BancoDoServidor banco, Guid usuarioId, TimeProvider relogio)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var inicio = new DateTime(agora.Year, agora.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var doMes = banco.UsosDeIa.Where(u => u.UsuarioId == usuarioId && u.Quando >= inicio);
        return (await doMes.SumAsync(u => (long)u.TokensTotais), await doMes.SumAsync(u => u.CustoEmMicrodolares));
    }
}
