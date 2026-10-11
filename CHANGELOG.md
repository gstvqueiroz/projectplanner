# Novidades do ProjectPlanner

A versão segue MAJOR.MINOR.PATCH, com o MINOR igual ao número do bloco de melhoria (ver PUBLISHING.md). A seção da
versão atual aparece também em Ajuda > Sobre o ProjectPlanner.

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
- Bloco 29 (sai na 0.30): IA e fotos (texto do relatório e legendas sugeridas, % concluído sugerido pelas fotos) e antes e depois.
- Bloco 30 (feito, na 0.29): desfazer e refazer em vários níveis (também o do assistente e as fotos), copiar, colar e
  duplicar.
- Depois: localizar e filtrar; linha de base e prazos; datas de tarefa e restrições (corrige as colunas Início e Término,
  que hoje mostram a data digitada mas o cálculo usa só o início do projeto e as predecessoras); feriados; entregas
  (Gantt em PNG, Excel, importar CSV, modelos de obra); Gantt virtualizado; salvamento automático, rótulos e cores.
