namespace cbm;

/// <summary>
/// Delegate for the history table view, responsible for providing cell views and handling selection.
/// </summary>
public sealed class HistoryTableDelegate : NSTableViewDelegate
{
    private const string CELL_ID = "HistoryCell";
    private const double DELETE_ANIMATION_SECONDS = 0.25;
    private const int MIN_VISIBLE_LINES = 1;
    private const int MAX_VISIBLE_LINES = 4;
    private const double ROW_VERTICAL_PADDING = 12;
    private const double ROW_SLOT_LINE_HEIGHT = 16;
    private const double MIN_TEXT_WIDTH = 40;

    private static readonly NSFont RowTextFont = NSFont.SystemFontOfSize(11)!;
    private static readonly double MeasurementLineHeight = Math.Max(
        1,
        (double)(RowTextFont.Ascender - RowTextFont.Descender + RowTextFont.Leading)
    );
    private static readonly NSStringAttributes RowTextAttributes = new()
    {
        Font = RowTextFont
    };

    private readonly ClipboardWatcher clipboardWatcher;
    private readonly NSTableView table;
    private bool suppressSelection;

    public HistoryTableDelegate(ClipboardWatcher clipboardWatcher, NSTableView table)
    {
        this.clipboardWatcher = clipboardWatcher;
        this.table = table;
    }

    public override nfloat GetRowHeight(NSTableView tableView, nint row)
    {
        if (row < 0 || row >= clipboardWatcher.History.Count)
            return HeightForLines(MIN_VISIBLE_LINES);

        var item = clipboardWatcher.History[(int)row];
        var text = TrimForDisplay(FormatItemForDisplay(item));
        var wrappedLines = MeasureWrappedLineCount(text, tableView);
        if (item.Kind == ClipboardHistoryItemKind.Image)
            wrappedLines = Math.Max(wrappedLines, 3);

        return HeightForLines(Math.Clamp(wrappedLines, MIN_VISIBLE_LINES, MAX_VISIBLE_LINES));
    }

    public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row)
    {
        var item = clipboardWatcher.History[(int)row];
        var text = FormatItemForDisplay(item);
        var toolTip = FormatItemToolTip(item);

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
                Font = RowTextFont
            };
            textField.Cell.Wraps = true;
            textField.Cell.Scrollable = false;
            textField.Cell.UsesSingleLineMode = false;

            textField.TranslatesAutoresizingMaskIntoConstraints = false;
            cell.AddSubview(textField);
            cell.TextField = textField;

            // Simple padding + full width/height constraints
            cell.TextLeadingConstraint = textField.LeadingAnchor.ConstraintEqualTo(cell.LeadingAnchor, 0);
            NSLayoutConstraint.ActivateConstraints(new[]
            {
                cell.TextLeadingConstraint,
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
        ConfigurePinButton(cell, item.IsPinned);
        cell.SetPreviewImage(LoadPreviewImage(item));
        cell.ResetHoverState();
        cell.TextField!.StringValue = TrimForDisplay(text);
        cell.ToolTip = toolTip;
        cell.TextField.ToolTip = toolTip;
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

        var selectedItem = clipboardWatcher.History[row];

        // Copy back + move to top without re-triggering selection events
        suppressSelection = true;
        try
        {
            clipboardWatcher.Activate(selectedItem);
            table.ReloadData();

            var selectedRow = clipboardWatcher.IndexOf(selectedItem);
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

        var item = clipboardWatcher.History[row];
        if (button.Superview is NSView tileView)
        {
            FadeOutAndDelete(tileView, item);
            return;
        }

        DeleteItem(item);
    }

    private void FadeOutAndDelete(NSView tileView, ClipboardHistoryItem item)
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
                DeleteItem(item);
            }
        );
    }

    private void DeleteItem(ClipboardHistoryItem item)
    {
        PerformSelectionSilently(() =>
        {
            var row = clipboardWatcher.IndexOf(item);
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

        var item = clipboardWatcher.History[row];

        PerformSelectionSilently(() =>
        {
            if (!clipboardWatcher.TogglePinnedAt(row))
                return;

            table.ReloadData();

            var newRow = clipboardWatcher.IndexOf(item);
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

    private static string FormatItemForDisplay(ClipboardHistoryItem item) =>
        item.Kind switch
        {
            ClipboardHistoryItemKind.Image => string.Empty,
            ClipboardHistoryItemKind.FileList => FormatFileListForDisplay(item),
            _ => item.Text
        };

    private static string FormatItemToolTip(ClipboardHistoryItem item) =>
        item.Kind == ClipboardHistoryItemKind.FileList
            ? string.Join(Environment.NewLine, GetFilePaths(item))
            : item.Text;

    private static string FormatFileListForDisplay(ClipboardHistoryItem item)
    {
        var paths = GetFilePaths(item);
        if (paths.Count == 0)
            return "📄 File";

        if (paths.Count == 1)
            return $"{GetFileIcon(paths[0])} {paths[0]}";

        var icon = paths.All(Directory.Exists) ? "📁" : "📄";
        return $"{icon} {paths.Count} items{Environment.NewLine}{string.Join(Environment.NewLine, paths)}";
    }

    private static string GetFileIcon(string path) =>
        Directory.Exists(path) ? "📁" : "📄";

    private NSImage? LoadPreviewImage(ClipboardHistoryItem item)
    {
        if (item.Kind != ClipboardHistoryItemKind.Image)
            return null;

        var payloadPath = clipboardWatcher.GetPayloadPath(item);
        return payloadPath != null && File.Exists(payloadPath)
            ? new NSImage(payloadPath)
            : null;
    }

    private static List<string> GetFilePaths(ClipboardHistoryItem item)
    {
        if (item.FilePaths != null)
            return item.FilePaths;

        return item.Text
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }

    private static int MeasureWrappedLineCount(string text, NSTableView tableView)
    {
        var width = Math.Max(MIN_TEXT_WIDTH, (double)tableView.Bounds.Width);
        using var nsText = new NSString(text);
        var bounds = NSExtendedStringDrawing.GetBoundingRect(
            nsText,
            new CGSize((nfloat)width, 10_000),
            NSStringDrawingOptions.UsesLineFragmentOrigin | NSStringDrawingOptions.UsesFontLeading,
            RowTextAttributes,
            null
        );

        return Math.Max(1, (int)Math.Ceiling((double)bounds.Height / MeasurementLineHeight));
    }

    private static nfloat HeightForLines(int lineCount) =>
        (nfloat)(ROW_VERTICAL_PADDING + lineCount * ROW_SLOT_LINE_HEIGHT);

    private static string TrimForDisplay(string s)
    {
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length <= 250 ? s : s[..250] + "…";
    }
}
