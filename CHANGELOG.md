# Novidades do ProjectPlanner

A versão segue MAJOR.MINOR.PATCH, com o MINOR igual ao número do bloco de melhoria (ver PUBLISHING.md). A seção da
versão atual aparece também em Ajuda > Sobre o ProjectPlanner.

## 0.59.0

**Ordens de serviço no grupo do Telegram** (precisa da obra no Telegram e de um grupo da obra com o bot).

- Na tela da OS, **Mandar ao grupo do Telegram** põe a OS emitida no grupo da obra: executante, datas, itens (com os preços
  e o total, ou sem eles, desmarcando "com os valores") e os botões para a equipe: **Aceitar** e **Recusar**; depois
  **Comecei** e **Terminei**. A mensagem se atualiza a cada passo, e as mudanças seguintes feitas no programa (conferir,
  reprovar, cancelar) vão junto.
- Quem é do campo ou da engenharia toca nos botões; quem só acompanha, não. Recusar pede o motivo:
  `/recusar 14 o preço não cobre o material`. `/os` lista as OS abertas e `/os 14` manda uma de novo.
- **Conta > Buscar do Telegram** traz os passos da equipe e aplica nas OS, com o nome de quem fez no histórico ("Aceita
  (João, pelo Telegram)"). O que a OS não aceita mais (porque já mudou no programa) fica de lado. Entra no Desfazer.
- Os botões não usam a IA (não gastam créditos).

## 0.58.0

**Ordens de serviço** (Projeto > Ordens de serviço… e o botão novo da barra, no grupo Obra).

- **Nova OS a partir do cronograma:** selecione uma tarefa (ou uma etapa inteira) na grid e clique em Nova OS. Os itens, as
  quantidades, as unidades e as datas vêm das tarefas; o preço vem da composição do orçamento, só a mão de obra (o
  material é da obra) ou o serviço completo, sem BDI (é o que se paga ao executante). Também dá para marcar outras tarefas e
  trazer os itens delas, ou pôr itens avulsos.
- **A ficha:** serviço, executante (equipe própria ou empreiteiro, com sugestões dos recursos e fornecedores), contato, forma
  de pagamento (empreitada, por medição ou diária), início e prazo, observações e uma lista de verificação.
- **O fluxo:** rascunho → emitida → aceita → em execução → concluída → conferida. A emitida pode ser recusada (com o motivo)
  e voltar a rascunho para corrigir; a concluída pode ser reprovada na conferência e voltar à execução; antes da
  conferência, dá para cancelar. Cada passo fica no histórico da OS e no Desfazer.
- **A lista:** filtro (abertas, vencidas, encerradas) e os números da obra: OS em andamento, vencidas, o valor a pagar e os
  rascunhos.
- **PDF para assinar** (A4): obra, contratante e endereço, executante, datas, itens com o total, observações, verificação,
  histórico e as assinaturas do emitente, do executante e da conferência. **Imprimir…** abre o PDF no leitor do sistema.
- As OS ficam no arquivo da obra (.pplan). Um arquivo salvo nesta versão não abre nas anteriores (atualize o programa nos outros computadores).

## 0.57.0

**Imprimir o Gantt** (Arquivo > Imprimir o Gantt…, ⌘P no Mac ou Ctrl+P no Windows, e o botão da impressora na barra).

- **Folha:** A4, A3, A2, A1, A0, Carta ou Ofício (216 × 330 mm), em paisagem ou retrato.
- **Escala do tempo:** "ajustar à largura da folha" (a obra inteira numa folha de largura) ou dias, semanas e meses. Nas
  escalas fixas, uma obra longa é dividida em folhas lado a lado para emendar; a tabela sai na primeira folha de cada
  fileira, e o rodapé diz o período de cada uma.
- **Colunas da tabela:** EDT, nome (com o recuo da EDT), duração, início, término, % concluído e predecessoras, cada uma
  opcional.
- **No desenho:** caminho crítico em vermelho, linha de base, vínculos (setas), linha de hoje, nomes ao lado das barras,
  prazos, sábados e domingos sombreados. Muitas tarefas são divididas em páginas.
- **Tarefas:** todas, como na tela (sem as dos resumos recolhidos) ou só até o 1º, 2º ou 3º nível da EDT.
- **Legenda** em todas as folhas, cabeçalho com a obra, as datas e a linha de base, e "Página X de Y".
- **Prévia** de cada página na própria janela. **Salvar PDF…** grava o arquivo; **Imprimir…** abre o PDF no leitor do
  sistema para imprimir (escolha na impressora a mesma folha).

## 0.56.0

**Mais atalhos na barra de ícones** (12 botões novos; cada um pode ser escondido em Exibir > Barra de ícones).

- **Conta e Telegram** (grupo novo): Minha conta, Conectar o Telegram, Usar esta obra no Telegram (fica destacado quando a
  obra já está no Telegram) e Buscar do Telegram.
- **Relatórios e obra:** Cronograma físico-financeiro, Resumo do projeto, Recursos do projeto, Uso de recursos e Custos
  realizados.
- **Planejamento:** Linhas de base e Importar valores.
- **Arquivo:** Exportar para CSV.
- Numa janela estreita, os que não cabem continuam no "»".

## 0.55.2

Correção no servidor: a causa do "Erro no servidor" ao montar o cronograma.

- **O servidor ficava sem memória** (erro 9EUK72: OutOfMemoryException). Com os documentos da obra (PDFs) anexados, o
  pedido tem dezenas de MB, e o servidor fazia várias cópias dele ao repassar ao Claude, passando dos 512 MB do Render.
  Agora o pedido é lido e repassado com bem menos cópias, e o servidor usa o modo de memória econômica do .NET. Só o
  servidor mudou.

## 0.55.1

Para achar e evitar o "Erro no servidor" ao montar o cronograma.

- **"Montar o cronograma agora" vai ao modelo forte.** A triagem do nível Automático via só a frase "Pode montar agora, com
  o que já tem." e achava que era um pedido simples, mandando a obra inteira ao modelo mais barato. Agora o programa avisa
  que é a montagem, e ela vai ao Sonnet (com a confirmação de custo).
- **Anexos grandes não estouram mais o limite.** O servidor aceitava até 15 MB por pedido, mas o programa manda até 14 MB
  de anexos, que crescem um terço no envio; o limite passou para 25 MB.
- **Erros inesperados agora têm código.** Em vez de só "Erro no servidor", a mensagem traz um código ("código K7Q2MX"), e
  o painel /admin mostra os últimos erros com o motivo, para a causa ser achada e corrigida.

## 0.55.0

**A SINAPI direto da Caixa** (Bloco 40).

- Em Projeto > Orçamento, aba Bases: escolha a **UF** e a **desoneração** e clique em **Baixar da Caixa**. O programa
  procura a SINAPI mais recente no site da Caixa (hoje, setembro de 2026), baixa o arquivo (uns 16 MB) e monta a base
  do estado com os **insumos e as composições** (em São Paulo, cerca de 4.900 insumos e 10.600 composições). Não precisa
  mais baixar e importar planilha por planilha.
- Conferido com o arquivo real de 09/2026: todas as composições com custo abrem, e o custo calculado bate com o da Caixa
  (em 883 de 8.444, diferença de centavos de arredondamento). As que a própria SINAPI marca "sem custo" ficam fora das
  sugestões.
- Com a base baixada, as sugestões de orçamento da IA (e o botão "Estimar orçamento e recursos desta obra") usam as
  composições reais, e aplicar o orçamento cria a mão de obra e os equipamentos de cada tarefa.

## 0.54.0

**A obra completa: cronograma, orçamento e recursos** (Bloco 54).

- **Depois de montar o cronograma**, o painel do assistente mostra o botão **Estimar orçamento e recursos desta obra**.
  Ele abre Projeto > Orçamento com as sugestões da IA para todas as tarefas: preços (da base SINAPI, se importada, ou
  estimados), quantidades e equipes. Você confere e aplica as que quiser.
- **Pedindo junto** ("monte o cronograma de uma casa de 100 m² com orçamento"), o assistente monta o cronograma e já abre
  as sugestões do orçamento.
- **As equipes viram recursos.** A estimativa da IA traz a equipe de cada tarefa (por exemplo, 2 pedreiros e 2 serventes, com
  o custo do dia). Ao aplicar, esses profissionais entram como recursos da obra e são atribuídos à tarefa, e o uso de
  recursos e o nivelamento passam a funcionar mesmo sem a base SINAPI.
- **Obras grandes** vão à IA em partes de 30 tarefas, para a resposta não sair cortada; as sugestões se juntam na tela.

## 0.53.1

Correções para quando a IA não gerava o cronograma.

- **O nível Automático mandava "monte o cronograma" ao modelo mais barato.** A triagem pedia ao Haiku uma configuração que
  ele recusa, falhava sempre e caía no pedido simples. Agora a triagem funciona, e os pedidos grandes vão ao Sonnet, com a
  confirmação de custo.
- **Às vezes o Claude respondia em texto, sem o formato combinado,** e o programa mostrava o texto sem mudar nada no
  cronograma. Agora o servidor percebe e pede de novo uma vez, lembrando o formato.
- **Cronogramas grandes saíam cortados.** O Haiku e o Sonnet pensam antes de responder, e esse pensamento entrava no
  limite de tamanho. O limite dobrou (32 mil tokens), o tempo de espera passou de 5 para 10 minutos, e o assistente monta
  até cerca de 100 tarefas por vez: numa obra maior, ele monta as primeiras etapas e você pede "continue".

## 0.53.0

**Grupos do Telegram por obra** (Bloco 53): a equipe trabalha junto no mesmo grupo.

- Crie um grupo no Telegram para a obra, ponha o bot nele e mande **/obra** (você, que já ligou o chat privado). A equipe
  não precisa de conta, e os créditos são os seus.
- O técnico manda **fotos** (vão para a caixa da obra sem gastar IA) e mensagens com **#atualizacao** ("#atualizacao
  alvenaria em 70%"). O engenheiro, o chefe ou quem precisar **marca o bot** para perguntar ("@bot o que está atrasado?").
- **A conversa normal do grupo é ignorada** e não gasta nada.
- **Papéis:** acompanhar (só pergunta), campo (manda fotos e atualizações) e engenharia (também muda os papéis).
  Responda à mensagem de alguém com **/papel campo** para mudar; **/equipe** mostra quem é quem.
- Tudo vai para a caixa da obra com o nome de quem mandou, e você confere e aplica no programa (Conta > Buscar do
  Telegram), como antes.
- Configuração única no @BotFather: `/setprivacy` → Disable (ver docs/SERVIDOR.md), para o bot ver as fotos do grupo.

## 0.52.0

O nível **Automático**: a IA escolhe o modelo certo para cada pedido (Bloco 51).

- **Agora é o padrão.** Perguntas e atualizações vão no modelo mais barato; pedidos médios (alterar várias tarefas), no
  Intermediário; os grandes (montar um cronograma inteiro, ler documentos longos), no mais forte que o seu plano libera.
  Quem tinha escolhido o Avançado, o Intermediário ou o Máximo continua no que escolheu.
- **Antes de um pedido caro, o programa pergunta.** A partir de 20 créditos, um cartão mostra a estimativa ("cerca de
  120 créditos, 2% do seu saldo") e deixa usar o modelo recomendado, tentar no mais barato ou cancelar. Dá para marcar
  "Não perguntar de novo nesta conversa".
- **Refazer com um modelo melhor.** Se a resposta não ficou boa, um botão manda a mesma pergunta ao modelo mais forte.
- Embaixo do seletor de nível aparece qual modelo respondeu o último pedido.
- As fotos, o orçamento, a análise da obra e o Telegram continuam no modelo mais barato.

## 0.51.0

Assinatura e créditos pelo Mercado Pago (Bloco 47).

- Em **Conta > Minha conta**:
  - **Assinar o Profissional:** o pagamento abre no navegador, com Pix ou cartão, e o plano muda sozinho assim que o
    Mercado Pago confirma (clique em **Atualizar**).
  - **Comprar créditos:** um pacote de 3.000 créditos que **não vencem no fim do mês**. Eles só são usados depois que
    acabam os créditos do plano.
  - **Cancelar a assinatura:** o Profissional continua valendo até o fim do mês já pago, e nada mais é cobrado.
  - O plano mostra a próxima cobrança ou até quando ele vale, e o uso mostra os créditos avulsos.
- Enquanto o Mercado Pago está no modo de teste, a tela avisa que nada é cobrado de verdade.

## 0.50.0

A IA gasta menos, e dá para ver onde vai o dinheiro.

- **Cache nas conversas:** a cada mensagem, o assistente recebe de novo as instruções e o retrato da obra. Agora, no
  Claude, essa parte fica guardada por alguns minutos e, nas mensagens seguintes, custa 10% do preço. No Gemini o cache já
  era automático; agora os créditos também descontam. Numa conversa seguida, a pergunta fica bem mais barata.
- **Nível Intermediário (Gemini 3 Flash):** cerca de 19 créditos por pedido, entre o Padrão (Haiku, cerca de 4) e o
  Máximo (Sonnet, cerca de 70). Bom para pedidos médios que não precisam do modelo mais caro. Do plano Profissional.
- **O custo de cada rotina no painel do servidor:** chat, Telegram, classificar fotos, legendas, % pelas fotos, texto do
  relatório, orçamento e análise da obra, com quanto veio do cache.

## 0.47.3

O chat da IA fica mais barato em conversas longas.

- A cada mensagem vão só as **últimas 20 mensagens** da conversa, e não mais a conversa inteira.
- Os anexos vão como arquivo só na mensagem atual e nas **2 últimas mensagens** suas. Os mais antigos viram a nota
  "(anexo: planta.pdf, enviado antes)". Para o assistente voltar a ler um PDF antigo, anexe de novo.
- A ficha do projeto e o retrato do cronograma continuam indo inteiros, então o assistente não perde o que já entendeu
  da obra. O corte vale também para o Telegram.

## 0.47.2

Modelos de IA mais baratos.

- O nível **Padrão** passa a ser o **Claude Haiku 5.5**: cerca de 4 créditos por pedido típico, contra 14 no Gemini 2.5
  Flash. Os 300 créditos do plano Grátis rendem cerca de 80 pedidos. O Telegram também usa o Haiku.
- O nível **Máximo** passa a ser o **Claude Sonnet 5.5**: metade do preço do Opus 5.5, e bom em montar e atualizar
  cronogramas.
- O preço do Haiku sobe em pedidos muito grandes (acima de 100 mil tokens, com PDFs ou muitas fotos), e os créditos
  acompanham.
- No Mac, uma foto HEIC (a do iPhone) anexada ao chat vira JPG, porque o Claude não lê HEIC.
- Os testes do programa deixam de ler o Chaves do macOS de verdade. O macOS pedia a senha das chaves ao executor dos testes
  e às vezes os segurava por minutos.

## 0.47.1

Correção do Telegram: o bot não respondia.

- O segredo do webhook que o Render gera tem "/", "+" e "=", e o Telegram só aceita letras, números, "_" e "-". Por isso
  ele recusava o registro, e as mensagens não chegavam ao servidor. Agora o servidor manda ao Telegram uma versão do
  segredo só com caracteres aceitos, e qualquer valor funciona.
- Se o Telegram recusar o registro, o motivo aparece nos logs do servidor. Só o servidor mudou.

## 0.47.0

Bloco 49: o assistente da obra no Telegram (plano Profissional).

- Conta > **Usar esta obra no Telegram**: o assistente do Telegram passa a enxergar a obra (as tarefas, o %, as datas, as
  medições, os custos e os indicadores). Ela é atualizada a cada vez que você salva.
- Conta > **Conectar o Telegram…**: gera o código e abre o bot; um toque em Iniciar liga a sua conta.
- No Telegram, do canteiro:
  - **Perguntas:** "quanto já medi?", "o que está atrasado?".
  - **Atualizações:** "a alvenaria está em 60%", "gastei R$ 1.200 de cimento na fundação".
  - **Fotos,** com legenda.
  - **Comandos:** /obra troca de obra; /ajuda; /sair.
- As atualizações e as fotos esperam no servidor. Ao abrir a obra, a barra de status avisa, e Conta > **Buscar do
  Telegram** traz tudo:
  - as atualizações viram cartões no chat, para conferir e aplicar, com Desfazer;
  - as fotos vão para a importação em lote, com a IA classificando.

  Nada muda na obra sem você confirmar.
- As perguntas no Telegram gastam os créditos do plano, como no programa.

## 0.46.0

Bloco 48: painel financeiro (no servidor).

- Uma página de administração em `https://projectplanner-servidor.onrender.com/admin`, protegida pela chave de admin, que
  funciona no computador e no celular.
- Ela mostra o custo de IA (em reais, com o IOF, e em dólares), a receita estimada pelos planos, a margem, os pedidos, as
  contas e os tokens. Tem o gráfico do custo por dia e as tabelas por modelo, por plano e das contas que mais gastam, para
  este mês, o mês passado, os últimos 30 dias ou outro período.
- Alerta de custo: quando o custo de IA do mês passa do limite (R$ 100 por padrão), o servidor manda um e-mail ao admin,
  uma vez por mês, e o painel mostra o aviso.
- O câmbio, o IOF, os impostos, o preço do Profissional e o limite do alerta se ajustam no Render, sem versão nova. Até os
  pagamentos de verdade (Bloco 47), a receita é estimada pelo preço do plano.
- O programa não mudou.

## 0.45.2

O nível Avançado passa a usar o Gemini 3.1 Pro.

- O Gemini 2.5 Pro não está mais disponível para chaves de projetos novos do Google (responde "não encontrado"). Com isso, o
  nível Avançado dava "problema de configuração". Agora ele usa o Gemini 3.1 Pro (prévia), testado com a chave do servidor.
- Um pedido típico no Avançado gasta cerca de 76 créditos (era 55 no 2.5 Pro). Só o servidor mudou.

## 0.45.1

Correção do nível Máximo (Claude Opus 5.5).

- O Claude recusava os pedidos ("tool_choice … not supported for this model"): o servidor forçava a ferramenta de resposta,
  e o Opus 5.5, que pensa antes de responder, não aceita isso. Agora a ferramenta vai como opção, com a instrução de usá-la;
  se a resposta vier em texto, o servidor tira o JSON do texto. Só o servidor mudou: o programa 0.45.0 continua valendo.

## 0.45.0

Bloco 46: três níveis de IA e créditos.

- O assistente tem três níveis: **Padrão** (Gemini 2.5 Flash), para o dia a dia; **Avançado** (Gemini 2.5 Pro); e
  **Máximo** (Claude Opus 5.5, da Anthropic), para os pedidos mais difíceis. Escolha nas configurações do assistente ou
  no seletor no topo do painel.
- O uso agora é em **créditos**: cada pedido gasta o custo real dele, e os níveis mais fortes gastam mais. Um pedido típico
  gasta cerca de 14 créditos no Padrão, 55 no Avançado e 140 no Máximo. O saldo do mês aparece no painel, depois de cada
  resposta, e em Minha conta.
- O plano **Grátis** tem 300 créditos por mês, só no nível Padrão. O **Profissional** tem 6.000, em todos os níveis.
- O assistente funciona **só pela conta**: a chave própria do Gemini saiu das configurações, e o macOS deixa de pedir a
  senha das chaves a cada versão. A chave antiga (item "ProjectPlanner — gemini-api-key") pode ser apagada no app Acesso às
  Chaves. Quem compila o código continua podendo usar a variável `GEMINI_API_KEY`.
- No servidor, o modelo de cada nível pode ser trocado por configuração, sem versão nova do programa.

## 0.44.0

Bloco 45: a conta ProjectPlanner no programa.

- Menu **Conta**: entre com e-mail e senha ou com o Google, ou crie a conta (o código de confirmação chega por e-mail).
  Também dá para trocar a senha esquecida, ver o plano e o uso de IA do mês, e sair. O nome da conta aparece na barra de
  status.
- O assistente funciona **sem chave do Gemini**: nas configurações do assistente, escolha "Conta ProjectPlanner". O chat,
  as fotos, o orçamento e a análise dos indicadores passam a usar a conta, com a cota do plano (grátis: 300 mil tokens por
  mês).
- Quem já usa a própria chave continua igual: a chave tem prioridade até você escolher a conta. Dá para trocar a qualquer
  hora.
- A sessão fica no cofre do sistema (Chaves do macOS, Gerenciador de Credenciais do Windows), como a chave, e se renova
  sozinha. Sem uso por 30 dias, é preciso entrar de novo.
- O "Entrar com Google" abre o navegador e volta ao programa; ele passa a funcionar quando o servidor tiver o cliente do
  Google configurado (`docs/SERVIDOR.md`).

## 0.43.1

Preparação para publicar o servidor.

- O servidor aceita a connection string do PostgreSQL também no formato de URL (`postgresql://…`), o que o Neon mostra.
- `render.yaml`: o servidor no Render num passo só, com as chaves do JWT e de admin geradas pelo próprio Render.
- `scripts/testar-servidor.sh`: confere um servidor publicado de ponta a ponta (conta, código por e-mail, plano e um
  pedido de IA).
- README do projeto, com a licença AGPL-3.0.

## 0.43.0

Bloco 44: o servidor de contas e IA (primeira parte da IA por assinatura).

- Novo projeto `src/ProjectPlanner.Servidor`, aberto como o resto do código: as contas do ProjectPlanner (cadastro com
  código por e-mail, login com senha ou com o Google, sessões que se renovam, troca de senha), os planos (grátis com cota
  pequena e Profissional) e o ponto de IA, que chama o Gemini com a chave do servidor e conta os tokens de cada conta.
- A chave do Gemini da empresa fica só na nuvem: o app nunca a recebe. A cota do plano é conferida a cada pedido.
- Segurança: senhas só como hash (PBKDF2), tokens e códigos só como hash, bloqueio depois de 5 senhas erradas, limites de
  pedidos por minuto, respostas que não revelam quem tem conta e nenhum conteúdo de conversa guardado.
- O programa ainda não usa o servidor: o login no app e a opção "Conta ProjectPlanner" no assistente vêm no Bloco 45. A
  chave própria do Gemini continua funcionando como sempre.
- Como publicar o servidor (PostgreSQL, Docker e as variáveis de ambiente): `docs/SERVIDOR.md`.

## 0.42.0

Bloco 42: fotos com IA.

- Fotos do projeto > Importar em lote > Classificar com IA: a IA olha cada foto e escolhe a tarefa (pelo que aparece e pela
  data), escreve a legenda, escolhe o local (da lista da obra) e diz o % que a foto mostra, com o grau de certeza; as
  duvidosas ficam marcadas. A legenda e o local são editáveis e vão para a foto ao importar; com "Subir o % das tarefas
  pelo que as fotos mostram", o % concluído sobe (nunca desce). Tudo num passo do Desfazer.
- Buscar na pasta da obra…: escolha uma vez a pasta onde as fotos chegam (a do celular sincronizada, por exemplo); cada
  busca traz só as fotos novas e já as classifica. A pasta fica guardada no projeto.
- Pelo chat: anexe fotos e peça "coloca essas fotos nas tarefas" (ou "importa as fotos novas da pasta"): abre a importação
  com a IA já classificando, para conferir e importar. "Gera o relatório fotográfico desta semana" abre o relatório no
  período, já com o texto do andamento escrito pela IA.
- A data da foto é conferida com a tarefa escolhida: uma foto bem antes do início previsto (ou depois do término de uma
  tarefa concluída) aparece com o aviso.
- Relatório fotográfico: o primeiro relatório começa na foto mais antiga quando ela é de antes do início do projeto; e,
  quando o período não pega nenhuma foto, a mensagem diz quantas estão fora dele e de quando, com o botão "Incluir todas
  as fotos".

## 0.41.2

Correção: medição num projeto sem Valor nas tarefas.

- A medição mede os itens do contrato, que são as tarefas com Valor. Sem Valor em nenhuma tarefa, "Sugerir pelo %
  concluído" e o chat não mediam nada e a mensagem falava de outra coisa. Agora Projeto > Medições… avisa no topo que
  falta o Valor e como preencher (coluna Valor, aplicar o orçamento ou importar os valores).
- "Sugerir pelo % concluído" diz o motivo certo: falta Valor, falta % concluído, ou tudo já foi medido.
- Medir uma tarefa sem Valor (na tela ou pelo cartão do chat) explica o que fazer.
- O assistente passa a ver quais tarefas têm Valor e quantidade, e não propõe medição sem itens no contrato.

## 0.41.1

Correção do Bloco 41.

- O cartão do chat recusava o % concluído ("o % precisa ir de 0 a 100") quando a IA mandava o número como "100%" ou em
  outro campo: agora o % chega nesses formatos comuns.
- O assistente não diz mais que "atualizou" ao propor: a mensagem pede para conferir e aplicar no cartão.
- Sem nenhuma atualização válida, o botão do cartão diz "Nada para aplicar" (em vez de "Aplicar as 0 válidas").

## 0.41.0

Bloco 41: o chat como central da obra.

- Consultar: o assistente passa a ver a obra inteira, além do cronograma: os indicadores e o semáforo (prazo, custo,
  margem, caixa), as medições, os custos realizados (por categoria e os últimos), os recursos e as superalocações, as fotos
  e os relatórios. Pergunte "quanto já medi?", "onde estou gastando mais?", "quem está superalocado?".
- Abrir telas: "mostra a Curva S", "abre a medição 2", "relatório fotográfico de setembro". O assistente abre a tela na
  aba certa (Indicadores, Orçamento, Medições), com a medição selecionada ou o período do relatório; a mensagem ganha um
  botão para abrir de novo.
- Atualizar a obra por um cartão: o assistente propõe e você confirma. Dá para atualizar o % concluído, medir (na medição
  em rascunho ou numa nova), lançar um custo, adiar o início de uma tarefa ("não iniciar antes de") e salvar uma linha de
  base. Cada item mostra o que muda ("2.2 Sapatas: 70% → 85% concluído"); os que não valem aparecem com o motivo.
  "Aplicar" põe as válidas num passo só do Desfazer (com o Desfazer no próprio cartão); "Descartar" não muda nada.
- O % concluído pedido ao chat agora vem no cartão para confirmar (antes era aplicado direto).

## 0.40.0

Bloco 43: indicadores ampliados (a versão segue a ordem de entrega).

- Projeto > Indicadores… ganhou três abas além da Visão geral (o painel de antes).
- Custos e margem: por etapa, do orçado ao realizado: contratado, custo orçado (do orçamento ou, sem ele, dos
  recursos), margem prevista, orçado do feito, custo real, desvio de custo, custo final e margem projetados (pelo
  desempenho de custo de cada etapa) e quanto falta gastar. A margem compara só as tarefas que têm custo orçado.
- Medições e caixa: o contrato na data (original, aditivos, atual, medido, líquido, retido, saldo); medido × físico por
  etapa, com o executado a medir e o medido à frente; e o fluxo de caixa por período (previsto, medido líquido, custo
  real, saldo e saldo acumulado), com gráfico.
- Alertas e análise: o semáforo da obra (prazo, custo, medição, recursos e fotos) e "Analisar com IA": a IA escreve o
  resumo, os pontos de atenção e as recomendações, que você revisa e edita.
- O PDF e o Word dos indicadores trazem as três páginas novas (e a análise, quando escrita).

## 0.39.0

Bloco 39: a barra de ícones ampliada.

- Botões novos, em grupos: Novo projeto e Abrir projeto; Recursos da tarefa; Ir para a tarefa e Salvar linha de base;
  Relatório fotográfico, Fotos do projeto, Medições, Orçamento e Indicadores; e as opções do Gantt (caminho crítico,
  linha de base e linha de hoje), que ficam marcadas quando ligadas, como os modos de exibição.
- Exibir > Barra de ícones: escolha quais botões aparecem, por grupo (fica guardado nas suas preferências); "Mostrar
  todos os botões" volta ao padrão. Os divisores aparecem só entre grupos à vista.
- Numa janela estreita, os botões que não cabem vão, na ordem, para o "»" no fim da barra, em vez de sumirem.
- As opções do Gantt em Exibir continuam lá, e o menu e o botão mostram sempre o mesmo estado.

## 0.38.0

Bloco 38: a IA no orçamento.

- Projeto > Orçamento…, aba Sugestões da IA: a IA sugere, para você conferir e aceitar, as composições de cada tarefa
  (escolhidas entre as candidatas que o programa acha na base importada pelas palavras do nome da tarefa; os preços
  vêm sempre da base), um preço estimado quando a base não tem o serviço ("Estimativa IA", em materiais e mão de obra,
  sem virar recursos), as quantidades (pela ficha da obra, como área e pavimentos, com a conta na justificativa,
  marcadas com "≈" até você mudar), a revisão (serviços que parecem faltar, unidades, quantidades fora do padrão, o
  BDI) e as equipes (quantos por dia de cada profissional e equipamento, e durações coerentes com as horas das
  composições).
- Escolha o que pedir e, se quiser, um foco ("só as vedações"). A lista mostra o porquê de cada sugestão; os alertas
  são só para ler, e as que não valem (código fora da base, tarefa que virou resumo) aparecem com o motivo. Aplicar
  marcadas é um passo do Desfazer.
- Pelo chat: peça "orce as vedações" ou "revise o orçamento" e o assistente abre o orçamento nessa aba, já pedindo. O
  assistente também passa a ver o resumo do orçamento para responder sobre custos.
- O PDF, o Word e o Excel do orçamento marcam o que a IA estimou. Um arquivo com quantidades estimadas pela IA não
  abre na 0.37 (os outros continuam abrindo).

## 0.37.0

Bloco 33: orçamento com a SINAPI (a versão segue a ordem de entrega).

- Projeto > Orçamento…, aba Bases: importe as planilhas da SINAPI (a de insumos e a de composições analíticas, em .xlsx
  ou .csv) de qualquer estado e mês, desonerada ou não; a UF, o mês e a desoneração vêm do nome do arquivo da Caixa. As
  bases ficam numa biblioteca sua, fora dos projetos (a pasta referencias, ao lado das preferências). A leitura é pelos
  nomes das colunas, então uma base própria no mesmo jeito (ou outra fonte, no futuro) também entra.
- Aba Composições: busque por código ou palavras, escolha a tarefa e "Usar na tarefa"; as composições auxiliares são
  abertas até os insumos (coeficiente, preço e parcial). O projeto guarda a composição que usa, com a base de onde veio.
- Aba Planilha: a planilha orçamentária sintética na ordem da EDT (os grupos somando; tarefas sem composição entram pelo
  Valor informado), com o BDI do projeto sobre o custo direto (os encargos já estão na SINAPI) e, se quiser, um BDI
  diferenciado para os materiais. Exporta em PDF, Word e Excel (a sintética e a analítica).
- Aplicar aos valores e recursos: o total de cada serviço vai para o Valor da tarefa (e a unidade, se estiver vazia), e
  os insumos viram recursos atribuídos a ela (mão de obra e equipamento em horas ÷ 8 por dia; materiais pelo total). É
  um passo do Desfazer e é recusado com o contrato travado por uma medição aprovada.
- Atualizar os preços do orçamento por outra base (outro mês, por exemplo): as composições de mesmo código são trocadas;
  as que não estão na base nova ficam como estavam. Tudo fica no .pplan.

## 0.36.0

Bloco 34: uso de recursos (a versão segue a ordem de entrega).

- Projeto > Uso de recursos…, aba Histograma: o uso de cada recurso por dia útil (pelas datas calculadas), com a linha do
  disponível por dia e os dias acima dele em vermelho; o pico, os dias acima e o total no projeto.
- Aba Superalocações: os trechos de dias em que um recurso passa do disponível, com o pico e as tarefas. Sugerir
  nivelamento adia, um dia útil de cada vez, a tarefa não iniciada que começa por último até tudo caber, e mostra as
  tarefas que mudam e o término novo sem mudar o cronograma; Aplicar põe "não iniciar antes de" nas tarefas adiadas
  (um passo do Desfazer). Tarefas já iniciadas não são adiadas; o que não dá para resolver aparece no aviso.
- Aba Necessidades: o que cada recurso pede por semana, quinzena ou mês (dias de equipe e de equipamento; quantidades de
  material, divididas pelos dias da tarefa), com o custo; filtra por tipo e exporta em CSV (a lista de compras e de
  equipes).

## 0.35.0

Bloco 32: recursos (a versão segue a ordem de entrega).

- Projeto > Recursos…: os profissionais, os materiais e os equipamentos da obra, com o custo (por dia nos profissionais e
  equipamentos; por unidade nos materiais), quantos há por dia (equipes e equipamentos) e o fornecedor; o uso e o custo
  de cada um no projeto. Importa de uma planilha (Nome e Custo, e Tipo, Unidade, Disponível e Fornecedor; os de mesmo
  nome são atualizados) e exporta em CSV.
- O seu catálogo de recursos, fora dos projetos: guardar nele os recursos de uma obra e trazê-los para outra.
- Tarefa > Recursos da tarefa… (ou um duplo clique na coluna Recursos): profissionais e equipamentos por dia (o total é ×
  os dias úteis da tarefa), materiais pelo total; o custo de cada um, o custo orçado da tarefa e a margem sobre o Valor.
- Colunas novas, ocultas por padrão (Exibir > Colunas > Recursos e custos): Recursos, Custo orçado (num resumo, a soma) e
  Margem (Valor − custo orçado, em vermelho quando negativa). O Valor continua sendo o preço do contrato.
- Copiar e colar tarefas leva os recursos (em outro projeto, só os que existem nele). Tudo entra no Desfazer e fica no
  .pplan.

## 0.34.0

Bloco 37: indicadores (a versão segue a ordem de entrega).

- Projeto > Custos realizados…: as notas, as folhas e as locações da obra (data, tarefa ou custo geral, categoria,
  descrição, documento e valor), com os totais por categoria e por mês, e a importação de uma planilha (.xlsx ou .csv)
  com as colunas Data e Valor (e Descrição, EDT, Categoria e Documento). Tudo entra no Desfazer e fica no .pplan.
- Projeto > Indicadores…: numa data de status, o valor agregado — o planejado (VP, pela linha de base em uso ou, sem ela,
  pelo cronograma atual), o agregado (VA, o orçamento de cada tarefa × % concluído) e o custo real (CR) — com o IDP, o
  IDC, a estimativa no término e o término pela tendência; o físico previsto × realizado × o financeiro medido; as
  etapas (orçamento, previsto, realizado, medido, custo real e VA − VP) e as tarefas mais atrasadas. Exporta em PDF e Word.
- Curva S física e financeira (também no cronograma físico-financeiro): prevista, atual, medida (pelas medições
  aprovadas, em % do contrato), custo real (em % do orçamento) e a linha do realizado pelo avanço guardado em cada
  medição aprovada até o ponto de hoje.
- A página de indicadores no relatório fotográfico (opção "Página de indicadores", no fim do período) e no boletim de
  medição (opção "Com indicadores", no fim da medição), em PDF e Word.

## 0.33.0

Bloco 36: medições (a versão segue a ordem de entrega).

- Tarefas com unidade e quantidade (colunas Unidade, Quantidade e Preço unitário, ocultas por padrão; Projeto > Importar
  valores… também lê as colunas Unid. e Quant. da planilha). Com quantidade, o item é medido nela; sem, em % do valor.
- Projeto > Medições…: os boletins de medição nº 01, 02, 03…, com o período sugerido pelo plano (semanal, quinzenal,
  mensal ou datas de corte). Em cada item, o contratado, o anterior, o medido no período, o acumulado e o saldo, em
  quantidade e em R$; o medido pode ser sugerido pelo % concluído das tarefas. Glosas, reajuste pelo índice do período
  (sobre o índice na data-base), retenção (caução) e a devolução dela, e o valor líquido a faturar.
- Aprovar trava o boletim como está (mudar o contrato depois não muda uma medição aprovada) e marca o contrato: a partir
  daí, o valor e a quantidade dos itens só mudam por aditivo. Só a última medição volta a rascunho, se não estiver paga.
  Situação: rascunho, aprovada e paga (com a data).
- Aditivos registrados: número, data, motivo e o acréscimo ou a supressão de cada item (ou um item novo). O boletim
  mostra o valor original, os aditivos e o valor atual do contrato. Uma supressão abaixo do já medido é recusada.
- O plano de medições: o previsto em cada período (físico-financeiro da linha de base ou do cronograma atual) × o medido
  pelas medições aprovadas.
- O boletim em PDF (A4 paisagem, com as assinaturas), Word e Excel (com os números de verdade), e o relatório fotográfico
  do período da medição. Tudo entra no Desfazer e fica no .pplan.
- Exibir > Colunas: as colunas de linha de base e prazos e as de valores e medição ficaram em dois submenus (a lista
  passava da altura da tela).

## 0.32.0

Bloco 35 (a versão segue a ordem de entrega: os Blocos 32 a 34, de recursos, vêm depois das medições).

- Projeto > Cronograma físico-financeiro…: o valor e o % de cada etapa (ou de cada tarefa) por período — semanal,
  quinzenal, mensal ou datas de corte escolhidas — com o total do período e o acumulado, em R$ e em %. O valor de cada
  tarefa é distribuído igualmente pelos dias úteis dela (o de um marco, no dia dele).
- Base: o cronograma atual ou a linha de base em uso. Curva S: prevista (linha de base) × atual, com o ponto do
  realizado hoje (% concluído das tarefas, pesado pelo valor).
- Sem valores, o físico é pesado pela duração, e a tela mostra como preencher: a coluna Valor (R$) da grid ou
  Projeto > Importar valores… (planilha .xlsx ou .csv com a EDT, ou Item, e o valor, ou Total).
- Exporta em PDF (A4 paisagem, a tabela dividida em páginas e a Curva S) e em CSV para o Excel.

## 0.31.0

Bloco 31: linha de base e datas.

- As colunas Início e Término mostram as datas calculadas (as mesmas do Gantt). Digitar um Início cria a restrição
  "não iniciar antes de" (📌 na célula): a tarefa começa nessa data ou depois, se as predecessoras exigirem (aí um aviso
  explica). Digitar um Término muda a duração, mantendo o início; num marco, move o marco.
- Prazo ("não terminar depois de") por tarefa ou resumo: não move nada; se a tarefa passa dele, a folga fica negativa
  (também nas tarefas que a empurram), o Gantt mostra a seta do prazo em vermelho, e ela aparece no Resumo do projeto.
- Projeto > Salvar linha de base…: guarda o cronograma de agora (datas, durações e valores) como LB0, LB1…, com nome.
  Projeto > Linhas de base…: qual comparar, ou nenhuma, e remover. No Gantt, a barra cinza da linha de base fica sob a
  barra atual (Exibir > Linha de base); na grid, as colunas de linha de base e de desvio em dias úteis; no Resumo do
  projeto, o término previsto e o atraso.
- Colunas novas, ocultas por padrão (Exibir > Colunas): Não iniciar antes de, Prazo, Início e Término da linha de base,
  Desvio do início e do término, e Valor (R$), que o cronograma físico-financeiro vai usar.
- Arquivos de versões anteriores abrem com as mesmas datas (sem restrições nem prazos).

## 0.30.0

Bloco 29 (depois do 30, que saiu na 0.29).

- Fotos do projeto > Sugestões da IA: legendas para as fotos sem legenda (a IA olha as imagens) e o % concluído
  conferido pelas fotos recentes das tarefas em andamento, apontando as que não batem com o informado. Tudo vem numa
  lista para revisar, editar e aplicar só o que estiver certo; cada aplicação pode ser desfeita.
- Relatório fotográfico: o texto do andamento do período e os pontos de atenção, escritos à mão ou sugeridos pela IA
  ("Sugerir com IA"), numa página logo depois da capa, no PDF e no Word.
- Antes e depois: na tela Fotos do projeto, o mesmo local (ou a mesma tarefa) em dois dias, lado a lado, com a evolução
  do % e a série das fotos dia a dia; no relatório, a opção "Antes e depois por local" acrescenta uma página por local.
- A IA das fotos usa a mesma chave do assistente; sem chave, as telas avisam onde configurar.

## 0.29.0

Bloco 30 (feito antes do 29, que precisa da chave do Gemini para calibrar; por isso a 0.29 é a do Bloco 30, e o Bloco 29
sai na 0.30).

- Editar > Desfazer e Refazer (⌘Z e ⇧⌘Z; no Windows, Ctrl+Z e Ctrl+Shift+Z ou Ctrl+Y), em até 100 níveis: edições da
  grid, tarefas adicionadas, inseridas e removidas (com as subtarefas e os vínculos), Promover e Rebaixar, mover na grid,
  vínculos criados no Gantt, nome e início do projeto, fotos (adicionar, excluir, girar, editar as informações, entrar
  no relatório e destaque) e as alterações do assistente. O menu e a dica do botão dizem o que vai ser desfeito.
- Desfazer até o estado salvo tira o * do título. A conversa com o assistente, os dados da obra, os locais e o alerta das
  fotos e o histórico dos relatórios não voltam no Desfazer (não são edições do cronograma).
- Uma foto excluída volta com a imagem, mesmo que o projeto tenha sido salvo depois.
- No chat, qualquer resposta aplicada pode ser desfeita, não só a última (as alterações feitas depois dela também são
  desfeitas, perguntando antes); e o Desfazer geral mostra a resposta como desfeita no chat.
- Editar > Copiar, Colar e Duplicar tarefa (⌘C, ⌘V, ⌘D; também no menu de contexto da grid): a tarefa com as subtarefas e
  os vínculos entre elas, sem as fotos, logo abaixo da selecionada. Dá para colar várias vezes e em outro projeto.
- Botões Desfazer e Refazer na barra. Num campo de texto, ⌘Z, ⌘C, ⌘V e ⌘D continuam sendo do texto.

## 0.28.0

- Exibir > Fotos do projeto…: todas as fotos do projeto numa linha do tempo, por dia, com a tarefa de cada foto e
  filtros por período, tarefa, local, tag e texto; dali dá para ampliar, ver no mapa e abrir a galeria da tarefa.
- Importação em lote: escolha (ou solte na janela) fotos de vários dias e cada uma vem com a tarefa prevista na data em
  que foi tirada (a mais curta, se houver mais de uma), numa tabela para conferir e trocar antes de importar. Fotos
  repetidas, já anexadas ou recusadas aparecem com o motivo; um local pode ir para todas de uma vez.
- Locais da obra: uma lista por projeto, sugerida no campo Local da galeria e da importação (junto com os locais já
  usados), para todos escreverem igual; e o filtro por local.
- GPS: a posição gravada pelo celular na foto é guardada com ela, mostrada na galeria e aberta no mapa (OpenStreetMap).
- Alerta de tarefa sem foto: uma tarefa em andamento sem foto há 7 dias ou mais (configurável; 0 desliga) ganha um ⚠
  ao lado do nome na grid, que abre a galeria, e aparece no Resumo do projeto e na tela Fotos do projeto.

## 0.27.0

- Arquivo > Relatório fotográfico…: as fotos do período em PDF ou Word, com capa (logotipo, obra, contratante,
  contrato, endereço, responsável técnico com CREA/CAU), o andamento da obra (% concluído × previsto, atrasadas e
  próximos marcos), as fotos numeradas com legenda automática (data, tarefa, % na data, local e autor), o índice das
  fotos, as assinaturas e o número da página.
- O que entra: período (com "desde o último relatório"), todas as tarefas, só as em execução no período, só as críticas,
  só as atrasadas ou uma etapa, e tags. Como organizar: por tarefa (na ordem da EDT), por data ou por local, com 1, 2,
  4 ou 6 fotos por página. A prévia mostra o número do relatório, as fotos e as páginas, e avisa sobre fotos parecidas
  (mesma tarefa, poucos minutos de diferença).
- Na galeria, cada foto pode sair do relatório ou virar destaque (vem primeiro e ocupa a linha inteira); as miniaturas
  mostram a estrela do destaque e o selo "fora do relatório".
- Os relatórios gerados ficam numerados no projeto (o próximo é o nº seguinte); os dados da obra ficam no projeto e os
  da empresa (logotipo e responsável técnico) valem para todos os projetos.

## 0.26.0

- Assistente em dois modos: ao pedir um cronograma, o assistente pergunta se você prefere montar agora (com premissas
  comuns) ou estudar e conversar antes. A escolha vale para a conversa; em Configurações do assistente dá para deixar
  sempre perguntar (padrão), sempre montar direto ou sempre estudar antes.
- Modo de estudo: o assistente lê os documentos, conta o que entendeu e faz perguntas em rodadas (até 4), com respostas
  para clicar e um campo para escrever outra; depois apresenta o plano (etapas, durações, término estimado e premissas)
  para você aprovar ou ajustar, e só então monta o cronograma completo. "Pular para o plano" encerra as perguntas.
- Ficha do projeto no painel do assistente: o que já foi definido sobre a obra (tipo, área, prazo, premissas), preenchida
  pelo assistente e editável por você; vai junto de cada pedido.
- A conversa com o assistente e a ficha ficam salvas no .pplan: ao reabrir o projeto, o estudo continua de onde parou.
  Os anexos ficam só como referência ao arquivo (não são copiados para dentro do projeto).

## 0.25.0

- Ajuda > Sobre o ProjectPlanner: versão, novidades desta versão e os componentes de terceiros com as licenças.
- Botão de fotos na barra de ícones: abre a galeria da tarefa selecionada e mostra quantas fotos ela tem.
- Atalhos de teclado (⌘ no Mac, Ctrl no Windows): N novo, O abrir, S salvar, ⇧S salvar como, ⇧F fotos da tarefa,
  1/2/3 modos de visualização, J assistente, e F9 recalcular. Aparecem nos menus e nas dicas da barra.
- Barra de status mais completa: à direita, o tamanho do projeto (tarefas, resumos, marcos), o período em dias úteis,
  a tarefa selecionada com as datas do cronograma e a versão.
- Correção: a barra de status mostra o nome do arquivo em vez do caminho completo, que cortava o resto da mensagem
  (o caminho completo aparece ao passar o mouse).
- Linha "Hoje" no Gantt (tracejada, com rótulo no cabeçalho); liga e desliga em Exibir > Linha Hoje.
- Tarefa > Ir para a tarefa… (⌘/Ctrl+G), pelo Nº ou pela EDT, abrindo os resumos recolhidos, e Tarefa > Rolar até a
  tarefa, que traz a selecionada para a vista na grid e no Gantt.
- No controle de zoom do Gantt: Ajustar ao projeto (o projeto inteiro na largura) e Ir para hoje.
- Dica completa ao parar o mouse sobre uma barra do Gantt: EDT e nome, datas, duração, % concluído, folga, se é
  crítica, predecessoras e fotos.
- Arquivo > Exportar para CSV…: a tabela do cronograma (com as datas do cálculo) numa planilha que o Excel em
  português abre direto.
- Erros inesperados não fecham mais o programa sem aviso: ficam num registro local (um arquivo por mês, em
  Ajuda > Abrir a pasta de registros de erros), e os da interface viram um aviso, com o programa aberto para salvar.
- Cópias de segurança: a cada salvamento, a versão anterior do .pplan é guardada (as 5 mais recentes de cada projeto,
  em Arquivo > Abrir a pasta de cópias de segurança); para recuperar, basta abrir uma delas.
- Situação de cada tarefa na data de hoje (não iniciada, em dia, atrasada, concluída), pelo % previsto × informado:
  coluna Situação na grid (antes de Notas; dá para ocultar em Exibir > Colunas), contorno laranja nas barras atrasadas
  e a situação na dica da barra.
- Exibir > Resumo do projeto…: % concluído geral × previsto para hoje, período, contagens (concluídas, em dia,
  atrasadas, críticas em aberto, fotos), a lista das atrasadas e os próximos marcos dos próximos 30 dias.
- Correção: em janelas estreitas, a grid encolhe para o Gantt manter pelo menos 340 px, e o controle de zoom não
  cobre mais o campo do chat.
- Correção: o tema escuro ficou legível (controle de zoom, balões e avisos do assistente, filtros e miniaturas da
  galeria, marcos, resumos e a seleção no Gantt); o tema claro continua igual.
- Exibir > Tema: igual ao sistema (padrão), claro ou escuro, guardado para as próximas vezes.
- Desempenho: selecionar uma tarefa não redesenha mais o Gantt (só o contorno muda de lugar). Com 2.024 tarefas, a
  seleção caiu de cerca de 750 ms para 4 ms, e adicionar uma tarefa, de cerca de 1,0 s para 0,8 s.

## 0.24.0

- O assistente de IA altera o cronograma pelo chat ("monte o cronograma de uma residência de 100 m²"), com
  aplicação automática, Desfazer e correção automática quando uma alteração não vale.

## 0.23.0

- Assistente de IA com o Gemini: chave de API no cofre do sistema, retrato do cronograma e anexos (PDF, imagens, texto).

## 0.22.0

- Controle fotográfico real nas tarefas: imagens no .pplan, galeria com filtros, edição, giro e visualizador.

## 0.21.0

- Inserir tarefa acima ou abaixo da selecionada, com deslocamento automático da ordem.

## 0.20.0

- Pistas vazias no Gantt, acompanhando as linhas vazias da grid.

## 0.19.0

- Painel lateral do assistente de IA (base, ainda sem integração).

## 0.18.0

- Reordenar tarefas arrastando, entre irmãs.

## 0.17.0

- Gerenciar fotos da tarefa pelo menu, pelo menu de contexto e pelo indicador na grid.

## 0.16.0

- Zoom flutuante no Gantt, linhas vazias na grid e modos de visualização Tabela/Gantt/Dividido.

## 0.15.1

- Menu e barra de ícones separados, ícones novos e reorganização dos menus Inserir e Tarefa.

## 0.15.0

- Menu no estilo do Office (Arquivo, Exibir, Inserir, Tarefa) e a versão no título da janela.

## Roteiro dos próximos blocos

Revisto no Bloco 26, a partir do planejamento de fotos e IA (cada bloco ainda passa pela aprovação dos itens antes de
começar):

- Bloco 26 (feito): chat em dois modos, ficha do projeto e conversa salva no .pplan.
- Bloco 27 (feito): relatório fotográfico em PDF e Word e a curadoria das fotos para o relatório.
- Bloco 28 (feito): fotos do projeto inteiro, importação em lote com a tarefa sugerida pela data, locais padronizados,
  GPS e alerta de tarefa sem foto.
- Bloco 29 (feito, na 0.30): IA e fotos (texto do relatório e legendas sugeridas, % concluído sugerido pelas fotos) e antes e depois.
- Bloco 30 (feito, na 0.29): desfazer e refazer em vários níveis (também o do assistente e as fotos), copiar, colar e
  duplicar.
- Bloco 31 (feito, na 0.31): linha de base, prazos, restrição de início e as colunas Início e Término calculadas.
- Bloco 35 (feito, na 0.32): cronograma físico-financeiro, Curva S e importação de valores.
- Bloco 36 (feito, na 0.33): medições (boletins, retenção, reajuste, glosas, aditivos e plano de medições).
- Bloco 37 (feito, na 0.34): custos realizados, valor agregado, painel de indicadores e a página de indicadores nos
  relatórios.
- Bloco 32 (feito, na 0.35): recursos, atribuições, custo orçado e margem, e o catálogo pessoal.
- Bloco 34 (feito, na 0.36): uso de recursos (histograma, superalocações, nivelamento sugerido e necessidades por
  período).
- Bloco 33 (feito, na 0.37): orçamento com a SINAPI (bases, composições, BDI, planilha sintética e analítica, aplicar aos
  valores e recursos).
- Bloco 38 (feito, na 0.38): a IA no orçamento (composições, preços estimados, quantidades, revisão e equipes, também
  pelo chat).
- Bloco 39 (feito, na 0.39): a barra de ícones ampliada e personalizável, com o "»" para janelas estreitas.
- Bloco 43 (feito, na 0.40): indicadores ampliados (custos e margem, medições e caixa, alertas e análise da IA).
- Bloco 41 (feito, na 0.41): o chat como central da obra (consultar, abrir telas e atualizar com cartão de confirmação).
- Bloco 42 (feito, na 0.42): fotos com IA (classificar pelo chat, pela importação em lote e pela pasta da obra) e as
  melhorias do relatório fotográfico.
- A seguir: Bloco 40 (baixar a SINAPI da Caixa) espera um arquivo real para o formato novo.
- Depois: localizar e filtrar; feriados; entregas
  (Gantt em PNG, Excel, importar CSV, modelos de obra); Gantt virtualizado; salvamento automático, rótulos e cores.
