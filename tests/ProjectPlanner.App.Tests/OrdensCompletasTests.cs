using System.Text;
using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core.OrdensDeServico;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Tests;

// Bloco 64: o que faltava nas OS: a marca no Gantt, a Nova OS direto da barra, o relatório das OS e a IA abrindo as telas e
// medindo uma OS.
public sealed class OrdensCompletasTests : IDisposable
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly TarefaViewModel _alvenaria;
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-os64-" + Guid.NewGuid().ToString("N"));

    public OrdensCompletasTests()
    {
        Directory.CreateDirectory(_pasta);
        _vm = _ambiente.CriarViewModel();
        _alvenaria = Ambiente.AdicionarTarefa(_vm, "Alvenaria", 5);
        _alvenaria.Tarefa.DefinirUnidade("m²");
        _alvenaria.Tarefa.DefinirQuantidade(100);
        _alvenaria.Valor = "3.000";
        Ambiente.AdicionarTarefa(_vm, "Reboco", 4, "1");
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    // A Nova OS da barra: a tela abre já com a OS da tarefa selecionada; a OS é emitida para a Construtora Silva.
    private OrdensDeServicoViewModel NovaEmitida()
    {
        _ambiente.Dialogos.AoMostrarOrdensDeServico = tela =>
        {
            tela.Executante = "Construtora Silva";
            tela.Itens[0].Preco = "30";
            tela.EmitirCommand.Execute(null);
            return Task.CompletedTask;
        };
        _vm.TarefaSelecionada = _alvenaria;
        _vm.NovaOrdemDeServicoCommand.Execute(null);
        _ambiente.Dialogos.AoMostrarOrdensDeServico = null;
        return _ambiente.Dialogos.OrdensDeServico.Last();
    }

    [Fact]
    public void NovaOS_PelaBarra_JaAbreComAOS_EAMarcaVaiAoGantt()
    {
        var tela = NovaEmitida();

        tela.Cabecalho.Should().Be("OS-001 · Emitida");
        _vm.Gantt.Ordens.Should().ContainKey(_alvenaria.Tarefa.Id);
        _vm.Gantt.Ordens[_alvenaria.Tarefa.Id].Should().Be(new MarcaDeOrdemNoGantt("OS-001 · Emitida (prazo 02/10) · Construtora Silva", false));
    }

    [Fact]
    public async Task Relatorio_FiltroResumoPdfECsv()
    {
        NovaEmitida();
        _vm.RelatorioDasOrdensCommand.Execute(null);
        var relatorio = _ambiente.Dialogos.RelatoriosDasOrdens.Single();

        relatorio.Resumo.Should().Be("1 OS · contratado R$ 3.000,00 · medido R$ 0,00 · pago R$ 0,00 · a pagar R$ 0,00");
        relatorio.Executantes.Should().Equal(RelatorioDasOrdensViewModel.TodosOsExecutantes, "Construtora Silva");
        relatorio.SituacaoEscolhida = relatorio.Situacoes.Single(s => s.Valor == SituacoesDoRelatorio.Encerradas);
        relatorio.Resumo.Should().Be("Nenhuma OS neste filtro.");
        relatorio.SituacaoEscolhida = relatorio.Situacoes[0];

        _ambiente.Dialogos.ArquivoExportado = Path.Combine(_pasta, "relatorio");
        await relatorio.SalvarPdfCommand.ExecuteAsync(null);
        await relatorio.ExportarCsvCommand.ExecuteAsync(null);

        Encoding.ASCII.GetString(await File.ReadAllBytesAsync(Path.Combine(_pasta, "relatorio.pdf")), 0, 5).Should().Be("%PDF-");
        (await File.ReadAllTextAsync(Path.Combine(_pasta, "relatorio.csv"))).Should().Contain("OS-001;Alvenaria;Construtora Silva");
        _ambiente.Dialogos.Exportacoes.Select(e => e.Nome).Should().Equal($"Relatório das OS - {_vm.NomeProjeto}", $"Relatório das OS - {_vm.NomeProjeto}");
    }

    [Fact]
    public async Task IA_MedeAOSAteUmPercentual_EAbreAsTelas()
    {
        var tela = NovaEmitida();
        tela.AceitarCommand.Execute(null);
        var cartao = _vm.ConferirAtualizacoes([new AtualizacaoDaObra("situacaoOs", Percentual: 60, Os: 1, Acao: "medir")], _vm.RetratoParaOAssistente());

        cartao.Single().Texto.Should().Be("OS-001: medir até 60% executado · R$ 1.800,00 a medir");
        await _vm.AplicarAtualizacoesAsync([cartao.Single().Operacao!], "Atualizações do assistente");

        var os = _vm.Cronograma.OrdensDeServico.Todas.Single();
        (os.TotalMedido, os.Medicoes.Single().Quantidades.Single(), _vm.Cronograma.Tarefas[0].PercentualConcluido).Should().Be((1800m, 60m, 60));
        _vm.ConferirAtualizacoes([new AtualizacaoDaObra("situacaoOs", Percentual: 50, Os: 1, Acao: "medir")], _vm.RetratoParaOAssistente())
            .Single().Problema.Should().Be("a OS-001 já está medida até esse %");

        await _vm.AbrirTelaAsync(new TelaParaAbrir(TelasDoPrograma.RelatorioDasOrdens, null, null, null, null, false));
        _ambiente.Dialogos.RelatoriosDasOrdens.Should().ContainSingle();
    }

    // ---- Bloco 65 ----

    [Fact]
    public async Task BoletimDaMedicao_EmPdfEExcel()
    {
        var tela = NovaEmitida();
        tela.AceitarCommand.Execute(null);
        tela.ItensParaMedir[0].NestaMedicao = "40";
        tela.RegistrarMedicaoCommand.Execute(null);
        _ambiente.Dialogos.ArquivoExportado = Path.Combine(_pasta, "boletim");

        await tela.BoletimPdfCommand.ExecuteAsync(tela.MedicoesNaTela[0]);
        await tela.BoletimExcelCommand.ExecuteAsync(tela.MedicoesNaTela[0]);

        _ambiente.Dialogos.Exportacoes.Select(e => (e.Titulo, e.Nome, e.Extensao)).Should().Equal(
            ("Boletim de medição da OS-001 M1 em PDF", "OS-001 M1 - Boletim de medição", "pdf"),
            ("Boletim de medição da OS-001 M1 em Excel", "OS-001 M1 - Boletim de medição", "xlsx"));
        Encoding.ASCII.GetString(await File.ReadAllBytesAsync(Path.Combine(_pasta, "boletim.pdf")), 0, 5).Should().Be("%PDF-");
        Encoding.ASCII.GetString(await File.ReadAllBytesAsync(Path.Combine(_pasta, "boletim.xlsx")), 0, 2).Should().Be("PK");
    }

    [Fact]
    public async Task Relatorio_EmExcel_EComODetalheDeCadaOS()
    {
        var tela = NovaEmitida();
        tela.AceitarCommand.Execute(null);
        tela.ItensParaMedir[0].NestaMedicao = "40";
        tela.RegistrarMedicaoCommand.Execute(null);
        var relatorio = tela.NovoRelatorio();
        _ambiente.Dialogos.ArquivoExportado = Path.Combine(_pasta, "relatorio");

        await relatorio.ExportarExcelCommand.ExecuteAsync(null);
        await relatorio.SalvarPdfCommand.ExecuteAsync(null);
        var simples = await File.ReadAllBytesAsync(Path.Combine(_pasta, "relatorio.pdf"));
        relatorio.Detalhar = true;
        await relatorio.SalvarPdfCommand.ExecuteAsync(null);
        var detalhado = await File.ReadAllBytesAsync(Path.Combine(_pasta, "relatorio.pdf"));

        Encoding.ASCII.GetString(await File.ReadAllBytesAsync(Path.Combine(_pasta, "relatorio.xlsx")), 0, 2).Should().Be("PK");
        int Paginas(byte[] pdf) => System.Text.RegularExpressions.Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page\b").Count;
        Paginas(detalhado).Should().BeGreaterThan(Paginas(simples), "o detalhe de cada OS vai no fim");
        relatorio.Relatorio.PorMes.Single().Medido.Should().Be(1200m);
    }
}
