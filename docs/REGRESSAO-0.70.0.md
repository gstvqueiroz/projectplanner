# Regressão da 0.70.0 (Bloco 70)

Feita em 25/10/2026, depois dos Blocos 68 (testes automáticos e avaliação da IA), 69 (fila do Telegram no banco, versões e
erros para o suporte) e 70 (as telas maiores divididas e os pacotes do Windows). Duas partes: o que foi conferido aqui, sem as
suas contas, e o roteiro do que depende de você (Telegram, Mercado Pago, a IA de verdade, Windows, Google).

## O que foi conferido aqui

| O quê | Como | Resultado |
|---|---|---|
| Os testes automáticos | `dotnet test ProjectPlanner.slnx` | ✓ 2.350 passam: Core 771, App 1.038, IA 200, Servidor 135, Data 127, Relatórios 59, Imagens 20 (em ~25 s) |
| Os testes num sistema em inglês e no fuso UTC, como no GitHub | `LANG=en_US.UTF-8 TZ=UTC dotnet test` | ✓ todos passam (antes do Bloco 68 um falhava: a data saía "9/29/2026") |
| Compilar com os avisos virando erro, como no GitHub | `dotnet build -warnaserror` | ✓ 0 avisos |
| Testes repetidos (instáveis) | o servidor 3 vezes seguidas, o App 2 vezes | ✓ (o do servidor que falhava às vezes, no Bloco 69, foi corrigido: o SQLite dos testes sem pool) |
| O vocabulário da IA (catálogo, esquema do Gemini, leitura, instruções) | testes `CatalogoDaIaTests` e `CatalogoNoProgramaTests` | ✓ todo tipo, campo, ação, tela e aba conferidos |
| A avaliação da IA, sem IA | os 40 pedidos com a resposta de referência, no cartão de verdade | ✓ 40 de 40; o avaliador reprova as respostas erradas dos testes |
| A avaliação da IA, o caminho de erro | o teste ao vivo com uma chave inválida, 1 pedido | ✓ "A chave do Gemini não é válida" no relatório, sem travar |
| A divisão das telas grandes (Bloco 70) | as linhas de código de antes e de depois, comparadas | ✓ as mesmas linhas; só mudaram de arquivo |
| O app do Mac publicado (`publicacao/ProjectPlanner.app`) | PlistBuddy e `codesign --verify --deep --strict` | ✓ 0.70.0 no `Info.plist`, assinatura válida |
| O app do Mac abrindo | o binário publicado com uma pasta de usuário isolada, 15 s | ✓ abriu e ficou aberto (cerca de 200 MB de memória), sem registro de erro; ao abrir, pergunta a versão ao servidor (o de agora, 0.67.0, ainda não tem a rota: sem aviso, como deve) |
| Os pacotes do Windows | a versão gravada no `.exe` (x64 e ARM64) e no instalador | ✓ 0.70.0 nos três; os `.zip` com o `.exe` portátil |
| O servidor publicado | a raiz do Render | ✓ 0.67.0 no ar; a 0.68–0.70 sobe quando você fizer o push (e os testes do GitHub passarem) |

Não deu para conferir aqui: os testes no GitHub (precisam do push), o servidor novo (idem), o programa no Windows (não há
Windows nesta máquina) e tudo o que usa as suas contas.

## O roteiro que depende de você

Na ordem. Cada item diz o que fazer e o que deve acontecer; se algo sair diferente, mande a captura de tela.

1. **O push e os testes no GitHub.** Faça o push. Em github.com/gstvqueiroz/projectplanner > **Actions**, a rodada "Testes"
   deve terminar verde em uns 5 minutos. Se ficar vermelha, abra-a e mande o trecho do erro (os resultados ficam no artefato
   "resultados-dos-testes").
2. **O Render só depois dos testes.** No painel do Render, o serviço > Settings > Build & Deploy: o Auto-Deploy deve estar em
   **After CI Checks Pass** (vem do `render.yaml`; se o Blueprint não sincronizar sozinho, escolha à mão). Com os testes
   verdes, o deploy começa sozinho; depois, a raiz do servidor mostra `"versao":"0.70.0"`.
3. **As versões no Render** (Environment): `App__UltimaVersao` = `0.70.0` e, se quiser o botão de baixar, `App__Download` =
   o endereço onde você deixa os pacotes. Para ver o aviso funcionando, ponha `0.71.0` por um instante e abra o programa:
   aparece "Versão nova: 0.71.0" na barra de status. Deixe `App__VersaoMinima` vazia por enquanto.
4. **A IA de verdade.** Rode a avaliação (docs/AVALIACAO-DA-IA.md) pelo Gemini com a sua chave e, depois de recarregar a
   Anthropic, pela conta com o `claude-haiku-5-5`. Mande os dois relatórios: os pedidos que falharem viram correções.
5. **O Telegram com botões.** No programa, emita uma OS e mande ao grupo da obra. No celular, toque em **Aceitar** no cartão:
   o cartão muda para "Aceita" e o programa recebe o passo na caixa da obra.
6. **O Telegram sem perder mensagens.** Logo depois de um deploy (enquanto o Render reinicia), mande uma mensagem ao bot: ela
   deve ser respondida quando o servidor voltar.
7. **Pedidos novos pelo Telegram:** "paga a OS-001 com o Pix", "coloca 2 serventes no reboco", "o contratante é…": cada um
   vai para a caixa e aparece no cartão quando você abre a obra no programa.
8. **Os erros para o suporte.** Em Conta > Minha conta, ligue "Mandar os erros do programa ao suporte". Quando houver um erro
   inesperado, ele aparece no painel /admin em "Erros do programa".
9. **O Mercado Pago (modo de teste).** Conta > Minha conta > Assinar, pague com o comprador de teste: o plano vira
   Profissional e o painel mostra o pagamento.
10. **O Windows.** Num PC (ou máquina virtual) com Windows, instale `ProjectPlanner-0.70.0-win-x64-instalador.exe` (ou o ARM64
    num Windows ARM), abra um `.pplan` com duplo clique e confira "0.70.0" no título.
11. **O que continua pendente nas contas:** recarregar a Anthropic, `Admin__Email` no Render, BotFather `/setprivacy` →
    Disable, `Planos__ProfissionalCreditosPorMes` de volta a 6000, o cliente OAuth do Google ("Entrar com Google"), e, antes de
    vender, o Render pago, o CNPJ, os limites de gasto e os certificados de assinatura (Apple Developer ID e o do Windows).
