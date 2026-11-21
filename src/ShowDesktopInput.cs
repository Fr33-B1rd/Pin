using System.Runtime.InteropServices;
using System.Windows.Automation;
using static PinApp.Native;

namespace PinApp;

// Explorer can leave focus on the taskbar or an application throughout Show Desktop.
// Snapshot visible pins before the taskbar button or Win+D can minimize them.
internal sealed class ShowDesktopInput : IDisposable
{
    private readonly Func<Pin[]> snapshot;
    private readonly Action<Pin[]> clicked;
    private readonly Action<Pin[]> shortcutStarted;
    private readonly Action otherInput;
    private readonly HookProc mouseCallback;
    private readonly HookProc keyboardCallback;
    private IntPtr keyboardHook;
    private bool dHeld;
    private readonly Control dispatcher = new();
    private IntPtr hook;
    private Pin[]? pressed;
    private POINT down;

    public ShowDesktopInput(Func<Pin[]> snapshot, Action<Pin[]> clicked, Action<Pin[]> shortcutStarted, Action otherInput)
    {
        this.snapshot = snapshot;
        this.clicked = clicked;
        this.shortcutStarted = shortcutStarted;
        this.otherInput = otherInput;
        dispatcher.CreateControl();
        mouseCallback = Observe;
        keyboardCallback = ObserveShortcut;
        var module = GetModuleHandle(null);
        hook = SetWindowsHookEx(14, mouseCallback, module, 0);
        keyboardHook = SetWindowsHookEx(13, keyboardCallback, module, 0);
    }

    private IntPtr Observe(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && (message == 0x201 || message == 0x204 || message == 0x207 ||
            message == 0x20A || message == 0x20B || message == 0x20E)) otherInput();
        if (code >= 0 && (message == 0x201 || message == 0x202))
        {
            var point = Marshal.PtrToStructure<POINT>(data);
            if (message == 0x201)
            {
                pressed = null;
                var root = GetAncestor(WindowFromPoint(point), 2);
                if (Win.ClassName(root) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                {
                    down = point;
                    pressed = snapshot();
                }
            }
            else if (pressed is { Length: > 0 } candidates)
            {
                var start = down;
                pressed = null;
                // Keep UI Automation out of the input hook. Check the actual
                // accessible button at both ends, not a guessed screen corner.
                dispatcher.BeginInvoke(() =>
                {
                    if (hook == IntPtr.Zero) return;
                    try
                    {
                        var bar = GetAncestor(WindowFromPoint(point), 2);
                        if (Win.ClassName(bar) is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd")) return;
                        var element = AutomationElement.FromHandle(bar).FindFirst(TreeScope.Descendants,
                            new OrCondition(
                                new PropertyCondition(AutomationElement.ClassNameProperty, "SystemTray.ShowDesktopButton"),
                                new PropertyCondition(AutomationElement.ClassNameProperty, "TrayShowDesktopButtonWClass")));
                        if (element == null) return;
                        var bounds = element.Current.BoundingRectangle;
                        if (bounds.Contains(start.X, start.Y) && bounds.Contains(point.X, point.Y)) clicked(candidates);
                    }
                    catch (ElementNotAvailableException) { }
                    catch (COMException) { }
                });
            }
        }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    private IntPtr ObserveShortcut(int code, IntPtr message, IntPtr data)
    {
        const int D = 0x44;
        if (code >= 0 && Marshal.ReadInt32(data) == D)
        {
            if (message == 0x100 || message == 0x104) // WM_KEYDOWN / WM_SYSKEYDOWN
            {
                bool win = IsDown(0x5B) || IsDown(0x5C);
                if (!dHeld && win && !IsDown(0x10) && !IsDown(0x11) && !IsDown(0x12))
                {
                    var candidates = snapshot();
                    shortcutStarted(candidates);
                    dispatcher.BeginInvoke(() =>
                    {
                        if (keyboardHook != IntPtr.Zero) clicked(candidates);
                    });
                }
                if (!win || IsDown(0x10) || IsDown(0x11) || IsDown(0x12)) otherInput();
                dHeld = true;
            }
            else if (message == 0x101 || message == 0x105) dHeld = false;
        }
        else if (code >= 0 && (message == 0x100 || message == 0x104) &&
            Marshal.ReadInt32(data) is not (0x5B or 0x5C)) otherInput();
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    private static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    public void Dispose()
    {
        if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
        if (keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(keyboardHook);
        keyboardHook = IntPtr.Zero;
        dispatcher.Dispose();
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint kind);
}

