namespace cbm;

/// <summary>
/// Delegate for the history table view, responsible for providing cell views and handling selection.
/// </summary>
public sealed class HistoryTableDelegate : NSTableViewDelegate
{
    private const string CELL_ID = "HistoryCell";
    private const double DELETE_ANIMATION_SECONDS = 0.25;

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
        var item = clipboardWatcher.History[(int)row];
        var text = item.Text;

        var cell = tableView.MakeView(CELL_ID, this) as HoverTableCellView;
        if (cell == null)
        {
            cell = new HoverTableCellView { Identifier = CELL_ID };
            cell.CloseButton.Activated += CloseButtonActivated;
            cell.PinButton.Activated += PinButtonActivated;

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
                textField.TrailingAnchor.ConstraintEqualTo(cell.TrailingAnchor, 0),
                textField.TopAnchor.ConstraintEqualTo(cell.TopAnchor, 6),
                textField.BottomAnchor.ConstraintEqualTo(cell.BottomAnchor, -6),
            });

            // Keep action buttons above text so text can use full width.
            cell.AddSubview(cell.PinButton, NSWindowOrderingMode.Above, textField);
            cell.AddSubview(cell.CloseButton, NSWindowOrderingMode.Above, textField);
        }

        cell.CloseButton.Tag = row;
        cell.PinButton.Tag = row;
        cell.CloseButton.Hidden = true;
        ConfigurePinButton(cell, item.IsPinned);
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

        var selectedText = clipboardWatcher.History[row].Text;

        // Copy back + move to top without re-triggering selection events
        suppressSelection = true;
        try
        {
            clipboardWatcher.Activate(selectedText);
            table.ReloadData();

            var selectedRow = clipboardWatcher.IndexOf(selectedText);
            if (selectedRow < 0)
                selectedRow = 0;

            table.SelectRow(selectedRow, byExtendingSelection: false);
            table.ScrollRowToVisible(selectedRow);
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

        var text = clipboardWatcher.History[row].Text;
        if (button.Superview is NSView tileView)
        {
            FadeOutAndDelete(tileView, text);
            return;
        }

        DeleteItem(text);
    }

    private void FadeOutAndDelete(NSView tileView, string text)
    {
        const int steps = 6;
        var step = 0;
        NSTimer? timer = null;
        timer = NSTimer.CreateRepeatingScheduledTimer(
            TimeSpan.FromSeconds(DELETE_ANIMATION_SECONDS / steps),
            _ =>
            {
                step++;
                var progress = Math.Min(1.0, (double)step / steps);
                tileView.AlphaValue = (nfloat)(1 - progress);

                if (step < steps)
                    return;

                timer?.Invalidate();
                timer?.Dispose();

                // Reset in case AppKit reuses this cell view instance.
                tileView.AlphaValue = 1;
                DeleteItem(text);
            }
        );
    }

    private void DeleteItem(string text)
    {
        PerformSelectionSilently(() =>
        {
            var row = clipboardWatcher.IndexOf(text);
            if (row < 0)
                return;

            if (!clipboardWatcher.RemoveAt(row))
                return;

            table.ReloadData();
            table.DeselectAll(null);
        });
    }

    private void PinButtonActivated(object? sender, EventArgs e)
    {
        if (sender is not NSButton button)
            return;

        var row = (int)button.Tag;
        if (row < 0 || row >= clipboardWatcher.History.Count)
            return;

        var text = clipboardWatcher.History[row].Text;

        PerformSelectionSilently(() =>
        {
            if (!clipboardWatcher.TogglePinnedAt(row))
                return;

            table.ReloadData();

            var newRow = clipboardWatcher.IndexOf(text);
            if (newRow >= 0)
            {
                table.SelectRow(newRow, byExtendingSelection: false);
                table.ScrollRowToVisible(newRow);
            }
        });
    }

    private static void ConfigurePinButton(HoverTableCellView cell, bool isPinned)
    {
        cell.PinButton.Title = isPinned ? "📌" : "📍";
        cell.PinButton.ToolTip = isPinned ? "Unpin" : "Pin";
        cell.SetPinAlwaysVisible(isPinned);
    }

    private static string TrimForDisplay(string s)
    {
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length <= 250 ? s : s[..250] + "…";
    }
}
