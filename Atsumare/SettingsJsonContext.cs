using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atsumare;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true
)]
[JsonSerializable(typeof(AtsumareSettings))]
[JsonSerializable(typeof(AtsumareRulesFile))]
internal partial class SettingsJsonContext : JsonSerializerContext
{
}
