using System.Runtime.InteropServices;
using YsfUtil.Hotkeys;
using Keys = System.Windows.Forms.Keys;

namespace YsfUtil.Features;

/// <summary>Changes the desktop shell view's icon visibility without restarting Explorer.</summary>
internal sealed class DesktopIconsToggleFeature : IFeature
{
    private const uint NoIcons = 0x1000; // FWF_NOICONS

    public string Id => "desktop-icons-toggle";
    public string Name => "Toggle desktop icons";
    public string Description => "Hides or shows all desktop icons.";
    public string Glyph => "\uE8A9"; // ViewAll in Segoe Fluent Icons
    public Hotkey? DefaultHotkey => new(HotkeyModifiers.Control | HotkeyModifiers.Alt, Keys.T);

    public string? Status
    {
        get
        {
            try
            {
                return WithDesktopView(view =>
                {
                    view.GetCurrentFolderFlags(out uint flags);
                    return (flags & NoIcons) != 0 ? "Icons hidden" : "Icons shown";
                });
            }
            catch
            {
                // Explorer may be restarting. A missing desktop must not prevent app startup.
                return "Desktop unavailable";
            }
        }
    }

    public Task ExecuteAsync()
    {
        WithDesktopView(view =>
        {
            view.GetCurrentFolderFlags(out uint flags);
            // Only change visibility; preserve layout, sorting and all other folder flags.
            view.SetCurrentFolderFlags(NoIcons, flags ^ NoIcons);
            view.GetCurrentFolderFlags(out uint updated);
            if ((updated & NoIcons) == (flags & NoIcons))
            {
                throw new InvalidOperationException("Windows could not change desktop icon visibility.");
            }
            return true;
        });
        return Task.CompletedTask;
    }

    private static T WithDesktopView<T>(Func<IFolderView2, T> action)
    {
        object? windows = null;
        object? desktop = null;
        object? browser = null;
        object? shellView = null;
        try
        {
            // Resolve on every operation so an Explorer restart cannot leave stale COM objects.
            Type type = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"), true)!;
            windows = Activator.CreateInstance(type)!;
            object location = 0; // CSIDL_DESKTOP
            object? root = null; // VT_EMPTY
            int handle;
            desktop = ((dynamic)windows).FindWindowSW(ref location, ref root, 8, out handle, 1);
            if (desktop == null)
            {
                throw new InvalidOperationException("The Windows desktop is unavailable. Try again once Explorer is running.");
            }

            Guid service = new("4C96BE40-915C-11CF-99D3-00AA004AE837"); // SID_STopLevelBrowser
            Guid browserId = typeof(IShellBrowser).GUID;
            ((IServiceProvider)desktop).QueryService(ref service, ref browserId, out browser);
            ((IShellBrowser)browser).QueryActiveShellView(out shellView);
            return action((IFolderView2)shellView);
        }
        finally
        {
            Release(shellView);
            Release(browser);
            Release(desktop);
            Release(windows);
        }
    }

    private static void Release(object? instance)
    {
        if (instance != null && Marshal.IsComObject(instance))
        {
            Marshal.ReleaseComObject(instance);
        }
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        void QueryService(ref Guid service, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object result);
    }

    // Native method order, including inherited methods, is required for COM's vtable.
    // These declarations end at the last method used here; trailing methods are unnecessary.
    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow(out IntPtr window);
        void ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enterMode);
        void InsertMenusSB(IntPtr menu, IntPtr widths);
        void SetMenuSB(IntPtr menu, IntPtr oleMenu, IntPtr activeWindow);
        void RemoveMenusSB(IntPtr menu);
        void SetStatusTextSB([MarshalAs(UnmanagedType.LPWStr)] string text);
        void EnableModelessSB([MarshalAs(UnmanagedType.Bool)] bool enable);
        void TranslateAcceleratorSB(IntPtr message, ushort id);
        void BrowseObject(IntPtr item, uint flags);
        void GetViewStateStream(uint mode, out IntPtr stream);
        void GetControlWindow(uint id, out IntPtr window);
        void SendControlMsg(uint id, uint message, IntPtr wParam, IntPtr lParam, out IntPtr result);
        void QueryActiveShellView([MarshalAs(UnmanagedType.Interface)] out object view);
    }

    [ComImport, Guid("1AF3A467-214F-4298-908E-06B03E0B39F9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView2
    {
        // IFolderView
        void GetCurrentViewMode(out uint mode);
        void SetCurrentViewMode(uint mode);
        void GetFolder(ref Guid interfaceId, out IntPtr folder);
        void Item(int index, out IntPtr item);
        void ItemCount(uint flags, out int count);
        void Items(uint flags, ref Guid interfaceId, out IntPtr items);
        void GetSelectionMarkedItem(out int index);
        void GetFocusedItem(out int index);
        void GetItemPosition(IntPtr item, IntPtr point);
        void GetSpacing(IntPtr point);
        void GetDefaultSpacing(IntPtr point);
        void GetAutoArrange();
        void SelectItem(int index, uint flags);
        void SelectAndPositionItems(uint count, IntPtr items, IntPtr points, uint flags);
        // IFolderView2
        void SetGroupBy(IntPtr key, [MarshalAs(UnmanagedType.Bool)] bool ascending);
        void GetGroupBy(IntPtr key, IntPtr ascending);
        void SetViewProperty(IntPtr item, IntPtr key, IntPtr value);
        void GetViewProperty(IntPtr item, IntPtr key, IntPtr value);
        void SetTileViewProperties(IntPtr item, [MarshalAs(UnmanagedType.LPWStr)] string properties);
        void SetExtendedTileViewProperties(IntPtr item, [MarshalAs(UnmanagedType.LPWStr)] string properties);
        void SetText(uint type, [MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetCurrentFolderFlags(uint mask, uint flags);
        void GetCurrentFolderFlags(out uint flags);
    }
}
