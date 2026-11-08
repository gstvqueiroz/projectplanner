using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Diario;
using ProjectPlanner.Core.Recursos;
using ProjectPlanner.Core.Relatorios;
using SkiaSharp;

namespace ProjectPlanner.App.Tests;

// Bloco 81: Projeto > Diário de obra… (o RDO): o dia novo já preenchido pelo cronograma; o formulário salvo ao trocar de dia e
// ao fechar a tela (um número que não vale segura a troca); fechar e reabrir; o % levado ao cronograma; o dia impraticável no
// calendário; o PDF com as fotos do dia; cada mudança é um passo do Desfazer. "Hoje" é segunda 28/09/2026; Escavação vai de
// 28 a 30/09 (2 pedreiros por dia) e Formas, depois dela, de 01 a 02/10.
public sealed class DiarioDeObraTelaTests : IDisposable
{
    private static readonly DateOnly D28 = new(2026, 9, 28), D29 = new(2026, 9, 29);

    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-rdo-" + Guid.NewGuid().ToString("N"));
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly TarefaViewModel _escavacao;

    public DiarioDeObraTelaTests()
    {
        Directory.CreateDirectory(_pasta);
        _vm = _ambiente.CriarViewModel();
        _escavacao = Ambiente.AdicionarTarefa(_vm, "Escavação", 3);
        Ambiente.AdicionarTarefa(_vm, "Formas", 2, "1");
        var pedreiro = new Recurso(Guid.NewGuid(), "Pedreiro", TipoDeRecurso.Profissional, "dia", 200, fornecedor: "Empreiteira X");
        _vm.Cronograma.Recursos.Adicionar(pedreiro);
        _vm.Cronograma.Recursos.Atribuir(_escavacao.Tarefa, [new AtribuicaoDeRecurso(pedreiro.Id, 2)]);
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private async Task<DiarioDeObraViewModel> Abrir()
    {
        await _vm.DiarioDeObraCommand.ExecuteAsync(null);
        return _ambiente.Dialogos.Diarios[^1];
    }

    private static Escolha<TempoNoPeriodo> Tempo(DiarioDeObraViewModel tela, TempoNoPeriodo tempo) => tela.Tempos.Single(t => t.Valor == tempo);

    [Fact]
    public async Task NovoDia_VemPreenchidoPeloCronograma_EUmPassoDoDesfazer()
    {
        var tela = await Abrir();
        (tela.SemDias, tela.TemDia, tela.DataDoNovo).Should().Be((true, false, (DateTime?)new DateTime(2026, 9, 28)), "hoje, que ainda não está no diário");

        tela.NovoDiaCommand.Execute(null);

        tela.Dias.Select(d => (d.Texto, d.Resumo)).Should().Equal(("RDO 001 · 28/09/2026 · seg", "2 pessoas · aberto"));
        (tela.Selecionado!.Data, tela.Cabecalho, tela.PodeEditar, tela.SemDias).Should().Be((D28, "RDO nº 001 · 28/09/2026 · segunda-feira", true, false));
        tela.Atividades.Select(a => (a.Descricao, a.Percentual, a.DoCronograma)).Should().Equal(("1 Escavação", "0", true));
        tela.Efetivo.Select(e => (e.Funcao, e.Quantidade, e.Empresa)).Should().Equal(("Pedreiro", "2", "Empreiteira X"));
        (tela.TempoDaManha!.Valor, tela.ManhaPraticavel, tela.TempoDaTarde!.Valor, tela.Impraticavel).Should().Be((TempoNoPeriodo.Bom, true, TempoNoPeriodo.Bom, false));
        tela.Mensagem.Should().Be("RDO nº 001 criado com o que o cronograma prevê para 28/09/2026: ajuste o que mudou.");
        tela.DataDoNovo.Should().Be(new DateTime(2026, 9, 29), "o próximo dia já fica escolhido");
        (_vm.TextoDesfazer, _vm.PossuiAlteracoesNaoSalvas).Should().Be(("Desfazer Diário: novo dia 28/09/2026", true));

        tela.NovoDiaCommand.Execute(null);
        tela.DataDoNovo = new DateTime(2026, 9, 28);
        tela.NovoDiaCommand.Execute(null);
        tela.Mensagem.Should().Be("O diário já tem o dia 28/09/2026.");
        tela.Dias.Select(d => d.Texto).Should().Equal("RDO 001 · 28/09/2026 · seg", "RDO 002 · 29/09/2026 · ter");

        _vm.DesfazerAlteracaoCommand.Execute(null);
        _vm.Cronograma.Diario.Dias.Select(d => d.Data).Should().Equal([D28], "o Desfazer tira o último dia");
    }

    [Fact]
    public async Task Formulario_SalvoAoTrocarDeDia_EAoFecharATela_ONumeroQueNaoValeSegura()
    {
        var tela = await Abrir();
        tela.NovoDiaCommand.Execute(null);
        tela.NovoDiaCommand.Execute(null);
        tela.Selecionado = tela.Dias[0];
        tela.TempoDaTarde = Tempo(tela, TempoNoPeriodo.Chuva);
        tela.TardePraticavel = false;
        tela.Efetivo[0].Quantidade = "3";
        tela.AdicionarEfetivoCommand.Execute(null);
        tela.Efetivo[1].Funcao = "Servente";
        tela.AdicionarEquipamentoCommand.Execute(null);
        tela.Equipamentos[0].Nome = "Betoneira";
        tela.Ocorrencias = "Chuva forte às 14h.";

        tela.Selecionado = tela.Dias[1];

        var dia = _vm.Cronograma.Diario.Dia(D28)!;
        (dia.Tarde, dia.TotalDoEfetivo, dia.Equipamentos.Single(), dia.Ocorrencias)
            .Should().Be((new ClimaDoPeriodo(TempoNoPeriodo.Chuva, false), 4, new EquipamentoNoDia("Betoneira", 1), "Chuva forte às 14h."));
        dia.Efetivo.Should().Equal(new EfetivoNoDia("Pedreiro", 3, "Empreiteira X"), new EfetivoNoDia("Servente", 1));
        tela.Dias.Select(d => d.Resumo).Should().Equal("4 pessoas · chuva à tarde · aberto", "2 pessoas · aberto");
        (tela.Selecionado!.Data, tela.Cabecalho).Should().Be((D29, "RDO nº 002 · 29/09/2026 · terça-feira"));
        _vm.TextoDesfazer.Should().Be("Desfazer Diário de 28/09/2026");

        tela.Efetivo[0].Quantidade = "dois";
        tela.Selecionado = tela.Dias[0];
        (tela.Selecionado!.Data, tela.Mensagem).Should().Be((D29, "A quantidade de \"Pedreiro\" não é um número."), "fica no dia, com o motivo");
        tela.SalvarSePreciso().Should().BeFalse("a tela não fecha");
        tela.Efetivo[0].Quantidade = "1000";
        tela.SalvarSePreciso().Should().BeFalse();
        tela.Mensagem.Should().Be("A quantidade de \"Pedreiro\" precisa ir de 1 a 999.");

        tela.Efetivo[0].Quantidade = "5";
        tela.Efetivo[0].RemoverCommand.Execute(null);
        tela.AdicionarAtividadeCommand.Execute(null);
        tela.Atividades[1].Descricao = "Limpeza do canteiro";
        tela.SalvarSePreciso().Should().BeTrue();
        var d29 = _vm.Cronograma.Diario.Dia(D29)!;
        d29.Efetivo.Should().BeEmpty();
        d29.Atividades.Select(a => a.Descricao).Should().Equal("1 Escavação", "Limpeza do canteiro");
    }

    [Fact]
    public async Task FecharATela_ComUmNumeroQueNaoVale_PerguntaSeFechaSemSalvar()
    {
        var tela = await Abrir();
        tela.NovoDiaCommand.Execute(null);
        tela.Ocorrencias = "Não vai.";
        tela.Efetivo[0].Quantidade = "x";
        tela.SalvarSePreciso().Should().BeFalse();

        _ambiente.Dialogos.Confirmar = false;
        (await tela.FecharSemSalvarAsync()).Should().BeFalse("ficou para corrigir");
        _ambiente.Dialogos.Confirmar = true;
        (await tela.FecharSemSalvarAsync()).Should().BeTrue();

        _ambiente.Dialogos.Confirmacoes[^1].Should().Be("A quantidade de \"Pedreiro\" não é um número. Fechar sem salvar as mudanças deste dia?");
        (tela.SalvarSePreciso(), _vm.Cronograma.Diario.Dia(D28)!.Ocorrencias).Should().Be((true, (string?)null), "o dia ficou como estava");
    }

    [Fact]
    public async Task FecharEReabrir_OFechadoNaoEdita_EOsNumerosNaoMudam()
    {
        var tela = await Abrir();
        tela.NovoDiaCommand.Execute(null);
        tela.Ocorrencias = "Visita da fiscalização.";

        tela.FecharDiaCommand.Execute(null);

        var dia = _vm.Cronograma.Diario.Dia(D28)!;
        (dia.Fechado, dia.Ocorrencias, dia.Fechamento!.Quando).Should().Be((true, "Visita da fiscalização.", new DateTime(2026, 9, 28, 12, 0, 0)), "salva e fecha");
        (tela.PodeEditar, tela.Fechado, tela.Situacao).Should().Be((false, true, "Fechado em 28/09/2026 12:00: para mudar, reabra."));
        tela.Dias[0].Resumo.Should().Be("2 pessoas · fechado");
        _vm.TextoDesfazer.Should().Be("Desfazer Diário: fechar 28/09/2026");

        tela.Ocorrencias = "Outra coisa.";
        tela.AdicionarEfetivoCommand.Execute(null);
        tela.SalvarSePreciso().Should().BeTrue("um dia fechado não é salvo");
        (_vm.Cronograma.Diario.Dia(D28)!.Ocorrencias, tela.Efetivo.Count).Should().Be(("Visita da fiscalização.", 1));

        tela.DataDoNovo = new DateTime(2026, 9, 25);
        tela.NovoDiaCommand.Execute(null);
        tela.Mensagem.Should().StartWith("O dia 25/09/2026 ficaria antes do último dia fechado (RDO nº 1, 28/09/2026)");

        tela.ReabrirDiaCommand.Execute(null);
        (tela.PodeEditar, tela.Situacao, tela.Mensagem).Should().Be((true, "Aberto: as mudanças são salvas ao trocar de dia e ao fechar a tela.", "RDO de 28/09/2026 reaberto."));

        _ambiente.Dialogos.Confirmar = false;
        await tela.ExcluirDiaCommand.ExecuteAsync(null);
        tela.Dias.Should().HaveCount(1, "não confirmou");
        _ambiente.Dialogos.Confirmar = true;
        await tela.ExcluirDiaCommand.ExecuteAsync(null);
        (tela.SemDias, tela.TemDia, _vm.Cronograma.Diario.Vazio).Should().Be((true, false, true));
    }

    [Fact]
    public void FecharDia_ComOResponsavelDosRelatorios()
    {
        var tela = new DiarioDeObraViewModel(_vm.Cronograma, _ambiente.Dialogos, _ambiente.Relogio, _ => { },
            () => new CabecalhoDoRelatorio("Obra", DadosDaObra.Vazios, "Eng. Ana", "CREA 123", null), _ => new Dictionary<Guid, byte[]>());
        tela.NovoDiaCommand.Execute(null);

        tela.FecharDiaCommand.Execute(null);

        tela.Situacao.Should().Be("Fechado em 28/09/2026 12:00 por Eng. Ana: para mudar, reabra.");
        tela.Mensagem.Should().Be("RDO de 28/09/2026 fechado por Eng. Ana: para mudar, reabra.");
    }

    [Fact]
    public async Task LevarOPercentual_EMarcarSemTrabalho_NoCronograma()
    {
        var tela = await Abrir();
        tela.NovoDiaCommand.Execute(null);
        tela.Atividades[0].Percentual = "40%";
        tela.AdicionarAtividadeCommand.Execute(null);
        tela.Atividades[1].Descricao = "Limpeza do canteiro";

        tela.LevarOPercentualCommand.Execute(null);

        (_escavacao.PercentualConcluido, tela.Mensagem).Should().Be((40, "No cronograma: 1 Escavação: 0% → 40%."));
        _vm.TextoDesfazer.Should().Be("Desfazer Diário: % de 28/09/2026 no cronograma");
        _vm.Cronograma.Diario.Dia(D28)!.Atividades.Select(a => a.Percentual).Should().Equal(40, null);
        tela.LevarOPercentualCommand.Execute(null);
        tela.Mensagem.Should().Be("O cronograma já está com o % dos serviços deste dia.");

        tela.NovoDiaCommand.Execute(null);
        tela.Atividades.Select(a => a.Percentual).Should().Equal(["40"], "o dia novo já vem com o % atual");
        tela.TempoDaManha = Tempo(tela, TempoNoPeriodo.Chuva);
        tela.ManhaPraticavel = false;
        tela.Impraticavel.Should().BeFalse("a tarde ainda é praticável");
        tela.TardePraticavel = false;
        tela.Impraticavel.Should().BeTrue("o botão aparece");
        _vm.TerminoProjeto!.Value.Date.Should().Be(new DateTime(2026, 10, 2));

        tela.MarcarSemTrabalhoCommand.Execute(null);

        _vm.Cronograma.Calendario.MotivoSemTrabalho(D29).Should().Be("Chuva (RDO nº 2)");
        _vm.TerminoProjeto!.Value.Date.Should().Be(new DateTime(2026, 10, 5), "as tarefas andaram um dia útil");
        _vm.Cronograma.Diario.Dia(D29)!.Impraticavel.Should().BeTrue("o formulário foi salvo antes");
        tela.Mensagem.Should().Be("29/09/2026 ficou sem trabalho no calendário: as tarefas andaram um dia útil.");
        _vm.TextoDesfazer.Should().Be("Desfazer Diário: 29/09/2026 sem trabalho no calendário");
        tela.MarcarSemTrabalhoCommand.Execute(null);
        tela.Mensagem.Should().Be("29/09/2026 já não é dia de trabalho no calendário.");

        _vm.DesfazerAlteracaoCommand.Execute(null);
        _vm.Cronograma.Calendario.EhDiaUtil(D29).Should().BeTrue("o Desfazer volta o calendário");
    }

    [Fact]
    public async Task Pdf_DoDiaEDoMes_ComAsFotosDoDia()
    {
        var foto = new FotoTarefa(Guid.NewGuid(), _escavacao.Tarefa.Id, new DadosDaImagem("a.jpg", 10, 10, 10, new string('a', 64)),
            new MetadadosFoto(new DateTime(2026, 9, 28, 9, 15, 0)), new DateTime(2026, 9, 28));
        _escavacao.Tarefa.AdicionarFoto(foto);
        var pedidas = new List<Guid>();
        var tela = new DiarioDeObraViewModel(_vm.Cronograma, _ambiente.Dialogos, _ambiente.Relogio, _ => { },
            () => new CabecalhoDoRelatorio("Obra", DadosDaObra.Vazios, null, null, null), ids =>
            {
                var lista = ids.ToList();
                pedidas.AddRange(lista);
                return lista.ToDictionary(id => id, _ => Jpeg());
            });
        tela.NovoDiaCommand.Execute(null);
        tela.NovoDiaCommand.Execute(null);
        tela.Selecionado = tela.Dias[0];
        tela.FotosDoDia.Should().Be("1 foto neste dia (vai no PDF).");
        _ambiente.Dialogos.ArquivoExportado = Path.Combine(_pasta, "rdo");

        await tela.PdfDoDiaCommand.ExecuteAsync(null);

        File.ReadAllBytes(Path.Combine(_pasta, "rdo.pdf"))[..5].Should().Equal("%PDF-"u8.ToArray());
        pedidas.Should().Equal(foto.Id);
        tela.Mensagem.Should().Be("Salvo em rdo.pdf (1 dia).");

        await tela.PdfDoMesCommand.ExecuteAsync(null);
        tela.Mensagem.Should().Be("Salvo em rdo.pdf (2 dias).");
        _ambiente.Dialogos.Exportacoes.Select(e => (e.Nome, e.Extensao)).Should().Equal(("RDO 001 - 2026-09-28", "pdf"), ("Diário de obra - 2026-09", "pdf"));

        _ambiente.Dialogos.ArquivoExportado = null;
        await tela.PdfDoDiaCommand.ExecuteAsync(null);
        tela.Mensagem.Should().Be("Salvo em rdo.pdf (2 dias).", "cancelou");
    }

    private static byte[] Jpeg()
    {
        using var bitmap = new SKBitmap(64, 48);
        bitmap.Erase(SKColors.SteelBlue);
        using var imagem = SKImage.FromBitmap(bitmap);
        return imagem.Encode(SKEncodedImageFormat.Jpeg, 80).ToArray();
    }
}
