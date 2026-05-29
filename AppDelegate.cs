namespace cbm;

using AppKit;
using Foundation;
using CoreGraphics;

/// <summary>
/// Main application delegate that sets up the clipboard watcher and configures the main window as a sidebar.
/// </summary>
[Register("AppDelegate")]
public class AppDelegate : NSApplicationDelegate
{
    private ClipboardWatcher? clipboardWatcher;

    public ClipboardWatcher Watcher => clipboardWatcher ??= new ClipboardWatcher(0.25);

    public override void DidFinishLaunching(NSNotification notification)
    {
        Log.Info("AppDelegate DidFinishLaunching");

        Watcher.OnNewItem += item =>
        {
            Log.Info($"Copied {DescribeClipboardItem(item)}");
        };

        var window = NSApplication.SharedApplication.MainWindow;
        if (window == null)
            return;

        var screen = window.Screen ?? NSScreen.MainScreen;
        if (screen == null)
            return;

        var visible = screen.VisibleFrame;
        nfloat width = 150;

        // Right edge sidebar
        var frame = new CGRect(
            visible.X + visible.Width - width,
            visible.Y,
            width,
            visible.Height
        );

        window.SetFrame(frame, display: true);

        window.TitleVisibility = NSWindowTitleVisibility.Hidden;
        window.TitlebarAppearsTransparent = true;
        window.MovableByWindowBackground = true;

        window.StyleMask |= NSWindowStyle.FullSizeContentView;

        // Panel-like behavior
        window.Level = NSWindowLevel.Floating;
        window.CollectionBehavior |= NSWindowCollectionBehavior.CanJoinAllSpaces;
        window.HidesOnDeactivate = false;
    }

    public override void WillTerminate(NSNotification notification)
    {
        clipboardWatcher?.Dispose();
        clipboardWatcher = null;
    }

    private static string DescribeClipboardItem(ClipboardHistoryItem item)
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
        var fileCount = CountFiles(item);
        return $"FileList ({fileCount} {Pluralize(fileCount, "item")})";
    }

    private static string DescribeImageSize(ClipboardHistoryItem item)
    {
        if (item.ImageWidth is not { } width || item.ImageHeight is not { } height)
            return "stored";

        return $"{Math.Round(width)}x{Math.Round(height)}";
    }

    private static int CountFiles(ClipboardHistoryItem item)
    {
        if (item.FilePaths != null)
            return item.FilePaths.Count;

        return item.Text
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
            .Length;
    }

    private static string Pluralize(int count, string singular) =>
        count == 1 ? singular : singular + "s";
}
