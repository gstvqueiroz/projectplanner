using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ProjectPlanner.Servidor.Dados;
using ProjectPlanner.Servidor.Servicos;

namespace ProjectPlanner.Servidor.Tests;

// Bloco 69: a fila do Telegram no banco (um deploy não perde o que esperava), as versões do programa (o aviso de versão nova,
// a mínima e a versão de cada conta no painel) e os erros que o programa manda ao suporte.
public sealed class FilaEVersoesTests : IDisposable
{
    private readonly Servidor _s = new(new()
    {
        ["App:UltimaVersao"] = "0.69.0",
        ["App:VersaoMinima"] = "0.60.0",
        ["App:Download"] = "https://exemplo.com/projectplanner",
    });

    public void Dispose() => _s.Dispose();

    private async Task<HttpStatusCode> AvisoDoTelegram(string corpo)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, "/v1/telegram/webhook") { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
        pedido.Headers.Add(Rotas.Telegram.CabecalhoDoSegredo, new OpcoesDoTelegram { Segredo = Servidor.SegredoDoTelegram }.SegredoParaOTelegram!);
        return (await _s.CreateClient().SendAsync(pedido)).StatusCode;
    }

    private static string Oi(long update, long chat) => "{\"update_id\":" + update + ",\"message\":{\"chat\":{\"id\":" + chat + "},\"text\":\"oi\"}}";

    [Fact]
    public async Task Fila_FicaNoBanco_UmAvisoRepetidoEntraUmaVez_EAtendidoSai()
    {
        var fila = _s.Services.GetRequiredService<FilaDoTelegram>();
        var agora = _s.Relogio.Agora.UtcDateTime;
        _s.NoBanco(b =>
        {
            using var json = JsonDocument.Parse(Oi(70, 7070));
            return (fila.GuardarAsync(b, json.RootElement, agora, CancellationToken.None).Result, fila.GuardarAsync(b, json.RootElement, agora, CancellationToken.None).Result);
        }).Should().Be((true, false), "o Telegram manda de novo o que não teve resposta a tempo");

        (await AvisoDoTelegram(Oi(71, 7171))).Should().Be(HttpStatusCode.OK);
        (await AvisoDoTelegram(Oi(71, 7171))).Should().Be(HttpStatusCode.OK, "o repetido também é respondido, mas não entra de novo");

        for (var i = 0; i < 50 && (_s.NoBanco(b => b.AvisosDoTelegram.Count(a => a.Atendido == null)) > 0 || _s.Telegram.Mensagens.Count(m => m.Chat is 7070 or 7171) < 2); i++)
            await Task.Delay(100);
        (await AvisoDoTelegram(Oi(71, 7171))).Should().Be(HttpStatusCode.OK, "e o que chega de novo depois de atendido também não");
        await Task.Delay(300);
        _s.Telegram.Mensagens.Where(m => m.Chat is 7070 or 7171).Select(m => (m.Chat, m.Texto)).Should()
            .BeEquivalentTo([(7070L, AtendenteDoTelegram.SemVinculo), (7171L, AtendenteDoTelegram.SemVinculo)], "cada um uma vez");
        _s.NoBanco(b => b.AvisosDoTelegram.Count(a => a.Atendido != null)).Should().Be(2, "os atendidos ficam marcados por um dia");
        _s.Relogio.Andar(TimeSpan.FromHours(25));
        await _s.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<ProcessadorDoTelegram>().Single()
            .AtenderOsGuardadosAsync(CancellationToken.None);
        _s.NoBanco(b => b.AvisosDoTelegram.Count()).Should().Be(0, "depois de um dia saem do banco");
    }

    [Fact]
    public async Task Fila_AoSubir_AtendeOQueFicou_ETiraOsVencidosEOsQueCairamDuasVezes()
    {
        var agora = _s.Relogio.Agora.UtcDateTime;
        // O que ficou de antes (o servidor caiu): um que esperava, um que caiu no meio uma vez, um que caiu duas e um velho.
        _s.NoBanco(b =>
        {
            b.AvisosDoTelegram.AddRange(
                new AvisoDoTelegram { UpdateId = 80, Json = Oi(80, 8080), Recebido = agora.AddMinutes(-3) },
                new AvisoDoTelegram { UpdateId = 81, Json = Oi(81, 8181), Recebido = agora.AddMinutes(-2), Tentativas = 1 },
                new AvisoDoTelegram { UpdateId = 82, Json = Oi(82, 8282), Recebido = agora.AddMinutes(-1), Tentativas = 2 },
                new AvisoDoTelegram { UpdateId = 83, Json = Oi(83, 8383), Recebido = agora.AddHours(-25) });
            return b.SaveChanges();
        });

        await _s.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<ProcessadorDoTelegram>().Single()
            .AtenderOsGuardadosAsync(CancellationToken.None);

        _s.Telegram.Mensagens.Where(m => m.Chat is >= 8080 and <= 8383).Select(m => m.Chat).Should().BeEquivalentTo([8080L, 8181L]);
        _s.NoBanco(b => b.AvisosDoTelegram.OrderBy(a => a.UpdateId).Select(a => a.UpdateId).ToList()).Should().Equal([80L, 81L, 82L],
            "o velho sai do banco; os outros ficam marcados como fora da fila");
        _s.NoBanco(b => b.AvisosDoTelegram.Count(a => a.Atendido == null)).Should().Be(0);
    }

    [Fact]
    public async Task Versao_ParaOAvisoDoPrograma_SemConta()
    {
        var versao = await (await _s.Get("/v1/versao")).Content.ReadFromJsonAsync<JsonElement>();

        (versao.GetProperty("ultima").GetString(), versao.GetProperty("minima").GetString(), versao.GetProperty("download").GetString())
            .Should().Be(("0.69.0", "0.60.0", "https://exemplo.com/projectplanner"));
        using var semConfig = new Servidor();
        var vazia = await (await semConfig.Get("/v1/versao")).Content.ReadFromJsonAsync<JsonElement>();
        vazia.GetProperty("ultima").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task VersaoMinima_RecusaOProgramaVelho_EAnotaAVersaoDaConta()
    {
        var sessao = await _s.CriarConta();
        async Task<HttpResponseMessage> Eu(string? versao)
        {
            var pedido = new HttpRequestMessage(HttpMethod.Get, "/v1/eu");
            pedido.Headers.Authorization = new("Bearer", sessao.Acesso);
            if (versao is not null)
                pedido.Headers.Add(VersoesDoApp.Cabecalho, versao);
            return await _s.CreateClient().SendAsync(pedido);
        }

        var velha = await Eu("0.59.3");
        (velha.StatusCode, await Servidor.Erro(velha)).Should().Be((HttpStatusCode.UpgradeRequired,
            "Esta versão do ProjectPlanner (0.59.3) não funciona mais com o servidor. Atualize para a 0.69.0 ou mais nova: https://exemplo.com/projectplanner"));
        (await Eu(null)).StatusCode.Should().Be(HttpStatusCode.OK, "sem o cabeçalho (scripts, versões antes do Bloco 69) passa");
        (await Eu("0.60.0")).StatusCode.Should().Be(HttpStatusCode.OK);
        _s.NoBanco(b => b.Usuarios.Single().VersaoDoApp).Should().Be("0.60.0");
        (await Eu("0.69.0")).StatusCode.Should().Be(HttpStatusCode.OK);
        _s.NoBanco(b => (b.Usuarios.Single().VersaoDoApp, b.Usuarios.Single().VersaoVistaEm)).Should().Be(("0.69.0", (DateTime?)_s.Relogio.Agora.UtcDateTime));
        VersoesDoApp.Ler("1.2").Should().Be(new Version(1, 2, 0));
        VersoesDoApp.Ler("abc").Should().BeNull();
    }

    [Fact]
    public async Task ErrosDoPrograma_ComConta_GuardadosNoLimiteDoDia_ENoPainel()
    {
        var sessao = await _s.CriarConta();
        var erro = new Rotas.ErroDoPrograma("0.69.0", "macOS 26.0", "interface", "System.NullReferenceException", "Object reference not set",
            "at ProjectPlanner.App.ViewModels.X.Y() in src/ProjectPlanner.App/ViewModels/X.cs:line 10");

        (await _s.Post("/v1/erros", erro)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        for (var i = 0; i < Rotas.Programa.PorDia + 3; i++)
            (await _s.Post("/v1/erros", erro, sessao.Acesso)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        _s.NoBanco(b => b.FalhasDoApp.Count()).Should().Be(Rotas.Programa.PorDia, "no máximo 20 por conta por dia");
        _s.NoBanco(b => b.FalhasDoApp.First()).Should().Match<FalhaDoApp>(f => f.Tipo == "System.NullReferenceException" && f.Origem == "interface" && f.Versao == "0.69.0");

        var pedido = new HttpRequestMessage(HttpMethod.Get, "/v1/admin/painel");
        pedido.Headers.Add(Rotas.Admin.Cabecalho, Servidor.ChaveDeAdmin);
        var painel = await (await _s.CreateClient().SendAsync(pedido)).Content.ReadFromJsonAsync<JsonElement>();
        var linha = painel.GetProperty("falhasDoApp")[0];
        (linha.GetProperty("email").GetString(), linha.GetProperty("sistema").GetString()).Should().Be(("ana@exemplo.com", "macOS 26.0"));
        painel.GetProperty("avisosNaFilaDoTelegram").GetInt32().Should().Be(0);
        painel.GetProperty("versoes").GetArrayLength().Should().Be(0, "a conta ainda não mandou o cabeçalho da versão");
    }
}
