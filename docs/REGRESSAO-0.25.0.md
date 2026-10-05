# Regressão da 0.25.0 (Bloco 25, parte D)

Feita em 04/10/2026 sobre os pacotes publicados em `publicacao/` (commit `63cfb4e`). Três frentes:

- **Binário real (`publicacao/ProjectPlanner.app`):** aberto com uma pasta de usuário de teste (`HOME` isolada, sem
  acesso às preferências nem às Chaves do usuário), conferido pela tela. Os menus desenhados dentro da janela não
  aceitam clique em segundo plano; os itens deles foram conferidos pelos atalhos, pelo roteiro abaixo e pelos testes.
- **Roteiro sem tela nos assemblies publicados:** os `ProjectPlanner*.dll` de `publicacao/osx-arm64` (mesmo SHA-256)
  carregados num programa com Avalonia headless e Skia, 37 itens, com retratos.
- **Testes automatizados:** 1.327 (Core 499, IA 103, Imagens 16, Data 64, App 645), incluindo os que clicam com o
  mouse em cada item de menu da janela de verdade.

## Checklist

| Funcionalidade | Como | Resultado |
|---|---|---|
| Versão 0.25.0 no título, na barra de status e no Sobre | binário real, roteiro | ✓ "obra.pplan — ProjectPlanner 0.25.0", "v0.25.0" |
| Abrir por duplo clique (evento do Finder, `open -a`) | binário real | ✓ "Residência Silva.pplan" carregado, nome com acento |
| Abrir pela linha de comando | binário real | ✓ |
| Abrir um `.pplan` da 0.24.0 | binário real, roteiro, testes | ✓ 13 tarefas, 10 vínculos, 3 fotos, mesmas datas do cálculo da 0.24 |
| Salvar, Salvar como, alterações pendentes ao trocar de projeto | binário real, roteiro | ✓ (`*` no título, pergunta antes de Novo) |
| Cópias de segurança ao salvar | binário real, roteiro | ✓ cópia idêntica ao arquivo anterior, na pasta do projeto |
| Exportar para CSV | roteiro | ✓ `;`, UTF-8 com BOM, nota com quebra de linha entre aspas |
| Ícones da barra (Salvar, Adicionar, Remover, Promover, Rebaixar, Recalcular, Fotos, modos, Assistente) | binário real | ✓ (Rebaixar desabilitado abaixo de um marco, como deve) |
| Menus (Arquivo, Exibir, Tarefa, Ajuda) | testes (clique de mouse em cada item), roteiro | ✓ |
| Atalhos ⌘1 / ⌘J / ⌘G | binário real | ✓ |
| Toggles de Exibir (Seleção, Caminho crítico, Fotos, Folga, Linha Hoje) | roteiro, testes | ✓ |
| Modos Dividido / Somente Tabela / Somente Gantt | binário real, roteiro | ✓ |
| Colunas: mostrar, ocultar, restaurar; coluna Situação | roteiro, testes | ✓ |
| Edição na grid: nome, duração, %, predecessoras na notação (II+1) | roteiro | ✓ |
| Predecessora que cria ciclo | roteiro | ✓ recusada, texto fica para corrigir, cronograma não muda |
| Inserção acima/abaixo, subtarefa | roteiro | ✓ |
| Resumos (recolher/expandir, sem vínculos) e marcos | roteiro | ✓ subtarefa numa tarefa com vínculos é recusada com aviso |
| Promover e Rebaixar | roteiro | ✓ |
| Reordenação arrastando (entre irmãs) | roteiro | ✓ |
| Remover tarefa | binário real, roteiro | ✓ |
| Drag-to-link no Gantt | roteiro | ✓ vínculo criado; ciclo recusado |
| Clique na barra seleciona | roteiro | ✓ |
| Dica completa da barra (situação, folga, predecessoras) | binário real, roteiro | ✓ |
| Zoom − / +, Ajustar ao projeto, Ir para hoje | roteiro, testes | ✓ |
| Linha Hoje, fins de semana sombreados, linhas vazias na grid e no Gantt | binário real | ✓ |
| Caminho crítico (barras, setas e fundo das linhas) | binário real, roteiro | ✓ |
| Seleção sem redesenhar o Gantt | roteiro, testes | ✓ as mesmas formas antes e depois |
| Fotos: galeria, adicionar JPG real, editar, girar, excluir, salvar e reabrir | binário real (galeria), roteiro | ✓ |
| Assistente sem chave (aviso, resposta simulada) | binário real | ✓ |
| Assistente alterando o cronograma, Desfazer | roteiro (assistente falso) | ✓ |
| Resumo do projeto | roteiro | ✓ |
| Tema escuro / claro / do sistema | roteiro, retratos | ✓ claro idêntico byte a byte ao de antes |
| Erro inesperado: registro e aviso, programa continua | roteiro | ✓ |
| Janela estreita (1024 × 640) | retratos, testes | ✓ |
| Tela cheia do macOS (botão verde) | — | não testado: tomaria a tela do usuário; a janela abre maximizada ✓ |
| Assistente com a chave real do Gemini | — | não testado: a chave fica nas Chaves do usuário, que não foram tocadas |

## Medições (2.024 e 368 tarefas)

O mesmo programa contra a 0.24.0, a 0.25 sem o D1 e a 0.25.0; sem tela, Release, um processo por medição, o mesmo
aquecimento, GC antes de cada passo, mediana de 3 rodadas.

| Medida (2.024 tarefas) | 0.24.0 | 0.25.0 |
|---|---|---|
| Selecionar uma linha | 753 ms | 3,5 ms (máx. 6) |
| Adicionar tarefa | 1.022 ms | 778 ms |
| Editar uma duração (recálculo e redesenho) | 763 ms | 795 ms |
| Redesenho completo do Gantt | 754 ms | 755 ms |
| Abrir até a tela pronta | 951 ms | 993 ms |
| Abrir o arquivo (SQLite) | 123 ms | 144 ms |
| Recálculo do CPM | 5,4 ms | 5,5 ms |
| Rolagem (por salto de 1/20 da lista) | 8,7 ms | 8,7 ms |
| Memória ao fim | 697 MB | 625 MB |

Com 368 tarefas: selecionar 118 → 1,4 ms; adicionar 153 → 118 ms; o resto igual. Editar, redesenhar e abrir variam
cerca de 5% entre rodadas em todas as versões; o ganho real do Bloco 25 é na seleção. O redesenho completo (~0,75 s
com 2.000 tarefas) continua sendo o custo de editar e é o alvo do Gantt virtualizado (Bloco 32).
