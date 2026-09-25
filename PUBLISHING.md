# Publicação do ProjectPlanner

Como gerar os executáveis distribuíveis a partir do Mac.

## O que é gerado

| RID | Arquivo para distribuir | Roda em |
|---|---|---|
| `win-x64` | `publicacao/ProjectPlanner-<versão>-win-x64.zip`, que contém `ProjectPlanner.exe` | Windows 10/11 em PCs Intel/AMD (a maioria) |
| `win-arm64` | `publicacao/ProjectPlanner-<versão>-win-arm64.zip`, que contém `ProjectPlanner.exe` | Windows 11 em ARM (Surface Pro X, Copilot+ PCs, máquinas virtuais Windows ARM no Mac) |
| `osx-arm64` | `publicacao/ProjectPlanner-<versão>-osx-arm64.zip`, que contém `ProjectPlanner.app` | Macs Apple Silicon (M1 ou mais novo), macOS 12+ |

Todos são **self-contained**: levam o runtime do .NET junto e não exigem instalar nada na máquina de destino.

- **Windows:** um único `ProjectPlanner.exe` (cerca de 50 MB). As bibliotecas nativas (Skia, HarfBuzz e SQLite) vêm dentro dele e são extraídas automaticamente na primeira execução.
- **macOS:** o `.app` é uma pasta com o executável e as dependências (cerca de 120 MB; o `.zip` tem 46 MB).

Os arquivos de projeto do usuário usam a extensão **`.pplan`** (um banco SQLite por dentro).

## Pré-requisitos

- .NET SDK 10 (`dotnet --version` deve mostrar 10.x; o `global.json` exige 10.0.100 ou mais novo).
- macOS, para o `osx-arm64`: o script usa `codesign`, `ditto` e `zip`, que já vêm com o sistema.
  Os `.exe` do Windows também podem ser gerados no Linux ou no Windows, com `dotnet publish` direto (ver abaixo).
- Internet na primeira vez: o SDK baixa do NuGet os runtimes de cada plataforma.

## Gerar uma nova versão

1. Suba a versão em [`Directory.Build.props`](Directory.Build.props):

   ```xml
   <Version>0.7.0</Version>
   ```

   A versão vai para os metadados do `.exe` (Propriedades → Detalhes no Windows), para o `Info.plist` do `.app` e para o nome dos `.zip`.

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
     ProjectPlanner-0.7.0-win-x64.zip      ← mande este para quem usa Windows
     ProjectPlanner-0.7.0-win-arm64.zip
     ProjectPlanner-0.7.0-osx-arm64.zip    ← ou este, para Mac
     win-x64/ProjectPlanner.exe
     win-arm64/ProjectPlanner.exe
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

O perfil `osx-arm64` gera só a pasta `publicacao/osx-arm64/`. Quem a transforma em `ProjectPlanner.app` (com `Info.plist`, ícone e assinatura ad hoc) é o `scripts/publicar.sh`.

Equivalente do perfil `win-x64` em linha de comando:

```bash
dotnet publish src/ProjectPlanner.App -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publicacao/win-x64
```

## Ícone e metadados

| O quê | Onde |
|---|---|
| Versão, produto, autor, copyright | [`Directory.Build.props`](Directory.Build.props) |
| Nome do executável (`ProjectPlanner`), descrição, ícone do `.exe` | [`src/ProjectPlanner.App/ProjectPlanner.App.csproj`](src/ProjectPlanner.App/ProjectPlanner.App.csproj) |
| Ícone do Windows e da janela | `src/ProjectPlanner.App/Assets/ProjectPlanner.ico` |
| Ícone e `Info.plist` do macOS | `src/ProjectPlanner.App/macOS/` |

O ícone atual é provisório. Para trocar, substitua o `.ico` (16 a 256 px) e o `.icns` (16 a 1024 px) mantendo os nomes. Em Macs, um `.icns` sai de uma pasta `.iconset` com `iconutil -c icns`.

No .NET 10, o ícone e os metadados entram no `.exe` mesmo compilando no Mac (conferido na versão 0.7.0).

## Testar

**No Mac:** abra `publicacao/ProjectPlanner.app` com duplo clique ou com `open publicacao/ProjectPlanner.app`.

**No Windows, estando num Mac:** o `.exe` não roda no macOS. As opções são:

- **Máquina virtual:** [UTM](https://mac.getutm.app) (gratuito) ou Parallels, com Windows 11 ARM. O Windows ARM roda o `ProjectPlanner.exe` do `win-arm64` nativamente e o do `win-x64` por emulação. Copie o `.zip` para a máquina virtual, extraia e dê duplo clique.
- **Um PC com Windows:** mande o `ProjectPlanner-<versão>-win-x64.zip`.

Sem Windows à mão, dá para conferir o binário gerado:

```bash
file publicacao/win-x64/ProjectPlanner.exe   # PE32+ executable (GUI) x86-64, for MS Windows
file publicacao/win-arm64/ProjectPlanner.exe # PE32+ executable (GUI) Aarch64, for MS Windows
ls -lh publicacao/win-*/ProjectPlanner.exe   # cerca de 50 MB cada
```

## Primeira execução em outras máquinas

Os executáveis não têm assinatura de desenvolvedor (Microsoft/Apple), então os sistemas avisam na primeira vez:

- **Windows (SmartScreen):** "O Windows protegeu o computador" → **Mais informações** → **Executar assim mesmo**.
- **macOS (Gatekeeper), num Mac que baixou o `.zip`:** clique com o botão direito no `ProjectPlanner.app` → **Abrir** → **Abrir**. Se aparecer "está danificado", rode `xattr -dr com.apple.quarantine ProjectPlanner.app`.
- **macOS, ao salvar em Documentos:** o sistema pode pedir permissão para o ProjectPlanner acessar a pasta. Isso acontece porque o salvamento grava um arquivo temporário ao lado do arquivo escolhido, para nunca deixar um projeto pela metade.

Para distribuir sem esses avisos, é preciso assinar os executáveis: certificado de assinatura de código no Windows; Developer ID e notarização da Apple no macOS. Isso fica para quando houver distribuição pública.
