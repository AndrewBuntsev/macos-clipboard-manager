namespace cbm;

/// <summary>
/// Delegate for the history table view, responsible for providing cell views and handling selection.
/// </summary>
public sealed class HistoryTableDelegate : NSTableViewDelegate
{
    private const string CELL_ID = "HistoryCell";

    private readonly ClipboardWatcher clipboardWatcher;
    private readonly NSTableView table;
    private bool suppressSelection;

    

    public HistoryTableDelegate(ClipboardWatcher clipboardWatcher, NSTableView table)
    {
        this.clipboardWatcher = clipboardWatcher;
        this.table = table;
    }

    public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row)
    {
        var text = clipboardWatcher.History[(int)row];

        var cell = tableView.MakeView(CELL_ID, this) as HoverTableCellView;
        if (cell == null)
        {
            cell = new HoverTableCellView { Identifier = CELL_ID };
            cell.CloseButton.Activated += CloseButtonActivated;

            var textField = new NSTextField
            {
                Editable = false,
                Bordered = false,
                DrawsBackground = false,
                LineBreakMode = NSLineBreakMode.ByWordWrapping,
                Font = NSFont.SystemFontOfSize(11)
            };
            textField.Cell.Wraps = true;
            textField.Cell.Scrollable = false;
            textField.Cell.UsesSingleLineMode = false;

            textField.TranslatesAutoresizingMaskIntoConstraints = false;
            cell.AddSubview(textField);
            cell.TextField = textField;

            // Simple padding + full width/height constraints
            NSLayoutConstraint.ActivateConstraints(new[]
            {
                textField.LeadingAnchor.ConstraintEqualTo(cell.LeadingAnchor, 0),
                textField.TrailingAnchor.ConstraintEqualTo(cell.TrailingAnchor, -18),
                textField.TopAnchor.ConstraintEqualTo(cell.TopAnchor, 6),
                textField.BottomAnchor.ConstraintEqualTo(cell.BottomAnchor, -6),
            });
        }

        cell.CloseButton.Tag = row;
        cell.CloseButton.Hidden = true;
        cell.TextField!.StringValue = TrimForDisplay(text);
        cell.ToolTip = text;
        cell.TextField.ToolTip = text;
        return cell;
    }

    public override void SelectionDidChange(NSNotification notification)
    {
        if (suppressSelection)
            return;

        if (notification.Object is not NSTableView table)
            return;

        var row = (int)table.SelectedRow;
        if (row < 0 || row >= clipboardWatcher.History.Count)
            return;

        var selected = clipboardWatcher.History[row];

        // Copy back + move to top without re-triggering selection events
        suppressSelection = true;
        try
        {
            clipboardWatcher.Activate(selected);
            table.ReloadData();
            table.SelectRow(0, byExtendingSelection: false);
            table.ScrollRowToVisible(0);
        }
        finally
        {
            suppressSelection = false;
        }
    }

    public void PerformSelectionSilently(Action action)
    {
        suppressSelection = true;
        try
        {
            action();
        }
        finally
        {
            suppressSelection = false;
        }
    }

    private void CloseButtonActivated(object? sender, EventArgs e)
    {
        if (sender is not NSButton button)
            return;

        var row = (int)button.Tag;
        if (row < 0 || row >= clipboardWatcher.History.Count)
            return;

        PerformSelectionSilently(() =>
        {
            if (!clipboardWatcher.RemoveAt(row))
                return;

            table.ReloadData();

            if (table.RowCount > 0)
            {
                var nextRow = Math.Min(row, (int)table.RowCount - 1);
                table.SelectRow(nextRow, byExtendingSelection: false);
                table.ScrollRowToVisible(nextRow);
            }
        });
    }

    private static string TrimForDisplay(string s)
    {
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length <= 250 ? s : s[..250] + "…";
    }
}
