# Novidades do ProjectPlanner

A versão segue MAJOR.MINOR.PATCH, com o MINOR igual ao número do bloco de melhoria (ver PUBLISHING.md). A seção da
versão atual aparece também em Ajuda > Sobre o ProjectPlanner.

## 0.82.1

- **Telegram:** as instruções da IA no Telegram agora citam o quadro Kanban e o diário de obra (RDO) entre o que dá para anotar
  de lá; antes ela podia responder que isso só se fazia no programa. Vale depois da atualização do servidor no Render.

## 0.82.0

**O Diário de Obra pela IA, no programa e no Telegram.** Conte o dia para o assistente ("RDO de hoje: choveu à tarde e
paramos às 14h; vieram 4 pedreiros e 3 serventes da Construtora Silva; o reboco chegou a 60%") e ele propõe, num cartão para
você aplicar (um passo do Desfazer):

- **Clima** de manhã, de tarde ou do dia todo (bom, nublado ou chuva) e se foi praticável.
- **Efetivo** (a função, quantos e a empresa) e **equipamentos**; quantidade 0 tira a linha. O nome vale sem diferença de
  maiúsculas, acentos e plural ("serventes" é a linha "Servente").
- **Serviços executados** (a tarefa do cronograma e/ou o que foi feito, com o % no fim do dia) e **ocorrências** (visitas,
  entregas, acidentes, paralisações). O % do diário não muda o cronograma: quando você conta o andamento de uma tarefa, a IA
  propõe também o % concluído.
- **O dia que ainda não está no diário é criado**, com o clima do calendário e as listas só com o que você contou (o "Preencher
  pelo cronograma" continua na tela). Um dia fechado (assinado) não muda, e fechar o dia continua sendo com você, no programa.
- "**Abre o RDO de ontem**": o assistente abre o diário no dia (um dia que não existe fica escolhido para o Novo dia).
- Os últimos 10 dias do diário vão no retrato que a IA recebe ("quantos pedreiros tivemos ontem?").
- **No Telegram**, a mesma coisa. O RDO sem data é o do dia em que a mensagem chegou, mesmo que você só busque as mensagens no
  programa dias depois. Correção: o "hoje" que a IA do Telegram usava era o do último retrato mandado pelo programa (o dia
  em que a obra foi salva); agora é o dia da mensagem, no horário de Brasília. O servidor só passa a conhecer o tipo novo
  depois da atualização dele no Render.
- A avaliação da IA ganhou 3 pedidos do diário (50 no total), um deles pelo Telegram.

## 0.81.0

**Diário de Obra (RDO).** Projeto > **Diário de obra…** (também pelo botão da barra e pela aba **Diário** no rodapé) guarda o
relatório de cada dia da obra:

- **Um dia novo já vem preenchido pelo cronograma:** os serviços previstos para o dia (as tarefas em andamento, com o % atual),
  o efetivo e os equipamentos delas (pelos recursos atribuídos, somados por função, com a empresa do fornecedor) e o clima. Se o
  calendário não tem trabalho no dia, ele já vem impraticável, com o motivo nas ocorrências. Você só ajusta o que mudou.
- **O dia:** o clima de manhã e de tarde (bom, nublado ou chuva, e se foi praticável), o efetivo, os equipamentos, os serviços
  executados (com o % ao fim do dia) e as ocorrências (visitas, entregas, acidentes, paralisações...). "Preencher pelo
  cronograma" repõe os serviços, o efetivo e os equipamentos.
- **Numeração:** RDO 001, 002... pela ordem das datas. **Fechar o dia** o assina (com o responsável dos relatórios): ele não
  muda mais, a não ser que seja reaberto, e os números dos dias fechados nunca mudam (não dá para criar um dia antes do último
  fechado).
- **Levar o % ao cronograma:** o % dos serviços que são tarefas vai para o % concluído delas.
- **Dia sem trabalho:** com manhã e tarde impraticáveis, um botão marca o dia sem trabalho no calendário ("Chuva (RDO nº 2)"),
  e as tarefas andam um dia útil.
- **PDF para imprimir e assinar:** o RDO do dia (com até 8 fotos tiradas no dia, com a hora e a tarefa) ou todos os dias do mês
  num PDF só, com o cabeçalho e o logotipo dos relatórios, o dia da obra ("Dia 12 da obra") e o término previsto, e as
  assinaturas do responsável técnico e da fiscalização. Um dia ainda aberto sai marcado como rascunho.
- O que está no formulário é salvo ao trocar de dia e ao fechar a tela; cada mudança é um passo do Desfazer. O diário fica no
  arquivo do projeto (.pplan).

## 0.80.1

- **Servidor do Telegram:** a fila de mensagens agora é atendida uma de cada vez. Duas rotinas atendendo ao mesmo tempo podiam
  pegar a mesma mensagem e responder duas vezes (apareceu num teste automático que falhava de vez em quando).

## 0.80.0

**O Kanban pela IA, no programa e no Telegram.** O assistente agora mexe no quadro com o tipo de atualização "quadro", que
aparece no cartão para você aplicar (um passo do Desfazer), como as outras atualizações:

- **Mover o card de coluna** ("põe o reboco em Impedida", "a fundação está concluída"), com a regra do %: Concluída põe 100%,
  A fazer põe 0%, sair de Concluída volta ao % de antes, Impedida não muda. Um card arquivado volta ao quadro na coluna pedida.
- **Checklist:** itens novos ("lixar; selador; duas demãos") e marcar um item como feito.
- **Etiquetas:** pôr (a que não existe é criada) e tirar.
- **Arquivar e desarquivar** o card (a tarefa continua no cronograma).
- **Coluna nova** (antes de Concluída) e **raias** por resumo, por responsável ou sem raias.
- A IA reconhece as colunas e as etiquetas pelo nome, sem diferença de maiúsculas e acentos; um nome que não existe vem
  explicado no cartão. O quadro (com as colunas, os cards, os checklists e as etiquetas) vai no retrato que a IA recebe.
- **No Telegram**, a mesma coisa ("arquiva o card da fundação"): fica anotado e aparece no programa para você aplicar. O
  servidor só passa a conhecer o tipo novo depois da atualização dele no Render.
- A avaliação da IA ganhou 3 pedidos do quadro (47 no total), um deles pelo Telegram.

## 0.79.0

**Abas de atalho no rodapé**, à esquerda, como as planilhas do Excel.

- **Gantt** e **Kanban** trocam a visão na própria janela, e a aba da visão atual fica marcada (clara, em negrito e
  sublinhada). A aba Gantt volta para a visão do cronograma que estava antes do Kanban: a tabela e o Gantt lado a lado, ou só
  um deles.
- **Orçamento, Recursos, Medições, Ordens de serviço, Custos, Físico-financeiro, Indicadores e Fotos** abrem as telas, com os
  mesmos comandos do menu.
- Exibir > **Abas no rodapé** mostra ou esconde a faixa; a escolha fica salva para as próximas vezes.

## 0.78.0

**Raias no Kanban e a IA no quadro** (o plano "Delete, arrastar e Kanban", K6 e os ganchos de IA do K7).

- **Raias:** o seletor no alto do quadro agrupa os cards por resumo ou por responsável. As colunas ficam em cima e cada raia é
  uma faixa com uma célula por coluna. O modo fica salvo no projeto.
  - **Por resumo:** uma raia para cada resumo que tem tarefas (o resumo direto, o mesmo da etiqueta do card) e "Sem resumo"
    para as do primeiro nível. Arrastar um card para outra raia muda o resumo da tarefa: ela vai para o fim daquele grupo no
    cronograma, e um resumo que fica vazio vira tarefa comum, com as datas que tinha.
  - **Por responsável:** uma raia para cada profissional do projeto (vale o primeiro profissional da tarefa) e "Sem
    responsável". Arrastar para outra raia põe o profissional dela no lugar do primeiro, com a mesma quantidade; em "Sem
    responsável", os profissionais saem da tarefa (os materiais e equipamentos ficam).
  - Arrastar para outra coluna da mesma raia é como sem raias (com a regra do %); para a célula de outra raia e de outra
    coluna, muda as duas coisas num passo só. A dica mostra a raia e a coluna; o aviso traz o Desfazer.
- **Sugerir itens (IA)**, no checklist do painel do card: a IA escreve de 3 a 8 itens de execução e conferência a partir da
  tarefa (o nome, o resumo, a descrição e a duração), sem repetir os que já estão. Eles entram desmarcados, num passo do
  Desfazer, para você revisar.
- **Resumo da semana (IA)**, no alto do quadro: o assistente escreve o resumo do quadro (o que está concluído, atrasado,
  impedido e o que vence nesta semana). O quadro agora vai no retrato que o assistente recebe a cada mensagem: as colunas com
  as tarefas, os arquivados, os checklists, as etiquetas e as raias.

## 0.77.0

**Os cards do Kanban** (o plano "Delete, arrastar e Kanban", K4 e K5).

- **Painel do card:** duplo clique num card abre, ao lado do quadro, todos os campos da tarefa (nome, início, término, duração,
  % concluído, predecessoras e responsáveis), a descrição, as etiquetas e o checklist. São os mesmos campos da grade, com as
  mesmas validações e o mesmo Desfazer: o que muda ali muda no cronograma (o % move o card).
- **Checklist:** itens com marcar, mudar o texto (sair do campo grava) e tirar; Enter adiciona. O card mostra "2/5". Marcar itens
  não muda o % concluído.
- **Etiquetas do projeto:** criadas pelo painel (já marcadas no card) ou pelo botão Etiquetas, no alto do quadro, que também
  renomeia, muda a cor e exclui (excluir tira de todos os cards). Cada etiqueta nova ganha uma cor ainda sem uso.
- **+ Adicionar card** no pé de cada coluna cria a tarefa no fim do cronograma (com 1 dia) e põe o card no fim da coluna, com
  a regra do % dela (numa Concluída, a tarefa nasce com 100%).
- **Arquivar** (no painel ou no botão direito do card) esconde o card do quadro; a tarefa continua no cronograma. O botão
  Arquivados, no alto do quadro, abre o card ou o traz de volta (no fim da coluna dele).
- **Menu do card** (botão direito): Abrir card, Mover para (as colunas, com a regra do %), Arquivar e Excluir tarefa; com vários
  selecionados, vale para todos.
- **Filtros:** busca por texto no nome e na descrição (sem diferença de maiúsculas e acentos), responsável, etiqueta, resumo
  (as tarefas dentro dele, em qualquer nível), atrasadas e "vencem nesta semana" (término de hoje até domingo). Com filtro, a
  contagem da coluna fica "2 de 5", e soltar um card entre os visíveis cai no lugar certo entre todos; o limite de cards conta
  todos. Limpar filtros mostra tudo de novo.
- O Delete não exclui tarefas com o foco no painel do card ou nos filtros (E1).

## 0.76.0

**As colunas do Kanban** (o plano "Delete, arrastar e Kanban", K2): cada coluna tem o menu ⋯ (também no botão direito do
cabeçalho), e o quadro termina com "+ Adicionar coluna".

- **Adicionar coluna:** a nova entra antes de Concluída, do tipo Em andamento (como uma "Em revisão"), com uma cor ainda sem uso.
- **Renomear, Cor e Mover para a esquerda/direita:** os cards vão junto com a coluna. Nomes repetidos são recusados.
- **Tipo** (A fazer, Em andamento ou Concluída): só muda se os cards da coluna já combinam com o tipo novo, e nenhum % muda por
  isso.
- **Limite de cards (WIP)**, opcional: passou do limite, a coluna fica destacada e a contagem ("4/2") em vermelho. Vazio tira o
  limite.
- **Excluir coluna:** com cards, pergunta para onde eles vão, e lá vale a regra do % (para Concluída vão a 100%, para A fazer
  voltam a 0); as tarefas continuam no cronograma. O aviso traz o Desfazer. A última coluna de um tipo não sai, porque é para
  onde vão as tarefas com aquele %.
- Cada mudança é um passo do Desfazer; mexer só nas colunas não recalcula o cronograma.

## 0.75.0

**Kanban** (o plano "Delete, arrastar e Kanban", K1 a K7): um quadro estilo Trello que é uma nova visão das mesmas tarefas, em
Exibir > Kanban (⌘4 no Mac, Ctrl+4 no Windows) ou pelo botão da barra. Editar em qualquer tela atualiza as outras, e o Desfazer
e as regras valem igual.

- **O que vira card:** as tarefas sem subtarefas (as que de fato se executam); os marcos entram com o ◆. O resumo aparece como
  etiqueta no card. Tarefa que vira resumo sai do quadro; resumo que fica sem subtarefas volta com o que o card tinha.
- **Colunas padrão:** A fazer · Em andamento · Impedida · Concluída. Cada coluna tem um tipo (A fazer, Em andamento ou
  Concluída); "Impedida" é do tipo Em andamento.
- **O card mostra** o nome, o resumo, o início e o término, a barra do %, os responsáveis (os profissionais da tarefa, se o
  projeto usa recursos), as etiquetas e o checklist. Atrasada (término antes de hoje e abaixo de 100%): a data em vermelho.
- **O % segue o card:** soltar numa coluna Concluída põe 100%; tirar de Concluída volta ao % de antes (0% se não houver); soltar
  numa A fazer põe 0%, com o Desfazer no aviso; numa Em andamento o % não muda. Concluir pelo quadro não grava término real.
- **O card segue o %:** mudar o % na grade (ou pelo assistente) leva o card: acima de 0 sai de A fazer, 100 vai para Concluída,
  abaixo de 100 sai de Concluída.
- **Arrastar** cards entre as colunas e dentro delas (a ordem no quadro não muda o cronograma nem recalcula), com o card junto
  do cursor, a linha azul onde ele vai cair, a dica do que acontece com o % e a rolagem automática perto das bordas. Esc ou
  soltar fora do quadro cancela; no tablet, segurando 0,4 s.
- **Seleção:** clicar num card seleciona a tarefa na grade e no Gantt; Ctrl+clique (⌘ no Mac) e Shift+clique selecionam
  vários, que vão juntos no arrasto. O Delete exclui com as mesmas regras da grade, e a seleção passa para o card que ficou no
  lugar.
- **No arquivo:** o quadro fica no mesmo .pplan. Um arquivo de antes abre com as colunas padrão, distribuindo os cards pelo %
  (0% em A fazer, 1 a 99% em Em andamento, 100% em Concluída).

## 0.74.0

**Regras no gráfico de Gantt** (o plano "Delete, arrastar e Kanban", G1 a G5): no Gantt vale o mesmo que na grade.

- **Seleção sincronizada:** a barra selecionada é a mesma tarefa da grade; Ctrl+clique (⌘ no Mac) e Shift+clique selecionam
  várias; clicar numa área vazia limpa a seleção. O Delete exclui com o foco no Gantt, pelas mesmas regras da grade.
- **Arrastar o corpo da barra na vertical reordena**, com as regras da grade (mantém o nível se couber; um resumo leva o grupo
  inteiro; várias selecionadas vão juntas). A linha azul de destino atravessa a grade e o Gantt na mesma altura, com a
  etiqueta do que acontece (ou do motivo do bloqueio).
- **Trava de eixo:** nos primeiros 8 px o programa vê a direção dominante e trava esse eixo até soltar; um gesto meio diagonal
  nunca muda posição e data ao mesmo tempo. Na horizontal o Gantt não muda datas (elas mudam na grade), e a dica diz isso.
- No Gantt não há ajuste lateral de nível (o eixo horizontal é o tempo): para mudar o nível, use a grade ou Alt+Shift+→/←.
- Arrastar pela ponta da barra continua criando vínculos; Esc cancela; Ctrl+Z desfaz.

## 0.73.0

**Arrastar para mover na grade** (o plano "Delete, arrastar e Kanban", regras M1 a M7). Arrastar muda só a organização (a ordem
e a hierarquia), nunca datas nem vínculos.

- **Como pegar:** pela coluna Nº, onde a alça ⋮⋮ aparece ao passar o mouse. O arrasto começa depois de 5 px (um clique continua
  sendo clique); no tablet, segurando 0,4 s.
- **Entre grupos:** a tarefa agora pode ir para outro resumo, não só entre as irmãs. Ela **mantém o nível que tinha, se couber**
  no novo lugar; se não couber, fica no nível válido mais perto. Nunca transforma sozinha a tarefa de cima em resumo.
- **Puxar para o lado durante o arrasto:** para a direita, a tarefa vira subtarefa da linha de cima ("Tornar subtarefa de
  'Escavação'"); para a esquerda, sobe um nível.
- **Na tela:** a linha azul mostra o destino já recuada no nível que a tarefa vai ter, com uma etiqueta junto do cursor (com a
  contagem, quando são várias). Perto das bordas a grade rola sozinha; Esc ou soltar fora da grade cancela.
- **Bloqueados, com o motivo na etiqueta:** soltar um resumo dentro dele mesmo; pôr subtarefas num marco ou numa tarefa com
  vínculos (resumos não têm vínculos).
- **Várias selecionadas** vão juntas, na ordem em que estavam; as subtarefas vão com o resumo.
- **Efeitos:** só reordenar não recalcula nada; mudar de resumo recalcula as datas dos resumos. Tarefa que recebe subtarefa vira
  resumo (com aviso); resumo que perde todas as subtarefas vira tarefa comum mantendo as datas.
- **Teclado:** Alt+Shift+↑/↓ move a tarefa uma posição entre as irmãs (pulando grupos inteiros, sem sair do grupo), também no
  menu Tarefa; Alt+Shift+→/← continua mudando o nível. Cada arrasto ou atalho é um passo só do Desfazer.

## 0.72.0

**Excluir com Delete e a seleção de várias tarefas** (o plano "Delete, arrastar e Kanban", regras E1 a E8).

- **Seleção de várias tarefas:** Ctrl+clique (⌘ no Mac) e Shift+clique, na grade e nas barras do Gantt; a seleção é a mesma nos
  dois. Clicar numa área vazia do Gantt limpa a seleção; o botão direito numa selecionada mantém a seleção.
- **A tecla Delete** (⌫ no Mac) exclui as selecionadas, com o foco na grade ou no Gantt; editando uma célula ou um texto, a
  tecla continua sendo do texto. O menu e a barra agora dizem "Excluir tarefa".
- **Tarefa comum ou marco sai na hora**, com o aviso "Tarefa excluída · Desfazer" por 8 segundos. Só pergunta antes quando a
  exclusão leva outras tarefas junto (um resumo), apaga progresso registrado ou fotos, ou passa de 10 tarefas de uma vez.
- **Resumo:** a janela pergunta "Excluir 'Fundação' e suas 7 subtarefas?", com **Excluir grupo inteiro** (como no MS Project)
  ou **Excluir só o resumo** (as subtarefas ficam no mesmo lugar, um nível acima).
- **Vínculos:** numa sequência simples (A → B → C, Término-Início), excluir B religa A → C com a defasagem que havia entre B e C;
  nos demais casos o aviso lista as tarefas que perderam a predecessora e podem mudar de data.
- **Resumo que fica vazio** vira tarefa comum e mantém o início e o término que tinha.
- **Depois de excluir**, a seleção passa para a tarefa que ocupou o lugar (ou a de cima, se era a última), para dar para
  apertar Delete em sequência. Ctrl+Z volta tudo de uma vez.

## 0.71.0

**Calendário de trabalho.** Até aqui o cronograma contava sempre de segunda a sexta, sem feriados. Agora cada projeto tem o
seu calendário (Projeto > Calendário de trabalho…, ou o ícone de calendário na barra), e todo o cálculo conta os dias úteis
por ele: o caminho crítico, as durações, as defasagens, os prazos, o físico-financeiro e a Curva S, os recursos e o
nivelamento, os indicadores e o resumo do projeto.

- **Os dias da semana de trabalho:** de segunda a sexta, de segunda a sábado ou qualquer combinação.
- **Os feriados nacionais**, calculados a cada ano (inclusive os móveis: Sexta-feira Santa, Carnaval e Corpus Christi, e o
  Dia da Consciência Negra a partir de 2024), com a opção de considerar ou não o Carnaval e o Corpus Christi.
- **Os feriados do projeto** (estaduais, municipais, da empresa), num dia ou todo ano.
- **Os períodos sem trabalho** (chuva, paralisação, férias coletivas; o botão do recesso de fim de ano põe de 24/12 a 01/01) e
  os **dias extras de trabalho** (um sábado, domingo ou feriado trabalhado).
- **A prévia:** a tela mostra os feriados e as paralisações do período do projeto e o que acontece com o término antes de
  aplicar. Aplicar recalcula na hora, e o Desfazer volta o calendário anterior.
- **O Gantt** (na tela e impresso) sombreia todos os dias sem trabalho, não só os fins de semana; o histograma de recursos mostra
  só os dias de trabalho.
- **Projetos novos** já começam com segunda a sexta e os feriados nacionais; a opção "Usar este calendário nos projetos novos"
  guarda outro como padrão. Os projetos de antes abrem com o calendário de antes (segunda a sexta, sem feriados), com as mesmas
  datas, até você mudar.
- **A IA conhece o calendário:** o retrato da obra traz o calendário e os feriados do período, e a IA pode mudá-lo pelo cartão,
  também pelo Telegram: "choveu hoje, a obra parou", "dia 15 é feriado municipal", "vamos trabalhar aos sábados".

## 0.70.0

**Regressão completa e os pacotes do Windows em dia.**

- **Pacotes do Windows** (instalador e portátil, x64 e ARM) gerados de novo: estavam na 0.55.1.
- **As telas maiores do código divididas em partes** (a janela principal, as ordens de serviço e o painel do assistente),
  sem mudar nada no que o programa faz: fica mais fácil e mais seguro mexer em cada parte.
- **Regressão da 0.70.0** (docs/REGRESSAO-0.70.0.md): o que foi conferido no programa publicado, nos testes e no servidor, e o
  roteiro dos testes que dependem das suas contas (Telegram, Mercado Pago, a IA de verdade, o Google).

## 0.69.0

**Pronto para clientes: a fila do Telegram no banco, o aviso de versão nova e os erros para o suporte.**

- **O Telegram não perde mais mensagens:** o que chega ao bot fica guardado no banco até ser atendido. Antes, uma publicação
  nova do servidor (ou uma reinicialização) no meio perdia as mensagens que esperavam a IA; agora o servidor atende o que ficou
  quando volta. Uma mensagem que o Telegram reenvia não é respondida duas vezes.
- **Aviso de versão nova:** ao abrir, o programa pergunta ao servidor qual é a última versão e, se a sua for mais velha,
  mostra "Versão nova: …" na barra de status, com o botão para baixar. Uma versão que o servidor não aceita mais (por exemplo,
  depois de uma mudança grande na IA) recebe o aviso para atualizar em vez de um erro sem explicação.
- **Erros para o suporte (opcional):** em Conta > Minha conta, "Mandar os erros do programa ao suporte". Ligado, um erro
  inesperado vai para o painel do servidor com o tipo, a mensagem e onde aconteceu, sem nada das obras e com a sua pasta
  trocada por "~". Começa desligado.
- **O painel do servidor** mostra os erros mandados pelo programa, quantas contas usam cada versão e quantas mensagens do
  Telegram estão esperando.

## 0.68.0

**Testes automáticos e avaliação da IA.**

- **Testes no GitHub a cada push:** o GitHub Actions compila (avisos viram erro) e roda os 2.300 testes no macOS. O servidor no
  Render só é publicado depois que eles passam no commit.
- **O programa e o servidor sempre em português do Brasil:** num computador configurado em inglês, algumas mensagens mostravam
  a data no formato americano ("9/29/2026"); agora as datas, os números e os valores seguem o padrão brasileiro em qualquer
  idioma do sistema.
- **O vocabulário da IA num lugar só:** os tipos de atualização e os campos de cada um saem de um catálogo único, de onde vêm o
  formato que o Gemini segue, a leitura da resposta e a lista dos campos nas instruções (o Claude passa a receber essa lista
  também). Testes conferem que todo tipo e toda ação têm como ser aplicados no programa.
- **Instruções corrigidas:** a lista das telas que a IA abre não tinha a impressão do Gantt nem as abas novas (as situações do
  relatório das OS e as folhas Carta e Ofício), e um trecho ainda dizia que os pagamentos das OS eram só pela tela.
- **Avaliação da IA:** 40 pedidos reais ("paga a OS-001 com a NF 123", "coloca 2 serventes no reboco", "imprime o Gantt em A3",
  pedidos pelo Telegram), cada um com o que se espera da resposta, numa obra de exemplo. Sem IA, os testes conferem que o
  programa aplica a resposta certa de cada um; com a IA de verdade (Gemini ou a conta), um comando gera o relatório de quantos
  ela acerta. Ver docs/AVALIACAO-DA-IA.md.

## 0.67.0

**A IA no programa todo.** O assistente (no programa e no Telegram) passa a conduzir o fluxo da obra inteiro: além do
cronograma, do andamento, das medições, dos custos e das OS, agora também:

- **Dados das tarefas:** "o valor da alvenaria é R$ 3.600, 120 m², prazo dia 9".
- **Dados da obra:** contratante, contrato e endereço (a capa dos relatórios).
- **Recursos:** criar ou mudar ("pedreiro a R$ 280 por dia, 4 disponíveis") e pôr nas tarefas ("2 pedreiros na
  alvenaria"; zero tira), na mesma resposta.
- **Medições do contrato:** aprovar e marcar como paga.
- **Ordens de serviço:** pagar as medições abertas (com o documento, lançando nos custos), liberar a retenção e duplicar.
- **Telas já configuradas:** "imprime o Gantt em A1", "relatório das OS vencidas de setembro", "relatório fotográfico da OS 3".
- **Estimativas:** quando falta um número (duração, quantidade, preço, equipe), a IA estima, avisa que é estimativa e propõe.
- As instruções da IA ganharam o mapa de tudo o que ela controla, para agir em vez de só explicar onde clicar.
- O cartão confere os itens em ordem (um item pode depender do anterior, como criar o recurso e depois atribuí-lo).
- Tudo o que muda a obra continua num cartão para conferir e aplicar, e o Desfazer volta tudo.

## 0.66.0

**Relatório fotográfico das ordens de serviço.**

- Na tela da OS, **Relatório fotográfico…** abre o relatório fotográfico já com as fotos dessa OS: as das tarefas dela (uma
  etapa traz as de dentro), da emissão até a conferência (ou até hoje). Também em Arquivo > Relatório fotográfico…, na opção
  de tarefas **"Uma ordem de serviço"**.
- A **capa** traz a OS (número e serviço), o executante, a situação com o prazo e o medido (R$ e %); cada tarefa mostra o %
  medido nela na OS. Sai em PDF ou Word, com o logotipo, o texto do andamento pela IA e as assinaturas, como os outros.
- As fotos do Telegram com "#os14" na legenda já vão para as tarefas da OS, então entram nesse relatório.
- A IA abre o relatório de uma OS: "monta o relatório fotográfico da OS 3".

## 0.65.0

**Relatórios das ordens de serviço aprimorados.**

- **Boletim de medição da OS** (os botões "Boletim PDF…" e "Excel…" em cada medição, na tela da OS): para pagar o
  executante, com cada item (contratado, anterior, nesta medição, acumulado, saldo, % e valores), o resumo (medido nas
  anteriores, nesta, acumulado, saldo, retenção e o **líquido a pagar**), se já foi paga e as assinaturas do executante, da
  engenharia (o responsável técnico) e da aprovação.
- **Relatório das OS:**
  - **gráficos:** por executante (contratado, medido e pago) e o medido e o pago de cada mês;
  - **"Incluir o detalhe de cada OS"**: no fim do PDF, cada OS com os itens (contratado, medido e %), as medições e os
    pagamentos;
  - **Excel**, com as abas Resumo, OS, Itens, Medições e Por mês (números de verdade, para somar e filtrar).
- **O PDF da OS** mostra o medido e o % de cada item quando já há medições, e as medições com os pagamentos.
- **Logotipo e responsável técnico** (os das preferências do relatório fotográfico, os mesmos do boletim do contrato) no PDF
  da OS, no boletim e no relatório das OS.

## 0.64.0

**Ordens de serviço completas: relatório, atalhos e Gantt.**

- **Relatório das ordens de serviço** (Projeto > Relatório das ordens de serviço…, o botão novo da barra e o botão na tela das
  OS): em PDF (A4 deitado), com os números (OS, contratado, medido, pago, a pagar com o retido, por medir), as situações, os
  prazos (no prazo e atraso médio), o que pede atenção (vencidas e para conferir), o resumo por executante e a lista das OS.
  Filtro pelo período de emissão, pelas situações (abertas, encerradas, vencidas) e pelo executante. Também salva a lista em
  CSV e imprime.
- **Atalhos novos na barra de ícones** (grupo Obra): **Nova ordem de serviço** (abre já com a OS da tarefa selecionada) e
  **Relatório das OS**. Também no menu Projeto.
- **Marca "OS" no Gantt:** as barras das tarefas com OS ganham a marca antes do nome (laranja se alguma venceu), com a dica
  das OS, como na grade.
- **A IA mede uma OS** ("a OS 3 está em 60%"): o cartão propõe a medição que leva cada item até o % (com o valor), e o % das
  tarefas acompanha. E abre as telas: "abre as ordens de serviço", "mostra o relatório das OS".
- Correção: o login com Google às vezes falhava com "Address already in use" (a porta de volta do navegador, no Mac); agora
  tenta outra porta e o cancelamento do login aparece certo.

## 0.63.1

Correções nos dois erros do assistente.

- **"Your credit balance is too low"**: a conta da Anthropic do servidor ficou sem crédito. Agora o servidor não mostra mais
  esse erro: os pedidos do Claude (Padrão e Máximo) vão sozinhos para o Gemini do mesmo porte (Gemini 3 Flash e 3.1 Pro),
  cobrando o que o Gemini custou, e voltam ao Claude em até 15 minutos depois que houver crédito. O dono do servidor fica
  sabendo no painel (/admin, erros do servidor) e por e-mail (no máximo a cada 6 horas). Vale também para o Telegram.
- **"A resposta do assistente ficou longa demais e veio cortada"**: o pedido ao Gemini agora tem um teto de saída (32 mil
  tokens, como o do Claude) e, se a resposta vier cortada no meio, o assistente tenta mais uma vez pedindo uma resposta curta.

## 0.63.0

**A IA cria e altera as ordens de serviço** (no chat do programa e no Telegram).

- Peça ao assistente: "abre uma OS da alvenaria e do reboco para a Construtora Silva, prazo 15/10, com 5% de retenção",
  "muda o prazo da OS 3 para dia 20", "emite a OS 3", "cancela a OS 2, o empreiteiro saiu". O pedido vira um cartão; nada
  muda antes de você clicar em Aplicar (e o Desfazer volta tudo).
- **A OS fica sempre ligada às tarefas.** Se o serviço ainda não está no cronograma ("abre uma OS para consertar a calha,
  2 dias, depois do telhado"), a tarefa nova é criada junto, depois da predecessora e no mesmo nível, e a OS fica ligada a
  ela, com as datas do cronograma.
- **Pelo Telegram:** os mesmos pedidos (marcando o bot no grupo, ou no chat privado) vão para a caixa da obra e viram o
  cartão em Conta > Buscar do Telegram. As perguntas sobre as OS já eram respondidas desde a 0.61.0.
- Uma OS emitida não muda os dados: a IA propõe antes voltar a rascunho.

## 0.62.0

**Ordens de serviço: retenção, cópia, fotos e prazos.**

- **Retenção de garantia:** na ficha, "Retenção (%)" (5% é o comum). Cada pagamento lança só o líquido nos custos
  realizados e guarda a retenção; depois da conferência, com tudo medido e pago, **Liberar a retenção** paga o que ficou
  retido (também nos custos, por tarefa) e a OS fica paga. Dá para desfazer. A retenção aparece no PDF e no Telegram.
- **Duplicar:** uma OS nova em rascunho com os mesmos dados, para o serviço que se repete (de pavimento em pavimento).
- **Fotos do Telegram ligadas à OS:** uma foto com "#os14" (ou "OS-14") na legenda vai para a tarefa da OS-014 na
  importação (a IA não troca), e a legenda do Telegram vira a legenda da foto.
- **Prazos:** a lista mostra quantas OS foram concluídas no prazo e o atraso médio, também por executante; o CSV ganhou os
  dias de atraso e o retido.
- Um arquivo com retenção não abre nas versões 0.58 a 0.61.

## 0.61.0

**As ordens de serviço à vista.**

- **Marca "OS" na grade:** as tarefas com ordem de serviço ganham a marca "OS" no fim do nome (azul; laranja se alguma
  passou do prazo). A dica mostra cada OS da tarefa, com a situação, o prazo e o executante; clicar abre as ordens de
  serviço já na OS da tarefa.
- **Aviso ao abrir a obra:** "Ordens de serviço: 1 vencida, 2 esperando a conferência".
- **O assistente enxerga as OS** (no programa e no Telegram): pergunte "quais OS estão vencidas?", "quanto falta pagar à
  Construtora Silva?" ou "o que está esperando conferência?". Ele só consulta; as OS continuam sendo mudadas na tela delas.

## 0.60.0

**Medição e pagamento das ordens de serviço.**

- **Medições na OS:** depois de aceita, registre quanto foi executado de cada item ("Nova medição", com a data; "Tudo o que
  falta" preenche o resto). A medição não passa do contratado. O medido leva o % concluído das tarefas da OS (pesado pelo
  valor dos itens; só aumenta; dá para desligar).
- **Conferir e aprovar** mede o que ainda faltava como a medição final e leva as tarefas a 100%.
- **Pagar…** registra o pagamento de uma medição (com o documento: NF, recibo, PIX) e lança o valor nos **custos
  realizados**, um lançamento por tarefa (os itens avulsos como custo geral), em "serviços de terceiros" para empreiteiro e
  em "mão de obra" para equipe própria. Assim o custo real, o IDC e a Curva S realizada já contam a OS. **Desfazer
  pagamento** tira os custos. Conferida, toda medida e toda paga, a OS fica **paga**.
- **Na lista:** o valor medido a pagar e o que ainda falta medir; **Por executante** mostra o total, o medido e o pago de
  cada um; **Exportar a lista (CSV)…** leva as OS ao Excel.
- O PDF da OS mostra as medições e os pagamentos.
- Um arquivo com medições de OS não abre nas versões 0.58 e 0.59 (elas não conhecem as medições).

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
