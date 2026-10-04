using ProjectPlanner.Core;

namespace ProjectPlanner.Core.Tests;

// Bloco 25: os dias úteis de um período, vistos de fora do domínio.
public class CalendarioDeTrabalhoTests
{
    [Theory]
    [InlineData("2026-09-28", "2026-09-28", 1)] // seg a seg
    [InlineData("2026-09-28", "2026-10-02", 5)] // seg a sex
    [InlineData("2026-09-28", "2026-10-05", 6)] // atravessa o fim de semana
    [InlineData("2026-09-26", "2026-09-27", 0)] // só sábado e domingo
    [InlineData("2026-09-26", "2026-09-29", 2)] // começa no sábado
    [InlineData("2026-09-28", "2026-10-04", 5)] // termina no domingo
    [InlineData("2026-10-05", "2026-09-28", 0)] // invertido
    [InlineData("2026-09-28 18:30", "2026-09-29 07:00", 2)] // o horário não conta
    public void ContarDiasUteis(string inicio, string termino, int dias)
    {
        CalendarioDeTrabalho.ContarDiasUteis(DateTime.Parse(inicio), DateTime.Parse(termino)).Should().Be(dias);
    }
}
