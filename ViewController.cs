using ObjCRuntime;

namespace cbm;

/// <summary>
/// Main view controller that sets up the UI to display clipboard history and handles interactions.
/// </summary>
public partial class ViewController : NSViewController {
	protected ViewController (NativeHandle handle) : base (handle)
	{
		// This constructor is required if the view controller is loaded from a xib or a storyboard.
		// Do not put any initialization here, use ViewDidLoad instead.
	}

	public override void ViewDidLoad ()
	{
		Log.Info("ViewController ViewDidLoad");
		base.ViewDidLoad();

		View.WantsLayer = true;

        // Get watcher from AppDelegate
        var appDelegate = (AppDelegate)NSApplication.SharedApplication.Delegate;
        var watcher = appDelegate.Watcher;

        if (watcher == null)
        {
			Log.Info("ViewController: ClipboardWatcher is null");
            // Shouldn't happen, but keep it safe
            var label = new NSTextField
            {
                StringValue = "Watcher not ready",
                Editable = false,
                Bordered = false,
                DrawsBackground = false
            };
            View.AddSubview(label);
            return;
        }

        var scroll = new NSScrollView
        {
            HasVerticalScroller = true,
            AutohidesScrollers = true,
            TranslatesAutoresizingMaskIntoConstraints = false
        };
        View.AddSubview(scroll);

        NSLayoutConstraint.ActivateConstraints(new[]
        {
            scroll.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor),
            scroll.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor),
            scroll.TopAnchor.ConstraintEqualTo(View.TopAnchor),
            scroll.BottomAnchor.ConstraintEqualTo(View.BottomAnchor)
        });

        var table = new HistoryTableView
        {
            HeaderView = null,
            UsesAlternatingRowBackgroundColors = true,
            RowHeight = 60,
            Style = NSTableViewStyle.FullWidth,
            IntercellSpacing = new CGSize(0, 0)
        };

        var col = new NSTableColumn("text")
        {
            Title = "Text",
            Editable = false
        };
        table.AddColumn(col);

        // Make the single column fill available width
        table.ColumnAutoresizingStyle = NSTableViewColumnAutoresizingStyle.LastColumnOnly;
        table.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
        table.Frame = scroll.ContentView.Bounds;
        col.Width = table.Frame.Width;
        table.DataSource = new HistoryTableDataSource(watcher);
        table.Delegate = new HistoryTableDelegate(watcher, table);
        scroll.DocumentView = table;

        // Refresh UI when clipboard changes
        watcher.OnNewText += (text) =>
        {
            // Must update UI on main thread
            BeginInvokeOnMainThread(() =>
            {
                if (table == null)
                    return;

                (table.Delegate as HistoryTableDelegate)?.PerformSelectionSilently(() =>
                {
                    table.ReloadData();

                    var row = watcher.IndexOf(text);
                    if (row >= 0)
                    {
                        table.SelectRow(row, byExtendingSelection: false);
                        table.ScrollRowToVisible(row);
                    }
                });
            });
        };

        // Initial load
        table.ReloadData();
	}

	public override NSObject RepresentedObject {
		get => base.RepresentedObject;
		set
        {
			base.RepresentedObject = value;
		}
	}
}
