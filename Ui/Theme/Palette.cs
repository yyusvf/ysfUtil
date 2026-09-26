using System.Windows.Media;
using Application = System.Windows.Application;
using DrawingColor = System.Drawing.Color;

namespace YsfUtil.Ui.Theme;

/// <summary>
/// Colours of the loaded palette for the part not drawn in WPF - the tray menu is WinForms.
///
/// No second colour table on purpose: values are read from the loaded resources on every
/// access, so a light/dark switch arrives here too.
/// </summary>
internal static class Palette
{
    public static DrawingColor Window => Get("WindowBrush");
    public static DrawingColor Card => Get("CardBrush");
    public static DrawingColor ControlHover => Get("ControlHoverBrush");
    public static DrawingColor Stroke => Get("StrokeBrush");
    public static DrawingColor Divider => Get("DividerBrush");
    public static DrawingColor Text => Get("TextPrimaryBrush");
    public static DrawingColor TextDisabled => Get("TextDisabledBrush");
    public static DrawingColor Accent => Get("AccentBrush");
    public static DrawingColor OnAccent => Get("OnAccentBrush");

    /// <summary>
    /// Opaque version of the soft accent. WinForms has no transparency between controls, so the
    /// value is blended against the window colour once.
    /// </summary>
    public static DrawingColor AccentSoft => Flatten(Brush("AccentSoftBrush"), Window);

    private static DrawingColor Get(string key) => ToDrawing(Brush(key));

    private static Color Brush(string key)
    {
        // Before the WPF application exists this stays black - never visible, since the menu
        // is only built afterwards.
        if (Application.Current?.TryFindResource(key) is SolidColorBrush brush)
        {
            return brush.Color;
        }
        return Colors.Black;
    }

    private static DrawingColor ToDrawing(Color c) => DrawingColor.FromArgb(c.A, c.R, c.G, c.B);

    /// <summary>Lays a translucent colour over a background and returns the opaque result.</summary>
    private static DrawingColor Flatten(Color front, DrawingColor back)
    {
        float a = front.A / 255f;
        return DrawingColor.FromArgb(
            255,
            (int)(front.R * a + back.R * (1 - a)),
            (int)(front.G * a + back.G * (1 - a)),
            (int)(front.B * a + back.B * (1 - a)));
    }
}
