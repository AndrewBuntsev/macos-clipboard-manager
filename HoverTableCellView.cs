namespace cbm;

/// <summary>
/// Custom NSTableCellView that shows a close button on hover, allowing individual items to be removed from the history.
/// </summary>
public sealed class HoverTableCellView : NSTableCellView
{
    private NSTrackingArea? trackingArea;

    public NSButton CloseButton { get; }

    public HoverTableCellView()
    {
        CloseButton = new NSButton
        {
            Title = "❌",
            Bordered = false,
            Font = NSFont.BoldSystemFontOfSize(13),
            Hidden = true
        };
        CloseButton.BezelColor = NSColor.SystemRed;
        CloseButton.AttributedTitle = new NSAttributedString(
            "❌",
            new NSStringAttributes { ForegroundColor = NSColor.White }
        );
        CloseButton.SetButtonType(NSButtonType.MomentaryChange);
        CloseButton.TranslatesAutoresizingMaskIntoConstraints = false;

        AddSubview(CloseButton);

        NSLayoutConstraint.ActivateConstraints(new[]
        {
            CloseButton.TrailingAnchor.ConstraintEqualTo(TrailingAnchor, 6),
            CloseButton.TopAnchor.ConstraintEqualTo(TopAnchor, 2),
            CloseButton.WidthAnchor.ConstraintEqualTo(18),
            CloseButton.HeightAnchor.ConstraintEqualTo(18)
        });
    }

    public override void UpdateTrackingAreas()
    {
        if (trackingArea != null)
            RemoveTrackingArea(trackingArea);

        trackingArea = new NSTrackingArea(
            Bounds,
            NSTrackingAreaOptions.ActiveInKeyWindow |
            NSTrackingAreaOptions.MouseEnteredAndExited |
            NSTrackingAreaOptions.InVisibleRect,
            this,
            null);

        AddTrackingArea(trackingArea);
        base.UpdateTrackingAreas();
    }

    public override void MouseEntered(NSEvent theEvent)
    {
        CloseButton.Hidden = false;
        base.MouseEntered(theEvent);
    }

    public override void MouseExited(NSEvent theEvent)
    {
        CloseButton.Hidden = true;
        base.MouseExited(theEvent);
    }
}
