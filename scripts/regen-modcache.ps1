# scripts/regen-modcache.ps1
# Регенерирует src/Data/ModCache.lua прогоном PoB с зажатым Ctrl.
# GGPK не нужен — ModCache собирается из Lua-исходников в src/Data/.
#
# Использование:
#   pwsh ./scripts/regen-modcache.ps1
#   pwsh ./scripts/regen-modcache.ps1 -NoWait   # просто запустить exe, не ждать

[CmdletBinding()]
param(
    [switch]$NoWait
)

$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)

$exe = "runtime\Path of Building-PoE2.exe"
if (-not (Test-Path $exe)) {
    throw "Не найден $exe — runtime отсутствует."
}

$modCachePath = "src\Data\ModCache.lua"
$beforeHash = if (Test-Path $modCachePath) { (Get-FileHash $modCachePath).Hash } else { $null }
$beforeSize = if (Test-Path $modCachePath) { (Get-Item $modCachePath).Length } else { 0 }

Write-Host "Перед регеном:" -ForegroundColor Cyan
Write-Host "  ModCache.lua size:  $beforeSize bytes"
Write-Host "  ModCache.lua sha256: $($beforeHash.Substring(0,16))..." -ForegroundColor DarkGray

Write-Host "`n--- Запуск PoB с зажатым Ctrl ---" -ForegroundColor Cyan
Write-Host "ВНИМАНИЕ: Удерживай LeftCtrl до момента, когда появится Build List." -ForegroundColor Yellow
Write-Host "Регенерация занимает ~30-60 секунд. Затем закрой PoB обычным способом." -ForegroundColor Yellow
Write-Host ""

# Ctrl должен быть зажат В МОМЕНТ старта процесса — иначе detection в Lua не сработает.
# Автоматизировать «зажми и держи» из PowerShell ненадёжно (SendInput работает в фокус,
# а фокус уйдёт на окно PoB до того как Init успеет проверить GetAsyncKeyState).
# Поэтому: пользователь физически держит Ctrl при запуске.

if (-not $NoWait) {
    Read-Host "Зажми и удерживай LeftCtrl, затем нажми Enter ЭТОЙ же рукой (правой)"
}

# Запуск без ожидания exit — PoB долгоживущий процесс.
$proc = Start-Process -FilePath ".\$exe" -PassThru
Write-Host "PoB запущен (PID $($proc.Id))." -ForegroundColor Green

if ($NoWait) {
    Write-Host "Флаг -NoWait: дальнейшие шаги выполни вручную и перезапусти без флага."
    exit 0
}

Read-Host "После того как PoB загрузился (Build List видно) и ты его закрыл, нажми Enter"

# Diff
if (-not (Test-Path $modCachePath)) {
    Write-Warning "ModCache.lua не найден после регена — возможно Ctrl-detection не сработал."
    exit 1
}

$afterHash = (Get-FileHash $modCachePath).Hash
$afterSize = (Get-Item $modCachePath).Length

Write-Host "`nПосле регена:" -ForegroundColor Cyan
Write-Host "  ModCache.lua size:  $afterSize bytes (delta: $($afterSize - $beforeSize))"
Write-Host "  ModCache.lua sha256: $($afterHash.Substring(0,16))..." -ForegroundColor DarkGray

if ($afterHash -eq $beforeHash) {
    Write-Warning "Хэш не изменился — ModCache НЕ был регенерирован."
    Write-Warning "Скорее всего Ctrl не был зажат в момент Init. Перезапустите."
    exit 1
}

Write-Host "`n✓ ModCache регенерирован." -ForegroundColor Green
Write-Host "Дальше:" -ForegroundColor Cyan
Write-Host "  git diff --stat -- src/Data/ModCache.lua"
Write-Host "  git add src/Data/ModCache.lua && git commit -m 'data: regen ModCache for 0.20.0'"
Write-Host "  pwsh ./scripts/regen-data-ggpk.ps1   # следом — GGPK-зависимые dumps"
