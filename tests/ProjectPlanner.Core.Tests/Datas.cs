namespace ProjectPlanner.Core.Tests;

/// <summary>
/// Datas de referência: a semana de segunda 28/09/2026 a domingo 04/10/2026.
/// </summary>
public static class Datas
{
    public static readonly DateTime Segunda = new(2026, 9, 28);
    public static readonly DateTime Terca = new(2026, 9, 29);
    public static readonly DateTime Quarta = new(2026, 9, 30);
    public static readonly DateTime Quinta = new(2026, 10, 1);
    public static readonly DateTime Sexta = new(2026, 10, 2);
    public static readonly DateTime Sabado = new(2026, 10, 3);
    public static readonly DateTime Domingo = new(2026, 10, 4);
    public static readonly DateTime ProximaSegunda = new(2026, 10, 5);
    public static readonly DateTime ProximaTerca = new(2026, 10, 6);
    public static readonly DateTime ProximaSexta = new(2026, 10, 9);

    /// <summary>A última segunda-feira representável (a semana termina em <see cref="DateTime.MaxValue"/>).</summary>
    public static readonly DateTime UltimaSegunda = new(9999, 12, 27);

    public static TheoryData<DateTime> FimDeSemana => new() { Sabado, Domingo };
}
