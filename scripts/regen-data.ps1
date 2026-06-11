# scripts/regen-data.ps1
# Перегенерация src/Data/ из свежего GGPK.
#
# Шаги:
#   1. Найти установленный PoE2 (Steam / standalone) либо взять путь из PBL_GGPK_PATH
#   2. Прогон src/Export/Launch.lua через runtime для обновления Gems/Bases/Stats
#   3. Перегенерация ModCache.lua (запуск PoB с зажатым Ctrl)
#   4. git diff src/Data/ → сводка изменений
#
# Использование:
#   pwsh ./scripts/regen-data.ps1
#   pwsh ./scripts/regen-data.ps1 -GgpkPath "D:\Games\steamapps\common\Path of Exile 2"

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
        if (Test-Path (Join-Path $c "PathOfExileSteam.exe")) { return $c }
        if (Test-Path (Join-Path $c "PathOfExile.exe")) { return $c }
        if (Test-Path (Join-Path $c "Content.ggpk")) { return $c }
        if (Test-Path (Join-Path $c "Bundles2")) { return $c }
    }
    # Steam library folders
    $libVdf = "C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf"
    if (Test-Path $libVdf) {
        foreach ($m in [regex]::Matches((Get-Content $libVdf -Raw), '"path"\s+"([^"]+)"')) {
            $candidate = Join-Path $m.Groups[1].Value "steamapps\common\Path of Exile 2"
            $candidate = $candidate -replace '\\\\', '\'
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

# Сохраняем для последующих скриптов и обновляем PBLExport/ggpk_export/config.json
$cfgPath = "PBLExport/ggpk_export/config.json"
if (Test-Path $cfgPath) {
    $cfg = Get-Content $cfgPath -Raw
    $escaped = $GgpkPath -replace '\\', '\\'
    $cfg = $cfg -replace '"steam":\s*"[^"]+"', "`"steam`": `"$escaped`""
    Set-Content $cfgPath $cfg -NoNewline
    Write-Host "Обновлён $cfgPath (steam path)" -ForegroundColor Green
}

# ---- 2. Снэпшот src/Data/ перед изменениями ---------------------------------
$dataBefore = git rev-parse HEAD
Write-Host "Snapshot HEAD: $dataBefore" -ForegroundColor Cyan

# ---- 3. Прогон src/Export/Launch.lua ----------------------------------------
$exe = "runtime\Path of Building-PoE2.exe"
if (-not (Test-Path $exe)) {
    throw "Не найден $exe — runtime отсутствует."
}

Write-Host "`n--- Запуск Export (src/Export/Launch.lua) ---" -ForegroundColor Cyan
Write-Host "ВРУЧНУЮ: в открывшемся окне Dat View выбрать GGPK и прогнать экспорт нужных таблиц." -ForegroundColor Yellow
Write-Host "Запускаем с явным указанием Launch.lua..." -ForegroundColor Yellow
# Точная команда зависит от того как у PoB настроено указание точки входа.
# Запускаем из src/Export/ — runtime обнаруживает Launch.lua автоматически.
Push-Location "src/Export"
& "..\..\$exe"
Pop-Location

Read-Host "Нажмите Enter когда Export завершён и окно закрыто"

# ---- 4. Регенерация ModCache.lua --------------------------------------------
Write-Host "`n--- Регенерация ModCache.lua (Ctrl-зажат) ---" -ForegroundColor Cyan
Write-Host "Запустится основное приложение PoB. Удерживайте Ctrl до полной загрузки." -ForegroundColor Yellow
Read-Host "Enter — продолжить"

# Эмулируем нажатый Ctrl: PoB обнаруживает зажатый Ctrl при старте.
# Делаем это вручную — автоматизация нажатий ненадёжна.
& ".\$exe"
Read-Host "Закройте PoB и нажмите Enter"

# ---- 5. Diff src/Data/ ------------------------------------------------------
Write-Host "`n--- Изменения в src/Data/ ---" -ForegroundColor Cyan
git diff --stat -- src/Data/ src/TreeData/
Write-Host "`nДетально: git diff -- src/Data/" -ForegroundColor Yellow
Write-Host "После проверки: git add src/Data src/TreeData && git commit -m 'data: regen for <version>'" -ForegroundColor Yellow
Write-Host "`nСледующий шаг: pwsh ./scripts/regen-localization.ps1" -ForegroundColor Green
