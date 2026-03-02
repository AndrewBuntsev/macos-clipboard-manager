namespace cbm;

/// <summary>
/// Custom NSTableCellView that shows a close button on hover, allowing individual items to be removed from the history.
/// </summary>
public sealed class HoverTableCellView : NSTableCellView
{
    private NSTrackingArea? trackingArea;
    private bool pinAlwaysVisible;

    public NSButton CloseButton { get; }
    public NSButton PinButton { get; }

    public HoverTableCellView()
    {
        PinButton = new NSButton
        {
            Title = "📍",
            Bordered = false,
            Font = NSFont.BoldSystemFontOfSize(13),
            Hidden = true
        };
        PinButton.SetButtonType(NSButtonType.MomentaryChange);
        PinButton.TranslatesAutoresizingMaskIntoConstraints = false;

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

        AddSubview(PinButton);
        AddSubview(CloseButton);

        NSLayoutConstraint.ActivateConstraints(new[]
        {
            CloseButton.TrailingAnchor.ConstraintEqualTo(TrailingAnchor, -2),
            CloseButton.TopAnchor.ConstraintEqualTo(TopAnchor, 2),
            CloseButton.WidthAnchor.ConstraintEqualTo(18),
            CloseButton.HeightAnchor.ConstraintEqualTo(18),
            PinButton.TrailingAnchor.ConstraintEqualTo(CloseButton.LeadingAnchor, -2),
            PinButton.TopAnchor.ConstraintEqualTo(TopAnchor, 2),
            PinButton.WidthAnchor.ConstraintEqualTo(18),
            PinButton.HeightAnchor.ConstraintEqualTo(18),
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
        if (!pinAlwaysVisible)
            PinButton.Hidden = false;

        base.MouseEntered(theEvent);
    }

    public override void MouseExited(NSEvent theEvent)
    {
        CloseButton.Hidden = true;
        if (!pinAlwaysVisible)
            PinButton.Hidden = true;

        base.MouseExited(theEvent);
    }

    public void SetPinAlwaysVisible(bool alwaysVisible)
    {
        pinAlwaysVisible = alwaysVisible;
        PinButton.Hidden = !pinAlwaysVisible;
    }
}
