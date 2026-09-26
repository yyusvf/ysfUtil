using System.Drawing;
using System.Windows.Forms;
using YsfUtil.Ui.Theme;

namespace YsfUtil.Ui;

/// <summary>
/// The tray menu in the flyout's colours, in both themes.
///
/// WinForms has no themes; the built-in renderer takes system colours. So the colour table is
/// replaced rather than the drawing, keeping sizes, keyboard handling and checkmarks familiar.
/// The table reads <see cref="Palette"/> on every paint, so theme switches need nothing extra.
/// </summary>
internal sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer
{
    public ThemedMenuRenderer() : base(new ThemedColours())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Palette.Stroke);
        Rectangle bounds = e.AffectedBounds;
        e.Graphics.DrawRectangle(pen, 0, 0, bounds.Width - 1, bounds.Height - 1);
    }

    /// <summary>The checkmark of a checked item - in the accent rather than the system colour.</summary>
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var box = new Rectangle(e.ImageRectangle.X, e.ImageRectangle.Y,
                                e.ImageRectangle.Width, e.ImageRectangle.Height);

        using var brush = new SolidBrush(Palette.Accent);
        using var pen = new Pen(Palette.OnAccent, 2f);

        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.FillEllipse(brush, box);

        float left = box.Left + box.Width * 0.28f;
        float middle = box.Left + box.Width * 0.45f;
        float right = box.Left + box.Width * 0.74f;
        float top = box.Top + box.Height * 0.36f;
        float bottom = box.Top + box.Height * 0.66f;
        float centre = box.Top + box.Height * 0.52f;

        e.Graphics.DrawLines(pen, [
            new PointF(left, centre),
            new PointF(middle, bottom),
            new PointF(right, top),
        ]);
    }

    private sealed class ThemedColours : ProfessionalColorTable
    {
        public ThemedColours()
        {
            // Otherwise WinForms mixes in system colours.
            UseSystemColors = false;
        }

        public override Color ToolStripDropDownBackground => Palette.Window;
        public override Color ImageMarginGradientBegin => Palette.Window;
        public override Color ImageMarginGradientMiddle => Palette.Window;
        public override Color ImageMarginGradientEnd => Palette.Window;

        public override Color MenuItemSelected => Palette.ControlHover;
        public override Color MenuItemSelectedGradientBegin => Palette.ControlHover;
        public override Color MenuItemSelectedGradientEnd => Palette.ControlHover;
        public override Color MenuItemBorder => Palette.ControlHover;
        public override Color MenuItemPressedGradientBegin => Palette.Card;
        public override Color MenuItemPressedGradientMiddle => Palette.Card;
        public override Color MenuItemPressedGradientEnd => Palette.Card;

        public override Color MenuBorder => Palette.Stroke;
        public override Color SeparatorDark => Palette.Divider;
        public override Color SeparatorLight => Palette.Divider;

        public override Color CheckBackground => Palette.AccentSoft;
        public override Color CheckSelectedBackground => Palette.AccentSoft;
        public override Color CheckPressedBackground => Palette.AccentSoft;
    }
}
