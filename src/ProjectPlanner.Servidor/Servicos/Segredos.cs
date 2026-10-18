using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ProjectPlanner.Servidor.Dados;

namespace ProjectPlanner.Servidor.Servicos;

/// <summary>Hashes, códigos e tokens: o que o servidor guarda nunca é o segredo, só o hash dele.</summary>
public static class Segredos
{
    /// <summary>SHA-256 em hexadecimal (para tokens de renovação e códigos de e-mail, que já são aleatórios).</summary>
    public static string Hash(string valor) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(valor)));

    /// <summary>Um token aleatório de 256 bits, em base64 para URL.</summary>
    public static string TokenAleatorio() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    /// <summary>Um código de 6 dígitos para o e-mail.</summary>
    public static string Codigo() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("000000");

    /// <summary>Compara sem vazar pelo tempo quanto das duas bate.</summary>
    public static bool Iguais(string? a, string? b) =>
        a is not null && b is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    public static string NormalizarEmail(string? email) => (email ?? "").Trim().ToLowerInvariant();
}

/// <summary>A sessão entregue ao app: o token de acesso (curto) e o de renovação (longo).</summary>
public sealed record Sessao(string Acesso, DateTime AcessoExpiraEm, string Renovacao, DateTime RenovacaoExpiraEm, PerfilDoUsuario Usuario);

public sealed record PerfilDoUsuario(string Email, string Nome, string Plano, bool TemSenha, bool Google);

/// <summary>Emite os tokens de acesso (JWT) e cuida dos de renovação (rotação e revogação da família).</summary>
public sealed class EmissorDeTokens(IOptions<OpcoesDoServidor> opcoes, ChaveDoJwt chave, TimeProvider relogio)
{
    private readonly OpcoesDoJwt _jwt = opcoes.Value.Jwt;

    public static PerfilDoUsuario Perfil(Usuario u) => new(u.Email, u.Nome, u.Plano, u.SenhaHash is not null, u.GoogleId is not null);

    /// <summary>Uma sessão nova (o token de renovação começa uma família nova, ou continua a de uma renovação).</summary>
    public Sessao Emitir(BancoDoServidor banco, Usuario usuario, Guid? familia = null)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var expira = agora.AddMinutes(_jwt.MinutosDeAcesso);
        var token = new JwtSecurityToken(_jwt.Emissor, _jwt.Publico,
            [new Claim(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()), new Claim(JwtRegisteredClaimNames.Email, usuario.Email)],
            agora, expira, new SigningCredentials(chave.Chave, SecurityAlgorithms.HmacSha256));
        var renovacao = Segredos.TokenAleatorio();
        var registro = new TokenDeRenovacao
        {
            Id = Guid.NewGuid(), UsuarioId = usuario.Id, Hash = Segredos.Hash(renovacao), Familia = familia ?? Guid.NewGuid(),
            CriadoEm = agora, ExpiraEm = agora.AddDays(_jwt.DiasDeRenovacao),
        };
        banco.TokensDeRenovacao.Add(registro);
        return new Sessao(new JwtSecurityTokenHandler().WriteToken(token), expira, renovacao, registro.ExpiraEm, Perfil(usuario));
    }

    /// <summary>Revoga todos os tokens de renovação de uma família (sair, ou reuso de um token já trocado).</summary>
    public void RevogarFamilia(BancoDoServidor banco, Guid familia)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        foreach (var t in banco.TokensDeRenovacao.Where(t => t.Familia == familia && t.RevogadoEm == null))
            t.RevogadoEm = agora;
    }

    /// <summary>Revoga todas as sessões do usuário (troca de senha).</summary>
    public void RevogarTudo(BancoDoServidor banco, Guid usuarioId)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        foreach (var t in banco.TokensDeRenovacao.Where(t => t.UsuarioId == usuarioId && t.RevogadoEm == null))
            t.RevogadoEm = agora;
    }
}

/// <summary>A chave que assina os JWT (da configuração; em desenvolvimento, sem ela, uma provisória a cada execução).</summary>
public sealed class ChaveDoJwt(SymmetricSecurityKey chave, bool provisoria)
{
    public SymmetricSecurityKey Chave { get; } = chave;
    public bool Provisoria { get; } = provisoria;

    public static ChaveDoJwt Da(OpcoesDoJwt jwt, bool desenvolvimento)
    {
        if (!string.IsNullOrWhiteSpace(jwt.Chave))
        {
            var bytes = Encoding.UTF8.GetBytes(jwt.Chave);
            if (bytes.Length < 32)
                throw new InvalidOperationException("Jwt:Chave precisa de 32 bytes ou mais.");
            return new ChaveDoJwt(new SymmetricSecurityKey(bytes), false);
        }
        if (!desenvolvimento)
            throw new InvalidOperationException("Configure Jwt:Chave (variável de ambiente Jwt__Chave) antes de rodar em produção.");
        return new ChaveDoJwt(new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(48)), true);
    }
}
