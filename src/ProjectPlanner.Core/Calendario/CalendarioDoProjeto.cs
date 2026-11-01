using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace ProjectPlanner.Core;

/// <summary>Bloco 71: um feriado do projeto (estadual, municipal, da empresa): num dia, ou no mesmo dia todo ano.</summary>
public sealed record FeriadoDoCalendario(DateOnly Data, string Nome, bool TodoAno = false)
{
    public bool CaiEm(DateOnly dia) => TodoAno ? dia.Month == Data.Month && dia.Day == Data.Day : dia == Data;
}

/// <summary>
/// Bloco 71: um período fora da regra: sem trabalho (chuva, paralisação, recesso, férias coletivas) ou com trabalho (um sábado
/// ou um feriado trabalhado). Um dia sem trabalho vale mais que um com trabalho que caia no mesmo dia.
/// </summary>
public sealed record ExcecaoDoCalendario(DateOnly De, DateOnly Ate, string Motivo, bool Trabalha)
{
    public int Dias => Ate.DayNumber - De.DayNumber + 1;

    public bool Contem(DateOnly dia) => dia >= De && dia <= Ate;
}

/// <summary>
/// Bloco 71: o calendário de trabalho do projeto: os dias da semana em que se trabalha, os feriados nacionais (e os pontos
/// facultativos de Carnaval e Corpus Christi), os feriados do projeto e as exceções (dias sem trabalho e dias extras). Todo o
/// cálculo (o caminho crítico, as durações em dias úteis, as defasagens, o físico-financeiro, os recursos e os indicadores)
/// conta os dias úteis por ele. É imutável: cada mudança cria um calendário novo (o Desfazer guarda o anterior).
/// </summary>
/// <remarks>
/// As contas são as do calendário de antes (segunda a sexta) generalizadas: o padrão da semana dá a conta em semanas inteiras,
/// e os dias que fogem do padrão (feriados e exceções) entram por ano, guardados na primeira vez que o ano é usado. Assim um
/// cronograma de décadas, ou uma defasagem enorme, não anda dia a dia.
/// </remarks>
public sealed class CalendarioDoProjeto : IEquatable<CalendarioDoProjeto>
{
    public const int MaximoDeFeriados = 500;
    public const int MaximoDeExcecoes = 500;
    public const int MaximoDeDiasNumaExcecao = 3660;
    public const int TamanhoMaximoDoNome = 80;

    /// <summary>Os dias da semana na ordem do Brasil: de segunda a domingo.</summary>
    public static IReadOnlyList<DayOfWeek> Semana { get; } =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    private static readonly int UltimoDia = DateOnly.MaxValue.DayNumber;

    private readonly bool[] _trabalha = new bool[7]; // pelo DayOfWeek (domingo = 0)
    private readonly int _porSemana;
    private readonly bool _soOPadrao;
    private readonly ConcurrentDictionary<int, Ano> _anos = new();

    private CalendarioDoProjeto(IEnumerable<DayOfWeek> dias, bool nacionais, bool facultativos, IReadOnlyList<FeriadoDoCalendario> feriados,
        IReadOnlyList<ExcecaoDoCalendario> excecoes)
    {
        foreach (var dia in dias)
            _trabalha[(int)dia] = true;
        _porSemana = _trabalha.Count(t => t);
        DiasDeTrabalho = [.. Semana.Where(d => _trabalha[(int)d])];
        (FeriadosNacionais, PontosFacultativos, Feriados, Excecoes) = (nacionais, facultativos, feriados, excecoes);
        _soOPadrao = !nacionais && !facultativos && feriados.Count == 0 && excecoes.Count == 0;
    }

    /// <summary>O calendário de antes do Bloco 71 (e dos arquivos sem calendário): segunda a sexta, sem feriados.</summary>
    public static CalendarioDoProjeto Padrao { get; } = new(Semana.Take(5), false, false, [], []);

    /// <summary>O de um projeto novo: segunda a sexta, com os feriados nacionais, o Carnaval e o Corpus Christi.</summary>
    public static CalendarioDoProjeto Brasil { get; } = new(Semana.Take(5), true, true, [], []);

    /// <exception cref="DomainValidationException">Nenhum dia de trabalho, um nome vazio ou longo, um período invertido ou longo
    /// demais, ou itens demais.</exception>
    public static CalendarioDoProjeto Criar(IEnumerable<DayOfWeek> diasDeTrabalho, bool feriadosNacionais, bool pontosFacultativos,
        IEnumerable<FeriadoDoCalendario>? feriados = null, IEnumerable<ExcecaoDoCalendario>? excecoes = null)
    {
        ArgumentNullException.ThrowIfNull(diasDeTrabalho);
        var dias = diasDeTrabalho.Distinct().ToList();
        if (dias.Count == 0)
            throw new DomainValidationException(nameof(DiasDeTrabalho), "Marque pelo menos um dia de trabalho na semana.");
        if (dias.Any(d => !Enum.IsDefined(d)))
            throw new DomainValidationException(nameof(DiasDeTrabalho), "Dia da semana desconhecido.");

        var listaDeFeriados = new List<FeriadoDoCalendario>();
        foreach (var f in feriados ?? [])
        {
            var nome = Nome(f.Nome, "Feriado");
            if (!listaDeFeriados.Any(x => x.Data == f.Data && x.TodoAno == f.TodoAno))
                listaDeFeriados.Add(f with { Nome = nome });
        }
        if (listaDeFeriados.Count > MaximoDeFeriados)
            throw new DomainValidationException(nameof(Feriados), $"São no máximo {MaximoDeFeriados} feriados do projeto.");

        var listaDeExcecoes = new List<ExcecaoDoCalendario>();
        foreach (var e in excecoes ?? [])
        {
            if (e.Ate < e.De)
                throw new DomainValidationException(nameof(Excecoes), $"O período de {Texto(e.De)} a {Texto(e.Ate)} termina antes de começar.");
            if (e.Dias > MaximoDeDiasNumaExcecao)
                throw new DomainValidationException(nameof(Excecoes),
                    $"O período de {Texto(e.De)} a {Texto(e.Ate)} é longo demais (no máximo {MaximoDeDiasNumaExcecao} dias).");
            var motivo = Nome(e.Motivo, e.Trabalha ? "Dia trabalhado" : "Sem trabalho");
            if (!listaDeExcecoes.Contains(e with { Motivo = motivo }))
                listaDeExcecoes.Add(e with { Motivo = motivo });
        }
        if (listaDeExcecoes.Count > MaximoDeExcecoes)
            throw new DomainValidationException(nameof(Excecoes), $"São no máximo {MaximoDeExcecoes} períodos de exceção.");

        return new CalendarioDoProjeto(dias, feriadosNacionais, pontosFacultativos,
            [.. listaDeFeriados.OrderBy(f => f.TodoAno ? new DateOnly(2000, f.Data.Month, f.Data.Day) : f.Data).ThenBy(f => f.Nome, StringComparer.Ordinal)],
            [.. listaDeExcecoes.OrderBy(e => e.De).ThenBy(e => e.Ate).ThenBy(e => e.Trabalha)]);
    }

    private static string Nome(string? texto, string seVazio)
    {
        var nome = string.IsNullOrWhiteSpace(texto) ? seVazio : texto.Trim();
        if (nome.Length > TamanhoMaximoDoNome)
            throw new DomainValidationException(nameof(Feriados), $"O nome \"{nome[..20]}…\" é longo demais (no máximo {TamanhoMaximoDoNome} letras).");
        return nome;
    }

    /// <summary>Os dias da semana de trabalho, de segunda a domingo.</summary>
    public IReadOnlyList<DayOfWeek> DiasDeTrabalho { get; }

    public bool FeriadosNacionais { get; }

    /// <summary>O Carnaval (segunda e terça) e o Corpus Christi, que são pontos facultativos.</summary>
    public bool PontosFacultativos { get; }

    public IReadOnlyList<FeriadoDoCalendario> Feriados { get; }

    public IReadOnlyList<ExcecaoDoCalendario> Excecoes { get; }

    public bool EhOPadrao => Equals(Padrao);

    // ---- Para mudar (cada um devolve um calendário novo) ----

    public CalendarioDoProjeto ComDias(IEnumerable<DayOfWeek> dias) => Criar(dias, FeriadosNacionais, PontosFacultativos, Feriados, Excecoes);

    public CalendarioDoProjeto ComFeriadosNacionais(bool nacionais, bool facultativos) => Criar(DiasDeTrabalho, nacionais, facultativos, Feriados, Excecoes);

    public CalendarioDoProjeto ComFeriado(FeriadoDoCalendario feriado) => Criar(DiasDeTrabalho, FeriadosNacionais, PontosFacultativos, [.. Feriados, feriado], Excecoes);

    public CalendarioDoProjeto SemFeriado(FeriadoDoCalendario feriado) =>
        Criar(DiasDeTrabalho, FeriadosNacionais, PontosFacultativos, Feriados.Where(f => f != feriado), Excecoes);

    public CalendarioDoProjeto ComExcecao(ExcecaoDoCalendario excecao) => Criar(DiasDeTrabalho, FeriadosNacionais, PontosFacultativos, Feriados, [.. Excecoes, excecao]);

    public CalendarioDoProjeto SemExcecao(ExcecaoDoCalendario excecao) =>
        Criar(DiasDeTrabalho, FeriadosNacionais, PontosFacultativos, Feriados, Excecoes.Where(e => e != excecao));

    // ---- Os dias ----

    public bool EhDiaUtil(DateOnly dia) => Trabalha(dia.DayNumber);

    public bool EhDiaUtil(DateTime dia) => Trabalha(DateOnly.FromDateTime(dia).DayNumber);

    /// <summary>Por que não se trabalha no dia ("Tiradentes", "Chuva", "domingo"); nulo num dia útil.</summary>
    public string? MotivoSemTrabalho(DateOnly dia)
    {
        if (EhDiaUtil(dia))
            return null;
        if (Excecoes.FirstOrDefault(e => !e.Trabalha && e.Contem(dia)) is { } parada)
            return parada.Motivo;
        if (FeriadoDo(dia) is { } feriado)
            return feriado;
        return NomeDoDia(dia.DayOfWeek);
    }

    /// <summary>Os dias sem trabalho do período que fogem da semana (feriados, paralisações); com <paramref name="comFolgasDaSemana"/>, também os sábados e domingos de folga.</summary>
    public IReadOnlyList<(DateOnly Dia, string Motivo)> DiasSemTrabalho(DateOnly de, DateOnly ate, bool comFolgasDaSemana = false)
    {
        var lista = new List<(DateOnly, string)>();
        for (var n = de.DayNumber; n <= ate.DayNumber && n <= UltimoDia; n++)
        {
            var dia = DateOnly.FromDayNumber(n);
            if (!Trabalha(n) && (comFolgasDaSemana || _trabalha[(int)dia.DayOfWeek]))
                lista.Add((dia, MotivoSemTrabalho(dia)!));
        }
        return lista;
    }

    /// <summary>Os dias extras de trabalho do período (um sábado ou um feriado trabalhado).</summary>
    public IReadOnlyList<(DateOnly Dia, string Motivo)> DiasExtras(DateOnly de, DateOnly ate)
    {
        var lista = new List<(DateOnly, string)>();
        foreach (var e in Excecoes.Where(e => e.Trabalha && e.Ate >= de && e.De <= ate))
            for (var n = Math.Max(e.De.DayNumber, de.DayNumber); n <= Math.Min(e.Ate.DayNumber, ate.DayNumber); n++)
            {
                var dia = DateOnly.FromDayNumber(n);
                if (Trabalha(n) && (!_trabalha[(int)dia.DayOfWeek] || FeriadoDo(dia) is not null) && !lista.Any(x => x.Item1 == dia))
                    lista.Add((dia, e.Motivo));
            }
        return [.. lista.OrderBy(x => x.Item1)];
    }

    // ---- A aritmética dos dias úteis (as mesmas operações de antes, agora por este calendário) ----

    /// <summary>O próprio dia se for útil; senão, o próximo dia útil.</summary>
    public DateTime ProximoOuMesmo(DateTime data)
    {
        var inicio = DateOnly.FromDateTime(data).DayNumber;
        var n = inicio;
        while (!Trabalha(n))
            n = n < UltimoDia ? n + 1 : throw new ArgumentOutOfRangeException(nameof(data), "Não há dia útil depois desta data.");
        return data.Date.AddDays(n - inicio);
    }

    /// <summary>O próprio dia se for útil; senão, o dia útil anterior.</summary>
    public DateTime AnteriorOuMesmo(DateTime data)
    {
        var inicio = DateOnly.FromDateTime(data).DayNumber;
        var n = inicio;
        while (!Trabalha(n))
            n = n > 0 ? n - 1 : throw new ArgumentOutOfRangeException(nameof(data), "Não há dia útil antes desta data.");
        return data.Date.AddDays(n - inicio);
    }

    /// <summary>
    /// Avança <paramref name="quantidade"/> dias úteis a partir de <paramref name="diaUtil"/> (com uma quantidade negativa, volta).
    /// Nulo se o resultado sair das datas que o .NET representa.
    /// </summary>
    public DateTime? Adicionar(DateTime diaUtil, int quantidade)
    {
        if (quantidade == 0)
            return diaUtil.Date;
        var d = DateOnly.FromDateTime(diaUtil).DayNumber;
        long alvo = Math.Abs((long)quantidade) + 1;
        long? achado = quantidade > 0 ? Adiante(d, alvo) : Atras(d, alvo);
        return achado is { } x ? diaUtil.Date.AddDays(x - d) : null;
    }

    /// <summary>Quantos dias úteis <paramref name="data"/> está depois de <paramref name="inicio"/> (0 no mesmo dia; negativo antes). Os dois devem ser dias úteis.</summary>
    public long Indice(DateTime inicio, DateTime data)
    {
        var (a, b) = (DateOnly.FromDateTime(inicio).DayNumber, DateOnly.FromDateTime(data).DayNumber);
        return b >= a ? Contar(a, b) - 1 : -(Contar(b, a) - 1);
    }

    /// <summary>Os dias úteis de <paramref name="inicio"/> a <paramref name="termino"/>, contando os dois (que devem ser dias úteis).</summary>
    public int ContarInclusivo(DateTime inicio, DateTime termino) =>
        (int)Contar(DateOnly.FromDateTime(inicio).DayNumber, DateOnly.FromDateTime(termino).DayNumber);

    /// <summary>Os dias úteis de <paramref name="inicio"/> a <paramref name="termino"/>, contando os dois; zero num período invertido. O horário não conta.</summary>
    public int ContarDiasUteis(DateTime inicio, DateTime termino)
    {
        var (a, b) = (DateOnly.FromDateTime(inicio).DayNumber, DateOnly.FromDateTime(termino).DayNumber);
        return b < a ? 0 : (int)Contar(a, b);
    }

    public int ContarDiasUteis(DateOnly inicio, DateOnly termino) => termino < inicio ? 0 : (int)Contar(inicio.DayNumber, termino.DayNumber);

    // ---- O texto ----

    /// <summary>"Segunda a sexta · feriados nacionais, Carnaval e Corpus Christi · 2 feriados do projeto · 1 período sem trabalho".</summary>
    public string Descricao
    {
        get
        {
            var partes = new List<string> { char.ToUpperInvariant(TextoDosDias[0]) + TextoDosDias[1..] };
            partes.Add((FeriadosNacionais, PontosFacultativos) switch
            {
                (true, true) => "feriados nacionais, Carnaval e Corpus Christi",
                (true, false) => "feriados nacionais",
                (false, true) => "Carnaval e Corpus Christi",
                _ => "sem feriados nacionais",
            });
            if (Feriados.Count > 0)
                partes.Add(Feriados.Count == 1 ? "1 feriado do projeto" : $"{Feriados.Count} feriados do projeto");
            var paradas = Excecoes.Count(e => !e.Trabalha);
            if (paradas > 0)
                partes.Add(paradas == 1 ? "1 período sem trabalho" : $"{paradas} períodos sem trabalho");
            var extras = Excecoes.Count(e => e.Trabalha);
            if (extras > 0)
                partes.Add(extras == 1 ? "1 período de trabalho extra" : $"{extras} períodos de trabalho extra");
            return string.Join(" · ", partes);
        }
    }

    /// <summary>"segunda a sexta", "segunda a sábado", "segunda, quarta e sexta", "todos os dias".</summary>
    public string TextoDosDias
    {
        get
        {
            if (_porSemana == 7)
                return "todos os dias";
            var indices = DiasDeTrabalho.Select(d => Semana.ToList().IndexOf(d)).ToList();
            if (indices.Count >= 3 && indices[^1] - indices[0] == indices.Count - 1)
                return $"{NomeDoDia(DiasDeTrabalho[0])} a {NomeDoDia(DiasDeTrabalho[^1])}";
            var nomes = DiasDeTrabalho.Select(NomeDoDia).ToList();
            return nomes.Count == 1 ? $"só {nomes[0]}" : string.Join(", ", nomes[..^1]) + " e " + nomes[^1];
        }
    }

    public static string NomeDoDia(DayOfWeek dia) => dia switch
    {
        DayOfWeek.Monday => "segunda",
        DayOfWeek.Tuesday => "terça",
        DayOfWeek.Wednesday => "quarta",
        DayOfWeek.Thursday => "quinta",
        DayOfWeek.Friday => "sexta",
        DayOfWeek.Saturday => "sábado",
        _ => "domingo",
    };

    /// <summary>
    /// Os dias da semana num texto ("segunda a sábado", "seg-sex", "seg, qua e sex", "todos os dias"); nulo se não entender.
    /// </summary>
    public static IReadOnlyList<DayOfWeek>? LerDias(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;
        var t = SemAcento(texto.Trim().ToLowerInvariant()).Replace("-feira", "").Replace(" feira", "").Replace(" ate ", " a ");
        if (t is "todos os dias" or "todo dia" or "todos" or "corridos")
            return [.. Semana];
        static int? Dia(string p) => p.Trim().TrimEnd('.', 's') switch
        {
            "seg" or "segunda" => 0,
            "ter" or "terca" => 1,
            "qua" or "quarta" => 2,
            "qui" or "quinta" => 3,
            "sex" or "sexta" => 4,
            "sab" or "sabado" => 5,
            "dom" or "domingo" => 6,
            _ => null,
        };
        var dias = new SortedSet<int>();
        foreach (var bruto in t.Replace(" e ", ",").Replace(";", ",").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parte = bruto.StartsWith("de ", StringComparison.Ordinal) ? bruto[3..] : bruto;
            var pontas = parte.Contains(" a ", StringComparison.Ordinal) ? parte.Split(" a ") : parte.Split('-');
            if (pontas.Length == 1 && Dia(pontas[0]) is { } um)
                dias.Add(um);
            else if (pontas.Length == 2 && Dia(pontas[0]) is { } de && Dia(pontas[1]) is { } ate)
                for (var i = de; ; i = (i + 1) % 7)
                {
                    dias.Add(i);
                    if (i == ate)
                        break;
                }
            else
                return null;
        }
        return dias.Count == 0 ? null : [.. dias.Select(i => Semana[i])];
    }

    private static string SemAcento(string texto)
    {
        var decomposto = texto.Normalize(NormalizationForm.FormD);
        return new string([.. decomposto.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)]).Normalize(NormalizationForm.FormC);
    }

    private static string Texto(DateOnly dia) => dia.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    // ---- Por dentro ----

    // O dia no padrão da semana (DayNumber 0 é a segunda-feira 01/01/0001).
    private bool NoPadrao(long n) => _trabalha[(int)((n + 1) % 7)];

    private bool Trabalha(long n)
    {
        if (_soOPadrao)
            return NoPadrao(n);
        var ano = AnoDe((int)n);
        var i = Array.BinarySearch(ano.Dias, (int)n);
        return i >= 0 ? ano.Situacao[i] : NoPadrao(n);
    }

    private string? FeriadoDo(DateOnly dia)
    {
        if (Feriados.FirstOrDefault(f => f.CaiEm(dia)) is { } doProjeto)
            return doProjeto.Nome;
        if (!FeriadosNacionais && !PontosFacultativos)
            return null;
        return FeriadosDoBrasil.Do(dia.Year).FirstOrDefault(f => f.Data == dia && (f.Facultativo ? PontosFacultativos : FeriadosNacionais))?.Nome;
    }

    // Os dias úteis de a até b (DayNumber, a ≤ b), contando os dois.
    private long Contar(long a, long b)
    {
        if (b < a)
            return 0;
        var dias = b - a + 1;
        var total = dias / 7 * _porSemana;
        var semana = (int)((a + 1) % 7);
        for (var i = 0; i < dias % 7; i++)
            if (_trabalha[(semana + i) % 7])
                total++;
        if (_soOPadrao)
            return total;
        int primeiroAno = DateOnly.FromDayNumber((int)a).Year, ultimoAno = DateOnly.FromDayNumber((int)b).Year;
        for (var y = primeiroAno; y <= ultimoAno; y++)
        {
            var ano = AnoDe(new DateOnly(y, 1, 1).DayNumber);
            if (ano.Dias.Length == 0)
                continue;
            var de = Array.BinarySearch(ano.Dias, (int)Math.Max(a, int.MinValue));
            de = de < 0 ? ~de : de;
            var ate = Array.BinarySearch(ano.Dias, (int)Math.Min(b, int.MaxValue));
            ate = ate < 0 ? ~ate : ate + 1;
            if (ate > de)
                total += ano.Saldo[ate] - ano.Saldo[de];
        }
        return total;
    }

    // O primeiro dia x ≥ a em que a conta do padrão de a até x chega a m (m ≥ 1); nulo depois do fim das datas.
    private long? NoPadraoAdiante(long a, long m)
    {
        var semanas = (m - 1) / _porSemana;
        var x = a + 7 * semanas;
        var falta = m - semanas * _porSemana;
        while (x <= UltimoDia)
        {
            if (NoPadrao(x) && --falta == 0)
                return x;
            x++;
        }
        return null;
    }

    // O último dia x ≤ b em que a conta do padrão de x até b chega a m (m ≥ 1); nulo antes do começo das datas.
    private long? NoPadraoAtras(long b, long m)
    {
        var semanas = (m - 1) / _porSemana;
        var x = b - 7 * semanas;
        var falta = m - semanas * _porSemana;
        while (x >= 0)
        {
            if (NoPadrao(x) && --falta == 0)
                return x;
            x--;
        }
        return null;
    }

    // O dia em que a conta a partir de d chega a alvo: primeiro um limite (o padrão, empurrado pelos feriados), depois a busca.
    private long? Adiante(long d, long alvo)
    {
        if (NoPadraoAdiante(d, alvo) is not { } limite)
            return null;
        if (_soOPadrao)
            return limite;
        for (var c = Contar(d, limite); c < alvo; c = Contar(d, limite))
            if (NoPadraoAdiante(limite + 1, alvo - c) is { } adiante)
                limite = adiante;
            else
                return null;
        var (lo, hi) = (d, limite);
        while (lo < hi)
        {
            var meio = lo + (hi - lo) / 2;
            if (Contar(d, meio) >= alvo)
                hi = meio;
            else
                lo = meio + 1;
        }
        return lo;
    }

    private long? Atras(long d, long alvo)
    {
        if (NoPadraoAtras(d, alvo) is not { } limite)
            return null;
        if (_soOPadrao)
            return limite;
        for (var c = Contar(limite, d); c < alvo; c = Contar(limite, d))
            if (NoPadraoAtras(limite - 1, alvo - c) is { } atras)
                limite = atras;
            else
                return null;
        var (lo, hi) = (limite, d);
        while (lo < hi)
        {
            var meio = hi - (hi - lo) / 2;
            if (Contar(meio, d) >= alvo)
                lo = meio;
            else
                hi = meio - 1;
        }
        return lo;
    }

    // Os dias do ano que fogem do padrão da semana, guardados na primeira vez (com o saldo acumulado, para contar depressa).
    private sealed record Ano(int[] Dias, bool[] Situacao, long[] Saldo);

    private Ano AnoDe(int diaDoAno) => _anos.GetOrAdd(DateOnly.FromDayNumber(diaDoAno).Year, MontarAno);

    private Ano MontarAno(int y)
    {
        var (inicio, fim) = (new DateOnly(y, 1, 1), new DateOnly(y, 12, 31));
        var feriados = new HashSet<int>();
        foreach (var f in FeriadosDoBrasil.Do(y).Where(f => f.Facultativo ? PontosFacultativos : FeriadosNacionais))
            feriados.Add(f.Data.DayNumber);
        foreach (var f in Feriados)
            if (!f.TodoAno ? f.Data.Year == y : f.Data.Month != 2 || f.Data.Day != 29 || DateTime.IsLeapYear(y))
                feriados.Add((f.TodoAno ? new DateOnly(y, f.Data.Month, f.Data.Day) : f.Data).DayNumber);
        var (parados, extras) = (new HashSet<int>(), new HashSet<int>());
        foreach (var e in Excecoes.Where(e => e.Ate >= inicio && e.De <= fim))
            for (var n = Math.Max(e.De.DayNumber, inicio.DayNumber); n <= Math.Min(e.Ate.DayNumber, fim.DayNumber); n++)
                (e.Trabalha ? extras : parados).Add(n);
        var diferentes = new SortedDictionary<int, bool>();
        foreach (var n in feriados.Concat(parados).Concat(extras).Distinct())
        {
            var situacao = !parados.Contains(n) && (extras.Contains(n) || (!feriados.Contains(n) && NoPadrao(n)));
            if (situacao != NoPadrao(n))
                diferentes[n] = situacao;
        }
        var saldo = new long[diferentes.Count + 1];
        var i = 0;
        foreach (var situacao in diferentes.Values)
        {
            saldo[i + 1] = saldo[i] + (situacao ? 1 : -1);
            i++;
        }
        return new Ano([.. diferentes.Keys], [.. diferentes.Values], saldo);
    }

    // ---- Igualdade pelo conteúdo ----

    public bool Equals(CalendarioDoProjeto? outro) =>
        outro is not null && (ReferenceEquals(this, outro) || _trabalha.SequenceEqual(outro._trabalha) && FeriadosNacionais == outro.FeriadosNacionais
            && PontosFacultativos == outro.PontosFacultativos && Feriados.SequenceEqual(outro.Feriados) && Excecoes.SequenceEqual(outro.Excecoes));

    public override bool Equals(object? obj) => Equals(obj as CalendarioDoProjeto);

    public override int GetHashCode() => HashCode.Combine(_porSemana, FeriadosNacionais, PontosFacultativos, Feriados.Count, Excecoes.Count);

    public override string ToString() => Descricao;
}
