using System.Text;
using System.Text.RegularExpressions;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core.Indicadores;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Tests;

// Bloco 43, na tela: as abas Custos e margem, Medições e caixa, e Alertas e análise de Projeto > Indicadores…. "Hoje" é seg
// 28/09/2026: Escavação (5 d, R$ 5.000) → Sapatas (10 d, R$ 10.000).
public sealed class IndicadoresAmpliadosTelaTests : IDisposable
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-ampliados-" + Guid.NewGuid().ToString("N"));

    public IndicadoresAmpliadosTelaTests()
    {
        Directory.CreateDirectory(_pasta);
        _vm = _ambiente.CriarViewModel();
        Ambiente.AdicionarTarefa(_vm, "Escavação", 5).Valor = "5.000";
        Ambiente.AdicionarTarefa(_vm, "Sapatas", 10, "1").Valor = "10.000";
        _vm.Conversa.Ficha.Definir("Área construída", "120 m²");
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private IndicadoresViewModel Abrir()
    {
        _vm.IndicadoresCommand.Execute(null);
        return _ambiente.Dialogos.Indicadores.Last();
    }

    [Fact]
    public void Abas_ComOsDados_SemCustoOrcadoAvisa()
    {
        var tela = Abrir();

        (tela.AbaEscolhida, tela.SemCustoOrcado).Should().Be((IndicadoresViewModel.AbaVisaoGeral, true));
        tela.CartoesDeCusto.Should().HaveCount(8);
        tela.Custos.Select(l => l.Celulas[0]).Should().Equal("1 Escavação", "2 Sapatas", "Total");
        tela.Custos[^1].Destaque.Should().BeTrue();
        tela.Medicoes.Should().HaveCount(3);
        tela.Contrato.Select(c => c.Rotulo).Should().StartWith(["Valor original"]);
        tela.Caixa.Should().NotBeEmpty();
        tela.ImagemDoCaixa.Should().NotBeNull();
        // No fim do dia 28/09 o planejado é R$ 1.000 e nada foi feito: IDP 0, crítico.
        tela.Alertas.Select(a => (a.Area, a.Nivel)).Should().Equal(("Prazo", "Crítico"), ("Custo", "Sem dados"), ("Medição", "Sem dados"),
            ("Recursos", "Sem dados"), ("Fotos", "Ok"));
        tela.Alertas[1].Cor.Should().Be("#90A4AE");

        tela.DataDeStatus = null;
        (tela.Analise, tela.Custos.Count, tela.AnalisarCommand.CanExecute(null)).Should().Be((null, 0, false));
    }

    [Fact]
    public async Task Analisar_PreencheOsCamposEditaveis_EVaiParaOPdf()
    {
        var tela = Abrir();

        await tela.AnalisarCommand.ExecuteAsync(null);

        var pedido = _ambiente.AnalistaDaObra.Pedidos.Single();
        (pedido.Obra, pedido.Data).Should().Be((_vm.Cronograma.Nome, new DateOnly(2026, 9, 28)));
        pedido.Ficha.Should().Equal("Área construída: 120 m²");
        pedido.Dados.Should().Contain(l => l.StartsWith("Prazo:")).And.Contain(l => l.StartsWith("Alerta Custo (Sem dados)"));
        (tela.ResumoDaAnalise, tela.PontosDaAnalise, tela.RecomendacoesDaAnalise)
            .Should().Be(("A obra está no prazo e dentro do custo.", "Atenção à estrutura.", "Lançar os custos da semana."));
        tela.AvisoDaAnalise.Should().StartWith("Análise escrita pela IA");

        tela.PontosDaAnalise = "• Primeiro\n\n- Segundo ";
        tela.EscritaParaExportar!.Pontos.Should().Equal("Primeiro", "Segundo");
        _ambiente.Dialogos.ArquivoExportado = Path.Combine(_pasta, "indicadores");
        await tela.ExportarPdfCommand.ExecuteAsync(null);
        var pdf = File.ReadAllBytes(Path.Combine(_pasta, "indicadores.pdf"));
        Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page\b").Count.Should().BeGreaterThanOrEqualTo(4);

        (tela.ResumoDaAnalise, tela.PontosDaAnalise, tela.RecomendacoesDaAnalise) = ("", "", "");
        tela.EscritaParaExportar.Should().BeNull("sem nada escrito, a análise não vai para o PDF");
    }

    [Fact]
    public async Task Analisar_SemIA_ErroECancelar()
    {
        var tela = Abrir();
        _ambiente.AnalistaDaObra.EstaConfigurado = false;
        await tela.AnalisarCommand.ExecuteAsync(null);
        (tela.AvisoDaAnalise, _ambiente.AnalistaDaObra.Pedidos.Count).Should().Be((AnalistaIndisponivel.Mensagem, 0));

        _ambiente.AnalistaDaObra.EstaConfigurado = true;
        _ambiente.AnalistaDaObra.Erro = new ErroDoAssistenteException("O limite de uso do Gemini foi atingido.");
        await tela.AnalisarCommand.ExecuteAsync(null);
        (tela.AvisoDaAnalise, tela.Analisando, tela.ResumoDaAnalise).Should().Be(("O limite de uso do Gemini foi atingido.", false, ""));

        _ambiente.AnalistaDaObra.Erro = null;
        _ambiente.AnalistaDaObra.Segurar = new TaskCompletionSource();
        var analise = tela.AnalisarCommand.ExecuteAsync(null);
        (tela.Analisando, tela.AnalisarCommand.CanExecute(null), tela.CancelarAnaliseCommand.CanExecute(null)).Should().Be((true, false, true));
        tela.Fechando();
        await analise;
        (tela.AvisoDaAnalise, tela.Analisando).Should().Be(("Análise cancelada.", false));
    }

    [Fact]
    public void ComCustoMedicaoERecursos_AsAbasMostramOsDesvios()
    {
        var c = _vm.Cronograma;
        var escavacao = c.Tarefas[0];
        escavacao.AtualizarPercentualConcluido(100);
        c.Recursos.Adicionar(new Core.Recursos.Recurso(Guid.NewGuid(), "Pedreiro", Core.Recursos.TipoDeRecurso.Profissional, null, 200));
        c.Recursos.Atribuir(escavacao, [new Core.Recursos.AtribuicaoDeRecurso(c.Recursos.Lista[0].Id, 2)]);
        c.Custos.Adicionar(new LancamentoDeCusto(Guid.NewGuid(), new DateOnly(2026, 9, 28), escavacao.Id, CategoriaDeCusto.MaoDeObra, "Equipe", null, 3000));

        var tela = Abrir();

        tela.SemCustoOrcado.Should().BeFalse();
        tela.Custos[0].Celulas.Should().Equal("1 Escavação", "5.000,00", "2.000,00", "3.000,00 (60,0%)", "2.000,00", "3.000,00", "-1.000,00",
            "3.000,00", "2.000,00");
        (tela.Custos[0].Alerta, tela.Custos[0].Alerta2).Should().Be((true, false), "gastando mais que o orçado pelo feito, mas com margem projetada positiva");
        (tela.Custos[^1].Destaque, tela.Custos[^1].Alerta).Should().Be((true, true), "o total também acende");
        tela.Alertas.Single(a => a.Area == "Custo").Nivel.Should().Be("Crítico");
        tela.CartoesDeCusto.Single(k => k.Rotulo == "Desvio de custo").Alerta.Should().BeTrue();
    }
}
