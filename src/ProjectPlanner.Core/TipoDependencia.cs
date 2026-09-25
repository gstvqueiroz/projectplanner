namespace ProjectPlanner.Core;

/// <summary>
/// Tipo de vínculo entre a tarefa predecessora (A, origem) e a sucessora (B, destino).
/// </summary>
/// <remarks>
/// <para>
/// Cada tipo é uma restrição de "não antes de": um ponto de B (início ou término) não pode
/// acontecer antes de um ponto de A somado ao <see cref="Predecessora.Lag"/>. Quando B tem várias
/// predecessoras, vale a mais restritiva.
/// </para>
/// <para>
/// O lag é contado em dias úteis. Positivo é atraso (espera entre A e B); negativo é lead
/// (antecipação: B pode se sobrepor a A).
/// </para>
/// <para>
/// As datas são inclusivas: o início de uma tarefa é o começo do dia <see cref="Tarefa.DataInicio"/>
/// e o término é o fim do dia <see cref="Tarefa.DataTermino"/>. Por isso FS e SF ganham ±1 dia útil
/// quando escritos em datas. Nas fórmulas abaixo, "d + n" significa avançar n dias úteis a partir
/// de d (n negativo recua), e os exemplos usam uma A que vai de segunda a sexta da mesma semana.
/// </para>
/// </remarks>
public enum TipoDependencia
{
    // Valores explícitos: não reordenar.

    /// <summary>
    /// Término-Início (padrão): B só começa depois que A termina.
    /// <c>Início(B) ≥ Término(A) + 1 + Lag</c>.
    /// Lag 0: B começa a partir da segunda seguinte. Lag +2: da quarta seguinte.
    /// Lag −1: da própria sexta em que A termina.
    /// </summary>
    FS = 0,

    /// <summary>
    /// Início-Início: B só começa depois que A começa.
    /// <c>Início(B) ≥ Início(A) + Lag</c>.
    /// Lag 0: B começa a partir da mesma segunda. Lag +2: da quarta.
    /// Lag −1: da sexta anterior.
    /// </summary>
    SS = 1,

    /// <summary>
    /// Término-Término: B só termina depois que A termina.
    /// <c>Término(B) ≥ Término(A) + Lag</c>.
    /// Lag 0: B termina a partir da mesma sexta. Lag +2: da terça seguinte.
    /// Lag −1: da quinta.
    /// </summary>
    FF = 2,

    /// <summary>
    /// Início-Término: B só termina depois que A começa.
    /// <c>Término(B) ≥ Início(A) − 1 + Lag</c>.
    /// Lag 0: B termina a partir da sexta anterior (o dia útil antes do início de A).
    /// Lag +2: da terça. Lag −1: da quinta anterior.
    /// </summary>
    SF = 3,
}
