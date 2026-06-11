# scripts/regen-localization.ps1
# Перегенерирует все переводы под актуальный GGPK + repoe-fork.
#
# Зависимости:
#   - PBLExport/Program.cs (gems_ru, items_ru, passive_*_ru)
#   - PBLExport/gen_passive_ru.py
#   - PBLExport/gen_passive_names_ru.py
#   - PBLExport/gen_gem_stats_ru.py
#   - PBLExport/ggpk_export/ (свежий экспорт через pathofexile-dat)
#   - repoe-fork dumps в PBLApp.Core/Translations/
#
# Использование:
#   pwsh ./scripts/regen-localization.ps1
#   pwsh ./scripts/regen-localization.ps1 -SkipExport   # пропустить pathofexile-dat

[CmdletBinding()]
param(
    [switch]$SkipExport
)

$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)

# ---- 0. Sanity: Program.cs пути ---------------------------------------------
$programCs = Get-Content "PBLExport/Program.cs" -Raw
if ($programCs -match 'D:\\Work\\PathOfBuilding-PoE2') {
    Write-Warning "PBLExport/Program.cs всё ещё ссылается на старый путь D:\Work\PathOfBuilding-PoE2."
    Write-Warning "Замените на путь репозитория (PathBuildLab) — иначе перевод запишется не туда."
    $r = Read-Host "Продолжить всё равно? (y/N)"
    if ($r -ne 'y') { exit 1 }
}

# ---- 1. pathofexile-dat (свежий GGPK dump) ----------------------------------
if (-not $SkipExport) {
    Write-Host "`n--- pathofexile-dat: экспорт таблиц из GGPK ---" -ForegroundColor Cyan
    Push-Location "PBLExport/ggpk_export"
    try {
        $hasNpx = $null -ne (Get-Command npx -ErrorAction SilentlyContinue)
        if (-not $hasNpx) {
            throw "npx не найден. Установите Node.js (нужен для pathofexile-dat)."
        }
        npx pathofexile-dat@latest
        if ($LASTEXITCODE -ne 0) { throw "pathofexile-dat завершился с ошибкой." }
    } finally {
        Pop-Location
    }
} else {
    Write-Host "Пропуск pathofexile-dat (флаг -SkipExport)." -ForegroundColor Yellow
}

# ---- 2. PBLExport (C# — gems_ru, items_ru, passive_names_ru) ----------------
Write-Host "`n--- dotnet run PBLExport ---" -ForegroundColor Cyan
dotnet run --project PBLExport/PBLExport.csproj --nologo
if ($LASTEXITCODE -ne 0) { throw "PBLExport завершился с ошибкой." }

# ---- 3. Python генераторы (passive_ru, passive_names_ru, gem_stats_ru) ------
$pythonScripts = @(
    "PBLExport/gen_passive_ru.py",
    "PBLExport/gen_passive_names_ru.py",
    "PBLExport/gen_gem_stats_ru.py"
)
foreach ($script in $pythonScripts) {
    if (-not (Test-Path $script)) {
        Write-Warning "Не найден $script — пропуск."
        continue
    }
    Write-Host "`n--- python $script ---" -ForegroundColor Cyan
    python $script
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "$script завершился с ошибкой (exit $LASTEXITCODE). Проверьте лог выше."
    }
}

# ---- 4. Diff переводов ------------------------------------------------------
Write-Host "`n--- Изменения в PBLApp.Core/Translations/ ---" -ForegroundColor Cyan
git diff --stat -- PBLApp.Core/Translations/ PBLApp.Core/Resources/

Write-Host "`nДетально: git diff -- PBLApp.Core/Translations/" -ForegroundColor Yellow
Write-Host "Коммит: git add PBLApp.Core/Translations PBLApp.Core/Resources && git commit -m 'loc: regen for <version>'" -ForegroundColor Yellow

Write-Host "`nСледующий шаг: /pbl-verify — убедиться что UI работает с обновлёнными данными." -ForegroundColor Green
