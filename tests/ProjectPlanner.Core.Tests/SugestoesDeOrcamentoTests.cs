using ProjectPlanner.Core.Orcamento;
using ProjectPlanner.Core.Recursos;

namespace ProjectPlanner.Core.Tests;

// Bloco 38: as sugestões da IA no orçamento: preço estimado, quantidade estimada, candidatas da base, e conferir e aplicar
// as aceitas. Base: alvenaria 87495 (pedreiro 0,8 h a 28; bloco 13,5 un a 1,20: 38,60/m²), chapisco 87879 e reboco 87529.
public class SugestoesDeOrcamentoTests
{
    private static readonly DateTime Seg = new(2026, 9, 28);

    private static readonly BaseDeReferencia Sinapi = new("SINAPI", "SP", "09/2026", true);

    private static TabelaDeReferencia Tabela() => new(Sinapi,
        [
            new Insumo("88309", "PEDREIRO", "H", TipoDeInsumo.MaoDeObra, 28), new Insumo("7271", "BLOCO CERAMICO", "UN", TipoDeInsumo.Material, 1.2m),
            new Insumo("1379", "CIMENTO", "KG", TipoDeInsumo.Material, 0.8m),
        ],
        [
            new Composicao("87495", "ALVENARIA DE VEDAÇÃO DE BLOCOS CERÂMICOS FURADOS 9X19X19 CM", "M2", [new(false, "88309", 0.8m), new(false, "7271", 13.5m)]),
            new Composicao("87879", "CHAPISCO APLICADO EM ALVENARIAS E ESTRUTURAS DE CONCRETO INTERNAS", "M2", [new(false, "88309", 0.1m), new(false, "1379", 2)]),
            new Composicao("87529", "MASSA ÚNICA (REBOCO) EM PAREDES INTERNAS DE ALVENARIA", "M2", [new(false, "88309", 0.5m), new(false, "1379", 6)]),
            new Composicao("90000", "ESTACA ESCAVADA", "M", [new(false, "88309", 1)]),
        ]);

    private static (Cronograma C, Tarefa Etapa, Tarefa Alvenaria, Tarefa Pintura, Tarefa Entrega) Obra()
    {
        var c = new Cronograma("Casa", Seg);
        var etapa = new Tarefa("Vedações", Seg, 1);
        var alvenaria = new Tarefa("Alvenaria do térreo", Seg, 5);
        var pintura = new Tarefa("Pintura", Seg, 4);
        var entrega = new Tarefa("Entrega", Seg, 0);
        c.AdicionarTarefa(etapa);
        c.AdicionarTarefa(alvenaria, etapa.Id);
        c.AdicionarTarefa(pintura);
        c.AdicionarTarefa(entrega);
        CalculadoraCronograma.Calcular(c);
        return (c, etapa, alvenaria, pintura, entrega);
    }

    // ---- Estimativa e quantidade estimada ----

    [Fact]
    public void Estimativa_DuasPartes_ComBdiDiferenciado_SemRecursos_ForaDasBases()
    {
        var (c, _, _, pintura, _) = Obra();
        var estimativa = ComposicaoAberta.Estimativa(" Pintura acrílica ", "m²", 8.504m, 12);
        estimativa.Should().Match<ComposicaoAberta>(e => e.EhEstimativa && e.Codigo == "IA" && e.Descricao == "Pintura acrílica" && e.Base.Descricao == "Estimativa IA");
        (estimativa.CustoUnitario, estimativa.CustoDeMateriais, estimativa.CustoDeMaoDeObraEEquipamentos).Should().Be((20.5m, 8.5m, 12m));
        ComposicaoAberta.Estimativa("Só mão de obra", "h", 0, 50).Insumos.Should().ContainSingle().Which.Tipo.Should().Be(TipoDeInsumo.MaoDeObra);

        c.Orcamento.DefinirConfiguracao(new ConfiguracaoDoOrcamento { Bdi = 20, BdiDeMateriais = 10 });
        pintura.DefinirQuantidade(100);
        c.Orcamento.Associar(pintura, estimativa);
        c.Orcamento.PrecoUnitario(estimativa).Should().Be(23.75m, "12 × 1,2 + 8,5 × 1,1");
        var resultado = c.Orcamento.Aplicar();

        (pintura.Valor, resultado.RecursosNovos, c.Recursos.Lista.Count).Should().Be((2375m, 0, 0));
        var p = c.Orcamento.Planilha();
        p.Bases.Should().BeEmpty("a estimativa não é uma base");
        p.Linhas.Single(l => l.TarefaId == pintura.Id).Fonte.Should().Be("Estimativa IA");
        var (atualizadas, naoEncontradas) = c.Orcamento.AtualizarPrecos(Tabela());
        (atualizadas, naoEncontradas.Count).Should().Be((0, 0), "a estimativa não muda com outra base (nem conta como faltando)");
    }

    [Theory]
    [InlineData("", "m²", 1, 1, "*descrição*")]
    [InlineData("Pintura", " ", 1, 1, "*unidade*")]
    [InlineData("Pintura", "m²", 0, 0, "*maior que zero*")]
    [InlineData("Pintura", "m²", -1, 5, "*maior que zero*")]
    public void Estimativa_Invalida_Recusada(string descricao, string unidade, decimal materiais, decimal maoDeObra, string mensagem) =>
        ((Action)(() => ComposicaoAberta.Estimativa(descricao, unidade, materiais, maoDeObra))).Should().Throw<DomainValidationException>().WithMessage(mensagem);

    [Fact]
    public void QuantidadeEstimada_MarcadaAteMudar_CopiadaERecusadaComContratoTravado()
    {
        var (c, etapa, alvenaria, pintura, _) = Obra();

        c.Orcamento.EstimarQuantidade(alvenaria, 342.72m, "m²");

        (alvenaria.Quantidade, alvenaria.Unidade, c.Orcamento.QuantidadeEhEstimada(alvenaria)).Should().Be((342.72m, "m²", true));
        c.Orcamento.Vazio.Should().BeFalse();
        c.Orcamento.Associar(alvenaria, Tabela().Abrir("87495"));
        c.Orcamento.Planilha().Linhas.Single(l => l.TarefaId == alvenaria.Id).QuantidadeEstimada.Should().BeTrue();
        var copia = c.Copiar();
        copia.Orcamento.QuantidadeEhEstimada(copia.Tarefas.Single(t => t.Id == alvenaria.Id)).Should().BeTrue();

        alvenaria.DefinirQuantidade(300);
        c.Orcamento.QuantidadeEhEstimada(alvenaria).Should().BeFalse("o usuário mudou a quantidade");
        ((Action)(() => c.Orcamento.EstimarQuantidade(etapa, 10, null))).Should().Throw<DomainValidationException>().WithMessage("*resumo*");

        c.Contrato.NovaMedicao(new DateOnly(2026, 9, 28), new DateOnly(2026, 9, 30));
        pintura.DefinirValor(1000);
        c.Contrato.Medir(1, pintura.Id, 10);
        c.Contrato.Aprovar(1, Seg);
        ((Action)(() => c.Orcamento.EstimarQuantidade(pintura, 10, "m²"))).Should().Throw<DomainValidationException>().WithMessage("*medição aprovada*");
    }

    // ---- Candidatas ----

    [Fact]
    public void Candidatas_PelasPalavrasDoNome_OResumoValeMenos_SemAcentoNemPlural()
    {
        var t = Tabela();

        var alvenaria = t.Candidatas("Alvenarias do térreo").Select(c => c.Codigo).ToList();
        alvenaria.First().Should().Be("87495", "a que começa por \"alvenaria\" vem antes das que só a citam");
        alvenaria.Should().BeEquivalentTo(["87495", "87879", "87529"]);
        t.Candidatas("Chapisco", "Vedações > Alvenaria").Select(c => c.Codigo).Should().Equal("87879");
        t.Candidatas("Reboco interno", "Alvenaria").Select(c => c.Codigo).First().Should().Be("87529");
        t.Candidatas("Reboco interno", maximo: 1).Should().HaveCount(1);
        t.Candidatas("de do da").Should().BeEmpty();
        t.Candidatas("Mobilização", "Alvenaria").Should().BeEmpty("só o resumo coincide (meio ponto) não basta");
    }

    // ---- Conferir e aplicar ----

    [Fact]
    public void Aplicar_CadaTipo_UmaVez_NaOrdem()
    {
        var (c, _, alvenaria, pintura, _) = Obra();
        var pedreiro = new Recurso(Guid.NewGuid(), "PEDREIRO", TipoDeRecurso.Profissional, null, 224);
        c.Recursos.Adicionar(pedreiro);
        SugestaoDeOrcamento[] aceitas =
        [
            new(TipoDeSugestao.Bdi, "faixa usual", Valor: 25),
            new(TipoDeSugestao.Composicao, "bloco de 9", alvenaria.Id, Codigo: "87495"),
            new(TipoDeSugestao.Quantidade, "1,2 × 120 × 2,8", alvenaria.Id, Unidade: "m²", Valor: 342.72m),
            new(TipoDeSugestao.Estimativa, "faixa usual", pintura.Id, Descricao: "Pintura acrílica", Unidade: "m²", CustoDeMateriais: 8, CustoDeMaoDeObra: 12),
            new(TipoDeSugestao.Duracao, "2 pintores", pintura.Id, Valor: 6),
            new(TipoDeSugestao.Disponivel, "uma equipe", RecursoId: pedreiro.Id, Valor: 2),
            new(TipoDeSugestao.Alerta, "", Descricao: "Falta o chapisco."),
        ];

        var r = AplicacaoDeSugestoes.Aplicar(c, Tabela(), aceitas);

        (r.Aplicadas, r.Recusadas.Count, r.Resumo).Should().Be((6, 0, "6 sugestões aplicadas."));
        r.Feitas.Select(s => s.Tipo).Should().Equal(TipoDeSugestao.Quantidade, TipoDeSugestao.Duracao, TipoDeSugestao.Composicao, TipoDeSugestao.Estimativa,
            TipoDeSugestao.Disponivel, TipoDeSugestao.Bdi);
        c.Orcamento.ComposicaoDe(alvenaria.Id)!.Codigo.Should().Be("87495");
        c.Orcamento.QuantidadeEhEstimada(alvenaria).Should().BeTrue();
        c.Orcamento.ComposicaoDe(pintura.Id)!.EhEstimativa.Should().BeTrue();
        (pintura.Duracao, c.Recursos.Obter(pedreiro.Id)!.Disponivel, c.Orcamento.Configuracao.Bdi).Should().Be((6, 2m, 25m));
        c.Recursos.Obter(pedreiro.Id)!.Should().Match<Recurso>(p => p.Nome == "PEDREIRO" && p.CustoUnitario == 224);
    }

    [Fact]
    public void Aplicar_AsQueNaoValemMais_RecusadasComOMotivo_AsOutrasEntram()
    {
        var (c, etapa, alvenaria, pintura, entrega) = Obra();
        var cimento = new Recurso(Guid.NewGuid(), "CIMENTO", TipoDeRecurso.Material, "kg", 0.8m);
        c.Recursos.Adicionar(cimento);
        SugestaoDeOrcamento[] aceitas =
        [
            new(TipoDeSugestao.Composicao, "", alvenaria.Id, Codigo: "11111"),
            new(TipoDeSugestao.Composicao, "", etapa.Id, Codigo: "87495"),
            new(TipoDeSugestao.Composicao, "", Guid.NewGuid(), Codigo: "87495"),
            new(TipoDeSugestao.Duracao, "", entrega.Id, Valor: 3),
            new(TipoDeSugestao.Duracao, "", pintura.Id, Valor: 2.5m),
            new(TipoDeSugestao.Disponivel, "", RecursoId: cimento.Id, Valor: 3),
            new(TipoDeSugestao.Bdi, "", Valor: 120),
            new(TipoDeSugestao.Quantidade, "", pintura.Id, Valor: 0),
            new(TipoDeSugestao.Composicao, "", pintura.Id, Codigo: "87529"),
        ];

        var r = AplicacaoDeSugestoes.Aplicar(c, Tabela(), aceitas);

        r.Aplicadas.Should().Be(1);
        r.Recusadas.Should().Equal(
            "3 Entrega: a tarefa é um marco",
            "2 Pintura: duração inválida",
            "2 Pintura: quantidade inválida",
            "1.1 Alvenaria do térreo: a composição 11111 não está na base SINAPI SP 09/2026 desonerado",
            "1 Vedações: a tarefa virou resumo",
            "composicao: a tarefa não existe mais",
            "disponivel: só profissionais e equipamentos têm quantidade por dia",
            "bdi: BDI fora de 0% a 100%");
        r.Resumo.Should().StartWith("1 sugestão aplicada; 8 não: 3 Entrega: a tarefa é um marco;");
        AplicacaoDeSugestoes.Motivo(c, null, new(TipoDeSugestao.Composicao, "", pintura.Id, Codigo: "87495")).Should().Be("nenhuma base escolhida");
        AplicacaoDeSugestoes.Motivo(c, null, new(TipoDeSugestao.Alerta, "", Descricao: "x")).Should().Be("é só um aviso");
    }

    [Fact]
    public void Texto_DeCadaTipo()
    {
        var (c, _, alvenaria, _, _) = Obra();
        var t = Tabela();
        string Texto(SugestaoDeOrcamento s) => AplicacaoDeSugestoes.Texto(s, t, c);

        Texto(new(TipoDeSugestao.Composicao, "", alvenaria.Id, Codigo: "87495")).Should()
            .Be("SINAPI 87495 — ALVENARIA DE VEDAÇÃO DE BLOCOS CERÂMICOS FURADOS 9X19X19 CM (M2) · custo 38,60");
        Texto(new(TipoDeSugestao.Composicao, "", alvenaria.Id, Codigo: "5")).Should().Be("composição 5");
        Texto(new(TipoDeSugestao.Estimativa, "", alvenaria.Id, Descricao: "Pintura", Unidade: "m²", CustoDeMateriais: 8.5m, CustoDeMaoDeObra: 12)).Should()
            .Be("Pintura (m²) · custo estimado 20,5/m² (materiais 8,5; mão de obra 12)");
        Texto(new(TipoDeSugestao.Quantidade, "", alvenaria.Id, Unidade: "m²", Valor: 1342.5m)).Should().Be("quantidade 1.342,5 m²");
        Texto(new(TipoDeSugestao.Duracao, "", alvenaria.Id, Valor: 1)).Should().Be("duração de 1 dia útil");
        Texto(new(TipoDeSugestao.Duracao, "", alvenaria.Id, Valor: 6)).Should().Be("duração de 6 dias úteis");
        Texto(new(TipoDeSugestao.Disponivel, "", Valor: 2)).Should().Be("2 por dia");
        Texto(new(TipoDeSugestao.Bdi, "", Valor: 24.5m)).Should().Be("BDI de 24,5%");
        Texto(new(TipoDeSugestao.Alerta, "", Descricao: "Falta o chapisco.")).Should().Be("Falta o chapisco.");
    }
}
