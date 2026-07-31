using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: json_quality_patcher <input-json> <output-json>");
    return 2;
}

var input = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
var root = JsonNode.Parse(File.ReadAllText(input, Encoding.UTF8)) as JsonArray
    ?? throw new InvalidDataException("Expected a JSON array.");
var addressedAs = root
    .OfType<JsonObject>()
    .Single(item => item["Id"]?.GetValue<int>() == 102969);

Validate(addressedAs, 102969, "Name", "Форма обращения к вам");
Validate(addressedAs, 102969, "Description", "Как вас величать?");
Validate(addressedAs, 102969, "LevelDescriptionText", "1|Мадам~2|Сэр~3|Гражданин~4|Милорд~5|Миледи~6|Капитан~100|невнятное бормотание");
var addressedAsChangeDescription = addressedAs["ChangeDescriptionText"]?.GetValue<string>() ?? string.Empty;
if (!string.IsNullOrEmpty(addressedAsChangeDescription))
    throw new InvalidDataException($"Quality 102969 ChangeDescriptionText: expected empty, found '{addressedAsChangeDescription}'.");
addressedAs["LevelDescriptionText"] = "1|Мадам~2|Сэр~3|Гражданин~4|Милорд~5|Миледи~6|Капитан~100|(невнятное бормотание)";
addressedAs["ChangeDescriptionText"] = "100|Событие! «Форма обращения к вам» теперь: (невнятное бормотание)";

var stranger = root
    .OfType<JsonObject>()
    .Single(item => item["Id"]?.GetValue<int>() == 231);
var changeDescription = stranger["ChangeDescriptionText"]?.GetValue<string>()
    ?? throw new InvalidDataException("Quality 231 has no ChangeDescriptionText.");
var levels = changeDescription.Split('~');
const string expectedLevelZero = "0|Ваш статус \"Незнакомец\" закончился. Добро пожаловать!";
const string replacementLevelZero = "0|Вы утратили статус «Незнакомец». Добро пожаловать!";
if (levels.Length == 0 ||
    !string.Equals(levels[0], expectedLevelZero, StringComparison.Ordinal) &&
    !string.Equals(levels[0], replacementLevelZero, StringComparison.Ordinal))
    throw new InvalidDataException($"Quality 231 level 0: expected known source or replacement, found '{levels.FirstOrDefault()}'.");
levels[0] = replacementLevelZero;
stranger["ChangeDescriptionText"] = string.Join("~", levels);

var options = new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};
Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
File.WriteAllText(output, root.ToJsonString(options), new UTF8Encoding(false));
Console.WriteLine("Patched qualities 102969 and 231.");
return 0;

static void Validate(JsonObject quality, int id, string property, string expected)
{
    var actual = quality[property]?.GetValue<string>();
    if (!string.Equals(actual, expected, StringComparison.Ordinal))
        throw new InvalidDataException($"Quality {id} property {property}: expected '{expected}', found '{actual}'.");
}
