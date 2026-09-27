namespace ProjectPlanner.App.Services;

/// <summary>Preferências de tela que valem entre as sessões, fora as colunas (Bloco 19).</summary>
/// <param name="MostrarAssistente">Se o painel do assistente de IA aparece.</param>
public sealed record PreferenciasInterface(bool MostrarAssistente);

/// <summary>Onde ficam as <see cref="PreferenciasInterface"/> (ver <see cref="ArquivoPreferenciasInterface"/>).</summary>
public interface IPreferenciasInterface
{
    /// <summary>As preferências salvas; nulas se ainda não há ou se não deu para ler.</summary>
    PreferenciasInterface? Carregar();

    /// <summary>Grava as preferências. Uma falha ao gravar não interrompe o uso do programa.</summary>
    void Salvar(PreferenciasInterface preferencias);
}
