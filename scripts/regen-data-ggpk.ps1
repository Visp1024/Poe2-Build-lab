# scripts/regen-data-ggpk.ps1
# Перегенерация src/Data/* и src/Export/* частей, зависящих от свежего GGPK
# (Gems, Bases, Stats, Skills, Uniques, Spectres). Использует src/Export/Launch.lua
# через PoB runtime (Dat View).
#
# Альтернатива: pathofexile-dat (используется в scripts/regen-localization.ps1
# для локализации) — но он покрывает только небольшой набор таблиц,
# а полный экспорт PoB-данных требует Dat View.
#
# Использование:
#   pwsh ./scripts/regen-data-ggpk.ps1
#   pwsh ./scripts/regen-data-ggpk.ps1 -GgpkPath "D:\Games\steamapps\common\Path of Exile 2"

[CmdletBinding()]
param(
    [string]$GgpkPath = $env:PBL_GGPK_PATH
)

$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)

# ---- 1. Локация игры --------------------------------------------------------
function Find-PoE2Install {
    $candidates = @(
        "D:\Games\steamapps\common\Path of Exile 2",
        "C:\Program Files (x86)\Steam\steamapps\common\Path of Exile 2",
        "C:\Program Files\Grinding Gear Games\Path of Exile 2",
        "${env:ProgramFiles(x86)}\Steam\steamapps\common\Path of Exile 2"
    )
    foreach ($c in $candidates) {
        if (Test-Path (Join-Path $c "Bundles2"))     { return $c }
        if (Test-Path (Join-Path $c "Content.ggpk")) { return $c }
    }
    $libVdf = "C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf"
    if (Test-Path $libVdf) {
        foreach ($m in [regex]::Matches((Get-Content $libVdf -Raw), '"path"\s+"([^"]+)"')) {
            $base = $m.Groups[1].Value -replace '\\\\', '\'
            $candidate = Join-Path $base "steamapps\common\Path of Exile 2"
            if (Test-Path $candidate) { return $candidate }
        }
    }
    return $null
}

if (-not $GgpkPath) {
    $GgpkPath = Find-PoE2Install
    if (-not $GgpkPath) {
        throw "Не найден установленный PoE2. Задайте `$env:PBL_GGPK_PATH или передайте -GgpkPath."
    }
}
if (-not (Test-Path $GgpkPath)) {
    throw "GgpkPath не существует: $GgpkPath"
}
Write-Host "PoE2 install: $GgpkPath" -ForegroundColor Cyan

# Обновляем PBLExport/ggpk_export/config.json (используется regen-localization)
$cfgPath = "PBLExport/ggpk_export/config.json"
if (Test-Path $cfgPath) {
    $cfg = Get-Content $cfgPath -Raw
    $escaped = $GgpkPath -replace '\\', '\\'
    $cfg = $cfg -replace '"steam":\s*"[^"]+"', "`"steam`": `"$escaped`""
    Set-Content $cfgPath $cfg -NoNewline
    Write-Host "Обновлён $cfgPath (поле steam)" -ForegroundColor Green
}

# ---- 2. Прогон src/Export/Launch.lua через Dat View -------------------------
$exe = "runtime\Path of Building-PoE2.exe"
if (-not (Test-Path $exe)) {
    throw "Не найден $exe — runtime отсутствует."
}

Write-Host "`n--- Открываю Dat View (src/Export/Launch.lua) ---" -ForegroundColor Cyan
Write-Host "В окне «Dat View» нужно:" -ForegroundColor Yellow
Write-Host "  1. Указать путь к GGPK: $GgpkPath" -ForegroundColor Yellow
Write-Host "  2. Прогнать экспорт: Skills, Gems, Bases, Stats, Uniques, Minions, Spectres" -ForegroundColor Yellow
Write-Host "  3. Закрыть окно" -ForegroundColor Yellow

Push-Location "src/Export"
try {
    $proc = Start-Process -FilePath "..\..\$exe" -PassThru -Wait
    if ($proc.ExitCode -ne 0) {
        Write-Warning "Dat View завершился с кодом $($proc.ExitCode)."
    }
} finally {
    Pop-Location
}

# ---- 3. Diff src/Data/ + src/Export/ ----------------------------------------
Write-Host "`n--- Изменения в src/Data/ + src/Export/ ---" -ForegroundColor Cyan
git diff --stat -- src/Data/ src/Export/ src/TreeData/

Write-Host "`nДетально: git diff -- src/Data/ src/Export/" -ForegroundColor Yellow
Write-Host "Коммит: git add src/Data src/Export src/TreeData && git commit -m 'data: regen GGPK dumps for 0.20.0'" -ForegroundColor Yellow

Write-Host "`nЕсли Dat View не открыл GGPK — переходи на pathofexile-dat:" -ForegroundColor DarkYellow
Write-Host "  pwsh ./scripts/regen-localization.ps1  # она тянет таблицы для loc, можно расширить config.json под нужды Data" -ForegroundColor DarkYellow

Write-Host "`nДальше:" -ForegroundColor Cyan
Write-Host "  pwsh ./scripts/regen-localization.ps1" -ForegroundColor Green
