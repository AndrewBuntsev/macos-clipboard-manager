namespace cbm;

using AppKit;
using Foundation;
using CoreGraphics;

[Register ("AppDelegate")]
public class AppDelegate : NSApplicationDelegate {
	private ClipboardWatcher? _watcher;
    public ClipboardWatcher Watcher => _watcher ??= new ClipboardWatcher(0.25);

	public override void DidFinishLaunching (NSNotification notification)
	{
		Log.Info("AppDelegate DidFinishLaunching");

		Watcher.OnNewText += text =>
		{
			Log.Info($"Copied text: {text}");
			// For now, just confirm it’s captured (you’ll see this in terminal output)
			// Later we’ll update the UI list.
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

	public override void WillTerminate (NSNotification notification)
	{
		// Insert code here to tear down your application
		_watcher?.Dispose();
		_watcher = null;
	}
}
