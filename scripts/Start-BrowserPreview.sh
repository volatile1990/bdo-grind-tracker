#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
if command -v dotnet >/dev/null 2>&1; then
    dotnet_command="$(command -v dotnet)"
elif [[ -x "$project_root/.artifacts/dotnet/dotnet" ]]; then
    dotnet_command="$project_root/.artifacts/dotnet/dotnet"
    export DOTNET_ROOT="$project_root/.artifacts/dotnet"
    export DOTNET_CLI_HOME="$project_root/.artifacts/dotnet-home"
else
    echo "Für die Browser-Vorschau wird das .NET-SDK 9.0.318 oder neuer benötigt."
    echo "Installation: https://dotnet.microsoft.com/download/dotnet/9.0"
    exit 1
fi

echo "Grindcrest Browser-Vorschau: http://127.0.0.1:5180"
echo "Overlay: links 'Overlay' wählen, dann 'Im Browser ansehen'. Beenden mit Strg+C."
exec "$dotnet_command" run --project "$project_root/src/BdoGrindTracker.BrowserPreview" \
    --no-launch-profile -p:UseSharedCompilation=false "$@"
