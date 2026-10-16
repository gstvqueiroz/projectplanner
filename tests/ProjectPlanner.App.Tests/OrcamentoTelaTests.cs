using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core.Orcamento;

namespace ProjectPlanner.App.Tests;

// Bloco 33, na tela: Projeto > Orçamento…. A base de teste (CSV no formato da Caixa): a alvenaria 87495 (m²) usa pedreiro,
// servente, bloco e a argamassa 87292 (auxiliar); custo 52,53/m². Alvenaria: 100 m²; Pintura: valor informado de 1.500.
public sealed class OrcamentoTelaTests : IDisposable
{
    private const string Insumos =
        "CODIGO;DESCRICAO DO INSUMO;UNIDADE DE MEDIDA;ORIGEM DE PRECO;PRECO MEDIANO R$\n" +
        "88309;PEDREIRO COM ENCARGOS COMPLEMENTARES;H;CR;{0}\n" +
        "88316;SERVENTE COM ENCARGOS COMPLEMENTARES;H;CR;20,00\n" +
        "7271;BLOCO CERAMICO DE VEDACAO 9X19X19 CM;UN;C;1,20\n" +
        "370;AREIA MEDIA - POSTO JAZIDA;M3;C;120,00\n" +
        "1379;CIMENTO PORTLAND COMPOSTO CP II-32;KG;C;0,80\n" +
        "88830;BETONEIRA 400 L - CHP DIURNO;CHP;CR;2,00\n" +
        "9999;INSUMO COM PRECO RUIM;UN;C;abc\n";

    private const string Composicoes =
        "CODIGO DA COMPOSICAO;DESCRICAO DA COMPOSICAO;UNIDADE;TIPO ITEM;CODIGO ITEM;DESCRIÇÃO ITEM;UNIDADE ITEM;COEFICIENTE;PRECO UNITARIO\n" +
        "87292;ARGAMASSA TRAÇO 1:2:8 (CIMENTO, CAL E AREIA);M3;;;;;;\n" +
        "87292;;;INSUMO;88316;SERVENTE;H;10,0000000;20,00\n" +
        "87292;;;INSUMO;370;AREIA;M3;1,1000000;120,00\n" +
        "87292;;;INSUMO;1379;CIMENTO;KG;200,0000000;0,80\n" +
        "87292;;;INSUMO;88830;BETONEIRA;CHP;1,0000000;2,00\n" +
        "87495;ALVENARIA DE VEDAÇÃO DE BLOCOS CERÂMICOS FURADOS 9X19X19 CM;M2;;;;;;\n" +
        "87495;;;INSUMO;88309;PEDREIRO;H;0,8000000;28,00\n" +
        "87495;;;INSUMO;88316;SERVENTE;H;0,4000000;20,00\n" +
        "87495;;;INSUMO;7271;BLOCO;UN;13,5000000;1,20\n" +
        "87495;;;COMPOSICAO;87292;ARGAMASSA;M3;0,0120000;533,00\n";

    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-orcamento-" + Guid.NewGuid().ToString("N"));

    public OrcamentoTelaTests()
    {
        Directory.CreateDirectory(_pasta);
        _vm = _ambiente.CriarViewModel();
        var alvenaria = Ambiente.AdicionarTarefa(_vm, "Alvenaria", 5);
        alvenaria.Quantidade = "100";
        alvenaria.Unidade = "m²";
        Ambiente.AdicionarTarefa(_vm, "Pintura", 2).Valor = "1500";
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private OrcamentoViewModel Abrir()
    {
        _vm.OrcamentoCommand.Execute(null);
        return _ambiente.Dialogos.Orcamentos.Last();
    }

    private string Arquivo(string nome, string conteudo)
    {
        var caminho = Path.Combine(_pasta, nome);
        File.WriteAllText(caminho, conteudo);
        return caminho;
    }

    private async Task Importar(OrcamentoViewModel tela, string mes = "202609", string pedreiro = "28,00")
    {
        _ambiente.Dialogos.PlanilhaEscolhida = Arquivo($"SINAPI_Preco_Ref_Insumos_SP_{mes}_NaoDesonerado.csv", string.Format(Insumos, pedreiro));
        await tela.ImportarPlanilhaCommand.ExecuteAsync(null);
        _ambiente.Dialogos.PlanilhaEscolhida = Arquivo($"SINAPI_Custo_Ref_Composicoes_Analitico_SP_{mes}_NaoDesonerado.csv", Composicoes);
        await tela.ImportarPlanilhaCommand.ExecuteAsync(null);
    }

    private static void UsarAlvenaria(OrcamentoViewModel tela)
    {
        tela.Busca = "alvenaria";
        tela.TarefaEscolhida = tela.Tarefas[0];
        tela.UsarCommand.Execute(null);
    }

    [Fact]
    public void SemBases_NemServicos_AvisaEBloqueia()
    {
        var tela = Abrir();

        (tela.SemBases, tela.SemServicos, tela.BaseEscolhida).Should().Be((true, true, null));
        tela.Linhas.Select(l => (l.Edt, l.Codigo, l.Fonte, l.Total)).Should().Equal(("2", "—", "valor informado", "1.500,00"));
        tela.AplicarCommand.CanExecute(null).Should().BeFalse();
        tela.UsarCommand.CanExecute(null).Should().BeFalse();
        tela.Tarefas.Select(t => t.Texto).Should().Equal("1 Alvenaria · 100,00 m²", "2 Pintura · sem quantidade");
    }

    [Fact]
    public async Task Importar_AsDuasPlanilhas_NaMesmaBase_PeloNomeDoArquivo()
    {
        var tela = Abrir();
        _ambiente.Dialogos.PlanilhaEscolhida = Arquivo("SINAPI_Preco_Ref_Insumos_SP_202609_NaoDesonerado.csv", string.Format(Insumos, "28,00"));

        await tela.ImportarPlanilhaCommand.ExecuteAsync(null);

        (tela.Uf, tela.Referencia, tela.DesoneradoEscolhido.Texto).Should().Be(("SP", "09/2026", "Não desonerado"));
        tela.Mensagem.Should().StartWith("SINAPI_Preco_Ref_Insumos_SP_202609_NaoDesonerado.csv → SINAPI SP 09/2026 não desonerado: 6 insumos e 0 composições. 1 linha pulada")
            .And.EndWith("A base tem agora 6 insumos e 0 composições.");

        _ambiente.Dialogos.PlanilhaEscolhida = Arquivo("SINAPI_Custo_Ref_Composicoes_Analitico_SP_202609_NaoDesonerado.csv", Composicoes);
        await tela.ImportarPlanilhaCommand.ExecuteAsync(null);

        tela.BasesNaBiblioteca.Should().Equal(new BaseNaBiblioteca(new BaseDeReferencia("SINAPI", "SP", "09/2026", false), 6, 2));
        tela.Mensagem.Should().EndWith("A base tem agora 6 insumos e 2 composições.");
        tela.BaseEscolhida!.Texto.Should().Be("SINAPI SP 09/2026 não desonerado");
        _ambiente.Biblioteca.Carregar(new BaseDeReferencia("SINAPI", "SP", "09/2026", false))!.Insumo("88309")!.Tipo
            .Should().Be(TipoDeInsumo.MaoDeObra, "o insumo da planilha de insumos fica");
    }

    [Fact]
    public async Task Importar_ArquivoQueNaoEBase_OuIlegivel_SoAvisa()
    {
        var tela = Abrir();
        _ambiente.Dialogos.PlanilhaEscolhida = Arquivo("qualquer.csv", "a;b\n1;2\n");
        await tela.ImportarPlanilhaCommand.ExecuteAsync(null);
        tela.Mensagem.Should().Contain("não parece uma base de preços");

        _ambiente.Dialogos.PlanilhaEscolhida = Path.Combine(_pasta, "nao-existe.csv");
        await tela.ImportarPlanilhaCommand.ExecuteAsync(null);
        tela.Mensagem.Should().StartWith("Não foi possível ler nao-existe.csv");

        _ambiente.Dialogos.PlanilhaEscolhida = null;
        await tela.ImportarPlanilhaCommand.ExecuteAsync(null);
        (tela.SemBases, tela.Ocupado).Should().Be((true, false));
    }

    [Fact]
    public async Task Buscar_EUsarNaTarefa_ComBdi_PlanilhaETotais_UmPassoDoDesfazer()
    {
        var tela = Abrir();
        await Importar(tela);

        tela.Busca = "ceramico";
        tela.Resultados.Should().Equal(new ComposicaoNaBusca(tela.Resultados[0].Composicao, "52,53"));
        tela.Resultados[0].Codigo.Should().Be("87495");
        tela.Bdi = "25";
        tela.BdiDeMateriais = "15%";
        tela.SalvarBdiCommand.Execute(null);
        tela.TarefaEscolhida = tela.Tarefas[0];
        tela.UsarCommand.Execute(null);

        tela.Mensagem.Should().Be("\"Alvenaria\" usa a composição 87495.");
        _vm.TextoDesfazer.Should().Be("Desfazer Composição de \"Alvenaria\"");
        tela.TextoDaComposicao.Should().Be("SINAPI 87495 — ALVENARIA DE VEDAÇÃO DE BLOCOS CERÂMICOS FURADOS 9X19X19 CM (M2) · SINAPI SP 09/2026 não desonerado · " +
                                           "custo R$ 52,53/M2 · preço com BDI R$ 63,69/M2");
        tela.InsumosDaTarefa.Select(i => (i.Codigo, i.Tipo, i.Coeficiente, i.Parcial)).Should().Contain(("88316", "Mão de obra", "0,5200", "10,40"));
        tela.Linhas.Select(l => (l.Edt, l.Codigo, l.Quantidade, l.CustoUnitario, l.PrecoUnitario, l.Total)).Should().Equal(
            ("1", "87495", "100,00", "52,53", "63,69", "6.369,00"),
            ("2", "—", "", "", "", "1.500,00"));
        tela.Totais.Select(t => (t.Rotulo, t.Valor)).Should().Equal(
            ("Custo direto (composições)", "R$ 5.253,00"), ("BDI (25%; materiais 15%)", "R$ 1.116,00"),
            ("Tarefas sem composição (valor informado)", "R$ 1.500,00"), ("Total do orçamento", "R$ 7.869,00"));
        tela.BasesUsadas.Should().Be("Preços: SINAPI SP 09/2026 não desonerado");
        tela.Tarefas[0].Texto.Should().Be("1 Alvenaria · 100,00 m² · 87495");

        _vm.DesfazerAlteracaoCommand.Execute(null);
        _vm.Cronograma.Orcamento.Servicos.Should().BeEmpty();
        _vm.Cronograma.Orcamento.Configuracao.Bdi.Should().Be(25, "o BDI foi outro passo");
    }

    [Fact]
    public async Task Bdi_Invalido_Avisa_ETirarComposicao()
    {
        var tela = Abrir();
        await Importar(tela);
        UsarAlvenaria(tela);

        tela.Bdi = "150";
        tela.SalvarBdiCommand.Execute(null);
        tela.Mensagem.Should().Be("O BDI vai de 0% a 100%.");
        tela.Bdi = "dez";
        tela.SalvarBdiCommand.Execute(null);
        _vm.Cronograma.Orcamento.Configuracao.Bdi.Should().Be(0);

        tela.TirarCommand.CanExecute(null).Should().BeTrue();
        tela.TirarCommand.Execute(null);
        (tela.TextoDaComposicao, tela.SemServicos, _vm.TextoDesfazer).Should().Be(("Sem composição.", true, "Desfazer Tirar a composição de \"Alvenaria\""));
        tela.TirarCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Aplicar_PoeValoresERecursos_ConfirmandoEDesfazendo()
    {
        var tela = Abrir();
        await Importar(tela);
        tela.Bdi = "25";
        tela.BdiDeMateriais = "15";
        tela.SalvarBdiCommand.Execute(null);
        UsarAlvenaria(tela);

        _ambiente.Dialogos.Confirmar = false;
        tela.AplicarCommand.Execute(null);
        _vm.Cronograma.Tarefas[0].Valor.Should().BeNull("não confirmou");

        _ambiente.Dialogos.Confirmar = true;
        tela.AplicarCommand.Execute(null);

        _vm.Tarefas[0].Valor.Should().Be("6.369,00");
        _vm.Cronograma.Recursos.Lista.Select(r => r.Nome).Should().Contain("PEDREIRO COM ENCARGOS COMPLEMENTARES");
        _vm.Cronograma.Tarefas[0].Atribuicoes.Should().HaveCount(6);
        tela.Mensagem.Should().StartWith("1 tarefa com o valor do orçamento");
        _vm.TextoDesfazer.Should().Be("Desfazer Aplicar o orçamento");

        _vm.DesfazerAlteracaoCommand.Execute(null);
        _vm.Cronograma.Tarefas[0].Valor.Should().BeNull();
        _vm.Cronograma.Recursos.Lista.Should().BeEmpty();
        _vm.Cronograma.Orcamento.Servicos.Should().ContainKey(_vm.Cronograma.Tarefas[0].Id, "só o aplicar foi desfeito");
    }

    [Fact]
    public async Task Aplicar_ComContratoTravado_Recusa()
    {
        var tela = Abrir();
        await Importar(tela);
        UsarAlvenaria(tela);
        var c = _vm.Cronograma;
        c.Contrato.NovaMedicao(new DateOnly(2026, 9, 28), new DateOnly(2026, 9, 30));
        c.Contrato.Medir(1, c.Tarefas[1].Id, 10);
        c.Contrato.Aprovar(1, Ambiente.Segunda);
        _ambiente.Dialogos.Confirmar = true;

        tela.AplicarCommand.Execute(null);

        tela.Mensagem.Should().Contain("medição aprovada");
        c.Tarefas[0].Valor.Should().BeNull();
    }

    [Fact]
    public async Task AtualizarPrecos_PorOutroMes_ERemoverBase()
    {
        var tela = Abrir();
        await Importar(tela);
        UsarAlvenaria(tela);
        await Importar(tela, "202610", "30,00");
        tela.BasesNaBiblioteca.Select(b => b.Base.Descricao).Should().Equal("SINAPI SP 09/2026 não desonerado", "SINAPI SP 10/2026 não desonerado");

        tela.BaseNaLista = tela.BasesNaBiblioteca[1];
        _ambiente.Dialogos.Confirmar = true;
        tela.AtualizarPrecosCommand.Execute(null);

        tela.Mensagem.Should().Be("1 composição atualizada pela SINAPI SP 10/2026 não desonerado.");
        tela.Linhas[0].CustoUnitario.Should().Be("54,13", "o pedreiro passou de 28 para 30: + 0,8 × 2");
        _vm.TextoDesfazer.Should().Be("Desfazer Atualizar os preços pela SINAPI SP 10/2026 não desonerado");

        tela.BaseNaLista = tela.BasesNaBiblioteca[0];
        tela.RemoverBaseCommand.Execute(null);
        tela.BasesNaBiblioteca.Should().ContainSingle().Which.Base.Referencia.Should().Be("10/2026");
        tela.Linhas[0].Codigo.Should().Be("87495", "o orçamento guarda as composições que usa");
    }

    [Fact]
    public async Task Exportar_PdfWordEExcel()
    {
        var tela = Abrir();
        await Importar(tela);
        UsarAlvenaria(tela);
        _ambiente.Dialogos.ArquivoExportado = Path.Combine(_pasta, "orcamento");

        foreach (var (comando, extensao, assinatura) in new[]
                 {
                     (tela.ExportarPdfCommand, "pdf", "%PDF"), (tela.ExportarWordCommand, "docx", "PK"), (tela.ExportarExcelCommand, "xlsx", "PK"),
                 })
        {
            await comando.ExecuteAsync(null);
            var caminho = Path.Combine(_pasta, "orcamento." + extensao);
            File.ReadAllText(caminho).Should().StartWith(assinatura);
            tela.Mensagem.Should().Be($"Salvo em orcamento.{extensao}.");
        }
        _ambiente.Dialogos.Exportacoes.Select(e => e.Nome).Should().AllBe($"Orçamento - {_vm.Cronograma.Nome}");

        _ambiente.Dialogos.ArquivoExportado = null;
        await tela.ExportarPdfCommand.ExecuteAsync(null);
        Directory.GetFiles(_pasta, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void Biblioteca_EmArquivo_GuardaListaERemove()
    {
        var pasta = Path.Combine(_pasta, "referencias");
        var biblioteca = new ArquivoBibliotecaDeReferencias(pasta);
        var @base = new BaseDeReferencia("SINAPI", "BA", "12/2025", true);
        biblioteca.Listar().Should().BeEmpty("a pasta nem existe");

        biblioteca.Salvar(new TabelaDeReferencia(@base, [new Insumo("1", "Areia", "M3", TipoDeInsumo.Material, 100)],
            [new Composicao("2", "Aterro", "M3", [new ItemDeComposicao(false, "1", 1.2m)])]));

        biblioteca.Listar().Should().Equal(new BaseNaBiblioteca(@base, 1, 1));
        new ArquivoBibliotecaDeReferencias(pasta).Carregar(@base)!.Abrir("2").CustoUnitario.Should().Be(120m);
        File.WriteAllText(Path.Combine(pasta, "estragado.json"), "{ não é json");
        biblioteca.Listar().Should().HaveCount(1, "um arquivo estragado é ignorado");
        biblioteca.Remover(@base);
        biblioteca.Carregar(@base).Should().BeNull();
    }
}
