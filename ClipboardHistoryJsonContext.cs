using System.Text.Json.Serialization;

namespace cbm;

[JsonSerializable(typeof(List<string>))]
internal partial class ClipboardHistoryJsonContext : JsonSerializerContext
{
}
