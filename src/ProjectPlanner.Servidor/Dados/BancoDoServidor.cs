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

    /// <summary>Bloco 46: o custo do pedido em milionésimos de dólar (pela tabela de preços do modelo), base dos créditos.</summary>
    public long CustoEmMicrodolares { get; set; }
}

/// <summary>Bloco 48: o alerta de custo já mandado num mês ("2026-10"), para não mandar de novo.</summary>
public sealed class AlertaDeCusto
{
    public string Mes { get; set; } = "";
    public DateTime EnviadoEm { get; set; }
    public long CustoEmMicrodolares { get; set; }

    /// <summary>Para quem o e-mail foi; nulo se não havia e-mail do admin configurado (o alerta ficou só no painel).</summary>
    public string? EnviadoPara { get; set; }
}

/// <summary>
/// Bloco 49: uma obra que o usuário marcou para o Telegram, com o retrato que o programa mandou (o mesmo que o assistente
/// usa: tarefas, datas, %, medições, indicadores), atualizado a cada vez que ele salva.
/// </summary>
public sealed class ObraNoServidor
{
    public Guid Id { get; set; }
    public Guid UsuarioId { get; set; }

    /// <summary>O id do projeto no arquivo .pplan.</summary>
    public Guid Projeto { get; set; }
    public string Nome { get; set; } = "";

    /// <summary>O retrato (ContextoDoProjeto) em JSON.</summary>
    public string Retrato { get; set; } = "";
    public DateTime AtualizadoEm { get; set; }
}

/// <summary>Bloco 49: a conta ligada a um chat do Telegram, e a obra em que o chat está.</summary>
public sealed class VinculoDoTelegram
{
    public Guid UsuarioId { get; set; }
    public long ChatId { get; set; }
    public string? Nome { get; set; }
    public Guid? ObraAtiva { get; set; }
    public DateTime CriadoEm { get; set; }
}

/// <summary>Bloco 49: o código que liga o Telegram à conta (guardado só o hash, vale 15 minutos).</summary>
public sealed class CodigoDoTelegram
{
    public string Hash { get; set; } = "";
    public Guid UsuarioId { get; set; }
    public DateTime ExpiraEm { get; set; }
}

/// <summary>
/// Bloco 49: o que chegou pelo Telegram para uma obra e espera o programa: atualizações (para o cartão de confirmação) ou uma
/// foto (para a importação). Some do servidor quando o programa leva.
/// </summary>
public sealed class ItemDaCaixa
{
    public const string DeAtualizacoes = "atualizacoes";
    public const string DeFoto = "foto";

    public Guid Id { get; set; }
    public Guid UsuarioId { get; set; }
    public Guid ObraId { get; set; }
    public string Tipo { get; set; } = DeAtualizacoes;
    public DateTime CriadoEm { get; set; }

    /// <summary>A mensagem (ou a legenda da foto) como chegou.</summary>
    public string Texto { get; set; } = "";

    /// <summary>As atualizações (AtualizacaoDaObra) em JSON.</summary>
    public string? Conteudo { get; set; }

    /// <summary>As tarefas do retrato daquele momento (Nº e id), para o programa achar a tarefa certa mesmo se a numeração mudou.</summary>
    public string? Tarefas { get; set; }

    public byte[]? Foto { get; set; }
    public string? NomeDaFoto { get; set; }
}

/// <summary>Bloco 44: o banco do servidor (SQLite em desenvolvimento e nos testes; PostgreSQL em produção).</summary>
public sealed class BancoDoServidor(DbContextOptions<BancoDoServidor> opcoes) : DbContext(opcoes)
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<TokenDeRenovacao> TokensDeRenovacao => Set<TokenDeRenovacao>();
    public DbSet<CodigoDeEmail> CodigosDeEmail => Set<CodigoDeEmail>();
    public DbSet<UsoDeIa> UsosDeIa => Set<UsoDeIa>();
    public DbSet<AlertaDeCusto> AlertasDeCusto => Set<AlertaDeCusto>();
    public DbSet<ObraNoServidor> Obras => Set<ObraNoServidor>();
    public DbSet<VinculoDoTelegram> VinculosDoTelegram => Set<VinculoDoTelegram>();
    public DbSet<CodigoDoTelegram> CodigosDoTelegram => Set<CodigoDoTelegram>();
    public DbSet<ItemDaCaixa> CaixaDeEntrada => Set<ItemDaCaixa>();

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
        modelo.Entity<UsoDeIa>().HasIndex(x => x.Quando);
        modelo.Entity<AlertaDeCusto>(a =>
        {
            a.HasKey(x => x.Mes);
            a.Property(x => x.Mes).HasMaxLength(7);
            a.Property(x => x.EnviadoPara).HasMaxLength(254);
        });
        modelo.Entity<ObraNoServidor>(o =>
        {
            o.HasIndex(x => new { x.UsuarioId, x.Projeto }).IsUnique();
            o.Property(x => x.Nome).HasMaxLength(200);
        });
        modelo.Entity<VinculoDoTelegram>(v =>
        {
            v.HasKey(x => x.UsuarioId);
            v.HasIndex(x => x.ChatId).IsUnique();
            v.Property(x => x.Nome).HasMaxLength(120);
        });
        modelo.Entity<CodigoDoTelegram>().HasKey(x => x.Hash);
        modelo.Entity<ItemDaCaixa>(i =>
        {
            i.HasIndex(x => new { x.UsuarioId, x.ObraId });
            i.Property(x => x.Tipo).HasMaxLength(20);
            i.Property(x => x.NomeDaFoto).HasMaxLength(200);
        });
    }
}
