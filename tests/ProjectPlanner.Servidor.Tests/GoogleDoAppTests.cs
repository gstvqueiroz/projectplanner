using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ProjectPlanner.Servidor.Rotas;
using ProjectPlanner.Servidor.Servicos;

namespace ProjectPlanner.Servidor.Tests;

// Bloco 45: o login com Google do programa (o navegador volta ao app com um código; o servidor troca pelo idToken).
public sealed class GoogleDoAppTests
{
    private static Servidor ComGoogle() => new(new()
    {
        ["Google:ClientIdDoApp"] = "app-123.apps.googleusercontent.com",
        ["Google:SegredoDoApp"] = "segredo-do-app",
    });

    [Fact]
    public async Task ClienteDoGoogle_SoComIdESegredo()
    {
        using var semGoogle = new Servidor();
        var r = await semGoogle.Get("/v1/google");
        (r.StatusCode, await Servidor.Erro(r)).Should().Be((HttpStatusCode.NotFound, "O login com Google ainda não está disponível. Use e-mail e senha."));
        using var soId = new Servidor(new() { ["Google:ClientIdDoApp"] = "app-123" });
        (await soId.Get("/v1/google")).StatusCode.Should().Be(HttpStatusCode.NotFound, "sem o segredo, o servidor não consegue trocar o código");

        using var s = ComGoogle();
        var ok = await s.Get("/v1/google");
        JsonDocument.Parse(await ok.Content.ReadAsStringAsync()).RootElement.GetProperty("clientId").GetString()
            .Should().Be("app-123.apps.googleusercontent.com");
        (await ok.Content.ReadAsStringAsync()).Should().NotContain("segredo");
    }

    [Fact]
    public async Task Codigo_TrocadoEValidado_AbreASessao()
    {
        using var s = ComGoogle();
        s.Google.Tokens["token-ana"] = new ContaGoogle("g-1", "ana@gmail.com", true, "Ana");

        var r = await s.Post("/v1/sessoes/google/codigo", new { codigo = "bom-ana", verificador = "v".PadRight(43, 'x'), redirecionamento = "http://127.0.0.1:53123/" });

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r.Content.ReadFromJsonAsync<Sessao>())!.Usuario.Should().Be(new PerfilDoUsuario("ana@gmail.com", "Ana", Planos.Gratis, false, true));
        s.Troca.Trocas.Single().Should().Be(("bom-ana", "v".PadRight(43, 'x'), "http://127.0.0.1:53123/"));
    }

    [Theory]
    [InlineData("ruim-1", "verificador", "http://127.0.0.1:5000/", 401)]
    [InlineData("", "verificador", "http://127.0.0.1:5000/", 400)]
    [InlineData("bom-ana", "", "http://127.0.0.1:5000/", 400)]
    [InlineData("bom-ana", "verificador", "https://site-de-alguem.com/volta", 400)]
    [InlineData("bom-ana", "verificador", "nao-e-endereco", 400)]
    public async Task Codigo_RecusadoOuPedidoIncompleto(string codigo, string verificador, string redirecionamento, int esperado)
    {
        using var s = ComGoogle();
        s.Google.Tokens["token-ana"] = new ContaGoogle("g-1", "ana@gmail.com", true, "Ana");

        var r = await s.Post("/v1/sessoes/google/codigo", new { codigo, verificador, redirecionamento });

        ((int)r.StatusCode).Should().Be(esperado);
        s.NoBanco(b => b.Usuarios.Count()).Should().Be(0);
    }

    [Fact]
    public async Task Codigo_SemGoogleConfigurado_404()
    {
        using var s = new Servidor();
        (await s.Post("/v1/sessoes/google/codigo", new { codigo = "bom-ana", verificador = "v", redirecionamento = "http://127.0.0.1:5000/" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        s.Troca.Trocas.Should().BeEmpty();
    }

    [Fact]
    public void Audiencias_JuntamOClientIdDoApp()
    {
        new OpcoesDoGoogle { ClientIds = ["telegram", "app"], ClientIdDoApp = "app" }.Audiencias.Should().Equal("telegram", "app");
        new OpcoesDoGoogle { ClientIdDoApp = " " }.Audiencias.Should().BeEmpty();
        (new OpcoesDoGoogle { ClientIdDoApp = "a" }.LoginDoAppLigado, new OpcoesDoGoogle { ClientIdDoApp = "a", SegredoDoApp = "s" }.LoginDoAppLigado)
            .Should().Be((false, true));
    }
}
