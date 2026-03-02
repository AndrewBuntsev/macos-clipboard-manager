using System.Text.Json.Serialization;

namespace cbm;

[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<ClipboardHistoryItem>))]
internal partial class ClipboardHistoryJsonContext : JsonSerializerContext
{
}
