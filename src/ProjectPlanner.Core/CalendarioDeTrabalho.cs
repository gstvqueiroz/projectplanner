namespace ProjectPlanner.Core;

/// <summary>
/// O calendário de trabalho do cronograma visto de fora do domínio (Bloco 25): segunda a sexta, sem feriados, como no
/// cálculo. Delega a <see cref="DiasUteis"/>, que continua interno.
/// </summary>
public static class CalendarioDeTrabalho
{
    /// <summary>
    /// Quantos dias úteis há de <paramref name="inicio"/> a <paramref name="termino"/>, contando os dois (fins de semana
    /// nas pontas não contam; o horário é ignorado). Zero se o período não tem nenhum dia útil.
    /// </summary>
    public static int ContarDiasUteis(DateTime inicio, DateTime termino)
    {
        var (primeiro, ultimo) = (DiasUteis.ProximoOuMesmo(inicio.Date), DiasUteis.AnteriorOuMesmo(termino.Date));
        return ultimo < primeiro ? 0 : DiasUteis.ContarInclusivo(primeiro, ultimo);
    }
}
