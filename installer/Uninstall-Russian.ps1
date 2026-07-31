[CmdletBinding()]
param(
    [string]$StateRoot = ''
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Assert-ChildPath([string]$Parent, [string]$Child) {
    $parentFull = [IO.Path]::GetFullPath($Parent).TrimEnd('\') + '\'
    $childFull = [IO.Path]::GetFullPath($Child)
    if (-not $childFull.StartsWith($parentFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Небезопасный путь: $childFull не находится внутри $parentFull"
    }
}

if ([string]::IsNullOrWhiteSpace($StateRoot)) {
    $StateRoot = Join-Path $env:LOCALAPPDATA 'SunlessSeaRuInstaller'
}
$StateRoot = [IO.Path]::GetFullPath($StateRoot)
$statePath = Join-Path $StateRoot 'current.json'
if (-not (Test-Path -LiteralPath $statePath)) { throw 'Состояние установленного русификатора не найдено.' }
if (Get-Process -Name 'Sunless Sea' -ErrorAction SilentlyContinue) { throw 'Закройте Sunless Sea перед удалением русификатора.' }

$state = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([string]$state.Status -ne 'Installed') { throw "Нельзя выполнить удаление: состояние $($state.Status)" }
$dataPath = Join-Path ([string]$state.GamePath) 'Sunless Sea_Data'
$filesBackupDir = Join-Path ([string]$state.BackupDir) 'files'

foreach ($file in $state.OriginalFiles) {
    $relative = [string]$file.RelativePath
    $backupPath = Join-Path $filesBackupDir $relative
    $targetPath = Join-Path $dataPath $relative
    if (-not (Test-Path -LiteralPath $backupPath)) { throw "Отсутствует резервная копия: $backupPath" }
    Copy-Item -LiteralPath $backupPath -Destination $targetPath -Force
    if ((Get-Sha256 $targetPath) -ne ([string]$file.Sha256)) { throw "Не удалось восстановить: $relative" }
}

$profilePath = [string]$state.ProfilePath
$addonPath = Join-Path (Join-Path $profilePath 'addon') 'Russian'
Assert-ChildPath $profilePath $addonPath
if (Test-Path -LiteralPath $addonPath) { Remove-Item -LiteralPath $addonPath -Recurse -Force }
if ([bool]$state.ProfileAddonExisted) {
    $profileBackupDir = Join-Path ([string]$state.BackupDir) 'profile-addon-Russian'
    if (-not (Test-Path -LiteralPath $profileBackupDir)) { throw "Отсутствует резервная копия перевода: $profileBackupDir" }
    New-Item -ItemType Directory -Path $addonPath -Force | Out-Null
    Copy-Item -Path (Join-Path $profileBackupDir '*') -Destination $addonPath -Recurse -Force
}

$state.Status = 'Uninstalled'
$state | Add-Member -NotePropertyName 'UninstalledAt' -NotePropertyValue ((Get-Date).ToString('o')) -Force
$state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $statePath -Encoding UTF8
Write-Host 'Исходные файлы и предыдущий каталог addon\Russian восстановлены.' -ForegroundColor Green
