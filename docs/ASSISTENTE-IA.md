# Assistente de IA

O painel lateral do assistente (Bloco 19) conversa com o **Gemini, do Google** (Bloco 23), quando há uma chave de API
configurada. Sem chave, quem responde é `AssistenteIAIndisponivel`, que só explica que a integração não está
configurada (respostas simuladas, sem rede).

A cada mensagem, o assistente recebe:

- as **instruções** (quem ele é, responder em português, texto simples, só ler o cronograma);
- um **retrato só de leitura do cronograma aberto**: projeto, datas, e por tarefa Nº, EDT, nome, tipo, duração,
  início/término calculados, % concluído, predecessoras, folga, se é crítica, quantidade de fotos e notas;
- a **conversa inteira** (as APIs de chat não guardam nada entre as chamadas), sem os avisos locais;
- os **anexos da conversa inteira**: PDF e imagens (PNG, JPG, WebP, HEIC) como arquivo, texto (TXT, MD, CSV, JSON)
  lido como parte da mensagem. Word, Excel e outros formatos são recusados ao anexar, com a orientação de exportar
  como PDF. A soma dos anexos da conversa vai até 14 MB (o Gemini aceita 20 MB por pedido, e os arquivos vão em
  base64).

O assistente **não altera o cronograma**: se o usuário pedir uma mudança, ele explica o que fazer no programa.

## Onde está cada coisa

| O quê | Onde |
|---|---|
| Contrato do assistente (`IAssistenteIA`), pedido e resposta (`PedidoIA`, `RespostaIA`), mensagem, anexo | `src/ProjectPlanner.IA/` |
| Provedor de IA (`IProvedorIA`) e a escolha conforme a configuração (`AssistenteConformeConfiguracao`, `FabricaAssistenteIA`) | `src/ProjectPlanner.IA/` |
| Retrato do cronograma (`ContextoDoProjeto`), instruções (`InstrucoesDoAssistente`), anexos aceitos (`AnexosAceitos`) | `src/ProjectPlanner.IA/` |
| O Gemini (`ProvedorGemini`, `AssistenteGemini`, `ChamadaGemini`): REST com `HttpClient`, sem SDK | `src/ProjectPlanner.IA/Gemini/` |
| Cofre do sistema (`ICofreDeSegredos`): Chaves do macOS, Gerenciador de Credenciais do Windows | `src/ProjectPlanner.App/Services/Cofre/` |
| Chave e modelo em uso (`ConfiguracaoDoAssistente`) e o modelo salvo (`assistente.json`) | `src/ProjectPlanner.App/Services/` |
| Tela Configurações do assistente | `ConfiguracaoAssistenteViewModel`, `ConfiguracaoAssistenteWindow` |
| Montagem do retrato a partir da grid | `MainWindowViewModel.RetratoParaOAssistente` |
| Registro no programa | `src/ProjectPlanner.App/Services/Servicos.cs` |

`ProjectPlanner.IA` continua sem depender do domínio (Core): recebe o retrato já pronto. A rede (`System.Net.Http`)
só aparece no namespace `ProjectPlanner.IA.Gemini`, e um teste garante isso.

## A chave de API

- O usuário cola a chave em **Assistente > ⚙ (Configurações do assistente)**, ou no link "Configurar a chave do
  Gemini…" do aviso do painel. A chave é criada em [aistudio.google.com](https://aistudio.google.com) (Get API key).
- Ela fica no **cofre do sistema**: um item "senha genérica" nas **Chaves do macOS** (serviço `ProjectPlanner`, conta
  `gemini-api-key`), ou uma credencial genérica `ProjectPlanner:gemini-api-key` no **Gerenciador de Credenciais do
  Windows**. O acesso é feito direto pelas APIs do sistema (Security.framework e advapi32), sem pacotes de terceiros.
- **Nunca** vai no `.pplan`, no `assistente.json` (que guarda só o modelo) nem no código. A tela não mostra a chave de
  volta: só o fim dela ("…rWc").
- Para desenvolvimento, sem chave no cofre, vale a variável de ambiente **`GEMINI_API_KEY`** (o `.app` aberto pelo
  Finder não enxerga variáveis do terminal).
- A chave vai no cabeçalho `x-goog-api-key` de cada chamada, nunca na URL, e é retirada de qualquer mensagem de erro.
- **Testar conexão** consulta o modelo (`GET models/<modelo>`), sem gastar a cota de mensagens.
- O modelo padrão é `gemini-2.5-flash`; outro nome pode ser salvo na mesma tela, sem nova versão do programa.

**Aviso do macOS ao trocar de versão:** o macOS liga o item das Chaves ao programa que o criou. Uma versão nova
publicada sem assinatura de desenvolvedor (ou o `dotnet run`) é outro programa para ele, e o sistema pergunta se pode
usar o item: "Permitir sempre" resolve. Com uma assinatura Developer ID (ver abaixo), o aviso não aparece.

**Privacidade:** a tela avisa que as mensagens, o cronograma e os anexos vão para o Google. Na camada gratuita da API,
o Google pode usar os dados para melhorar os produtos dele; na camada paga, não.

## Para vender o programa (caminho seguro)

Hoje cada pessoa usa **a própria chave** (o modelo "traga sua chave"), guardada no cofre do computador dela. Isso já é
seguro para distribuir: nenhuma chave vai dentro do `.exe` ou do `.app`. O que **nunca** fazer é embutir a sua chave no
programa: qualquer um extrai a chave do executável e passa a gastar a sua cota.

Para vender com a IA incluída (sem o cliente precisar de chave), o caminho é um **servidor intermediário**:

1. O programa fala com um servidor seu (HTTPS), identificado pela licença ou pela conta do cliente, não com o Google.
2. O servidor guarda a chave do Gemini (ou do Claude) e repassa os pedidos, com limite de uso por cliente, registro de
   consumo e bloqueio de licenças canceladas.
3. No programa, isso é só outro `IProvedorIA` (por exemplo, `ProvedorServidorProprio`), escolhido em `Servicos.cs`; o
   painel, o retrato do cronograma e os anexos não mudam. O "segredo" guardado no cofre passa a ser o token da conta do
   cliente, não uma chave de IA.

Outros pontos para a versão comercial: assinar o programa (Developer ID e notarização no Mac, certificado de assinatura
de código no Windows), usar a camada paga da API (os dados não são usados para treino) e mostrar os termos de uso de IA
ao cliente.

## Trocar ou acrescentar provedores (Claude)

Um provedor novo é uma classe que implementa `IProvedorIA` (nome, modelo padrão, validação do nome do modelo, criar o
assistente, testar a conexão) e uma que implementa `IAssistenteIA`. As instruções (`InstrucoesDoAssistente`), o retrato
(`ContextoDoProjeto`) e as regras de anexos (`AnexosAceitos`) são de todos os provedores. A chave de cada provedor fica
no mesmo cofre, com outro nome de conta.

## O que ainda não existe (blocos futuros)

- **Propostas de alteração**: a resposta ganharia `RespostaIA.Propostas`, com operações tipadas (adicionar tarefa,
  mudar duração, criar vínculo…), que o painel mostraria para o usuário aceitar ou recusar e aplicaria pelos comandos do
  domínio. Hoje o assistente só lê.
- **Gerar um cronograma a partir de documentos**: usaria o mesmo caminho das propostas.
- **Respostas em partes (streaming)** e anexos maiores que 14 MB (pela API de arquivos do Gemini).
- **Guardar a conversa**: o histórico continua só em memória, na sessão, e recomeça quando outro projeto é aberto.
