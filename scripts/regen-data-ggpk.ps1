# scripts/regen-data-ggpk.ps1
# Headless регенерация src/Data/* через PBLDataExport: pathofexile-dat (JSON dumps)
# -> JsonDatFile shim -> src/Export/Scripts/*.lua. Dat View больше не нужен.
#
# Использование:
#   pwsh ./scripts/regen-data-ggpk.ps1                          # дамп + дефолтный скрипт (costs)
#   pwsh ./scripts/regen-data-ggpk.ps1 -Scripts costs,bases     # выборочный список
#   pwsh ./scripts/regen-data-ggpk.ps1 -NoDump                  # переиспользовать существующие JSON

[CmdletBinding()]
param(
    [string[]]$Scripts = @("costs"),
    [switch]$NoDump,
    [string]$GgpkPath = $env:PBL_GGPK_PATH
)

$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)

# ---- 1. Локация игры (только если делаем pathofexile-dat дамп) -------------
if (-not $NoDump) {
    function Find-PoE2Install {
        $candidates = @(
            "D:\Games\steamapps\common\Path of Exile 2",
            "C:\Program Files (x86)\Steam\steamapps\common\Path of Exile 2",
            "${env:ProgramFiles(x86)}\Steam\steamapps\common\Path of Exile 2"
        )
        foreach ($c in $candidates) {
            if (Test-Path (Join-Path $c "Bundles2")) { return $c }
        }
        return $null
    }
    if (-not $GgpkPath) { $GgpkPath = Find-PoE2Install }
    if (-not $GgpkPath -or -not (Test-Path $GgpkPath)) {
        throw "Не найден PoE2. Задайте `$env:PBL_GGPK_PATH или передайте -GgpkPath, либо используйте -NoDump."
    }
    Write-Host "PoE2 install: $GgpkPath" -ForegroundColor Cyan

    # Синхронизируем PBLExport/ggpk_export/config.json (поле steam).
    $cfgPath = "PBLExport/ggpk_export/config.json"
    $cfg = Get-Content $cfgPath -Raw
    $escaped = $GgpkPath -replace '\\', '\\'
    $cfg = $cfg -replace '"steam":\s*"[^"]+"', "`"steam`": `"$escaped`""
    Set-Content $cfgPath $cfg -NoNewline
}

# ---- 2. Запуск PBLDataExport ----------------------------------------------
Write-Host "`n--- dotnet run PBLDataExport ---" -ForegroundColor Cyan
$exportArgs = @()
if ($NoDump) { $exportArgs += "--no-dump" }
foreach ($s in $Scripts) { $exportArgs += "--script=$s" }

dotnet run --project PBLDataExport/PBLDataExport.csproj --nologo -- @exportArgs
if ($LASTEXITCODE -ne 0) {
    throw "PBLDataExport завершился с кодом $LASTEXITCODE."
}

# ---- 3. Diff ---------------------------------------------------------------
Write-Host "`n--- Изменения в src/Data/ ---" -ForegroundColor Cyan
git diff --stat -- src/Data/ src/Export/

Write-Host "`nДетально: git diff -- src/Data/" -ForegroundColor Yellow
Write-Host "Коммит: git add src/Data && git commit -m 'data: regen GGPK dumps for <version>'" -ForegroundColor Yellow

Write-Host "`nДальше: pwsh ./scripts/regen-localization.ps1" -ForegroundColor Green
