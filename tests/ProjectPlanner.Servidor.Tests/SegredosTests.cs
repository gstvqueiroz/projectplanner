using ProjectPlanner.Servidor.Servicos;

namespace ProjectPlanner.Servidor.Tests;

public sealed class SegredosTests
{
    [Fact]
    public void ChaveDoJwt_CurtaOuAusenteEmProducao_Recusa_EmDesenvolvimentoProvisoria()
    {
        FluentActions.Invoking(() => ChaveDoJwt.Da(new OpcoesDoJwt { Chave = "curta" }, true)).Should().Throw<InvalidOperationException>()
            .WithMessage("Jwt:Chave precisa de 32 bytes ou mais.");
        FluentActions.Invoking(() => ChaveDoJwt.Da(new OpcoesDoJwt(), false)).Should().Throw<InvalidOperationException>()
            .WithMessage("Configure Jwt:Chave*");
        var provisoria = ChaveDoJwt.Da(new OpcoesDoJwt(), true);
        (provisoria.Provisoria, provisoria.Chave.Key.Length).Should().Be((true, 48));
        ChaveDoJwt.Da(new OpcoesDoJwt { Chave = new string('k', 32) }, false).Provisoria.Should().BeFalse();
    }

    [Fact]
    public void Hash_Codigo_Token_Iguais_Email()
    {
        Segredos.Hash("abc").Should().Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
        Enumerable.Range(0, 50).Select(_ => Segredos.Codigo()).Should().OnlyContain(c => c.Length == 6 && c.All(char.IsDigit));
        Segredos.TokenAleatorio().Should().HaveLength(43).And.NotBe(Segredos.TokenAleatorio());
        (Segredos.Iguais("a", "a"), Segredos.Iguais("a", "b"), Segredos.Iguais(null, "a"), Segredos.Iguais("a", "ab")).Should().Be((true, false, false, false));
        Segredos.NormalizarEmail("  Ana@Exemplo.COM ").Should().Be("ana@exemplo.com");
        Segredos.NormalizarEmail(null).Should().Be("");
    }

    [Fact]
    public void Planos_CotaDoPlano()
    {
        var p = new OpcoesDosPlanos();
        (p.CreditosPorMes(Planos.Gratis), p.CreditosPorMes(Planos.Profissional), p.CreditosPorMes("desconhecido")).Should().Be((300L, 6_000L, 300L));
        (p.Libera(Planos.Gratis, Niveis.Padrao), p.Libera(Planos.Gratis, Niveis.Avancado), p.Libera(Planos.Profissional, Niveis.Maximo)).Should().Be((true, false, true));
        new OpcoesDosPlanos { NiveisDoGratis = " padrao , avancado " }.Libera(Planos.Gratis, Niveis.Avancado).Should().BeTrue();
        p.Creditos(6_750).Should().Be(13.5m, "um pedido típico no Flash (US$ 0,00675) gasta 13,5 créditos de US$ 0,0005");
    }

    [Fact]
    public void Banco_PelaConnectionString()
    {
        (Dados.Banco.EhPostgres("Host=db;Database=pp"), Dados.Banco.EhPostgres("Data Source=x.db")).Should().Be((true, false));
        Dados.Banco.EhPostgres("postgresql://u:s@h/db").Should().BeTrue();
        Dados.Banco.ParaNpgsql("Host=db;Database=pp").Should().Be("Host=db;Database=pp");
        // O formato que o Neon mostra (com senha que precisa de escape).
        var npgsql = new Npgsql.NpgsqlConnectionStringBuilder(Dados.Banco.ParaNpgsql(
            "postgresql://neondb_owner:s%40nh%3Aa@ep-azul-123.us-east-1.aws.neon.tech/neondb?sslmode=require&channel_binding=require"));
        (npgsql.Host, npgsql.Port, npgsql.Database, npgsql.Username, npgsql.Password, npgsql.SslMode, npgsql.ChannelBinding).Should().Be(
            ("ep-azul-123.us-east-1.aws.neon.tech", 5432, "neondb", "neondb_owner", "s@nh:a", Npgsql.SslMode.Require, Npgsql.ChannelBinding.Require));
        new Npgsql.NpgsqlConnectionStringBuilder(Dados.Banco.ParaNpgsql("postgres://eu@localhost:6543/pp")).Should().Match<Npgsql.NpgsqlConnectionStringBuilder>(
            b => b.Port == 6543 && b.Password == null && b.Database == "pp");
    }
}
