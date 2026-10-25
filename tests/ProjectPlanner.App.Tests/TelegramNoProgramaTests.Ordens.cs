using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core.OrdensDeServico;
using ProjectPlanner.IA.Conta;

namespace ProjectPlanner.App.Tests;

// Bloco 59: as OS no Telegram, no programa: mandar ao grupo (com ou sem os valores), cada passo seguinte vai junto, e o que a
// equipe fez pelos botões volta pela caixa e entra na OS, com o nome de quem fez.
public sealed partial class TelegramNoProgramaTests
{
    private async Task<OrdensDeServicoViewModel> AbrirOrdens(bool obraNoTelegram = true)
    {
        _ambiente.Conta.Conectar(plano: "profissional");
        if (obraNoTelegram)
            await _vm.UsarObraNoTelegramCommand.ExecuteAsync(null);
        _vm.TarefaSelecionada = _vm.Tarefas[0];
        _vm.OrdensDeServicoCommand.Execute(null);
        var tela = _ambiente.Dialogos.OrdensDeServico.Last();
        tela.NovaOrdemCommand.Execute(null);
        tela.Executante = "Construtora Silva";
        tela.Itens[0].Preco = "1.200";
        return tela;
    }

    [Fact]
    public async Task ObraForaDoTelegram_SemBotao()
    {
        var tela = await AbrirOrdens(obraNoTelegram: false);
        tela.EmitirCommand.Execute(null);

        (tela.TemTelegram, tela.PodeMandarAoTelegram).Should().Be((false, false));
    }

    [Fact]
    public async Task Mandar_SoDepoisDeEmitir_ComOsValores_EOsPassosSeguintesVaoJunto()
    {
        var tela = await AbrirOrdens();
        tela.PodeMandarAoTelegram.Should().BeFalse("rascunho não vai ao grupo");
        tela.EmitirCommand.Execute(null);

        await tela.MandarAoTelegramCommand.ExecuteAsync(null);

        var (ordem, numero, titulo, texto, situacao) = _ambiente.Telegram.Publicadas.Single();
        (ordem, numero, titulo, situacao).Should().Be((tela.Ordem!.Id, 1, "Fundação", "Emitida"));
        texto.Should().Be("Executante: Construtora Silva (empreiteiro)\nInício: 28/09/2026 · prazo: 02/10/2026\nPagamento: Empreitada (preço fechado)\n\n" +
                          "Itens:\n• Fundação: 1,00 vb × R$ 1.200,00 = R$ 1.200,00\nTotal: R$ 1.200,00");
        tela.Ordem!.NoTelegram.Should().BeTrue();
        tela.Mensagem.Should().StartWith("OS-001 mandada ao grupo da obra no Telegram");

        tela.ValoresNoTelegram = false;
        tela.AceitarCommand.Execute(null);

        _ambiente.Telegram.Publicadas.Last().Situacao.Should().Be("Aceita");
        _ambiente.Telegram.Publicadas.Last().Texto.Should().NotContain("R$").And.Contain("• Fundação: 1,00 vb");
    }

    [Fact]
    public async Task SemGrupo_Avisa_ENaoMarca()
    {
        var tela = await AbrirOrdens();
        tela.EmitirCommand.Execute(null);
        _ambiente.Telegram.Grupos = 0;

        await tela.MandarAoTelegramCommand.ExecuteAsync(null);

        (tela.Ordem!.NoTelegram, tela.Mensagem).Should().Be((false,
            "A obra ainda não tem um grupo no Telegram: crie um grupo com a equipe, ponha o bot nele e mande /obra lá. Depois, mande a OS de novo."));
    }

    [Fact]
    public async Task BuscarDoTelegram_AplicaOsPassosDaEquipe_NaOrdem_EIgnoraOQueNaoValeMais()
    {
        var tela = await AbrirOrdens();
        tela.EmitirCommand.Execute(null);
        var os = tela.Ordem!.Id;
        var quando = new DateTime(2026, 9, 28, 15, 0, 0, DateTimeKind.Utc);
        ItemDoTelegram Passo(string acao, int minutos, string? motivo = null) => new(Guid.NewGuid(), ItemDoTelegram.DeOrdem, quando.AddMinutes(minutos),
            $"João {acao}", null, null, null, new EventoDeOrdemDoTelegram(os, acao, "João", quando.AddMinutes(minutos), motivo));
        _ambiente.Telegram.Caixas[Projeto] = [Passo("iniciar", 5), Passo("aceitar", 1), Passo("aceitar", 9), Passo("concluir", 20)];

        await _vm.BuscarDoTelegramCommand.ExecuteAsync(null);

        var ordem = _vm.Cronograma.OrdensDeServico.Obter(os)!;
        ordem.Situacao.Should().Be(SituacaoDaOrdem.Concluida);
        ordem.Historico.Skip(2).Select(e => (e.Descricao, e.Autor)).Should().Equal(
            ("Aceita", "João, pelo Telegram"), ("Iniciada", "João, pelo Telegram"), ("Concluída", "João, pelo Telegram"));
        ordem.Historico[2].Quando.Should().Be(quando.AddMinutes(1).ToLocalTime());
        _vm.Mensagem.Should().Be("Ordens de serviço pelo Telegram: OS-001 aceita (João), OS-001 em execução (João), OS-001 concluída (João).");
        (_ambiente.Telegram.Caixas[Projeto].Count, _vm.PendentesDoTelegram).Should().Be((0, 0), "todos foram levados, até o que ficou de lado");
        _vm.TextoDesfazer.Should().StartWith("Desfazer Telegram: OS-001 aceita (João)");
    }

    // ---- Bloco 62 ----

    [Fact]
    public async Task FotoComAOSNaLegenda_VaiParaATarefaDaOS_ComALegenda()
    {
        var tela = await AbrirOrdens(); // a OS-001 é da Fundação
        tela.EmitirCommand.Execute(null);
        var (comOs, semOs) = (Guid.NewGuid(), Guid.NewGuid());
        _ambiente.Telegram.Caixas[Projeto] =
        [
            new ItemDoTelegram(comOs, ItemDoTelegram.DeFoto, DateTime.UtcNow, "João: gabarito pronto #os1", "telegram-1.jpg", null, null),
            new ItemDoTelegram(semOs, ItemDoTelegram.DeFoto, DateTime.UtcNow, "Bia: vista geral", "telegram-2.jpg", null, null),
        ];
        _ambiente.Telegram.Fotos[comOs] = [1, 2, 3];
        _ambiente.Telegram.Fotos[semOs] = [4, 5, 6];

        await _vm.BuscarDoTelegramCommand.ExecuteAsync(null);

        var lote = _ambiente.Dialogos.FotosDoProjeto.Single().Lote;
        var daOs = lote.Single(l => l.Arquivo == "telegram-1.jpg");
        (daOs.TarefaEscolhida.Id, daOs.TarefaFixada, daOs.Legenda, daOs.Situacao)
            .Should().Be((IdDe("Fundação"), true, "João: gabarito pronto #os1", "Da OS-001, citada na legenda."));
        var outra = lote.Single(l => l.Arquivo == "telegram-2.jpg");
        (outra.TarefaFixada, outra.Legenda).Should().Be((false, "Bia: vista geral"));
    }
}
