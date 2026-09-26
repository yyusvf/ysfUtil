using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace YsfUtil.Ui.Flyout;

/// <summary>
/// Worüber das Flyout stehen soll: das Tray-Symbol - oder, wenn es im aufgeklappten Überlauf
/// (^) steckt, das ganze Überlauf-Fenster. So legt sich das Flyout darüber, statt den Überlauf
/// zu verdecken, wie bei Razer Synapse.
///
/// Muss bestimmt werden, solange der Überlauf noch offen ist - also beim Drücken der Maustaste,
/// nicht erst beim Loslassen.
/// </summary>
internal static class TrayAnchor
{
    private const uint GaRoot = 2;

    public static Rectangle? Resolve(Rectangle? icon)
    {
        if (icon is not Rectangle r)
        {
            return null;
        }

        var centre = new Point32 { X = r.Left + r.Width / 2, Y = r.Top + r.Height / 2 };
        IntPtr host = GetAncestor(WindowFromPoint(centre), GaRoot);
        if (host != IntPtr.Zero && !IsTaskbar(host) && GetWindowRect(host, out Rect32 popup))
        {
            return Rectangle.FromLTRB(popup.Left, popup.Top, popup.Right, popup.Bottom);
        }
        return r;
    }

    private static bool IsTaskbar(IntPtr window) =>
        ClassOf(window) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";

    private static string ClassOf(IntPtr window)
    {
        var name = new StringBuilder(128);
        GetClassName(window, name, name.Capacity);
        return name.ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point32 point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect32 rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int capacity);
}
