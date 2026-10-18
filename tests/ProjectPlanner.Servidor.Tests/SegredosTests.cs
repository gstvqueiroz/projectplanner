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
        (p.TokensPorMes(Planos.Gratis), p.TokensPorMes(Planos.Profissional), p.TokensPorMes("desconhecido")).Should().Be((300_000L, 10_000_000L, 300_000L));
        new OpcoesDoGemini().Liberados.Should().Equal("gemini-2.5-flash", "gemini-2.5-flash-lite", "gemini-2.5-pro");
        new OpcoesDoGemini { Modelos = ["gemini-x", "gemini-x"] }.Liberados.Should().Equal("gemini-x");
    }

    [Fact]
    public void Banco_PelaConnectionString()
    {
        (Dados.Banco.EhPostgres("Host=db;Database=pp"), Dados.Banco.EhPostgres("Data Source=x.db")).Should().Be((true, false));
    }
}
