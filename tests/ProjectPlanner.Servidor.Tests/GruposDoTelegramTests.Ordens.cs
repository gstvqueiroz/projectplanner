using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ProjectPlanner.Servidor.Dados;
using ProjectPlanner.Servidor.Servicos;

namespace ProjectPlanner.Servidor.Tests;

// Bloco 59: as ordens de serviço nos grupos: o programa publica, a mensagem leva os botões da situação, a equipe toca
// (aceitar, começar, terminar) e o aviso vai para a caixa; /recusar pede o motivo; /os lista e manda de novo.
public sealed partial class GruposDoTelegramTests
{
    private static readonly Guid Os = Guid.Parse("0a0a0a0a-1111-2222-3333-444444444444");

    private async Task<HttpResponseMessage> Publicar(string situacao, int numero = 14, string titulo = "Alvenaria do térreo", Guid? id = null)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Put, $"/v1/obras/{Projeto}/ordens/{id ?? Os}")
        {
            Content = JsonContent.Create(new { numero, titulo, texto = "Executante: Construtora Silva\nPrazo: 02/10", situacao }),
        };
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _acesso);
        return await _s.CreateClient().SendAsync(pedido);
    }

    // Um toque num botão da mensagem da OS.
    private async Task Tocar(long quem, string nome, string acao, long grupo = Grupo, Guid? os = null)
    {
        var consulta = new JsonObject
        {
            ["id"] = $"toque-{acao}", ["from"] = Pessoa(quem, nome), ["data"] = $"os:{os ?? Os:N}:{acao}",
            ["message"] = new JsonObject { ["message_id"] = 1001, ["chat"] = new JsonObject { ["id"] = grupo, ["type"] = "supergroup" } },
        };
        using var escopo = _s.Services.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<AtendenteDoTelegram>()
            .AtenderAsync(JsonDocument.Parse(new JsonObject { ["update_id"] = 2, ["callback_query"] = consulta }.ToJsonString()).RootElement, default);
    }

    private List<(string Texto, string[] Botoes)> Cartoes() =>
        [.. _s.Telegram.ComBotoes.Where(m => m.Chat == Grupo).Select(m => (m.Texto, m.Botoes.Select(b => b.Texto).ToArray()))];

    [Fact]
    public async Task Publicar_SemGrupo_ZeroGrupos_ComGrupo_MandaComOsBotoes_EAtualizaAMesmaMensagem()
    {
        await Preparar();
        var semGrupo = await Publicar("Emitida");
        (await semGrupo.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("grupos").GetInt32().Should().Be(0);

        await NoGrupo(Dono, "Ana", "/obra 1");
        var publicada = await Publicar("Emitida");

        (await publicada.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("grupos").GetInt32().Should().Be(1);
        var (texto, botoes) = Cartoes().Single();
        texto.Should().StartWith("📋 OS-014 · Alvenaria do térreo\nSituação: Emitida\n\nExecutante: Construtora Silva\nPrazo: 02/10")
            .And.EndWith("Executante: toque em Aceitar (ou Recusar, mandando /recusar 14 e o motivo).");
        botoes.Should().Equal("✅ Aceitar", "❌ Recusar");

        await Publicar("Cancelada");
        var edicao = _s.Telegram.Edicoes.Single();
        (edicao.Chat, edicao.Mensagem, edicao.Botoes.Count).Should().Be((Grupo, _s.Telegram.ComBotoes.Single().Id, 0));
        edicao.Texto.Should().Contain("Situação: Cancelada");
        Cartoes().Should().HaveCount(1, "a mesma mensagem foi editada");
    }

    [Fact]
    public async Task Publicar_Validacoes()
    {
        await Preparar();
        (await Publicar("Voando")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Publicar("Emitida", titulo: " ")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await AdminTests.TrocarPlano(_s, "ana@exemplo.com", Planos.Gratis, Servidor.ChaveDeAdmin);
        (await Publicar("Emitida")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Botoes_AceitarComecarTerminar_MudamASituacao_VaoParaACaixa_EAtualizamOCartao()
    {
        await Ligar();
        await Publicar("Emitida");

        await Tocar(Joao, "João", "aceitar");

        _s.Telegram.RespostasAosBotoes.Last().Should().Be(("toque-aceitar", "OS-014: aceitou. Avisado à engenharia."));
        DoGrupo().Last().Should().Be("✅ João aceitou a OS-014 (Alvenaria do térreo). A engenharia vê no programa.");
        var edicao = _s.Telegram.Edicoes.Last();
        edicao.Texto.Should().Contain("Situação: Aceita (João)");
        edicao.Botoes.Select(b => b.Texto).Should().Equal("▶️ Comecei", "🏁 Terminei");

        await Tocar(Joao, "João", "aceitar");
        _s.Telegram.RespostasAosBotoes.Last().Texto.Should().Be("A OS-014 já está \"aceita\".");
        await Tocar(Joao, "João", "iniciar");
        await Tocar(Joao, "João", "concluir");

        _s.Telegram.Edicoes.Last().Botoes.Should().BeEmpty("concluída espera a conferência");
        var caixa = _s.NoBanco(b => b.CaixaDeEntrada.Where(i => i.Tipo == ItemDaCaixa.DeOrdem).OrderBy(i => i.CriadoEm).ToList());
        caixa.Select(i => i.Texto).Should().Equal("João aceitou a OS-014", "João começou a OS-014", "João terminou a OS-014");
        var evento = JsonSerializer.Deserialize<EventoDaOrdemNoTelegram>(caixa[0].Conteudo!, AtendenteDoTelegram.Json)!;
        (evento.Ordem, evento.Acao, evento.Autor).Should().Be((Os, "aceitar", "João"));
        _s.NoBanco(b => b.OrdensNoTelegram.Single().Situacao).Should().Be(OrdemNoTelegram.Concluida);
        _s.Gemini.Pedidos.Should().BeEmpty("os botões não usam a IA");

        // A caixa entrega o aviso da OS no campo "ordem" (e não como atualizações).
        var pedido = new HttpRequestMessage(HttpMethod.Get, $"/v1/obras/{Projeto}/caixa");
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _acesso);
        var itens = await (await _s.CreateClient().SendAsync(pedido)).Content.ReadFromJsonAsync<JsonElement>();
        var primeiro = itens.EnumerateArray().First(i => i.GetProperty("tipo").GetString() == ItemDaCaixa.DeOrdem);
        (primeiro.GetProperty("ordem").GetProperty("acao").GetString(), primeiro.GetProperty("atualizacoes").ValueKind).Should().Be(("aceitar", JsonValueKind.Null));
    }

    [Fact]
    public async Task Recusar_PedeOMotivo_ComOComando_EQuemSoAcompanhaNaoMexe()
    {
        await Ligar();
        await Publicar("Emitida");
        await Tocar(Joao, "João", "recusar");
        _s.NoBanco(b => b.OrdensNoTelegram.Single().Situacao).Should().Be(OrdemNoTelegram.Emitida, "recusar pede o motivo");
        DoGrupo().Last().Should().StartWith("Para recusar a OS-014, mande /recusar 14 e o motivo");

        await NoGrupo(Dono, "Ana", "/papel acompanhar", respondendo: Pessoa(Bia, "Bia"));
        await Tocar(Bia, "Bia", "aceitar");
        _s.Telegram.RespostasAosBotoes.Last().Texto.Should().StartWith("Quem só acompanha a obra não mexe nas OS.");

        await NoGrupo(Joao, "João", "/recusar 14");
        DoGrupo().Last().Should().StartWith("Para recusar uma OS, mande /recusar, o número e o motivo");
        await NoGrupo(Joao, "João", "/recusar 14 o preço não cobre o material");

        _s.NoBanco(b => b.OrdensNoTelegram.Single().Situacao).Should().Be(OrdemNoTelegram.Recusada);
        DoGrupo().Last().Should().Be("❌ João recusou a OS-014 (Alvenaria do térreo): o preço não cobre o material. A engenharia vê no programa.");
        _s.NoBanco(b => b.CaixaDeEntrada.Single(i => i.Tipo == ItemDaCaixa.DeOrdem).Texto).Should().Be("João recusou a OS-014: o preço não cobre o material");
    }

    [Fact]
    public async Task ComandoOs_ListaAsAbertas_EMandaUmaDeNovo_EBotaoDeOutroGrupoNaoVale()
    {
        await Ligar();
        await Publicar("Emitida");
        await Publicar("Conferida", numero: 3, titulo: "Fundação", id: Guid.NewGuid());

        await NoGrupo(Joao, "João", "/os");
        DoGrupo().Last().Should().Be("OS abertas:\n• OS-014 · Alvenaria do térreo: emitida\n\nPara ver uma com os botões, mande /os e o número (por exemplo, /os 14).");

        await NoGrupo(Joao, "João", "/os 14");
        Cartoes().Should().HaveCount(2, "mandou a OS de novo");
        await Publicar("Aceita");
        _s.Telegram.Edicoes.Last().Mensagem.Should().Be(_s.Telegram.ComBotoes.Last().Id, "passa a atualizar a mensagem nova");

        await Tocar(Joao, "João", "concluir", grupo: -999);
        _s.Telegram.RespostasAosBotoes.Last().Texto.Should().Be("Esta OS não está mais ligada a este grupo.");
    }
}
