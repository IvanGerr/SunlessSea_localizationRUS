[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ArchivePath,

    [Parameter(Mandatory)]
    [string]$BaseGamePath
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

$ArchivePath = [IO.Path]::GetFullPath($ArchivePath)
$BaseGamePath = [IO.Path]::GetFullPath($BaseGamePath)
if (-not (Test-Path -LiteralPath $ArchivePath -PathType Leaf)) {
    throw "Archive not found: $ArchivePath"
}
if (-not (Test-Path -LiteralPath $BaseGamePath -PathType Container)) {
    throw "Base game directory not found: $BaseGamePath"
}

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$testRoot = [IO.Path]::GetFullPath((Join-Path $tempRoot "SunlessSeaRuReleaseTest-$PID"))
if (-not $testRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe test path: $testRoot"
}
if (Test-Path -LiteralPath $testRoot) {
    throw "Test path already exists: $testRoot"
}

$passed = $false
try {
    $extractRoot = Join-Path $testRoot 'archive'
    New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null
    Expand-Archive -LiteralPath $ArchivePath -DestinationPath $extractRoot

    $packageDirectories = @(Get-ChildItem -LiteralPath $extractRoot -Directory)
    if ($packageDirectories.Count -ne 1) {
        throw "Expected one package directory, found $($packageDirectories.Count)."
    }
    $packageRoot = $packageDirectories[0].FullName
    $manifest = Get-Content -LiteralPath (Join-Path $packageRoot 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $payloadProfile = Join-Path $packageRoot 'payload\profile\Russian'

    $checksumPath = Join-Path $packageRoot 'FILES.sha256'
    $checksumLines = @(Get-Content -LiteralPath $checksumPath -Encoding ASCII | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $packageFiles = @(Get-ChildItem -LiteralPath $packageRoot -Recurse -File | Where-Object { $_.Name -ne 'FILES.sha256' })
    if ($checksumLines.Count -ne $packageFiles.Count) {
        throw "Checksum entry count mismatch: $($checksumLines.Count) entries for $($packageFiles.Count) files."
    }
    $packagePrefix = [IO.Path]::GetFullPath($packageRoot).TrimEnd('\') + '\'
    foreach ($line in $checksumLines) {
        if ($line -notmatch '^(?<hash>[A-Fa-f0-9]{64})  (?<path>.+)$') {
            throw "Invalid checksum line: $line"
        }
        $checkedPath = [IO.Path]::GetFullPath((Join-Path $packageRoot $Matches['path']))
        if (-not $checkedPath.StartsWith($packagePrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unsafe checksum path: $($Matches['path'])"
        }
        if (-not (Test-Path -LiteralPath $checkedPath -PathType Leaf)) {
            throw "Checksum file missing: $($Matches['path'])"
        }
        if ((Get-Sha256 $checkedPath) -ne $Matches['hash'].ToUpperInvariant()) {
            throw "Checksum mismatch: $($Matches['path'])"
        }
    }

    $gameRoot = Join-Path $testRoot 'game'
    Copy-Item -LiteralPath $BaseGamePath -Destination $gameRoot -Recurse
    $profileRoot = Join-Path $testRoot 'profile'
    $previousAddon = Join-Path $profileRoot 'addon\Russian'
    New-Item -ItemType Directory -Path $previousAddon -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $previousAddon 'marker.txt') -Value 'previous-profile' -Encoding ASCII
    $liveTutorialRoot = Join-Path $profileRoot 'encyclopaedia'
    New-Item -ItemType Directory -Path $liveTutorialRoot -Force | Out-Null
    $tutorialSourceJson = Get-Content -LiteralPath (Join-Path $payloadProfile 'encyclopaedia\Tutorials.json') -Raw -Encoding UTF8
    $parsedTutorials = ConvertFrom-Json -InputObject $tutorialSourceJson
    $liveTutorials34 = @()
    foreach ($tutorial in $parsedTutorials) { $liveTutorials34 += $tutorial }
    foreach ($tutorial in @($liveTutorials34 | Where-Object { [int]$_.Id -in @(14, 15) })) {
        $tutorial.Name = 'Zee-bat'
        $tutorial.Description = 'Untranslated tutorial fixture.'
    }
    $liveTutorials68 = @()
    foreach ($copy in 1..2) {
        $parsedCopy = ConvertFrom-Json -InputObject ($liveTutorials34 | ConvertTo-Json -Depth 100)
        foreach ($tutorial in $parsedCopy) { $liveTutorials68 += $tutorial }
    }
    $liveTutorialPath = Join-Path $liveTutorialRoot 'Tutorials.json'
    $liveImportPath = Join-Path $liveTutorialRoot 'Tutorials_import.json'
    [IO.File]::WriteAllText(
        $liveTutorialPath,
        ($liveTutorials68 | ConvertTo-Json -Depth 100),
        (New-Object Text.UTF8Encoding($false)))
    [IO.File]::WriteAllText(
        $liveImportPath,
        ($liveTutorials34 | ConvertTo-Json -Depth 100),
        (New-Object Text.UTF8Encoding($false)))
    $originalLiveTutorialHash = Get-Sha256 $liveTutorialPath
    $originalLiveImportHash = Get-Sha256 $liveImportPath
    $stateRoot = Join-Path $testRoot 'state'

    $installerPath = Join-Path $packageRoot 'Install-Russian.ps1'
    $uninstallerPath = Join-Path $packageRoot 'Uninstall-Russian.ps1'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installerPath `
        -GamePath $gameRoot -ProfilePath $profileRoot -StateRoot $stateRoot
    if ($LASTEXITCODE -ne 0) { throw "Install failed with exit code $LASTEXITCODE." }

    $gameDataPath = Join-Path $gameRoot 'Sunless Sea_Data'
    foreach ($file in $manifest.files) {
        $actual = Get-Sha256 (Join-Path $gameDataPath ([string]$file.relativePath))
        if ($actual -ne ([string]$file.targetSha256)) {
            throw "Installed hash mismatch: $($file.relativePath)"
        }
    }

    $statePath = Join-Path $stateRoot 'current.json'
    $state = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$state.Status -ne 'Installed' -or [bool]$state.CompatibilityMode) {
        throw 'Unexpected state after supported installation.'
    }

    $installedProfile = Join-Path $profileRoot 'addon\Russian'
    $payloadFiles = @(Get-ChildItem -LiteralPath $payloadProfile -Recurse -File)
    $installedFiles = @(Get-ChildItem -LiteralPath $installedProfile -Recurse -File)
    if ($payloadFiles.Count -ne $installedFiles.Count) {
        throw 'Installed profile file count mismatch.'
    }
    foreach ($file in $payloadFiles) {
        $relative = $file.FullName.Substring($payloadProfile.Length).TrimStart('\')
        $installedPath = Join-Path $installedProfile $relative
        if (-not (Test-Path -LiteralPath $installedPath)) {
            throw "Missing installed profile file: $relative"
        }
        if ((Get-Sha256 $file.FullName) -ne (Get-Sha256 $installedPath)) {
            throw "Installed profile hash mismatch: $relative"
        }
    }

    $parsedReferenceTutorials = ConvertFrom-Json -InputObject (Get-Content -LiteralPath (Join-Path $payloadProfile 'encyclopaedia\Tutorials.json') -Raw -Encoding UTF8)
    $referenceTutorials = @()
    foreach ($tutorial in $parsedReferenceTutorials) { $referenceTutorials += $tutorial }
    foreach ($livePath in @($liveTutorialPath, $liveImportPath)) {
        $parsedLiveTutorials = ConvertFrom-Json -InputObject (Get-Content -LiteralPath $livePath -Raw -Encoding UTF8)
        $liveTutorials = @()
        foreach ($tutorial in $parsedLiveTutorials) { $liveTutorials += $tutorial }
        foreach ($id in @(14, 15)) {
            $expectedMatches = @($referenceTutorials | Where-Object { [int]$_.Id -eq $id })
            if ($expectedMatches.Count -ne 1) {
                throw "Expected one reference tutorial $id."
            }
            $expected = $expectedMatches[0]
            foreach ($actual in @($liveTutorials | Where-Object { [int]$_.Id -eq $id })) {
                if ([string]$actual.Name -ne [string]$expected.Name -or
                    [string]$actual.Description -ne [string]$expected.Description) {
                    throw "Live tutorial $id was not translated: $livePath"
                }
            }
        }
    }

    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installerPath `
        -GamePath $gameRoot -ProfilePath $profileRoot -StateRoot $stateRoot
    if ($LASTEXITCODE -ne 0) { throw "Second install failed with exit code $LASTEXITCODE." }

    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $uninstallerPath -StateRoot $stateRoot
    if ($LASTEXITCODE -ne 0) { throw "Uninstall failed with exit code $LASTEXITCODE." }
    foreach ($file in $manifest.files) {
        $actual = Get-Sha256 (Join-Path $gameDataPath ([string]$file.relativePath))
        if ($actual -ne ([string]$file.baseSha256)) {
            throw "Restored hash mismatch: $($file.relativePath)"
        }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $previousAddon 'marker.txt'))) {
        throw 'Previous profile was not restored.'
    }
    if ((Get-Sha256 $liveTutorialPath) -ne $originalLiveTutorialHash -or
        (Get-Sha256 $liveImportPath) -ne $originalLiveImportHash) {
        throw 'Previous live tutorial files were not restored.'
    }

    $passed = $true
    [pscustomobject]@{
        InstallerVersion = [string]$manifest.installerVersion
        GameVersion = [string]$manifest.gameVersion
        PatchedFiles = @($manifest.files).Count
        ProfileFiles = $payloadFiles.Count
        ChecksumsVerified = $checksumLines.Count
        InstallVerified = $true
        SecondInstallVerified = $true
        UninstallVerified = $true
        LiveTutorialInstallVerified = $true
        LiveTutorialRestoreVerified = $true
    } | ConvertTo-Json -Compress
}
finally {
    if ($passed -and (Test-Path -LiteralPath $testRoot)) {
        $resolved = [IO.Path]::GetFullPath($testRoot)
        if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove unsafe test path: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    elseif (Test-Path -LiteralPath $testRoot) {
        Write-Warning "Failed test files retained at: $testRoot"
    }
}
