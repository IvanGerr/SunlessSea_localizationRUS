using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

var preserveLayout = args.Length == 3 && args[0] == "--patch-live-data";
if (!preserveLayout && args.Length != 2)
{
    Console.Error.WriteLine("Usage: json_tutorial_patcher [--patch-live-data] <input-json> <output-json>");
    return 2;
}

var input = Path.GetFullPath(args[preserveLayout ? 1 : 0]);
var output = Path.GetFullPath(args[preserveLayout ? 2 : 1]);
var root = JsonNode.Parse(File.ReadAllText(input, Encoding.UTF8)) as JsonArray
    ?? throw new InvalidDataException("Expected a JSON array.");

PatchTutorial(
    root,
    14,
    ["Морская летучая мышь", "Зи-бэт", "Zee-bat"],
    [
        "Когда Вы далеко от земли, нажмите на иконку морской летучей мыши. Она будет искать для вас неисследованные места поблизости.",
        "Когда вы далеко от земли, нажмите на иконку зи-бэта. Он будет искать для вас неисследованные места поблизости.",
        "When you are far from land, click on the zee-bat icon. It will search for undiscovered locations nearby.",
    ],
    "Зи-бэт",
    "Когда вы далеко от земли, нажмите на иконку зи-бэта. Он будет искать для вас неисследованные места поблизости.");
PatchTutorial(
    root,
    15,
    ["Морская летучая мышь", "Зи-бэт", "Zee-bat", "Zee-bat "],
    [
        "Ваша летучая мышь что-то нашла! Нажмите на сообщение, чтобы отметить это место на вашей карте.",
        "Зи-бэт что-то нашёл! Нажмите на сообщение, чтобы отметить это место на вашей карте.",
        "Ваш зи-бэт что-то обнаружил! Нажмите на сообщение, чтобы отметить это место на карте.",
        "Your zee-bat has discovered something! Click on the message to mark the location on your chart.",
    ],
    "Зи-бэт",
    "Ваш зи-бэт что-то обнаружил! Нажмите на сообщение, чтобы отметить это место на карте.");

if (preserveLayout)
    ValidatePreservedTutorialSet(root);
else
    NormalizeTutorialSet(root);

var options = new JsonSerializerOptions
{
    WriteIndented = false,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};
Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
File.WriteAllText(output, root.ToJsonString(options), new UTF8Encoding(false));
Console.WriteLine(preserveLayout
    ? $"Patched Zeebat tutorials 14 and 15 and preserved the {root.Count}-entry live-data layout."
    : "Patched Zeebat tutorials 14 and 15 and verified the unique 34-entry addon layout.");
return 0;

static void PatchTutorial(
    JsonArray root,
    int id,
    string[] expectedNames,
    string[] expectedDescriptions,
    string replacementName,
    string replacementDescription)
{
    var tutorials = root
        .OfType<JsonObject>()
        .Where(item => item["Id"]?.GetValue<int>() == id)
        .ToArray();

    if (tutorials.Length is < 1 or > 2)
        throw new InvalidDataException($"Tutorial {id}: expected one or two entries, found {tutorials.Length}.");

    foreach (var tutorial in tutorials)
    {
        Validate(tutorial, id, "Name", expectedNames);
        Validate(tutorial, id, "Description", expectedDescriptions);
        tutorial["Name"] = replacementName;
        tutorial["Description"] = replacementDescription;
    }
}

static void NormalizeTutorialSet(JsonArray root)
{
    if (root.Count == 68)
    {
        ValidateIdCounts(root, 2);
        foreach (var group in root.OfType<JsonObject>().GroupBy(item => item["Id"]!.GetValue<int>()))
        {
            var pair = group.ToArray();
            if (!JsonNode.DeepEquals(pair[0], pair[1]))
                throw new InvalidDataException($"Tutorial {group.Key}: duplicated entries differ.");
        }

        while (root.Count > 34)
            root.RemoveAt(root.Count - 1);
    }

    if (root.Count != 34)
        throw new InvalidDataException($"Expected 34 or 68 tutorials, found {root.Count}.");

    ValidateIdCounts(root, 1);
}

static void ValidatePreservedTutorialSet(JsonArray root)
{
    var expectedCount = root.Count switch
    {
        34 => 1,
        68 => 2,
        _ => throw new InvalidDataException($"Expected 34 or 68 tutorials, found {root.Count}."),
    };
    ValidateIdCounts(root, expectedCount);
}

static void ValidateIdCounts(JsonArray root, int expectedCount)
{
    var groups = root
        .OfType<JsonObject>()
        .GroupBy(item => item["Id"]?.GetValue<int>() ?? -1)
        .ToDictionary(group => group.Key, group => group.Count());

    var expectedIds = Enumerable.Range(1, 34).ToArray();
    var actualIds = groups.Keys.Order().ToArray();
    if (!actualIds.SequenceEqual(expectedIds))
        throw new InvalidDataException("Expected tutorial IDs 1 through 34.");

    var invalid = groups.Where(item => item.Value != expectedCount).ToArray();
    if (invalid.Length > 0)
        throw new InvalidDataException($"Expected each tutorial ID {expectedCount} time(s).");
}

static void Validate(JsonObject tutorial, int id, string property, string[] expected)
{
    var actual = tutorial[property]?.GetValue<string>();
    if (!expected.Contains(actual, StringComparer.Ordinal))
        throw new InvalidDataException($"Tutorial {id} property {property}: unexpected value '{actual}'.");
}
