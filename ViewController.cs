using ObjCRuntime;
using AppKit;
using Foundation;
using CoreGraphics;

namespace cbm;

public partial class ViewController : NSViewController {
	private NSTableView? _table;
    private HistoryTableDataSource? _ds;
    private HistoryTableDelegate? _del;
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
            AutohidesScrollers = true
        };
        scroll.TranslatesAutoresizingMaskIntoConstraints = false;
        View.AddSubview(scroll);

        NSLayoutConstraint.ActivateConstraints(new[]
        {
            scroll.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor),
            scroll.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor),
            scroll.TopAnchor.ConstraintEqualTo(View.TopAnchor),
            scroll.BottomAnchor.ConstraintEqualTo(View.BottomAnchor)
        });

        var table = new NSTableView
        {
            HeaderView = null,            // no column header
            UsesAlternatingRowBackgroundColors = true,
            RowHeight = 60
        };
        table.Style = NSTableViewStyle.FullWidth;
        table.IntercellSpacing = new CGSize(0, 0);

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

        _ds = new HistoryTableDataSource(watcher);
        _del = new HistoryTableDelegate(watcher, table);

        table.DataSource = _ds;
        table.Delegate = _del;

        scroll.DocumentView = table;
        _table = table;

        // Refresh UI when clipboard changes
        watcher.OnNewText += _ =>
        {
            // Must update UI on main thread
            BeginInvokeOnMainThread(() =>
            {
                if (_table == null)
                    return;

                _del?.PerformSelectionSilently(() =>
                {
                    _table.ReloadData();

                    // Select the newest item (row 0) if exists
                    if (_table.RowCount > 0)
                    {
                        _table.SelectRow(0, byExtendingSelection: false);
                        _table.ScrollRowToVisible(0);
                    }
                });
            });
        };

        // Initial load
        _table.ReloadData();
	}

	public override NSObject RepresentedObject {
		get => base.RepresentedObject;
		set {
			base.RepresentedObject = value;

			// Update the view, if already loaded.
		}
	}
}
