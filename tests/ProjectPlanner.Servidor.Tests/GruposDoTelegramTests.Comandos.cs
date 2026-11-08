using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ProjectPlanner.Servidor.Servicos;

namespace ProjectPlanner.Servidor.Tests;

// Bloco 83: no grupo da obra, a ajuda com os assuntos em botões (com a dica da marca #atualizacao), as consultas sem a IA para
// todos os papéis, o /caixa só com o que veio do grupo e o Desfazer só de quem mandou ou de quem é da engenharia.
public sealed partial class GruposDoTelegramTests
{
    // Um toque num botão de uma mensagem do bot no grupo.
    private async Task TocarNoGrupo(long quem, string nome, string dados, long mensagem = 7000, string texto = "…")
    {
        var consulta = new JsonObject
        {
            ["id"] = $"toque-{quem}-{dados}", ["data"] = dados, ["from"] = Pessoa(quem, nome),
            ["message"] = new JsonObject { ["message_id"] = mensagem, ["text"] = texto, ["chat"] = new JsonObject { ["id"] = Grupo, ["type"] = "supergroup" } },
        };
        using var escopo = _s.Services.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<AtendenteDoTelegram>()
            .AtenderAsync(JsonDocument.Parse(new JsonObject { ["update_id"] = 4, ["callback_query"] = consulta }.ToJsonString()).RootElement, default);
    }

    private (long Chat, long Id, string Texto, IReadOnlyList<BotaoDoTelegram> Botoes) UltimaComBotoesDoGrupo() =>
        _s.Telegram.ComBotoes.Last(m => m.Chat == Grupo);

    [Fact]
    public async Task Ajuda_NoGrupo_ComBotoes_ADicaDaMarca_EOsAtalhos()
    {
        await Ligar();
        var depoisDeLigar = UltimaComBotoesDoGrupo();
        depoisDeLigar.Texto.Should().StartWith("👷 Assistente da obra neste grupo\nObra: Casa da Ana\n\n" +
                                                $"O bot só lê o que é para ele: fotos, mensagens com #atualizacao, mensagens que o marcam (@{Bot}) e respostas a ele.");

        await NoGrupo(Joao, "João", $"/ajuda@{Bot}");
        var menu = UltimaComBotoesDoGrupo();
        (menu.Id != depoisDeLigar.Id, menu.Texto).Should().Be((true, depoisDeLigar.Texto));

        await TocarNoGrupo(Joao, "João", "aj:andamento", menu.Id);
        _s.Telegram.Edicoes.Last().Texto.Should().StartWith("📈 Andamento das tarefas")
            .And.EndWith($"No grupo, comece a mensagem com #atualizacao ou marque @{Bot}.");
        await TocarNoGrupo(Joao, "João", "aj:comandos", menu.Id);
        _s.Telegram.Edicoes.Last().Texto.Should().StartWith("⌨️ Comandos do grupo").And.Contain("/os — as ordens de serviço abertas");
        await TocarNoGrupo(Joao, "João", "aj:menu", menu.Id);
        _s.Telegram.Edicoes.Last().Texto.Should().Be(menu.Texto, "a obra do grupo volta no começo");

        await TocarNoGrupo(Joao, "João", "cmd:hoje", menu.Id);
        DoGrupo().Last().Should().StartWith("📅 Hoje, domingo 18/10 — Casa da Ana\nHoje não é dia de trabalho no calendário da obra.");
        _s.Gemini.Pedidos.Should().BeEmpty("nada disso usa a IA");
    }

    [Fact]
    public async Task Consultas_ParaTodos_CaixaSoDoGrupo_DesfazerSoDoAutorOuDaEngenharia()
    {
        await Ligar();
        _s.Gemini.Resposta = Resposta("""{"mensagem":"Anotei.","alteracoes":[],"atualizacoes":[{"tipo":"percentual","tarefa":"1","percentual":70}]}""");
        // A Ana manda uma pelo chat privado; o João, uma no grupo.
        await Atender(new JsonObject { ["chat"] = new JsonObject { ["id"] = Dono, ["type"] = "private" }, ["from"] = Pessoa(Dono, "Ana"), ["text"] = "alvenaria em 65%" });
        await NoGrupo(Joao, "João", "#atualizacao alvenaria em 70%");
        var anotado = UltimaComBotoesDoGrupo();
        await NoGrupo(Dono, "Ana", "/papel acompanhar", respondendo: Pessoa(Bia, "Bia"));

        await NoGrupo(Bia, "Bia", "/resumo");
        DoGrupo().Last().Should().StartWith("📊 Casa da Ana — 18/10/2026", "quem só acompanha também consulta");
        await NoGrupo(Bia, "Bia", "/caixa");
        DoGrupo().Last().Should().Be("📥 Na caixa da obra Casa da Ana, esperando o programa: 1\n" +
                                     "• 18/10 09:00 · \"João: alvenaria em 70%\" (1 atualização)\n\n" +
                                     "O dono traz tudo para o programa e aplica. /desfazer tira da caixa a última coisa que você mandou.",
            "só o que veio do grupo (o do chat privado da Ana não aparece aqui)");

        await TocarNoGrupo(Bia, "Bia", anotado.Botoes.Single().Dados, anotado.Id, anotado.Texto);
        _s.Telegram.RespostasAosBotoes.Last().Texto.Should().Be("Só quem mandou (ou quem é da engenharia) desfaz.");
        _s.NoBanco(b => b.CaixaDeEntrada.Count()).Should().Be(2);

        await TocarNoGrupo(Dono, "Ana", anotado.Botoes.Single().Dados, anotado.Id, anotado.Texto);
        _s.Telegram.Edicoes.Last().Texto.Should().Be(anotado.Texto + "\n\n↩️ Desfeito por Ana: saiu da caixa.");
        _s.NoBanco(b => b.CaixaDeEntrada.Single().ChatId).Should().Be(Dono, "o do chat privado continua");

        await NoGrupo(Joao, "João", "/desfazer");
        DoGrupo().Last().Should().Be(AtendenteDoTelegram.NadaParaDesfazer);
        _s.Gemini.Pedidos.Should().HaveCount(2, "só as duas mensagens com atualização usaram a IA");
    }
}
