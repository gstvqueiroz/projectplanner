using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ProjectPlanner.Servidor.Dados;
using ProjectPlanner.Servidor.Servicos;

namespace ProjectPlanner.Servidor.Tests;

// Bloco 85: o áudio (o Gemini transcreve e o texto segue como escrito, com a transcrição no começo da resposta), a foto com
// #atualizacao na legenda (guarda a foto e anota o que a legenda conta) e o /obra com um botão por obra.
public sealed partial class TelegramTests
{
    private async Task MandarAudio(string arquivo, int segundos = 6, string tipo = "audio/ogg", string campo = "voice")
    {
        var mensagem = new JsonObject
        {
            ["chat"] = new JsonObject { ["id"] = Chat, ["type"] = "private" }, ["from"] = new JsonObject { ["id"] = Chat, ["first_name"] = "Ana" },
            [campo] = new JsonObject { ["file_id"] = arquivo, ["duration"] = segundos, ["mime_type"] = tipo, ["file_size"] = 12_000 },
        };
        using var escopo = _s.Services.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<AtendenteDoTelegram>()
            .AtenderAsync(JsonDocument.Parse(new JsonObject { ["update_id"] = 5, ["message"] = mensagem }.ToJsonString()).RootElement, default);
    }

    [Fact]
    public async Task Audio_OGeminiTranscreve_ESegueComoTexto_ComATranscricaoNaResposta()
    {
        await Preparar();
        var voz = Encoding.UTF8.GetBytes("bytes-da-voz");
        _s.Telegram.Arquivos["voz-1"] = voz;
        _s.Gemini.Proximas.Enqueue(Resposta("a alvenaria está em 60%\n"));
        _s.Gemini.Proximas.Enqueue(RespostaComAtualizacao);

        await MandarAudio("voz-1");

        var transcricao = _s.Gemini.Pedidos.First();
        transcricao.Endereco.AbsolutePath.Should().EndWith("models/gemini-2.5-flash:generateContent", "o Gemini mais barato configurado");
        transcricao.Chave.Should().Be(Servidor.ChaveDoGemini);
        var partes = JsonNode.Parse(transcricao.Corpo)!["contents"]![0]!["parts"]!.AsArray();
        (partes[0]!["inlineData"]!["mimeType"]!.GetValue<string>(), partes[0]!["inlineData"]!["data"]!.GetValue<string>(), partes[1]!["text"]!.GetValue<string>())
            .Should().Be(("audio/ogg", Convert.ToBase64String(voz), AtendenteDoTelegram.PromptDaTranscricao));
        var pergunta = JsonNode.Parse(_s.Gemini.Pedidos.Last().Corpo)!["contents"]!.AsArray()[^1]!["parts"]![0]!["text"]!.GetValue<string>();
        pergunta.Should().Be("a alvenaria está em 60%", "o que a pessoa falou vai como se tivesse escrito");

        _s.Telegram.UltimaComBotoes.Texto.Should().StartWith("🎤 \"a alvenaria está em 60%\"\n\nAnotei: alvenaria em 60%.\n\n📥 Anotado na caixa da obra Casa da Ana:\n" +
                                                            "• 2 Alvenaria: 40% → 60% concluído");
        _s.NoBanco(b => b.CaixaDeEntrada.Single().Texto).Should().Be("a alvenaria está em 60%");
        _s.NoBanco(b => b.UsosDeIa.Select(u => u.Rotina).ToList()).Should().Equal(["telegram", "telegram"], "a transcrição também gasta créditos do dono");
        _s.Telegram.Digitando.Should().Be(2);
    }

    [Fact]
    public async Task Audio_Longo_Vazio_QueFalhou_EQueNaoChegou()
    {
        await Preparar();
        _s.Telegram.Arquivos["voz"] = [1, 2, 3];

        await MandarAudio("voz", segundos: 600);
        _s.Telegram.Ultima.Should().Be("O áudio passa de 3 minutos. Mande um mais curto (ou escreva).");
        _s.Gemini.Pedidos.Should().BeEmpty();

        _s.Gemini.Resposta = Resposta("\n");
        await MandarAudio("voz");
        _s.Telegram.Ultima.Should().Be(AtendenteDoTelegram.NaoEntendiOAudio);
        _s.Gemini.Pedidos.Should().HaveCount(1, "sem fala, não pergunta nada à IA");

        _s.Gemini.Status = HttpStatusCode.InternalServerError;
        await MandarAudio("voz");
        _s.Telegram.Ultima.Should().Be(AtendenteDoTelegram.NaoEntendiOAudio);

        await MandarAudio("sumiu");
        _s.Telegram.Ultima.Should().Be("Não deu para receber o áudio. Mande de novo.");

        // Um arquivo de áudio (não a voz gravada): o MP3 vai ao Gemini como audio/mp3.
        _s.Gemini.Status = HttpStatusCode.OK;
        _s.Gemini.Resposta = Resposta("");
        await MandarAudio("voz", tipo: "audio/mpeg", campo: "audio");
        JsonNode.Parse(_s.Gemini.Pedidos.Last().Corpo)!["contents"]![0]!["parts"]![0]!["inlineData"]!["mimeType"]!.GetValue<string>().Should().Be("audio/mp3");
    }

    [Fact]
    public async Task Audio_SemAChaveDoGemini_PedeParaEscrever()
    {
        AtendenteDoTelegram.AudioDa(JsonDocument.Parse("""{"voice":{"file_id":"v","duration":4}}""").RootElement)
            .Should().Be(new AudioDoTelegram("v", "audio/ogg", 4, 0));
        AtendenteDoTelegram.AudioDa(JsonDocument.Parse("""{"text":"oi"}""").RootElement).Should().BeNull();

        _s.Dispose();
        using var s = new Servidor(new() { ["Gemini:Chave"] = "" });
        var sessao = await s.CriarConta();
        await AdminTests.TrocarPlano(s, "ana@exemplo.com", Planos.Profissional, Servidor.ChaveDeAdmin);
        var marcar = new HttpRequestMessage(HttpMethod.Put, $"/v1/obras/{Projeto}")
        {
            Content = JsonContent.Create(new { nome = "Casa da Ana", retrato = Retrato() }, options: AtendenteDoTelegram.Json),
        };
        marcar.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessao.Acesso);
        (await s.CreateClient().SendAsync(marcar)).StatusCode.Should().Be(HttpStatusCode.OK);
        var gerar = new HttpRequestMessage(HttpMethod.Post, "/v1/telegram/codigo");
        gerar.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessao.Acesso);
        var codigo = JsonDocument.Parse(await (await s.CreateClient().SendAsync(gerar)).Content.ReadAsStringAsync()).RootElement.GetProperty("codigo").GetString();
        async Task Atender(JsonObject mensagem)
        {
            using var escopo = s.Services.CreateScope();
            await escopo.ServiceProvider.GetRequiredService<AtendenteDoTelegram>()
                .AtenderAsync(JsonDocument.Parse(new JsonObject { ["message"] = mensagem }.ToJsonString()).RootElement, default);
        }
        await Atender(new JsonObject { ["chat"] = new JsonObject { ["id"] = Chat }, ["text"] = $"/start {codigo}" });
        s.Telegram.Arquivos["voz"] = [1, 2, 3];

        await Atender(new JsonObject { ["chat"] = new JsonObject { ["id"] = Chat }, ["voice"] = new JsonObject { ["file_id"] = "voz", ["duration"] = 3 } });

        s.Telegram.Ultima.Should().Be("Ainda não consigo ouvir áudios por aqui: mande a mensagem por escrito.");
        s.Gemini.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task FotoComAtualizacaoNaLegenda_GuardaAFoto_EAnotaOQueElaConta()
    {
        await Preparar();
        _s.Telegram.Arquivos["foto-1"] = [1, 2, 3];
        _s.Gemini.Resposta = RespostaComAtualizacao;

        await Mandar("#atualizacao alvenaria em 60%", foto: "foto-1");

        var itens = _s.NoBanco(b => b.CaixaDeEntrada.OrderBy(i => i.Tipo).Select(i => new { i.Tipo, i.Texto }).ToList());
        itens.Select(i => (i.Tipo, i.Texto)).Should().Equal((ItemDaCaixa.DeAtualizacoes, "alvenaria em 60%"), (ItemDaCaixa.DeFoto, "alvenaria em 60%"));
        JsonNode.Parse(_s.Gemini.Pedidos.Single().Corpo)!["contents"]!.AsArray()[^1]!["parts"]![0]!["text"]!.GetValue<string>().Should().Be("alvenaria em 60%");

        await Mandar("laje concretada", foto: "foto-1");
        _s.Gemini.Pedidos.Should().HaveCount(1, "sem a marca, a legenda só acompanha a foto (não gasta)");
    }

    [Fact]
    public async Task Obra_UmBotaoPorObra_TrocaComUmToque()
    {
        var acesso = await Preparar();
        await Pedir(HttpMethod.Put, $"/v1/obras/{Guid.NewGuid()}", acesso, new { nome = "Galpão", retrato = Retrato() with { Nome = "Galpão" } });
        await Mandar("/obra");
        var lista = _s.Telegram.UltimaComBotoes;

        await Tocar("ob:2", lista.Id, lista.Texto);

        _s.Telegram.Ultima.Should().Be("Obra: Galpão.");
        _s.NoBanco(b => b.Obras.Single(o => o.Id == b.VinculosDoTelegram.Single().ObraAtiva).Nome).Should().Be("Galpão");
        await Tocar("ob:x", lista.Id, lista.Texto);
        _s.Telegram.RespostasAosBotoes.Last().Texto.Should().Be("Botão desconhecido.");
    }
}
