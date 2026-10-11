using Microsoft.Extensions.DependencyInjection;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core;
using ProjectPlanner.Data;
using ProjectPlanner.Core.Fotos;
using ProjectPlanner.App.Services.Cofre;
using ProjectPlanner.IA;
using ProjectPlanner.IA.Gemini;
using ProjectPlanner.Imagens;

namespace ProjectPlanner.App.Services;

/// <summary>Composição da aplicação: quem implementa cada dependência dos ViewModels.</summary>
public static class Servicos
{
    public static ServiceProvider Configurar() => new ServiceCollection()
        .AddSingleton(TimeProvider.System)
        // Bloco 25 (R2): antes de gravar por cima, a versão anterior vai para as cópias de segurança.
        .AddSingleton<IProjetoRepository>(s =>
        {
            var backups = new BackupsDeProjetos(BackupsDeProjetos.PastaPadrao(), s.GetRequiredService<TimeProvider>());
            return new SqliteProjetoRepository(s.GetRequiredService<TimeProvider>(), caminho => backups.Guardar(caminho));
        })
        .AddSingleton<ICalculadoraCronograma, CalculadoraCronogramaServico>()
        .AddSingleton<IDialogosService, DialogosAvalonia>()
        .AddSingleton<IPreferenciasColunas, ArquivoPreferenciasColunas>()
        .AddSingleton<IPreferenciasInterface, ArquivoPreferenciasInterface>()
        // Fotos (Bloco 22): SkiaSharp + MetadataExtractor, no projeto ProjectPlanner.Imagens.
        .AddSingleton<IProcessadorDeImagens, ProcessadorSkia>()
        // Assistente de IA (Blocos 19 e 23): o Gemini pela API REST, com a chave no cofre do sistema (Chaves do macOS,
        // Gerenciador de Credenciais do Windows) e GEMINI_API_KEY para desenvolvimento; sem chave, respostas simuladas.
        .AddSingleton(_ => ProvedorGemini.CriarHttpClient())
        .AddSingleton<IProvedorIA>(s => new ProvedorGemini(s.GetRequiredService<HttpClient>()))
        .AddSingleton(_ => CofreDoSistema.Criar())
        .AddSingleton<IPreferenciasAssistente, ArquivoPreferenciasAssistente>()
        .AddSingleton<IPreferenciasRelatorio, ArquivoPreferenciasRelatorio>()
        .AddSingleton(s => new ConfiguracaoDoAssistente(s.GetRequiredService<ICofreDeSegredos>(),
            s.GetRequiredService<IPreferenciasAssistente>(), s.GetRequiredService<IProvedorIA>(), Environment.GetEnvironmentVariable))
        .AddSingleton<IConfiguracaoIA>(s => s.GetRequiredService<ConfiguracaoDoAssistente>())
        .AddSingleton<IConfiguracaoAssistente>(s => s.GetRequiredService<ConfiguracaoDoAssistente>())
        .AddSingleton(s => FabricaAssistenteIA.Criar(s.GetRequiredService<IConfiguracaoIA>(), s.GetRequiredService<IProvedorIA>()))
        // Bloco 29: a IA que olha as fotos usa a mesma chave e o mesmo modelo do assistente.
        .AddSingleton<IAnalistaDeFotos>(s => new AnalistaConformeConfiguracao(s.GetRequiredService<IConfiguracaoIA>(), s.GetRequiredService<IProvedorIA>()))
        .AddTransient<MainWindowViewModel>()
        .BuildServiceProvider();
}
