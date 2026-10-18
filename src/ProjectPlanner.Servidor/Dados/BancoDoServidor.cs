using Microsoft.EntityFrameworkCore;

namespace ProjectPlanner.Servidor.Dados;

public sealed class Usuario
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";

    /// <summary>O e-mail em minúsculas, sem espaços: é por ele que se acha a conta.</summary>
    public string EmailNormalizado { get; set; } = "";
    public string Nome { get; set; } = "";

    /// <summary>O hash da senha (PBKDF2, do ASP.NET Identity); nulo numa conta só do Google ou ainda não confirmada.</summary>
    public string? SenhaHash { get; set; }

    /// <summary>O "sub" da conta Google vinculada.</summary>
    public string? GoogleId { get; set; }
    public bool EmailConfirmado { get; set; }
    public DateTime CriadoEm { get; set; }
    public string Plano { get; set; } = Planos.Gratis;
    public int TentativasFalhas { get; set; }
    public DateTime? BloqueadoAte { get; set; }
}

/// <summary>Um token de renovação (guardado só o hash). Os da mesma família vêm um do outro: reusar um velho revoga todos.</summary>
public sealed class TokenDeRenovacao
{
    public Guid Id { get; set; }
    public Guid UsuarioId { get; set; }
    public string Hash { get; set; } = "";
    public Guid Familia { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime ExpiraEm { get; set; }
    public DateTime? RevogadoEm { get; set; }
}

/// <summary>
/// Um código mandado por e-mail (confirmação ou senha), guardado só o hash, de uso único e com prazo. O de confirmação leva a
/// senha e o nome do cadastro que o pediu: confirmar exige o código e essa senha, para que um cadastro de outra pessoa com o
/// mesmo e-mail (ainda não confirmado) não vire a senha da conta.
/// </summary>
public sealed class CodigoDeEmail
{
    public const string Confirmacao = "confirmacao";
    public const string Senha = "senha";

    public Guid Id { get; set; }
    public Guid UsuarioId { get; set; }
    public string Tipo { get; set; } = Confirmacao;
    public string Hash { get; set; } = "";
    public DateTime CriadoEm { get; set; }
    public DateTime ExpiraEm { get; set; }
    public DateTime? UsadoEm { get; set; }
    public int Tentativas { get; set; }
    public string? SenhaHash { get; set; }
    public string? Nome { get; set; }
}

/// <summary>Um pedido de IA atendido: só quem, quando, o modelo e os tokens (nunca o conteúdo).</summary>
public sealed class UsoDeIa
{
    public long Id { get; set; }
    public Guid UsuarioId { get; set; }
    public DateTime Quando { get; set; }
    public string Modelo { get; set; } = "";
    public int TokensDeEntrada { get; set; }
    public int TokensDeSaida { get; set; }
    public int TokensTotais { get; set; }
}

/// <summary>Bloco 44: o banco do servidor (SQLite em desenvolvimento e nos testes; PostgreSQL em produção).</summary>
public sealed class BancoDoServidor(DbContextOptions<BancoDoServidor> opcoes) : DbContext(opcoes)
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<TokenDeRenovacao> TokensDeRenovacao => Set<TokenDeRenovacao>();
    public DbSet<CodigoDeEmail> CodigosDeEmail => Set<CodigoDeEmail>();
    public DbSet<UsoDeIa> UsosDeIa => Set<UsoDeIa>();

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        modelo.Entity<Usuario>(u =>
        {
            u.HasIndex(x => x.EmailNormalizado).IsUnique();
            u.HasIndex(x => x.GoogleId).IsUnique();
            u.Property(x => x.Email).HasMaxLength(254);
            u.Property(x => x.EmailNormalizado).HasMaxLength(254);
            u.Property(x => x.Nome).HasMaxLength(120);
            u.Property(x => x.Plano).HasMaxLength(40);
        });
        modelo.Entity<TokenDeRenovacao>(t =>
        {
            t.HasIndex(x => x.Hash).IsUnique();
            t.HasIndex(x => x.Familia);
        });
        modelo.Entity<CodigoDeEmail>(c =>
        {
            c.HasIndex(x => new { x.UsuarioId, x.Tipo });
            c.Property(x => x.Nome).HasMaxLength(120);
        });
        modelo.Entity<UsoDeIa>().HasIndex(x => new { x.UsuarioId, x.Quando });
    }
}
