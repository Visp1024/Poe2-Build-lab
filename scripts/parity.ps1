# scripts/parity.ps1
# Сравнивает stats между оригинальным PoB (LuaJIT runtime) и нашим PBLEngine
# (NLua 5.4) для одного и того же build XML. Тонкий wrapper над PBLParity.
#
# Использование:
#   pwsh ./scripts/parity.ps1                            # все билды из %LOCALAPPDATA%\PathOfBuilding2\Builds
#   pwsh ./scripts/parity.ps1 -Build "C:\path\build.xml" # один файл
#   pwsh ./scripts/parity.ps1 -All                       # подробный вывод (включая MATCH-строки)
#   pwsh ./scripts/parity.ps1 -Json out\parity.json      # JSON-репорт

[CmdletBinding()]
param(
    [string]$Build,
    [string]$Json,
    [switch]$All,
    [double]$Tolerance = 1e-6
)

$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)

dotnet build PBLParity/PBLParity.csproj --nologo --verbosity minimal | Out-Null
if ($LASTEXITCODE -ne 0) { throw "PBLParity build failed" }

function Invoke-Parity {
    param([string]$xmlPath)
    $args = @($xmlPath, "--tolerance=$Tolerance")
    if ($All)  { $args += "--all" }
    if ($Json) { $args += "--json=$Json" }
    dotnet run --project PBLParity --no-build -- @args
}

if ($Build) {
    if (-not (Test-Path $Build)) { throw "build XML not found: $Build" }
    Invoke-Parity $Build
} else {
    $buildsDir = Join-Path $env:LOCALAPPDATA "PathOfBuilding2\Builds"
    if (-not (Test-Path $buildsDir)) { throw "no builds directory at $buildsDir" }
    $xmls = Get-ChildItem $buildsDir -Filter "*.xml" -Recurse
    if ($xmls.Count -eq 0) { throw "no build XMLs in $buildsDir" }

    $fails = 0
    foreach ($xml in $xmls) {
        Write-Host "`n========================================================================" -ForegroundColor Cyan
        Write-Host "  $($xml.FullName)" -ForegroundColor Cyan
        Write-Host "========================================================================" -ForegroundColor Cyan
        Invoke-Parity $xml.FullName
        if ($LASTEXITCODE -eq 2) { $fails++ }
    }
    Write-Host ""
    if ($fails -eq 0) {
        Write-Host "✓ All $($xmls.Count) builds: parity confirmed." -ForegroundColor Green
    } else {
        Write-Host "✗ $fails / $($xmls.Count) builds have drift." -ForegroundColor Red
        exit 2
    }
}
