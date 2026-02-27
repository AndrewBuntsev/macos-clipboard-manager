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
    private readonly List<string> history = new();

    // Simple in-memory history (most recent first)
    public IReadOnlyList<string> History => history;
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

        // Keep newest first, avoid duplicates
        history.Remove(text);
        history.Insert(0, text);

        if (history.Count > MAX_ITEMS)
            history.RemoveRange(MAX_ITEMS, history.Count - MAX_ITEMS);

        SaveHistory();
    }

    public void Activate(string text)
    {
        text = Normalize(text);

        // Write back to clipboard
        pasteboard.ClearContents();
        pasteboard.SetStringForType(text, NSPasteboard.NSPasteboardTypeString);

        // Move item to top (MRU behavior)
        history.Remove(text);
        history.Insert(0, text);
        if (history.Count > MAX_ITEMS)
            history.RemoveRange(MAX_ITEMS, history.Count - MAX_ITEMS);

        lastText = text;
        SaveHistory();

        Log.Info($"activated clipboard item");
    }

    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= history.Count)
            return false;

        var removed = history[index];
        history.RemoveAt(index);
        SaveHistory();

        if (lastText == removed)
            lastText = null;

        return true;
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
            var loaded = JsonSerializer.Deserialize(
                json,
                ClipboardHistoryJsonContext.Default.ListString
            );
            if (loaded == null)
                return;

            foreach (var item in loaded)
            {
                if (string.IsNullOrWhiteSpace(item))
                    continue;

                var normalized = Normalize(item);
                if (history.Contains(normalized))
                    continue;

                history.Add(normalized);
                if (history.Count >= MAX_ITEMS)
                    break;
            }

            if (history.Count > 0)
                lastText = history[0];
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
                    ClipboardHistoryJsonContext.Default.ListString
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

    private static string Normalize(string text) =>
        text.Length <= MAX_CHARS ? text : text[..MAX_CHARS];
}
