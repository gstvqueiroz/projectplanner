using Microsoft.Extensions.DependencyInjection;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core;
using ProjectPlanner.Data;
using ProjectPlanner.Core.Fotos;
using ProjectPlanner.App.Services.Cofre;
using ProjectPlanner.IA;
using ProjectPlanner.IA.Conta;
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
        // Assistente de IA (Blocos 19, 23, 45 e 46): pela conta ProjectPlanner (o servidor chama o Gemini ou o Claude), com a
        // sessão no cofre do sistema; GEMINI_API_KEY (direto ao Google) só para desenvolvimento; sem conta, respostas simuladas.
        .AddSingleton(_ => ProvedorGemini.CriarHttpClient())
        .AddSingleton(_ => CofreDoSistema.Criar())
        // Bloco 45: a conta ProjectPlanner (sessão no cofre) e a IA por ela, pelo servidor; a credencial decide o caminho.
        .AddSingleton(s => new ClienteDaConta(s.GetRequiredService<HttpClient>(), ContaProjectPlanner.Servidor(Environment.GetEnvironmentVariable)))
        .AddSingleton<IContaProjectPlanner>(s => new ContaProjectPlanner(s.GetRequiredService<ClienteDaConta>(), s.GetRequiredService<ICofreDeSegredos>(),
            s.GetRequiredService<TimeProvider>(), endereco => s.GetRequiredService<IDialogosService>().AbrirEnderecoAsync(endereco)))
        // Bloco 49: o Telegram pela conta (obras marcadas, caixa de entrada, ligação do chat).
        .AddSingleton<ITelegramDaConta>(s => new TelegramDaConta(s.GetRequiredService<ClienteDaConta>(), s.GetRequiredService<IContaProjectPlanner>()))
        .AddSingleton<IProvedorIA>(s => new ProvedorConformeCredencial(new ProvedorGemini(s.GetRequiredService<HttpClient>()),
            new ProvedorDaConta(s.GetRequiredService<HttpClient>(), s.GetRequiredService<ClienteDaConta>().Servidor, s.GetRequiredService<IContaProjectPlanner>())))
        .AddSingleton<IPreferenciasAssistente, ArquivoPreferenciasAssistente>()
        .AddSingleton<IPreferenciasRelatorio, ArquivoPreferenciasRelatorio>()
        // Bloco 32: o catálogo pessoal de recursos (recursos.json).
        .AddSingleton<ICatalogoDeRecursos, ArquivoCatalogoDeRecursos>()
        // Bloco 33: as bases de preços (SINAPI…) importadas, na pasta referencias.
        .AddSingleton<IBibliotecaDeReferencias, ArquivoBibliotecaDeReferencias>()
        // Bloco 46: a IA pela conta; GEMINI_API_KEY só para quem compila o código.
        .AddSingleton(s => new ConfiguracaoDoAssistente(s.GetRequiredService<IPreferenciasAssistente>(), s.GetRequiredService<IProvedorIA>(),
            Environment.GetEnvironmentVariable, s.GetRequiredService<IContaProjectPlanner>()))
        .AddSingleton<IConfiguracaoIA>(s => s.GetRequiredService<ConfiguracaoDoAssistente>())
        .AddSingleton<IConfiguracaoAssistente>(s => s.GetRequiredService<ConfiguracaoDoAssistente>())
        .AddSingleton(s => FabricaAssistenteIA.Criar(s.GetRequiredService<IConfiguracaoIA>(), s.GetRequiredService<IProvedorIA>()))
        // Bloco 29: a IA que olha as fotos usa a mesma chave e o mesmo modelo do assistente.
        .AddSingleton<IAnalistaDeFotos>(s => new AnalistaConformeConfiguracao(s.GetRequiredService<IConfiguracaoIA>(), s.GetRequiredService<IProvedorIA>()))
        // Bloco 38: a IA que sugere o orçamento, com a mesma chave e o mesmo modelo.
        .AddSingleton<IConsultorDeOrcamento>(s => new ConsultorConformeConfiguracao(s.GetRequiredService<IConfiguracaoIA>(), s.GetRequiredService<IProvedorIA>()))
        // Bloco 43: a IA que analisa os indicadores da obra.
        .AddSingleton<IAnalistaDaObra>(s => new AnalistaDaObraConformeConfiguracao(s.GetRequiredService<IConfiguracaoIA>(), s.GetRequiredService<IProvedorIA>()))
        .AddTransient<MainWindowViewModel>()
        .BuildServiceProvider();
}
