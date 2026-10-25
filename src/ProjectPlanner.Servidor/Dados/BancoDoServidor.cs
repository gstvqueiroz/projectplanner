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

    /// <summary>
    /// Bloco 47: até quando o Profissional vale depois de cancelado (a última mensalidade paga); nulo: sem prazo (assinatura
    /// ativa ou plano dado pelo admin). Passada a data, a conta volta ao Grátis (<c>ExpiracaoDosPlanos</c>).
    /// </summary>
    public DateTime? ProfissionalAte { get; set; }

    /// <summary>Bloco 47: os créditos avulsos (pacotes comprados ou dados pelo admin), em milionésimos de dólar. Não vencem no fim do mês.</summary>
    public long MicrodolaresAvulsos { get; set; }
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

    /// <summary>Bloco 50: quantos dos tokens de entrada vieram do cache (custam 10% no Claude e no Gemini 3).</summary>
    public int TokensDoCache { get; set; }

    /// <summary>Bloco 50: a rotina que fez o pedido (<see cref="ProjectPlanner.IA.RotinasDeIa"/>); nula nos pedidos anteriores.</summary>
    public string? Rotina { get; set; }

    public static UsoDeIa Novo(Guid usuario, DateTime quando, string modelo, Servicos.TokensDoPedido tokens, long custo, string rotina) => new()
    {
        UsuarioId = usuario, Quando = quando, Modelo = modelo, TokensDeEntrada = (int)tokens.Entrada, TokensDeSaida = (int)tokens.Saida,
        TokensTotais = (int)(tokens.Entrada + tokens.Saida), TokensDoCache = (int)tokens.LidosDoCache, CustoEmMicrodolares = custo, Rotina = rotina,
    };
}

/// <summary>Bloco 47: a assinatura do Profissional no Mercado Pago (um "preapproval"), como o Mercado Pago informou por último.</summary>
public sealed class Assinatura
{
    /// <summary>O id do preapproval no Mercado Pago.</summary>
    public string Id { get; set; } = "";
    public Guid UsuarioId { get; set; }

    /// <summary>pending (esperando o pagamento), authorized (ativa), paused ou cancelled.</summary>
    public string Status { get; set; } = "";
    public decimal ValorEmReais { get; set; }
    public DateTime CriadaEm { get; set; }
    public DateTime AtualizadaEm { get; set; }
    public DateTime? ProximaCobranca { get; set; }
}

/// <summary>Bloco 47: um pagamento aprovado no Mercado Pago (mensalidade ou pacote de créditos). O id evita contar duas vezes.</summary>
public sealed class Pagamento
{
    public const string DeAssinatura = "assinatura";
    public const string DeCreditos = "creditos";

    /// <summary>O id do pagamento no Mercado Pago.</summary>
    public string Id { get; set; } = "";
    public Guid UsuarioId { get; set; }
    public string Tipo { get; set; } = "";
    public decimal ValorEmReais { get; set; }
    public long Creditos { get; set; }
    public DateTime Quando { get; set; }
}

/// <summary>
/// 0.55.1: um erro inesperado do servidor (uma exceção que nenhuma rota tratou), para o painel mostrar o que aconteceu. O
/// usuário vê o código na mensagem ("Erro no servidor (código K7Q2MX)"); nunca vai conteúdo de pedido nem segredo.
/// </summary>
public sealed class FalhaDoServidor
{
    public long Id { get; set; }
    public string Codigo { get; set; } = "";
    public DateTime Quando { get; set; }
    public string Rota { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Mensagem { get; set; } = "";
    public string? Pilha { get; set; }
    public Guid? UsuarioId { get; set; }
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

/// <summary>
/// Bloco 53: um grupo do Telegram ligado a uma obra. O dono (quem ligou, pelo chat privado já ligado à conta) paga os créditos;
/// a equipe não precisa de conta.
/// </summary>
public sealed class GrupoDoTelegram
{
    public long ChatId { get; set; }
    public Guid UsuarioId { get; set; }
    public Guid ObraId { get; set; }
    public string? Titulo { get; set; }
    public DateTime CriadoEm { get; set; }
}

/// <summary>
/// Bloco 59: uma ordem de serviço publicada nos grupos da obra. O programa manda o texto e a situação; os botões do Telegram
/// (aceitar, começar, terminar) mudam a situação aqui e deixam o aviso na caixa, para o programa aplicar.
/// </summary>
public sealed class OrdemNoTelegram
{
    public const string Emitida = "Emitida";
    public const string Aceita = "Aceita";
    public const string Recusada = "Recusada";
    public const string EmExecucao = "EmExecucao";
    public const string Concluida = "Concluida";

    /// <summary>As situações que o programa manda (os nomes do SituacaoDaOrdem).</summary>
    public static IReadOnlySet<string> Situacoes { get; } =
        new HashSet<string> { "Rascunho", Emitida, Aceita, Recusada, EmExecucao, Concluida, "Conferida", "Paga", "Cancelada" };

    /// <summary>O id da OS no programa.</summary>
    public Guid Id { get; set; }
    public Guid UsuarioId { get; set; }
    public Guid ObraId { get; set; }
    public int Numero { get; set; }
    public string Titulo { get; set; } = "";

    /// <summary>O corpo da mensagem (executante, datas, itens), montado pelo programa.</summary>
    public string Texto { get; set; } = "";
    public string Situacao { get; set; } = Emitida;

    /// <summary>Quem mudou a situação pelo Telegram por último (nulo quando foi o programa).</summary>
    public string? Autor { get; set; }

    /// <summary>As mensagens da OS nos grupos, em JSON: [{"chat":-100500,"mensagem":42}].</summary>
    public string Mensagens { get; set; } = "[]";
    public DateTime AtualizadoEm { get; set; }

    public string Codigo => $"OS-{Numero:000}";
}

/// <summary>Bloco 53: quem fala num grupo ligado, e o papel (o que pode fazer lá).</summary>
public sealed class MembroDoGrupo
{
    public const string Acompanhar = "acompanhar";
    public const string Campo = "campo";
    public const string Engenharia = "engenharia";

    public static IReadOnlyList<string> Papeis { get; } = [Acompanhar, Campo, Engenharia];

    public long ChatId { get; set; }

    /// <summary>O id do usuário no Telegram.</summary>
    public long PessoaId { get; set; }
    public string Nome { get; set; } = "";
    public string Papel { get; set; } = Campo;
    public DateTime DesdeEm { get; set; }

    /// <summary>Pode mandar fotos e #atualizacao para a caixa da obra.</summary>
    public bool Envia => Papel is Campo or Engenharia;

    /// <summary>Pode mudar os papéis da equipe (o dono também pode, sempre).</summary>
    public bool Gerencia => Papel == Engenharia;
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

    /// <summary>Bloco 59: um botão de OS tocado no Telegram (aceitar, iniciar, concluir) ou um /recusar; o Conteudo é o EventoDaOrdemNoTelegram.</summary>
    public const string DeOrdem = "ordem";

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
    public DbSet<Assinatura> Assinaturas => Set<Assinatura>(); // Bloco 47
    public DbSet<Pagamento> Pagamentos => Set<Pagamento>();
    public DbSet<AlertaDeCusto> AlertasDeCusto => Set<AlertaDeCusto>();
    public DbSet<ObraNoServidor> Obras => Set<ObraNoServidor>();
    public DbSet<VinculoDoTelegram> VinculosDoTelegram => Set<VinculoDoTelegram>();
    public DbSet<CodigoDoTelegram> CodigosDoTelegram => Set<CodigoDoTelegram>();
    public DbSet<ItemDaCaixa> CaixaDeEntrada => Set<ItemDaCaixa>();
    public DbSet<GrupoDoTelegram> GruposDoTelegram => Set<GrupoDoTelegram>(); // Bloco 53
    public DbSet<FalhaDoServidor> FalhasDoServidor => Set<FalhaDoServidor>(); // 0.55.1
    public DbSet<MembroDoGrupo> MembrosDosGrupos => Set<MembroDoGrupo>();
    public DbSet<OrdemNoTelegram> OrdensNoTelegram => Set<OrdemNoTelegram>(); // Bloco 59

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
        modelo.Entity<UsoDeIa>().Property(x => x.Rotina).HasMaxLength(32);
        modelo.Entity<Assinatura>(a =>
        {
            a.Property(x => x.Id).HasMaxLength(64);
            a.Property(x => x.Status).HasMaxLength(32);
            a.Property(x => x.ValorEmReais).HasPrecision(12, 2);
            a.HasIndex(x => x.UsuarioId);
        });
        modelo.Entity<Pagamento>(p =>
        {
            p.Property(x => x.Id).HasMaxLength(64);
            p.Property(x => x.Tipo).HasMaxLength(16);
            p.Property(x => x.ValorEmReais).HasPrecision(12, 2);
            p.HasIndex(x => new { x.UsuarioId, x.Quando });
        });
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
        modelo.Entity<FalhaDoServidor>(f =>
        {
            f.HasIndex(x => x.Quando);
            f.Property(x => x.Codigo).HasMaxLength(12);
            f.Property(x => x.Rota).HasMaxLength(200);
            f.Property(x => x.Tipo).HasMaxLength(200);
            f.Property(x => x.Mensagem).HasMaxLength(1000);
            f.Property(x => x.Pilha).HasMaxLength(4000);
        });
        modelo.Entity<GrupoDoTelegram>(g =>
        {
            g.HasKey(x => x.ChatId);
            g.Property(x => x.ChatId).ValueGeneratedNever();
            g.HasIndex(x => x.UsuarioId);
            g.Property(x => x.Titulo).HasMaxLength(200);
        });
        modelo.Entity<OrdemNoTelegram>(o =>
        {
            o.Property(x => x.Id).ValueGeneratedNever();
            o.HasIndex(x => new { x.UsuarioId, x.ObraId });
            o.Property(x => x.Titulo).HasMaxLength(200);
            o.Property(x => x.Texto).HasMaxLength(3500);
            o.Property(x => x.Situacao).HasMaxLength(20);
            o.Property(x => x.Autor).HasMaxLength(120);
            o.Property(x => x.Mensagens).HasMaxLength(2000);
            o.Ignore(x => x.Codigo);
        });
        modelo.Entity<MembroDoGrupo>(m =>
        {
            m.HasKey(x => new { x.ChatId, x.PessoaId });
            m.Property(x => x.Nome).HasMaxLength(120);
            m.Property(x => x.Papel).HasMaxLength(20);
            m.Ignore(x => x.Envia);
            m.Ignore(x => x.Gerencia);
        });
    }
}
