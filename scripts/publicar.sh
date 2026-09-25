#!/usr/bin/env bash
# Gera os executáveis distribuíveis do ProjectPlanner (ver PUBLISHING.md).
#
#   scripts/publicar.sh            # todos: win-x64, win-arm64 e osx-arm64
#   scripts/publicar.sh win-x64    # só um (win-x64, win-arm64 ou osx-arm64)
#
# Resultado em publicacao/:
#   ProjectPlanner-<versão>-win-x64.zip     (contém ProjectPlanner.exe)
#   ProjectPlanner-<versão>-win-arm64.zip   (contém ProjectPlanner.exe)
#   ProjectPlanner-<versão>-osx-arm64.zip   (contém ProjectPlanner.app)
set -euo pipefail

raiz="$(cd "$(dirname "$0")/.." && pwd)"
app="$raiz/src/ProjectPlanner.App/ProjectPlanner.App.csproj"
saida="$raiz/publicacao"
versao="$(dotnet msbuild "$app" -getProperty:Version)"

publicar_windows() {
    local rid="$1"
    echo "==> $rid (versão $versao)"
    rm -rf "${saida:?}/$rid"
    dotnet publish "$app" -p:PublishProfile="$rid"
    (cd "$saida/$rid" && zip -q -j "$saida/ProjectPlanner-$versao-$rid.zip" ProjectPlanner.exe)
    echo "    $saida/$rid/ProjectPlanner.exe"
}

publicar_mac() {
    local rid="osx-arm64"
    local pacote="$saida/ProjectPlanner.app"
    echo "==> $rid (versão $versao)"
    rm -rf "${saida:?}/$rid" "$pacote"
    dotnet publish "$app" -p:PublishProfile="$rid"

    # Monta o .app: executável e dependências em Contents/MacOS, ícone em Contents/Resources.
    mkdir -p "$pacote/Contents/MacOS" "$pacote/Contents/Resources"
    cp -R "$saida/$rid/." "$pacote/Contents/MacOS/"
    cp "$raiz/src/ProjectPlanner.App/macOS/ProjectPlanner.icns" "$pacote/Contents/Resources/"
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
ls -lh "$saida"/ProjectPlanner-"$versao"-*.zip
