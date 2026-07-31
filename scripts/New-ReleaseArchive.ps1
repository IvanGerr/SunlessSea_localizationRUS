[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Version,

    [Parameter(Mandatory)]
    [string]$PayloadPath,

    [Parameter(Mandatory)]
    [string]$ManifestPath,

    [string]$OutputDirectory = ''
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$PayloadPath = [IO.Path]::GetFullPath($PayloadPath)
$ManifestPath = [IO.Path]::GetFullPath($ManifestPath)
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot 'artifacts'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

if (-not (Test-Path -LiteralPath $PayloadPath -PathType Container)) {
    throw "Payload directory not found: $PayloadPath"
}
if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "Manifest not found: $ManifestPath"
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([string]$manifest.installerVersion -ne $Version) {
    throw "Manifest version $($manifest.installerVersion) does not match $Version."
}

$forbiddenPayloadExtensions = @('.dll', '.exe', '.assets', '.ress', '.resource', '.x7')
$forbidden = Get-ChildItem -LiteralPath $PayloadPath -Recurse -File | Where-Object {
    $forbiddenPayloadExtensions -contains $_.Extension.ToLowerInvariant()
}
if ($forbidden) {
    throw ("Payload contains full game binaries:`n" + (($forbidden.FullName) -join "`n"))
}

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$stageRoot = [IO.Path]::GetFullPath((Join-Path $tempRoot "SunlessSea-RU-$Version-$PID"))
if (-not $stageRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe temporary path: $stageRoot"
}

$packageName = "SunlessSea-RU-Epic-Zubmariner-v$Version"
$packageRoot = Join-Path $stageRoot $packageName
try {
    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot 'installer\Install-Russian.ps1') -Destination $packageRoot
    Copy-Item -LiteralPath (Join-Path $repoRoot 'installer\Uninstall-Russian.ps1') -Destination $packageRoot
    Copy-Item -LiteralPath (Join-Path $repoRoot 'installer\Install.cmd') -Destination $packageRoot
    Copy-Item -LiteralPath (Join-Path $repoRoot 'installer\Uninstall.cmd') -Destination $packageRoot
    Copy-Item -LiteralPath $ManifestPath -Destination (Join-Path $packageRoot 'manifest.json')
    Copy-Item -LiteralPath $PayloadPath -Destination (Join-Path $packageRoot 'payload') -Recurse

    $readmeTemplate = Get-Content -LiteralPath (Join-Path $repoRoot 'installer\README.template.txt') -Raw -Encoding UTF8
    $readme = $readmeTemplate.Replace('{{VERSION}}', $Version).Replace('{{GAME_VERSION}}', [string]$manifest.gameVersion)
    Set-Content -LiteralPath (Join-Path $packageRoot 'README.txt') -Value $readme -Encoding UTF8

    $checksumLines = Get-ChildItem -LiteralPath $packageRoot -Recurse -File |
        Where-Object { $_.Name -ne 'FILES.sha256' } |
        Sort-Object FullName |
        ForEach-Object {
            $relative = $_.FullName.Substring($packageRoot.Length).TrimStart('\')
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            "$hash  $relative"
        }
    Set-Content -LiteralPath (Join-Path $packageRoot 'FILES.sha256') -Value $checksumLines -Encoding ASCII

    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    $archivePath = Join-Path $OutputDirectory "$packageName.zip"
    Compress-Archive -LiteralPath $packageRoot -DestinationPath $archivePath -CompressionLevel Optimal -Force
    $archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    Write-Host "Created: $archivePath" -ForegroundColor Green
    Write-Host "SHA-256: $archiveHash"
}
finally {
    if (Test-Path -LiteralPath $stageRoot) {
        $resolvedStage = [IO.Path]::GetFullPath($stageRoot)
        if (-not $resolvedStage.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove unsafe path: $resolvedStage"
        }
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
