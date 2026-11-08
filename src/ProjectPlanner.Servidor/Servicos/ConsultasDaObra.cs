using System.Globalization;
using ProjectPlanner.IA;

namespace ProjectPlanner.Servidor.Servicos;

/// <summary>
/// Bloco 83: as consultas do Telegram que respondem sem a IA (na hora e sem gastar créditos): /resumo, /hoje e /atrasadas. Usam o
/// último retrato que o programa mandou (o cronograma da última vez que a obra foi salva) e o dia de hoje no horário de Brasília;
/// o andamento segue as regras do programa (<see cref="AndamentoNoDia"/>), com o calendário de trabalho do retrato.
/// </summary>
public static class ConsultasDaObra
{
    /// <summary>Itens de uma lista, no máximo; o resto vira "e mais N".</summary>
    public const int ItensPorLista = 15;

    /// <summary>Até quantos dias à frente o /hoje mostra o que vai começar.</summary>
    public const int DiasAFrente = 7;

    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>O que espera o programa na caixa da obra, para o /resumo.</summary>
    public sealed record CaixaEmNumeros(int Atualizacoes, int Fotos, int Ordens)
    {
        public int Total => Atualizacoes + Fotos + Ordens;
    }

    /// <summary>A obra em números: o avanço físico (ponderado pela duração, como o % dos resumos no programa), o término, as tarefas, o contrato, as OS e a caixa.</summary>
    /// <param name="salvoEm">Quando o programa mandou o retrato (no horário de Brasília).</param>
    public static string Resumo(string obra, ContextoDoProjeto retrato, DateOnly hoje, DateTime salvoEm, CaixaEmNumeros caixa)
    {
        ArgumentNullException.ThrowIfNull(retrato);
        ArgumentNullException.ThrowIfNull(caixa);
        var calendario = retrato.DiasUteis ?? CalendarioNoContexto.SegundaASexta;
        var folhas = retrato.Tarefas.Where(t => t.Tipo != TipoNoContexto.Resumo).ToList();
        var linhas = new List<string> { $"📊 {obra} — {hoje.ToString("dd/MM/yyyy", Brasil)}" };
        if (folhas.Count == 0)
            linhas.Add("O cronograma ainda não tem tarefas.");
        else
        {
            var (feito, previsto) = Avanco(folhas, calendario, hoje);
            var diferenca = previsto - feito;
            linhas.Add($"Avanço físico: {feito}% (previsto para hoje: {previsto}%)" +
                       (diferenca > 0 ? $" — {Pontos(diferenca)} atrás" : diferenca < 0 ? $" — {Pontos(-diferenca)} à frente" : " — em dia"));
            var linhaDeBase = retrato.Andamento?.TerminoDaLinhaDeBase;
            var desvio = linhaDeBase is { } lb ? retrato.Termino.DayNumber - lb.DayNumber : 0;
            linhas.Add($"Término: {Data(retrato.Termino)}" + (linhaDeBase is { } b
                ? $" (linha de base {Data(b)}" + (desvio > 0 ? $", {Dias(desvio)} depois)" : desvio < 0 ? $", {Dias(-desvio)} antes)" : ", no prazo)")
                : ""));
            var situacoes = folhas.Select(t => AndamentoNoDia.Situacao(t, calendario, hoje)).ToList();
            linhas.Add($"Tarefas: {folhas.Count(t => t.PercentualConcluido is > 0 and < 100)} em andamento · " +
                       $"{situacoes.Count(s => s == SituacaoNoDia.Atrasada)} atrasada(s) · " +
                       $"{situacoes.Count(s => s == SituacaoNoDia.Concluida)} de {folhas.Count} concluída(s)");
        }
        if (retrato.Andamento is { } andamento)
        {
            if (andamento.ValorContratado > 0)
                linhas.Add($"Medido: {Reais(andamento.Medido)} de {Reais(andamento.ValorContratado)} " +
                           $"({Math.Round(andamento.Medido / andamento.ValorContratado * 100, MidpointRounding.AwayFromZero)}%)");
            if (andamento.OrdensEmAndamento > 0 || andamento.OrdensVencidas > 0)
                linhas.Add($"OS: {andamento.OrdensEmAndamento} em andamento" + (andamento.OrdensVencidas > 0 ? $" · {andamento.OrdensVencidas} vencida(s) ⚠️" : ""));
        }
        if (caixa.Total > 0)
            linhas.Add("📥 Na caixa, esperando o programa: " + string.Join(", ", new[]
            {
                caixa.Atualizacoes == 0 ? null : caixa.Atualizacoes == 1 ? "1 mensagem com atualizações" : $"{caixa.Atualizacoes} mensagens com atualizações",
                caixa.Fotos == 0 ? null : caixa.Fotos == 1 ? "1 foto" : $"{caixa.Fotos} fotos",
                caixa.Ordens == 0 ? null : caixa.Ordens == 1 ? "1 passo de OS" : $"{caixa.Ordens} passos de OS",
            }.OfType<string>()));
        linhas.Add("");
        linhas.Add(Rodape(salvoEm) + "\nMais: /hoje · /atrasadas · /caixa");
        return string.Join("\n", linhas);
    }

    /// <summary>O dia: as tarefas em andamento (e as que começam ou terminam hoje), os marcos, o que começa nos próximos dias e o RDO de hoje.</summary>
    public static string Hoje(string obra, ContextoDoProjeto retrato, DateOnly hoje, DateTime salvoEm)
    {
        ArgumentNullException.ThrowIfNull(retrato);
        var calendario = retrato.DiasUteis ?? CalendarioNoContexto.SegundaASexta;
        var linhas = new List<string> { $"📅 Hoje, {Brasil.DateTimeFormat.GetDayName(hoje.DayOfWeek)} {hoje.ToString("dd/MM", Brasil)} — {obra}" };
        if (!calendario.EhDiaUtil(hoje))
            linhas.Add("Hoje não é dia de trabalho no calendário da obra.");

        var emAndamento = retrato.Tarefas
            .Where(t => t.Tipo == TipoNoContexto.Tarefa && t.PercentualConcluido < 100 && t.Inicio <= hoje && (t.Termino >= hoje || t.PercentualConcluido > 0))
            .OrderBy(t => t.Termino).ThenBy(t => t.Numero).ToList();
        linhas.Add("");
        if (emAndamento.Count == 0)
            linhas.Add("Nenhuma tarefa em andamento hoje pelo cronograma.");
        else
        {
            linhas.Add($"Em andamento ({emAndamento.Count}):");
            linhas.AddRange(Lista(emAndamento, t =>
            {
                var previsto = AndamentoNoDia.Previsto(t, calendario, hoje);
                var atrasada = t.PercentualConcluido < previsto;
                var quando = t.Inicio == hoje ? "começa hoje" : t.Termino == hoje ? "termina hoje"
                    : t.Termino < hoje ? $"devia ter terminado em {Dia(t.Termino)}" : $"até {Dia(t.Termino)}";
                return $"• {Nome(t)}: {t.PercentualConcluido}% (previsto {previsto}%), {quando}{(atrasada ? " ⚠️" : "")}";
            }));
        }
        var marcos = retrato.Tarefas.Where(t => t.Tipo == TipoNoContexto.Marco && t.Inicio == hoje && t.PercentualConcluido < 100).ToList();
        if (marcos.Count > 0)
        {
            linhas.Add("");
            linhas.Add("Marcos de hoje:");
            linhas.AddRange(Lista(marcos, t => $"• 🏁 {Nome(t)}"));
        }
        var proximas = retrato.Tarefas
            .Where(t => t.Tipo != TipoNoContexto.Resumo && t.PercentualConcluido == 0 && t.Inicio > hoje && t.Inicio <= hoje.AddDays(DiasAFrente))
            .OrderBy(t => t.Inicio).ThenBy(t => t.Numero).ToList();
        if (proximas.Count > 0)
        {
            linhas.Add("");
            linhas.Add($"Começam nos próximos {DiasAFrente} dias:");
            linhas.AddRange(Lista(proximas, t => $"• {Nome(t)} em {Dia(t.Inicio)}"));
        }
        linhas.Add("");
        linhas.Add(RdoDoDia(retrato, hoje) is { } rdo
            ? "📒 " + rdo
            : "📒 O RDO de hoje ainda não está no programa. Conte o dia aqui (clima, equipe, o que foi feito) que eu anoto; o que você já " +
              "contou espera na caixa (/caixa).");
        linhas.Add("");
        linhas.Add(Rodape(salvoEm));
        return string.Join("\n", linhas);
    }

    /// <summary>As tarefas atrasadas (o feito abaixo do previsto para hoje), da maior diferença para a menor.</summary>
    public static string Atrasadas(string obra, ContextoDoProjeto retrato, DateOnly hoje, DateTime salvoEm)
    {
        ArgumentNullException.ThrowIfNull(retrato);
        var calendario = retrato.DiasUteis ?? CalendarioNoContexto.SegundaASexta;
        var atrasadas = retrato.Tarefas.Where(t => t.Tipo != TipoNoContexto.Resumo)
            .Select(t => (Tarefa: t, Previsto: AndamentoNoDia.Previsto(t, calendario, hoje)))
            .Where(x => x.Tarefa.PercentualConcluido < 100 && x.Tarefa.PercentualConcluido < x.Previsto)
            .OrderByDescending(x => x.Previsto - x.Tarefa.PercentualConcluido).ThenBy(x => x.Tarefa.Termino).ThenBy(x => x.Tarefa.Numero).ToList();
        if (atrasadas.Count == 0)
            return $"✅ Nenhuma tarefa atrasada em {obra} hoje ({Dia(hoje)}).\n\n{Rodape(salvoEm)}";
        var linhas = new List<string> { $"⚠️ Atrasadas em {obra} hoje ({Dia(hoje)}): {atrasadas.Count}" };
        linhas.AddRange(Lista(atrasadas, x => x.Tarefa.Tipo == TipoNoContexto.Marco
            ? $"• 🏁 {Nome(x.Tarefa)}: era em {Dia(x.Tarefa.Inicio)}"
            : $"• {Nome(x.Tarefa)}: feito {x.Tarefa.PercentualConcluido}%, previsto {x.Previsto}%" +
              (x.Tarefa.Termino < hoje ? $", terminava em {Dia(x.Tarefa.Termino)}" : $", termina em {Dia(x.Tarefa.Termino)}")));
        linhas.Add("");
        linhas.Add("Para atualizar, conte o andamento aqui (\"a impermeabilização terminou\", \"o contrapiso está em 30%\").");
        linhas.Add(Rodape(salvoEm));
        return string.Join("\n", linhas);
    }

    /// <summary>O avanço físico (feito e previsto para o dia), ponderado pela duração; só com marcos, a média simples.</summary>
    public static (int Feito, int Previsto) Avanco(IReadOnlyList<TarefaNoContexto> folhas, CalendarioNoContexto calendario, DateOnly hoje)
    {
        ArgumentNullException.ThrowIfNull(folhas);
        if (folhas.Count == 0)
            return (0, 0);
        var pesos = folhas.Sum(t => (long)Math.Max(0, t.DuracaoDias));
        double Media(Func<TarefaNoContexto, int> valor) => pesos == 0
            ? folhas.Average(t => valor(t))
            : folhas.Sum(t => (double)Math.Max(0, t.DuracaoDias) * valor(t)) / pesos;
        return ((int)Math.Round(Media(t => t.PercentualConcluido), MidpointRounding.AwayFromZero),
            (int)Math.Round(Media(t => AndamentoNoDia.Previsto(t, calendario, hoje)), MidpointRounding.AwayFromZero));
    }

    // A linha do RDO do dia no retrato ("RDO nº 005 · 08/11/2026 (sex) · aberto · manhã: …"), como "RDO nº 005 de hoje (aberto): manhã: …".
    private static string? RdoDoDia(ContextoDoProjeto retrato, DateOnly dia)
    {
        var marca = $" · {dia.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} (";
        if (retrato.Diario?.Skip(1).FirstOrDefault(l => l.Contains(marca, StringComparison.Ordinal)) is not { } linha)
            return null;
        var partes = linha.Split(" · ");
        return partes.Length < 4 ? linha : $"{partes[0]} de hoje ({partes[2]}): {string.Join(" · ", partes.Skip(3))}";
    }

    private static IEnumerable<string> Lista<T>(IReadOnlyList<T> itens, Func<T, string> linha) =>
        itens.Count <= ItensPorLista ? itens.Select(linha) : itens.Take(ItensPorLista - 1).Select(linha).Append($"• e mais {itens.Count - ItensPorLista + 1}");

    private static string Rodape(DateTime salvoEm) =>
        $"(Pelo cronograma salvo no programa em {salvoEm.ToString("dd/MM", Brasil)} às {salvoEm.ToString("HH:mm", Brasil)}.)";

    private static string Nome(TarefaNoContexto t) => $"{t.Edt} {t.Nome}".Trim();

    private static string Data(DateOnly d) => d.ToString("dd/MM/yyyy", Brasil);

    private static string Dia(DateOnly d) => d.ToString("dd/MM", Brasil);

    private static string Dias(int n) => n == 1 ? "1 dia" : $"{n} dias";

    private static string Pontos(int n) => n == 1 ? "1 ponto" : $"{n} pontos";

    private static string Reais(decimal v) => "R$ " + v.ToString("#,##0.00", Brasil);
}
