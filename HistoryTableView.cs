namespace cbm;

/// <summary>
/// Custom NSTableView to handle right-click selection without activating the window first.
/// </summary>
public sealed class HistoryTableView : NSTableView
{
    public override bool AcceptsFirstMouse(NSEvent theEvent)
    {
        // Allow right-clicks to act without first activating the window.
        return theEvent.Type == NSEventType.RightMouseDown ||
               theEvent.Type == NSEventType.OtherMouseDown;
    }

    public override bool ShouldDelayWindowOrderingForEvent(NSEvent theEvent)
    {
        if (theEvent.Type == NSEventType.RightMouseDown ||
            theEvent.Type == NSEventType.OtherMouseDown)
            return true;

        return base.ShouldDelayWindowOrderingForEvent(theEvent);
    }

    public override void RightMouseDown(NSEvent theEvent)
    {
        var location = ConvertPointFromView(theEvent.LocationInWindow, null);
        var row = GetRow(location);
        if (row >= 0)
        {
            SelectRow(row, byExtendingSelection: false);
            ScrollRowToVisible(row);
        }

        base.RightMouseDown(theEvent);
    }
}
