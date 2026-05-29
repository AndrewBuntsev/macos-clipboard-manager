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
    public NSImageView PreviewImageView { get; }
    public NSLayoutConstraint? TextLeadingConstraint { get; set; }

    public HoverTableCellView()
    {
        PreviewImageView = new NSImageView
        {
            Hidden = true,
            ImageScaling = NSImageScale.ProportionallyUpOrDown,
            TranslatesAutoresizingMaskIntoConstraints = false
        };

        PinButton = new NSButton
        {
            Title = "📍",
            Bordered = false,
            Font = NSFont.BoldSystemFontOfSize(13)!,
            Hidden = true
        };
        PinButton.SetButtonType(NSButtonType.MomentaryChange);
        PinButton.TranslatesAutoresizingMaskIntoConstraints = false;

        CloseButton = new NSButton
        {
            Title = "❌",
            Bordered = false,
            Font = NSFont.BoldSystemFontOfSize(13)!,
            Hidden = true,
            BezelColor = NSColor.SystemRed,
            AttributedTitle = new NSAttributedString(
                "❌",
                new NSStringAttributes { ForegroundColor = NSColor.White }
            )
        };
        CloseButton.SetButtonType(NSButtonType.MomentaryChange);
        CloseButton.TranslatesAutoresizingMaskIntoConstraints = false;

        AddSubview(PreviewImageView);
        AddSubview(PinButton);
        AddSubview(CloseButton);

        NSLayoutConstraint.ActivateConstraints(new[]
        {
            PreviewImageView.LeadingAnchor.ConstraintEqualTo(LeadingAnchor, 4),
            PreviewImageView.TopAnchor.ConstraintEqualTo(TopAnchor, 8),
            PreviewImageView.WidthAnchor.ConstraintEqualTo(36),
            PreviewImageView.HeightAnchor.ConstraintEqualTo(36),
            CloseButton.TrailingAnchor.ConstraintEqualTo(TrailingAnchor, 8),
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
        ResetHoverState();
    }

    public void ResetHoverState()
    {
        CloseButton.Hidden = true;
        PinButton.Hidden = !pinAlwaysVisible;
    }

    public void SetPreviewImage(NSImage? image)
    {
        PreviewImageView.Image = image;
        PreviewImageView.Hidden = image == null;

        if (TextLeadingConstraint != null)
            TextLeadingConstraint.Constant = image == null ? 0 : 44;
    }
}
