[CmdletBinding()]
param(
    [string]$Root = ''
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Split-Path -Parent $PSScriptRoot
}
$Root = [IO.Path]::GetFullPath($Root)

$ignoredDirectories = @('.git', '.vs', 'bin', 'obj', 'artifacts', 'release-input')
$forbiddenExtensions = @(
    '.dll', '.exe', '.assets', '.ress', '.resource', '.x7', '.tpk',
    '.dds', '.zip', '.7z', '.rar', '.pdb', '.png', '.jpg', '.jpeg'
)
$textExtensions = @(
    '.cs', '.csproj', '.slnx', '.json', '.md', '.txt', '.ps1',
    '.cmd', '.props', '.targets', '.gitattributes', '.gitignore'
)
$violations = New-Object System.Collections.Generic.List[string]

$files = Get-ChildItem -LiteralPath $Root -Recurse -File -Force | Where-Object {
    $relative = $_.FullName.Substring($Root.Length).TrimStart('\')
    $parts = $relative -split '\\'
    -not ($parts | Where-Object { $ignoredDirectories -contains $_ })
}

foreach ($file in $files) {
    $relative = $file.FullName.Substring($Root.Length).TrimStart('\')
    $extension = $file.Extension.ToLowerInvariant()

    if ($file.Length -gt 5MB) {
        $violations.Add("File exceeds 5 MB: $relative")
    }
    if ($forbiddenExtensions -contains $extension) {
        $violations.Add("Forbidden binary or archive: $relative")
    }
    if ($file.Name -match '^(Autosave|current\.json|backup)' -or
        $file.Name -match '^\.env' -or
        $extension -in @('.key', '.pem', '.pfx')) {
        $violations.Add("Sensitive filename: $relative")
    }

    if ($textExtensions -contains $extension -or $file.Name -in @('.gitignore', '.gitattributes')) {
        $content = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
        if ($content -match 'C:\\Users\\') {
            $violations.Add("User-specific absolute path: $relative")
        }
        if ($content -match '[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}') {
            $violations.Add("Email address: $relative")
        }
        if ($content -match '-----BEGIN (RSA |OPENSSH |EC )?PRIVATE KEY-----') {
            $violations.Add("Private key material: $relative")
        }
        if ($content -match '\?{4,}' -or $content.Contains([char]0xFFFD)) {
            $violations.Add("Possible encoding damage: $relative")
        }
    }
}

if ($violations.Count -gt 0) {
    throw ("Public tree validation failed:`n" + ($violations -join "`n"))
}

Write-Host "Public tree validation passed: $($files.Count) files." -ForegroundColor Green
