using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ProjectPlanner.Servidor;
using ProjectPlanner.Servidor.Dados;
using ProjectPlanner.Servidor.Rotas;
using ProjectPlanner.Servidor.Servicos;

// Bloco 44: o servidor de contas e IA do ProjectPlanner. Toda a configuração (e os segredos) vem do ambiente: ver docs/SERVIDOR.md.
// A configuração é lida pelo IOptions (na hora de montar os serviços), não antes: assim os testes conseguem trocá-la.
var builder = WebApplication.CreateBuilder(args);
static OpcoesDoServidor Opcoes(IServiceProvider s) => s.GetRequiredService<IOptions<OpcoesDoServidor>>().Value;

builder.Services.Configure<OpcoesDoServidor>(builder.Configuration);
builder.Services.AddSingleton(s => ChaveDoJwt.Da(Opcoes(s).Jwt, s.GetRequiredService<IHostEnvironment>().IsDevelopment()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<BancoDoServidor>((s, o) =>
    Banco.Configurar(o, s.GetRequiredService<IConfiguration>().GetConnectionString("Banco") ?? Banco.Padrao));
builder.Services.AddScoped<EmissorDeTokens>();
builder.Services.AddSingleton<IValidadorDoGoogle, ValidadorDoGoogle>();
builder.Services.AddSingleton<EmailNoLog>();
builder.Services.AddSingleton<EmailPorSmtp>();
builder.Services.AddSingleton<IEnviadorDeEmail>(s =>
    string.IsNullOrWhiteSpace(Opcoes(s).Email.Smtp) ? s.GetRequiredService<EmailNoLog>() : s.GetRequiredService<EmailPorSmtp>());
builder.Services.AddHttpClient(Ia.ClienteDoGemini, c => c.Timeout = TimeSpan.FromMinutes(2));
builder.Services.AddRateLimiter(_ => { });
builder.Services.AddOptions<RateLimiterOptions>().Configure<IOptions<OpcoesDoServidor>>((o, opcoes) => LimitesDeUso.Configurar(o, opcoes.Value.Limites));
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear(); // o proxy da nuvem não tem endereço fixo
    o.KnownProxies.Clear();
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<ChaveDoJwt, TimeProvider, IOptions<OpcoesDoServidor>>((o, chave, relogio, opcoes) =>
{
    var jwt = opcoes.Value.Jwt;
    o.MapInboundClaims = false;
    var folga = TimeSpan.FromSeconds(30);
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Emissor,
        ValidAudience = jwt.Publico,
        IssuerSigningKey = chave.Chave,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        NameClaimType = "sub",
        // O relógio do servidor (o dos testes pode andar no tempo).
        LifetimeValidator = (antes, expira, _, _) =>
        {
            var agora = relogio.GetUtcNow().UtcDateTime;
            return expira is { } e && e > agora - folga && (antes is not { } a || a <= agora + folga);
        },
    };
    o.Events = new JwtBearerEvents
    {
        OnChallenge = async contexto =>
        {
            contexto.HandleResponse();
            contexto.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await contexto.Response.WriteAsJsonAsync(new Erro("Entre na sua conta ProjectPlanner (ou a sessão venceu: entre de novo)."));
        },
    };
});
builder.Services.AddAuthorization();

var app = builder.Build();
var configuracao = Opcoes(app.Services);

using (var escopo = app.Services.CreateScope())
{
    var chave = escopo.ServiceProvider.GetRequiredService<ChaveDoJwt>(); // sem chave em produção, para aqui
    Banco.Preparar(escopo.ServiceProvider.GetRequiredService<BancoDoServidor>());
    var log = escopo.ServiceProvider.GetRequiredService<ILogger<Program>>();
    if (chave.Provisoria)
        log.LogWarning("Sem Jwt:Chave: usando uma chave provisória (as sessões caem a cada reinício). Só para desenvolvimento.");
    if (string.IsNullOrWhiteSpace(configuracao.Gemini.Chave))
        log.LogWarning("Sem Gemini:Chave: os pedidos de IA vão responder que a IA não está configurada.");
    if (string.IsNullOrWhiteSpace(configuracao.Email.Smtp))
        log.LogWarning("Sem Email:Smtp: os e-mails (com os códigos) vão só para o log.");
}

if (configuracao.Limites.AtrasDeProxy)
    app.UseForwardedHeaders();
app.UseExceptionHandler(erro => erro.Run(async contexto =>
{
    contexto.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await contexto.Response.WriteAsJsonAsync(new Erro("Erro no servidor. Tente de novo."));
}));
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/", () => Results.Ok(new { servico = "ProjectPlanner.Servidor", versao = typeof(Program).Assembly.GetName().Version?.ToString(3) }));
Contas.Mapear(app);
Ia.Mapear(app);
Admin.Mapear(app);

app.Run();

public partial class Program;
