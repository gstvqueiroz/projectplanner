using System.IO.Compression;
using System.Net;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Orcamento;

namespace ProjectPlanner.App.Tests;

// Bloco 40: a SINAPI baixada do site da Caixa (formato de 2025 em diante: um ZIP por mês, com a planilha de referência de
// todos os estados). A planilha de teste imita a de verdade: cabeçalho na linha 10, um preço por estado nas abas de insumos e
// as composições na aba Analítico (uma delas "sem custo", com um insumo que a SINAPI não precifica).
public sealed class SinapiDaCaixaTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-sinapi-" + Guid.NewGuid().ToString("N"));

    public SinapiDaCaixaTests() => Directory.CreateDirectory(_pasta);

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private static IReadOnlyList<object?[]> Insumos(decimal fator) =>
    [
        ["SINAPI - Sistema Nacional de Pesquisa de Custos"],
        [], [], [], [], [], [], [], [],
        ["Classificação", "Código do\nInsumo", "Descrição do Insumo", "Unidade", "Origem de\nPreço", "AC", "RJ", "SP"],
        ["MÃO DE OBRA", "88309", "PEDREIRO COM ENCARGOS COMPLEMENTARES", "H", "CR", 20.0, 27.0 * (double)fator, 28.0 * (double)fator],
        ["MÃO DE OBRA", "88316", "SERVENTE COM ENCARGOS COMPLEMENTARES", "H", "CR", 15.0, 19.0, 20.0],
        ["MATERIAL", "7271", "BLOCO CERAMICO DE VEDACAO 9X19X19 CM", "UN", "C", 1.0, 1.1, 1.2],
        ["MATERIAL", "9999", "INSUMO SEM PRECO EM SP", "UN", "C", 3.0, 3.1, null],
    ];

    private static readonly IReadOnlyList<object?[]> Analitico =
    [
        ["SINAPI - Sistema Nacional de Pesquisa de Custos"],
        [], [], [], [], [], [], [], [],
        ["Grupo", "Código da\nComposição", "Tipo Item", "Código do\nItem", "Descrição", "Unidade", "Coeficiente", "Situação"],
        ["Vedações", "87495", null, null, "ALVENARIA DE VEDAÇÃO DE BLOCOS CERÂMICOS 9X19X19 CM", "M2", null, "COM CUSTO"],
        ["Vedações", "87495", "INSUMO", "88309", "PEDREIRO COM ENCARGOS COMPLEMENTARES", "H", 0.8, "COM CUSTO"],
        ["Vedações", "87495", "INSUMO", "88316", "SERVENTE COM ENCARGOS COMPLEMENTARES", "H", 0.4, "COM CUSTO"],
        ["Vedações", "87495", "INSUMO", "7271", "BLOCO CERAMICO", "UN", 13.5, "COM CUSTO"],
        ["Acessibilidade", "105006", null, null, "RAMPA COM KIT PRÉ-MOLDADO", "UN", null, "SEM CUSTO"],
        ["Acessibilidade", "105006", "INSUMO", "45087", "KIT PECAS PRE MOLDADAS PARA RAMPA", "UN", 1.0, "SEM PREÇO"],
    ];

    /// <summary>A planilha de referência de teste, com as abas como a da Caixa (e um Menu antes, como a de verdade).</summary>
    private string Referencia(string nome = "SINAPI_Referência_2026_09.xlsx")
    {
        var caminho = Path.Combine(_pasta, nome);
        using var documento = SpreadsheetDocument.Create(caminho, SpreadsheetDocumentType.Workbook);
        var livro = documento.AddWorkbookPart();
        livro.Workbook = new Workbook();
        var abas = livro.Workbook.AppendChild(new Sheets());
        uint id = 1;
        foreach (var (aba, linhas) in new[] { ("Menu", (IReadOnlyList<object?[]>)[["Menu"]]), ("ISD", Insumos(1)), ("ICD", Insumos(0.9m)), ("Analítico", Analitico) })
        {
            var parte = livro.AddNewPart<WorksheetPart>();
            var dados = new SheetData();
            for (var l = 0; l < linhas.Count; l++)
            {
                var linha = new Row { RowIndex = (uint)(l + 1) };
                for (var c = 0; c < linhas[l].Length; c++)
                {
                    var referencia = $"{(char)('A' + c)}{l + 1}";
                    switch (linhas[l][c])
                    {
                        case string texto:
                            linha.Append(new Cell { CellReference = referencia, DataType = CellValues.InlineString, InlineString = new InlineString(new Text(texto)) });
                            break;
                        case double numero:
                            linha.Append(new Cell { CellReference = referencia, CellValue = new CellValue(numero) });
                            break;
                    }
                }
                dados.Append(linha);
            }
            parte.Worksheet = new Worksheet(dados);
            abas.Append(new Sheet { Id = livro.GetIdOfPart(parte), SheetId = id++, Name = aba });
        }
        return caminho;
    }

    [Fact]
    public void Importar_OsInsumosDaColunaDoEstado_NoRegimeEscolhido_EAsComposicoesDoAnalitico()
    {
        var planilha = Referencia();

        var (tabela, avisos) = ImportacaoDoSinapi.Importar(planilha, new BaseDeReferencia("SINAPI", "sp", "09/2026", false));

        tabela.Base.Descricao.Should().Be("SINAPI SP 09/2026 não desonerado");
        tabela.Insumos.Where(i => i.Codigo != "45087").Select(i => (i.Codigo, i.Tipo, i.Preco)).Should().BeEquivalentTo(new[]
        {
            ("88309", TipoDeInsumo.MaoDeObra, 28m), ("88316", TipoDeInsumo.MaoDeObra, 20m), ("7271", TipoDeInsumo.Material, 1.2m),
        });
        avisos.Should().Be(1, "o insumo sem preço em SP é pulado");
        tabela.Composicoes.Select(c => (c.Codigo, c.Unidade)).Should().BeEquivalentTo(new[] { ("87495", "M2"), ("105006", "UN") });
        // 0,8 × 28 + 0,4 × 20 + 13,5 × 1,2 = 22,40 + 8,00 + 16,20 = 46,60.
        tabela.Abrir("87495").CustoUnitario.Should().Be(46.60m);
        FluentActions.Invoking(() => tabela.Abrir("105006")).Should().Throw<DomainValidationException>("a própria SINAPI marca sem custo");

        var (desonerada, _) = ImportacaoDoSinapi.Importar(planilha, new BaseDeReferencia("SINAPI", "SP", "09/2026", true));
        desonerada.Insumos.Single(i => i.Codigo == "88309").Preco.Should().Be(25.2m, "a aba ICD");
        var (rj, _) = ImportacaoDoSinapi.Importar(planilha, new BaseDeReferencia("SINAPI", "RJ", "09/2026", false));
        rj.Insumos.Single(i => i.Codigo == "7271").Preco.Should().Be(1.1m);
        FluentActions.Invoking(() => ImportacaoDoSinapi.Importar(planilha, new BaseDeReferencia("SINAPI", "XX", "09/2026", false)))
            .Should().Throw<DomainValidationException>().WithMessage("Informe a UF*");
        FluentActions.Invoking(() => ImportacaoDeReferencia.Ler(ProjectPlanner.Relatorios.LeitorDePlanilha.LerAba(planilha, "ISD")))
            .Should().Throw<DomainValidationException>("sem a UF, uma planilha com preço por estado não tem a coluna Preço");
        FluentActions.Invoking(() => ProjectPlanner.Relatorios.LeitorDePlanilha.LerAba(planilha, "CSD")).Should().Throw<InvalidDataException>()
            .WithMessage("A planilha não tem a aba CSD.");
    }

    // ---- O site da Caixa, de mentira ----

    private sealed class CaixaDeTeste : HttpMessageHandler
    {
        public HashSet<string> Existentes { get; } = [];
        public byte[] Zip { get; set; } = [];
        public List<(HttpMethod Metodo, string Arquivo)> Pedidos { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken c)
        {
            var arquivo = Path.GetFileName(pedido.RequestUri!.LocalPath);
            Pedidos.Add((pedido.Method, arquivo));
            if (!Existentes.Contains(arquivo))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(pedido.Method == HttpMethod.Head ? [] : Zip) });
        }
    }

    private byte[] ZipCom(params (string Nome, byte[] Conteudo)[] arquivos)
    {
        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (nome, conteudo) in arquivos)
            {
                using var entrada = zip.CreateEntry(nome).Open();
                entrada.Write(conteudo);
            }
        return memoria.ToArray();
    }

    [Fact]
    public async Task Procurar_DoMesAtualParaTras_ARetificacaoMaisNovaPrimeiro()
    {
        var caixa = new CaixaDeTeste();
        caixa.Existentes.UnionWith(["SINAPI-2026-09-formato-xlsx.zip", "SINAPI-2026-09-formato-xlsx_Retificacao01.zip", "SINAPI-2026-08-formato-xlsx.zip"]);
        var sinapi = new SinapiDaCaixa(new HttpClient(caixa));

        var achado = await sinapi.ProcurarAsync(new DateOnly(2026, 10, 25), default);

        (achado!.Nome, achado.Referencia).Should().Be(("SINAPI-2026-09-formato-xlsx_Retificacao01.zip", "09/2026"));
        caixa.Pedidos.Should().OnlyContain(p => p.Metodo == HttpMethod.Head, "procurar não baixa nada");
        caixa.Pedidos.Select(p => p.Arquivo).Should().StartWith(["SINAPI-2026-10-formato-xlsx_Retificacao03.zip", "SINAPI-2026-10-formato-xlsx_Retificacao02.zip",
            "SINAPI-2026-10-formato-xlsx_Retificacao01.zip", "SINAPI-2026-10-formato-xlsx.zip"]);
        caixa.Existentes.Clear();
        (await sinapi.ProcurarAsync(new DateOnly(2026, 10, 25), default)).Should().BeNull();
        SinapiDaCaixa.Candidatos(new DateOnly(2026, 1, 5)).Select(a => a.Referencia).Distinct().Should().Equal("01/2026", "12/2025", "11/2025", "10/2025", "09/2025", "08/2025");
    }

    [Fact]
    public async Task Baixar_TiraSoAPlanilhaDeReferencia_ERecusaZipSemElaOuCorrompido()
    {
        var caixa = new CaixaDeTeste();
        caixa.Existentes.Add("SINAPI-2026-09-formato-xlsx.zip");
        var arquivo = new ArquivoDoSinapi(new Uri(SinapiDaCaixa.Pasta + "SINAPI-2026-09-formato-xlsx.zip"), 2026, 9);
        var sinapi = new SinapiDaCaixa(new HttpClient(caixa));
        var referencia = await File.ReadAllBytesAsync(Referencia());
        caixa.Zip = ZipCom(("SINAPI_familias_e_coeficientes_2026_09.xlsx", [1]), ("../../fora/SINAPI_Referência_2026_09.xlsx", referencia));
        var destino = Path.Combine(_pasta, "baixado");

        var planilha = await sinapi.BaixarAsync(arquivo, destino, default);

        planilha.Should().Be(Path.Combine(destino, "SINAPI_Referencia.xlsx"), "o caminho é do programa, não do ZIP");
        (await File.ReadAllBytesAsync(planilha)).Should().Equal(referencia);
        caixa.Zip = ZipCom(("SINAPI_mao_de_obra_2026_09.xlsx", [1]));
        (await FluentActions.Awaiting(() => sinapi.BaixarAsync(arquivo, destino, default)).Should().ThrowAsync<InvalidDataException>())
            .WithMessage("O arquivo da Caixa não tem a planilha de referência*");
        caixa.Zip = [1, 2, 3];
        (await FluentActions.Awaiting(() => sinapi.BaixarAsync(arquivo, destino, default)).Should().ThrowAsync<InvalidDataException>())
            .WithMessage("O arquivo da Caixa veio corrompido*");
        caixa.Existentes.Clear();
        await FluentActions.Awaiting(() => sinapi.BaixarAsync(arquivo, destino, default)).Should().ThrowAsync<HttpRequestException>();
    }

    // ---- Na tela: Projeto > Orçamento, aba Bases ----

    private sealed class SinapiFalsa(string planilha) : ISinapiDaCaixa
    {
        public ArquivoDoSinapi? Achado { get; set; } = new(new Uri(SinapiDaCaixa.Pasta + "SINAPI-2026-09-formato-xlsx.zip"), 2026, 9);
        public Exception? Erro { get; set; }

        public Task<ArquivoDoSinapi?> ProcurarAsync(DateOnly hoje, CancellationToken c) => Task.FromResult(Achado);

        public Task<string> BaixarAsync(ArquivoDoSinapi arquivo, string pasta, CancellationToken c)
        {
            if (Erro is not null)
                throw Erro;
            Directory.CreateDirectory(pasta);
            var copia = Path.Combine(pasta, "SINAPI_Referencia.xlsx");
            File.Copy(planilha, copia);
            return Task.FromResult(copia);
        }
    }

    [Fact]
    public async Task Tela_BaixarDaCaixa_MontaABaseDoEstadoEEscolhe_EComProblemasAvisa()
    {
        var ambiente = new Ambiente();
        var vm = ambiente.CriarViewModel();
        var sinapi = new SinapiFalsa(Referencia());
        vm.SinapiDaCaixa = sinapi;
        vm.OrcamentoCommand.Execute(null);
        var tela = ambiente.Dialogos.Orcamentos.Last();

        await tela.BaixarSinapiCommand.ExecuteAsync(null);
        tela.Mensagem.Should().Be("Informe a UF do estado da obra (por exemplo, SP) e clique de novo.");

        tela.Uf = "sp";
        await tela.BaixarSinapiCommand.ExecuteAsync(null);

        tela.Mensagem.Should().StartWith("SINAPI SP 09/2026 não desonerado: 3 insumos e 2 composições, baixados da Caixa.")
            .And.EndWith(" 1 linha da planilha foi pulada.");
        (tela.BaseEscolhida!.Valor.Descricao, tela.Uf, tela.Referencia, tela.Ocupado).Should().Be(("SINAPI SP 09/2026 não desonerado", "SP", "09/2026", false));
        ambiente.Biblioteca.Listar().Single().Composicoes.Should().Be(2);

        sinapi.Achado = null;
        await tela.BaixarSinapiCommand.ExecuteAsync(null);
        tela.Mensagem.Should().StartWith("Não achei a SINAPI dos últimos meses");
        sinapi.Achado = new(new Uri(SinapiDaCaixa.Pasta + "x.zip"), 2026, 9);
        sinapi.Erro = new HttpRequestException("caiu");
        await tela.BaixarSinapiCommand.ExecuteAsync(null);
        tela.Mensagem.Should().Be("Sem conexão com o site da Caixa. Verifique a internet e tente de novo.");
        sinapi.Erro = new InvalidDataException("O arquivo da Caixa veio corrompido. Tente de novo.");
        await tela.BaixarSinapiCommand.ExecuteAsync(null);
        tela.Mensagem.Should().Be("Não foi possível montar a base: O arquivo da Caixa veio corrompido. Tente de novo.");
    }
}
