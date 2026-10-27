$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet test tests/Dome.Core.Tests/Dome.Core.Tests.csproj -c Release --logger 'trx;LogFileName=core.trx' --results-directory artifacts/test-results
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed' }
dotnet publish src/Dome/Dome.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o artifacts/win-x64
if ($LASTEXITCODE -ne 0) { throw 'Windows publication failed' }
Copy-Item README.md, VALIDATION.md, LICENSE artifacts/win-x64/
Compress-Archive -Path artifacts/win-x64/* -DestinationPath artifacts/Dome-windows-x64.zip -Force
$files = 'artifacts/win-x64/Dome.exe', 'artifacts/Dome-windows-x64.zip'
$lines = foreach ($file in $files) { '{0}  {1}' -f (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant(), $file.Replace('artifacts/', '') }
$lines | Set-Content artifacts/SHA256SUMS.txt
Write-Host 'Created artifacts/win-x64/Dome.exe and artifacts/Dome-windows-x64.zip'
