using System;
using AppKit;
using Foundation;

namespace cbm;

public sealed class HistoryTableDelegate : NSTableViewDelegate
{
    private readonly ClipboardWatcher _watcher;
    private readonly NSTableView _table;
    private bool _suppressSelection;

    private const string CellId = "HistoryCell";

    public HistoryTableDelegate(ClipboardWatcher watcher, NSTableView table)
    {
        _watcher = watcher;
        _table = table;
    }

    public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row)
    {
        var text = _watcher.History[(int)row];

        var cell = tableView.MakeView(CellId, this) as HoverTableCellView;
        if (cell == null)
        {
            cell = new HoverTableCellView { Identifier = CellId };
            cell.CloseButton.Activated += CloseButtonActivated;

            var tf = new NSTextField
            {
                Editable = false,
                Bordered = false,
                DrawsBackground = false,
                LineBreakMode = NSLineBreakMode.ByWordWrapping,
                Font = NSFont.SystemFontOfSize(11)
            };
            tf.Cell.Wraps = true;
            tf.Cell.Scrollable = false;
            tf.Cell.UsesSingleLineMode = false;

            tf.TranslatesAutoresizingMaskIntoConstraints = false;
            cell.AddSubview(tf);
            cell.TextField = tf;

            // Simple padding + full width/height constraints
            NSLayoutConstraint.ActivateConstraints(new[]
            {
                tf.LeadingAnchor.ConstraintEqualTo(cell.LeadingAnchor, 0),
                tf.TrailingAnchor.ConstraintEqualTo(cell.TrailingAnchor, -18),
                tf.TopAnchor.ConstraintEqualTo(cell.TopAnchor, 6),
                tf.BottomAnchor.ConstraintEqualTo(cell.BottomAnchor, -6),
            });
        }

        cell.CloseButton.Tag = row;
        cell.CloseButton.Hidden = true;
        cell.TextField!.StringValue = TrimForDisplay(text);
        return cell;
    }

    public override void SelectionDidChange(NSNotification notification)
    {
        if (_suppressSelection)
            return;

        if (notification.Object is not NSTableView table)
            return;

        var row = (int)table.SelectedRow;
        if (row < 0 || row >= _watcher.History.Count)
            return;

        var selected = _watcher.History[row];

        // Copy back + move to top without re-triggering selection events
        _suppressSelection = true;
        try
        {
            _watcher.Activate(selected);
            table.ReloadData();
            table.SelectRow(0, byExtendingSelection: false);
            table.ScrollRowToVisible(0);
        }
        finally
        {
            _suppressSelection = false;
        }
    }

    public void PerformSelectionSilently(Action action)
    {
        _suppressSelection = true;
        try
        {
            action();
        }
        finally
        {
            _suppressSelection = false;
        }
    }

    private void CloseButtonActivated(object? sender, EventArgs e)
    {
        if (sender is not NSButton button)
            return;

        var row = (int)button.Tag;
        if (row < 0 || row >= _watcher.History.Count)
            return;

        PerformSelectionSilently(() =>
        {
            if (!_watcher.RemoveAt(row))
                return;

            _table.ReloadData();

            if (_table.RowCount > 0)
            {
                var nextRow = Math.Min(row, (int)_table.RowCount - 1);
                _table.SelectRow(nextRow, byExtendingSelection: false);
                _table.ScrollRowToVisible(nextRow);
            }
        });
    }

    private static string TrimForDisplay(string s)
    {
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length <= 250 ? s : s[..250] + "…";
    }
}
