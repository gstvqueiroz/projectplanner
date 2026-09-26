#!/usr/bin/env bash
# Gera os executáveis distribuíveis do ProjectPlanner (ver PUBLISHING.md).
#
#   scripts/publicar.sh            # todos: win-x64, win-arm64 e osx-arm64
#   scripts/publicar.sh win-x64    # só um (win-x64, win-arm64 ou osx-arm64)
#
# Resultado em publicacao/:
#   ProjectPlanner-<versão>-win-x64-instalador.exe    (instalador: associa os arquivos .pplan; precisa do makensis)
#   ProjectPlanner-<versão>-win-arm64-instalador.exe
#   ProjectPlanner-x64.exe                  (o de PCs Intel/AMD, solto, para copiar direto, sem instalar)
#   ProjectPlanner-<versão>-win-x64.zip     (contém ProjectPlanner-x64.exe)
#   ProjectPlanner-<versão>-win-arm64.zip   (contém ProjectPlanner-ARM64.exe)
#   ProjectPlanner-<versão>-osx-arm64.zip   (contém ProjectPlanner.app)
set -euo pipefail

raiz="$(cd "$(dirname "$0")/.." && pwd)"
app="$raiz/src/ProjectPlanner.App/ProjectPlanner.App.csproj"
saida="$raiz/publicacao"
versao="$(dotnet msbuild "$app" -getProperty:Version)"

publicar_windows() {
    local rid="$1"
    # A arquitetura vai no nome do .exe: com o mesmo nome nos dois, é fácil abrir o ARM64 num PC
    # Intel/AMD, e o Windows só diz "Este aplicativo não pode ser executado em seu PC".
    local exe arquitetura
    case "$rid" in
        win-x64) exe="ProjectPlanner-x64.exe"; arquitetura="x64" ;;
        win-arm64) exe="ProjectPlanner-ARM64.exe"; arquitetura="ARM64" ;;
    esac
    echo "==> $rid (versão $versao)"
    rm -rf "${saida:?}/$rid" "$saida/$exe"
    dotnet publish "$app" -p:PublishProfile="$rid"
    mv "$saida/$rid/ProjectPlanner.exe" "$saida/$rid/$exe"
    rm -f "$saida/ProjectPlanner-$versao-$rid.zip"
    (cd "$saida/$rid" && zip -q -j "$saida/ProjectPlanner-$versao-$rid.zip" "$exe")
    # O x64 (a maioria dos PCs) também fica solto, para copiar sem precisar extrair.
    if [ "$rid" = win-x64 ]; then
        cp "$saida/$rid/$exe" "$saida/$exe"
    fi
    echo "    $saida/$rid/$exe"

    # Instalador (NSIS): instala para o usuário, sem administrador, e registra os arquivos .pplan.
    local instalador="$saida/ProjectPlanner-$versao-$rid-instalador.exe"
    if ! command -v makensis >/dev/null; then
        echo "    AVISO: makensis não encontrado; o instalador não foi gerado (instale com: brew install makensis)." >&2
        return
    fi
    rm -f "$instalador"
    # O makensis precisa de um locale UTF-8 para ler os acentos do script.
    LC_ALL=en_US.UTF-8 makensis -V2 \
        -DVERSAO="$versao" -DARQUITETURA="$arquitetura" -DEXE="$saida/$rid/$exe" \
        -DICONE_APP="$raiz/src/ProjectPlanner.App/Assets/ProjectPlanner.ico" \
        -DICONE_ARQUIVO="$raiz/src/ProjectPlanner.App/Windows/ProjetoPplan.ico" \
        -DSAIDA="$instalador" "$raiz/src/ProjectPlanner.App/Windows/ProjectPlanner.nsi"
    echo "    $instalador"
}

publicar_mac() {
    local rid="osx-arm64"
    local pacote="$saida/ProjectPlanner.app"
    echo "==> $rid (versão $versao)"
    rm -rf "${saida:?}/$rid" "$pacote"
    dotnet publish "$app" -p:PublishProfile="$rid"

    # Monta o .app: executável e dependências em Contents/MacOS; ícones do app e dos arquivos .pplan
    # em Contents/Resources (o Info.plist declara o tipo .pplan, ver PUBLISHING.md).
    mkdir -p "$pacote/Contents/MacOS" "$pacote/Contents/Resources"
    cp -R "$saida/$rid/." "$pacote/Contents/MacOS/"
    cp "$raiz/src/ProjectPlanner.App/macOS/ProjectPlanner.icns" "$raiz/src/ProjectPlanner.App/macOS/ProjetoPplan.icns" \
        "$pacote/Contents/Resources/"
    sed "s/__VERSAO__/$versao/g" "$raiz/src/ProjectPlanner.App/macOS/Info.plist" > "$pacote/Contents/Info.plist"

    # Assinatura ad hoc: obrigatória para rodar em Apple Silicon. Não é a assinatura de
    # desenvolvedor da Apple, então em outro Mac o Gatekeeper pede confirmação na 1ª vez.
    codesign --force --deep --sign - "$pacote"

    rm -f "$saida/ProjectPlanner-$versao-$rid.zip"
    ditto -c -k --keepParent "$pacote" "$saida/ProjectPlanner-$versao-$rid.zip"
    echo "    $pacote"
}

alvo="${1:-todos}"
mkdir -p "$saida"
case "$alvo" in
    win-x64 | win-arm64) publicar_windows "$alvo" ;;
    osx-arm64) publicar_mac ;;
    todos)
        publicar_windows win-x64
        publicar_windows win-arm64
        publicar_mac
        ;;
    *)
        echo "Uso: $0 [win-x64 | win-arm64 | osx-arm64 | todos]" >&2
        exit 1
        ;;
esac

echo
ls -lh "$saida"/ProjectPlanner-"$versao"-*.zip "$saida"/ProjectPlanner-*.exe 2>/dev/null || true
