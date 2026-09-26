using System.Drawing;
using System.Drawing.Drawing2D;

namespace YsfUtil.Ui;

/// <summary>
/// Das Erscheinungsbild des Programms an einer Stelle: vier Kacheln im Raster, die rechte
/// obere auf die Spitze gestellt - ein Werkzeugkasten aus kleinen Einzelteilen. Im Indigo der
/// App-Familie (wie Audio Mirror), ohne Hintergrund und ohne Verlauf.
///
/// Infobereich, Taskleiste, Fenster und Titelleiste kommen alle hierher; ysfUtil.ico ist nur
/// das Abbild davon (siehe <see cref="AppIconFile"/>).
/// </summary>
internal static class AppIconArt
{
    public static readonly Color Colour = Color.FromArgb(0x63, 0x5D, 0xF1);

    private const float Reference = 32f;
    private const float Tile = 12f;
    private const float Gap = 3f;
    private const float Radius = 3.2f;

    /// <summary>
    /// Vierfach vergrößert gezeichnet und dann verkleinert - bei 16 Pixeln wären die Fugen
    /// sonst nur ein grauer Schleier.
    /// </summary>
    public static Bitmap Render(int size)
    {
        const int oversample = 4;
        using Bitmap large = Draw(size * oversample);

        var result = new Bitmap(size, size);
        using Graphics g = Graphics.FromImage(result);
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(large, new Rectangle(0, 0, size, size));
        return result;
    }

    private static Bitmap Draw(int size)
    {
        var bitmap = new Bitmap(size, size);
        using Graphics g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        float s = size / Reference;
        float tile = Tile * s;
        float gap = Gap * s;
        float radius = Radius * s;
        float start = (size - (2 * tile + gap)) / 2;
        float second = start + tile + gap;

        using var brush = new SolidBrush(Colour);

        FillRounded(g, brush, start, start, tile, radius);
        FillRounded(g, brush, start, second, tile, radius);
        FillRounded(g, brush, second, second, tile, radius);

        // Die Raute: dieselbe Kachel, um 45 Grad gedreht und etwas kleiner, damit ihre Spitzen
        // nicht über das Raster hinausragen.
        GraphicsState state = g.Save();
        float centre = second + tile / 2;
        g.TranslateTransform(centre, start + tile / 2);
        g.RotateTransform(45);
        float diamond = tile * 0.74f;
        FillRounded(g, brush, -diamond / 2, -diamond / 2, diamond, radius * 0.8f);
        g.Restore(state);

        return bitmap;
    }

    private static void FillRounded(Graphics g, Brush brush, float x, float y, float edge, float radius)
    {
        float d = radius * 2;
        using var path = new GraphicsPath();
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + edge - d, y, d, d, 270, 90);
        path.AddArc(x + edge - d, y + edge - d, d, d, 0, 90);
        path.AddArc(x, y + edge - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}
