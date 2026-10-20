# Publicação do ProjectPlanner

Como gerar os executáveis e o instalador a partir do Mac.

## O que é gerado

| RID | Arquivo para distribuir | Roda em |
|---|---|---|
| `win-x64` | `publicacao/ProjectPlanner-<versão>-win-x64-instalador.exe`: **instalador**, que também associa os arquivos `.pplan` | Windows 10/11 em PCs Intel/AMD (a maioria) |
| | `publicacao/ProjectPlanner-x64.exe` (ou o `.zip` `ProjectPlanner-<versão>-win-x64.zip`): o mesmo programa, **portátil**, sem instalar | |
| `win-arm64` | `publicacao/ProjectPlanner-<versão>-win-arm64-instalador.exe`, ou o `.zip` `ProjectPlanner-<versão>-win-arm64.zip` com `ProjectPlanner-ARM64.exe` | Windows 11 em ARM (Surface Pro X, Copilot+ PCs, máquinas virtuais Windows ARM no Mac) |
| `osx-arm64` | `publicacao/ProjectPlanner-<versão>-osx-arm64.zip`, que contém `ProjectPlanner.app` | Macs Apple Silicon (M1 ou mais novo), macOS 12+ |

Todos são **self-contained**: levam o runtime do .NET junto e não exigem instalar mais nada na máquina de destino.

- **Windows:** um único `.exe` (cerca de 50 MB), com a arquitetura no nome. As bibliotecas nativas (Skia, HarfBuzz e SQLite) vêm dentro dele e são extraídas automaticamente na primeira execução. O instalador (cerca de 44 MB) leva esse mesmo `.exe`.
  As fotos (Bloco 22) usam essa mesma biblioteca Skia (o SkiaSharp na mesma versão que o Avalonia usa) para ler, reduzir e girar as imagens; a leitura do EXIF é .NET puro (MetadataExtractor). Não há biblioteca nativa nova.
- **macOS:** o `.app` é uma pasta com o executável e as dependências (cerca de 120 MB; o `.zip` tem 46 MB).

Os arquivos de projeto do usuário usam a extensão **`.pplan`** (um banco SQLite por dentro).

## Pré-requisitos

- .NET SDK 10 (`dotnet --version` deve mostrar 10.x; o `global.json` exige 10.0.100 ou mais novo).
- **NSIS**, para o instalador do Windows: `brew install makensis` (versão 3.12 ou mais nova). Sem ele, o script gera tudo menos os instaladores e avisa.
- macOS, para o `osx-arm64`: o script usa `codesign`, `ditto` e `zip`, que já vêm com o sistema.
  Os `.exe` do Windows também podem ser gerados no Linux ou no Windows, com `dotnet publish` direto (ver abaixo).
- Internet na primeira vez: o SDK baixa do NuGet os runtimes de cada plataforma.

## Gerar uma nova versão

1. Suba a versão em [`Directory.Build.props`](Directory.Build.props), o único lugar em que ela está:

   ```xml
   <Version>0.42.0</Version>
   ```

   O padrão é MAJOR.MINOR.PATCH, com o MINOR igual ao número do bloco de melhoria (Bloco 15 = `0.15.0`, Bloco 16 = `0.16.0`); o PATCH sobe em ajustes e correções dentro do mesmo bloco (Bloco 15.1 = `0.15.1`).

   A versão vai para o título da janela ("obra.pplan — ProjectPlanner 0.42.0"), para os metadados do `.exe` (Propriedades → Detalhes no Windows), para o instalador (nome do arquivo e "Configurações → Aplicativos"), para o `Info.plist` do `.app` e para o nome dos `.zip`.

2. Confira que tudo está passando:

   ```bash
   dotnet test
   ```

3. Gere os pacotes, todos de uma vez ou um de cada vez:

   ```bash
   scripts/publicar.sh
   scripts/publicar.sh win-x64
   scripts/publicar.sh win-arm64
   scripts/publicar.sh osx-arm64
   ```

   **A partir do Bloco 15, toda publicação confirma a versão antes de gerar os executáveis.** O script mostra a versão do `Directory.Build.props` e pergunta "Publicar a versão 0.42.0? [s/N]"; se já houver pacotes dessa versão em `publicacao/`, avisa que serão substituídos (sinal de que a versão não foi subida). Sem terminal para perguntar (em automação), a confirmação vem na linha de comando, e uma versão diferente da do `Directory.Build.props` interrompe tudo antes de compilar:

   ```bash
   scripts/publicar.sh --versao 0.42.0 osx-arm64
   ```

   Depois de gerar, confira a versão no resultado: no nome dos `.zip` e dos instaladores, no título da janela ao abrir o programa e, no Windows, em Propriedades → Detalhes do `.exe`.

   O resultado fica em `publicacao/`, que é ignorada pelo git:

   ```
   publicacao/
     ProjectPlanner-0.42.0-win-x64-instalador.exe    ← mande este para quem usa Windows
     ProjectPlanner-0.42.0-win-arm64-instalador.exe
     ProjectPlanner-x64.exe                          ← ou este, portátil, onde não der para instalar
     ProjectPlanner-0.42.0-win-x64.zip
     ProjectPlanner-0.42.0-win-arm64.zip
     ProjectPlanner-0.42.0-osx-arm64.zip             ← para Mac
     win-x64/ProjectPlanner-x64.exe
     win-arm64/ProjectPlanner-ARM64.exe
     ProjectPlanner.app
   ```

### Sem o script

As configurações de cada RID estão nos perfis de publicação em
[`src/ProjectPlanner.App/Properties/PublishProfiles/`](src/ProjectPlanner.App/Properties/PublishProfiles/):

```bash
dotnet publish src/ProjectPlanner.App -p:PublishProfile=win-x64
dotnet publish src/ProjectPlanner.App -p:PublishProfile=win-arm64
dotnet publish src/ProjectPlanner.App -p:PublishProfile=osx-arm64
```

O perfil `osx-arm64` gera só a pasta `publicacao/osx-arm64/`. Quem a transforma em `ProjectPlanner.app` (com `Info.plist`, ícones e assinatura ad hoc) é o `scripts/publicar.sh`.

Equivalente do perfil `win-x64` em linha de comando:

```bash
dotnet publish src/ProjectPlanner.App -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publicacao/win-x64
```

E o instalador, a partir do `.exe` publicado (o `LC_ALL` é necessário: o `makensis` precisa de um locale UTF-8 para ler os acentos do script):

```bash
LC_ALL=en_US.UTF-8 makensis -DVERSAO=0.42.0 -DARQUITETURA=x64 \
  -DEXE="$PWD/publicacao/win-x64/ProjectPlanner-x64.exe" \
  -DICONE_APP="$PWD/src/ProjectPlanner.App/Assets/ProjectPlanner.ico" \
  -DICONE_ARQUIVO="$PWD/src/ProjectPlanner.App/Windows/ProjetoPplan.ico" \
  -DSAIDA="$PWD/publicacao/ProjectPlanner-0.42.0-win-x64-instalador.exe" \
  src/ProjectPlanner.App/Windows/ProjectPlanner.nsi
```

## Abrir projetos com duplo clique (arquivos `.pplan`)

Para o duplo clique num `.pplan` abrir o projeto, são necessárias duas coisas:

1. **O programa trata o arquivo que recebe.** Isso já está no código e vale para qualquer versão distribuída:
   - **Windows e linha de comando:** o arquivo chega como argumento, por exemplo `ProjectPlanner.exe "C:\Obras\Sede.pplan"`.
   - **macOS:** o Finder entrega o arquivo ao app por um evento, tanto quando o app está fechado quanto quando já está aberto.
   - **Regras:**
     - Vale o primeiro `.pplan` pedido; o resto dos argumentos é ignorado.
     - Um arquivo inexistente ou inválido dá a mesma mensagem do "Abrir…", e o programa fica com o projeto vazio.
     - Com alterações não salvas, o programa pergunta antes de trocar de projeto.
     - Se vários `.pplan` chegarem juntos, abre o primeiro e avisa na barra de status que os outros não foram abertos, porque o ProjectPlanner abre um projeto por janela. No Windows, selecionar vários no Explorer e teclar Enter abre uma janela para cada um.
2. **O sistema sabe que `.pplan` é do ProjectPlanner.** Quem cuida disso é o empacotamento:
   - **Windows, instalador ([`src/ProjectPlanner.App/Windows/ProjectPlanner.nsi`](src/ProjectPlanner.App/Windows/ProjectPlanner.nsi)):**
     - Instala **só para o usuário atual, sem pedir administrador**, em `%LOCALAPPDATA%\Programs\ProjectPlanner`, com atalho no menu Iniciar.
     - Registra em `HKEY_CURRENT_USER\Software\Classes`:
       - `.pplan` apontando para o tipo `ProjectPlanner.Projeto`;
       - o nome "Projeto do ProjectPlanner";
       - o ícone `ProjetoPplan.ico`;
       - o comando `"…\ProjectPlanner.exe" "%1"`.
     - Aparece em **Configurações → Aplicativos**. Desinstalar remove o programa, o atalho e a associação.
   - **Windows, `.exe` portátil:** o sistema não sabe nada sobre `.pplan` até alguém escolher. Clique com o botão direito num `.pplan` → **Abrir com** → **Escolher outro aplicativo** → procure o `ProjectPlanner-x64.exe` → marque **Sempre**. Se o `.exe` mudar de pasta, é preciso escolher de novo.
   - **macOS ([`src/ProjectPlanner.App/macOS/Info.plist`](src/ProjectPlanner.App/macOS/Info.plist)):**
     - O plist declara o tipo `com.gustavodossantosqueiroz.projectplanner.projeto` (extensão `pplan`, ícone `ProjetoPplan.icns`) com o app como dono e editor.
     - O macOS registra isso quando vê o app pela primeira vez, ao abri-lo ou copiá-lo para Aplicativos. A partir daí os `.pplan` ganham o ícone próprio e abrem com duplo clique.

> **Se o `.pplan` já foi associado à mão no Windows** ("Abrir com… → Sempre"), o Windows mantém essa escolha mesmo depois de instalar. Nenhum instalador pode trocar a escolha do usuário. Para passar a usar o programa instalado, clique com o botão direito num `.pplan` → **Abrir com** → **Escolher outro aplicativo** → **ProjectPlanner** → **Sempre**.

## Ícones e metadados

| O quê | Onde |
|---|---|
| Versão, produto, autor, copyright | [`Directory.Build.props`](Directory.Build.props) |
| Nome do executável (`ProjectPlanner`), descrição, ícone do `.exe` | [`src/ProjectPlanner.App/ProjectPlanner.App.csproj`](src/ProjectPlanner.App/ProjectPlanner.App.csproj) |
| Ícone do Windows e da janela | `src/ProjectPlanner.App/Assets/ProjectPlanner.ico` |
| Ícone dos arquivos `.pplan` no Windows e script do instalador | `src/ProjectPlanner.App/Windows/` (`ProjetoPplan.ico`, `ProjectPlanner.nsi`) |
| Ícones do app e dos arquivos `.pplan` no macOS, `Info.plist` | `src/ProjectPlanner.App/macOS/` (`ProjectPlanner.icns`, `ProjetoPplan.icns`, `Info.plist`) |

Os ícones atuais são provisórios. Para trocar, substitua os `.ico` (16 a 256 px) e os `.icns` (16 a 1024 px) mantendo os nomes. Em Macs, um `.icns` sai de uma pasta `.iconset` com `iconutil -c icns`.

O script do instalador precisa continuar em UTF-8 com BOM, a marca no início do arquivo que faz o `makensis` ler os acentos.

No .NET 10, o ícone e os metadados entram no `.exe` mesmo compilando no Mac (conferido na versão 0.7.0).

## Testar

**Abrir um projeto pela linha de comando** (o mesmo caminho do duplo clique no Windows), sem publicar:

```bash
dotnet run --project src/ProjectPlanner.App -- "/caminho/para/Obra.pplan"
```

Ou no `.app` publicado:

```bash
publicacao/ProjectPlanner.app/Contents/MacOS/ProjectPlanner "/caminho/para/Obra.pplan"
```

**No Mac, o duplo clique de verdade:** abra uma vez o `publicacao/ProjectPlanner.app`, para o macOS registrar o tipo `.pplan`, e depois dê duplo clique num `.pplan` no Finder. Pelo terminal, o equivalente é:

```bash
open "/caminho/para/Obra.pplan"
```

Se o Finder ainda mostrar o ícone genérico, force o registro:

```bash
/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f publicacao/ProjectPlanner.app
```

Para conferir o tipo reconhecido:

```bash
mdls -name kMDItemContentType "/caminho/para/Obra.pplan"
```

O resultado esperado é `com.gustavodossantosqueiroz.projectplanner.projeto`.

**No Windows, estando num Mac:** nem o `.exe` nem o instalador rodam no macOS. As opções são:

- **Máquina virtual:** [UTM](https://mac.getutm.app) (gratuito) ou Parallels, com Windows 11 ARM. O Windows ARM roda o instalador e o programa ARM64 nativamente, e os x64 por emulação.
- **Um PC com Windows.**

Roteiro para validar o instalador no Windows:

1. Execute `ProjectPlanner-<versão>-win-x64-instalador.exe`. O SmartScreen vai avisar (ver abaixo). O instalador não deve pedir administrador.
2. Ao terminar, confira o atalho no menu Iniciar e o programa em **Configurações → Aplicativos**, com a versão certa.
3. Copie um `.pplan` para a máquina. Ele deve aparecer com o ícone de folha e barras, e o tipo "Projeto do ProjectPlanner".
4. Dê duplo clique: o ProjectPlanner abre com o projeto carregado e o nome do arquivo no título.
5. Com o programa aberto, dê duplo clique em outro `.pplan`: abre em outra janela.
6. Abra a galeria de fotos de uma tarefa e adicione uma foto JPG: a miniatura deve aparecer (confirma que a biblioteca de imagens foi junto no `.exe`).
7. Abra o painel do assistente, clique na engrenagem (Configurações do assistente), cole uma chave do Gemini, clique em **Testar conexão** e depois em **Salvar**: a chave vai para o **Gerenciador de Credenciais do Windows** (Painel de Controle → Gerenciador de Credenciais → Credenciais do Windows → `ProjectPlanner:gemini-api-key`). Feche e abra o programa: o assistente continua configurado e responde.
8. Desinstale por **Configurações → Aplicativos**: o `.pplan` volta ao ícone genérico e deixa de abrir no ProjectPlanner.

Sem Windows à mão, dá para conferir os binários gerados:

```bash
file publicacao/win-x64/ProjectPlanner-x64.exe            # PE32+ executable (GUI) x86-64, for MS Windows
file publicacao/win-arm64/ProjectPlanner-ARM64.exe        # PE32+ executable (GUI) Aarch64, for MS Windows
file publicacao/ProjectPlanner-*-win-x64-instalador.exe   # PE32 executable (GUI) Intel 80386, … Nullsoft Installer
ls -lh publicacao/*.exe
```

**Assistente de IA no binário publicado (Bloco 23):** no `.app`, abra o painel do assistente, clique na engrenagem, cole uma chave do Gemini, **Testar conexão** e **Salvar**, mande uma pergunta sobre o cronograma e peça um cronograma ("monte o cronograma de uma residência de 100 m²"): as tarefas aparecem na grid e no Gantt, e o Desfazer da conversa volta ao cronograma de antes (Bloco 24). A chave fica nas **Chaves do macOS** (app Acesso às Chaves, item `ProjectPlanner`). Depois de publicar uma versão nova, o macOS pode perguntar se o programa pode usar esse item (o `.app` é assinado sem identidade de desenvolvedor): "Permitir sempre". Ver `docs/ASSISTENTE-IA.md`.

**Novidades da 0.46 (Bloco 48):** o programa não muda. A novidade é o painel financeiro do servidor, em
`/admin` (ver `docs/SERVIDOR.md`).

**Novidades da 0.45 no binário publicado (Bloco 46):** com a conta conectada, abra o painel do assistente: no topo
aparece o seletor de nível (Padrão, Avançado, Máximo). No plano Grátis, Avançado e Máximo aparecem apagados. Mande uma
pergunta: ao lado do seletor aparece o saldo de créditos do mês. Nas configurações (engrenagem) não há mais campo de
chave: só a conta, o nível e o modo. Para testar o Máximo, passe a conta para o Profissional pela rota de admin, com a
`Anthropic__Chave` configurada no Render.

**Novidades da 0.44 no binário publicado (Bloco 45):** em **Conta > Entrar ou criar conta…**, entre com a conta do
servidor (a criada no teste do servidor, por exemplo): a barra de status mostra "Conta: Nome" e Minha conta mostra o plano
e o uso. Nas configurações do assistente (engrenagem), escolha **Conta ProjectPlanner** e mande uma pergunta: a resposta
vem pelo servidor, e o uso do mês sobe. Volte para **Minha chave do Gemini**: tudo continua como antes. Com o servidor
dormindo (plano grátis do Render), o primeiro pedido pode levar cerca de 1 minuto.

**Novidades da 0.43 (Bloco 44):** o programa não muda (só a versão e as novidades em Ajuda > Sobre). A novidade é o
servidor de contas e IA, que não vai nos pacotes do programa: ele roda na nuvem, com Docker. Ver `docs/SERVIDOR.md`.

**Novidades da 0.42 no binário publicado (Bloco 42):** com a chave do Gemini, em **Fotos do projeto > Importar em lote**,
escolha algumas fotos da obra e **Classificar com IA**: confira a tarefa, a legenda, o local e o % de cada uma (as
duvidosas em vermelho) e **Importar**. Em **Buscar na pasta da obra…**, escolha uma pasta com fotos; buscar de novo traz só
as novas. No chat, anexe uma foto e peça "coloca essa foto na tarefa certa". No relatório fotográfico, escolha um período
sem fotos: a mensagem diz onde elas estão e **Incluir todas as fotos** resolve.

**Novidades da 0.41 no binário publicado (Bloco 41):** com a chave do Gemini, num projeto com valores, custos e uma
medição, pergunte no chat "como está a obra?" e "onde estou gastando mais?" (as respostas citam os números). Peça
"mostra os indicadores de custo": a tela abre na aba Custos e margem, e a mensagem tem o botão para abrir de novo. Diga
"a escavação está em 80% e chegou a nota de R$ 2.000 de diesel dela": aparece um cartão; **Aplicar** e confira a grid e
Projeto > Custos realizados…; o **Desfazer** do cartão volta tudo.

**Novidades da 0.40 no binário publicado (Bloco 43):** num projeto com valores, orçamento (ou recursos), custos
realizados e uma medição aprovada, abra **Projeto > Indicadores…**. Aba **Custos e margem**: margem prevista e projetada
por etapa, desvios em vermelho. Aba **Medições e caixa**: medido × físico e o fluxo de caixa com gráfico. Aba **Alertas
e análise**: o semáforo e **Analisar com IA** (com a chave do Gemini); edite o texto e exporte o PDF: as páginas novas
e a análise vêm depois do painel.

**Novidades da 0.39 no binário publicado (Bloco 39):** confira os botões novos na barra de ícones (Novo, Abrir,
Recursos da tarefa, Ir para a tarefa, Linha de base, Relatório fotográfico, Fotos do projeto, Medições, Orçamento,
Indicadores e as opções do Gantt) e passe o mouse para ver as dicas. Em **Exibir > Barra de ícones**, esconda alguns,
feche e reabra o programa (continuam escondidos) e use **Mostrar todos os botões**. Estreite a janela: os que não cabem
aparecem no **»**.

**Novidades da 0.38 no binário publicado (Bloco 38):** com a chave do Gemini configurada e uma base SINAPI importada,
abra **Projeto > Orçamento…**, aba **Sugestões da IA**, e **Pedir sugestões**: aparecem composições (da base), preços
estimados ("Estimativa IA"), quantidades, alertas e equipes, cada uma com o porquê. Marque algumas e **Aplicar
marcadas**: confira na aba **Planilha** (as quantidades estimadas com "≈"), e Editar > Desfazer volta tudo. No chat, peça
"orce as vedações": o orçamento abre nessa aba já pedindo.

**Novidades da 0.37 no binário publicado (Bloco 33):** em **Projeto > Orçamento…**, aba **Bases**, importe a planilha
de insumos e a de composições analíticas da SINAPI (a UF, o mês e a desoneração vêm do nome do arquivo). Na aba
**Composições**, busque "alvenaria", escolha uma tarefa com quantidade e **Usar na tarefa**: aparecem os insumos com
coeficiente e parcial. Na aba **Planilha**, informe o BDI e **Guardar BDI**, confira os totais, exporte em PDF, Word e
Excel e **Aplicar aos valores e recursos…**: o Valor das tarefas e os recursos mudam (Editar > Desfazer volta).

**Novidades da 0.36 no binário publicado (Bloco 34):** num projeto com recursos atribuídos (e um disponível por dia
no pedreiro, por exemplo), abra **Projeto > Uso de recursos…**. Aba **Histograma**: as barras de cada dia, a linha do
disponível e os dias acima em vermelho. Aba **Superalocações**: os trechos acima do disponível; **Sugerir nivelamento**
mostra as tarefas que seriam adiadas e o término novo, sem mudar nada; **Aplicar…** põe "não iniciar antes de" nelas
(Editar > Desfazer volta). Aba **Necessidades**: as equipes, os equipamentos e as compras por semana, quinzena ou mês,
com o custo; filtre por tipo e exporte em CSV.

**Novidades da 0.35 no binário publicado (Bloco 32):** em **Projeto > Recursos…**, cadastre profissionais (custo por
dia e disponível por dia), materiais (unidade e custo) e equipamentos; importe uma planilha de insumos (Nome, Unidade,
Preço) e exporte em CSV; **Guardar todos no catálogo**, crie um projeto novo e **Trazer para o projeto**. Selecione uma
tarefa e use **Tarefa > Recursos da tarefa…** (ou um duplo clique na coluna Recursos): 2 pedreiros por dia numa tarefa de
5 dias dão 10 dias; materiais vão pelo total. Ligue as colunas Recursos, Custo orçado e Margem (Exibir > Colunas > Recursos
e custos): a margem fica vermelha quando o custo passa do Valor. Desfaça e refaça; salve e abra de novo.

**Novidades da 0.34 no binário publicado (Bloco 37):** num projeto com valores, linha de base e uma medição aprovada,
abra **Projeto > Custos realizados…**: lance alguns custos (com tarefa e sem tarefa), altere um, remova outro e importe
uma planilha com as colunas Data, EDT, Categoria, Descrição e Valor. Abra **Projeto > Indicadores…**: confira os oito
indicadores (IDP e IDC em vermelho abaixo de 0,95), a Curva S com a linha de base, o atual, o medido, o custo real e a
linha laranja do realizado, as etapas e as atrasadas; mude a data de status e a periodicidade; exporte em PDF e Word. No
relatório fotográfico marque **Página de indicadores**, e nas medições marque **Com indicadores** antes de exportar o
boletim em PDF e Word: aparece uma página a mais com o painel.

**Novidades da 0.33 no binário publicado (Bloco 36):** importe uma planilha com as colunas Item, Unid., Quant. e
Total (**Projeto > Importar valores…**): as colunas Unidade e Quantidade aparecem na grid. Abra **Projeto > Medições…**:
na aba Contrato, ponha uma retenção (5%) e um índice com o valor na data-base; na aba Medições, **Nova medição**,
**Sugerir pelo % concluído** (ou digite o medido de cada item na coluna "No período"), o índice do período e uma glosa, e
confira o resumo financeiro. **Aprovar…**: o boletim trava, e a coluna Valor da grid passa a recusar mudanças (o
contratado muda por aditivo). Na aba Aditivos, digite um acréscimo de quantidade e **Registrar aditivo**: a tarefa muda
na grid. Exporte o boletim em PDF, Word e Excel (abra os três) e use **Fotos do período…**. Salve, feche e abra o
projeto: as medições e os aditivos voltam iguais.

**Novidades da 0.32 no binário publicado (Bloco 35):** num projeto com algumas etapas, use **Projeto > Importar
valores…** com uma planilha (.xlsx ou .csv) que tenha as colunas EDT e Valor, ou preencha a coluna Valor (R$) na grid.
Abra **Projeto > Cronograma físico-financeiro…**: troque mensal, quinzenal, semanal e datas de corte; com uma linha de
base salva, compare a base atual com a LB na Curva S (a tracejada é a prevista; o ponto laranja é o realizado de hoje).
Exporte em PDF (abre no leitor de PDF, em paisagem) e em CSV (abre no Excel com os acentos certos).

**Novidades da 0.31 no binário publicado (Bloco 31):** abra um projeto com vínculos e confira que a coluna Início das
sucessoras mostra a mesma data do Gantt. Digite um Início mais tarde numa tarefa: aparece o 📌 e o Gantt anda. Use
**Projeto > Salvar linha de base…**, mude uma duração e ligue em **Exibir > Colunas** as de linha de base e desvio: a
barra cinza fica sob a barra atual e o desvio aparece em dias úteis. Ponha um prazo antes do término numa tarefa (coluna
Prazo): a seta vermelha aparece no Gantt, a folga fica negativa e a tarefa entra no **Resumo do projeto**. Um arquivo
da 0.30 abre com as mesmas datas.

**Novidades da 0.30 no binário publicado (Bloco 29):** com a chave do Gemini configurada no assistente, abra
**Exibir > Fotos do projeto… > Sugestões da IA**: **Sugerir legendas** manda as fotos sem legenda (até 12) e mostra as
legendas para revisar; **Conferir o % pelas fotos** manda as fotos recentes das tarefas em andamento e sugere o %, com
o porquê. Edite, marque e aplique; Editar > Desfazer volta. Na aba **Antes e depois**, escolha um local ou uma tarefa
com fotos em dias diferentes. Em **Arquivo > Relatório fotográfico…**, **Sugerir com IA** preenche o texto do andamento
e os pontos de atenção, e **Antes e depois por local** acrescenta as páginas de comparação; gere o PDF e o Word e
confira a página do texto e as de antes e depois. Sem chave, as telas avisam onde configurar.

**Novidades da 0.29 no binário publicado (Bloco 30):** edite algumas tarefas (duração, nome, remova um resumo, mova uma
linha) e use **Editar > Desfazer** ou ⌘Z várias vezes: tudo volta na ordem, e ⇧⌘Z (no Windows também Ctrl+Y) refaz. O
item do menu e a dica do botão dizem o que vai ser desfeito ("Desfazer Duração de "Escavação""). Exclua uma foto na
galeria, feche e desfaça: ela volta com a imagem, mesmo se o projeto foi salvo no meio. Uma resposta do assistente pode
ser desfeita no chat mesmo que não seja a última. **Copiar** (⌘C), **Colar** (⌘V) e **Duplicar** (⌘D) uma tarefa com
subtarefas trazem os vínculos entre elas, sem as fotos. Num campo de texto (editando uma célula, o nome do projeto, o
chat), essas teclas continuam sendo do texto.

**Novidades da 0.28 no binário publicado (Bloco 28):** em **Exibir > Fotos do projeto…**, aba **Importar em lote**,
escolha fotos de vários dias (de preferência tiradas no celular, com data e GPS no EXIF): cada uma aparece com a tarefa
prevista no dia da foto; confira, troque o que precisar e **Importar**. A **Linha do tempo** mostra todas as fotos do
projeto por dia, com a tarefa de cada uma; uma foto com GPS tem o selo "GPS" e o botão **Ver no mapa** (abre o
OpenStreetMap no navegador). Em **Locais e alerta**, cadastre os locais da obra (viram sugestão no campo Local da
galeria) e os dias do alerta: as tarefas em andamento sem foto recente ganham um ⚠ ao lado do nome na grid e aparecem
no **Resumo do projeto**. Os locais, os dias e o GPS ficam no .pplan: um arquivo salvo na 0.28 não abre mais na 0.27
(ela avisa que foi salvo por uma versão mais nova), como acontece sempre que o arquivo ganha dados novos.

**Novidades da 0.27 no binário publicado (Bloco 27):** abra um projeto com fotos, marque uma foto como destaque e tire
outra do relatório na galeria, e gere **Arquivo > Relatório fotográfico… > Gerar PDF…** e **Gerar Word…**: o PDF abre no
Visualizador/Acrobat e o .docx no Word, com a capa, as fotos e as legendas. O relatório usa a biblioteca SkiaSharp (a
mesma das fotos, já dentro do programa) e a DocumentFormat.OpenXml (MIT), que vai junto no `.exe` e no `.app`:

```bash
ls publicacao/ProjectPlanner.app/Contents/MacOS/DocumentFormat.OpenXml*.dll
```

O logotipo escolhido é copiado para a pasta das preferências (`logotipo.png` ou `.jpg`, ao lado do `relatorio.json`).

**Novidades da 0.26 no binário publicado (Bloco 26):** com uma chave do Gemini configurada, peça "monte o cronograma
desta casa" (de preferência com uma planta ou memorial em PDF anexado): aparece o cartão "Como você prefere?". Em
**Estudar e conversar antes**, responda às perguntas clicando nas respostas, confira a **Ficha do projeto** no topo do
painel, aprove o plano e veja o cronograma montado. Salve, feche e abra o projeto de novo: a conversa e a ficha voltam.
Em **Configurações do assistente > Ao pedir um cronograma**, a escolha fica no `assistente.json`. Sem chave, o cartão não
aparece (as respostas são simuladas).

**Novidades da 0.25 no binário publicado (Bloco 25):**

- **Ajuda > Sobre o ProjectPlanner:** a versão é a publicada e a lista de novidades é a seção dela no `CHANGELOG.md`, que vai embutido no programa. Uma versão que não aparece no `CHANGELOG.md` mostra a lista vazia: confira antes de publicar.
- **Cópias de segurança:** salve o mesmo projeto duas vezes e abra **Arquivo > Abrir a pasta de cópias de segurança**: abre a pasta das cópias daquele projeto, com a versão anterior (`<nome>-AAAAMMDD-HHMMSS-mmm.pplan`; ficam as 5 mais recentes), que abre como um projeto qualquer. As pastas de todos os projetos ficam em `backups`, junto das preferências: `~/Library/Application Support/ProjectPlanner/backups` no Mac, `%APPDATA%\ProjectPlanner\backups` no Windows.
- **Registro de erros:** **Ajuda > Abrir a pasta de registros de erros** abre a pasta `logs` (um arquivo `erros-AAAA-MM.log` por mês), na mesma pasta das preferências. Ela só ganha arquivos quando um erro inesperado acontece; um erro na tela vira um aviso e o programa continua aberto.
- **Tema:** **Exibir > Tema > Escuro** deixa tudo legível (zoom do Gantt, balões do assistente, galeria de fotos); a escolha fica no `interface.json` e volta na próxima abertura.
- **Desempenho:** num projeto grande, clicar nas linhas da grid troca a seleção na hora, sem redesenhar o Gantt.

**Fotos no binário publicado (Bloco 22):** a biblioteca de imagens precisa ir junto. No Mac, confira `publicacao/ProjectPlanner.app/Contents/MacOS/libSkiaSharp.dylib` e, no `.app`, abra a galeria de uma tarefa e adicione uma foto JPG (de preferência de celular, com EXIF): a miniatura aparece com a data da foto, e girar e ampliar funcionam. No Windows, o `.exe` único leva a `libSkiaSharp.dll` dentro dele:

```bash
ls -l publicacao/ProjectPlanner.app/Contents/MacOS/libSkiaSharp.dylib
LC_ALL=C grep -a -c libSkiaSharp.dll publicacao/win-x64/ProjectPlanner-x64.exe   # 1 ou mais
```

## Qual `.exe` usar no Windows

Na dúvida, veja em **Configurações → Sistema → Sobre → Tipo de sistema**:

- "processador baseado em x64" (quase todos os PCs Intel/AMD): **`ProjectPlanner-<versão>-win-x64-instalador.exe`** ou o portátil **`ProjectPlanner-x64.exe`**.
- "processador baseado em ARM": **`ProjectPlanner-<versão>-win-arm64-instalador.exe`** ou o portátil **`ProjectPlanner-ARM64.exe`**. O x64 também roda, por emulação, só que mais devagar.

Abrir o ARM64 num PC x64 dá a mensagem "Este aplicativo não pode ser executado em seu PC". O instalador de cada arquitetura roda nas duas, mas instala o programa daquela arquitetura.

## Primeira execução em outras máquinas

Os executáveis e o instalador não têm assinatura de desenvolvedor (Microsoft/Apple), então os sistemas avisam na primeira vez:

- **Windows (SmartScreen), no instalador e no `.exe` portátil:** "O Windows protegeu o computador" → **Mais informações** → **Executar assim mesmo**.
- **macOS (Gatekeeper), num Mac que baixou o `.zip`:** clique com o botão direito no `ProjectPlanner.app` → **Abrir** → **Abrir**. Se aparecer "está danificado", rode `xattr -dr com.apple.quarantine ProjectPlanner.app`.
- **macOS, ao salvar em Documentos:** o sistema pode pedir permissão para o ProjectPlanner acessar a pasta. Isso acontece porque o salvamento grava um arquivo temporário ao lado do arquivo escolhido, para nunca deixar um projeto pela metade.

Para distribuir sem esses avisos, é preciso assinar os executáveis: certificado de assinatura de código no Windows; Developer ID e notarização da Apple no macOS. Isso fica para quando houver distribuição pública.
