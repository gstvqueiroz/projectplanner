# Assistente de IA — base arquitetural (Bloco 19)

O painel lateral do assistente existe, mas **não há integração com nenhuma API de IA**: não há chave, não há
chamada de rede e o conteúdo dos anexos não é lido. Quem responde hoje é `AssistenteIAIndisponivel`, que só
explica que a integração não foi configurada. Este documento orienta o bloco que fará a integração real.

## Onde está cada coisa

| O quê | Onde |
|---|---|
| Contrato do assistente (`IAssistenteIA`), pedido e resposta (`PedidoIA`, `RespostaIA`), mensagem (`MensagemChat`), anexo (`AnexoIA`) | `src/ProjectPlanner.IA/` |
| Implementação provisória (sem rede) | `src/ProjectPlanner.IA/AssistenteIAIndisponivel.cs` |
| Chave de API e escolha da implementação (`IConfiguracaoIA`, `SemChaveDeApi`, `FabricaAssistenteIA`) | `src/ProjectPlanner.IA/ConfiguracaoIA.cs` |
| Registro no programa | `src/ProjectPlanner.App/Services/Servicos.cs` |
| Painel (ViewModel e tela) | `src/ProjectPlanner.App/ViewModels/AssistenteViewModel.cs`, `src/ProjectPlanner.App/Views/AssistenteView.axaml` |
| Visibilidade do painel entre sessões | `interface.json`, ao lado do `colunas.json` (`ArquivoPreferenciasInterface`) |

`ProjectPlanner.IA` não depende de nada (nem do Core). O SDK do provedor e o `HttpClient` entram **só** nele,
para não contaminar o domínio (Core) nem a interface (App). Um teste garante hoje que o assembly não referencia
`System.Net.*`; ele deve ser ajustado (não apagado) quando a implementação real chegar.

## O contrato

```csharp
public interface IAssistenteIA
{
    bool EstaConfigurado { get; }
    Task<RespostaIA> EnviarAsync(PedidoIA pedido, CancellationToken cancelamento);
}

public sealed record PedidoIA(string Mensagem, IReadOnlyList<AnexoIA> Anexos, IReadOnlyList<MensagemChat> Historico);
public sealed record RespostaIA(string Texto);
```

- Pedido e resposta são registros para que campos novos entrem sem mudar a assinatura.
- `Historico` já vai em cada pedido (as APIs de chat não guardam a conversa), sem os avisos locais
  (`MensagemChat.EhErro`: falhas e cancelamentos).
- Falhas chegam como exceções; o painel as mostra como aviso na conversa. O cancelamento usa o `CancellationToken`
  (botão Cancelar do painel).

## Passos da integração real

1. **Implementação**: em `ProjectPlanner.IA`, uma classe (ex.: `AssistenteIAClaude`) que implemente
   `IAssistenteIA`, com `EstaConfigurado = true`, lendo os anexos (PDF, imagens, texto…) e montando a chamada com
   o histórico.
2. **Chave**: uma implementação de `IConfiguracaoIA` que leia a chave do **cofre do sistema** (Chaves do macOS,
   Gerenciador de Credenciais do Windows), gravada por uma tela Configurações > Assistente. **Nunca** no `.pplan`
   nem em JSON em texto puro. Uma variável de ambiente pode servir só para desenvolvimento.
3. **Escolha**: em `FabricaAssistenteIA.Criar`, devolver a implementação real quando houver chave (o ponto está
   marcado no código).
4. **Latência**: `AssistenteIAIndisponivel` espera 0,5 s só para o painel mostrar "Assistente digitando…"; a
   implementação real tem a latência dela. Resposta em partes (streaming) poderia vir num método adicional que
   devolva `IAsyncEnumerable`.

## O assistente e o cronograma (intenção, não implementado)

- **Leitura**: o App monta um retrato **só de leitura** do projeto aberto — nome, datas, e por tarefa: id, EDT,
  nome, duração, início/término, predecessoras (com tipo e lag), folga e criticidade — e o manda num campo novo
  `PedidoIA.Contexto`. O assistente nunca recebe o `Cronograma` do domínio.
- **Alterações**: a resposta ganha um campo `RespostaIA.Propostas` com operações tipadas (adicionar tarefa,
  mudar duração, criar vínculo, mover, …). O painel mostra cada proposta para o usuário **aceitar ou recusar**, e
  aplica as aceitas pelos comandos do domínio que já existem (com as mesmas validações). O assistente nunca muda o
  cronograma sozinho.
- **Geração de cronograma a partir de documentos** é outra funcionalidade, para um bloco próprio; ela usaria o
  mesmo caminho de propostas.

## Decisões do Bloco 19 (para rever na integração)

- O histórico fica **só em memória**, na sessão, e recomeça quando outro projeto é aberto (Novo, Abrir). Guardar a
  conversa no `.pplan` tem implicações (privacidade, tamanho, compartilhamento do arquivo) a decidir depois.
- Os anexos guardam só caminho, nome, extensão e tamanho (dos metadados); o conteúdo não é aberto.
- Os anexos pendentes vão com a próxima mensagem e a lista é esvaziada no envio.
