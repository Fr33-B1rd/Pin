using System.Runtime.InteropServices;
using static PinApp.Native;

namespace PinApp;

/// <summary>One "paper pinned to a wall" relation. Wall == IntPtr.Zero means the desktop.</summary>
internal sealed class Pin
{
    public required IntPtr Paper;
    public required IntPtr Wall;
    /// <summary>Where the needle went in, relative to the paper's top-left (physical px).</summary>
    public Point Offset;
    public bool FollowMove = true;
    public bool SyncMinimize = true;
    public bool MadeTopmost;
    public bool MinimizedByUs;
    public bool WallWasZoomed;
    /// <summary>When the user (not Pin) last minimized this paper; suppresses the instant pop-back.</summary>
    public long UserMinimizedAt;
    public RECT LastWallRect;
    public PinDecal? Decal;
    public DesktopWidget? Widget;
    public Color? CustomColor;
    public Color HeadColor => CustomColor ?? (Widget == null ? PinArt.Red : PinArt.PaperBlue);
    public long DesktopOrder;
    public bool OnDesktop => Wall == IntPtr.Zero;
}

/// <summary>
/// Owns all pins (a forest: every paper has exactly one wall, a wall can hold many papers) and keeps
/// the z-order relationship alive by listening to system-wide WinEvents.
/// </summary>
internal sealed class PinManager : IDisposable
{
    private readonly Dictionary<IntPtr, Pin> _pins = new();
    private readonly List<IntPtr> _hooks = new();
    private readonly WinEventProc _proc;            // keep delegate alive (GC!)
    private readonly System.Windows.Forms.Timer _janitor;
    private readonly DesktopUndo _desktopUndo = new();
    private ShowDesktopInput? _desktopInput;
    private readonly System.Windows.Forms.Timer _desktopRecoveryTimer = new() { Interval = 100 };
    private readonly HashSet<Pin> _awaitingMinimize = new();
    private long _desktopRecoveryDeadline;
    private long _activationOrder;
    private bool _raising;
    private IntPtr _desktopView;
    private long _desktopActivatedAt;

    public event Action? Changed;
    public event Action<string>? Notice;

    public PinManager()
    {
        _proc = OnWinEvent;
        Hook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND);
        Hook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND);
        Hook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND);
        Hook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_HIDE);
        Hook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE);
        Hook(EVENT_OBJECT_CLOAKED, EVENT_OBJECT_UNCLOAKED);

        // Safety net for anything the events miss (killed processes, virtual-desktop switches...).
        _janitor = new System.Windows.Forms.Timer { Interval = 500 };
        _janitor.Tick += (_, _) => Janitor();
        _janitor.Start();
        _desktopRecoveryTimer.Tick += (_, _) => RecoverDesktopMinimizations();
    }

    private void Hook(uint min, uint max)
    {
        var h = SetWinEventHook(min, max, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (h != IntPtr.Zero) _hooks.Add(h);
    }

    public IReadOnlyCollection<Pin> All => _pins.Values;
    public Pin? Get(IntPtr paper) => _pins.GetValueOrDefault(paper);

    // ---- tree queries ------------------------------------------------------------------------------

    private IEnumerable<Pin> PinsOn(IntPtr wall) => _pins.Values.Where(p => p.Wall == wall && wall != IntPtr.Zero);

    /// <summary>Would pinning <paramref name="paper"/> onto <paramref name="wall"/> create a loop?</summary>
    public bool WouldCycle(IntPtr paper, IntPtr wall, IntPtr ignorePaper = default)
    {
        for (var w = wall; w != IntPtr.Zero;)
        {
            if (w == paper) return true;
            if (w == ignorePaper || !_pins.TryGetValue(w, out var p)) return false;
            w = p.Wall;
        }
        return false;
    }

    private IntPtr RootOf(IntPtr h)
    {
        while (_pins.TryGetValue(h, out var p) && p.Wall != IntPtr.Zero) h = p.Wall;
        return h;
    }

    /// <summary>Root first, every paper after its wall.</summary>
    private List<IntPtr> PreOrder(IntPtr root)
    {
        var list = new List<IntPtr>();
        void Visit(IntPtr h, int depth)
        {
            if (depth > 32) return;
            list.Add(h);
            foreach (var c in PinsOn(h).ToList()) Visit(c.Paper, depth + 1);
        }
        Visit(root, 0);
        return list;
    }

    private bool IsInvolved(IntPtr h) => _pins.ContainsKey(h) || _pins.Values.Any(p => p.Wall == h);

    // ---- create / remove ---------------------------------------------------------------------------

    /// <summary>Pin <paramref name="paper"/> onto <paramref name="wall"/> (Zero = desktop). Returns an error message or null.</summary>
    public string? Add(IntPtr paper, IntPtr wall, POINT needle, Color? customColor = null)
    {
        if (paper == IntPtr.Zero || paper == wall) return UiText.Invalid;
        if (WouldCycle(paper, wall)) return UiText.Cycle;

        // UIPI: an elevated window can't be repositioned by a non-elevated Pin.
        if (!Touchable(paper) || (wall != IntPtr.Zero && !Touchable(wall)))
            return UiText.Elevated;

        if (_pins.ContainsKey(paper)) Remove(paper, notify: false);

        var pb = Win.Bounds(paper);
        var pin = new Pin
        {
            Paper = paper,
            Wall = wall,
            CustomColor = customColor,
            Offset = new Point(Math.Clamp(needle.X - pb.Left, 0, pb.Width), Math.Clamp(needle.Y - pb.Top, 0, pb.Height)),
        };
        if (wall != IntPtr.Zero)
        {
            pin.LastWallRect = Win.Bounds(wall);
            pin.WallWasZoomed = IsZoomed(wall);
            // A normal window can never sit above a topmost one, so the paper has to join the topmost band.
            if (Win.IsTopmost(wall) && !Win.IsTopmost(paper))
            {
                SetWindowPos(paper, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                pin.MadeTopmost = true;
            }
        }
        _pins[paper] = pin;
        if (pin.OnDesktop)
        {
            pin.DesktopOrder = ++_activationOrder;
            _desktopInput ??= new ShowDesktopInput(
                () => _pins.Values.Where(p => p.OnDesktop && IsWindowVisible(p.Paper) &&
                    !IsIconic(p.Paper) && !Win.IsCloaked(p.Paper)).ToArray(), BeginDesktopRecovery,
                pins => _desktopUndo.Begin(pins, IsInvolved), CancelDesktopRecovery);
        }
        pin.Decal = new PinDecal(this, pin);
        SyncDecal(pin);
        RaiseTree(paper);
        Changed?.Invoke();
        return null;
    }

    public void Remove(IntPtr paper, bool notify = true)
    {
        if (!_pins.Remove(paper, out var pin)) return;
        _awaitingMinimize.Remove(pin);
        if (!_pins.Values.Any(p => p.OnDesktop)) { _desktopUndo.Cancel(); _desktopInput?.Dispose(); _desktopInput = null; }
        pin.Widget?.Dispose();
        if (pin.MadeTopmost && IsWindow(paper))
            SetWindowPos(paper, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        if (pin.MinimizedByUs && IsWindow(paper) && IsIconic(paper))
            ShowWindow(paper, SW_SHOWNOACTIVATE);
        pin.Decal?.Close();
        pin.Decal?.Dispose();
        pin.Decal = null;
        if (notify) Changed?.Invoke();
    }

    public void RemoveAll()
    {
        foreach (var p in _pins.Keys.ToList()) Remove(p, notify: false);
        Changed?.Invoke();
    }

    public ToolStripMenuItem WidgetMenu(Pin pin)
    {
        var item = new ToolStripMenuItem(UiText.Widget) { Checked = pin.Widget != null };
        item.Click += (_, _) =>
        {
            if (Get(pin.Paper) != pin || !pin.OnDesktop) return;
            if (pin.Widget != null)
            {
                pin.Widget.Dispose();
                pin.Widget = null;
            }
            else
            {
                pin.Widget = DesktopWidget.Attach(pin.Paper);
                if (pin.Widget != null && IsIconic(pin.Paper)) ShowWindow(pin.Paper, SW_SHOWNOACTIVATE);
                if (pin.Widget == null) Warn(UiText.WidgetFailed);
            }
            SyncDecal(pin);
            Touch();
        };
        return item;
    }

    public void Touch() => Changed?.Invoke();
    public void Warn(string message) => Notice?.Invoke(message);

    private static bool Touchable(IntPtr h)
    {
        // A no-op SetWindowPos fails with ERROR_ACCESS_DENIED when UIPI blocks us.
        if (SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER))
            return true;
        return Marshal.GetLastWin32Error() != 5;
    }

    // ---- the core: keep the stack together ------------------------------------------------------------

    /// <summary>Bring the whole tree containing <paramref name="h"/> together: every paper directly above its wall.</summary>
    public void RaiseTree(IntPtr h)
    {
        if (_raising) return;
        _raising = true;
        try
        {
            var root = RootOf(h);
            var order = PreOrder(root);
            bool desktopTree = _pins.TryGetValue(root, out var rootPin) && rootPin.OnDesktop;

            // "打开其中一个，另一个自动弹出": restore papers whose wall is showing.
            foreach (var w in order)
            {
                if (!_pins.TryGetValue(w, out var p) || p.OnDesktop) continue;
                if (!IsIconic(w) || IsIconic(p.Wall)) continue;
                if (Environment.TickCount64 - p.UserMinimizedAt < 800) continue;   // user just minimized it
                ShowWindow(w, SW_SHOWNOACTIVATE);
                p.MinimizedByUs = false;
            }

            // Bottom-to-top sequence: wall, paper, paper's decal, grand-paper, ...
            var seq = new List<IntPtr>();
            foreach (var w in order)
            {
                if (!IsWindow(w) || !IsWindowVisible(w) || IsIconic(w)) continue;
                if (IsHungAppWindow(w)) continue;
                seq.Add(w);
                if (_pins.TryGetValue(w, out var p) && p.Decal is { IsHandleCreated: true, Visible: true } d)
                    seq.Add(d.Handle);
            }

            const uint F = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER;
            if (desktopTree)
            {
                // Pinned to the desktop: the whole stack sinks to the bottom (just above the wallpaper/icons).
                for (int i = seq.Count - 1; i >= 0; i--) SetWindowPos(seq[i], HWND_BOTTOM, 0, 0, 0, 0, F);
            }
            else
            {
                // Each call puts the window on top of its band, so the last one ends up highest.
                foreach (var w in seq) SetWindowPos(w, HWND_TOP, 0, 0, 0, 0, F);
                EnsureStackOrder(seq);
            }
        }
        finally { _raising = false; }
    }

    // ---- events ------------------------------------------------------------------------------------------

    private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero || idObject != OBJID_WINDOW || idChild != CHILDID_SELF) return;
        if (_pins.Count == 0) return;
        if (evt == EVENT_SYSTEM_MINIMIZESTART) _desktopUndo.OnMinimize(hwnd, time);
        if (evt == EVENT_OBJECT_DESTROY) _desktopUndo.Forget(hwnd);
        if (!IsInvolved(hwnd)) return;

        try
        {
            switch (evt)
            {
                case EVENT_SYSTEM_FOREGROUND:
                    var rootHandle = RootOf(hwnd);
                    if (_pins.TryGetValue(rootHandle, out var rootPin) && rootPin.OnDesktop)
                        rootPin.DesktopOrder = ++_activationOrder;
                    goto case EVENT_SYSTEM_MOVESIZEEND;
                case EVENT_SYSTEM_MOVESIZEEND:
                    RaiseTree(hwnd);
                    break;

                case EVENT_SYSTEM_MINIMIZESTART:
                    if (_pins.TryGetValue(hwnd, out var self) && !self.MinimizedByUs)
                    {
                        self.UserMinimizedAt = Environment.TickCount64;
                        if (TryRecoverDesktopPin(self)) break;
                    }
                    foreach (var child in Descendants(hwnd))
                    {
                        if (!child.SyncMinimize || IsIconic(child.Paper)) continue;
                        ShowWindow(child.Paper, SW_SHOWMINNOACTIVE);
                        child.MinimizedByUs = true;
                    }
                    SyncDecalOf(hwnd);
                    break;

                case EVENT_SYSTEM_MINIMIZEEND:
                    foreach (var child in Descendants(hwnd))
                    {
                        if (child.MinimizedByUs && IsIconic(child.Paper)) ShowWindow(child.Paper, SW_SHOWNOACTIVATE);
                        child.MinimizedByUs = false;
                    }
                    SyncDecalOf(hwnd);
                    RaiseTree(hwnd);
                    break;

                case EVENT_OBJECT_LOCATIONCHANGE:
                    OnMoved(hwnd);
                    break;

                case EVENT_OBJECT_DESTROY:
                    PullOutEverythingAt(hwnd);
                    break;

                case EVENT_OBJECT_SHOW:
                case EVENT_OBJECT_HIDE:
                case EVENT_OBJECT_CLOAKED:
                case EVENT_OBJECT_UNCLOAKED:
                    SyncDecalOf(hwnd);
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Pin] event 0x{evt:X}: {ex}");
        }
    }

    private IEnumerable<Pin> Descendants(IntPtr h)
    {
        foreach (var p in PinsOn(h).ToList())
        {
            yield return p;
            foreach (var d in Descendants(p.Paper)) yield return d;
        }
    }

    private void OnMoved(IntPtr hwnd)
    {
        // As a paper: its needle head follows.
        SyncDecalOf(hwnd);

        // As a wall: papers follow the wall's top-left corner, like paper pinned to a cork board.
        var papers = PinsOn(hwnd).ToList();
        if (papers.Count == 0 || IsIconic(hwnd)) return;
        var now = Win.Bounds(hwnd);
        foreach (var p in papers)
        {
            var before = p.LastWallRect;
            bool zoomed = IsZoomed(hwnd), wasZoomed = p.WallWasZoomed;
            p.LastWallRect = now;
            p.WallWasZoomed = zoomed;
            int dx = now.Left - before.Left, dy = now.Top - before.Top;
            if (!p.FollowMove || (dx == 0 && dy == 0) || before.Width == 0) continue;
            if (zoomed || wasZoomed || IsIconic(p.Paper) || IsZoomed(p.Paper)) continue;   // maximize/restore jumps: leave papers where they are
            GetWindowRect(p.Paper, out var r);
            SetWindowPos(p.Paper, IntPtr.Zero, r.Left + dx, r.Top + dy, 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_ASYNCWINDOWPOS);
        }
    }

    private void PullOutEverythingAt(IntPtr hwnd)
    {
        bool changed = false;
        if (_pins.ContainsKey(hwnd)) { Remove(hwnd, notify: false); changed = true; }
        foreach (var p in _pins.Values.Where(p => p.Wall == hwnd).ToList()) { Remove(p.Paper, notify: false); changed = true; }
        if (changed) Changed?.Invoke();
    }

    private void SyncDecalOf(IntPtr paper)
    {
        if (_pins.TryGetValue(paper, out var pin)) SyncDecal(pin);
    }

    /// <summary>Show the needle head on the paper (or hide it while the paper is hidden/minimized/on another desktop).</summary>
    public void SyncDecal(Pin pin)
    {
        var d = pin.Decal;
        if (d == null || d.IsDisposed || d.Dragging) return;
        bool show = IsWindow(pin.Paper) && IsWindowVisible(pin.Paper) && !IsIconic(pin.Paper) && !Win.IsCloaked(pin.Paper);
        if (!show) { if (d.Visible) d.Visible = false; return; }

        var b = Win.Bounds(pin.Paper);
        var needle = new Point(b.Left + Math.Min(pin.Offset.X, Math.Max(0, b.Width - 1)),
                               b.Top + Math.Min(pin.Offset.Y, Math.Max(0, b.Height - 1)));
        bool topmost = Win.IsTopmost(pin.Paper);
        d.ShowAt(needle, topmost);
    }

    private void Janitor()
    {
        bool changed = false;
        foreach (var p in _pins.Values.ToList())
        {
            if (!IsWindow(p.Paper) || (p.Wall != IntPtr.Zero && !IsWindow(p.Wall)))
            {
                Remove(p.Paper, notify: false);
                changed = true;
            }
            else
            {
                if (p.Widget != null && !p.Widget.SyncShell())
                {
                    p.Widget.Dispose();
                    p.Widget = null;
                    changed = true;
                }
                SyncDecal(p);
            }
        }
        if (changed) Changed?.Invoke();

        // Explorer can finish taskbar activation after the foreground/restore callbacks.
        // Recheck the active group after that ordering has settled, without taking focus.
        UpdateDesktopPins();
        RepairDesktopOrder();
        RepairActiveStack();
    }

    private void CancelDesktopRecovery()
    {
        _desktopUndo.Cancel();
        _awaitingMinimize.Clear();
        _desktopRecoveryTimer.Stop();
    }

    private void BeginDesktopRecovery(Pin[] visibleBeforeCommand)
    {
        _awaitingMinimize.Clear();
        foreach (var pin in visibleBeforeCommand)
            if (Get(pin.Paper) == pin) _awaitingMinimize.Add(pin);
        _desktopRecoveryDeadline = Environment.TickCount64 + 800;
        _desktopRecoveryTimer.Start();
        RecoverDesktopMinimizations();
    }

    private bool TryRecoverDesktopPin(Pin pin)
    {
        if (!_awaitingMinimize.Contains(pin) || Environment.TickCount64 > _desktopRecoveryDeadline ||
            !IsIconic(pin.Paper) || Win.IsCloaked(pin.Paper)) return false;
        _awaitingMinimize.Remove(pin);
        ShowWindow(pin.Paper, SW_SHOWNOACTIVATE);
        SyncDecal(pin);
        _desktopUndo.OnPaperRestored();
        return true;
    }

    private void RecoverDesktopMinimizations()
    {
        foreach (var pin in _awaitingMinimize.ToArray()) TryRecoverDesktopPin(pin);
        if (_awaitingMinimize.Count == 0 || Environment.TickCount64 >= _desktopRecoveryDeadline)
        {
            _desktopRecoveryTimer.Stop();
            _awaitingMinimize.Clear();
        }
    }

    private void UpdateDesktopPins()
    {
        if (!_pins.Values.Any(p => p.OnDesktop && p.Widget == null))
        {
            _desktopView = IntPtr.Zero;
            return;
        }
        var foreground = GetForegroundWindow();
        var desktop = Win.IsDesktopSurface(foreground) ? foreground : IntPtr.Zero;
        if (desktop != _desktopView)
        {
            _desktopView = desktop;
            _desktopActivatedAt = Environment.TickCount64;
        }
        if (desktop == IntPtr.Zero) return;

        // Explorer minimizes ordinary windows when Show desktop starts, then puts
        // its icon view in front. Only undo minimizations from that transition.
        var stack = new List<IntPtr>();
        foreach (var root in DesktopRoots().ToList())
        {
            if (Win.IsCloaked(root.Paper)) continue;
            if (IsIconic(root.Paper))
            {
                // For a recognized command, only the pre-command snapshot may restore windows.
                if (Environment.TickCount64 <= _desktopRecoveryDeadline + 750) continue;
                if (root.Widget != null || root.UserMinimizedAt == 0 || Math.Abs(root.UserMinimizedAt - _desktopActivatedAt) > 750) continue;
                root.UserMinimizedAt = 0;
                ShowWindow(root.Paper, SW_SHOWNOACTIVATE);
            }
            if (!IsWindowVisible(root.Paper) || IsIconic(root.Paper)) continue;
            foreach (var w in PreOrder(root.Paper))
            {
                if (IsIconic(w) || !IsWindowVisible(w) || Win.IsCloaked(w)) continue;
                stack.Add(w);
                var pin = _pins[w];
                SyncDecal(pin);
                if (pin.Decal is { Visible: true } decal) stack.Add(decal.Handle);
            }
        }
        if (stack.Count == 0) return;

        // Read upwards from the icon view. Avoid rewriting an already correct stack
        // or using a pin head as the insertion anchor for another desktop pin.
        int i = 0;
        var anchor = GetWindow(desktop, GW_HWNDPREV);
        while (i < stack.Count && anchor == stack[i])
        {
            ++i;
            anchor = GetWindow(anchor, GW_HWNDPREV);
        }
        if (i == stack.Count) return;
        while (anchor != IntPtr.Zero && stack.Contains(anchor)) anchor = GetWindow(anchor, GW_HWNDPREV);
        if (anchor != IntPtr.Zero && Win.IsTopmost(anchor)) anchor = HWND_TOP;
        foreach (var w in stack)
            SetWindowPos(w, anchor, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    private IEnumerable<Pin> DesktopRoots() => _pins.Values.Where(p => p.OnDesktop)
        .OrderBy(p => p.Widget != null).ThenBy(p => p.DesktopOrder);

    private void RepairDesktopOrder()
    {
        // Two desktop pins form separate trees. Sending an active tree to HWND_BOTTOM
        // can put a full-size ordinary pin over a smaller widget. Order the desktop
        // group within its existing layer; leave unrelated applications alone.
        var roots = DesktopRoots().Where(p => !IsIconic(p.Paper) && IsWindowVisible(p.Paper) &&
            !Win.IsCloaked(p.Paper)).ToList();
        if (roots.Count < 2) return;
        var stack = new List<IntPtr>();
        foreach (var root in roots)
        {
            foreach (var window in PreOrder(root.Paper))
            {
                if (IsIconic(window) || !IsWindowVisible(window) || Win.IsCloaked(window)) continue;
                stack.Add(window);
                if (_pins.GetValueOrDefault(window)?.Decal is { Visible: true } decal) stack.Add(decal.Handle);
            }
        }
        EnsureStackOrder(stack);
    }

    private void RepairActiveStack()
    {
        var active = GetForegroundWindow();
        if (!IsInvolved(active)) return;
        var root = RootOf(active);
        if (_pins.TryGetValue(root, out var rootPin) && rootPin.OnDesktop) return;

        var order = PreOrder(root)
            .Where(w => IsWindowVisible(w) && !IsIconic(w) && !Win.IsCloaked(w))
            .ToList();
        EnsureStackOrder(order);
    }

    private static void EnsureStackOrder(List<IntPtr> bottomToTop)
    {
        if (bottomToTop.Count < 2) return;
        const uint flags = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER;
        // Never insert a normal window behind a topmost handle: that can promote it.
        foreach (var band in bottomToTop.GroupBy(Win.IsTopmost))
        {
            var desired = band.Reverse().ToList();
            if (desired.Count < 2) continue;
            var members = desired.ToHashSet();
            var actual = new List<IntPtr>(desired.Count);
            for (var w = GetTopWindow(IntPtr.Zero); w != IntPtr.Zero; w = GetWindow(w, GW_HWNDNEXT))
            {
                if (members.Contains(w)) actual.Add(w);
                if (actual.Count == desired.Count) break;
            }
            if (actual.Count != desired.Count || actual.SequenceEqual(desired)) continue;

            // HWND_TOP can leave a nonactivating paper below the foreground wall.
            // Anchor at the group's current highest member and explicitly chain
            // the others behind the paper, preserving the user's keyboard focus.
            if (desired[0] != actual[0])
                SetWindowPos(desired[0], actual[0], 0, 0, 0, 0, flags);
            for (int i = 1; i < desired.Count; ++i)
                SetWindowPos(desired[i], desired[i - 1], 0, 0, 0, 0, flags);
        }
    }

    public void Dispose()
    {
        _desktopRecoveryTimer.Dispose();
        _janitor.Stop();
        foreach (var h in _hooks) UnhookWinEvent(h);
        _hooks.Clear();
        foreach (var p in _pins.Keys.ToList()) Remove(p, notify: false);
        _desktopUndo.Dispose();
    }

    [DllImport("user32.dll")] private static extern bool IsHungAppWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
}




