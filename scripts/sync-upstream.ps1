# scripts/sync-upstream.ps1
# Подтягивает изменения Lua-кода из upstream PathOfBuildingCommunity/PathOfBuilding-PoE2
# через 3-way patch — без общего git-предка.
#
# Использование:
#   pwsh ./scripts/sync-upstream.ps1                     # синк до upstream/dev HEAD
#   pwsh ./scripts/sync-upstream.ps1 -TargetRef v0.16.0  # синк до конкретного tag/sha
#   pwsh ./scripts/sync-upstream.ps1 -DryRun             # только показать diff, патч не применять

[CmdletBinding()]
param(
    [string]$TargetRef = "upstream/dev",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)

# ---- 0. Читаем маркер --------------------------------------------------------
$markerPath = ".upstream-sync.yaml"
if (-not (Test-Path $markerPath)) {
    throw "Не найден $markerPath — пайплайн не инициализирован."
}
$marker = Get-Content $markerPath -Raw
$lastSha = ([regex]::Match($marker, 'last_synced_sha:\s*([0-9a-f]+)')).Groups[1].Value
$lastVer = ([regex]::Match($marker, 'last_synced_version:\s*([0-9.]+)')).Groups[1].Value
$syncPaths = @()
$inPaths = $false
foreach ($line in $marker -split "`n") {
    if ($line -match '^sync_paths:') { $inPaths = $true; continue }
    if ($inPaths -and $line -match '^\S') { break }
    if ($inPaths -and $line -match '^\s*-\s*(.+?)\s*$') { $syncPaths += $Matches[1] }
}
Write-Host "Last synced: $lastVer ($lastSha)" -ForegroundColor Cyan
Write-Host "Target:      $TargetRef" -ForegroundColor Cyan
Write-Host "Sync paths:  $($syncPaths -join ', ')" -ForegroundColor Cyan

# ---- 1. Чистое дерево + fetch upstream --------------------------------------
if (-not $DryRun) {
    $status = git status --porcelain
    if ($status) {
        throw "Рабочее дерево грязное. Закоммитьте или stash перед синком."
    }
}
git fetch upstream --no-tags --quiet
$targetSha = (git rev-parse $TargetRef).Trim()
Write-Host "Target SHA:  $targetSha" -ForegroundColor Cyan

if ($targetSha -eq $lastSha) {
    Write-Host "Уже на последней версии — нечего синкать." -ForegroundColor Green
    exit 0
}

# ---- 2. Ветка для синка ------------------------------------------------------
$date = Get-Date -Format "yyyyMMdd"
$branchName = "upstream-sync/$date"
if (-not $DryRun) {
    git checkout -B $branchName | Out-Null
    Write-Host "Ветка: $branchName" -ForegroundColor Cyan
}

# ---- 3. Считаем patch upstream<lastSha..targetSha> по выбранным путям -------
$pathArgs = @("--") + $syncPaths
$diffArgs = @($lastSha, $targetSha) + $pathArgs
$shortStat = git diff --shortstat @diffArgs
Write-Host "`nUpstream diff: $shortStat" -ForegroundColor Yellow

if ($DryRun) {
    Write-Host "`n--- Файлы изменённые в upstream ---" -ForegroundColor Yellow
    git diff --name-status @diffArgs
    Write-Host "`n(DryRun) Патч НЕ применён." -ForegroundColor Magenta
    exit 0
}

# ---- 4. 3-way apply ----------------------------------------------------------
$patchFile = Join-Path ([IO.Path]::GetTempPath()) "pbl-upstream-$date.patch"
git diff --binary @diffArgs > $patchFile
Write-Host "Патч записан: $patchFile" -ForegroundColor Cyan

$applyOutput = git apply --3way --whitespace=nowarn $patchFile 2>&1
$applyExit = $LASTEXITCODE
$applyOutput | ForEach-Object { Write-Host $_ }

if ($applyExit -ne 0) {
    Write-Host "`n!!! Конфликты — разрешите вручную (git status), затем:" -ForegroundColor Red
    Write-Host "    git add -A && git commit -m 'sync(upstream): $lastVer -> <new version>'" -ForegroundColor Red
    Write-Host "    pwsh ./scripts/sync-upstream.ps1 -FinalizeOnly  # обновит маркер" -ForegroundColor Red
    exit 1
}

# ---- 5. Build + tests --------------------------------------------------------
Write-Host "`n--- dotnet build PBLEngine ---" -ForegroundColor Cyan
dotnet build PBLEngine/PBLEngine.csproj --nologo --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    throw "Сборка PBLEngine упала после применения патча."
}

# ---- 6. Обновляем маркер ----------------------------------------------------
$newVer = (Select-String -Path manifest.xml -Pattern '<Version number="([0-9.]+)"').Matches[0].Groups[1].Value
$today = Get-Date -Format "yyyy-MM-dd"
(Get-Content $markerPath) `
    -replace 'last_synced_sha:.*', "last_synced_sha: $targetSha" `
    -replace 'last_synced_version:.*', "last_synced_version: $newVer" `
    -replace 'last_synced_date:.*', "last_synced_date: $today" `
    | Set-Content $markerPath

Write-Host "`n✓ Sync $lastVer -> $newVer готов в ветке $branchName" -ForegroundColor Green
Write-Host "  Следующий шаг: pwsh ./scripts/regen-data.ps1" -ForegroundColor Green
