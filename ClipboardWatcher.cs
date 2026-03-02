using System.Text.Json;

namespace cbm;

/// <summary>
/// Watches the clipboard for changes, keeps a history, and allows activating items back to the clipboard.
/// </summary>
public sealed class ClipboardWatcher : IDisposable
{
    private const int MAX_CHARS = 50_000;
    private const int MAX_ITEMS = 100;

    private readonly NSPasteboard pasteboard = NSPasteboard.GeneralPasteboard;
    private readonly NSTimer timer;
    private readonly string historyPath;
    private int lastChangeCount;
    private string? lastText;
    private readonly List<ClipboardHistoryItem> history = new();

    // Pinned items are always first and preserve pin order.
    public IReadOnlyList<ClipboardHistoryItem> History => history;
    // Event raised when new text is copied to the clipboard
    public event Action<string>? OnNewText;


    public ClipboardWatcher(double pollSeconds = 0.25)
    {
        Log.Info("[cbm] ClipboardWatcher started");
        lastChangeCount = (int)pasteboard.ChangeCount;
        historyPath = BuildHistoryPath();
        LoadHistory();

        // Run on main runloop (safe for AppKit usage)
        timer = NSTimer.CreateRepeatingScheduledTimer(
            TimeSpan.FromSeconds(pollSeconds),
            (_) => PollOnce()
        );
    }

    private void PollOnce()
    {
        var changeCount = pasteboard.ChangeCount;
        if (changeCount == lastChangeCount) return;
        lastChangeCount = (int)changeCount;

        // Text only for now. (Later: images, files, RTF/HTML)
        var text = pasteboard.GetStringForType(NSPasteboard.NSPasteboardTypeString);

        if (string.IsNullOrEmpty(text)) return;

        // Dedupe repeated updates that keep same text
        if (text == lastText) return;
        lastText = text;

        AddToHistory(text);

        // Log for now (so you can see it working immediately)
        Console.WriteLine($"[cbm] clipboard: {TrimForLog(text)}");

        OnNewText?.Invoke(text);
    }

    private void AddToHistory(string text)
    {
        text = Normalize(text);

        var existingIndex = IndexOf(text);
        if (existingIndex >= 0)
        {
            if (history[existingIndex].IsPinned)
                return;

            var pinnedCount = GetPinnedCount();
            if (existingIndex == pinnedCount)
                return;

            var existing = history[existingIndex];
            history.RemoveAt(existingIndex);
            history.Insert(pinnedCount, existing);
            SaveHistory();
            return;
        }

        history.Insert(GetPinnedCount(), new ClipboardHistoryItem(text, IsPinned: false));
        TrimToLimit();

        SaveHistory();
    }

    public void Activate(string text)
    {
        text = Normalize(text);

        // Write back to clipboard
        pasteboard.ClearContents();
        pasteboard.SetStringForType(text, NSPasteboard.NSPasteboardTypeString);

        var changed = false;
        var existingIndex = IndexOf(text);
        if (existingIndex >= 0)
        {
            var selected = history[existingIndex];
            if (!selected.IsPinned)
            {
                var pinnedCount = GetPinnedCount();
                if (existingIndex != pinnedCount)
                {
                    history.RemoveAt(existingIndex);
                    history.Insert(pinnedCount, selected);
                    changed = true;
                }
            }
        }
        else
        {
            history.Insert(GetPinnedCount(), new ClipboardHistoryItem(text, IsPinned: false));
            TrimToLimit();
            changed = true;
        }

        lastText = text;
        if (changed)
            SaveHistory();

        Log.Info($"activated clipboard item");
    }

    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= history.Count)
            return false;

        var removed = history[index].Text;
        history.RemoveAt(index);
        SaveHistory();

        if (lastText == removed)
            lastText = null;

        return true;
    }

    public bool TogglePinnedAt(int index)
    {
        if (index < 0 || index >= history.Count)
            return false;

        var item = history[index];
        if (item.IsPinned)
            return UnpinAt(index, item);

        return PinAt(index, item);
    }

    public int IndexOf(string text)
    {
        text = Normalize(text);
        for (var i = 0; i < history.Count; i++)
        {
            if (history[i].Text == text)
                return i;
        }

        return -1;
    }

    private static string TrimForLog(string s)
    {
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length <= 250 ? s : s[..250] + "…";
    }

    public void Dispose()
    {
        timer.Invalidate();
        timer.Dispose();
    }

    private void LoadHistory()
    {
        try
        {
            if (!File.Exists(historyPath))
                return;

            var json = File.ReadAllText(historyPath);
            var loadedFromLegacy = false;
            var loaded = TryDeserializeHistoryItems(json);
            if (loaded == null)
            {
                var legacy = JsonSerializer.Deserialize(
                    json,
                    ClipboardHistoryJsonContext.Default.ListString
                );
                if (legacy != null)
                {
                    loaded = new List<ClipboardHistoryItem>(legacy.Count);
                    foreach (var item in legacy)
                    {
                        loaded.Add(new ClipboardHistoryItem(item, IsPinned: false));
                    }

                    loadedFromLegacy = true;
                }
            }

            if (loaded == null)
                return;

            var changed = loadedFromLegacy;
            var pinnedSectionClosed = false;

            foreach (var item in loaded)
            {
                if (string.IsNullOrWhiteSpace(item.Text))
                    continue;

                var normalized = Normalize(item.Text);
                if (IndexOf(normalized) >= 0)
                {
                    changed = true;
                    continue;
                }

                var isPinned = item.IsPinned && !pinnedSectionClosed;
                if (!item.IsPinned && !pinnedSectionClosed)
                    pinnedSectionClosed = true;
                if (item.IsPinned && pinnedSectionClosed)
                    changed = true;

                if (normalized != item.Text)
                    changed = true;

                history.Add(new ClipboardHistoryItem(normalized, isPinned));
                if (history.Count >= MAX_ITEMS)
                    break;
            }

            if (history.Count > 0)
                lastText = history[0].Text;

            if (changed)
                SaveHistory();
        }
        catch (Exception ex)
        {
            Log.Info($"failed to load clipboard history: {ex.Message}");
        }
    }

    private void SaveHistory()
    {
        try
        {
            var directory = Path.GetDirectoryName(historyPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(
                historyPath,
                JsonSerializer.Serialize(
                    history,
                    ClipboardHistoryJsonContext.Default.ListClipboardHistoryItem
                )
            );
        }
        catch (Exception ex)
        {
            Log.Info($"failed to save clipboard history: {ex.Message}");
        }
    }

    private static string BuildHistoryPath()
    {
        var appSupport = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appSupport, "cbm", "history.json");
    }

    private static List<ClipboardHistoryItem>? TryDeserializeHistoryItems(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(
                json,
                ClipboardHistoryJsonContext.Default.ListClipboardHistoryItem
            );
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private int GetPinnedCount()
    {
        var count = 0;
        while (count < history.Count && history[count].IsPinned)
            count++;

        return count;
    }

    private static void TrimToLimit(List<ClipboardHistoryItem> items)
    {
        if (items.Count <= MAX_ITEMS)
            return;

        items.RemoveRange(MAX_ITEMS, items.Count - MAX_ITEMS);
    }

    private void TrimToLimit() => TrimToLimit(history);

    private bool PinAt(int index, ClipboardHistoryItem item)
    {
        var pinnedCount = GetPinnedCount();
        history.RemoveAt(index);
        history.Insert(pinnedCount, item with { IsPinned = true });
        SaveHistory();
        return true;
    }

    private bool UnpinAt(int index, ClipboardHistoryItem item)
    {
        var pinnedCount = GetPinnedCount();
        history.RemoveAt(index);
        history.Insert(pinnedCount - 1, item with { IsPinned = false });
        SaveHistory();
        return true;
    }

    private static string Normalize(string text) =>
        text.Length <= MAX_CHARS ? text : text[..MAX_CHARS];
}
