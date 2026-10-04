using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Tests;

// Bloco 24: o assistente altera o cronograma pelo chat (aplicação automática, Desfazer, nova tentativa com o erro).
public class AssistenteAlteracoesTests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;

    public AssistenteAlteracoesTests()
    {
        _vm = _ambiente.CriarViewModel();
        _ambiente.Dialogos.ArquivoParaSalvar = "/obra.pplan";
        _vm.SalvarProjetoCommand.Execute(null);
    }

    private AssistenteViewModel Painel => _vm.Assistente;

    private static AlteracaoDoCronograma Criar(string referencia, string nome, int? duracao = null, string? pai = null) =>
        new(AcoesNoCronograma.Criar, Ref: referencia, Nome: nome, Duracao: duracao, Pai: pai);

    private static AlteracaoDoCronograma Vincular(string de, string para) => new(AcoesNoCronograma.Vincular, De: de, Para: para);

    // Uma casa pequena: 2 etapas, 4 tarefas, 1 marco e os vínculos.
    private static readonly AlteracaoDoCronograma[] Casa =
    [
        new(AcoesNoCronograma.Projeto, Nome: "Residência 100 m²"),
        Criar("n1", "Fundação"), Criar("n2", "Escavação", 3, "n1"), Criar("n3", "Sapatas e baldrames", 5, "n1"),
        Criar("n4", "Estrutura"), Criar("n5", "Pilares e vigas", 8, "n4"), Criar("n6", "Laje", 6, "n4"),
        Criar("n7", "Estrutura concluída", 0),
        Vincular("n2", "n3"), Vincular("n3", "n5"), Vincular("n5", "n6"), Vincular("n6", "n7"),
    ];

    private void Responder(string texto, params AlteracaoDoCronograma[] alteracoes) =>
        _ambiente.Assistente.ResponderComAlteracoes = _ => new RespostaIA(texto) { Alteracoes = alteracoes };

    private void Pedir(string texto)
    {
        Painel.TextoDigitado = texto;
        Painel.EnviarCommand.Execute(null);
    }

    private IEnumerable<string> Linhas => _vm.Tarefas.Select(l => $"{l.Edt} {l.Nome}");

    [Fact]
    public void PedirUmCronograma_AplicaNaHora_EMostraOQueMudou()
    {
        Responder("Montei o cronograma da residência com 2 etapas.", Casa);

        Pedir("Monte o cronograma de uma residência de 100 m²");

        Linhas.Should().Equal("1 Fundação", "1.1 Escavação", "1.2 Sapatas e baldrames", "2 Estrutura", "2.1 Pilares e vigas",
            "2.2 Laje", "3 Estrutura concluída");
        (_vm.NomeProjeto, _vm.PossuiAlteracoesNaoSalvas, _vm.Mensagem).Should().Be(
            ("Residência 100 m²", true, "Assistente: 7 tarefas criadas · 4 vínculos criados · projeto alterado."));
        _vm.TerminoProjeto.Should().Be(Ambiente.DiaUtil(21), "3 + 5 + 8 + 6 dias úteis em sequência, a partir de seg 28/09");
        _vm.Tarefas.Single(l => l.Nome == "Laje").Predecessoras.Should().Be("5");
        var resposta = Painel.Mensagens[^1];
        (resposta.Texto, resposta.TemAlteracoes, resposta.Alteracoes!.Texto, resposta.Alteracoes.PodeDesfazer).Should().Be(
            ("Montei o cronograma da residência com 2 etapas.", true,
             "Cronograma alterado: 7 tarefas criadas · 4 vínculos criados · projeto alterado", true));
        _ambiente.Assistente.Pedidos.Should().ContainSingle();
    }

    [Fact]
    public void RespostaSemAlteracoes_NaoMexeNoCronograma()
    {
        Ambiente.AdicionarTarefa(_vm, "Fundação", 3);
        _vm.SalvarProjetoCommand.Execute(null);
        Responder("O caminho crítico é a Fundação.");

        Pedir("Qual é o caminho crítico?");

        (Painel.Mensagens[^1].TemAlteracoes, _vm.PossuiAlteracoesNaoSalvas).Should().Be((false, false));
    }

    [Fact]
    public void Desfazer_VoltaAoCronogramaDeAntes()
    {
        Ambiente.AdicionarTarefa(_vm, "Tarefa minha", 2);
        Responder("Refiz tudo.", [new AlteracaoDoCronograma(AcoesNoCronograma.Limpar), .. Casa]);
        Pedir("Refaça o cronograma");
        Linhas.Should().HaveCount(7);
        var alteracoes = Painel.Mensagens[^1].Alteracoes!;

        Painel.DesfazerCommand.Execute(alteracoes);

        Linhas.Should().Equal("1 Tarefa minha");
        (_vm.NomeProjeto, _vm.Mensagem).Should().Be(("Projeto sem título", "Alterações do assistente desfeitas."));
        (alteracoes.Desfeitas, alteracoes.PodeDesfazer, alteracoes.Texto).Should().Be(
            (true, false, "Desfeito: 7 tarefas criadas · 1 tarefa removida · 4 vínculos criados · projeto alterado"));
        _ambiente.Dialogos.Confirmacoes.Should().BeEmpty("nada mudou depois da resposta");
    }

    [Fact]
    public void Desfazer_DepoisDeMudarAMao_PerguntaAntes()
    {
        Responder("Pronto.", Casa);
        Pedir("Monte o cronograma");
        var alteracoes = Painel.Mensagens[^1].Alteracoes!;
        _vm.Tarefas.Single(l => l.Nome == "Laje").Duracao = 10;
        _ambiente.Dialogos.Confirmar = false;

        Painel.DesfazerCommand.Execute(alteracoes);

        _ambiente.Dialogos.Confirmacoes.Last().Should().StartWith("O cronograma mudou depois desta resposta do assistente.");
        (Linhas.Count(), alteracoes.PodeDesfazer).Should().Be((7, true), "cancelou");

        _ambiente.Dialogos.Confirmar = true;
        Painel.DesfazerCommand.Execute(alteracoes);
        Linhas.Should().BeEmpty();
    }

    [Fact]
    public void SoAUltimaAplicacaoPodeSerDesfeita()
    {
        Responder("Etapa 1.", Criar("n1", "Fundação", 3));
        Pedir("Crie a fundação");
        Responder("Etapa 2.", Criar("n1", "Estrutura", 5), Vincular("1", "n1"));
        Pedir("Crie a estrutura depois dela");

        var (primeira, segunda) = (Painel.Mensagens[1].Alteracoes!, Painel.Mensagens[3].Alteracoes!);
        (primeira.PodeDesfazer, segunda.PodeDesfazer).Should().Be((false, true));

        Painel.DesfazerCommand.Execute(primeira);
        Linhas.Should().HaveCount(2, "a primeira não pode mais ser desfeita");

        Painel.DesfazerCommand.Execute(segunda);
        Linhas.Should().Equal("1 Fundação");
        (primeira.PodeDesfazer, segunda.PodeDesfazer).Should().Be((false, false), "desfazer não volta duas respostas");
    }

    [Fact]
    public void AlteracaoInvalida_OAssistenteRecebeOErro_ECorrigeSozinho()
    {
        var tentativas = 0;
        _ambiente.Assistente.ResponderComAlteracoes = pedido => ++tentativas == 1
            ? new RespostaIA("Montei.") { Alteracoes = [Criar("n1", "Fundação", 3), Vincular("n1", "n9")] }
            : new RespostaIA("Corrigi o vínculo.") { Alteracoes = [Criar("n1", "Fundação", 3), Criar("n2", "Estrutura", 5), Vincular("n1", "n2")] };

        Pedir("Monte a fundação e a estrutura");

        Linhas.Should().Equal("1 Fundação", "2 Estrutura");
        var pedidos = _ambiente.Assistente.Pedidos;
        pedidos.Should().HaveCount(2);
        pedidos[1].Mensagem.Should().StartWith("As alterações da sua resposta anterior não foram aplicadas, e o cronograma não mudou. " +
                                               "Motivo: Alteração 2 (vincular n1 → n9): a tarefa \"n9\" (campo para) não existe");
        pedidos[1].Historico.Select(m => (m.Autor, m.Texto)).Should().Equal(
            (AutorMensagem.Usuario, "Monte a fundação e a estrutura"), (AutorMensagem.Assistente, "Montei."));
        Painel.Mensagens.Select(m => m.Texto).Should().Equal("Monte a fundação e a estrutura", "Corrigi o vínculo.");
        Painel.Mensagens[^1].Alteracoes!.Aplicadas.Should().BeTrue();
    }

    [Fact]
    public void AlteracaoInvalidaDuasVezes_NadaMuda_EOMotivoAparece()
    {
        Ambiente.AdicionarTarefa(_vm, "Fundação", 3);
        _vm.SalvarProjetoCommand.Execute(null);
        Responder("Liguei o resumo.", new AlteracaoDoCronograma(AcoesNoCronograma.Alterar, Tarefa: "1", Duracao: -1));

        Pedir("Mude a duração");

        Linhas.Should().Equal("1 Fundação");
        (_vm.PossuiAlteracoesNaoSalvas, _ambiente.Assistente.Pedidos.Count).Should().Be((false, 2));
        var alteracoes = Painel.Mensagens[^1].Alteracoes!;
        (alteracoes.Aplicadas, alteracoes.PodeDesfazer).Should().Be((false, false));
        alteracoes.Texto.Should().StartWith("Nada foi alterado. Alteração 1 (alterar 1): ");
    }

    [Fact]
    public void FalhaNaCorrecao_FicaAPrimeiraRespostaComOMotivo()
    {
        var tentativas = 0;
        _ambiente.Assistente.ResponderComAlteracoes = _ => ++tentativas == 1
            ? new RespostaIA("Montei.") { Alteracoes = [Vincular("1", "2")] }
            : throw new ErroDoAssistenteException("Sem conexão com o Gemini.");

        Pedir("Ligue as tarefas");

        (Painel.Mensagens[^1].Texto, Painel.Mensagens[^1].EhErro).Should().Be(("Montei.", false));
        Painel.Mensagens[^1].Alteracoes!.Texto.Should().StartWith("Nada foi alterado. Alteração 1 (vincular 1 → 2)");
    }

    [Fact]
    public async Task RemoverTarefaComFotos_PerguntaAntes_ECancelarNaoMudaNada()
    {
        var linha = Ambiente.AdicionarTarefa(_vm, "Fundação", 3);
        _vm.GerenciarFotosCommand.Execute(linha);
        await _ambiente.Dialogos.Galerias.Last().AdicionarArquivosAsync(["/fotos/a.jpg", "/fotos/b.jpg"]);
        Responder("Refiz.", new AlteracaoDoCronograma(AcoesNoCronograma.Limpar), Criar("n1", "Nova fundação", 4));
        _ambiente.Dialogos.Confirmar = false;

        Pedir("Refaça o cronograma");

        _ambiente.Dialogos.Confirmacoes.Last().Should().Be(
            "As alterações do assistente removem a tarefa \"Fundação\", que tem 2 fotos. Aplicar mesmo assim?");
        Linhas.Should().Equal("1 Fundação");
        Painel.Mensagens[^1].Alteracoes!.Texto.Should().Be("Alterações não aplicadas: elas removeriam tarefas com fotos.");
        _ambiente.Assistente.Pedidos.Should().ContainSingle("cancelar não é erro: não há nova tentativa");
    }

    [Fact]
    public async Task RemoverTarefaComFotos_Confirmado_EDesfazerTrazAsFotosDeVolta()
    {
        var linha = Ambiente.AdicionarTarefa(_vm, "Fundação", 3);
        _vm.GerenciarFotosCommand.Execute(linha);
        await _ambiente.Dialogos.Galerias.Last().AdicionarArquivosAsync(["/fotos/a.jpg"]);
        Responder("Refiz.", new AlteracaoDoCronograma(AcoesNoCronograma.Limpar), Criar("n1", "Nova fundação", 4));

        Pedir("Refaça o cronograma");
        Linhas.Should().Equal("1 Nova fundação");

        Painel.DesfazerCommand.Execute(Painel.Mensagens[^1].Alteracoes);

        var fundacao = _vm.Tarefas.Single();
        (fundacao.Nome, fundacao.QuantidadeDeFotos).Should().Be(("Fundação", 1));
        _vm.Fotos.Imagem(fundacao.Tarefa.Fotos.Single().Id).Should().NotBeNull("a imagem ainda não salva continua na memória");
    }

    [Fact]
    public void ResumoRecolhidoESelecao_SobrevivemAAplicacao()
    {
        Responder("Pronto.", Casa);
        Pedir("Monte");
        var estrutura = _vm.Tarefas.Single(l => l.Nome == "Estrutura");
        estrutura.Expandido = false;
        _vm.TarefaSelecionada = _vm.Tarefas.Single(l => l.Nome == "Escavação");
        Responder("Ajustei.", new AlteracaoDoCronograma(AcoesNoCronograma.Alterar, Tarefa: "2", Duracao: 4));

        Pedir("Escavação com 4 dias");

        _vm.Tarefas.Single(l => l.Nome == "Estrutura").Expandido.Should().BeFalse();
        _vm.LinhasVisiveis.Select(l => l.Nome).Should().NotContain("Laje");
        _vm.TarefaSelecionada!.Nome.Should().Be("Escavação");
        _vm.TarefaSelecionada.Duracao.Should().Be(4);
    }

    [Fact]
    public void AbrirOutroProjeto_NaoDaMaisParaDesfazer()
    {
        Responder("Pronto.", Casa);
        Pedir("Monte");
        var alteracoes = Painel.Mensagens[^1].Alteracoes!;
        _vm.SalvarProjetoCommand.Execute(null);

        _vm.NovoProjetoCommand.Execute(null);

        _vm.PodeDesfazer(alteracoes.Resultado.Marca!).Should().BeFalse();
        Painel.Mensagens.Should().BeEmpty("a conversa era sobre o projeto anterior");
    }

    [Fact]
    public async Task ConversaLimpaDuranteAEspera_NaoAplicaNada()
    {
        _ambiente.Assistente.Segurar();
        Responder("Pronto.", Casa);
        Painel.TextoDigitado = "Monte";
        var envio = Painel.EnviarCommand.ExecuteAsync(null);

        Painel.LimparConversaCommand.Execute(null);
        _ambiente.Assistente.Soltar();
        await envio;

        Linhas.Should().BeEmpty();
    }

    [Fact]
    public void RespostaSoComAlteracoesVazias_DeixaOProjetoComoEsta()
    {
        Responder("Nada a mudar.", new AlteracaoDoCronograma(AcoesNoCronograma.Limpar));

        Pedir("Limpe o que houver");

        (_vm.PossuiAlteracoesNaoSalvas, Painel.Mensagens[^1].Alteracoes!.Texto).Should().Be((false, "Nenhuma mudança no cronograma."));
    }
}
