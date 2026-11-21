using System.Runtime.InteropServices;
using static PinApp.Native;

namespace PinApp;

// Restoring a paper can end Explorer's Show Desktop state before the next Win+D.
// Retain an undo list only for adjacent shortcuts where Pin actually intervened.
internal sealed class DesktopUndo : IDisposable
{
    private sealed record Window(IntPtr Handle, uint Process, uint Thread)
    {
        public bool MinimizedByCommand;
        public bool Matches() => GetWindowThreadProcessId(Handle, out var pid) == Thread && pid == Process;
    }

    private readonly List<Window> before = new();
    private List<Window> pending = new();
    private readonly System.Windows.Forms.Timer settle = new() { Interval = 200 };
    private uint commandTime;
    private bool recoveredPaper;

    public DesktopUndo() => settle.Tick += (_, _) => FinishUndo();

    public void Begin(Pin[] visiblePins, Func<IntPtr, bool> isPinned)
    {
        // A second shortcut can arrive before the shell has delivered its hook callbacks.
        var undo = recoveredPaper ? before.Where(w => w.MinimizedByCommand && w.Matches() &&
            IsIconic(w.Handle)).Reverse().ToList() : new List<Window>();
        Cancel();
        if (undo.Count != 0)
        {
            pending = undo;
            settle.Start();
            return;
        }
        if (!visiblePins.Any(p => p.Widget == null)) return;
        commandTime = unchecked((uint)Environment.TickCount);
        for (var w = GetTopWindow(IntPtr.Zero); w != IntPtr.Zero; w = GetWindow(w, GW_HWNDNEXT))
        {
            if (!IsWindowVisible(w) || IsIconic(w) || isPinned(w) || (Style(w) & WS_CHILD) != 0) continue;
            uint thread = GetWindowThreadProcessId(w, out uint pid);
            if (thread != 0 && pid != Environment.ProcessId) before.Add(new(w, pid, thread));
        }
    }

    public void OnMinimize(IntPtr hwnd, uint eventTime)
    {
        if (unchecked(eventTime - commandTime) > 800) return;
        var item = before.Find(w => w.Handle == hwnd);
        if (item != null && item.Matches()) item.MinimizedByCommand = true;
    }

    public void OnPaperRestored() => recoveredPaper = true;

    public void Forget(IntPtr hwnd)
    {
        before.RemoveAll(w => w.Handle == hwnd);
        pending.RemoveAll(w => w.Handle == hwnd);
    }

    public void Cancel()
    {
        settle.Stop();
        before.Clear();
        pending.Clear();
        recoveredPaper = false;
    }

    private void FinishUndo()
    {
        settle.Stop();
        foreach (var w in pending)
        {
            if (w.Matches() && IsIconic(w.Handle) && IsWindowVisible(w.Handle) && !Win.IsCloaked(w.Handle))
                PostMessage(w.Handle, 0x0112, 0xF120, IntPtr.Zero); // WM_SYSCOMMAND / SC_RESTORE
        }
        pending.Clear();
    }

    public void Dispose() => settle.Dispose();

    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
