using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ProjectPlanner.IA.Gemini;

namespace ProjectPlanner.IA.Tests;

// Bloco 23: o assistente do Gemini contra um servidor falso (nenhum teste chama a API de verdade).
public sealed class GeminiTests : IDisposable
{
    private const string Chave = "AIzaChaveDeTesteSoParaOsTestes123456789";
    private const string Endereco = "https://generativelanguage.googleapis.com/v1beta/";

    private readonly ManipuladorFalso _servidor = new();
    private readonly HttpClient _http;
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-gemini-" + Guid.NewGuid().ToString("N"));

    public GeminiTests()
    {
        _http = new HttpClient(_servidor) { Timeout = TimeSpan.FromSeconds(10) };
        Directory.CreateDirectory(_pasta);
    }

    public void Dispose()
    {
        _http.Dispose();
        Directory.Delete(_pasta, recursive: true);
    }

    private AssistenteGemini Assistente(string modelo = "gemini-2.5-flash") => new(_http, Chave, modelo);

    private static PedidoIA Pedido(string mensagem, IReadOnlyList<AnexoIA>? anexos = null, IReadOnlyList<MensagemChat>? historico = null,
        ContextoDoProjeto? contexto = null) => new(mensagem, anexos ?? [], historico ?? [], contexto);

    private static MensagemChat Do(AutorMensagem autor, string texto, IReadOnlyList<AnexoIA>? anexos = null, bool erro = false) =>
        new(autor, texto, DateTimeOffset.UnixEpoch, anexos ?? [], erro);

    private AnexoIA Arquivo(string nome, byte[] conteudo)
    {
        var caminho = Path.Combine(_pasta, nome);
        File.WriteAllBytes(caminho, conteudo);
        return AnexoIA.DoArquivo(caminho);
    }

    private static ContextoDoProjeto Obra() => new("Obra Teste", new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 2), new DateOnly(2026, 9, 30),
        [new TarefaNoContexto(1, "1", "Fundação", 0, TipoNoContexto.Tarefa, 5, new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 2), 40, "", 0, true, null, 2)]);

    private static IEnumerable<(string Papel, string Textos)> Conteudos(JsonNode corpo) =>
        corpo["contents"]!.AsArray().Select(c => (c!["role"]!.GetValue<string>(),
            string.Join(" + ", c["parts"]!.AsArray().Select(p => p!["text"]?.GetValue<string>() ?? $"[{p!["inlineData"]!["mimeType"]}]"))));

    // ---- O pedido ----

    [Fact]
    public async Task Enviar_ChaveNoCabecalho_NuncaNaUrl_EOModeloNoEndereco()
    {
        var resposta = await Assistente().EnviarAsync(Pedido("Qual é o caminho crítico?"), CancellationToken.None);

        resposta.Texto.Should().Be("Olá!");
        var (pedido, _) = _servidor.Pedidos.Single();
        pedido.Method.Should().Be(HttpMethod.Post);
        pedido.RequestUri!.ToString().Should().Be(Endereco + "models/gemini-2.5-flash:generateContent");
        pedido.RequestUri.ToString().Should().NotContain(Chave);
        pedido.Headers.GetValues("x-goog-api-key").Should().Equal(Chave);
        pedido.Content!.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task Enviar_InstrucoesComORetratoDoCronograma()
    {
        await Assistente().EnviarAsync(Pedido("Quanto falta?", contexto: Obra()), CancellationToken.None);

        var instrucoes = _servidor.UltimoCorpo["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>();
        instrucoes.Should().StartWith(InstrucoesDoAssistente.Base).And.Contain("CRONOGRAMA ABERTO:")
            .And.Contain("Projeto: Obra Teste").And.Contain("1 | 1 | Fundação | tarefa | 5 dias");
        Conteudos(_servidor.UltimoCorpo).Should().Equal(("user", "Quanto falta?"));
    }

    // Bloco 24: o Gemini é obrigado a responder no formato {mensagem, alteracoes}.
    [Fact]
    public async Task Enviar_PedeRespostaEmJson_ComOEsquemaDasAlteracoes()
    {
        await Assistente().EnviarAsync(Pedido("Monte o cronograma"), CancellationToken.None);

        var configuracao = _servidor.UltimoCorpo["generationConfig"]!;
        configuracao["responseMimeType"]!.GetValue<string>().Should().Be("application/json");
        var esquema = configuracao["responseSchema"]!;
        esquema["required"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal("mensagem", "alteracoes");
        var item = esquema["properties"]!["alteracoes"]!["items"]!;
        item["properties"]!["acao"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal(AcoesNoCronograma.Todas);
        item["properties"]!["tipo"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal("TI", "II", "TT", "IT");
        item["properties"]!["duracao"]!["type"]!.GetValue<string>().Should().Be("INTEGER");
    }

    [Fact]
    public async Task Resposta_EmJson_TrazAMensagemEAsAlteracoes()
    {
        const string json = """
            {"mensagem": "Montei a fundação.", "alteracoes": [
              {"acao": "criar", "ref": "n1", "nome": "Fundação"},
              {"acao": "criar", "ref": "n2", "nome": "Escavar", "duracao": 2, "pai": "n1"},
              {"acao": "vincular", "de": "n2", "para": "3", "tipo": "II", "defasagem": 1}]}
            """;
        _servidor.Responder = (_, _) => Task.FromResult(ManipuladorFalso.Json(200, ManipuladorFalso.Resposta(json)));

        var resposta = await Assistente().EnviarAsync(Pedido("Monte a fundação"), CancellationToken.None);

        resposta.Texto.Should().Be("Montei a fundação.");
        resposta.Alteracoes.Should().Equal(
            new AlteracaoDoCronograma("criar", Ref: "n1", Nome: "Fundação"),
            new AlteracaoDoCronograma("criar", Ref: "n2", Nome: "Escavar", Duracao: 2, Pai: "n1"),
            new AlteracaoDoCronograma("vincular", De: "n2", Para: "3", Tipo: "II", Defasagem: 1));
    }

    [Fact]
    public async Task Resposta_EmJsonCortadaPeloTamanho_NaoAlteraNada()
    {
        const string cortado = """{"mensagem": "Montei", "alteracoes": [{"acao": "criar", "ref": "n1", "no""";
        _servidor.Responder = (_, _) => Task.FromResult(ManipuladorFalso.Json(200, ManipuladorFalso.Resposta(cortado, "MAX_TOKENS")));

        var enviar = () => Assistente().EnviarAsync(Pedido("Monte tudo"), CancellationToken.None);

        await enviar.Should().ThrowAsync<ErroDoAssistenteException>().WithMessage("A resposta do assistente ficou longa demais e veio cortada*");
    }

    [Fact]
    public async Task Enviar_SemContexto_AvisaQueNaoHaProjeto()
    {
        await Assistente().EnviarAsync(Pedido("Oi"), CancellationToken.None);

        _servidor.UltimoCorpo["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>().Should().EndWith("Nenhum projeto está aberto no momento.");
    }

    [Fact]
    public async Task Enviar_Historico_SemOsAvisos_EFalasSeguidasDoMesmoAutorViramUma()
    {
        var historico = new[]
        {
            Do(AutorMensagem.Usuario, "Oi"),
            Do(AutorMensagem.Assistente, "Olá! Em que posso ajudar?"),
            Do(AutorMensagem.Usuario, "Qual a folga da tarefa 2?"),
            Do(AutorMensagem.Assistente, "Não foi possível obter a resposta: sem conexão.", erro: true),
        };

        await Assistente().EnviarAsync(Pedido("E da tarefa 3?", historico: historico), CancellationToken.None);

        Conteudos(_servidor.UltimoCorpo).Should().Equal(
            ("user", "Oi"),
            ("model", "Olá! Em que posso ajudar?"),
            ("user", "Qual a folga da tarefa 2? + E da tarefa 3?"));
    }

    [Fact]
    public async Task Enviar_MensagemVaziaSemAnexos_RecusadaSemChamarAApi()
    {
        var enviar = () => Assistente().EnviarAsync(Pedido("   "), CancellationToken.None);

        await enviar.Should().ThrowAsync<ErroDoAssistenteException>().WithMessage("Escreva uma mensagem ou anexe um documento.");
        _servidor.Pedidos.Should().BeEmpty();
    }

    // ---- A resposta ----

    [Fact]
    public async Task Resposta_JuntaAsPartes_SemOsPensamentos()
    {
        _servidor.Responder = (_, _) => Task.FromResult(ManipuladorFalso.Json(200, """
            {"candidates": [{"content": {"parts": [{"text": "A tarefa 2 "}, {"text": "pensando...", "thought": true},
             {"text": "tem 3 dias de folga."}]}, "finishReason": "STOP"}]}
            """));

        (await Assistente().EnviarAsync(Pedido("Folga?"), CancellationToken.None)).Texto.Should().Be("A tarefa 2 tem 3 dias de folga.");
    }

    [Fact]
    public async Task Resposta_CortadaPeloTamanho_Avisa()
    {
        _servidor.Responder = (_, _) => Task.FromResult(ManipuladorFalso.Json(200, ManipuladorFalso.Resposta("Começo da resposta", "MAX_TOKENS")));

        (await Assistente().EnviarAsync(Pedido("Explique tudo"), CancellationToken.None)).Texto
            .Should().Be("Começo da resposta\n\n(Resposta cortada: ficou longa demais.)");
    }

    [Theory]
    [InlineData("""{"promptFeedback": {"blockReason": "SAFETY"}}""", "O Gemini recusou a mensagem (motivo: SAFETY).")]
    [InlineData("""{"candidates": [{"finishReason": "SAFETY"}]}""", "O Gemini não respondeu por um bloqueio de segurança (SAFETY).")]
    [InlineData("""{"candidates": [{"content": {"parts": []}, "finishReason": "OTHER"}]}""", "O Gemini devolveu uma resposta vazia (OTHER).")]
    [InlineData("""{"candidates": []}""", "O Gemini não devolveu nenhuma resposta. Tente de novo.")]
    [InlineData("""{}""", "O Gemini não devolveu nenhuma resposta. Tente de novo.")]
    [InlineData("""[1, 2]""", "O Gemini mandou uma resposta que não deu para entender. Tente de novo.")]
    [InlineData("""isto não é json""", "O Gemini mandou uma resposta que não deu para entender. Tente de novo.")]
    public async Task Resposta_SemTexto_ViraErroAmigavel(string json, string mensagem)
    {
        _servidor.Responder = (_, _) => Task.FromResult(ManipuladorFalso.Json(200, json));

        var enviar = () => Assistente().EnviarAsync(Pedido("Oi"), CancellationToken.None);

        await enviar.Should().ThrowAsync<ErroDoAssistenteException>().WithMessage(mensagem);
    }

    // ---- Falhas ----

    public static TheoryData<int, string, string> Erros => new()
    {
        { 400, ManipuladorFalso.Erro(400, "API key not valid. Please pass a valid API key.", "API_KEY_INVALID"),
            "A chave do Gemini não é válida. Confira a chave nas configurações do assistente." },
        { 400, ManipuladorFalso.Erro(400, "User location is not supported for the API use."),
            "O Gemini recusou o pedido: User location is not supported for the API use." },
        { 403, ManipuladorFalso.Erro(403, "Permission denied."),
            "A chave do Gemini não tem permissão para este uso. Confira no Google AI Studio se ela está ativa (Permission denied.)." },
        { 404, ManipuladorFalso.Erro(404, "models/gemini-9 is not found"),
            "O modelo \"gemini-2.5-flash\" não existe ou não está disponível para esta chave. Confira o nome do modelo nas configurações do assistente." },
        { 429, ManipuladorFalso.Erro(429, "Resource has been exhausted"),
            "O limite de uso do Gemini foi atingido. Espere um pouco e tente de novo (a cota da chave aparece no Google AI Studio)." },
        { 503, "", "O Gemini está com problemas no momento (erro 503). Tente de novo em instantes." },
        { 418, "não é json", "O Gemini respondeu com erro 418." },
    };

    [Theory]
    [MemberData(nameof(Erros))]
    public async Task ErroDaApi_ViraMensagemAmigavel(int status, string corpo, string mensagem)
    {
        _servidor.Responder = (_, _) => Task.FromResult(ManipuladorFalso.Json(status, corpo));

        var enviar = () => Assistente().EnviarAsync(Pedido("Oi"), CancellationToken.None);

        (await enviar.Should().ThrowAsync<ErroDoAssistenteException>()).Which.Message.Should().Be(mensagem);
    }

    [Fact]
    public async Task ErroDaApi_QueRepeteAChave_NaoAMostra()
    {
        _servidor.Responder = (_, _) => Task.FromResult(ManipuladorFalso.Json(400, ManipuladorFalso.Erro(400, $"Bad key {Chave} here")));

        var enviar = () => Assistente().EnviarAsync(Pedido("Oi"), CancellationToken.None);

        var erro = (await enviar.Should().ThrowAsync<ErroDoAssistenteException>()).Which;
        erro.Message.Should().Be("O Gemini recusou o pedido: Bad key (chave) here").And.NotContain(Chave);
    }

    [Fact]
    public async Task SemConexao_Avisa()
    {
        _servidor.Responder = (_, _) => throw new HttpRequestException("Name or service not known");

        var enviar = () => Assistente().EnviarAsync(Pedido("Oi"), CancellationToken.None);

        await enviar.Should().ThrowAsync<ErroDoAssistenteException>().WithMessage("Sem conexão com o Gemini. Verifique a internet e tente de novo.");
    }

    [Fact]
    public async Task TempoLimite_Avisa_SemConfundirComCancelamento()
    {
        using var http = new HttpClient(_servidor) { Timeout = TimeSpan.FromMilliseconds(100) };
        _servidor.Responder = async (_, cancelamento) =>
        {
            await Task.Delay(Timeout.Infinite, cancelamento);
            return ManipuladorFalso.Json(200, "{}");
        };

        var enviar = () => new AssistenteGemini(http, Chave, "gemini-2.5-flash").EnviarAsync(Pedido("Oi"), CancellationToken.None);

        await enviar.Should().ThrowAsync<ErroDoAssistenteException>()
            .WithMessage("O Gemini demorou mais de 1 segundo para responder. Tente de novo (com anexos menores, se houver).");
    }

    [Fact]
    public async Task CanceladoPeloUsuario_ContinuaCancelamento()
    {
        using var cancelamento = new CancellationTokenSource();
        _servidor.Responder = async (_, c) =>
        {
            await Task.Delay(Timeout.Infinite, c);
            return ManipuladorFalso.Json(200, "{}");
        };

        var envio = Assistente().EnviarAsync(Pedido("Oi"), cancelamento.Token);
        await cancelamento.CancelAsync();

        await envio.Invoking(e => e).Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(0.05, "1 segundo")]
    [InlineData(30, "30 segundos")]
    [InlineData(60, "1 minuto")]
    [InlineData(120, "2 minutos")]
    [InlineData(90, "1,5 minutos")]
    public void Duracao_EmPortugues(double segundos, string esperado)
    {
        ChamadaGemini.Duracao(TimeSpan.FromSeconds(segundos)).Should().Be(esperado);
    }

    // ---- Anexos ----

    [Fact]
    public async Task Anexos_PdfEImagemVaoComoArquivo_TextoVaiLido_AntesDaMensagem()
    {
        var pdf = Arquivo("planta.pdf", "%PDF-1.4 conteúdo"u8.ToArray());
        var foto = Arquivo("FACHADA.JPG", [0xFF, 0xD8, 0xFF, 0xE0]);
        var nota = Arquivo("memorial.txt", Encoding.UTF8.GetBytes("Concreto fck 30 MPa."));

        await Assistente().EnviarAsync(Pedido("Confere com o cronograma?", [pdf, foto, nota]), CancellationToken.None);

        var partes = _servidor.UltimoCorpo["contents"]![0]!["parts"]!.AsArray();
        partes.Select(p => p!["text"]?.GetValue<string>() ?? $"[{p!["inlineData"]!["mimeType"]}]").Should().Equal(
            "Anexo: planta.pdf", "[application/pdf]",
            "Anexo: FACHADA.JPG", "[image/jpeg]",
            "Conteúdo do anexo \"memorial.txt\":\nConcreto fck 30 MPa.",
            "Confere com o cronograma?");
        Convert.FromBase64String(partes[1]!["inlineData"]!["data"]!.GetValue<string>()).Should().Equal("%PDF-1.4 conteúdo"u8.ToArray());
    }

    [Fact]
    public async Task Anexos_SoOAnexoSemTexto_Vai()
    {
        var pdf = Arquivo("contrato.pdf", [1, 2, 3]);

        await Assistente().EnviarAsync(Pedido("", [pdf]), CancellationToken.None);

        Conteudos(_servidor.UltimoCorpo).Should().Equal(("user", "Anexo: contrato.pdf + [application/pdf]"));
    }

    [Fact]
    public async Task Anexos_DoHistorico_SaoReenviados_EOsQueSumiramViramNota()
    {
        var planta = Arquivo("planta.pdf", [1, 2, 3]);
        var velho = Arquivo("velho.pdf", [4, 5]);
        File.Delete(velho.CaminhoLocal);
        var historico = new[]
        {
            Do(AutorMensagem.Usuario, "Veja a planta", [planta, velho]),
            Do(AutorMensagem.Assistente, "Vi."),
        };

        await Assistente().EnviarAsync(Pedido("E a escada?", historico: historico), CancellationToken.None);

        Conteudos(_servidor.UltimoCorpo).Should().Equal(
            ("user", "Anexo: planta.pdf + [application/pdf] + [O anexo velho.pdf, de uma mensagem anterior, não está mais disponível.] + Veja a planta"),
            ("model", "Vi."),
            ("user", "E a escada?"));
    }

    [Fact]
    public async Task Anexos_DoHistoricoEmFormatoNaoAceito_ViramNota()
    {
        var historico = new[] { Do(AutorMensagem.Usuario, "Contrato", [new AnexoIA(Path.Combine(_pasta, "contrato.docx"), 10)]), Do(AutorMensagem.Assistente, "Ok.") };

        await Assistente().EnviarAsync(Pedido("E então?", historico: historico), CancellationToken.None);

        Conteudos(_servidor.UltimoCorpo).First().Textos
            .Should().Be("[O anexo contrato.docx, de uma mensagem anterior, não foi enviado: formato não aceito.] + Contrato");
    }

    // ---- Bloco 46: o corte do histórico e dos anexos antigos ----

    private static MensagemChat[] Conversa(int pares) =>
    [
        .. Enumerable.Range(1, pares).SelectMany(i => new[]
        {
            Do(AutorMensagem.Usuario, $"Pergunta {i}"),
            Do(AutorMensagem.Assistente, $"Resposta {i}"),
        }),
    ];

    [Fact]
    public async Task Historico_Longo_VaoSoAsUltimasMensagens()
    {
        var historico = Conversa(15); // 30 mensagens

        await Assistente().EnviarAsync(Pedido("E agora?", historico: historico), CancellationToken.None);

        var conteudos = Conteudos(_servidor.UltimoCorpo).ToList();
        conteudos.Should().HaveCount(LimitesDaConversa.MensagensDoHistorico + 1);
        conteudos.First().Should().Be(("user", "Pergunta 6"));
        conteudos.SkipLast(1).Last().Should().Be(("model", "Resposta 15"));
        conteudos.Last().Should().Be(("user", "E agora?"));
    }

    [Fact]
    public async Task Historico_CorteQueComecariaPeloAssistente_ComecaPeloUsuario()
    {
        // Termina numa pergunta sem resposta (a resposta falhou): as últimas 20 começam por "Resposta 6".
        MensagemChat[] historico = [.. Conversa(15), Do(AutorMensagem.Usuario, "Pergunta 16")];

        await Assistente().EnviarAsync(Pedido("E agora?", historico: historico), CancellationToken.None);

        var conteudos = Conteudos(_servidor.UltimoCorpo).ToList();
        conteudos.First().Should().Be(("user", "Pergunta 7"));
        conteudos.Last().Should().Be(("user", "Pergunta 16 + E agora?"));
        conteudos.Should().HaveCount(LimitesDaConversa.MensagensDoHistorico - 1);
    }

    [Fact]
    public async Task Historico_Curto_VaiInteiro()
    {
        var historico = Conversa(LimitesDaConversa.MensagensDoHistorico / 2);

        await Assistente().EnviarAsync(Pedido("E agora?", historico: historico), CancellationToken.None);

        Conteudos(_servidor.UltimoCorpo).Should().HaveCount(LimitesDaConversa.MensagensDoHistorico + 1)
            .And.StartWith(("user", "Pergunta 1"));
    }

    [Fact]
    public async Task Anexos_Antigos_ViramNota_EOsRecentesEOAtualContinuamIndo()
    {
        var antigo = Arquivo("antigo.pdf", [1]);
        var penultimo = Arquivo("penultimo.pdf", [2]);
        var ultimo = Arquivo("ultimo.png", [3]);
        var atual = Arquivo("atual.pdf", [4]);
        var historico = new[]
        {
            Do(AutorMensagem.Usuario, "Veja o contrato", [antigo]),
            Do(AutorMensagem.Assistente, "Vi."),
            Do(AutorMensagem.Usuario, "Veja a planta", [penultimo]),
            Do(AutorMensagem.Assistente, "Vi."),
            Do(AutorMensagem.Usuario, "Veja a foto", [ultimo]),
            Do(AutorMensagem.Assistente, "Vi."),
        };

        await Assistente().EnviarAsync(Pedido("E o memorial?", [atual], historico), CancellationToken.None);

        Conteudos(_servidor.UltimoCorpo).Where(c => c.Papel == "user").Select(c => c.Textos).Should().Equal(
            "(anexo: antigo.pdf, enviado antes) + Veja o contrato",
            "Anexo: penultimo.pdf + [application/pdf] + Veja a planta",
            "Anexo: ultimo.png + [image/png] + Veja a foto",
            "Anexo: atual.pdf + [application/pdf] + E o memorial?");
    }

    [Fact]
    public async Task Anexos_Antigos_NaoSaoLidosDoDisco_NemContamNoLimite()
    {
        var sumiu = Arquivo("sumiu.pdf", [1]);
        File.Delete(sumiu.CaminhoLocal);
        var historico = new[]
        {
            Do(AutorMensagem.Usuario, "Veja", [sumiu, new AnexoIA(Path.Combine(_pasta, "contrato.docx"), 10)]),
            Do(AutorMensagem.Assistente, "Vi."),
            Do(AutorMensagem.Usuario, "Oi"),
            Do(AutorMensagem.Assistente, "Olá."),
            Do(AutorMensagem.Usuario, "Tudo bem?"),
            Do(AutorMensagem.Assistente, "Sim."),
        };

        await Assistente().EnviarAsync(Pedido("E então?", historico: historico), CancellationToken.None);

        Conteudos(_servidor.UltimoCorpo).First().Textos
            .Should().Be("(anexo: sumiu.pdf, enviado antes) + (anexo: contrato.docx, enviado antes) + Veja");
    }

    [Fact]
    public async Task Historico_Cortado_RespostaEstruturadaContinuaIndoComoJson()
    {
        var perguntas = new RespostaIA("Antes, uma pergunta.")
        {
            Tipo = TipoDeResposta.Perguntas,
            Perguntas = [new PerguntaIA("Qual a estrutura?", ["Concreto armado", "Alvenaria estrutural"])],
        };
        MensagemChat[] historico =
        [
            .. Conversa(12),
            Do(AutorMensagem.Usuario, "Monte a casa"),
            new MensagemChat(AutorMensagem.Assistente, perguntas.Texto, DateTimeOffset.UnixEpoch, [], Resposta: perguntas),
        ];

        await Assistente().EnviarAsync(Pedido("Concreto armado", historico: historico), CancellationToken.None);

        var conteudos = Conteudos(_servidor.UltimoCorpo).ToList();
        conteudos.Should().HaveCount(LimitesDaConversa.MensagensDoHistorico + 1);
        var (papel, textos) = conteudos.SkipLast(1).Last();
        papel.Should().Be("model");
        textos.Should().StartWith("{\"tipo\":\"perguntas\"").And.Contain("Alvenaria estrutural");
    }

    [Fact]
    public async Task Anexos_AtualQueNaoDaParaLer_InterrompeSemChamarAApi()
    {
        var sumiu = Arquivo("sumiu.pdf", [1]);
        File.Delete(sumiu.CaminhoLocal);

        var enviar = () => Assistente().EnviarAsync(Pedido("Veja", [sumiu]), CancellationToken.None);

        await enviar.Should().ThrowAsync<ErroDoAssistenteException>()
            .WithMessage("Não deu para ler o anexo sumiu.pdf: o arquivo foi movido, apagado ou está sem permissão.");
        _servidor.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task Anexos_AtualEmFormatoNaoAceito_Recusado()
    {
        var planilha = Arquivo("orcamento.xlsx", [1]);

        var enviar = () => Assistente().EnviarAsync(Pedido("Veja", [planilha]), CancellationToken.None);

        await enviar.Should().ThrowAsync<ErroDoAssistenteException>().WithMessage("orcamento.xlsx: o assistente ainda não lê arquivos .xlsx.*");
        _servidor.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task Anexos_AcimaDoLimiteDaConversa_Recusados()
    {
        var metade = (int)(AnexosAceitos.TamanhoMaximoPorEnvio / 2) + 1;
        var a = Arquivo("a.pdf", new byte[metade]);
        var b = Arquivo("b.pdf", new byte[metade]);
        var historico = new[] { Do(AutorMensagem.Usuario, "Primeira", [a]), Do(AutorMensagem.Assistente, "Ok.") };

        var enviar = () => Assistente().EnviarAsync(Pedido("Segunda", [b], historico), CancellationToken.None);

        await enviar.Should().ThrowAsync<ErroDoAssistenteException>()
            .WithMessage("Os anexos desta conversa passam de 14 MB (o limite por envio). Use \"Limpar conversa\" ou anexe arquivos menores.");
        _servidor.Pedidos.Should().BeEmpty();
    }

    // ---- Provedor: testar a chave, modelo ----

    [Fact]
    public async Task Testar_ChaveValida_ConsultaOModeloSemGastarMensagens()
    {
        _servidor.Responder = (_, _) => Task.FromResult(ManipuladorFalso.Json(200, """{"name": "models/gemini-2.5-flash"}"""));

        await new ProvedorGemini(_http).TestarAsync(Chave, " models/gemini-2.5-flash ", CancellationToken.None);

        var (pedido, corpo) = _servidor.Pedidos.Single();
        (pedido.Method, pedido.RequestUri!.ToString(), corpo).Should().Be((HttpMethod.Get, Endereco + "models/gemini-2.5-flash", ""));
        pedido.Headers.GetValues("x-goog-api-key").Should().Equal(Chave);
    }

    [Fact]
    public async Task Testar_ChaveInvalida_EModeloInexistente_Avisam()
    {
        var provedor = new ProvedorGemini(_http);
        _servidor.Responder = (_, _) => Task.FromResult(ManipuladorFalso.Json(400, ManipuladorFalso.Erro(400, "API key not valid.", "API_KEY_INVALID")));
        var chaveRuim = () => provedor.TestarAsync(Chave, "gemini-2.5-flash", CancellationToken.None);
        await chaveRuim.Should().ThrowAsync<ErroDoAssistenteException>().WithMessage("A chave do Gemini não é válida.*");

        _servidor.Responder = (_, _) => Task.FromResult(ManipuladorFalso.Json(404, ManipuladorFalso.Erro(404, "not found")));
        var modeloRuim = () => provedor.TestarAsync(Chave, "gemini-9-ultra", CancellationToken.None);
        await modeloRuim.Should().ThrowAsync<ErroDoAssistenteException>().WithMessage("O modelo \"gemini-9-ultra\" não existe*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("chave com espaço")]
    [InlineData("chave\ncom-quebra")]
    public async Task ChaveInvalida_RecusadaAntesDaRede(string chave)
    {
        var criar = () => new AssistenteGemini(_http, chave, "gemini-2.5-flash");
        var testar = () => new ProvedorGemini(_http).TestarAsync(chave, "gemini-2.5-flash", CancellationToken.None);

        criar.Should().Throw<ArgumentException>();
        await testar.Should().ThrowAsync<ArgumentException>();
        _servidor.Pedidos.Should().BeEmpty();
    }

    [Theory]
    [InlineData("gemini-2.5-flash", "gemini-2.5-flash")]
    [InlineData("  models/gemini-2.5-pro ", "gemini-2.5-pro")]
    [InlineData("gemini-2.5-flash-lite", "gemini-2.5-flash-lite")]
    public void Modelo_Normalizado(string digitado, string esperado)
    {
        new ProvedorGemini(_http).NormalizarModelo(digitado).Should().Be(esperado);
    }

    [Theory]
    [InlineData("", "Informe o modelo do Gemini (por exemplo, gemini-2.5-flash).")]
    [InlineData("gemini 2.5", "\"gemini 2.5\" não é um nome de modelo válido. Use um nome como gemini-2.5-flash.")]
    [InlineData("../chaves", "\"../chaves\" não é um nome de modelo válido. Use um nome como gemini-2.5-flash.")]
    [InlineData("Gemini-Pro", "\"Gemini-Pro\" não é um nome de modelo válido. Use um nome como gemini-2.5-flash.")]
    public void Modelo_Invalido_Recusado(string digitado, string mensagem)
    {
        var normalizar = () => new ProvedorGemini(_http).NormalizarModelo(digitado);

        normalizar.Should().Throw<ArgumentException>().Which.Message.Should().Be(mensagem);
    }

    [Fact]
    public void Provedor_NomeModeloPadrao_EClienteDoPrograma()
    {
        var provedor = new ProvedorGemini(_http);
        using var cliente = ProvedorGemini.CriarHttpClient();

        (provedor.Nome, provedor.ModeloPadrao, cliente.Timeout).Should().Be(("Gemini (Google)", "gemini-2.5-flash", TimeSpan.FromMinutes(5)));
        provedor.CriarAssistente(Chave, " models/gemini-2.5-pro").Should().BeOfType<AssistenteGemini>()
            .Which.Modelo.Should().Be("gemini-2.5-pro");
        var semHttp = () => new ProvedorGemini(null!);
        semHttp.Should().Throw<ArgumentNullException>();
    }
}
