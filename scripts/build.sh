#!/usr/bin/env bash
set -euo pipefail
repo_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_dir"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet test tests/Dome.Core.Tests/Dome.Core.Tests.csproj -c Release --logger 'trx;LogFileName=core.trx' --results-directory artifacts/test-results
dotnet publish src/Dome/Dome.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o artifacts/win-x64
python3 scripts/package.py
