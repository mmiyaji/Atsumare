using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atsumare;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true
)]
[JsonSerializable(typeof(AtsumareSettings))]
internal partial class SettingsJsonContext : JsonSerializerContext
{
}