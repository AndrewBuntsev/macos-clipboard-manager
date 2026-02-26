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
        if (text.Length > MAX_CHARS)
            text = text[..MAX_CHARS];

        // Keep newest first, avoid duplicates
        history.Remove(text);
        history.Insert(0, text);

        if (history.Count > MAX_ITEMS)
            history.RemoveRange(MAX_ITEMS, history.Count - MAX_ITEMS);
    }

    public void Activate(string text)
    {
        // Write back to clipboard
        pasteboard.ClearContents();
        pasteboard.SetStringForType(text, NSPasteboard.NSPasteboardTypeString);

        // Move item to top (MRU behavior)
        history.Remove(text);
        history.Insert(0, text);

        lastText = text;

        Log.Info($"activated clipboard item");
    }

    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= history.Count)
            return false;

        var removed = history[index];
        history.RemoveAt(index);

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
}
