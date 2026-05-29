using System.Security.Cryptography;
using System.Text.Json;

namespace cbm;

/// <summary>
/// Watches the clipboard for changes, keeps a history, and allows activating items back to the clipboard.
/// </summary>
public sealed class ClipboardWatcher : IDisposable
{
    private const int MAX_CHARS = 50_000;
    private const int MAX_ITEMS = 100;
    private const long MAX_IMAGE_STORAGE_BYTES = 256L * 1024 * 1024;
    private const string PASTEBOARD_TYPE_STRING = "public.utf8-plain-text";
    private const string PASTEBOARD_TYPE_FILE_URL = "public.file-url";
    private const string PASTEBOARD_TYPE_PNG = "public.png";
    private const string PASTEBOARD_TYPE_TIFF = "public.tiff";

    private readonly NSPasteboard pasteboard = NSPasteboard.GeneralPasteboard;
    private readonly NSTimer timer;
    private readonly string appSupportDirectory;
    private readonly string imagesDirectory;
    private readonly string historyPath;
    private int lastChangeCount;
    private string? lastItemKey;
    private readonly List<ClipboardHistoryItem> history = new();

    // Pinned items are always first and preserve pin order.
    public IReadOnlyList<ClipboardHistoryItem> History => history;
    public event Action<ClipboardHistoryItem>? OnNewItem;

    public ClipboardWatcher(double pollSeconds = 0.25)
    {
        Log.Info("[cbm] ClipboardWatcher started");
        lastChangeCount = (int)pasteboard.ChangeCount;
        appSupportDirectory = BuildAppSupportPath();
        imagesDirectory = Path.Combine(appSupportDirectory, "images");
        historyPath = Path.Combine(appSupportDirectory, "history.json");
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

        var item = TryReadCurrentItem();
        if (item == null) return;

        var itemKey = GetItemKey(item);
        if (itemKey == lastItemKey) return;
        lastItemKey = itemKey;

        AddToHistory(item);

        Console.WriteLine($"[cbm] clipboard: {DescribeForLog(item)}");

        OnNewItem?.Invoke(item);
    }

    private ClipboardHistoryItem? TryReadCurrentItem() =>
        TryReadFileListItem() ??
        TryReadImageItem() ??
        TryReadTextItem();

    private ClipboardHistoryItem? TryReadTextItem()
    {
        var text = pasteboard.GetStringForType(PASTEBOARD_TYPE_STRING);
        if (string.IsNullOrEmpty(text))
            return null;

        return ClipboardHistoryItem.TextItem(Normalize(text));
    }

    private ClipboardHistoryItem? TryReadFileListItem()
    {
        var pasteboardItems = pasteboard.PasteboardItems;
        if (pasteboardItems == null || pasteboardItems.Length == 0)
            return null;

        var paths = new List<string>();
        foreach (var pasteboardItem in pasteboardItems)
        {
            var fileUrl = pasteboardItem.GetStringForType(PASTEBOARD_TYPE_FILE_URL);
            if (string.IsNullOrEmpty(fileUrl))
                continue;

            var url = NSUrl.FromString(fileUrl);
            var path = url?.IsFileUrl == true ? url.Path : null;
            if (string.IsNullOrWhiteSpace(path))
                continue;

            if (!paths.Contains(path, StringComparer.Ordinal))
                paths.Add(path);
        }

        return paths.Count == 0 ? null : ClipboardHistoryItem.FileListItem(paths);
    }

    private ClipboardHistoryItem? TryReadImageItem()
    {
        var pngData = pasteboard.GetDataForType(PASTEBOARD_TYPE_PNG);
        NSImage? image = null;

        if (pngData != null)
        {
            image = new NSImage(pngData);
        }
        else
        {
            var tiffData = pasteboard.GetDataForType(PASTEBOARD_TYPE_TIFF);
            if (tiffData == null)
                return null;

            image = new NSImage(tiffData);
            pngData = ConvertImageToPng(image);
        }

        if (pngData == null || image == null)
            return null;

        var bytes = pngData.ToArray();
        if (bytes.Length == 0)
            return null;

        var hash = HashBytes(bytes);
        var relativePath = Path.Combine("images", $"{hash}.png");
        var absolutePath = Path.Combine(appSupportDirectory, relativePath);

        Directory.CreateDirectory(imagesDirectory);
        if (!File.Exists(absolutePath))
            File.WriteAllBytes(absolutePath, bytes);

        var size = image.Size;
        var displayText = $"Image {Math.Round((double)size.Width)}x{Math.Round((double)size.Height)}";
        return ClipboardHistoryItem.ImageItem(displayText, hash, relativePath, size);
    }

    private void AddToHistory(ClipboardHistoryItem item)
    {
        item = Normalize(item);

        var existingIndex = IndexOf(item);
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

        history.Insert(GetPinnedCount(), item with { IsPinned = false });
        TrimToLimit();
        EnforceImageStorageLimit();

        SaveHistory();
    }

    public void Activate(ClipboardHistoryItem item)
    {
        item = Normalize(item);

        if (!WriteItemToPasteboard(item))
            return;

        var changed = false;
        var existingIndex = IndexOf(item);
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
            history.Insert(GetPinnedCount(), item with { IsPinned = false });
            TrimToLimit();
            changed = true;
        }

        lastItemKey = GetItemKey(item);
        if (changed)
            SaveHistory();

        Log.Info("activated clipboard item");
    }

    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= history.Count)
            return false;

        var removed = history[index];
        history.RemoveAt(index);
        DeletePayloadIfUnused(removed);
        SaveHistory();

        if (lastItemKey == GetItemKey(removed))
            lastItemKey = null;

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

    public string? GetPayloadPath(ClipboardHistoryItem item) =>
        string.IsNullOrEmpty(item.PayloadPath) ? null : ResolvePayloadPath(item.PayloadPath);

    public int IndexOf(ClipboardHistoryItem item)
    {
        var key = GetItemKey(Normalize(item));
        for (var i = 0; i < history.Count; i++)
        {
            if (GetItemKey(history[i]) == key)
                return i;
        }

        return -1;
    }

    public void Dispose()
    {
        timer.Invalidate();
        timer.Dispose();
    }

    private bool WriteItemToPasteboard(ClipboardHistoryItem item)
    {
        switch (item.Kind)
        {
            case ClipboardHistoryItemKind.Text:
                pasteboard.ClearContents();
                pasteboard.SetStringForType(item.Text, PASTEBOARD_TYPE_STRING);
                return true;

            case ClipboardHistoryItemKind.Image:
                return WriteImageToPasteboard(item);

            case ClipboardHistoryItemKind.FileList:
                return WriteFilesToPasteboard(GetFilePaths(item));

            default:
                return false;
        }
    }

    private bool WriteImageToPasteboard(ClipboardHistoryItem item)
    {
        if (string.IsNullOrEmpty(item.PayloadPath))
            return false;

        var absolutePath = ResolvePayloadPath(item.PayloadPath);
        if (absolutePath == null || !File.Exists(absolutePath))
            return false;

        var pngData = NSData.FromFile(absolutePath);
        if (pngData == null)
            return false;

        pasteboard.ClearContents();
        pasteboard.SetDataForType(pngData, PASTEBOARD_TYPE_PNG);

        using var image = new NSImage(pngData);
        var tiffData = image.AsTiff();
        if (tiffData != null)
            pasteboard.SetDataForType(tiffData, PASTEBOARD_TYPE_TIFF);

        return true;
    }

    private bool WriteFilesToPasteboard(List<string> filePaths)
    {
        if (filePaths.Count == 0)
            return false;

        var pasteboardItems = new List<INSPasteboardWriting>();
        foreach (var filePath in filePaths)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                continue;

            var fileUrl = NSUrl.FromFilename(filePath);
            if (string.IsNullOrEmpty(fileUrl.AbsoluteString))
                continue;

            var pasteboardItem = new NSPasteboardItem();
            pasteboardItem.SetStringForType(
                fileUrl.AbsoluteString,
                PASTEBOARD_TYPE_FILE_URL
            );
            pasteboardItems.Add(pasteboardItem);
        }

        if (pasteboardItems.Count == 0)
            return false;

        pasteboard.ClearContents();
        pasteboard.WriteObjects(pasteboardItems.ToArray());
        return true;
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
                        loaded.Add(new ClipboardHistoryItem(item, isPinned: false));
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
                var normalized = NormalizeLoadedItem(item, ref changed);
                if (normalized == null)
                    continue;

                if (IndexOf(normalized) >= 0)
                {
                    changed = true;
                    continue;
                }

                var isPinned = normalized.IsPinned && !pinnedSectionClosed;
                if (!normalized.IsPinned && !pinnedSectionClosed)
                    pinnedSectionClosed = true;
                if (normalized.IsPinned && pinnedSectionClosed)
                    changed = true;

                history.Add(normalized with { IsPinned = isPinned });
                if (history.Count >= MAX_ITEMS)
                    break;
            }

            if (history.Count > 0)
                lastItemKey = GetItemKey(history[0]);

            if (changed || EnforceImageStorageLimit())
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
            Directory.CreateDirectory(appSupportDirectory);

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

    private static string BuildAppSupportPath()
    {
        var appSupport = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appSupport, "cbm");
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

    private ClipboardHistoryItem? NormalizeLoadedItem(
        ClipboardHistoryItem item,
        ref bool changed
    )
    {
        var normalized = Normalize(item);
        if (string.IsNullOrWhiteSpace(normalized.Text))
            return null;

        if (normalized.Kind == ClipboardHistoryItemKind.Image)
        {
            if (string.IsNullOrEmpty(normalized.PayloadPath))
                return null;

            var payloadPath = ResolvePayloadPath(normalized.PayloadPath);
            if (payloadPath == null || !File.Exists(payloadPath))
            {
                changed = true;
                return null;
            }
        }

        if (normalized.Kind == ClipboardHistoryItemKind.FileList &&
            (normalized.FilePaths == null || normalized.FilePaths.Count == 0))
        {
            changed = true;
            return null;
        }

        if (normalized != item)
            changed = true;

        return normalized;
    }

    private ClipboardHistoryItem Normalize(ClipboardHistoryItem item)
    {
        var text = Normalize(item.Text ?? string.Empty);

        if (item.Kind != ClipboardHistoryItemKind.FileList)
            return item with { Text = text };

        var filePaths = GetFilePaths(item)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return item with
        {
            Text = Normalize(string.Join(Environment.NewLine, filePaths)),
            FilePaths = filePaths
        };
    }

    private static NSData? ConvertImageToPng(NSImage image)
    {
        var tiffData = image.AsTiff();
        if (tiffData == null)
            return null;

        var imageRep = NSBitmapImageRep.ImageRepFromData(tiffData) as NSBitmapImageRep;
        return imageRep?.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png);
    }

    private static string HashBytes(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string DescribeForLog(ClipboardHistoryItem item)
    {
        return item.Kind switch
        {
            ClipboardHistoryItemKind.Image => $"Image ({DescribeImageSize(item)})",
            ClipboardHistoryItemKind.FileList => DescribeFiles(item),
            _ => DescribeText(item)
        };
    }

    private static string DescribeText(ClipboardHistoryItem item)
    {
        var charCount = item.Text.Length;
        return $"Text ({charCount} {Pluralize(charCount, "char")})";
    }

    private static string DescribeFiles(ClipboardHistoryItem item)
    {
        var fileCount = GetFilePaths(item).Count;
        return $"Files ({fileCount} {Pluralize(fileCount, "item")})";
    }

    private static string DescribeImageSize(ClipboardHistoryItem item)
    {
        if (item.ImageWidth is not { } width || item.ImageHeight is not { } height)
            return "stored";

        return $"{Math.Round(width)}x{Math.Round(height)}";
    }

    private static string Pluralize(int count, string singular) =>
        count == 1 ? singular : singular + "s";

    private static List<string> GetFilePaths(ClipboardHistoryItem item)
    {
        if (item.FilePaths != null)
            return item.FilePaths;

        return item.Text
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }

    private int GetPinnedCount()
    {
        var count = 0;
        while (count < history.Count && history[count].IsPinned)
            count++;

        return count;
    }

    private void TrimToLimit()
    {
        if (history.Count <= MAX_ITEMS)
            return;

        var removed = history.GetRange(MAX_ITEMS, history.Count - MAX_ITEMS);
        history.RemoveRange(MAX_ITEMS, history.Count - MAX_ITEMS);
        foreach (var item in removed)
            DeletePayloadIfUnused(item);
    }

    private bool EnforceImageStorageLimit()
    {
        var imageItems = history
            .Where(item => item.Kind == ClipboardHistoryItemKind.Image)
            .Where(item => !string.IsNullOrEmpty(item.PayloadPath))
            .ToList();

        var totalBytes = imageItems
            .Select(item => ResolvePayloadPath(item.PayloadPath!))
            .Where(path => path != null && File.Exists(path))
            .Distinct(StringComparer.Ordinal)
            .Sum(path => new FileInfo(path!).Length);

        if (totalBytes <= MAX_IMAGE_STORAGE_BYTES)
            return false;

        var changed = false;
        for (var i = history.Count - 1; i >= 0 && totalBytes > MAX_IMAGE_STORAGE_BYTES; i--)
        {
            var item = history[i];
            if (item.Kind != ClipboardHistoryItemKind.Image || item.IsPinned)
                continue;

            var payloadPath = string.IsNullOrEmpty(item.PayloadPath)
                ? null
                : ResolvePayloadPath(item.PayloadPath);
            var payloadSize = payloadPath != null && File.Exists(payloadPath)
                ? new FileInfo(payloadPath).Length
                : 0;

            history.RemoveAt(i);
            DeletePayloadIfUnused(item);
            totalBytes -= payloadSize;
            changed = true;
        }

        return changed;
    }

    private void DeletePayloadIfUnused(ClipboardHistoryItem item)
    {
        if (item.Kind != ClipboardHistoryItemKind.Image ||
            string.IsNullOrEmpty(item.PayloadPath) ||
            history.Any(existing => existing.PayloadPath == item.PayloadPath))
            return;

        var payloadPath = ResolvePayloadPath(item.PayloadPath);
        if (payloadPath == null || !File.Exists(payloadPath))
            return;

        try
        {
            File.Delete(payloadPath);
        }
        catch (Exception ex)
        {
            Log.Info($"failed to delete clipboard payload: {ex.Message}");
        }
    }

    private string? ResolvePayloadPath(string relativePath)
    {
        var root = Path.GetFullPath(appSupportDirectory);
        var candidate = Path.GetFullPath(Path.Combine(appSupportDirectory, relativePath));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return candidate.Equals(root, StringComparison.Ordinal) ||
               candidate.StartsWith(rootWithSeparator, StringComparison.Ordinal)
            ? candidate
            : null;
    }

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

    private static string GetItemKey(ClipboardHistoryItem item) =>
        item.Kind switch
        {
            ClipboardHistoryItemKind.Image => $"image:{item.ContentHash ?? item.PayloadPath ?? item.Text}",
            ClipboardHistoryItemKind.FileList => $"files:{string.Join('\n', GetFilePaths(item))}",
            _ => $"text:{item.Text}"
        };

    private static string Normalize(string text) =>
        text.Length <= MAX_CHARS ? text : text[..MAX_CHARS];
}
