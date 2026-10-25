using System.Diagnostics;
using System.Text;
using ProjectPlanner.IA;
using ProjectPlanner.IA.Conta;
using ProjectPlanner.IA.Gemini;
using Xunit.Abstractions;

namespace ProjectPlanner.App.Tests.Avaliacao;

// Bloco 68: a avaliação com a IA de verdade. Só roda com PROJECTPLANNER_AVALIACAO=gemini (e GEMINI_API_KEY) ou =conta (a sessão
// do programa, ou o token em PROJECTPLANNER_ACESSO); sem elas, passa sem chamar nada (o CI e o dotnet test de sempre).
// Escreve o relatório em PROJECTPLANNER_AVALIACAO_SAIDA (ou na pasta temporária) e falha se algum pedido falhar.
// Ver docs/AVALIACAO-DA-IA.md.
public sealed class AvaliacaoAoVivoTests(ITestOutputHelper saida)
{
    private sealed class SessaoFixa(string acesso) : ISessaoDaConta
    {
        public Task<string> AcessoAsync(bool renovar, CancellationToken cancelamento) => renovar
            ? throw new ErroDoAssistenteException("O token de PROJECTPLANNER_ACESSO foi recusado: entre de novo e copie um novo.")
            : Task.FromResult(acesso);
    }

    [Fact]
    [Trait("Categoria", "AvaliacaoAoVivo")]
    public async Task PedidosDaAvaliacao_ComAIaDeVerdade()
    {
        var modo = Environment.GetEnvironmentVariable("PROJECTPLANNER_AVALIACAO")?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(modo))
        {
            saida.WriteLine("Avaliação ao vivo pulada: defina PROJECTPLANNER_AVALIACAO (gemini ou conta). Ver docs/AVALIACAO-DA-IA.md.");
            return;
        }
        var modelo = Environment.GetEnvironmentVariable("PROJECTPLANNER_AVALIACAO_MODELO") is { Length: > 0 } m ? m : ProvedorGemini.ModeloRecomendado;
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        var servidor = new Uri(Environment.GetEnvironmentVariable("PROJECTPLANNER_SERVIDOR") is { Length: > 0 } s ? s : "https://projectplanner-servidor.onrender.com/");
        IAssistenteIA assistente = modo switch
        {
            "gemini" => new AssistenteGemini(http, Exigir("GEMINI_API_KEY"), modelo),
            // Pela conta: o token dado, ou a sessão do programa (no cofre do sistema, como o programa faz; o macOS pede para deixar).
            "conta" => new AssistenteGemini(http, new AcessoPelaConta(servidor,
                Environment.GetEnvironmentVariable("PROJECTPLANNER_ACESSO") is { Length: > 0 } acesso ? new SessaoFixa(acesso)
                    : new ProjectPlanner.App.Services.ContaProjectPlanner(new ClienteDaConta(http, servidor), ProjectPlanner.App.Services.Cofre.CofreDoSistema.Criar(),
                        TimeProvider.System, _ => Task.CompletedTask)), modelo),
            _ => throw new InvalidOperationException($"PROJECTPLANNER_AVALIACAO={modo}: use gemini ou conta."),
        };
        var filtro = Environment.GetEnvironmentVariable("PROJECTPLANNER_AVALIACAO_CASOS")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var obra = ObraDaAvaliacao.Montar();
        var retrato = obra.RetratoParaOAssistente();
        var conducao = new ConducaoDaConversa(ModoDeTrabalho.NaoEscolhido, PreferenciaDeModo.Perguntar, []);
        var resultados = new List<(CasoDaAvaliacao Caso, ResultadoDoCaso Resultado, RespostaIA? Resposta, TimeSpan Tempo)>();
        foreach (var caso in AvaliadorDaIa.Casos().Where(c => filtro is null || filtro.Contains(c.Id)))
        {
            var relogio = Stopwatch.StartNew();
            RespostaIA? resposta = null;
            ResultadoDoCaso resultado;
            try
            {
                // Como o programa manda (com a condução da conversa) e como o servidor manda do Telegram (sem ela).
                var pedido = caso.Canal == CanaisDaConversa.Telegram
                    ? new PedidoIA(caso.Pedido, [], [], retrato) { Canal = CanaisDaConversa.Telegram }
                    : new PedidoIA(caso.Pedido, [], [], retrato, conducao);
                resposta = await assistente.EnviarAsync(pedido, CancellationToken.None);
                resultado = AvaliadorDaIa.Avaliar(caso, resposta, obra, retrato);
            }
            catch (ErroDoAssistenteException ex)
            {
                resultado = new ResultadoDoCaso(caso.Id, [$"erro do assistente: {ex.Message}"], []);
            }
            resultados.Add((caso, resultado, resposta, relogio.Elapsed));
            saida.WriteLine($"{(resultado.Passou ? "ok   " : "FALHA")} {caso.Id} ({relogio.Elapsed.TotalSeconds:0.0} s)");
        }

        var arquivo = Relatorio(modo, modelo, resultados);
        var falhas = resultados.Where(r => !r.Resultado.Passou).ToList();
        saida.WriteLine($"{resultados.Count - falhas.Count} de {resultados.Count} pedidos passaram. Relatório: {arquivo}");
        falhas.Should().BeEmpty($"o relatório está em {arquivo}");
    }

    private static string Exigir(string variavel) =>
        Environment.GetEnvironmentVariable(variavel) is { Length: > 0 } valor ? valor
            : throw new InvalidOperationException($"Falta a variável {variavel} (ver docs/AVALIACAO-DA-IA.md).");

    private static string Relatorio(string modo, string modelo,
        IReadOnlyList<(CasoDaAvaliacao Caso, ResultadoDoCaso Resultado, RespostaIA? Resposta, TimeSpan Tempo)> resultados)
    {
        var agora = DateTime.Now;
        var texto = new StringBuilder();
        texto.AppendLine($"# Avaliação da IA — {modelo} ({modo}), {agora:dd/MM/yyyy HH:mm}");
        texto.AppendLine();
        texto.AppendLine($"**{resultados.Count(r => r.Resultado.Passou)} de {resultados.Count} pedidos passaram**, em " +
                         $"{resultados.Sum(r => r.Tempo.TotalSeconds):0} s. Versão do programa {VersaoDoProduto()}.");
        texto.AppendLine();
        texto.AppendLine("| Pedido | Resultado | Tempo | O que falhou ou sobrou |");
        texto.AppendLine("|---|---|---|---|");
        foreach (var (caso, resultado, _, tempo) in resultados)
            texto.AppendLine($"| `{caso.Id}`: {caso.Pedido} | {(resultado.Passou ? "ok" : "**falha**")} | {tempo.TotalSeconds:0.0} s | " +
                             string.Join("<br>", resultado.Falhas.Concat(resultado.Avisos.Select(a => "aviso: " + a))).Replace("|", "\\|") + " |");
        texto.AppendLine();
        texto.AppendLine("## As respostas");
        foreach (var (caso, _, resposta, _) in resultados.Where(r => r.Resposta is not null))
        {
            texto.AppendLine();
            texto.AppendLine($"### {caso.Id}");
            texto.AppendLine($"> {resposta!.Texto.Replace("\n", "\n> ")}");
            foreach (var a in resposta.Atualizacoes)
                texto.AppendLine($"- atualização: {AvaliadorDaIa.Descrever(a)}");
            foreach (var a in resposta.Alteracoes)
                texto.AppendLine($"- alteração: {a.Descricao}");
            if (resposta.Abrir is { } tela)
                texto.AppendLine($"- abrir: {tela.Tela}{(tela.Aba is { } aba ? $" ({aba})" : "")}");
            if (resposta.PedidoDeOrcamento is { } orcamento)
                texto.AppendLine($"- orçamento: {orcamento}");
        }
        var pasta = Environment.GetEnvironmentVariable("PROJECTPLANNER_AVALIACAO_SAIDA") is { Length: > 0 } p ? p
            : Path.Combine(Path.GetTempPath(), "projectplanner-avaliacao");
        Directory.CreateDirectory(pasta);
        var arquivo = Path.Combine(pasta, $"avaliacao-{modelo}-{agora:yyyy-MM-dd-HHmm}.md");
        File.WriteAllText(arquivo, texto.ToString());
        return arquivo;
    }

    private static string VersaoDoProduto() =>
        typeof(ProjectPlanner.App.Services.AtualizacoesDoAssistente).Assembly.GetName().Version?.ToString(3) ?? "?";
}
