namespace cbm;

/// <summary>
/// A clipboard history row with pin state.
/// </summary>
public sealed record ClipboardHistoryItem(string Text, bool IsPinned);
