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
   …24826 tokens truncated…placements = ReplaceLiteral(hotkeyConstructor, "Pause ", "Пауза ");
    var zeebatReplacements = ReplaceLiteral(hotkeyConstructor, "Летучая мышь ", "Зи-бэт ");
    if (pauseReplacements != 1 || zeebatReplacements != 1)
        throw new InvalidDataException($"Unexpected hotkey tooltip layout: pause={pauseReplacements}, zeebat={zeebatReplacements}.");

    var menuProviderClosure = allTypes.Single(candidate => candidate.FullName == "Sunless.Game.ApplicationProviders.MenuProvider/<>c");
    var newGameDefaults = menuProviderClosure.Methods.Single(candidate => candidate.Name == "<NewGame>b__30_0" && candidate.HasBody);
    var nameReplacements = ReplaceLiteral(newGameDefaults, "ShadowedStranger", "Незнакомец из Тени");
    if (nameReplacements != 1)
        throw new InvalidDataException($"Expected one default captain name, replaced {nameReplacements}.");

    var formatter = allTypes.Single(candidate => candidate.FullName == "Sunless.Game.Formatters.QIcons.QPossessionTooltipFormatter");
    var weaponTooltip = formatter.Methods.Single(candidate =>
        candidate.Name == "GetQualityPossessionTooltip" &&
        candidate.Parameters.Any(parameter => parameter.ParameterType.FullName == "Sunless.Game.Entities.Combat.CombatAttack"));
    const string helperName = "FormatWeaponSlotForDisplay";
    if (formatter.Methods.Any(candidate => candidate.Name == helperName))
        throw new InvalidDataException($"Method {helperName} already exists.");

    const string stringEqualitySignature = "System.Boolean System.String::op_Equality(System.String,System.String)";
    var stringEquality = allTypes
        .SelectMany(type => type.Methods)
        .Where(method => method.HasBody)
        .SelectMany(method => method.Body.Instructions)
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .First(reference => reference.FullName == stringEqualitySignature);
    var toUpper = weaponTooltip.Body.Instructions
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .Single(reference => reference.FullName == "System.String System.String::ToUpper()");
    var concat = weaponTooltip.Body.Instructions
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .Single(reference => reference.FullName == "System.String System.String::Concat(System.String,System.String)");

    var helper = new MethodDefinition(
        helperName,
        MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
        module.TypeSystem.String);
    helper.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, module.TypeSystem.String));
    helper.Body.MaxStackSize = 2;
    formatter.Methods.Add(helper);
    var helperIl = helper.Body.GetILProcessor();
    var slotNames = new Dictionary<string, string>
    {
        ["Палуба"] = "ПАЛУБНОЕ ОРУДИЕ",
        ["Нос"] = "НОСОВОЕ ОРУДИЕ",
        ["Корма"] = "КОРМОВОЕ ОРУДИЕ",
        ["Deck"] = "ПАЛУБНОЕ ОРУДИЕ",
        ["Forward"] = "НОСОВОЕ ОРУДИЕ",
        ["Aft"] = "КОРМОВОЕ ОРУДИЕ",
    };
    foreach (var (from, to) in slotNames)
    {
        var next = Instruction.Create(OpCodes.Nop);
        helperIl.Append(Instruction.Create(OpCodes.Ldarg_0));
        helperIl.Append(Instruction.Create(OpCodes.Ldstr, from));
        helperIl.Append(Instruction.Create(OpCodes.Call, stringEquality));
        helperIl.Append(Instruction.Create(OpCodes.Brfalse, next));
        helperIl.Append(Instruction.Create(OpCodes.Ldstr, to));
        helperIl.Append(Instruction.Create(OpCodes.Ret));
        helperIl.Append(next);
    }
    helperIl.Append(Instruction.Create(OpCodes.Ldarg_0));
    helperIl.Append(Instruction.Create(OpCodes.Callvirt, toUpper));
    helperIl.Append(Instruction.Create(OpCodes.Ldstr, " ОРУДИЕ"));
    helperIl.Append(Instruction.Create(OpCodes.Call, concat));
    helperIl.Append(Instruction.Create(OpCodes.Ret));

    var instructions = weaponTooltip.Body.Instructions;
    var headingReplacements = 0;
    for (var index = 0; index <= instructions.Count - 3; index++)
    {
        if (instructions[index].Operand is not MethodReference calledToUpper || calledToUpper.FullName != toUpper.FullName ||
            instructions[index + 1].OpCode.Code != Code.Ldstr || instructions[index + 1].Operand as string != " ОРУДИЕ" ||
            instructions[index + 2].Operand is not MethodReference calledConcat || calledConcat.FullName != concat.FullName)
            continue;
        instructions[index].OpCode = OpCodes.Call;
        instructions[index].Operand = helper;
        instructions[index + 1].OpCode = OpCodes.Nop;
        instructions[index + 1].Operand = null;
        instructions[index + 2].OpCode = OpCodes.Nop;
        instructions[index + 2].Operand = null;
        headingReplacements++;
    }
    if (headingReplacements != 1)
        throw new InvalidDataException($"Expected one weapon-slot heading formatter, replaced {headingReplacements}.");

    Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
    module.Write(output);
    Console.WriteLine("Patched Hull, pause, Zeebat, default captain name, and grammatical weapon-slot labels.");
}

static int ReplaceLiteral(MethodDefinition method, string from, string to)
{
    var replacements = 0;
    foreach (var instruction in method.Body.Instructions)
    {
        if (instruction.OpCode.Code != Code.Ldstr || instruction.Operand is not string value || value != from)
            continue;
        instruction.Operand = to;
        replacements++;
    }
    return replacements;
}

static void PatchStoryUpdateLabels(string inputPath, string outputPath)
{
    var input = Path.GetFullPath(inputPath);
    var output = Path.GetFullPath(outputPath);
    using var module = ModuleDefinition.ReadModule(input);
    var allTypes = module.Types.SelectMany(WalkTypes).ToArray();
    var targetTypes = new[]
    {
        "Sunless.Game.UI.Menus.LatestNews/UpdateButtonValues",
        "Sunless.Game.UI.Menus.MainMenu/UpdateButtonValues",
    };
    var replacements = new Dictionary<string, string>
    {
        ["(Cannot connect)"] = "(Нет соединения)",
        ["All stories up to date!"] = "Все истории обновлены!",
        ["NEW STORIES AVAILABLE!"] = "ДОСТУПНЫ НОВЫЕ ИСТОРИИ!",
        ["New Stories Available!"] = "Доступны новые истории!",
        ["Checking for update..."] = "Проверка обновлений...",
        ["Need latest game version"] = "Требуется обновить игру",
        ["Connect to server"] = "Подключиться к серверу",
    };
    var expectedCounts = new Dictionary<string, int>
    {
        ["(Cannot connect)"] = 2,
        ["All stories up to date!"] = 2,
        ["NEW STORIES AVAILABLE!"] = 1,
        ["New Stories Available!"] = 1,
        ["Checking for update..."] = 2,
        ["Need latest game version"] = 2,
        ["Connect to server"] = 1,
    };
    var counts = replacements.Keys.ToDictionary(value => value, _ => 0);

    foreach (var typeName in targetTypes)
    {
        var type = allTypes.Single(candidate => candidate.FullName == typeName);
        var constructor = type.Methods.Single(candidate => candidate.IsConstructor && candidate.IsStatic && candidate.HasBody);
        foreach (var instruction in constructor.Body.Instructions)
        {
            if (instruction.OpCode.Code != Code.Ldstr ||
                instruction.Operand is not string value ||
                !replacements.TryGetValue(value, out var replacement))
                continue;
            instruction.Operand = replacement;
            counts[value]++;
        }
    }

    var invalid = expectedCounts
        .Where(pair => counts[pair.Key] != pair.Value)
        .Select(pair => $"{pair.Key}={counts[pair.Key]}")
        .ToArray();
    if (invalid.Length != 0)
        throw new InvalidDataException("Unexpected story-update label counts: " + string.Join(", ", invalid));

    Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
    module.Write(output);
    Console.WriteLine("Patched story-update status labels.");
}

static void PatchAccountUi(string inputPath, string outputPath)
{
    var input = Path.GetFullPath(inputPath);
    var output = Path.GetFullPath(outputPath);
    using var module = ModuleDefinition.ReadModule(input);
    var type = module.Types.SelectMany(WalkTypes)
        .Single(candidate => candidate.FullName == "Sunless.Game.UI.Menus.Options.AccountOptionsPanel");
    var replacements = new Dictionary<string, string>
    {
        ["<b>Username:</b> (authentication required)"] = "<b>Имя пользователя:</b> (требуется авторизация)",
        ["<b>Username:</b> "] = "<b>Имя пользователя:</b> ",
        ["Quit to the Title Screen before attempting to Authenticate."] = "Перед авторизацией выйдите на титульный экран.",
        ["Currently Playing"] = "Игра запущена",
        ["Outdated Version"] = "Устаревшая версия",
        ["Please update to the latest version of the game."] = "Обновите игру до последней версии.",
    };
    var expectedCounts = new Dictionary<string, int>
    {
        ["<b>Username:</b> (authentication required)"] = 1,
        ["<b>Username:</b> "] = 1,
        ["Quit to the Title Screen before attempting to Authenticate."] = 2,
        ["Currently Playing"] = 1,
        ["Outdated Version"] = 1,
        ["Please update to the latest version of the game."] = 1,
    };
    var counts = replacements.Keys.ToDictionary(value => value, _ => 0);

    foreach (var method in type.Methods.Where(method => method.HasBody))
    {
        foreach (var instruction in method.Body.Instructions)
        {
            if (instruction.OpCode.Code != Code.Ldstr ||
                instruction.Operand is not string value ||
                !replacements.TryGetValue(value, out var replacement))
                continue;
            instruction.Operand = replacement;
            counts[value]++;
        }
    }

    var invalid = expectedCounts
        .Where(pair => counts[pair.Key] != pair.Value)
        .Select(pair => $"{pair.Key}={counts[pair.Key]}")
        .ToArray();
    if (invalid.Length != 0)
        throw new InvalidDataException("Unexpected account UI string counts: " + string.Join(", ", invalid));

    Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
    module.Write(output);
    Console.WriteLine("Patched account management display strings.");
}

static void PatchQualityLabelPrefix(string inputPath, string outputPath)
{
    var input = Path.GetFullPath(inputPath);
    var output = Path.GetFullPath(outputPath);
    using var module = ModuleDefinition.ReadModule(input);
    var type = module.Types.SelectMany(WalkTypes)
        .Single(candidate => candidate.FullName == "FailBetter.Core.Result.QualityChangeMessages.QualityExplicitlySetMessage");
    var constructor = type.Methods.Single(method => method.IsConstructor && !method.IsStatic && method.HasBody);

    var replacements = 0;
    foreach (var instruction in constructor.Body.Instructions)
    {
        if (instruction.OpCode.Code == Code.Ldstr &&
            instruction.Operand is string value &&
            value == "Событие! Характеристика «")
        {
            instruction.Operand = "Событие! «";
            replacements++;
        }
    }
    if (replacements != 1)
        throw new InvalidDataException($"Expected one quality-label prefix, replaced {replacements}.");

    Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
    module.Write(output);
    Console.WriteLine("Patched quality notification prefix: Событие! Характеристика « -> Событие! «");
}
