using System;
using System.Collections.Generic;
using AppKit;
using Foundation;

namespace cbm;

public sealed class ClipboardWatcher : IDisposable
{
    private readonly NSPasteboard _pb = NSPasteboard.GeneralPasteboard;
    private readonly NSTimer _timer;

    private int _lastChangeCount;
    private string? _lastText;

    // Simple in-memory history (most recent first)
    public IReadOnlyList<string> History => _history;
    private readonly List<string> _history = new();

    public event Action<string>? OnNewText;

    public ClipboardWatcher(double pollSeconds = 0.25)
    {
        Log.Info("[cbm] ClipboardWatcher started");
        _lastChangeCount = (int)_pb.ChangeCount;

        // Run on main runloop (safe for AppKit usage)
        _timer = NSTimer.CreateRepeatingScheduledTimer(
            TimeSpan.FromSeconds(pollSeconds),
            _ => PollOnce()
        );
    }

    private void PollOnce()
    {
        var cc = _pb.ChangeCount;
        if (cc == _lastChangeCount) return;

        _lastChangeCount = (int)cc;

        // Text only for now. (Later: images, files, RTF/HTML)
        var text = _pb.GetStringForType(NSPasteboard.NSPasteboardTypeString);

        if (string.IsNullOrEmpty(text)) return;

        // Dedupe repeated updates that keep same text
        if (text == _lastText) return;
        _lastText = text;

        AddToHistory(text);

        // Log for now (so you can see it working immediately)
        Console.WriteLine($"[cbm] clipboard: {TrimForLog(text)}");

        OnNewText?.Invoke(text);
    }

    private void AddToHistory(string text)
    {
        // Optional: collapse very large entries for memory safety (tweak later)
        const int maxChars = 50_000;
        if (text.Length > maxChars)
            text = text[..maxChars];

        // Keep newest first, avoid duplicates
        _history.Remove(text);
        _history.Insert(0, text);

        // Cap history length (tweak later)
        const int maxItems = 100;
        if (_history.Count > maxItems)
            _history.RemoveRange(maxItems, _history.Count - maxItems);
    }

    public void Activate(string text)
    {
        // Write back to clipboard
        _pb.ClearContents();
        _pb.SetStringForType(text, NSPasteboard.NSPasteboardTypeString);

        // Move item to top (MRU behavior)
        _history.Remove(text);
        _history.Insert(0, text);

        _lastText = text;

        Log.Info($"activated clipboard item");
    }

    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= _history.Count)
            return false;

        var removed = _history[index];
        _history.RemoveAt(index);

        if (_lastText == removed)
            _lastText = null;

        return true;
    }

    private static string TrimForLog(string s)
    {
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length <= 250 ? s : s[..250] + "…";
    }

    public void Dispose()
    {
        _timer.Invalidate();
        _timer.Dispose();
    }
}
