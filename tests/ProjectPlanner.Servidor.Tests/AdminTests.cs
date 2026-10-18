using System.Net;
using System.Net.Http.Json;
using ProjectPlanner.Servidor.Rotas;
using ProjectPlanner.Servidor.Servicos;

namespace ProjectPlanner.Servidor.Tests;

// Bloco 44: trocar o plano à mão (até a cobrança do Bloco 46), a raiz do serviço e os limites das rotas de conta.
public sealed class AdminTests
{
    public static Task<HttpResponseMessage> TrocarPlano(Servidor s, string email, string plano, string? chave)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, $"/v1/admin/usuarios/{Uri.EscapeDataString(email)}/plano") { Content = JsonContent.Create(new { plano }) };
        if (chave is not null)
            pedido.Headers.Add(Admin.Cabecalho, chave);
        return s.CreateClient().SendAsync(pedido);
    }

    [Fact]
    public async Task TrocarPlano_ComAChave_ESemElaFechado()
    {
        using var s = new Servidor();
        await s.CriarConta();

        (await TrocarPlano(s, "ana@exemplo.com", Planos.Profissional, null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await TrocarPlano(s, "ana@exemplo.com", Planos.Profissional, "errada")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var desconhecido = await TrocarPlano(s, "ana@exemplo.com", "ouro", Servidor.ChaveDeAdmin);
        (desconhecido.StatusCode, await Servidor.Erro(desconhecido)).Should().Be((HttpStatusCode.BadRequest, "Plano desconhecido. Use um destes: gratis, profissional."));
        (await TrocarPlano(s, "ninguem@exemplo.com", Planos.Profissional, Servidor.ChaveDeAdmin)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var ok = await TrocarPlano(s, "ANA@exemplo.com", Planos.Profissional, Servidor.ChaveDeAdmin);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.Content.ReadFromJsonAsync<PerfilDoUsuario>())!.Plano.Should().Be(Planos.Profissional);
        s.NoBanco(b => b.Usuarios.Single().Plano).Should().Be(Planos.Profissional);

        using var semChave = new Servidor(new() { ["Admin:Chave"] = "" });
        await semChave.CriarConta();
        (await TrocarPlano(semChave, "ana@exemplo.com", Planos.Profissional, "")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "sem chave configurada, fechado");
    }

    [Fact]
    public async Task Raiz_DizOServicoEAVersao()
    {
        using var s = new Servidor();
        var r = await s.Get("/");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r.Content.ReadAsStringAsync()).Should().StartWith("{\"servico\":\"ProjectPlanner.Servidor\",\"versao\":\"0.");
    }

    [Fact]
    public async Task RotasDeConta_LimitePorMinuto_429()
    {
        using var s = new Servidor(new() { ["Limites:PedidosDeContaPorMinuto"] = "3" });
        for (var i = 0; i < 3; i++)
            (await s.Post("/v1/sessoes", new { email = "a@b.com", senha = "x" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var r = await s.Post("/v1/senha/esqueci", new { email = "a@b.com" });

        (r.StatusCode, await Servidor.Erro(r)).Should().Be(((HttpStatusCode)429, "Muitos pedidos seguidos. Espere um minuto e tente de novo."));
    }
}
