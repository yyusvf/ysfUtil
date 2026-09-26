using System.Runtime.InteropServices;

namespace YsfUtil;

/// <summary>
/// Sorgt dafür, dass immer nur ein ysfUtil läuft. Ein zweiter Start meldet sich beim ersten
/// und beendet sich; der erste holt daraufhin sein Fenster nach vorn.
/// </summary>
internal static class SingleInstance
{
    private const string MutexName = @"Local\ysfUtil.SingleInstance";
    private static readonly IntPtr BroadcastWindow = new(0xFFFF);

    private static Mutex? mutex;

    /// <summary>Fensternachricht, mit der eine zweite Instanz das Fenster anfordert.</summary>
    public static uint ShowWindowMessage { get; } = RegisterWindowMessage("ysfUtil.ShowWindow");

    public static bool TryAcquire()
    {
        try
        {
            mutex = new Mutex(initiallyOwned: true, MutexName, out bool created);
            return created;
        }
        catch
        {
            return true;
        }
    }

    public static void SignalExistingInstance()
    {
        if (ShowWindowMessage != 0)
        {
            PostMessage(BroadcastWindow, ShowWindowMessage, IntPtr.Zero, IntPtr.Zero);
        }
    }

    public static void Release()
    {
        try
        {
            mutex?.ReleaseMutex();
        }
        catch
        {
            // Beim Beenden unkritisch.
        }
        mutex?.Dispose();
        mutex = null;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
}
