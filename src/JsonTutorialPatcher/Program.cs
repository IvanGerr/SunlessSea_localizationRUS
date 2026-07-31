using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: json_tutorial_patcher <input-json> <output-json>");
    return 2;
}

var input = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
var root = JsonNode.Parse(File.ReadAllText(input, Encoding.UTF8)) as JsonArray
    ?? throw new InvalidDataException("Expected a JSON array.");

PatchTutorial(
    root,
    14,
    "Морская летучая мышь",
    "Когда Вы далеко от земли, нажмите на иконку морской летучей мыши. Она будет искать для вас неисследованные места поблизости.",
    "Зи-бэт",
    "Когда вы далеко от земли, нажмите на иконку зи-бэта. Он будет искать для вас неисследованные места поблизости.");
PatchTutorial(
    root,
    15,
    "Морская летучая мышь",
    "Ваша летучая мышь что-то нашла! Нажмите на сообщение, чтобы отметить это место на вашей карте.",
    "Зи-бэт",
    "Зи-бэт что-то нашёл! Нажмите на сообщение, чтобы отметить это место на вашей карте.");

var options = new JsonSerializerOptions
{
    WriteIndented = false,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};
Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
File.WriteAllText(output, root.ToJsonString(options), new UTF8Encoding(false));
Console.WriteLine("Patched Zeebat tutorials 14 and 15.");
return 0;

static void PatchTutorial(
    JsonArray root,
    int id,
    string expectedName,
    string expectedDescription,
    string replacementName,
    string replacementDescription)
{
    var tutorial = root.OfType<JsonObject>().Single(item => item["Id"]?.GetValue<int>() == id);
    Validate(tutorial, id, "Name", expectedName);
    Validate(tutorial, id, "Description", expectedDescription);
    tutorial["Name"] = replacementName;
    tutorial["Description"] = replacementDescription;
}

static void Validate(JsonObject tutorial, int id, string property, string expected)
{
    var actual = tutorial[property]?.GetValue<string>();
    if (!string.Equals(actual, expected, StringComparison.Ordinal))
        throw new InvalidDataException($"Tutorial {id} property {property}: expected '{expected}', found '{actual}'.");
}
