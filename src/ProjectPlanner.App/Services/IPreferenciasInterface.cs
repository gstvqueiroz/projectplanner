namespace ProjectPlanner.App.Services;

/// <summary>O tema das cores do programa (Bloco 25, V3): o do sistema (padrão), sempre claro ou sempre escuro.</summary>
public enum TemaDaInterface
{
    Sistema,
    Claro,
    Escuro,
}

/// <summary>Preferências de tela que valem entre as sessões, fora as colunas (Bloco 19).</summary>
/// <param name="MostrarAssistente">Se o painel do assistente de IA aparece.</param>
/// <param name="Tema">O tema escolhido em Exibir > Tema (Bloco 25); sem escolha salva, o do sistema.</param>
/// <param name="OcultosNaBarra">Bloco 39: os botões que o usuário tirou da barra de ícones (pelo id); nulo, todos aparecem.</param>
public sealed record PreferenciasInterface(bool MostrarAssistente, TemaDaInterface Tema = TemaDaInterface.Sistema,
    IReadOnlyList<string>? OcultosNaBarra = null);

/// <summary>Onde ficam as <see cref="PreferenciasInterface"/> (ver <see cref="ArquivoPreferenciasInterface"/>).</summary>
public interface IPreferenciasInterface
{
    /// <summary>As preferências salvas; nulas se ainda não há ou se não deu para ler.</summary>
    PreferenciasInterface? Carregar();

    /// <summary>Grava as preferências. Uma falha ao gravar não interrompe o uso do programa.</summary>
    void Salvar(PreferenciasInterface preferencias);
}
