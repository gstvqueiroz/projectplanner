namespace ProjectPlanner.Core;

/// <summary>
/// Aritmética de dias úteis do calendário padrão: segunda a sexta, sem feriados.
/// Espera datas sem hora (<see cref="DateTime.Date"/>).
/// </summary>
internal static class DiasUteis
{
    /// <summary>Retorna a própria data se for dia útil; senão, a segunda-feira seguinte.</summary>
    public static DateTime ProximoOuMesmo(DateTime data) =>
        data.DayOfWeek switch
        {
            DayOfWeek.Saturday => data.AddDays(2),
            DayOfWeek.Sunday => data.AddDays(1),
            _ => data,
        };

    /// <summary>Retorna a própria data se for dia útil; senão, a sexta-feira anterior.</summary>
    public static DateTime AnteriorOuMesmo(DateTime data) =>
        data.DayOfWeek switch
        {
            DayOfWeek.Saturday => data.AddDays(-1),
            DayOfWeek.Sunday => data.AddDays(-2),
            _ => data,
        };

    /// <summary>
    /// Avança <paramref name="quantidade"/> dias úteis a partir de <paramref name="diaUtil"/> (com uma quantidade
    /// negativa, volta: Bloco 31, datas tarde antes do início do projeto quando há prazo vencido).
    /// Retorna <c>null</c> se o resultado sair do intervalo de <see cref="DateTime"/>.
    /// </summary>
    public static DateTime? Adicionar(DateTime diaUtil, int quantidade)
    {
        var semanas = quantidade / 5;
        var resto = quantidade % 5;
        var diasCorridos = semanas * 7L + resto;
        // Se o resto passa da sexta-feira (ou, voltando, da segunda), atravessa um fim de semana.
        if (IndiceNaSemana(diaUtil) + resto > 4)
            diasCorridos += 2;
        else if (IndiceNaSemana(diaUtil) + resto < 0)
            diasCorridos -= 2;

        if (diasCorridos > (DateTime.MaxValue.Date - diaUtil).Days || diasCorridos < -(diaUtil - DateTime.MinValue).Days)
            return null;
        return diaUtil.AddDays(diasCorridos);
    }

    /// <summary>
    /// Bloco 31: quantos dias úteis <paramref name="data"/> está depois de <paramref name="inicio"/> (0 no mesmo dia;
    /// negativo antes). Os dois devem ser dias úteis.
    /// </summary>
    public static long Indice(DateTime inicio, DateTime data) =>
        data >= inicio ? ContarInclusivo(inicio, data) - 1 : -(ContarInclusivo(data, inicio) - 1);

    /// <summary>
    /// Conta os dias úteis de <paramref name="inicio"/> a <paramref name="termino"/>, incluindo ambos.
    /// Os dois devem ser dias úteis, com <paramref name="inicio"/> ≤ <paramref name="termino"/>.
    /// </summary>
    public static int ContarInclusivo(DateTime inicio, DateTime termino)
    {
        var diasCorridos = (termino - inicio).Days;
        var semanas = diasCorridos / 7;
        var resto = diasCorridos % 7;
        // Na semana incompleta, se o resto passa da sexta-feira, sábado e domingo não contam.
        if (IndiceNaSemana(inicio) + resto > 4)
            resto -= 2;
        return semanas * 5 + resto + 1;
    }

    // Segunda = 0 ... Sexta = 4 (Sábado = 5, Domingo = 6).
    private static int IndiceNaSemana(DateTime data) => ((int)data.DayOfWeek + 6) % 7;
}
