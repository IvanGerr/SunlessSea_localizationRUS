[CmdletBinding()]
param()

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$testRoot = [IO.Path]::GetFullPath((Join-Path $tempBase "SunlessSeaRuInstallerTest-$PID"))
if (-not $testRoot.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe test path: $testRoot"
}

try {
    $packageRoot = Join-Path $testRoot 'package'
    $patchRoot = Join-Path $packageRoot 'payload\patches'
    $profilePayload = Join-Path $packageRoot 'payload\profile\Russian'
    $gameRoot = Join-Path $testRoot 'game'
    $dataRoot = Join-Path $gameRoot 'Sunless Sea_Data'
    $profileRoot = Join-Path $testRoot 'profile'
    $stateRoot = Join-Path $testRoot 'state'
    $inputRoot = Join-Path $testRoot 'input'
    New-Item -ItemType Directory -Path $patchRoot, $profilePayload, $dataRoot, $profileRoot, $inputRoot -Force | Out-Null

    $supportedVersion = '2.2.12.202509090925'
    $unsupportedVersion = '9.9.99.209912312359'
    if ($supportedVersion.Length -ne $unsupportedVersion.Length) {
        throw 'Synthetic version strings must have equal length.'
    }

    $baseBytes = New-Object byte[] (128KB)
    for ($index = 0; $index -lt $baseBytes.Length; $index++) {
        $baseBytes[$index] = [byte][char]'A'
    }
    $baseMarker = [Text.Encoding]::UTF8.GetBytes('"VersionNumber": "' + $supportedVersion + '"')
    $unknownMarker = [Text.Encoding]::UTF8.GetBytes('"VersionNumber": "' + $unsupportedVersion + '"')
    [Array]::Copy($baseMarker, 0, $baseBytes, 70000, $baseMarker.Length)

    $targetBytes = [byte[]]$baseBytes.Clone()
    $targetBytes[0] = [byte][char]'T'
    $unknownBytes = [byte[]]$baseBytes.Clone()
    [Array]::Copy($unknownMarker, 0, $unknownBytes, 70000, $unknownMarker.Length)

    $basePath = Join-Path $inputRoot 'base.assets'
    $targetPath = Join-Path $inputRoot 'target.assets'
    $gameAssetPath = Join-Path $dataRoot 'resources.assets'
    [IO.File]::WriteAllBytes($basePath, $baseBytes)
    [IO.File]::WriteAllBytes($targetPath, $targetBytes)
    [IO.File]::WriteAllBytes($gameAssetPath, $unknownBytes)
    [IO.File]::WriteAllBytes((Join-Path $gameRoot 'Sunless Sea.exe'), [byte[]]@())
    Set-Content -LiteralPath (Join-Path $profilePayload 'test.txt') -Value 'test' -Encoding ASCII
    $tutorialDirectory = Join-Path $profilePayload 'encyclopaedia'
    New-Item -ItemType Directory -Path $tutorialDirectory -Force | Out-Null
    $tutorials = @(1..34 | ForEach-Object {
        [ordered]@{
            Id = $_
            Name = "Tutorial $_"
            Description = "Description $_"
        }
    })
    $tutorialJson = $tutorials | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText(
        (Join-Path $tutorialDirectory 'Tutorials.json'),
        $tutorialJson,
        (New-Object Text.UTF8Encoding($false)))

    $deltaPath = Join-Path $patchRoot 'resources.assets.ssdelta'
    dotnet run --project (Join-Path $repoRoot 'src\BinaryDeltaTool\BinaryDeltaTool.csproj') `
        --configuration Release -- create $basePath $targetPath $deltaPath | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "BinaryDeltaTool failed with exit code $LASTEXITCODE." }

    $manifest = [ordered]@{
        installerVersion = 'compatibility-test'
        gameVersion = $supportedVersion + '-Windows'
        files = @(
            [ordered]@{
                relativePath = 'resources.assets'
                patch = 'resources.assets.ssdelta'
                baseSha256 = Get-Sha256 $basePath
                targetSha256 = Get-Sha256 $targetPath
            }
        )
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $packageRoot 'manifest.json') -Encoding UTF8
    Copy-Item -LiteralPath (Join-Path $repoRoot 'installer\Install-Russian.ps1') -Destination $packageRoot
    Copy-Item -LiteralPath (Join-Path $repoRoot 'installer\Uninstall-Russian.ps1') -Destination $packageRoot

    [IO.File]::WriteAllBytes($gameAssetPath, $baseBytes)
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $packageRoot 'Install-Russian.ps1') `
        -GamePath $gameRoot -ProfilePath $profileRoot -StateRoot $stateRoot
    if ($LASTEXITCODE -ne 0) { throw "Supported installation failed with exit code $LASTEXITCODE." }
    $supportedState = Get-Content -LiteralPath (Join-Path $stateRoot 'current.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([bool]$supportedState.CompatibilityMode) { throw 'Supported installation was incorrectly marked as experimental.' }
    if ((Get-Sha256 $gameAssetPath) -ne (Get-Sha256 $targetPath)) { throw 'Supported installation did not produce the target file.' }
    if (-not [bool]$supportedState.InstalledFiles[0].Verified) { throw 'Supported target was not marked as verified.' }

    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $packageRoot 'Uninstall-Russian.ps1') -StateRoot $stateRoot
    if ($LASTEXITCODE -ne 0) { throw "Supported uninstall failed with exit code $LASTEXITCODE." }
    if ((Get-Sha256 $gameAssetPath) -ne (Get-Sha256 $basePath)) { throw 'Supported uninstall did not restore the base file.' }
    Remove-Item -LiteralPath $stateRoot -Recurse -Force

    $sameVersionModifiedBytes = [byte[]]$baseBytes.Clone()
    $sameVersionModifiedBytes[90000] = [byte][char]'M'
    [IO.File]::WriteAllBytes($gameAssetPath, $sameVersionModifiedBytes)
    $sameVersionModifiedHash = Get-Sha256 $gameAssetPath
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $packageRoot 'Install-Russian.ps1') `
            -GamePath $gameRoot -ProfilePath $profileRoot -StateRoot $stateRoot -AllowUnsupportedVersion *> $null
        $strictExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    if ($strictExitCode -eq 0) { throw 'Modified files with the supported game version were not rejected.' }
    if ((Get-Sha256 $gameAssetPath) -ne $sameVersionModifiedHash) { throw 'Rejected installation changed the game file.' }
    if (Test-Path -LiteralPath $stateRoot) { throw 'Rejected installation created installer state.' }

    [IO.File]::WriteAllBytes($gameAssetPath, $unknownBytes)
    $originalHash = Get-Sha256 $gameAssetPath
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $packageRoot 'Install-Russian.ps1') `
        -GamePath $gameRoot -ProfilePath $profileRoot -StateRoot $stateRoot -AllowUnsupportedVersion
    if ($LASTEXITCODE -ne 0) { throw "Compatibility installation failed with exit code $LASTEXITCODE." }

    $statePath = Join-Path $stateRoot 'current.json'
    $state = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $installedHash = Get-Sha256 $gameAssetPath
    $backupPath = Join-Path ([string]$state.BackupDir) 'files\resources.assets'

    if (-not [bool]$state.CompatibilityMode) { throw 'CompatibilityMode was not recorded.' }
    if ([string]$state.DetectedGameVersion -ne ($unsupportedVersion + '-Windows')) { throw 'Detected game version was not recorded.' }
    if ((Get-Sha256 $backupPath) -ne $originalHash) { throw 'Backup does not match the original unsupported file.' }
    if ($installedHash -eq $originalHash) { throw 'Compatibility delta did not change the file.' }
    if ($installedHash -eq (Get-Sha256 $targetPath)) { throw 'Synthetic compatibility result unexpectedly matches the supported target.' }
    if ([bool]$state.InstalledFiles[0].Verified) { throw 'Unsupported compatibility result was incorrectly marked as verified.' }

    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $packageRoot 'Uninstall-Russian.ps1') -StateRoot $stateRoot
    if ($LASTEXITCODE -ne 0) { throw "Uninstall failed with exit code $LASTEXITCODE." }
    if ((Get-Sha256 $gameAssetPath) -ne $originalHash) { throw 'Uninstall did not restore the unsupported source file.' }

    Write-Host 'Installer compatibility test passed.' -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        $resolved = [IO.Path]::GetFullPath($testRoot)
        if (-not $resolved.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove unsafe test path: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
