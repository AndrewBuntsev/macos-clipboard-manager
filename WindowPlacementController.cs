using System.Globalization;

namespace cbm;

using AppKit;
using CoreGraphics;
using Foundation;

public sealed class WindowPlacementController
{
    private const string KEY_PREFIX = "WindowPlacement.";
    private const string LOCKED_KEY = KEY_PREFIX + "Locked";
    private const string DISPLAY_ID_KEY = KEY_PREFIX + "DisplayId";
    private const string SCREEN_X_KEY = KEY_PREFIX + "ScreenX";
    private const string SCREEN_Y_KEY = KEY_PREFIX + "ScreenY";
    private const string SCREEN_WIDTH_KEY = KEY_PREFIX + "ScreenWidth";
    private const string SCREEN_HEIGHT_KEY = KEY_PREFIX + "ScreenHeight";
    private const string EDGE_KEY = KEY_PREFIX + "Edge";
    private const string WIDTH_KEY = KEY_PREFIX + "Width";
    private const string HEIGHT_KEY = KEY_PREFIX + "Height";
    private const string Y_OFFSET_KEY = KEY_PREFIX + "YOffset";
    private const string FULL_HEIGHT_KEY = KEY_PREFIX + "FullHeight";

    private const double DEFAULT_WIDTH = 150;
    private const double MIN_WIDTH = 96;
    private const double MIN_HEIGHT = 120;
    private const double EDGE_TOLERANCE = 10;
    private const double FULL_HEIGHT_TOLERANCE = 16;
    private const double SCREEN_MATCH_TOLERANCE = 8;

    private readonly NSWindow window;
    private readonly NSUserDefaults defaults = NSUserDefaults.StandardUserDefaults;

    public WindowPlacementController(NSWindow window)
    {
        this.window = window;
    }

    public bool IsLocked => defaults.BoolForKey(LOCKED_KEY);

    public void ApplyInitialPlacement()
    {
        if (!ApplyLockedPlacement())
            DockDefaultRight();
    }

    public bool ApplyLockedPlacement()
    {
        var placement = LoadPlacement();
        if (placement == null || !placement.IsLocked)
            return false;

        var screen = FindSavedScreen(placement) ?? FindWindowScreen() ?? NSScreen.MainScreen;
        if (screen == null)
            return false;

        ApplyPlacement(placement, screen);
        return true;
    }

    public void LockCurrentPosition()
    {
        var screen = FindWindowScreen() ?? NSScreen.MainScreen;
        if (screen == null)
            return;

        var placement = BuildCurrentPlacement(screen);
        SavePlacement(placement);
        ApplyPlacement(placement, screen);
        Log.Info($"Locked window placement: {placement.Edge} edge");
    }

    public void Unlock()
    {
        defaults.SetBool(false, LOCKED_KEY);
        defaults.Synchronize();
        Log.Info("Unlocked window placement");
    }

    public void DockLeftOnCurrentDisplay()
    {
        DockOnCurrentDisplay(DockEdge.Left);
    }

    public void DockRightOnCurrentDisplay()
    {
        DockOnCurrentDisplay(DockEdge.Right);
    }

    private void DockDefaultRight()
    {
        var screen = window.Screen ?? NSScreen.MainScreen;
        if (screen == null)
            return;

        ApplyPlacement(
            new Placement(
                IsLocked: false,
                DisplayId: GetDisplayId(screen),
                ScreenFrame: screen.VisibleFrame,
                Edge: DockEdge.Right,
                Width: DEFAULT_WIDTH,
                Height: screen.VisibleFrame.Height,
                YOffset: 0,
                FullHeight: true
            ),
            screen
        );
    }

    private void DockOnCurrentDisplay(DockEdge edge)
    {
        var screen = FindWindowScreen() ?? NSScreen.MainScreen;
        if (screen == null)
            return;

        var visible = screen.VisibleFrame;
        var placement = new Placement(
            IsLocked: true,
            DisplayId: GetDisplayId(screen),
            ScreenFrame: visible,
            Edge: edge,
            Width: Clamp(window.Frame.Width, MIN_WIDTH, visible.Width),
            Height: visible.Height,
            YOffset: 0,
            FullHeight: true
        );

        SavePlacement(placement);
        ApplyPlacement(placement, screen);
        Log.Info($"Docked window to {edge} edge");
    }

    private Placement BuildCurrentPlacement(NSScreen screen)
    {
        var frame = window.Frame;
        var visible = screen.VisibleFrame;
        var leftDistance = Math.Abs(frame.X - visible.X);
        var rightDistance = Math.Abs((visible.X + visible.Width) - (frame.X + frame.Width));
        var edge = leftDistance <= rightDistance ? DockEdge.Left : DockEdge.Right;
        var fullHeight =
            Math.Abs(frame.Y - visible.Y) <= EDGE_TOLERANCE &&
            Math.Abs(frame.Height - visible.Height) <= FULL_HEIGHT_TOLERANCE;
        var height = fullHeight
            ? visible.Height
            : Clamp(frame.Height, MIN_HEIGHT, visible.Height);
        var yOffset = fullHeight
            ? 0
            : Clamp(frame.Y - visible.Y, 0, Math.Max(0, visible.Height - height));

        return new Placement(
            IsLocked: true,
            DisplayId: GetDisplayId(screen),
            ScreenFrame: visible,
            Edge: edge,
            Width: Clamp(frame.Width, MIN_WIDTH, visible.Width),
            Height: height,
            YOffset: yOffset,
            FullHeight: fullHeight
        );
    }

    private void ApplyPlacement(Placement placement, NSScreen screen)
    {
        var visible = screen.VisibleFrame;
        var width = Clamp(placement.Width, MIN_WIDTH, visible.Width);
        var height = placement.FullHeight
            ? visible.Height
            : Clamp(placement.Height, MIN_HEIGHT, visible.Height);
        var y = placement.FullHeight
            ? visible.Y
            : visible.Y + Clamp(placement.YOffset, 0, Math.Max(0, visible.Height - height));
        var x = placement.Edge == DockEdge.Left
            ? visible.X
            : visible.X + visible.Width - width;

        window.SetFrame(new CGRect(x, y, width, height), display: true);
    }

    private void SavePlacement(Placement placement)
    {
        defaults.SetBool(placement.IsLocked, LOCKED_KEY);
        if (placement.DisplayId.HasValue)
        {
            defaults.SetString(
                placement.DisplayId.Value.ToString(CultureInfo.InvariantCulture),
                DISPLAY_ID_KEY
            );
        }
        else
        {
            defaults.RemoveObject(DISPLAY_ID_KEY);
        }

        defaults.SetDouble(placement.ScreenFrame.X, SCREEN_X_KEY);
        defaults.SetDouble(placement.ScreenFrame.Y, SCREEN_Y_KEY);
        defaults.SetDouble(placement.ScreenFrame.Width, SCREEN_WIDTH_KEY);
        defaults.SetDouble(placement.ScreenFrame.Height, SCREEN_HEIGHT_KEY);
        defaults.SetString(placement.Edge.ToString(), EDGE_KEY);
        defaults.SetDouble(placement.Width, WIDTH_KEY);
        defaults.SetDouble(placement.Height, HEIGHT_KEY);
        defaults.SetDouble(placement.YOffset, Y_OFFSET_KEY);
        defaults.SetBool(placement.FullHeight, FULL_HEIGHT_KEY);
        defaults.Synchronize();
    }

    private Placement? LoadPlacement()
    {
        if (!defaults.BoolForKey(LOCKED_KEY))
            return null;

        if (!Enum.TryParse(defaults.StringForKey(EDGE_KEY), out DockEdge edge))
            edge = DockEdge.Right;

        return new Placement(
            IsLocked: true,
            DisplayId: LoadDisplayId(),
            ScreenFrame: new CGRect(
                defaults.DoubleForKey(SCREEN_X_KEY),
                defaults.DoubleForKey(SCREEN_Y_KEY),
                defaults.DoubleForKey(SCREEN_WIDTH_KEY),
                defaults.DoubleForKey(SCREEN_HEIGHT_KEY)
            ),
            Edge: edge,
            Width: defaults.DoubleForKey(WIDTH_KEY) > 0
                ? defaults.DoubleForKey(WIDTH_KEY)
                : DEFAULT_WIDTH,
            Height: defaults.DoubleForKey(HEIGHT_KEY) > 0
                ? defaults.DoubleForKey(HEIGHT_KEY)
                : MIN_HEIGHT,
            YOffset: defaults.DoubleForKey(Y_OFFSET_KEY),
            FullHeight: defaults.BoolForKey(FULL_HEIGHT_KEY)
        );
    }

    private long? LoadDisplayId()
    {
        var value = defaults.StringForKey(DISPLAY_ID_KEY);
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
    }

    private NSScreen? FindSavedScreen(Placement placement)
    {
        var screens = NSScreen.Screens;
        if (screens.Length == 0)
            return null;

        if (placement.DisplayId.HasValue)
        {
            foreach (var screen in screens)
            {
                if (GetDisplayId(screen) == placement.DisplayId.Value)
                    return screen;
            }
        }

        foreach (var screen in screens)
        {
            if (AreFramesSimilar(screen.VisibleFrame, placement.ScreenFrame, includeOrigin: true))
                return screen;
        }

        foreach (var screen in screens)
        {
            if (AreFramesSimilar(screen.VisibleFrame, placement.ScreenFrame, includeOrigin: false))
                return screen;
        }

        return null;
    }

    private NSScreen? FindWindowScreen()
    {
        var screens = NSScreen.Screens;
        if (screens.Length == 0)
            return window.Screen;

        NSScreen? bestScreen = null;
        var bestArea = 0d;
        foreach (var screen in screens)
        {
            var area = IntersectionArea(window.Frame, screen.Frame);
            if (area > bestArea)
            {
                bestArea = area;
                bestScreen = screen;
            }
        }

        return bestScreen ?? window.Screen;
    }

    private static long? GetDisplayId(NSScreen screen)
    {
        using var screenNumberKey = new NSString("NSScreenNumber");
        return screen.DeviceDescription.ObjectForKey(screenNumberKey) is NSNumber screenNumber
            ? screenNumber.Int64Value
            : null;
    }

    private static bool AreFramesSimilar(CGRect current, CGRect saved, bool includeOrigin)
    {
        if (includeOrigin &&
            (!NearlyEqual(current.X, saved.X) || !NearlyEqual(current.Y, saved.Y)))
        {
            return false;
        }

        return NearlyEqual(current.Width, saved.Width) &&
               NearlyEqual(current.Height, saved.Height);
    }

    private static bool NearlyEqual(double a, double b) =>
        Math.Abs(a - b) <= SCREEN_MATCH_TOLERANCE;

    private static double IntersectionArea(CGRect a, CGRect b)
    {
        var width = Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X));
        var height = Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y));
        return width * height;
    }

    private static double Clamp(double value, double min, double max) =>
        Math.Min(Math.Max(value, min), max);

    private enum DockEdge
    {
        Left,
        Right
    }

    private sealed record Placement(
        bool IsLocked,
        long? DisplayId,
        CGRect ScreenFrame,
        DockEdge Edge,
        double Width,
        double Height,
        double YOffset,
        bool FullHeight
    );
}
