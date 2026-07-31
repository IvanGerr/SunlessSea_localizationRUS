using System.Text;
using System.Text.Json;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length < 5 || (args[0] != "inspect" && args[0] != "dump" && args[0] != "patch"))
{
    Console.Error.WriteLine("Usage: asset_text_patcher <inspect|dump|patch> <assets> <managed-dir> <classdata.tpk> <output> [manifest.json|pathIds...]");
    return 2;
}

var command = args[0];
var assetsPath = Path.GetFullPath(args[1]);
var managedPath = Path.GetFullPath(args[2]);
var classDataPath = Path.GetFullPath(args[3]);
var outputPath = Path.GetFullPath(args[4]);

var manager = new AssetsManager();
manager.LoadClassPackage(classDataPath);
manager.MonoTempGenerator = new MonoCecilTempGenerator(managedPath);
var assetsInstance = manager.LoadAssetsFile(assetsPath, true);
var assetsFile = assetsInstance.file;
manager.LoadClassDatabaseFromPackage(assetsFile.Metadata.UnityVersion);

if (command == "inspect")
{
    Inspect(manager, assetsInstance, outputPath);
    return 0;
}

if (command == "dump")
{
    var pathIds = args.Skip(5).Select(long.Parse).ToArray();
    Dump(manager, assetsInstance, outputPath, pathIds);
    return 0;
}

if (args.Length < 6)
{
    Console.Error.WriteLine("Patch mode requires a manifest path.");
    return 2;
}

var manifest = JsonSerializer.Deserialize<PatchManifest>(
    File.ReadAllText(args[5], Encoding.UTF8),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
    ?? throw new InvalidDataException("Invalid patch manifest.");

var applied = new List<PatchResult>();

foreach (var replacement in manifest.Replacements)
{
    var info = assetsFile.GetAssetInfo(replacement.PathId)
        ?? throw new InvalidDataException($"MonoBehaviour PathId {replacement.PathId} not found.");
    if (info.TypeId != (int)AssetClassID.MonoBehaviour)
        throw new InvalidDataException($"PathId {replacement.PathId} is not a MonoBehaviour.");

    var baseField = manager.GetBaseField(assetsInstance, info, AssetReadFlags.None);
    var textField = baseField["m_Text"];
    if (textField.IsDummy)
        throw new InvalidDataException($"PathId {info.PathId} does not contain m_Text.");
    var current = textField.AsString;
    if (!string.Equals(current, replacement.From, StringComparison.Ordinal))
        throw new InvalidDataException($"PathId {info.PathId}: expected {Escape(replacement.From)}, found {Escape(current)}.");

    textField.AsString = replacement.To;
    info.SetNewData(baseField);
    applied.Add(new PatchResult(info.PathId, current, replacement.To));
}

var renames = manifest.GameObjectRenames ?? [];
foreach (var rename in renames)
{
    var info = assetsFile.GetAssetInfo(rename.PathId)
        ?? throw new InvalidDataException($"GameObject PathId {rename.PathId} not found.");
    if (info.TypeId != (int)AssetClassID.GameObject)
        throw new InvalidDataException($"PathId {rename.PathId} is not a GameObject.");

    var baseField = manager.GetBaseField(assetsInstance, info, AssetReadFlags.None);
    var nameField = baseField["m_Name"];
    var current = nameField.AsString;
    if (!string.Equals(current, rename.From, StringComparison.Ordinal))
        throw new InvalidDataException($"GameObject PathId {rename.PathId}: expected {Escape(rename.From)}, found {Escape(current)}.");

    nameField.AsString = rename.To;
    info.SetNewData(baseField);
    applied.Add(new PatchResult(info.PathId, current, rename.To));
}

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
using (var writer = new AssetsFileWriter(outputPath))
{
    assetsFile.Write(writer, 0);
}

foreach (var item in applied.OrderBy(item => item.PathId))
    Console.WriteLine($"{item.PathId}\t{Escape(item.From)}\t=>\t{Escape(item.To)}");
Console.WriteLine($"Patched {applied.Count} objects into {outputPath}");
return 0;

static void Inspect(AssetsManager manager, AssetsFileInstance assetsInstance, string outputPath)
{
    var rows = new List<InspectRow>();
    var failures = 0;
    var failureSamples = new List<string>();

    foreach (var info in assetsInstance.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
    {
        try
        {
            var baseField = manager.GetBaseField(assetsInstance, info, AssetReadFlags.None);
            var textField = baseField["m_Text"];
            if (textField.IsDummy)
                continue;

            var gameObjectName = GetGameObjectName(manager, assetsInstance, baseField);
            rows.Add(new InspectRow(info.PathId, info.ByteSize, gameObjectName, textField.AsString));
        }
        catch (Exception ex)
        {
            failures++;
            if (failureSamples.Count < 20)
                failureSamples.Add($"PathId {info.PathId}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    using var writer = new StreamWriter(outputPath, false, new UTF8Encoding(false));
    writer.WriteLine("PathId\tByteSize\tGameObject\tText");
    foreach (var row in rows.OrderBy(item => item.PathId))
        writer.WriteLine($"{row.PathId}\t{row.ByteSize}\t{Escape(row.GameObject)}\t{Escape(row.Text)}");

    Console.WriteLine($"Exported {rows.Count} UI text objects to {outputPath}; {failures} MonoBehaviours could not be decoded.");
    foreach (var sample in failureSamples)
        Console.WriteLine(sample);
}

static void Dump(AssetsManager manager, AssetsFileInstance assetsInstance, string outputPath, long[] pathIds)
{
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    using var writer = new StreamWriter(outputPath, false, new UTF8Encoding(false));

    foreach (var pathId in pathIds)
    {
        var info = assetsInstance.file.GetAssetInfo(pathId)
            ?? throw new InvalidDataException($"PathId {pathId} not found.");
        var baseField = manager.GetBaseField(assetsInstance, info, AssetReadFlags.None);
        writer.WriteLine($"===== PathId {pathId}, TypeId {info.TypeId}, ByteSize {info.ByteSize} =====");
        DumpField(writer, baseField, 0);
    }
}

static void DumpField(TextWriter writer, AssetTypeValueField field, int depth)
{
    var indent = new string(' ', depth * 2);
    var value = string.Empty;
    if (field.TemplateField.HasValue && field.Value is not null)
    {
        try
        {
            value = $" = {Escape(Convert.ToString(field.AsObject, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)}";
        }
        catch
        {
            value = " = <unprintable>";
        }
    }
    writer.WriteLine($"{indent}{field.TypeName} {field.FieldName}{value}");
    foreach (var child in field.Children)
        DumpField(writer, child, depth + 1);
}

static string GetGameObjectName(AssetsManager manager, AssetsFileInstance assetsInstance, AssetTypeValueField baseField)
{
    try
    {
        var gameObjectPointer = baseField["m_GameObject"];
        if (gameObjectPointer.IsDummy)
            return string.Empty;
        var external = manager.GetExtAsset(assetsInstance, gameObjectPointer, false, AssetReadFlags.None);
        if (external.baseField is null)
            return string.Empty;
        var nameField = external.baseField["m_Name"];
        return nameField.IsDummy ? string.Empty : nameField.AsString;
    }
    catch
    {
        return string.Empty;
    }
}

static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");

sealed record PatchManifest(List<PatchItem> Replacements, List<PatchItem>? GameObjectRenames);
sealed record PatchItem(long PathId, string From, string To);
sealed record PatchResult(long PathId, string From, string To);
sealed record InspectRow(long PathId, uint ByteSize, string GameObject, string Text);
