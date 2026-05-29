namespace cbm;

using AppKit;
using Foundation;
using ObjCRuntime;

/// <summary>
/// Main application delegate that sets up the clipboard watcher and configures the main window as a sidebar.
/// </summary>
[Register("AppDelegate")]
public class AppDelegate : NSApplicationDelegate
{
    private ClipboardWatcher? clipboardWatcher;
    private WindowPlacementController? windowPlacement;
    private NSObject? screenChangeObserver;
    private NSObject? wakeObserver;
    private NSMenuItem? lockPositionMenuItem;
    private NSMenuItem? unlockPositionMenuItem;

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

        window.TitleVisibility = NSWindowTitleVisibility.Hidden;
        window.TitlebarAppearsTransparent = true;
        window.MovableByWindowBackground = true;

        window.StyleMask |= NSWindowStyle.FullSizeContentView;

        // Panel-like behavior
        window.Level = NSWindowLevel.Floating;
        window.CollectionBehavior |= NSWindowCollectionBehavior.CanJoinAllSpaces;
        window.HidesOnDeactivate = false;

        windowPlacement = new WindowPlacementController(window);
        InstallWindowPlacementMenuItems();
        windowPlacement.ApplyInitialPlacement();

        screenChangeObserver = NSNotificationCenter.DefaultCenter.AddObserver(
            NSApplication.DidChangeScreenParametersNotification,
            _ => ReapplyLockedPlacementSoon()
        );
        wakeObserver = NSWorkspace.SharedWorkspace.NotificationCenter.AddObserver(
            NSWorkspace.DidWakeNotification,
            _ => ReapplyLockedPlacementSoon()
        );
    }

    public override void DidBecomeActive(NSNotification notification)
    {
        ReapplyLockedPlacementSoon();
    }

    public override void WillTerminate(NSNotification notification)
    {
        if (screenChangeObserver != null)
            NSNotificationCenter.DefaultCenter.RemoveObserver(screenChangeObserver);
        if (wakeObserver != null)
            NSWorkspace.SharedWorkspace.NotificationCenter.RemoveObserver(wakeObserver);

        clipboardWatcher?.Dispose();
        clipboardWatcher = null;
    }

    [Export("lockCurrentPosition:")]
    public void LockCurrentPosition(NSObject sender)
    {
        windowPlacement?.LockCurrentPosition();
        UpdatePlacementMenuItems();
    }

    [Export("unlockPosition:")]
    public void UnlockPosition(NSObject sender)
    {
        windowPlacement?.Unlock();
        UpdatePlacementMenuItems();
    }

    [Export("dockLeftOnThisDisplay:")]
    public void DockLeftOnThisDisplay(NSObject sender)
    {
        windowPlacement?.DockLeftOnCurrentDisplay();
        UpdatePlacementMenuItems();
    }

    [Export("dockRightOnThisDisplay:")]
    public void DockRightOnThisDisplay(NSObject sender)
    {
        windowPlacement?.DockRightOnCurrentDisplay();
        UpdatePlacementMenuItems();
    }

    [Export("validateMenuItem:")]
    public bool ValidateMenuItem(NSMenuItem menuItem)
    {
        UpdatePlacementMenuItems();
        return menuItem != unlockPositionMenuItem || windowPlacement?.IsLocked == true;
    }

    private async void ReapplyLockedPlacementSoon()
    {
        ReapplyLockedPlacement();
        await Task.Delay(500);
        BeginInvokeOnMainThread(ReapplyLockedPlacement);
    }

    private void ReapplyLockedPlacement()
    {
        if (windowPlacement?.IsLocked == true)
            windowPlacement.ApplyLockedPlacement();

        UpdatePlacementMenuItems();
    }

    private void InstallWindowPlacementMenuItems()
    {
        var windowMenu = NSApplication.SharedApplication.MainMenu?
            .ItemWithTitle("Window")?
            .Submenu;
        if (windowMenu == null ||
            windowMenu.ItemWithTitle("Lock Current Position") != null)
        {
            return;
        }

        windowMenu.AddItem(NSMenuItem.SeparatorItem);

        lockPositionMenuItem = AddPlacementMenuItem(
            windowMenu,
            "Lock Current Position",
            "lockCurrentPosition:"
        );
        unlockPositionMenuItem = AddPlacementMenuItem(
            windowMenu,
            "Unlock Position",
            "unlockPosition:"
        );
        AddPlacementMenuItem(
            windowMenu,
            "Dock Left on This Display",
            "dockLeftOnThisDisplay:"
        );
        AddPlacementMenuItem(
            windowMenu,
            "Dock Right on This Display",
            "dockRightOnThisDisplay:"
        );

        UpdatePlacementMenuItems();
    }

    private NSMenuItem AddPlacementMenuItem(NSMenu menu, string title, string selectorName)
    {
        var item = new NSMenuItem(title, new Selector(selectorName), string.Empty)
        {
            Target = this
        };
        menu.AddItem(item);
        return item;
    }

    private void UpdatePlacementMenuItems()
    {
        var locked = windowPlacement?.IsLocked == true;
        if (lockPositionMenuItem != null)
            lockPositionMenuItem.State = locked ? NSCellStateValue.On : NSCellStateValue.Off;
        if (unlockPositionMenuItem != null)
            unlockPositionMenuItem.Enabled = locked;
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
