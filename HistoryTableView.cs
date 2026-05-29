namespace cbm;

/// <summary>
/// Custom NSTableView to handle right-click selection without activating the window first.
/// </summary>
public sealed class HistoryTableView : NSTableView
{
    private const nint HISTORY_COLUMN = 0;

    public override void SetFrameSize(CGSize newSize)
    {
        var oldWidth = Frame.Width;
        base.SetFrameSize(newSize);

        if (Math.Abs((double)(newSize.Width - oldWidth)) < 0.5 || RowCount <= 0)
            return;

        NoteHeightOfRowsWithIndexesChanged(
            NSIndexSet.FromNSRange(new NSRange(0, RowCount))
        );
    }

    public override void ScrollWheel(NSEvent? theEvent)
    {
        ResetVisibleHoverState();
        if (theEvent != null)
            base.ScrollWheel(theEvent);
        ResetVisibleHoverState();
    }

    public override bool AcceptsFirstMouse(NSEvent? theEvent)
    {
        // Allow right-clicks to act without first activating the window.
        return theEvent?.Type == NSEventType.RightMouseDown ||
               theEvent?.Type == NSEventType.OtherMouseDown;
    }

    public override bool ShouldDelayWindowOrderingForEvent(NSEvent? theEvent)
    {
        if (theEvent?.Type == NSEventType.RightMouseDown ||
            theEvent?.Type == NSEventType.OtherMouseDown)
            return true;

        return theEvent != null && base.ShouldDelayWindowOrderingForEvent(theEvent);
    }

    public override void RightMouseDown(NSEvent? theEvent)
    {
        if (theEvent == null)
            return;

        var location = ConvertPointFromView(theEvent.LocationInWindow, null);
        var row = GetRow(location);
        if (row >= 0)
        {
            SelectRow(row, byExtendingSelection: false);
            ScrollRowToVisible(row);
        }

        base.RightMouseDown(theEvent);
    }

    private void ResetVisibleHoverState()
    {
        var visibleRows = RowsInRect(VisibleRect());
        var length = (nint)visibleRows.Length;
        if (length <= 0)
            return;

        var start = (nint)visibleRows.Location;
        var end = start + length;
        if (end > RowCount)
            end = RowCount;

        for (var row = start; row < end; row++)
        {
            if (GetView(HISTORY_COLUMN, row, makeIfNecessary: false) is HoverTableCellView cell)
                cell.ResetHoverState();
        }
    }
}
