namespace ProjectPlanner.IA.Tests;

// Bloco 23: o retrato do cronograma mandado ao assistente, as instruções e os anexos aceitos.
public class ContextoEAnexosTests
{
    private static readonly DateOnly Seg = new(2026, 9, 28);

    private static TarefaNoContexto Linha(int numero, string edt, string nome, int nivel = 0, TipoNoContexto tipo = TipoNoContexto.Tarefa,
        int duracao = 3, string predecessoras = "", int? folga = 0, bool critica = true, string? notas = null, int fotos = 0, int percentual = 0) =>
        new(numero, edt, nome, nivel, tipo, duracao, Seg, Seg.AddDays(duracao), percentual, predecessoras, folga, critica, notas, fotos);

    [Fact]
    public void Retrato_CabecalhoLegendaEUmaLinhaPorTarefa()
    {
        var contexto = new ContextoDoProjeto("Obra Residencial", Seg, new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 4),
        [
            Linha(1, "1", "Fundação", tipo: TipoNoContexto.Resumo, duracao: 5),
            Linha(2, "1.1", "Escavar", nivel: 1, duracao: 2, percentual: 100, fotos: 3, notas: "Solo argiloso"),
            Linha(3, "1.2", "Armar", nivel: 1, predecessoras: "2", folga: 2, critica: false),
            Linha(4, "2", "Entrega", tipo: TipoNoContexto.Marco, duracao: 0, predecessoras: "3TT+1", folga: null, critica: false),
        ]);

        var linhas = contexto.ComoTexto().Split('\n');

        linhas[0].Should().Be("Projeto: Obra Residencial");
        linhas[1].Should().Be("Início: 28/09/2026 · Término: 08/10/2026 · Hoje: 04/10/2026");
        linhas[2].Should().StartWith("Calendário: dias úteis de segunda a sexta");
        linhas[3].Should().Contain("TI = término-início").And.Contain("II = início-início");
        linhas[5].Should().Be("Tarefas (4):");
        linhas[6].Should().Be("Nº | EDT | Nome | Tipo | Duração | Início | Término | % concluído | Predecessoras | Folga | Crítica | Fotos | Notas");
        linhas[7..].Should().Equal(
            "1 | 1 | Fundação | resumo | 5 dias | 28/09/2026 | 03/10/2026 | 0% | - | 0 d | sim | 0 | -",
            "2 | 1.1 |   Escavar | tarefa | 2 dias | 28/09/2026 | 30/09/2026 | 100% | - | 0 d | sim | 3 | Solo argiloso",
            "3 | 1.2 |   Armar | tarefa | 3 dias | 28/09/2026 | 01/10/2026 | 0% | 2 | 2 d | não | 0 | -",
            "4 | 2 | Entrega | marco | 0 dias | 28/09/2026 | 28/09/2026 | 0% | 3TT+1 | - | não | 0 | -");
    }

    [Fact]
    public void Retrato_TextoComQuebrasEBarras_NaoQuebraATabela_ENotasLongasSaoCortadas()
    {
        var notas = "Linha 1\nLinha | 2\r\n" + new string('x', 600);
        var contexto = new ContextoDoProjeto("Obra | A\nB", Seg, Seg, Seg, [Linha(1, "1", "Laje\nsuperior", duracao: 1, notas: notas)]);

        var texto = contexto.ComoTexto();

        texto.Split('\n')[0].Should().Be("Projeto: Obra / A B");
        var linha = texto.Split('\n')[^1];
        linha.Should().StartWith("1 | 1 | Laje superior | tarefa | 1 dia |");
        var campoNotas = linha.Split(" | ")[^1];
        campoNotas.Should().StartWith("Linha 1 Linha / 2 / xxx").And.EndWith("…");
        campoNotas.Length.Should().Be(ContextoDoProjeto.TamanhoMaximoDasNotas + 1);
    }

    [Fact]
    public void Retrato_SemTarefas_Avisa()
    {
        new ContextoDoProjeto("Novo projeto", Seg, Seg, Seg, []).ComoTexto().Should().EndWith("O projeto ainda não tem tarefas.");
    }

    [Fact]
    public void Instrucoes_ComESemRetrato()
    {
        var contexto = new ContextoDoProjeto("Obra", Seg, Seg, Seg, []);

        InstrucoesDoAssistente.Montar(contexto).Should().Be(InstrucoesDoAssistente.Base + "\n\nCRONOGRAMA ABERTO:\n" + contexto.ComoTexto());
        InstrucoesDoAssistente.Montar(null).Should().EndWith("Nenhum projeto está aberto no momento.");
        InstrucoesDoAssistente.Base.Should().Contain("português do Brasil").And.Contain("sem Markdown").And.Contain(InstrucoesDoAssistente.Alteracoes);
        foreach (var acao in AcoesNoCronograma.Todas)
            InstrucoesDoAssistente.Alteracoes.Should().Contain($"- {acao}:", "cada ação é explicada ao assistente");
        InstrucoesDoAssistente.Alteracoes.Should().Contain("resumos não têm vínculos").And.Contain("Nº da linha").And.Contain("todas ou nenhuma");
    }

    [Theory]
    [InlineData("planta.pdf", true, false, "application/pdf")]
    [InlineData("FOTO.JPG", true, false, "image/jpeg")]
    [InlineData("foto.jpeg", true, false, "image/jpeg")]
    [InlineData("fachada.png", true, false, "image/png")]
    [InlineData("obra.webp", true, false, "image/webp")]
    [InlineData("iphone.heic", true, false, "image/heic")]
    [InlineData("memorial.txt", true, true, null)]
    [InlineData("leia.MD", true, true, null)]
    [InlineData("medicao.csv", true, true, null)]
    [InlineData("dados.json", true, true, null)]
    [InlineData("contrato.docx", false, false, null)]
    [InlineData("orcamento.xlsx", false, false, null)]
    [InlineData("projeto.dwg", false, false, null)]
    [InlineData("LEIAME", false, false, null)]
    public void Anexos_FormatosAceitos(string nome, bool aceito, bool texto, string? mime)
    {
        var anexo = new AnexoIA("/obra/" + nome, 100);

        (AnexosAceitos.Aceita(anexo), AnexosAceitos.EhTexto(anexo), AnexosAceitos.TipoMime(anexo)).Should().Be((aceito, texto, mime));
        (AnexosAceitos.Problema(anexo) is null).Should().Be(aceito);
    }

    [Fact]
    public void Anexos_Problema_ExplicaOFormato_OuOTamanho()
    {
        AnexosAceitos.Problema(new AnexoIA("/obra/contrato.docx", 10)).Should().Be(
            "contrato.docx: o assistente ainda não lê arquivos .docx. Use PDF, imagens (PNG, JPG, WebP, HEIC) e texto (TXT, MD, CSV, JSON); " +
            "um documento do Word ou do Excel pode ser exportado como PDF.");
        AnexosAceitos.Problema(new AnexoIA("/obra/LEIAME", 10)).Should().StartWith("LEIAME: o assistente ainda não lê arquivos sem extensão.");
        AnexosAceitos.Problema(new AnexoIA("/obra/planta.pdf", 15L * 1024 * 1024 + 200_000))
            .Should().Be("planta.pdf: o arquivo tem 15,2 MB; o limite é 14 MB por envio.");
        AnexosAceitos.Problema(new AnexoIA("/obra/planta.pdf", AnexosAceitos.TamanhoMaximoPorEnvio)).Should().BeNull("no limite ainda cabe");
    }

    [Fact]
    public void Anexos_PadroesDoDialogo()
    {
        AnexosAceitos.Padroes.Should().Contain(["*.pdf", "*.png", "*.jpg", "*.heic", "*.txt", "*.csv"]).And.NotContain("*.docx");
    }
}
