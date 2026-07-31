using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length >= 2 && args[0] == "--find")
{
    FindStrings(args[1], args.Skip(2).ToArray());
    return;
}

if (args.Length >= 3 && args[0] == "--dump-type")
{
    DumpType(args[1], args[2]);
    return;
}

if (args.Length >= 2 && args[0] == "--list-ui-english")
{
    ListUiEnglishStrings(args[1]);
    return;
}

if (args.Length >= 2 && args[0] == "--patch-dialogs")
{
    PatchDialogs(args[1], args[2]);
    return;
}

if (args.Length >= 2 && args[0] == "--patch-visual-dialog-labels")
{
    PatchVisualDialogLabels(args[1], args[2]);
    return;
}

if (args.Length >= 2 && args[0] == "--patch-main-menu-labels")
{
    PatchMainMenuLabels(args[1], args[2]);
    return;
}

if (args.Length >= 2 && args[0] == "--patch-safe-ui-texts")
{
    PatchSafeUiTexts(args[1], args[2]);
    return;
}

if (args.Length >= 2 && args[0] == "--patch-combat-ship-ui")
{
    PatchCombatShipUi(args[1], args[2]);
    return;
}

if (args.Length >= 3 && args[0] == "--patch-category-display-names")
{
    PatchCategoryDisplayNames(args[1], args[2]);
    return;
}

if (args.Length >= 3 && args[0] == "--restore-main-menu-back-lookup")
{
    RestoreMainMenuBackLookup(args[1], args[2]);
    return;
}

if (args.Length >= 3 && args[0] == "--audit-menu-lookups")
{
    AuditMenuLookups(args[1], args[2]);
    return;
}

if (args.Length >= 3 && args[0] == "--audit-ui-lookups")
{
    AuditUiLookups(args[1], args[2]);
    return;
}

if (args.Length >= 3 && args[0] == "--restore-alert-dialog-continue-lookup")
{
    RestoreAlertDialogContinueLookup(args[1], args[2]);
    return;
}

if (args.Length >= 3 && args[0] == "--patch-quality-change-messages")
{
    PatchQualityChangeMessages(args[1], args[2]);
    return;
}

if (args.Length >= 3 && args[0] == "--patch-result-messages")
{
    PatchResultMessages(args[1], args[2]);
    return;
}

if (args.Length >= 3 && args[0] == "--patch-log-date")
{
    PatchLogDate(args[1], args[2]);
    return;
}

if (args.Length >= 3 && args[0] == "--patch-item-tooltip-texts")
{
    PatchItemTooltipTexts(args[1], args[2]);
    return;
}

if (args.Length >= 3 && args[0] == "--patch-keybinding-ui")
{
    PatchKeybindingUi(args[1], args[2]);
    return;
}

if (args.Length >= 3 && args[0] == "--patch-jettison-ui")
{
    PatchJettisonUi(args[1], args[2]);
    return;
}

if (args.Length >= 3 && args[0] == "--patch-shipyard-ui")
{
    PatchShipyardUi(args[1], args[2]);
    return;
}

if (args.Length >= 3 && args[0] == "--patch-hud-and-weapon-labels")
{
    PatchHudAndWeaponLabels(args[1], args[2]);
    return;
}

if (args.Length >= 3 && args[0] == "--patch-story-update-labels")
{
    PatchStoryUpdateLabels(args[1], args[2]);
    return;
}

if (args.Length >= 3 && args[0] == "--patch-account-ui")
{
    PatchAccountUi(args[1], args[2]);
    return;
}

if (args.Length >= 3 && args[0] == "--patch-quality-label-prefix")
{
    PatchQualityLabelPrefix(args[1], args[2]);
    return;
}

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: dll_string_patcher <input-dll> <output-dll>");
    Environment.ExitCode = 2;
    return;
}

var input = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);

var replacements = new Dictionary<string, string>
{
    ["Continue"] = "Продолжить",
    ["Pages"] = "Страницы",
    ["Supplies"] = "Припасы",
    ["Hull"] = "Корпус",
    ["Fuel"] = "Топливо",
    ["Hold"] = "Трюм",
    ["<b>Supplies:</b> "] = "<b>Припасы:</b> ",
    ["<b>Hull:</b> "] = "<b>Корпус:</b> ",
    ["Our fuel reserves are empty."] = "Запасы топлива иссякли.",
    ["We're out of fuel!"] = "Топливо закончилось!",
    ["No supplies. The crew must go hungry!"] = "Припасов нет. Команда будет голодать!",
    ["The Witherweed damages our hull!"] = "Иссушайка повреждает корпус!",
    ["The hull does not need repairing."] = "Корпус не требует ремонта.",
    ["The import failed, continue with your saved game or try again later."] = "Импорт не удался. Продолжайте сохраненную игру или попробуйте позже.",
    ["We must dock before we change the ship's equipment."] = "Для смены снаряжения нужно пришвартоваться.",
    ["No resale value in this shipyard."] = "На этой верфи нет выкупной цены.",
    ["Transform Ship"] = "Переоборудовать судно",
};

var resolver = new DefaultAssemblyResolver();
var inputDir = Path.GetDirectoryName(input);
if (!string.IsNullOrWhiteSpace(inputDir))
{
    resolver.AddSearchDirectory(inputDir);
}

var parameters = new ReaderParameters
{
    ReadWrite = false,
    AssemblyResolver = resolver,
};

var module = ModuleDefinition.ReadModule(input, parameters);
var hitCounts = replacements.Keys.ToDictionary(k => k, _ => 0);
var replacementCount = 0;

foreach (var type in GetTypes(module.Types))
{
    foreach (var method in type.Methods)
    {
        if (!method.HasBody)
        {
            continue;
        }

        foreach (var instruction in method.Body.Instructions)
        {
            if (instruction.OpCode.Code != Code.Ldstr || instruction.Operand is not string value)
            {
                continue;
            }

            if (!replacements.TryGetValue(value, out var replacement))
            {
                continue;
            }

            instruction.Operand = replacement;
            hitCounts[value]++;
            replacementCount++;
        }
    }
}

Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
module.Write(output);

Console.WriteLine($"Input: {input}");
Console.WriteLine($"Output: {output}");
Console.WriteLine($"Replacements: {replacementCount}");
foreach (var pair in hitCounts.Where(p => p.Value > 0).OrderBy(p => p.Key))
{
    Console.WriteLine($"{pair.Key} -> {replacements[pair.Key]} ({pair.Value})");
}

static IEnumerable<TypeDefinition> GetTypes(IEnumerable<TypeDefinition> roots)
{
    foreach (var type in roots)
    {
        yield return type;
        foreach (var nested in GetTypes(type.NestedTypes))
        {
            yield return nested;
        }
    }
}

static void FindStrings(string dllPath, string[] needles)
{
    var input = Path.GetFullPath(dllPath);
    var resolver = new DefaultAssemblyResolver();
    var inputDir = Path.GetDirectoryName(input);
    if (!string.IsNullOrWhiteSpace(inputDir))
    {
        resolver.AddSearchDirectory(inputDir);
    }

    var module = ModuleDefinition.ReadModule(input, new ReaderParameters
    {
        ReadWrite = false,
        AssemblyResolver = resolver,
    });

    foreach (var type in GetTypes(module.Types))
    {
        foreach (var method in type.Methods)
        {
            if (!method.HasBody)
            {
                continue;
            }

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode.Code != Code.Ldstr || instruction.Operand is not string value)
                {
                    continue;
                }

                if (needles.Length > 0 && !needles.Any(n => value.Contains(n, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                Console.WriteLine($"{type.FullName}::{method.Name} | {value}");
            }
        }
    }
}

static void DumpType(string dllPath, string typeName)
{
    var input = Path.GetFullPath(dllPath);
    var resolver = new DefaultAssemblyResolver();
    var inputDir = Path.GetDirectoryName(input);
    if (!string.IsNullOrWhiteSpace(inputDir))
    {
        resolver.AddSearchDirectory(inputDir);
    }

    var module = ModuleDefinition.ReadModule(input, new ReaderParameters
    {
        ReadWrite = false,
        AssemblyResolver = resolver,
    });

    foreach (var type in module.Types.SelectMany(WalkTypes))
    {
        if (!type.FullName.Contains(typeName, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        Console.WriteLine($"TYPE {type.FullName}");
        foreach (var method in type.Methods.Where(m => m.HasBody))
        {
            Console.WriteLine($"METHOD {method.FullName}");
            foreach (var instruction in method.Body.Instructions)
            {
                Console.WriteLine($"  {instruction}");
            }
        }
    }
}

static IEnumerable<TypeDefinition> WalkTypes(TypeDefinition type)
{
    yield return type;
    foreach (var nested in type.NestedTypes)
    {
        foreach (var nestedType in WalkTypes(nested))
        {
            yield return nestedType;
        }
    }
}

static void ListUiEnglishStrings(string dllPath)
{
    var input = Path.GetFullPath(dllPath);
    var resolver = new DefaultAssemblyResolver();
    var inputDir = Path.GetDirectoryName(input);
    if (!string.IsNullOrWhiteSpace(inputDir))
    {
        resolver.AddSearchDirectory(inputDir);
    }

    var module = ModuleDefinition.ReadModule(input, new ReaderParameters
    {
        ReadWrite = false,
        AssemblyResolver = resolver,
    });

    var interestingTypeParts = new[]
    {
        ".UI.",
        ".Menus.",
        "MenuProvider",
        "LoadingProvider",
        "JournalProvider",
        "ShipyardProvider",
        "ExchangeProvider",
        "HoldProvider",
        "OfficersProvider",
        "TutorialProvider",
    };

    foreach (var type in GetTypes(module.Types))
    {
        if (!interestingTypeParts.Any(part => type.FullName.Contains(part, StringComparison.Ordinal)))
        {
            continue;
        }

        foreach (var method in type.Methods)
        {
            if (!method.HasBody)
            {
                continue;
            }

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode.Code != Code.Ldstr || instruction.Operand is not string value)
                {
                    continue;
                }

                if (!LooksEnglish(value))
                {
                    continue;
                }

                Console.WriteLine($"{type.FullName}::{method.Name}\t{value.Replace("\r", "\\r").Replace("\n", "\\n")}");
            }
        }
    }
}

static bool LooksEnglish(string value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return false;
    }

    if (!value.Any(c => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z'))
    {
        return false;
    }

    if (value.Length > 220)
    {
        return false;
    }

    return true;
}

static void PatchCategoryDisplayNames(string inputPath, string outputPath)
{
    var input = Path.GetFullPath(inputPath);
    var output = Path.GetFullPath(outputPath);
    var resolver = new DefaultAssemblyResolver();
    var inputDir = Path.GetDirectoryName(input);
    if (!string.IsNullOrWhiteSpace(inputDir))
    {
        resolver.AddSearchDirectory(inputDir);
    }

    var module = ModuleDefinition.ReadModule(input, new ReaderParameters
    {
        ReadWrite = false,
        AssemblyResolver = resolver,
    });

    var type = module.Types
        .SelectMany(WalkTypes)
        .Single(t => t.FullName == "FailBetter.Core.ExtensionMethods.EnumExtensionMethods.CategoryExtensionMethods");
    var method = type.Methods.Single(m => m.Name == "DisplayNameSunless" && m.Parameters.Count == 1);
    var replacements = new Dictionary<string, string>
    {
        ["Cargo"] = "Груз",
        ["Curiosities"] = "Диковинки",
        ["Stories"] = "Истории",
        ["Accomplishments"] = "Достижения",
        ["Circumstances"] = "Обстоятельства",
    };
    var counts = replacements.Keys.ToDictionary(key => key, _ => 0);

    foreach (var instruction in method.Body.Instructions)
    {
        if (instruction.OpCode.Code != Code.Ldstr || instruction.Operand is not string value)
            continue;
        if (!replacements.TryGetValue(value, out var replacement))
            continue;
        instruction.Operand = replacement;
        counts[value]++;
    }

    var unexpected = counts.Where(pair => pair.Value != 1).ToArray();
    if (unexpected.Length != 0)
        throw new InvalidDataException("Unexpected DisplayNameSunless layout: " + string.Join(", ", unexpected.Select(pair => $"{pair.Key}={pair.Value}")));

    Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
    module.Write(output);
    foreach (var pair in replacements)
        Console.WriteLine($"{pair.Key} -> {pair.Value}");
}

static void RestoreMainMenuBackLookup(string inputPath, string outputPath)
{
    var input = Path.GetFullPath(inputPath);
    var output = Path.GetFullPath(outputPath);
    var resolver = new DefaultAssemblyResolver();
    var inputDir = Path.GetDirectoryName(input);
    if (!string.IsNullOrWhiteSpace(inputDir))
    {
        resolver.AddSearchDirectory(inputDir);
    }

    var module = ModuleDefinition.ReadModule(input, new ReaderParameters
    {
        ReadWrite = false,
        AssemblyResolver = resolver,
    });

    var type = module.Types
        .SelectMany(WalkTypes)
        .Single(t => t.FullName == "Sunless.Game.UI.Menus.MainMenu");
    var constructor = type.Methods.Single(m => m.Name == ".ctor");
    var replacements = 0;

    foreach (var instruction in constructor.Body.Instructions)
    {
        if (instruction.OpCode.Code == Code.Ldstr && instruction.Operand is string value && value == "Назад")
        {
            instruction.Operand = "Back";
            replacements++;
        }
    }

    if (replacements != 2)
        throw new InvalidDataException($"Expected two MainMenu Back lookups, replaced {replacements}.");

    Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
    module.Write(output);
    Console.WriteLine("Restored two internal MainMenu lookups: Назад -> Back");
}

static void AuditMenuLookups(string currentPath, string baselinePath)
{
    using var current = ModuleDefinition.ReadModule(Path.GetFullPath(currentPath));
    using var baseline = ModuleDefinition.ReadModule(Path.GetFullPath(baselinePath));
    var currentLookups = CollectMenuLookups(current);
    var baselineLookups = CollectMenuLookups(baseline);
    var changed = 0;

    foreach (var pair in currentLookups.OrderBy(pair => pair.Key))
    {
        if (!baselineLookups.TryGetValue(pair.Key, out var baselineValue))
        {
            Console.WriteLine($"UNMATCHED\t{pair.Key}\t{pair.Value}");
            continue;
        }
        if (pair.Value == baselineValue)
            continue;
        changed++;
        Console.WriteLine($"CHANGED\t{pair.Key}\t{baselineValue}\t=>\t{pair.Value}");
    }

    Console.WriteLine($"Menu lookup calls: current={currentLookups.Count}, baseline={baselineLookups.Count}, changed={changed}");
}

static void AuditUiLookups(string currentPath, string baselinePath)
{
    using var current = ModuleDefinition.ReadModule(Path.GetFullPath(currentPath));
    using var baseline = ModuleDefinition.ReadModule(Path.GetFullPath(baselinePath));
    var currentLookups = CollectLookups(current, type => type.FullName.Contains(".UI.", StringComparison.Ordinal));
    var baselineLookups = CollectLookups(baseline, type => type.FullName.Contains(".UI.", StringComparison.Ordinal));
    var changed = 0;

    foreach (var pair in currentLookups.OrderBy(pair => pair.Key))
    {
        if (!baselineLookups.TryGetValue(pair.Key, out var baselineValue))
        {
            Console.WriteLine($"UNMATCHED\t{pair.Key}\t{pair.Value}");
            continue;
        }
        if (pair.Value == baselineValue)
            continue;
        changed++;
        Console.WriteLine($"CHANGED\t{pair.Key}\t{baselineValue}\t=>\t{pair.Value}");
    }

    Console.WriteLine($"UI lookup calls: current={currentLookups.Count}, baseline={baselineLookups.Count}, changed={changed}");
}

static Dictionary<string, string> CollectMenuLookups(ModuleDefinition module)
{
    return CollectLookups(module, type => type.FullName.Contains(".UI.Menus.", StringComparison.Ordinal));
}

static Dictionary<string, string> CollectLookups(ModuleDefinition module, Func<TypeDefinition, bool> typeFilter)
{
    var result = new Dictionary<string, string>();
    foreach (var type in GetTypes(module.Types).Where(typeFilter))
    {
        foreach (var method in type.Methods.Where(method => method.HasBody))
        {
            var ordinal = 0;
            var instructions = method.Body.Instructions;
            for (var index = 0; index < instructions.Count; index++)
            {
                var instruction = instructions[index];
                if (instruction.OpCode.Code is not (Code.Call or Code.Callvirt) || instruction.Operand is not MethodReference called)
                    continue;
                if (!called.Name.Contains("Find", StringComparison.Ordinal) || !called.Parameters.Any(parameter => parameter.ParameterType.FullName == "System.String"))
                    continue;

                var literal = "<non-literal>";
                for (var back = index - 1; back >= 0 && back >= index - 8; back--)
                {
                    if (instructions[back].OpCode.Code == Code.Ldstr && instructions[back].Operand is string value)
                    {
                        literal = value;
                        break;
                    }
                    if (instructions[back].OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj)
                        break;
                }

                var key = $"{type.FullName}::{method.FullName}|{called.FullName}|{ordinal++}";
                result[key] = literal;
            }
        }
    }
    return result;
}

static void RestoreAlertDialogContinueLookup(string inputPath, string outputPath)
{
    var input = Path.GetFullPath(inputPath);
    var output = Path.GetFullPath(outputPath);
    using var module = ModuleDefinition.ReadModule(input);
    var type = module.Types.SelectMany(WalkTypes)
        .Single(t => t.FullName == "Sunless.Game.UI.Components.AlertDialog");
    var constructor = type.Methods.Single(m => m.Name == ".ctor");
    var replacements = 0;

    foreach (var instruction in constructor.Body.Instructions)
    {
        if (instruction.OpCode.Code == Code.Ldstr && instruction.Operand is string value && value == "Продолжить")
        {
            instruction.Operand = "Continue";
            replacements++;
        }
    }

    if (replacements != 1)
        throw new InvalidDataException($"Expected one AlertDialog Continue lookup, replaced {replacements}.");

    Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
    module.Write(output);
    Console.WriteLine("Restored AlertDialog internal lookup: Продолжить -> Continue");
}

static void PatchQualityChangeMessages(string inputPath, string outputPath)
{
    var input = Path.GetFullPath(inputPath);
    var output = Path.GetFullPath(outputPath);
    using var module = ModuleDefinition.ReadModule(input);
    var replacements = new Dictionary<(string Type, string From), string>
    {
        [("FailBetter.Core.Result.QualityChangeMessages.QualityExplicitlySetMessage", "You now have ")] = "Теперь у вас есть ",
        [("FailBetter.Core.Result.QualityChangeMessages.QualityExplicitlySetMessage", " of this: '")] = " ед.: '",
        [("FailBetter.Core.Result.QualityChangeMessages.QualityExplicitlySetMessage", "You no longer have any of this: '")] = "У вас больше нет: '",
        [("FailBetter.Core.Result.QualityChangeMessages.StandardQualityChangeMessage", "You now have ")] = "Теперь у вас есть ",
    };
    var hitCounts = replacements.Keys.ToDictionary(key => key, _ => 0);

    foreach (var type in module.Types.SelectMany(WalkTypes))
    {
        foreach (var method in type.Methods.Where(method => method.HasBody))
        {
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode.Code != Code.Ldstr || instruction.Operand is not string value)
                    continue;
                var key = (type.FullName, value);
                if (!replacements.TryGetValue(key, out var translated))
                    continue;
                instruction.Operand = translated;
                hitCounts[key]++;
                Console.WriteLine($"{type.FullName}::{method.Name} | {value} -> {translated}");
            }
        }
    }

    var invalid = hitCounts.Where(pair => pair.Value != 1).ToList();
    if (invalid.Count > 0)
        throw new InvalidDataException("Unexpected quality-message match counts: " + string.Join(", ", invalid.Select(pair => $"{pair.Key}={pair.Value}")));

    Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
    module.Write(output);
    Console.WriteLine($"Patched {hitCounts.Count} quality-change message fragments.");
}

static void PatchResultMessages(string inputPath, string outputPath)
{
    var input = Path.GetFullPath(inputPath);
    var output = Path.GetFullPath(outputPath);
    using var module = ModuleDefinition.ReadModule(input);
    var replacements = new Dictionary<(string Type, string From), string>
    {
        [("FailBetter.Core.Result.DifficultyRollSuccessMessage", "You were fortunate!")] = "Повезло!",
        [("FailBetter.Core.Result.DifficultyRollSuccessMessage", "(Risky challenges mean you learn more.)")] = "(Сложные испытания дают больше опыта.)",
        [("FailBetter.Core.Result.DifficultyRollSuccessMessage", "(Simple challenges mean you don't learn so much.)")] = "(Простые испытания дают меньше опыта.)",
        [("FailBetter.Core.Result.DifficultyRollSuccessMessage", "(But this was a second chance, so you'd already learnt from it.)")] = "(Но это была вторая попытка, поэтому опыт уже получен.)",
        [("FailBetter.Core.Result.DifficultyRollSuccessMessage", "You succeeded in a {0} challenge! {1} {2}")] = "Испытание характеристики «{0}» пройдено! {1} {2}",

        [("FailBetter.Core.Result.DifficultyRollFailureMessage", "You were unlucky. Better luck next time...")] = "Не повезло. Возможно, в следующий раз...",
        [("FailBetter.Core.Result.DifficultyRollFailureMessage", " failed in a challenge! Try again and you may have better luck...")] = " — недостаточный уровень для испытания. Попробуйте ещё раз...",
        [("FailBetter.Core.Result.DifficultyRollFailureMessage", "(When you try a challenge that's difficult for you, you learn more even when you fail) ")] = "(Сложные испытания дают опыт даже при неудаче.) ",
        [("FailBetter.Core.Result.DifficultyRollFailureMessage", "(This challenge was old territory for you - you won't learn so much.) ")] = "(Знакомое испытание даст меньше опыта.) ",
        [("FailBetter.Core.Result.DifficultyRollFailureMessage", "(This was a second chance, so you'd already learnt from it.)")] = "(Это была вторая попытка, поэтому опыт уже получен.)",
        [("FailBetter.Core.Result.DifficultyRollFailureMessage", " failed in a challenge! ")] = " — недостаточный уровень для испытания! ",

        [("FailBetter.Core.Result.QualityChangeMessages.StandardQualityChangeMessage", "lost ")] = "потеряли ",
        [("FailBetter.Core.Result.QualityChangeMessages.StandardQualityChangeMessage", "gained ")] = "получили ",
        [("FailBetter.Core.Result.QualityChangeMessages.StandardQualityChangeMessage", "You've {0} x {1} (new total {2}).")] = "Вы {0} ед. «{1}» (теперь: {2}).",

        [("FailBetter.Core.Result.QualityChangeMessages.QualityExplicitlySetMessage", "You have a new Accomplishment...")] = "Новое достижение: ",
        [("FailBetter.Core.Result.QualityChangeMessages.QualityExplicitlySetMessage", "An occurrence! Your '")] = "Событие! Характеристика «",
        [("FailBetter.Core.Result.QualityChangeMessages.QualityExplicitlySetMessage", "' Quality is now ")] = "» теперь: ",
        [("FailBetter.Core.Result.QualityChangeMessages.QualityExplicitlySetMessage", "' has been reset: a conclusion, or a new beginning?")] = "' сброшено: конец или новое начало?",
        [("FailBetter.Core.Result.QualityChangeMessages.QualityExplicitlySetMessage", "Your '")] = "Ваша характеристика «",
        [("FailBetter.Core.Result.QualityChangeMessages.QualityExplicitlySetMessage", "' Quality has gone!")] = "» утрачена!",
        [("FailBetter.Core.Result.QualityChangeMessages.QualityExplicitlySetMessage", "[This is a metaquality! It will appear on your user profile, and may unlock new starting options in other worlds.]")] = "[Это метахарактеристика: она появится в профиле и может открыть новые начальные возможности в других мирах.]",

        [("FailBetter.Core.Help.ChallengeHelpTooltip", "The higher the Quality, the higher the chance of success.")] = "Чем выше характеристика, тем больше шанс на успех.",
    };
    var hitCounts = replacements.Keys.ToDictionary(key => key, _ => 0);

    foreach (var type in module.Types.SelectMany(WalkTypes))
    {
        foreach (var method in type.Methods.Where(method => method.HasBody))
        {
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode.Code != Code.Ldstr || instruction.Operand is not string value)
                    continue;
                var key = (type.FullName, value);
                if (!replacements.TryGetValue(key, out var translated))
                    continue;
                instruction.Operand = translated;
                hitCounts[key]++;
                Console.WriteLine($"{type.FullName}::{method.Name} | {value} -> {translated}");
            }
        }
    }

    var invalid = hitCounts.Where(pair => pair.Value != 1).ToList();
    if (invalid.Count > 0)
        throw new InvalidDataException("Unexpected result-message match counts: " + string.Join(", ", invalid.Select(pair => $"{pair.Key}={pair.Value}")));

    Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
    module.Write(output);
    Console.WriteLine($"Patched {hitCounts.Count} result-message fragments.");
}

static void PatchLogDate(string inputPath, string outputPath)
{
    var input = Path.GetFullPath(inputPath);
    var output = Path.GetFullPath(outputPath);
    var resolver = new DefaultAssemblyResolver();
    var inputDir = Path.GetDirectoryName(input);
    if (!string.IsNullOrWhiteSpace(inputDir))
        resolver.AddSearchDirectory(inputDir);

    using var module = ModuleDefinition.ReadModule(input, new ReaderParameters { AssemblyResolver = resolver });
    var type = module.Types.SelectMany(WalkTypes)
        .Single(t => t.FullName == "Sunless.Game.Formatters.LogEntries");
    var method = type.Methods.Single(m => m.Name == "GetInGameTime");

    var gameProviderCall = method.Body.Instructions
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .Single(reference => reference.FullName.Contains("GameProvider::get_Instance", StringComparison.Ordinal));
    var currentCharacterCall = method.Body.Instructions
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .Single(reference => reference.FullName.Contains("GameProvider::get_CurrentCharacter", StringComparison.Ordinal));
    var inGameDateCall = method.Body.Instructions
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .Single(reference => reference.FullName.Contains("SunlessCharacter::get_InGameDate", StringComparison.Ordinal));

    var cultureInfo = module.GetTypeReferences().Single(reference => reference.FullName == "System.Globalization.CultureInfo").Resolve();
    var getCultureInfo = module.ImportReference(cultureInfo.Methods.Single(candidate =>
        candidate.Name == "GetCultureInfo" &&
        candidate.IsStatic &&
        candidate.Parameters.Count == 1 &&
        candidate.Parameters[0].ParameterType.FullName == "System.String"));
    var dateTimeReference = module.ImportReference(inGameDateCall.ReturnType);
    var dateTime = dateTimeReference.Resolve();
    var dateToString = module.ImportReference(dateTime.Methods.Single(candidate =>
        candidate.Name == "ToString" &&
        candidate.Parameters.Count == 2 &&
        candidate.Parameters[0].ParameterType.FullName == "System.String" &&
        candidate.Parameters[1].ParameterType.FullName == "System.IFormatProvider"));

    method.Body.Instructions.Clear();
    method.Body.ExceptionHandlers.Clear();
    method.Body.Variables.Clear();
    method.Body.InitLocals = true;
    method.Body.Variables.Add(new VariableDefinition(dateTimeReference));
    var il = method.Body.GetILProcessor();
    il.Append(Instruction.Create(OpCodes.Call, gameProviderCall));
    il.Append(Instruction.Create(OpCodes.Callvirt, currentCharacterCall));
    il.Append(Instruction.Create(OpCodes.Callvirt, inGameDateCall));
    il.Append(Instruction.Create(OpCodes.Stloc_0));
    il.Append(Instruction.Create(OpCodes.Ldloca_S, method.Body.Variables[0]));
    il.Append(Instruction.Create(OpCodes.Ldstr, "d MMMM yyyy 'г.'"));
    il.Append(Instruction.Create(OpCodes.Ldstr, "ru-RU"));
    il.Append(Instruction.Create(OpCodes.Call, getCultureInfo));
    il.Append(Instruction.Create(OpCodes.Call, dateToString));
    il.Append(Instruction.Create(OpCodes.Ret));

    Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
    module.Write(output);
    Console.WriteLine("Patched log dates to fixed ru-RU format: d MMMM yyyy 'г.'");
}

static void PatchDialogs(string inputPath, string outputPath)
{
    var input = Path.GetFullPath(inputPath);
    var output = Path.GetFullPath(outputPath);
    var resolver = new DefaultAssemblyResolver();
    var inputDir = Path.GetDirectoryName(input);
    if (!string.IsNullOrWhiteSpace(inputDir))
    {
        resolver.AddSearchDirectory(inputDir);
    }

    var module = ModuleDefinition.ReadModule(input, new ReaderParameters
    {
        ReadWrite = false,
        AssemblyResolver = resolver,
    });

    var allowedMethods = new HashSet<string>
    {
        "Sunless.Game.UI.Components.ConfirmDialog::.ctor",
        "Sunless.Game.UI.Components.Sunless.Game.UI.Components.UserInput::.ctor",
        "Sunless.Game.UI.Components.AlertDialog::.ctor",
    };

    var replacements = new Dictionary<string, string>
    {
        ["Yes"] = "Да",
        ["No"] = "Нет",
        ["Continue"] = "Продолжить",
    };

    var replacementCount = 0;
    foreach (var type in GetTypes(module.Types))
    {
        foreach (var method in type.Methods)
        {
            if (!method.HasBody || !allowedMethods.Contains($"{type.FullName}::{method.Name}"))
            {
                continue;
            }

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode.Code != Code.Ldstr || instruction.Operand is not string value)
                {
                    continue;
                }

                if (!replacements.TryGetValue(value, out var replacement))
                {
                    continue;
                }

                instruction.Operand = replacement;
                replacementCount++;
                Console.WriteLine($"{type.FullName}::{method.Name} | {value} -> {replacement}");
            }
        }
    }

    Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
    module.Write(output);
    Console.WriteLine($"Replacements: {replacementCount}");
}

static void PatchVisualDialogLabels(string inputPath, string outputPath)
{
    var input = Path.GetFullPath(inputPath);
    var output = Path.GetFullPath(outputPath);
    var resolver = new DefaultAssemblyResolver();
    var inputDir = Path.GetDirectoryName(input);
    if (!string.IsNullOrWhiteSpace(inputDir))
    {
        resolver.AddSearchDirectory(inputDir);
    }

    var module = ModuleDefinition.ReadModule(input, new ReaderParameters
    {
        ReadWrite = false,
        AssemblyResolver = resolver,
    });

    var confirmDialog = module.Types.First(t => t.FullName == "Sunless.Game.UI.Components.ConfirmDialog");
    var constructors = confirmDialog.Methods.Where(m => m.Name == ".ctor").ToList();
    var simpleCtor = constructors.First(m =>
        m.Parameters.Count == 5 &&
        m.Parameters[1].ParameterType.FullName == "System.Action" &&
        m.Parameters[2].ParameterType.FullName == "System.Action");

    var buttonType = module.GetTypeReferences().First(t => t.FullName == "UnityEngine.UI.Button");
    var textType = module.GetTypeReferences().First(t => t.FullName == "UnityEngine.UI.Text");
    var confirmField = confirmDialog.Fields.First(f => f.Name == "_confirmButton");
    var cancelField = confirmDialog.Fields.First(f => f.Name == "_cancelButton");

    var getTextComponent = constructors
        .SelectMany(m => m.Body?.Instructions ?? Enumerable.Empty<Instruction>())
        .Select(i => i.Operand)
        .OfType<GenericInstanceMethod>()
        .First(m =>
            m.Name == "GetComponentInChildren" &&
            m.GenericArguments.Count == 1 &&
            m.GenericArguments[0].FullName == textType.FullName);

    var setText = module.GetTypeReferences()
        .First(t => t.FullName == "UnityEngine.UI.Text")
        .Resolve()
        .Methods
        .First(m => m.Name == "set_text" && m.Parameters.Count == 1);
    var setTextRef = module.ImportReference(setText);

    if (ContainsLdstr(simpleCtor, "Да") && ContainsLdstr(simpleCtor, "Нет"))
    {
        Console.WriteLine("ConfirmDialog visual labels already patched.");
    }
    else
    {
        var il = simpleCtor.Body.GetILProcessor();
        var insertAfter = simpleCtor.Body.Instructions.First(i =>
            i.OpCode.Code == Code.Stfld &&
            i.Operand is FieldReference field &&
            field.Name == "_cancelButton");
        var target = insertAfter.Next;

        foreach (var instruction in BuildSetButtonTextInstructions(confirmField, getTextComponent, setTextRef, "Да")
                     .Concat(BuildSetButtonTextInstructions(cancelField, getTextComponent, setTextRef, "Нет")))
        {
            il.InsertBefore(target, instruction);
        }

        Console.WriteLine("Patched ConfirmDialog visual labels: Yes/No -> Да/Нет");
    }

    var alertDialog = module.Types.First(t => t.FullName == "Sunless.Game.UI.Components.AlertDialog");
    var alertCtor = alertDialog.Methods.First(m => m.Name == ".ctor");
    var buttonLabelField = alertDialog.Fields.First(f => f.Name == "_buttonlabel");
    if (ContainsLdstr(alertCtor, "Продолжить"))
    {
        Console.WriteLine("AlertDialog visual label already patched.");
    }
    else
    {
        var il = alertCtor.Body.GetILProcessor();
        var insertAfter = alertCtor.Body.Instructions.First(i =>
            i.OpCode.Code == Code.Stfld &&
            i.Operand is FieldReference field &&
            field.Name == "_buttonlabel");
        var target = insertAfter.Next;
        il.InsertBefore(target, Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(target, Instruction.Create(OpCodes.Ldfld, buttonLabelField));
        il.InsertBefore(target, Instruction.Create(OpCodes.Ldstr, "Продолжить"));
        il.InsertBefore(target, Instruction.Create(OpCodes.Callvirt, setTextRef));
        Console.WriteLine("Patched AlertDialog visual label: Continue -> Продолжить");
    }

    Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
    module.Write(output);
}

static IEnumerable<Instruction> BuildSetButtonTextInstructions(
    FieldReference buttonField,
    MethodReference getTextComponent,
    MethodReference setText,
    string text)
{
    yield return Instruction.Create(OpCodes.Ldarg_0);
    yield return Instruction.Create(OpCodes.Ldfld, buttonField);
    yield return Instruction.Create(OpCodes.Callvirt, getTextComponent);
    yield return Instruction.Create(OpCodes.Ldstr, text);
    yield return Instruction.Create(OpCodes.Callvirt, setText);
}

static bool ContainsLdstr(MethodDefinition method, string value)
{
    return method.HasBody && method.Body.Instructions.Any(i =>
        i.OpCode.Code == Code.Ldstr &&
        i.Operand is string operand &&
        operand == value);
}

static void PatchMainMenuLabels(string inputPath, string outputPath)
{
    var input = Path.GetFullPath(inputPath);
    var output = Path.GetFullPath(outputPath);
    var resolver = new DefaultAssemblyResolver();
    var inputDir = Path.GetDirectoryName(input);
    if (!string.IsNullOrWhiteSpace(inputDir))
    {
        resolver.AddSearchDirectory(inputDir);
    }

    var module = ModuleDefinition.ReadModule(input, new ReaderParameters
    {
        ReadWrite = false,
        AssemblyResolver = resolver,
    });

    var buttonType = module.Types.First(t => t.FullName == "Sunless.Game.UI.Menus.MainMenuButton");
    var ctor = buttonType.Methods.First(m => m.Name == ".ctor" && m.Parameters.Count == 4);
    if (ContainsLdstr(ctor, "Сохранить вручную"))
    {
        Console.WriteLine("MainMenuButton visual labels already patched.");
        Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
        module.Write(output);
        return;
    }

    var setLabelText = buttonType.Methods.First(m => m.Name == "set_LabelText");
    var setLabelTextRef = module.ImportReference(setLabelText);
    var stringEquality = module.ImportReference(
        typeof(string).GetMethod("op_Equality", new[] { typeof(string), typeof(string) })!);

    var il = ctor.Body.GetILProcessor();
    var existingSet = ctor.Body.Instructions.First(i =>
        (i.OpCode.Code == Code.Call || i.OpCode.Code == Code.Callvirt) &&
        i.Operand is MethodReference method &&
        method.Name == "set_LabelText");
    var insertAt = existingSet.Next;

    var skip = Instruction.Create(OpCodes.Nop);
    var injected = new[]
    {
        Instruction.Create(OpCodes.Ldarg_3),
        Instruction.Create(OpCodes.Ldstr, "Manual Save"),
        Instruction.Create(OpCodes.Call, stringEquality),
        Instruction.Create(OpCodes.Brfalse_S, skip),
        Instruction.Create(OpCodes.Ldarg_0),
        Instruction.Create(OpCodes.Ldstr, "Сохранить вручную"),
        Instruction.Create(OpCodes.Call, setLabelTextRef),
        skip,
    };

    foreach (var instruction in injected)
    {
        il.InsertBefore(insertAt, instruction);
    }

    Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
    module.Write(output);
    Console.WriteLine("Patched MainMenuButton visual label: Manual Save -> Сохранить вручную");
}

static void PatchSafeUiTexts(string inputPath, string outputPath)
{
    var input = Path.GetFullPath(inputPath);
    var output = Path.GetFullPath(outputPath);
    var resolver = new DefaultAssemblyResolver();
    var inputDir = Path.GetDirectoryName(input);
    if (!string.IsNullOrWhiteSpace(inputDir))
    {
        resolver.AddSearchDirectory(inputDir);
    }

    var module = ModuleDefinition.ReadModule(input, new ReaderParameters
    {
        ReadWrite = false,
        AssemblyResolver = resolver,
    });

    var replacements = new Dictionary<(string Context, string Old), string>
    {
        [("Sunless.Game.UI.Menus.LatestNews/UpdateButtonValues::.cctor", "(Cannot connect)")] = "(Нет подключения)",
        [("Sunless.Game.UI.Menus.LatestNews/UpdateButtonValues::.cctor", "Checking for update...")] = "Проверка обновлений...",
        [("Sunless.Game.UI.Menus.LatestNews/UpdateButtonValues::.cctor", "Need latest game version")] = "Требуется последняя версия игры",
        [("Sunless.Game.UI.Menus.LatestNews/UpdateButtonValues::.cctor", "Connect to server")] = "Подключиться к серверу",
        [("Sunless.Game.UI.Menus.LatestNews::SetPanelContents", "Connect to our server to retrieve latest news and check for content updates.")] = "Подключитесь к серверу, чтобы получить последние новости и проверить обновления контента.",
        [("Sunless.Game.UI.Menus.LatestNews::<SetPanelContents>b__16_0", "Cannot connect to the server to retrieve latest news.")] = "Не удалось подключиться к серверу для получения новостей.",

        [("Sunless.Game.UI.Menus.MainMenu/UpdateButtonValues::.cctor", "(Cannot connect)")] = "(Нет подключения)",
        [("Sunless.Game.UI.Menus.MainMenu/UpdateButtonValues::.cctor", "Checking for update...")] = "Проверка обновлений...",
        [("Sunless.Game.UI.Menus.MainMenu/UpdateButtonValues::.cctor", "Need latest game version")] = "Требуется последняя версия игры",
        [("Sunless.Game.UI.Menus.MainMenu/UpdateButtonValues::.cctor", "New Stories Available!")] = "Доступны новые истории!",
        [("Sunless.Game.UI.Menus.MainMenu/UpdateButtonValues::.cctor", "All stories up to date!")] = "Все истории обновлены!",

        [("Sunless.Game.UI.Menus.KeymappingPanel::WarnAndRestoreDefaults", "Restore defaults")] = "Восстановить настройки",
        [("Sunless.Game.UI.Menus.KeymappingPanel::WarnAndRestoreDefaults", "Are you sure you want to restore the default control scheme?")] = "Восстановить схему управления по умолчанию?",
        [("Sunless.Game.UI.Menus.KeybindingOptionPanel::ListenForKey", "Bind key")] = "Назначить клавишу",
        [("Sunless.Game.UI.Menus.KeybindingOptionPanel::ListenForKey", "Press a new key for ")] = "Нажмите новую клавишу для ",
        [("Sunless.Game.UI.Menus.KeybindingOptionPanel::ListenForKey", "Cancel")] = "Отмена",
        [("Sunless.Game.UI.Menus.KeybindingOptionPanel::ListenForAlt", "Bind key")] = "Назначить клавишу",
        [("Sunless.Game.UI.Menus.KeybindingOptionPanel::ListenForAlt", "Press a new key for ")] = "Нажмите новую клавишу для ",
        [("Sunless.Game.UI.Menus.KeybindingOptionPanel::ListenForAlt", "Cancel")] = "Отмена",

        [("Sunless.Game.UI.Menus.Options.AccountOptionsPanel::.ctor", "<b>Username:</b> (authentication required)")] = "<b>Имя пользователя:</b> (требуется авторизация)",
        [("Sunless.Game.UI.Menus.Options.AccountOptionsPanel::.ctor", "<b>Username:</b> ")] = "<b>Имя пользователя:</b> ",
        [("Sunless.Game.UI.Menus.Options.AccountOptionsPanel::.ctor", "Quit to the Title Screen before attempting to Authenticate.")] = "Перед авторизацией выйдите на титульный экран.",
        [("Sunless.Game.UI.Menus.Options.AccountOptionsPanel::ReAuthenticate", "Currently Playing")] = "Игра запущена",
        [("Sunless.Game.UI.Menus.Options.AccountOptionsPanel::ReAuthenticate", "Quit to the Title Screen before attempting to Authenticate.")] = "Перед авторизацией выйдите на титульный экран.",
        [("Sunless.Game.UI.Menus.Options.AccountOptionsPanel::ReAuthenticate", "Outdated Version")] = "Устаревшая версия",
        [("Sunless.Game.UI.Menus.Options.AccountOptionsPanel::ReAuthenticate", "Please update to the latest version of the game.")] = "Обновите игру до последней версии.",

        [("Sunless.Game.UI.Components.AuthPanel::get_IsValid", "enter password")] = "введите пароль",
        [("Sunless.Game.UI.Components.AuthPanel::get_IsValid", "enter a valid email address")] = "введите корректный email",
        [("Sunless.Game.UI.Components.AuthPanel::.ctor", "If you have forgotten your password, enter your email address and click here to begin the password reset process")] = "Если вы забыли пароль, введите email и нажмите здесь, чтобы начать сброс пароля",
        [("Sunless.Game.UI.Components.AuthPanel::<.ctor>b__16_3", "You have been sent an email to begin the password reset process")] = "Вам отправлено письмо для начала сброса пароля",
        [("Sunless.Game.UI.Components.AuthPanel::SetLoginButtonState", "Login")] = "Войти",
        [("Sunless.Game.UI.Components.AuthPanel::SetLoginButtonState", "Connecting...")] = "Подключение...",

        [("Sunless.Game.UI.Components.RegisterPanel::get_IsValid", "enter a password")] = "введите пароль",
        [("Sunless.Game.UI.Components.RegisterPanel::get_IsValid", "invalid email")] = "некорректный email",

        [("Sunless.Game.ApplicationProviders.MenuProvider/<>c::<UpdateLocalData>b__26_0", "The import failed, continue with your saved game or try again later.")] = "Импорт не удался. Продолжайте сохраненную игру или попробуйте позже.",
        [("Sunless.Game.ApplicationProviders.MenuProvider::FatalErrorMessage", "Fatal Error")] = "Критическая ошибка",
        [("Sunless.Game.ApplicationProviders.MenuProvider::QualityMissingRecovery", "Fatal Error")] = "Критическая ошибка",
        [("Sunless.Game.ApplicationProviders.MenuProvider::WarningErrorMessage", "Warning")] = "Предупреждение",
    };

    var replacementCount = 0;
    foreach (var type in GetTypes(module.Types))
    {
        foreach (var method in type.Methods)
        {
            if (!method.HasBody)
            {
                continue;
            }

            var context = $"{type.FullName}::{method.Name}";
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode.Code != Code.Ldstr || instruction.Operand is not string value)
                {
                    continue;
                }

                if (!replacements.TryGetValue((context, value), out var replacement))
                {
                    continue;
                }

                instruction.Operand = replacement;
                replacementCount++;
                Console.WriteLine($"{context} | {value} -> {replacement}");
            }
        }
    }

    Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
    module.Write(output);
    Console.WriteLine($"Safe UI replacements: {replacementCount}");
}

static void PatchCombatShipUi(string inputPath, string outputPath)
{
    var input = Path.GetFullPath(inputPath);
    var output = Path.GetFullPath(outputPath);
    var resolver = new DefaultAssemblyResolver();
    var inputDir = Path.GetDirectoryName(input);
    if (!string.IsNullOrWhiteSpace(inputDir))
    {
        resolver.AddSearchDirectory(inputDir);
    }

    var module = ModuleDefinition.ReadModule(input, new ReaderParameters
    {
        ReadWrite = false,
        AssemblyResolver = resolver,
    });

    var replacements = new Dictionary<(string Context, string Old), string>
    {
        [("Sunless.Game.UI.Gazetteer.JettisonDialog::.ctor", " + Click for stacks of 10)")] = " + клик для стопок по 10)",
        [("Sunless.Game.UI.Gazetteer.JettisonDialog::UpdateJettisonPanels", "Hold")] = "Трюм",
        [("Sunless.Game.UI.Combat.HealthBar::Update", " (Crew: ")] = " (Команда: ",
    };

    var replacementCount = 0;
    foreach (var type in GetTypes(module.Types))
    {
        foreach (var method in type.Methods)
        {
            if (!method.HasBody)
            {
                continue;
            }

            var context = $"{type.FullName}::{method.Name}";
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode.Code != Code.Ldstr || instruction.Operand is not string value)
                {
                    continue;
                }

                if (!replacements.TryGetValue((context, value), out var replacement))
                {
                    continue;
                }

                instruction.Operand = replacement;
                replacementCount++;
                Console.WriteLine($"{context} | {value} -> {replacement}");
            }
        }
    }

    Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
    module.Write(output);
    Console.WriteLine($"Combat/ship UI replacements: {replacementCount}");
}

static void PatchItemTooltipTexts(string inputPath, string outputPath)
{
    var input = Path.GetFullPath(inputPath);
    var output = Path.GetFullPath(outputPath);
    using var module = ModuleDefinition.ReadModule(input);

    const string typeName = "Sunless.Game.Formatters.QIcons.QPossessionTooltipFormatter";
    var type = module.Types.SelectMany(WalkTypes).Single(candidate => candidate.FullName == typeName);
    var method = type.Methods.Single(candidate =>
        candidate.Name == "GetQualityPossessionTooltip" &&
        candidate.Parameters.Any(parameter => parameter.ParameterType.FullName == "Sunless.Game.Entities.Combat.CombatAttack"));

    var replacements = 0;
    foreach (var instruction in method.Body.Instructions)
    {
        if (instruction.OpCode.Code == Code.Ldstr && instruction.Operand is string value && value == " WEAPON")
        {
            instruction.Operand = " ОРУДИЕ";
            replacements++;
        }
    }

    if (replacements != 1)
        throw new InvalidDataException($"Expected one visible WEAPON label, replaced {replacements}.");

    Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
    module.Write(output);
    Console.WriteLine("Patched item tooltip label: WEAPON -> ОРУДИЕ");
}

static void PatchKeybindingUi(string inputPath, string outputPath)
{
    var input = Path.GetFullPath(inputPath);
    var output = Path.GetFullPath(outputPath);
    using var module = ModuleDefinition.ReadModule(input);

    var optionPanel = module.Types.SelectMany(WalkTypes)
        .Single(type => type.FullName == "Sunless.Game.UI.Menus.KeybindingOptionPanel");
    var translations = new Dictionary<string, string>
    {
        ["Forward"] = "Вперёд",
        ["Backward"] = "Назад",
        ["Left"] = "Влево",
        ["Right"] = "Вправо",
        ["Chart"] = "Карта",
        ["Gazetteer"] = "Журнал",
        ["Transform Ship"] = "Погружение/всплытие",
        ["Zeebat"] = "Запустить зи-бэт",
        ["Lights"] = "Прожектор",
        ["Repair"] = "Ремонт",
        ["Turbo"] = "Полный ход",
        ["Use"] = "Взаимодействие",
        ["Horn"] = "Гудок",
        ["PauseResume"] = "Пауза",
        ["Pause"] = "Пауза",
        ["Pause/Resume"] = "Пауза",
        ["Target"] = "Выбор цели",
        ["Scroll Up"] = "Прокрутка вверх",
        ["Scroll Down"] = "Прокрутка вниз",
        ["Stack Item"] = "Перенос стопки",
        ["Deck Weapon"] = "Палубное орудие",
        ["Forward Weapon"] = "Носовое орудие",
        ["Aft Weapon"] = "Кормовое орудие",
        ["Combat Item 1"] = "Снаряжение 1",
        ["Combat Item 2"] = "Снаряжение 2",
        ["Combat Item 3"] = "Снаряжение 3",
        ["Combat Item 4"] = "Снаряжение 4",
        ["Combat Item 5"] = "Снаряжение 5",
        ["Combat Item 6"] = "Снаряжение 6",
    };

    const string translatorName = "TranslateBindingNameForDisplay";
    if (optionPanel.Methods.Any(method => method.Name == translatorName))
        throw new InvalidDataException($"Method {translatorName} already exists.");

    var translator = new MethodDefinition(
        translatorName,
        MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
        module.TypeSystem.String);
    translator.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, module.TypeSystem.String));
    optionPanel.Methods.Add(translator);

    const string stringEqualitySignature = "System.Boolean System.String::op_Equality(System.String,System.String)";
    var stringEquality = module.Types.SelectMany(WalkTypes)
        .SelectMany(type => type.Methods)
        .Where(method => method.HasBody)
        .SelectMany(method => method.Body.Instructions)
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .First(reference => reference.FullName == stringEqualitySignature);
    translator.Body.MaxStackSize = 2;
    var translatorIl = translator.Body.GetILProcessor();
    foreach (var (from, to) in translations)
    {
        var next = Instruction.Create(OpCodes.Nop);
        translatorIl.Append(Instruction.Create(OpCodes.Ldarg_0));
        translatorIl.Append(Instruction.Create(OpCodes.Ldstr, from));
        translatorIl.Append(Instruction.Create(OpCodes.Call, stringEquality));
        translatorIl.Append(Instruction.Create(OpCodes.Brfalse, next));
        translatorIl.Append(Instruction.Create(OpCodes.Ldstr, to));
        translatorIl.Append(Instruction.Create(OpCodes.Ret));
        translatorIl.Append(next);
    }
    translatorIl.Append(Instruction.Create(OpCodes.Ldarg_0));
    translatorIl.Append(Instruction.Create(OpCodes.Ret));

    var displayCallInsertions = 0;
    foreach (var method in optionPanel.Methods.Where(method => method.HasBody && method != translator))
    {
        var il = method.Body.GetILProcessor();
        foreach (var instruction in method.Body.Instructions.ToArray())
        {
            if (instruction.Operand is not MethodReference called ||
                !called.FullName.Contains("Sunless.Game.Entities.Keybinding.KeyBinding::get_Name", StringComparison.Ordinal))
                continue;
            il.InsertAfter(instruction, Instruction.Create(OpCodes.Call, translator));
            displayCallInsertions++;
        }
    }
    if (displayCallInsertions != 3)
        throw new InvalidDataException($"Expected three keybinding display-name calls, inserted {displayCallInsertions}.");

    var literalReplacements = new Dictionary<(string Type, string Method, string From), string>
    {
        [(optionPanel.FullName, "ListenForKey", "Bind key")] = "Назначение клавиши",
        [(optionPanel.FullName, "ListenForKey", "Press a new key for ")] = "Нажмите новую клавишу для «",
        [(optionPanel.FullName, "ListenForKey", ".")] = "».",
        [(optionPanel.FullName, "ListenForKey", "Cancel")] = "Отмена",
        [(optionPanel.FullName, "ListenForAlt", "Bind key")] = "Назначение клавиши",
        [(optionPanel.FullName, "ListenForAlt", "Press a new key for ")] = "Нажмите новую клавишу для «",
        [(optionPanel.FullName, "ListenForAlt", ".")] = "».",
        [(optionPanel.FullName, "ListenForAlt", "Cancel")] = "Отмена",
        [("Sunless.Game.UI.Menus.KeymappingPanel", "WarnAndRestoreDefaults", "Restore defaults")] = "Настройки по умолчанию",
        [("Sunless.Game.UI.Menus.KeymappingPanel", "WarnAndRestoreDefaults", "Are you sure you want to restore the default control scheme?")] = "Восстановить стандартную схему управления?",
        [("Sunless.Game.ExtensionMethods.KeycodeExtensionMethods", "HumanReadable", "Up")] = "Вверх",
        [("Sunless.Game.ExtensionMethods.KeycodeExtensionMethods", "HumanReadable", "Down")] = "Вниз",
        [("Sunless.Game.ExtensionMethods.KeycodeExtensionMethods", "HumanReadable", "Left")] = "Влево",
        [("Sunless.Game.ExtensionMethods.KeycodeExtensionMethods", "HumanReadable", "Right")] = "Вправо",
    };
    var hitCounts = literalReplacements.Keys.ToDictionary(key => key, _ => 0);
    foreach (var type in module.Types.SelectMany(WalkTypes))
    {
        foreach (var method in type.Methods.Where(method => method.HasBody))
        {
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode.Code != Code.Ldstr || instruction.Operand is not string value)
                    continue;
                var key = (type.FullName, method.Name, value);
                if (!literalReplacements.TryGetValue(key, out var replacement))
                    continue;
                instruction.Operand = replacement;
                hitCounts[key]++;
            }
        }
    }
    var invalid = hitCounts.Where(pair => pair.Value != 1).ToList();
    if (invalid.Count > 0)
        throw new InvalidDataException("Unexpected keybinding UI match counts: " + string.Join(", ", invalid.Select(pair => $"{pair.Key}={pair.Value}")));

    Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
    module.Write(output);
    Console.WriteLine($"Patched keybinding UI: {translations.Count} display names, {displayCallInsertions} display calls, {hitCounts.Count} literals.");
}

static void PatchJettisonUi(string inputPath, string outputPath)
{
    var input = Path.GetFullPath(inputPath);
    var output = Path.GetFullPath(outputPath);
    using var module = ModuleDefinition.ReadModule(input);
    var type = module.Types.SelectMany(WalkTypes)
        .Single(candidate => candidate.FullName == "Sunless.Game.UI.Gazetteer.JettisonDialog");
    var constructor = type.Methods.Single(method => method.IsConstructor && !method.IsStatic && method.HasBody);
    var updatePanels = type.Methods.Single(method => method.Name == "UpdateJettisonPanels" && method.HasBody);

    var constructorReplacements = new Dictionary<string, (string To, int Expected)>
    {
        ["(Зажмите "] = ("Stack Item", 2),
        [" чтобы переместить 10)"] = ("(", 1),
        [" + Click for stacks of 10)"] = (" + ЛКМ: по 10)", 2),
    };
    var constructorCounts = constructorReplacements.Keys.ToDictionary(value => value, _ => 0);
    foreach (var instruction in constructor.Body.Instructions)
    {
        if (instruction.OpCode.Code != Code.Ldstr || instruction.Operand is not string value)
            continue;
        if (!constructorReplacements.TryGetValue(value, out var replacement))
            continue;
        instruction.Operand = replacement.To;
        constructorCounts[value]++;
    }
    var invalidConstructorCounts = constructorReplacements
        .Where(pair => constructorCounts[pair.Key] != pair.Value.Expected)
        .Select(pair => $"{pair.Key}={constructorCounts[pair.Key]}")
        .ToArray();
    if (invalidConstructorCounts.Length != 0)
        throw new InvalidDataException("Unexpected JettisonDialog constructor layout: " + string.Join(", ", invalidConstructorCounts));

    var titleReplacements = 0;
    foreach (var instruction in updatePanels.Body.Instructions)
    {
        if (instruction.OpCode.Code == Code.Ldstr &&
            instruction.Operand is string value &&
            value == "Занято грузом:")
        {
            instruction.Operand = "ТРЮМ";
            titleReplacements++;
        }
    }
    if (titleReplacements != 1)
        throw new InvalidDataException($"Expected one jettison hold title, replaced {titleReplacements}.");

    Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
    module.Write(output);
    Console.WriteLine("Restored Stack Item lookup and patched jettison labels.");
}

static void PatchShipyardUi(string inputPath, string outputPath)
{
    var input = Path.GetFullPath(inputPath);
    var output = Path.GetFullPath(outputPath);
    using var module = ModuleDefinition.ReadModule(input);
    var type = module.Types.SelectMany(WalkTypes)
        .Single(candidate => candidate.FullName == "Sunless.Game.UI.Shipyard.ShipPanel");
    var method = type.Methods.Single(candidate => candidate.Name == "FormattedPrice" && candidate.HasBody);

    var replacements = 0;
    foreach (var instruction in method.Body.Instructions)
    {
        if (instruction.OpCode.Code != Code.Call ||
            instruction.Operand is not MethodReference called ||
            called.FullName != "System.String Sunless.Game.ExtensionMethods.StringExtensionMethods::Pluralise(System.String)")
            continue;
        instruction.OpCode = OpCodes.Nop;
        instruction.Operand = null;
        replacements++;
    }
    if (replacements != 1)
        throw new InvalidDataException($"Expected one shipyard currency pluralizer call, replaced {replacements}.");

    Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
    module.Write(output);
    Console.WriteLine("Disabled English plural suffix for shipyard Echo prices.");
}

static void PatchHudAndWeaponLabels(string inputPath, string outputPath)
{
    var input = Path.GetFullPath(inputPath);
    var output = Path.GetFullPath(outputPath);
    using var module = ModuleDefinition.ReadModule(input);
    var allTypes = module.Types.SelectMany(WalkTypes).ToArray();

    var sailingHud = allTypes.Single(candidate => candidate.FullName == "Sunless.Game.UI.HUD.SailingHUD");
    var hullTooltip = sailingHud.Methods.Single(candidate => candidate.Name == "GetHullTooltip" && candidate.HasBody);
    var hullReplacements = ReplaceLiteral(hullTooltip, "(Hull) Корпус: ", "Корпус: ");
    if (hullReplacements != 1)
        throw new InvalidDataException($"Expected one visible Hull tooltip label, replaced {hullReplacements}.");

    var hotkeys = allTypes.Single(candidate => candidate.FullName == "Sunless.Game.Dictionaries.HotkeysDictionary");
    var hotkeyConstructor = hotkeys.Methods.Single(candidate => candidate.IsConstructor && candidate.IsStatic && candidate.HasBody);
    var pauseReplacements = ReplaceLiteral(hotkeyConstructor, "Pause ", "Пауза ");
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
