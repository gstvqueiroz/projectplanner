# Avaliação da IA (Bloco 68)

Um conjunto de **44 pedidos reais**, do jeito que um engenheiro escreve no chat ou no Telegram ("paga a OS-001 com a NF 123",
"coloca 2 serventes no reboco", "imprime o Gantt em A3"). Cada pedido tem o que se espera da resposta e uma resposta de
referência, numa obra de exemplo. Serve para saber se a IA continua acertando quando as instruções, o catálogo dos campos ou o
modelo mudam.

| Arquivo | O que tem |
|---|---|
| [`tests/ProjectPlanner.App.Tests/Avaliacao/pedidos.json`](../tests/ProjectPlanner.App.Tests/Avaliacao/pedidos.json) | Os pedidos: `id`, `pedido`, `canal` (vazio = o programa; `telegram`), `esperado` e `referencia` |
| [`ObraDaAvaliacao.cs`](../tests/ProjectPlanner.App.Tests/Avaliacao/ObraDaAvaliacao.cs) | A obra em que os pedidos são feitos (5 tarefas com valor, 2 medições, 1 recurso, 2 OS; "hoje" é 28/09/2026) |
| [`AvaliadorDaIa.cs`](../tests/ProjectPlanner.App.Tests/Avaliacao/AvaliadorDaIa.cs) | Confere uma resposta contra o esperado, e passa as atualizações pelo cartão de verdade |
| [`AvaliacaoDaIaTests.cs`](../tests/ProjectPlanner.App.Tests/Avaliacao/AvaliacaoDaIaTests.cs) | Sem IA (roda sempre, no CI também): cada resposta de referência passa, e o avaliador pega as erradas |
| [`AvaliacaoAoVivoTests.cs`](../tests/ProjectPlanner.App.Tests/Avaliacao/AvaliacaoAoVivoTests.cs) | Com a IA de verdade: só roda quando você pede (abaixo) |

## Como o esperado funciona

O esperado é **parcial**: só os campos que importam. Uma resposta passa quando:

- cada atualização esperada aparece nela com esses campos (`"executante": "~eletro forte"`: um texto começando com `~` quer
  dizer "contém", sem acento nem maiúscula; datas valem como `2026-10-05` ou `05/10/2026`; o Nº da tarefa vale com ou sem `#`);
- cada alteração do cronograma esperada aparece (`"acao": "vincular", "de": "4"`);
- a tela esperada é a aberta (`"abrir": {"tela": "imprimirGantt", "aba": "A3"}`), ou nenhuma (`"abrir": "nenhum"`, como no
  Telegram);
- `"semMudancas": true`: a resposta só responde (nenhuma atualização nem alteração); `"orcamento": true`: pede as sugestões;
- **todas as atualizações passam no cartão de verdade** e as alterações se aplicam na obra da avaliação: uma OS que não existe,
  uma medição já aprovada, uma tarefa errada viram falha.

O que vier a mais (uma atualização que ninguém pediu) é **aviso**, não falha.

## Rodar com a IA de verdade

Cada rodada manda os 44 pedidos (cerca de 8 mil tokens cada) e custa alguns centavos no Gemini 2.5 Flash. Rode no Terminal, na
pasta do projeto. A chave e o token **não** devem ir para o chat.

**Pelo Gemini, com a sua chave** (a mesma variável que o programa usa):

```bash
export GEMINI_API_KEY="cole-aqui-a-sua-chave"
PROJECTPLANNER_AVALIACAO=gemini dotnet test tests/ProjectPlanner.App.Tests --filter "Categoria=AvaliacaoAoVivo" --logger "console;verbosity=detailed"
```

**Pela conta ProjectPlanner** (o servidor no Render, com o Claude ou o Gemini, gastando os créditos da conta). Usa a sessão do
programa, guardada nas Chaves do macOS: feche o programa antes (a sessão é renovada e o programa aberto perderia a dele) e
clique em **Permitir** quando o macOS perguntar.

```bash
PROJECTPLANNER_AVALIACAO=conta PROJECTPLANNER_AVALIACAO_MODELO=claude-haiku-5-5 dotnet test tests/ProjectPlanner.App.Tests --filter "Categoria=AvaliacaoAoVivo" --logger "console;verbosity=detailed"
```

Outras variáveis:

| Variável | Para quê |
|---|---|
| `PROJECTPLANNER_AVALIACAO_MODELO` | O modelo (padrão `gemini-2.5-flash`). Pela conta: `claude-haiku-5-5`, `gemini-3-flash-preview`, `gemini-3.1-pro-preview`, `claude-sonnet-5-5` |
| `PROJECTPLANNER_AVALIACAO_CASOS` | Só alguns pedidos, pelos ids separados por vírgula (`os-pagar,tela-gantt-a3`) |
| `PROJECTPLANNER_AVALIACAO_SAIDA` | A pasta do relatório (padrão: a pasta temporária, em `projectplanner-avaliacao`) |
| `PROJECTPLANNER_SERVIDOR` | Outro servidor (o mesmo que o programa aceita) |
| `PROJECTPLANNER_ACESSO` | Pela conta, um token de acesso no lugar da sessão do programa |

O teste mostra `ok` ou `FALHA` em cada pedido e termina com "N de 44 pedidos passaram. Relatório: …". O relatório (Markdown)
tem a tabela dos pedidos, o que falhou e cada resposta da IA (a mensagem, as atualizações, as alterações e a tela). O teste
falha se algum pedido falhar.

## Quando mexer

- **Um tipo de atualização novo:** entra no `CatalogoDaIa` (ProjectPlanner.IA) e precisa de pelo menos um pedido aqui; um
  teste confere que todo tipo do catálogo tem pedido.
- **As instruções mudaram:** rode a avaliação nos modelos que o servidor usa e compare com o relatório anterior.
- **Um pedido real que a IA errou:** acrescente-o ao `pedidos.json` com a resposta certa como referência; ele passa a ser
  conferido sempre.
