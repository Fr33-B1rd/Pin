using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using static PinApp.Native;

namespace PinApp;

/// <summary>Desktop ownership for windows that explicitly opt into widget mode.</summary>
internal sealed class DesktopWidget : IDisposable
{
    private const string Marker = "Pin.DesktopWidget.Guard.v1";
    private const uint GW_OWNER = 4;
    private static Process? guard;
    private static int members;
    private readonly IntPtr paper;
    private readonly IntPtr token;
    private IntPtr shell;
    private bool disposed;
    private bool showingDesktop;
    private IntPtr previousApp;
    private int restoreChecks;

    private DesktopWidget(IntPtr paper, IntPtr shell, IntPtr token)
    {
        this.paper = paper;
        this.shell = shell;
        this.token = token;
        previousApp = FindAppAbove();
    }

    public static DesktopWidget? Attach(IntPtr paper)
    {
        var shell = GetShellWindow();
        if (shell == IntPtr.Zero || !IsWindow(paper) || GetWindow(paper, GW_OWNER) != IntPtr.Zero ||
            GetProp(paper, Marker) != IntPtr.Zero) return null;

        // Start the independent cleanup process before changing the foreign window.
        if (guard == null || guard.HasExited)
        {
            guard?.Dispose();
            guard = null;
            try
            {
                guard = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--widget-guard")
                {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                });
                if (guard == null) return null;
                var ready = guard.StandardOutput.ReadLineAsync();
                if (!ready.Wait(TimeSpan.FromSeconds(3)) || ready.Result != "ready")
                {
                    CloseGuard();
                    return null;
                }
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or AggregateException)
            {
                CloseGuard();
                return null;
            }
        }
        var token = (IntPtr)guard.Id;
        var widget = new DesktopWidget(paper, shell, token);
        members++;
        if (SetProp(paper, Marker, token))
        {
            SetOwner(paper, -8, shell);
            if (GetWindow(paper, GW_OWNER) == shell) return widget;
        }
        widget.Dispose();
        return null;
    }

    public bool SyncShell()
    {
        if (guard == null || guard.HasExited || guard.Id != token.ToInt64() || GetProp(paper, Marker) != token) return false;
        var current = GetShellWindow();
        var owner = GetWindow(paper, GW_OWNER);
        if (owner != IntPtr.Zero && owner != shell && owner != current) return false;
        if (current == IntPtr.Zero) return true;
        if (owner != current) SetOwner(paper, -8, current);
        shell = current;
        if (GetWindow(paper, GW_OWNER) != current) return false;
        var foreground = GetForegroundWindow();
        bool desktop = Win.IsDesktopSurface(foreground);
        if (desktop)
        {
            restoreChecks = 0;
        }
        else if (showingDesktop || restoreChecks > 0)
        {
            // Explorer restores its windows asynchronously. Retain the pre-desktop
            // anchor across the transition instead of sampling a half-restored stack.
            if (showingDesktop) restoreChecks = 3;
            if (previousApp != IntPtr.Zero && IsWindow(previousApp) &&
                !Win.IsTopmost(previousApp) && Win.IsPinnable(previousApp) &&
                !IsIconic(paper) && !Win.IsCloaked(paper) && FindAppAbove() != previousApp)
            {
                SetWindowPos(paper, previousApp, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
            }
            restoreChecks--;
        }
        else if (Win.IsPinnable(foreground) && !IsIconic(paper) && !Win.IsCloaked(paper))
        {
            previousApp = FindAppAbove();
        }
        showingDesktop = desktop;
        return true;
    }

    private IntPtr FindAppAbove()
    {
        for (var w = GetWindow(paper, GW_HWNDPREV); w != IntPtr.Zero; w = GetWindow(w, GW_HWNDPREV))
            if (!Win.IsTopmost(w) && Win.IsPinnable(w)) return w;
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Release(paper, token, shell);
        if (--members == 0) CloseGuard();
    }

    private static void CloseGuard()
    {
        if (guard == null) return;
        guard.StandardInput.Close();
        guard.Dispose();
        guard = null;
    }

    private static void Release(IntPtr window, IntPtr token, IntPtr previousShell)
    {
        if (GetProp(window, Marker) != token) return;
        var owner = GetWindow(window, GW_OWNER);
        if (owner != IntPtr.Zero && (owner == previousShell || owner == GetShellWindow()))
            SetOwner(window, -8, IntPtr.Zero);
        RemoveProp(window, Marker);
    }

    // stdin's write end belongs to Pin. Windows closes it on graceful or abrupt exit.
    internal static void RunGuard()
    {
        Console.WriteLine("ready");
        Console.Out.Flush();
        Console.In.ReadToEnd();
        var token = (IntPtr)Environment.ProcessId;
        EnumWindows((w, _) => { Release(w, token, IntPtr.Zero); return true; }, IntPtr.Zero);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetOwner(IntPtr h, int index, IntPtr value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetProp(IntPtr h, string name, IntPtr value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetProp(IntPtr h, string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr RemoveProp(IntPtr h, string name);
    private delegate bool WindowCallback(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr state);
}
