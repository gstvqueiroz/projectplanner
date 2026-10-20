# Assistente de IA

O painel lateral do assistente (Bloco 19) conversa com o **Gemini, do Google** (Bloco 23) e, desde o Bloco 46, também
com o **Claude, da Anthropic**, pela conta ProjectPlanner (o servidor chama a IA; ver "Níveis e créditos", abaixo). Sem
conta, quem responde é `AssistenteIAIndisponivel`, que só explica que a integração não está configurada (respostas
simuladas, sem rede). Os Blocos 23 a 44 usavam a chave do Gemini da própria pessoa; desde o Bloco 46 ela só vale pela
variável `GEMINI_API_KEY`, para quem compila o código.

A cada mensagem, o assistente recebe:

- as **instruções** (quem ele é, responder em português, texto simples, como alterar o cronograma);
- um **retrato só de leitura do cronograma aberto**: projeto, datas, e por tarefa Nº, EDT, nome, tipo, duração,
  início/término calculados, % concluído, predecessoras, folga, se é crítica, quantidade de fotos e notas;
- a **conversa inteira** (as APIs de chat não guardam nada entre as chamadas), sem os avisos locais;
- desde o Bloco 26, a **condução da conversa**: o modo (montar agora ou estudar antes), a ficha do projeto e o andamento
  do estudo (ver "Dois modos, ficha do projeto e conversa salva", abaixo);
- os **anexos da conversa inteira**: PDF e imagens (PNG, JPG, WebP, HEIC) como arquivo, texto (TXT, MD, CSV, JSON)
  lido como parte da mensagem. Word, Excel e outros formatos são recusados ao anexar, com a orientação de exportar
  como PDF. A soma dos anexos da conversa vai até 14 MB (o Gemini aceita 20 MB por pedido, e os arquivos vão em
  base64).

Desde o Bloco 24, o assistente **altera o cronograma pelo chat**: pedir "monte o cronograma de uma residência de
100 m²", "acrescente a etapa de cobertura depois da estrutura" ou "a laje leva 8 dias" faz o cronograma mudar na hora
(ver "Alterações do cronograma pelo chat", abaixo).

## Onde está cada coisa

| O quê | Onde |
|---|---|
| Contrato do assistente (`IAssistenteIA`), pedido e resposta (`PedidoIA`, `RespostaIA`), mensagem, anexo | `src/ProjectPlanner.IA/` |
| Provedor de IA (`IProvedorIA`) e a escolha conforme a configuração (`AssistenteConformeConfiguracao`, `FabricaAssistenteIA`) | `src/ProjectPlanner.IA/` |
| Retrato do cronograma (`ContextoDoProjeto`), instruções (`InstrucoesDoAssistente`), anexos aceitos (`AnexosAceitos`) | `src/ProjectPlanner.IA/` |
| Formato da resposta (`FormatoDaResposta`) e as alterações (`AlteracaoDoCronograma`, `AcoesNoCronograma`) | `src/ProjectPlanner.IA/` |
| Aplicação das alterações (`AplicadorDeAlteracoes`) e o contrato com o painel (`ICronogramaDoAssistente`) | `src/ProjectPlanner.App/Services/` |
| Aplicar, conferir fotos, trocar o cronograma e Desfazer | `MainWindowViewModel.Assistente.cs` |
| O Gemini (`ProvedorGemini`, `AssistenteGemini`, `ChamadaGemini`): REST com `HttpClient`, sem SDK | `src/ProjectPlanner.IA/Gemini/` |
| Cofre do sistema (`ICofreDeSegredos`): Chaves do macOS, Gerenciador de Credenciais do Windows | `src/ProjectPlanner.App/Services/Cofre/` |
| Chave e modelo em uso (`ConfiguracaoDoAssistente`) e o modelo salvo (`assistente.json`) | `src/ProjectPlanner.App/Services/` |
| Tela Configurações do assistente | `ConfiguracaoAssistenteViewModel`, `ConfiguracaoAssistenteWindow` |
| Montagem do retrato a partir da grid | `MainWindowViewModel.RetratoParaOAssistente` |
| Registro no programa | `src/ProjectPlanner.App/Services/Servicos.cs` |
| Bloco 29: a IA das fotos (`IAnalistaDeFotos`, pedidos e sugestões, `AnalistaConformeConfiguracao`) | `src/ProjectPlanner.IA/AnaliseDeFotos.cs` |
| Bloco 29: o Gemini olhando as fotos (`AnalistaGemini`) | `src/ProjectPlanner.IA/Gemini/` |
| Bloco 29: as telas (Sugestões da IA, texto do relatório) | `FotosDoProjetoViewModel.IA.cs`, `RelatorioFotograficoViewModel.Texto.cs` |
| Bloco 38: o consultor de orçamento (`IConsultorDeOrcamento`, `PedidoDeOrcamento`, `SugestaoDaIA`) | `src/ProjectPlanner.IA/ConsultaDeOrcamento.cs` |
| Bloco 38: o Gemini sugerindo o orçamento (`ConsultorGemini`) | `src/ProjectPlanner.IA/Gemini/` |
| Bloco 38: o pedido montado do projeto (candidatas, horas de equipe, ficha) e a volta para o domínio | `src/ProjectPlanner.App/Services/ConsultaDeOrcamento.cs` |
| Bloco 38: conferir e aplicar as sugestões (`AplicacaoDeSugestoes`) | `src/ProjectPlanner.Core/Orcamento/SugestoesDeOrcamento.cs` |
| Bloco 38: a aba Sugestões da IA | `OrcamentoViewModel.Sugestoes.cs`, `OrcamentoWindow` |

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

Desde o Bloco 44 esse servidor existe, neste repositório: `src/ProjectPlanner.Servidor`, com contas, planos e cota, e o
ponto de IA no mesmo caminho do Gemini. Desde o Bloco 45, o programa usa a "Conta ProjectPlanner" (menu Conta e
configurações do assistente) como outra forma de chegar ao Gemini. As mesmas classes `AssistenteGemini`, `AnalistaGemini`,
`ConsultorGemini` e `AnalistaDaObraGemini` recebem um `AcessoAoGemini`: `AcessoPorChave` vai direto ao Google, e
`AcessoPelaConta` vai pelo servidor, com o token. Ver `docs/SERVIDOR.md`.

Outros pontos para a versão comercial: assinar o programa (Developer ID e notarização no Mac, certificado de assinatura
de código no Windows), usar a camada paga da API (os dados não são usados para treino) e mostrar os termos de uso de IA
ao cliente.

## Trocar ou acrescentar provedores (Claude)

Um provedor novo é uma classe que implementa `IProvedorIA` (nome, modelo padrão, validação do nome do modelo, criar o
assistente, testar a conexão) e uma que implementa `IAssistenteIA`. As instruções (`InstrucoesDoAssistente`), o retrato
(`ContextoDoProjeto`) e as regras de anexos (`AnexosAceitos`) são de todos os provedores. A chave de cada provedor fica
no mesmo cofre, com outro nome de conta.

## Alterações do cronograma pelo chat (Bloco 24)

**O formato.** O assistente responde sempre em JSON: `{"mensagem": "...", "alteracoes": [...]}` (`FormatoDaResposta`).
O Gemini é obrigado a seguir esse formato pelo esquema de resposta (`responseSchema`); outro provedor recebe o mesmo
pelas instruções (`InstrucoesDoAssistente.Alteracoes`). Uma resposta que só conversa traz a lista vazia.

**As ações** (`AlteracaoDoCronograma`, `AcoesNoCronograma`):

| Ação | Campos | O que faz no domínio |
|---|---|---|
| `criar` | `ref`, `nome`, `duracao` (0 = marco), `pai`, `percentual`, `notas` | `AdicionarTarefa` no fim das subtarefas do pai |
| `alterar` | `tarefa` + `nome`, `duracao`, `percentual`, `notas` | `Renomear`, `AlterarDuracao`, `AtualizarPercentualConcluido`, `AlterarNotas` |
| `remover` | `tarefa` | `RemoverTarefa` (com as subtarefas e os vínculos) |
| `vincular` | `de`, `para`, `tipo` (TI, II, TT, IT), `defasagem` | `AdicionarPredecessora` (troca o vínculo que já existir entre as duas) |
| `desvincular` | `de`, `para` | `RemoverPredecessora` |
| `mover` | `tarefa`, `pai` | `DefinirPai` |
| `projeto` | `nome`, `inicio` (AAAA-MM-DD) | `Renomear`, `AlterarDataInicio` do cronograma |
| `limpar` | — | remove todas as tarefas |

**As referências.** Uma tarefa que já existe é indicada pelo **Nº da linha no retrato mandado com o pedido** (ou pela
EDT, "1.2"); o retrato leva o Id de cada linha, então o Nº continua apontando para a mesma tarefa mesmo depois de outras
serem criadas ou removidas na mesma resposta. Uma tarefa criada na resposta é indicada pela referência que o assistente
deu a ela (`ref`, como "n1").

**A aplicação** (`AplicadorDeAlteracoes`, `MainWindowViewModel.Assistente.cs`):

1. As alterações são aplicadas, na ordem, numa **cópia** do cronograma (`Cronograma.Copiar`), pelos comandos do domínio
   e com as mesmas regras de sempre (resumos sem vínculos, marcos sem subtarefas, sem ciclos…). Depois, o cálculo do
   cronograma precisa fechar.
2. Se uma alteração não vale, **nenhuma é aplicada**: o assistente recebe o motivo ("Alteração 5 (vincular 3 → n2): …")
   e manda a lista de novo, corrigida, uma vez. Se falhar de novo, a conversa mostra "Nada foi alterado" com o motivo.
3. Se as alterações removem tarefas com fotos, o programa pergunta antes de aplicar.
4. A cópia entra no lugar do cronograma aberto (o projeto fica com alteração não salva), e a conversa mostra o que
   mudou ("Cronograma alterado: 12 tarefas criadas · 15 vínculos criados") com o botão **Desfazer**.
5. **Desfazer** põe de volta o cronograma de antes daquela resposta (guardado intacto, com as fotos). Só a última
   aplicação pode ser desfeita, e só enquanto o mesmo projeto estiver aberto; se o cronograma mudou depois dela (à mão),
   o programa pergunta antes.

## Dois modos, ficha do projeto e conversa salva (Bloco 26)

**A escolha do modo.** Num pedido para criar ou refazer o cronograma inteiro, o assistente não monta nada de cara: responde
com o cartão "Como você prefere?":
- **Montar o cronograma agora:** com o que já tem e premissas comuns de obra, que ele registra na ficha;
- **Estudar e conversar antes:** ele lê os documentos, pergunta e mostra o plano antes de montar.

Quem reconhece o pedido de cronograma é o próprio assistente (pelas instruções), não uma lista de palavras no programa;
ajustes pontuais ("a laje leva 8 dias"), perguntas e análises são atendidos direto, como antes. A escolha vale para a
conversa (Limpar conversa recomeça). Em **Configurações do assistente > Ao pedir um cronograma** dá para deixar sempre
perguntar (padrão), sempre montar direto ou sempre estudar antes; fica no `assistente.json`, como o campo `"modo"`.

**O estudo**, com a etapa à vista no topo do painel ("Estudo · perguntas (2 de 4)"):

1. **Leitura:** o assistente conta o que entendeu dos documentos e preenche a ficha.
2. **Perguntas:** de 1 a 5 por rodada, só sobre o que falta e importa para o cronograma (início, prazo, dias de trabalho,
   equipe, etapas incluídas ou excluídas, nível de detalhe). Cada uma tem respostas para clicar (mais "Não sei (use o
   comum)") e um campo para escrever outra; o texto da caixa embaixo vai junto como observação. No máximo 4 rodadas;
   **Pular para o plano** encerra antes.
3. **Plano:** as etapas com as durações, o término estimado e as premissas, com **Aprovar e montar** e **Ajustar…**
   (que começa a mensagem "Ajuste no plano: " na caixa). Um plano novo precisa de aprovação nova.
4. **Montagem:** aprovado o plano, o assistente monta o cronograma completo pelas alterações, aplicadas como sempre (com
   Desfazer). Montado o cronograma, a conversa passa ao modo direto, para os ajustes seguintes.

Os botões do cartão, das perguntas e do plano só valem na última mensagem e não durante a espera.

**A ficha do projeto** (no topo do painel, recolhida) é o que já foi definido sobre a obra: o assistente acrescenta e
corrige campos a cada resposta, e você edita, apaga (valor vazio ou ×) e acrescenta campos. Ela vai em cada pedido, e o
assistente é instruído a não perguntar de novo o que já está nela. Limites: 60 campos, nome até 80 e valor até 1.000
caracteres.

**O formato.** Além de `mensagem` e `alteracoes`, a resposta pode ter `tipo` (`resposta`, `modo`, `perguntas`, `plano`),
`ficha` ([{campo, valor}], valor vazio apaga), `perguntas` ([{pergunta, opcoes}]) e `plano` ({etapas: [{nome, duracao,
observacao}], termino, premissas}). Os campos novos são opcionais no esquema do Gemini (os obrigatórios continuam os de
antes). Respostas dos tipos modo, perguntas e plano nunca alteram o cronograma (alterações nelas são ignoradas); perguntas
ou plano vazios viram uma resposta comum. O pedido leva a condução (`ConducaoDaConversa`: modo, preferência, ficha,
rodadas, plano aprovado, "apresentar o plano agora"), e as instruções mudam com ela (`InstrucoesDoAssistente.Conducao`).
No histórico, uma resposta estruturada volta como o JSON que ela foi (sem as alterações, que já estão no retrato).

**A conversa no .pplan.** A conversa e a ficha ficam na tabela `AssistenteDoProjeto` (migration aditiva, uma linha por
projeto, JSON versionado; nenhuma linha com a conversa vazia). Ao reabrir, as mensagens, as perguntas, o plano e a ficha
voltam, e o estudo continua de onde parou. Os avisos locais ("Resposta cancelada") não são guardados; as alterações de
sessões anteriores aparecem como texto, sem Desfazer. Os anexos ficam como referência ao arquivo (caminho e tamanho):
voltam a ser enviados enquanto o arquivo existir no computador; em outro computador viram uma nota. Uma conversa que não
dá para ler fica de fora com aviso, e o projeto abre normalmente.

As mensagens sozinhas não marcam o projeto como alterado (vão junto quando ele for salvo); a ficha, o modo e a aprovação
do plano marcam, então um estudo em andamento não se perde sem a pergunta de salvar.

| O quê | Onde |
|---|---|
| Conversa, ficha, modo (domínio) | `src/ProjectPlanner.Core/Assistente/ConversaDoProjeto.cs` |
| Gravação no .pplan | `src/ProjectPlanner.Data/FormatoDaConversa.cs`, migration `AssistenteDoProjeto` |
| Condução, tipos de resposta, ficha e plano no protocolo | `src/ProjectPlanner.IA/ConducaoDaConversa.cs`, `FormatoDaResposta`, `InstrucoesDoAssistente` |
| Painel: cartão, perguntas, plano, ficha | `AssistenteViewModel`, `ConversaNaTela.cs`, `AssistenteView.axaml` |

## Desfazer em vários níveis (Bloco 30)

Cada resposta aplicada é um passo do histórico geral do Desfazer (Editar > Desfazer, ⌘Z). O Desfazer do chat vale para
qualquer resposta ainda no histórico: as alterações feitas depois dela também são desfeitas (perguntando antes), e o
Refazer traz tudo de volta. O chat mostra "Desfeito" também quando o Desfazer geral passa pela resposta.

## A IA olhando as fotos (Bloco 29)

`IAnalistaDeFotos` usa a mesma chave e o mesmo modelo do assistente, e faz três pedidos, cada um numa chamada só ao
Gemini, com as imagens guardadas no projeto (JPEG em base64) e um formato de resposta obrigatório:

| Pedido | Onde | Resposta | Limite |
|---|---|---|---|
| Legendas das fotos sem legenda (as da linha do tempo, com os filtros) | Fotos do projeto > Sugestões da IA | `{legendas: [{id, legenda}]}` | 12 fotos |
| % concluído pelas fotos recentes (tarefas em andamento com fotos dos últimos 30 dias) | Fotos do projeto > Sugestões da IA | `{tarefas: [{id, percentual, justificativa, diverge}]}` | 10 tarefas, 4 fotos cada |
| Texto do andamento do período e pontos de atenção | Arquivo > Relatório fotográfico… > Sugerir com IA | `{andamento, pontos: []}` | 8 fotos |

Tudo é sugestão: as telas mostram para revisar e editar, e só o que o usuário marcar é aplicado (um passo do Desfazer:
"Legendas sugeridas pela IA", "% sugerido pela IA"). O texto do relatório vai para os campos da tela, editáveis, antes
de gerar. A resposta é conferida antes: ids que não foram pedidos, repetidos ou vazios ficam de fora, o % fica entre 0
e 100, e as legendas são cortadas em 300 caracteres. As instruções pedem para descrever só o que aparece nas fotos e nos
dados, sem inventar serviços ou problemas (`AnalistaGemini.Instrucoes*`).

**Custo:** cada foto vai na versão guardada (até 1600 px), cerca de 1.500 tokens de entrada no Gemini; um pedido de 12
fotos fica em torno de 20 mil tokens, alguns centavos de real com o `gemini-2.5-flash`. Os limites acima seguram o custo.

**Calibrar:** os testes usam uma IA simulada. A qualidade das legendas, do % e do texto só se vê com a chave e fotos de
obra de verdade; ajuste as instruções em `AnalistaGemini` depois de testar.

## A IA no orçamento (Bloco 38)

`IConsultorDeOrcamento` usa a mesma chave e o mesmo modelo. Um pedido só, sem imagens: a obra (a ficha do assistente,
como área e pavimentos), a base escolhida, o BDI, as tarefas (até 150, as sem preço ou sem quantidade primeiro) e os
recursos por dia. Para cada tarefa o App manda até 10 **candidatas**: as composições da base com mais palavras do nome
da tarefa (as do resumo valem a metade). A IA só pode escolher entre elas, e o preço vem sempre da base; sem candidata
adequada, ela estima o custo por unidade (materiais e mão de obra, sem BDI), que entra como "Estimativa IA".

Resposta: `{resumo, sugestoes: [{tipo, tarefa, recurso, codigo, descricao, unidade, valor, custoMateriais,
custoMaoDeObra, justificativa}]}`, com `tipo` entre composicao, estimativa, quantidade, duracao, disponivel, bdi e
alerta. Antes de virar sugestão, a resposta é conferida (`ConsultorGemini.Interpretar`): tipos dos grupos pedidos,
tarefas e recursos do pedido, códigos só entre as candidatas, números válidos, uma de cada por tarefa. Na hora de
aplicar, o domínio confere de novo (`AplicacaoDeSugestoes.Motivo`): a tarefa pode ter virado resumo, o contrato pode ter
travado.

**Pelo chat:** a resposta do assistente ganhou o campo opcional `orcamento` (o pedido resumido). Com ele, o App abre
Projeto > Orçamento… na aba Sugestões da IA, já pedindo com esse foco. O retrato do cronograma leva o resumo do
orçamento (até 200 serviços) para o assistente responder sobre custos.

**Custo:** só texto; com 100 tarefas e 10 candidatas cada, cerca de 40 mil tokens de entrada, alguns centavos com o
`gemini-2.5-flash`. **Calibrar:** a qualidade das escolhas e das estimativas só se vê com a chave e uma base SINAPI de
verdade; ajuste `ConsultorGemini.Instrucoes` e a busca das candidatas (`TabelaDeReferencia.Candidatas`) depois de testar.

## A IA analisando os indicadores (Bloco 43)

`IAnalistaDaObra` usa a mesma chave e o mesmo modelo. O App manda os indicadores já em texto
(`TextosDaAnalise.ParaAIA`: prazo, cartões de custo e margem, contrato, etapas, caixa e o semáforo) e a ficha do
assistente; o Gemini responde `{resumo, pontos, recomendacoes}` (até 6 itens de 300 caracteres cada). O texto vai para
campos editáveis em Indicadores > Alertas e análise e, quando preenchido, para o PDF e o Word. Não muda nada no projeto
e não é guardado no .pplan (vale para a exportação daquele momento). Calibrar: `AnalistaDaObraGemini.Instrucoes`.

## O chat como central da obra (Bloco 41)

O retrato do pedido ganhou a seção **Situação da obra** (`MainWindowViewModel.SituacaoDaObra`, até 250 linhas): os
indicadores e o semáforo (`TextosDaAnalise.ParaAIA`), as medições, os custos, os recursos, as superalocações, as fotos e
os relatórios. A resposta ganhou dois campos opcionais:

- `abrir: {tela, aba, medicao, de, ate}`: o App abre a tela (`MainWindowViewModel.AbrirTelaAsync`) e põe na mensagem o
  botão "Abrir …". Telas e abas em `TelasDoPrograma`.
- `atualizacoes: [{tipo, tarefa, percentual, quantidade, medicao, de, ate, data, categoria, descricao, valor, documento,
  nome}]`, com `tipo` entre percentual, medicao, custo, inicio e linhaDeBase. Elas **não** entram direto: viram um cartão
  (`CartaoDeAtualizacoes`). `AtualizacoesDoAssistente.Conferir` resolve as tarefas pelo Nº do retrato, escreve cada item e
  descobre o problema de cada um aplicando-o sozinho numa cópia. "Aplicar" aplica as válidas numa cópia e, se tudo vale,
  troca o cronograma: um passo do Desfazer, "Atualizações do assistente". O estado do cartão fica na conversa como texto.

## Fotos com IA (Bloco 42)

`IAnalistaDeFotos.ClassificarFotosAsync`: até 10 fotos por pedido (o App divide o lote), com as tarefas (até 150, as em
execução no período das fotos primeiro, marcadas) e os locais da obra. Resposta `{fotos: [{id, tarefa, confianca,
legenda, local, percentual, justificativa}]}`, conferida: tarefas e locais só do pedido, % de 0 a 100, sem tarefa a
confiança vira baixa. O resultado preenche a tabela da importação em lote (Fotos do projeto); nada é importado antes do
clique em Importar. O chat ganhou `importarFotos: "anexos" | "pasta"` e `abrir.texto` (o relatório já pedindo o texto).

## Níveis e créditos (Bloco 46)

- **Os níveis:** Padrão (Gemini 2.5 Flash), Avançado (Gemini 2.5 Pro) e Máximo (Claude Opus 5.5). O programa só conhece
  o id do modelo, salvo em `assistente.json`: o catálogo e os preços ficam no servidor (`GET /v1/modelos`).
- **Onde escolher:** nas configurações do assistente ou no seletor no topo do painel. Os níveis fora do plano aparecem
  apagados, com "do plano Profissional".
- **O mesmo formato para todos:** as classes do Gemini montam o mesmo pedido para qualquer nível. Para o Claude, o
  servidor traduz (`TradutorClaude`), e a resposta estruturada vira uma ferramenta obrigatória; as fotos HEIC não vão ao
  Claude.
- **O saldo:** chega no cabeçalho `X-Creditos-Restantes` de cada resposta (`AcessoPelaConta.Recebeu` →
  `ISessaoDaConta.AtualizarSaldo`) e aparece ao lado do seletor.

## O que ainda não existe (blocos futuros)

- **Inserir em posição**: tarefas criadas pelo assistente entram no fim das subtarefas do pai; não há "antes de".
- **Respostas em partes (streaming)** e anexos maiores que 14 MB (pela API de arquivos do Gemini).
- **Ler Word e Excel direto** (hoje, exportar como PDF).
