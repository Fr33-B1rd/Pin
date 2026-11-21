using System.Diagnostics;
using System.Text;
using static PinApp.Native;

namespace PinApp;

/// <summary>Helpers for inspecting other applications' top-level windows.</summary>
internal static class Win
{
    private static readonly uint OwnPid = (uint)Environment.ProcessId;

    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland",
    };

    public static string Title(IntPtr h)
    {
        var sb = new StringBuilder(512);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string ClassName(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string ProcessName(IntPtr h)
    {
        try
        {
            GetWindowThreadProcessId(h, out var pid);
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return "?"; }
    }

    /// <summary>Short human label for menus / tooltips.</summary>
    public static string Label(IntPtr h)
    {
        if (h == IntPtr.Zero) return UiText.Desktop;
        var t = Title(h);
        if (string.IsNullOrWhiteSpace(t)) t = ProcessName(h);
        return t.Length > 40 ? t[..38] + "…" : t;
    }

    public static bool IsOwn(IntPtr h)
    {
        GetWindowThreadProcessId(h, out var pid);
        return pid == OwnPid;
    }

    public static bool IsCloaked(IntPtr h) =>
        DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int c, sizeof(int)) == 0 && c != 0;

    public static bool IsTopmost(IntPtr h) => (ExStyle(h) & WS_EX_TOPMOST) != 0;

    /// <summary>Visible frame (without the invisible resize border Windows 10/11 adds).</summary>
    public static RECT Bounds(IntPtr h)
    {
        if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, System.Runtime.InteropServices.Marshal.SizeOf<RECT>()) == 0
            && r.Width > 0)
            return r;
        GetWindowRect(h, out r);
        return r;
    }

    /// <summary>Can this top-level window be a "paper" or a "wall"?</summary>
    public static bool IsPinnable(IntPtr h)
    {
        if (!IsWindowVisible(h) || IsIconic(h) || IsCloaked(h) || IsOwn(h)) return false;
        var ex = ExStyle(h);
        if ((ex & WS_EX_TRANSPARENT) != 0) return false;     // click-through overlays (FPS counters etc.)
        // Desktop enumeration can include desktop-parented WS_CHILD surfaces
        // (for example a monitor-sized WebView2 composition host).
        if ((Style(h) & WS_CHILD) != 0) return false;
        var cls = ClassName(h);
        if (ShellClasses.Contains(cls)) return false;
        // NVIDIA uses a monitor-sized, nonactivating tool window without WS_EX_TRANSPARENT.
        const long overlayStyle = WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        if ((ex & overlayStyle) == overlayStyle && cls == "CEF-OSC-WIDGET") return false;
        var r = Bounds(h);
        return r.Width > 8 && r.Height > 8;
    }

    /// <summary>All pinnable top-level windows under <paramref name="pt"/>, topmost first.</summary>
    public static List<IntPtr> WindowsAt(POINT pt)
    {
        var list = new List<IntPtr>();
        for (var h = GetTopWindow(IntPtr.Zero); h != IntPtr.Zero; h = GetWindow(h, GW_HWNDNEXT))
        {
            if (!IsWindowVisible(h) || !Bounds(h).Contains(pt)) continue;
            if (IsDesktopSurface(h)) break;
            if (IsPinnable(h)) list.Add(h);
            if (list.Count >= 2) break;
        }
        return list;
    }

    public static bool IsDesktopSurface(IntPtr h) =>
        ClassName(h) is "Progman" or "WorkerW" && FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero;

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? title);

    public static POINT Cursor()
    {
        GetCursorPos(out var p);
        return p;
    }
}
