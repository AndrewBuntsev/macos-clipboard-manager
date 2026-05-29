namespace cbm;

public enum ClipboardHistoryItemKind
{
    Text,
    Image,
    FileList
}

/// <summary>
/// A clipboard history row with typed payload metadata and pin state.
/// </summary>
public sealed record ClipboardHistoryItem
{
    public ClipboardHistoryItemKind Kind { get; init; } = ClipboardHistoryItemKind.Text;
    public string Text { get; init; } = string.Empty;
    public bool IsPinned { get; init; }
    public string? ContentHash { get; init; }
    public string? PayloadPath { get; init; }
    public double? ImageWidth { get; init; }
    public double? ImageHeight { get; init; }
    public List<string>? FilePaths { get; init; }

    public ClipboardHistoryItem()
    {
    }

    public ClipboardHistoryItem(string text, bool isPinned)
    {
        Kind = ClipboardHistoryItemKind.Text;
        Text = text;
        IsPinned = isPinned;
    }

    public static ClipboardHistoryItem TextItem(string text) =>
        new()
        {
            Kind = ClipboardHistoryItemKind.Text,
            Text = text
        };

    public static ClipboardHistoryItem ImageItem(
        string displayText,
        string contentHash,
        string payloadPath,
        CGSize size
    ) =>
        new()
        {
            Kind = ClipboardHistoryItemKind.Image,
            Text = displayText,
            ContentHash = contentHash,
            PayloadPath = payloadPath,
            ImageWidth = (double)size.Width,
            ImageHeight = (double)size.Height
        };

    public static ClipboardHistoryItem FileListItem(List<string> filePaths) =>
        new()
        {
            Kind = ClipboardHistoryItemKind.FileList,
            Text = string.Join(Environment.NewLine, filePaths),
            FilePaths = filePaths
        };
}
