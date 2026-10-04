using Microsoft.Extensions.DependencyInjection;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core;
using ProjectPlanner.Data;
using ProjectPlanner.Core.Fotos;
using ProjectPlanner.IA;
using ProjectPlanner.Imagens;

namespace ProjectPlanner.App.Services;

/// <summary>Composição da aplicação: quem implementa cada dependência dos ViewModels.</summary>
public static class Servicos
{
    public static ServiceProvider Configurar() => new ServiceCollection()
        .AddSingleton(TimeProvider.System)
        .AddSingleton<IProjetoRepository>(s => new SqliteProjetoRepository(s.GetRequiredService<TimeProvider>()))
        .AddSingleton<ICalculadoraCronograma, CalculadoraCronogramaServico>()
        .AddSingleton<IDialogosService, DialogosAvalonia>()
        .AddSingleton<IPreferenciasColunas, ArquivoPreferenciasColunas>()
        .AddSingleton<IPreferenciasInterface, ArquivoPreferenciasInterface>()
        // Fotos (Bloco 22): SkiaSharp + MetadataExtractor, no projeto ProjectPlanner.Imagens.
        .AddSingleton<IProcessadorDeImagens, ProcessadorSkia>()
        // Assistente de IA (Bloco 19): sem chave de API, a fábrica devolve o AssistenteIAIndisponivel.
        .AddSingleton<IConfiguracaoIA, SemChaveDeApi>()
        .AddSingleton(s => FabricaAssistenteIA.Criar(s.GetRequiredService<IConfiguracaoIA>()))
        .AddTransient<MainWindowViewModel>()
        .BuildServiceProvider();
}
