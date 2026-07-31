[CmdletBinding()]
param(
    [string]$GamePath = 'C:\Program Files\Epic Games\SunlessSea',
    [string]$ProfilePath = '',
    [string]$StateRoot = '',
    [switch]$AllowUnsupportedVersion
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-GameVersion([string]$ResourcesPath) {
    $text = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($ResourcesPath))
    $match = [regex]::Match($text, '"VersionNumber"\s*:\s*"(?<version>[^"]+)"')
    if (-not $match.Success) { return $null }
    return $match.Groups['version'].Value + '-Windows'
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

function Get-Tutorials([string]$Path) {
    $parsed = ConvertFrom-Json -InputObject (Get-Content -LiteralPath $Path -Raw -Encoding UTF8)
    $tutorials = @()
    foreach ($tutorial in $parsed) { $tutorials += $tutorial }
    $expectedMultiplicity = if ($tutorials.Count -eq 34) {
        1
    }
    elseif ($tutorials.Count -eq 68) {
        2
    }
    else {
        throw "Неподдерживаемая структура обучения: $Path ($($tutorials.Count) записей)"
    }

    foreach ($id in 1..34) {
        $count = @($tutorials | Where-Object { [int]$_.Id -eq $id }).Count
        if ($count -ne $expectedMultiplicity) {
            throw "Неподдерживаемая структура обучения: $Path (Id $id встречается $count раз)"
        }
    }
    return $tutorials
}

function Get-TutorialReplacements([string]$Path) {
    $tutorials = Get-Tutorials $Path
    $replacements = @{}
    foreach ($id in @(14, 15)) {
        $matches = @($tutorials | Where-Object { [int]$_.Id -eq $id })
        if ($matches.Count -ne 1) {
            throw "В текстовом пакете ожидалась одна запись обучения Id ${id}: $Path"
        }
        $replacements[$id] = $matches[0]
    }
    return $replacements
}

function Test-TutorialTranslations([string]$Path, [hashtable]$Replacements) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    $tutorials = Get-Tutorials $Path
    foreach ($id in @(14, 15)) {
        foreach ($tutorial in @($tutorials | Where-Object { [int]$_.Id -eq $id })) {
            $replacement = $Replacements[$id]
            if ([string]$tutorial.Name -ne [string]$replacement.Name -or
                [string]$tutorial.Description -ne [string]$replacement.Description) {
                return $false
            }
        }
    }
    return $true
}

function Update-TutorialTranslations([string]$Path, [hashtable]$Replacements) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    $tutorials = Get-Tutorials $Path
    $changed = $false
    foreach ($id in @(14, 15)) {
        foreach ($tutorial in @($tutorials | Where-Object { [int]$_.Id -eq $id })) {
            $replacement = $Replacements[$id]
            if ([string]$tutorial.Name -ne [string]$replacement.Name -or
                [string]$tutorial.Description -ne [string]$replacement.Description) {
                $tutorial.Name = [string]$replacement.Name
                $tutorial.Description = [string]$replacement.Description
                $changed = $true
            }
        }
    }
    if ($changed) {
        $json = $tutorials | ConvertTo-Json -Depth 100
        [IO.File]::WriteAllText($Path, $json, (New-Object Text.UTF8Encoding($false)))
    }
    return $changed
}

function Apply-Delta(
    [string]$SourcePath,
    [string]$DeltaPath,
    [string]$OutputPath,
    [string]$ExpectedBaseSha256,
    [string]$ExpectedTargetSha256,
    [switch]$AllowUnknownBase
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
        $sourceHash = Get-Sha256 $SourcePath
        $isExpectedBase = $sourceInfo.Length -eq $baseLength -and $sourceHash -eq $baseHash
        if (-not $isExpectedBase -and -not $AllowUnknownBase) {
            throw "Исходный файл не соответствует поддерживаемой версии: $SourcePath"
        }
        if (-not $isExpectedBase) {
            Write-Warning "Дельта применяется к непроверенному файлу: $SourcePath"
            if ($sourceInfo.Length -ne $baseLength) {
                Write-Warning "Размер файла отличается: найдено $($sourceInfo.Length), ожидалось $baseLength байт."
            }
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
        if (-not $AllowUnknownBase) {
            Remove-Item -LiteralPath $OutputPath -Force -ErrorAction SilentlyContinue
            throw "Итоговый хэш не совпал: $SourcePath"
        }
        Write-Warning "Итоговый хэш отличается от проверенной сборки: $actualTargetHash"
    }
    return $actualTargetHash
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$manifestPath = Join-Path $scriptRoot 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$dataPath = Join-Path ([IO.Path]::GetFullPath($GamePath)) 'Sunless Sea_Data'
$exePath = Join-Path ([IO.Path]::GetFullPath($GamePath)) 'Sunless Sea.exe'
$versionSourcePath = Join-Path $dataPath 'resources.assets'

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
if (-not (Test-Path -LiteralPath $versionSourcePath)) { throw "Не удалось найти файл версии игры: $versionSourcePath" }

$detectedGameVersion = Get-GameVersion $versionSourcePath
$versionMismatch = [string]::IsNullOrWhiteSpace($detectedGameVersion) -or
    $detectedGameVersion -ne ([string]$manifest.gameVersion)
if ($versionMismatch) {
    $versionLabel = if ([string]::IsNullOrWhiteSpace($detectedGameVersion)) {
        'не удалось определить'
    }
    else {
        $detectedGameVersion
    }

    Write-Host ''
    Write-Host 'ВНИМАНИЕ: версия игры отличается от проверенной.' -ForegroundColor Yellow
    Write-Host "Найдена версия: $versionLabel" -ForegroundColor Yellow
    Write-Host "Проверенная версия: $($manifest.gameVersion)" -ForegroundColor Yellow
    Write-Host ''
    Write-Host 'Продолжайте установку на свой страх и риск: игра может вылетать или перестать запускаться.' -ForegroundColor Red
    Write-Host 'Перед изменением файлов будет создана резервная копия.' -ForegroundColor Yellow

    if (-not $AllowUnsupportedVersion) {
        $confirmation = Read-Host 'Чтобы продолжить, введите ДА. Для отмены нажмите Enter'
        if (([string]$confirmation).Trim() -ine 'ДА') {
            Write-Host 'Установка отменена. Файлы игры не изменены.' -ForegroundColor Yellow
            exit 2
        }
    }
}

$fileStates = @()
foreach ($file in $manifest.files) {
    $targetPath = Join-Path $dataPath ([string]$file.relativePath)
    if (-not (Test-Path -LiteralPath $targetPath)) { throw "Отсутствует файл игры: $targetPath" }
    $hash = Get-Sha256 $targetPath
    $status = if ($hash -eq ([string]$file.baseSha256)) {
        'Base'
    }
    elseif ($hash -eq ([string]$file.targetSha256)) {
        'Target'
    }
    else {
        'Unknown'
    }
    if ($status -eq 'Unknown' -and -not $versionMismatch) {
        throw "Версия игры совпадает с проверенной, но файл изменён или повреждён: $($file.relativePath). Выполните проверку файлов в Epic Games и повторите установку. SHA-256: $hash"
    }
    $fileStates += [pscustomobject]@{ Manifest = $file; Path = $targetPath; Hash = $hash; Status = $status }
}

$unsupportedStates = @($fileStates | Where-Object { $_.Status -eq 'Unknown' })
if ($versionMismatch -and $unsupportedStates.Count -gt 0) {
    Write-Host 'Для экспериментальной установки будут изменены непроверенные файлы:' -ForegroundColor Yellow
    foreach ($entry in $unsupportedStates) {
        Write-Host "  $($entry.Manifest.relativePath)" -ForegroundColor Yellow
        Write-Host "    SHA-256: $($entry.Hash)" -ForegroundColor DarkYellow
    }
    Write-Host ''
}

$needsPatch = @($fileStates | Where-Object { $_.Status -eq 'Base' -or $_.Status -eq 'Unknown' })
$profilePayload = Join-Path $scriptRoot 'payload\profile\Russian'
if (-not (Test-Path -LiteralPath $profilePayload)) { throw "Отсутствует текстовый пакет: $profilePayload" }
$referenceTutorialPath = Join-Path $profilePayload 'encyclopaedia\Tutorials.json'
$tutorialReplacements = Get-TutorialReplacements $referenceTutorialPath
$addonRoot = Join-Path $ProfilePath 'addon'
$addonPath = Join-Path $addonRoot 'Russian'
$addonTutorialPath = Join-Path $addonPath 'encyclopaedia\Tutorials.json'
$liveTutorialRelativePaths = @('encyclopaedia\Tutorials.json', 'encyclopaedia\Tutorials_import.json')
$profileNeedsUpdate = -not (Test-TutorialTranslations $addonTutorialPath $tutorialReplacements)
foreach ($relative in $liveTutorialRelativePaths) {
    $livePath = Join-Path $ProfilePath $relative
    if ((Test-Path -LiteralPath $livePath -PathType Leaf) -and
        -not (Test-TutorialTranslations $livePath $tutorialReplacements)) {
        $profileNeedsUpdate = $true
    }
}

if ($needsPatch.Count -eq 0 -and -not $profileNeedsUpdate) {
    Write-Host "Русификатор $($manifest.installerVersion) уже установлен." -ForegroundColor Green
    exit 0
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupDir = Join-Path $StateRoot "backup-$timestamp"
$filesBackupDir = Join-Path $backupDir 'files'
$profileBackupDir = Join-Path $backupDir 'profile-addon-Russian'
$profileLiveBackupDir = Join-Path $backupDir 'profile-live'
New-Item -ItemType Directory -Path $filesBackupDir -Force | Out-Null

$originalFiles = @()
foreach ($entry in $fileStates) {
    $relative = [string]$entry.Manifest.relativePath
    $backupPath = Join-Path $filesBackupDir $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $backupPath) -Force | Out-Null
    Copy-Item -LiteralPath $entry.Path -Destination $backupPath -Force
    $originalFiles += [pscustomobject]@{ RelativePath = $relative; Sha256 = $entry.Hash }
}

Assert-ChildPath $ProfilePath $addonPath
$profileAddonExisted = Test-Path -LiteralPath $addonPath
if ($profileAddonExisted) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $profileBackupDir) -Force | Out-Null
    Copy-Item -LiteralPath $addonPath -Destination $profileBackupDir -Recurse -Force
}

$profileLiveFiles = @()
foreach ($relative in $liveTutorialRelativePaths) {
    $livePath = Join-Path $ProfilePath $relative
    Assert-ChildPath $ProfilePath $livePath
    if (-not (Test-Path -LiteralPath $livePath -PathType Leaf)) { continue }
    $backupPath = Join-Path $profileLiveBackupDir $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $backupPath) -Force | Out-Null
    Copy-Item -LiteralPath $livePath -Destination $backupPath -Force
    $profileLiveFiles += [ordered]@{
        RelativePath = $relative
        Sha256 = Get-Sha256 $livePath
    }
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
    ProfileLiveFiles = $profileLiveFiles
    DetectedGameVersion = $detectedGameVersion
    CompatibilityMode = $versionMismatch
    UnsupportedFiles = @($unsupportedStates | ForEach-Object {
        [ordered]@{
            RelativePath = [string]$_.Manifest.relativePath
            Sha256 = [string]$_.Hash
        }
    })
    OriginalFiles = $originalFiles
    InstalledAt = (Get-Date).ToString('o')
}
$state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $statePath -Encoding UTF8

foreach ($entry in $needsPatch) {
    $relative = [string]$entry.Manifest.relativePath
    $deltaPath = Join-Path (Join-Path $scriptRoot 'payload\patches') ([string]$entry.Manifest.patch)
    $tempPath = $entry.Path + '.ssru.tmp'
    Write-Host "Обновление: $relative"
    $allowUnknownBase = $entry.Status -eq 'Unknown'
    $null = Apply-Delta -SourcePath $entry.Path -DeltaPath $deltaPath -OutputPath $tempPath `
        -ExpectedBaseSha256 ([string]$entry.Manifest.baseSha256) `
        -ExpectedTargetSha256 ([string]$entry.Manifest.targetSha256) `
        -AllowUnknownBase:$allowUnknownBase
    Copy-Item -LiteralPath $tempPath -Destination $entry.Path -Force
    Remove-Item -LiteralPath $tempPath -Force
}

New-Item -ItemType Directory -Path $addonRoot -Force | Out-Null
if (Test-Path -LiteralPath $addonPath) {
    Assert-ChildPath $ProfilePath $addonPath
    Remove-Item -LiteralPath $addonPath -Recurse -Force
}
Copy-Item -LiteralPath $profilePayload -Destination $addonRoot -Recurse -Force
foreach ($relative in $liveTutorialRelativePaths) {
    $livePath = Join-Path $ProfilePath $relative
    if (Update-TutorialTranslations $livePath $tutorialReplacements) {
        Write-Host "Обновление обучения: $relative"
    }
}

$installedFiles = @()
foreach ($entry in $fileStates) {
    $actual = Get-Sha256 $entry.Path
    if ($entry.Status -ne 'Unknown' -and $actual -ne ([string]$entry.Manifest.targetSha256)) {
        throw "Проверка после установки не пройдена: $($entry.Manifest.relativePath)"
    }
    $installedFiles += [ordered]@{
        RelativePath = [string]$entry.Manifest.relativePath
        Sha256 = $actual
        Verified = $actual -eq ([string]$entry.Manifest.targetSha256)
    }
}

$state['InstalledFiles'] = $installedFiles
$state.Status = 'Installed'
$state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $statePath -Encoding UTF8
if ($versionMismatch) {
    Write-Host "Русификатор $($manifest.installerVersion) установлен в экспериментальном режиме совместимости." -ForegroundColor Yellow
    Write-Host 'Если игра вылетает или не запускается, запустите Uninstall.cmd.' -ForegroundColor Yellow
    Write-Host "Резервная копия: $backupDir" -ForegroundColor Yellow
}
else {
    Write-Host "Русификатор $($manifest.installerVersion) установлен. Резервная копия: $backupDir" -ForegroundColor Green
}
