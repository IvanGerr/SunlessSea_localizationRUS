[CmdletBinding()]
param(
    [string]$GamePath = 'C:\Program Files\Epic Games\SunlessSea',
    [string]$ProfilePath = '',
    [string]$StateRoot = ''
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Convert-BytesToHex([byte[]]$Bytes) {
    return ([BitConverter]::ToString($Bytes)).Replace('-', '')
}

function Assert-ChildPath([string]$Parent, [string]$Child) {
    $parentFull = [IO.Path]::GetFullPath($Parent).TrimEnd('\') + '\'
    $childFull = [IO.Path]::GetFullPath($Child)
    if (-not $childFull.StartsWith($parentFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Небезопасный путь: $childFull не находится внутри $parentFull"
    }
}

function Apply-Delta(
    [string]$SourcePath,
    [string]$DeltaPath,
    [string]$OutputPath,
    [string]$ExpectedBaseSha256,
    [string]$ExpectedTargetSha256
) {
    $deltaFile = $null
    $gzip = $null
    $reader = $null
    $source = $null
    $output = $null
    try {
        $deltaFile = [IO.File]::OpenRead($DeltaPath)
        $gzip = New-Object IO.Compression.GZipStream($deltaFile, [IO.Compression.CompressionMode]::Decompress)
        $reader = New-Object IO.BinaryReader($gzip, [Text.Encoding]::UTF8)

        $magic = [Text.Encoding]::ASCII.GetString($reader.ReadBytes(8))
        if ($magic -ne 'SSRUDEL1') { throw "Неверный формат дельты: $DeltaPath" }
        $blockSize = $reader.ReadInt32()
        $baseLength = $reader.ReadInt64()
        $targetLength = $reader.ReadInt64()
        $baseHash = Convert-BytesToHex $reader.ReadBytes(32)
        $targetHash = Convert-BytesToHex $reader.ReadBytes(32)

        if ($blockSize -ne 65536) { throw "Неподдерживаемый размер блока: $blockSize" }
        if ($baseHash -ne $ExpectedBaseSha256 -or $targetHash -ne $ExpectedTargetSha256) {
            throw "Хэши в дельте не совпадают с manifest.json: $DeltaPath"
        }
        $sourceInfo = Get-Item -LiteralPath $SourcePath
        if ($sourceInfo.Length -ne $baseLength -or (Get-Sha256 $SourcePath) -ne $baseHash) {
            throw "Исходный файл не соответствует поддерживаемой версии: $SourcePath"
        }

        $source = [IO.File]::Open($SourcePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        $output = [IO.File]::Open($OutputPath, [IO.FileMode]::Create, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $source.CopyTo($output)
        $output.SetLength($targetLength)

        while ($true) {
            $offset = $reader.ReadInt64()
            if ($offset -eq -1) { break }
            $length = $reader.ReadInt32()
            if ($length -lt 0 -or $length -gt $blockSize -or $offset -lt 0 -or ($offset + $length) -gt $targetLength) {
                throw "Повреждённый блок дельты: $DeltaPath"
            }
            $bytes = $reader.ReadBytes($length)
            if ($bytes.Length -ne $length) { throw "Дельта неожиданно закончилась: $DeltaPath" }
            $null = $output.Seek($offset, [IO.SeekOrigin]::Begin)
            $output.Write($bytes, 0, $bytes.Length)
        }
    }
    finally {
        if ($output) { $output.Dispose() }
        if ($source) { $source.Dispose() }
        if ($reader) { $reader.Dispose() }
        elseif ($gzip) { $gzip.Dispose() }
        elseif ($deltaFile) { $deltaFile.Dispose() }
    }

    $actualTargetHash = Get-Sha256 $OutputPath
    if ($actualTargetHash -ne $ExpectedTargetSha256) {
        Remove-Item -LiteralPath $OutputPath -Force -ErrorAction SilentlyContinue
        throw "Итоговый хэш не совпал: $SourcePath"
    }
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$manifestPath = Join-Path $scriptRoot 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$dataPath = Join-Path ([IO.Path]::GetFullPath($GamePath)) 'Sunless Sea_Data'
$exePath = Join-Path ([IO.Path]::GetFullPath($GamePath)) 'Sunless Sea.exe'

if ([string]::IsNullOrWhiteSpace($ProfilePath)) {
    $ProfilePath = [IO.Path]::GetFullPath((Join-Path $env:APPDATA '..\LocalLow\Failbetter Games\Sunless Sea'))
}
if ([string]::IsNullOrWhiteSpace($StateRoot)) {
    $StateRoot = Join-Path $env:LOCALAPPDATA 'SunlessSeaRuInstaller'
}
$ProfilePath = [IO.Path]::GetFullPath($ProfilePath)
$StateRoot = [IO.Path]::GetFullPath($StateRoot)

if (-not (Test-Path -LiteralPath $exePath)) { throw "Игра не найдена: $exePath" }
if (Get-Process -Name 'Sunless Sea' -ErrorAction SilentlyContinue) { throw 'Закройте Sunless Sea перед установкой.' }

$fileStates = @()
foreach ($file in $manifest.files) {
    $targetPath = Join-Path $dataPath ([string]$file.relativePath)
    if (-not (Test-Path -LiteralPath $targetPath)) { throw "Отсутствует файл игры: $targetPath" }
    $hash = Get-Sha256 $targetPath
    if ($hash -ne ([string]$file.baseSha256) -and $hash -ne ([string]$file.targetSha256)) {
        throw "Неподдерживаемая версия файла $($file.relativePath). Выполните проверку файлов в Epic Games и повторите установку. SHA-256: $hash"
    }
    $fileStates += [pscustomobject]@{ Manifest = $file; Path = $targetPath; Hash = $hash }
}

$needsPatch = @($fileStates | Where-Object { $_.Hash -eq ([string]$_.Manifest.baseSha256) })
if ($needsPatch.Count -eq 0) {
    Write-Host "Русификатор $($manifest.installerVersion) уже установлен." -ForegroundColor Green
    exit 0
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupDir = Join-Path $StateRoot "backup-$timestamp"
$filesBackupDir = Join-Path $backupDir 'files'
$profileBackupDir = Join-Path $backupDir 'profile-addon-Russian'
New-Item -ItemType Directory -Path $filesBackupDir -Force | Out-Null

$originalFiles = @()
foreach ($entry in $fileStates) {
    $relative = [string]$entry.Manifest.relativePath
    $backupPath = Join-Path $filesBackupDir $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $backupPath) -Force | Out-Null
    Copy-Item -LiteralPath $entry.Path -Destination $backupPath -Force
    $originalFiles += [pscustomobject]@{ RelativePath = $relative; Sha256 = $entry.Hash }
}

$addonRoot = Join-Path $ProfilePath 'addon'
$addonPath = Join-Path $addonRoot 'Russian'
Assert-ChildPath $ProfilePath $addonPath
$profileAddonExisted = Test-Path -LiteralPath $addonPath
if ($profileAddonExisted) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $profileBackupDir) -Force | Out-Null
    Copy-Item -LiteralPath $addonPath -Destination $profileBackupDir -Recurse -Force
}

New-Item -ItemType Directory -Path $StateRoot -Force | Out-Null
$statePath = Join-Path $StateRoot 'current.json'
$state = [ordered]@{
    Status = 'Installing'
    InstallerVersion = [string]$manifest.installerVersion
    GameVersion = [string]$manifest.gameVersion
    GamePath = [IO.Path]::GetFullPath($GamePath)
    ProfilePath = $ProfilePath
    BackupDir = $backupDir
    ProfileAddonExisted = $profileAddonExisted
    OriginalFiles = $originalFiles
    InstalledAt = (Get-Date).ToString('o')
}
$state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $statePath -Encoding UTF8

foreach ($entry in $needsPatch) {
    $relative = [string]$entry.Manifest.relativePath
    $deltaPath = Join-Path (Join-Path $scriptRoot 'payload\patches') ([string]$entry.Manifest.patch)
    $tempPath = $entry.Path + '.ssru.tmp'
    Write-Host "Обновление: $relative"
    Apply-Delta $entry.Path $deltaPath $tempPath ([string]$entry.Manifest.baseSha256) ([string]$entry.Manifest.targetSha256)
    Copy-Item -LiteralPath $tempPath -Destination $entry.Path -Force
    Remove-Item -LiteralPath $tempPath -Force
}

$profilePayload = Join-Path $scriptRoot 'payload\profile\Russian'
if (-not (Test-Path -LiteralPath $profilePayload)) { throw "Отсутствует текстовый пакет: $profilePayload" }
New-Item -ItemType Directory -Path $addonRoot -Force | Out-Null
if (Test-Path -LiteralPath $addonPath) {
    Assert-ChildPath $ProfilePath $addonPath
    Remove-Item -LiteralPath $addonPath -Recurse -Force
}
Copy-Item -LiteralPath $profilePayload -Destination $addonRoot -Recurse -Force

foreach ($entry in $fileStates) {
    $actual = Get-Sha256 $entry.Path
    if ($actual -ne ([string]$entry.Manifest.targetSha256)) {
        throw "Проверка после установки не пройдена: $($entry.Manifest.relativePath)"
    }
}

$state.Status = 'Installed'
$state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $statePath -Encoding UTF8
Write-Host "Русификатор $($manifest.installerVersion) установлен. Резервная копия: $backupDir" -ForegroundColor Green
