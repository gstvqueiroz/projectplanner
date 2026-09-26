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
- **macOS:** o `.app` é uma pasta com o executável e as dependências (cerca de 120 MB; o `.zip` tem 46 MB).

Os arquivos de projeto do usuário usam a extensão **`.pplan`** (um banco SQLite por dentro).

## Pré-requisitos

- .NET SDK 10 (`dotnet --version` deve mostrar 10.x; o `global.json` exige 10.0.100 ou mais novo).
- **NSIS**, para o instalador do Windows: `brew install makensis` (versão 3.12 ou mais nova). Sem ele, o script gera tudo menos os instaladores e avisa.
- macOS, para o `osx-arm64`: o script usa `codesign`, `ditto` e `zip`, que já vêm com o sistema.
  Os `.exe` do Windows também podem ser gerados no Linux ou no Windows, com `dotnet publish` direto (ver abaixo).
- Internet na primeira vez: o SDK baixa do NuGet os runtimes de cada plataforma.

## Gerar uma nova versão

1. Suba a versão em [`Directory.Build.props`](Directory.Build.props):

   ```xml
   <Version>0.13.0</Version>
   ```

   A versão vai para os metadados do `.exe` (Propriedades → Detalhes no Windows), para o instalador (nome do arquivo e "Configurações → Aplicativos"), para o `Info.plist` do `.app` e para o nome dos `.zip`.

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

   O resultado fica em `publicacao/`, que é ignorada pelo git:

   ```
   publicacao/
     ProjectPlanner-0.13.0-win-x64-instalador.exe    ← mande este para quem usa Windows
     ProjectPlanner-0.13.0-win-arm64-instalador.exe
     ProjectPlanner-x64.exe                          ← ou este, portátil, onde não der para instalar
     ProjectPlanner-0.13.0-win-x64.zip
     ProjectPlanner-0.13.0-win-arm64.zip
     ProjectPlanner-0.13.0-osx-arm64.zip             ← para Mac
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
LC_ALL=en_US.UTF-8 makensis -DVERSAO=0.13.0 -DARQUITETURA=x64 \
  -DEXE="$PWD/publicacao/win-x64/ProjectPlanner-x64.exe" \
  -DICONE_APP="$PWD/src/ProjectPlanner.App/Assets/ProjectPlanner.ico" \
  -DICONE_ARQUIVO="$PWD/src/ProjectPlanner.App/Windows/ProjetoPplan.ico" \
  -DSAIDA="$PWD/publicacao/ProjectPlanner-0.13.0-win-x64-instalador.exe" \
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
6. Desinstale por **Configurações → Aplicativos**: o `.pplan` volta ao ícone genérico e deixa de abrir no ProjectPlanner.

Sem Windows à mão, dá para conferir os binários gerados:

```bash
file publicacao/win-x64/ProjectPlanner-x64.exe            # PE32+ executable (GUI) x86-64, for MS Windows
file publicacao/win-arm64/ProjectPlanner-ARM64.exe        # PE32+ executable (GUI) Aarch64, for MS Windows
file publicacao/ProjectPlanner-*-win-x64-instalador.exe   # PE32 executable (GUI) Intel 80386, … Nullsoft Installer
ls -lh publicacao/*.exe
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
