using AppKit;
using Foundation;

namespace cbm;

public sealed class HistoryTableDataSource : NSTableViewDataSource
{
    private readonly ClipboardWatcher _watcher;

    public HistoryTableDataSource(ClipboardWatcher watcher)
    {
        _watcher = watcher;
    }

    public override nint GetRowCount(NSTableView tableView)
        => _watcher.History.Count;
}
