using Microsoft.Win32;
using System.Globalization;

namespace PinApp;

internal enum LanguageMode { System, Chinese, English }

internal static class UiText
{
    private const string Key = @"HKEY_CURRENT_USER\Software\PinApp";
    public static LanguageMode Mode { get; private set; } = Registry.GetValue(Key, "Language", null) switch
    {
        "zh-Hans" => LanguageMode.Chinese,
        "en" => LanguageMode.English,
        _ => LanguageMode.System
    };

    public static void Select(LanguageMode mode)
    {
        Registry.SetValue(Key, "Language", mode switch
        {
            LanguageMode.Chinese => "zh-Hans",
            LanguageMode.English => "en",
            _ => "system"
        });
        Mode = mode;
    }

    private static string Choose(string chinese, string english) =>
        (Mode == LanguageMode.Chinese || (Mode == LanguageMode.System &&
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh")) ? chinese : english;

    public static string PinCount(int count) => count == 0 ? NoPins :
        string.Format(Choose("Pin — 钉住了 {0} 个窗口", count == 1 ? "Pin — {0} pinned window" : "Pin — {0} pinned windows"), count);

    public static string Desktop => Choose("桌面", "Desktop");
    public static string ShowBox => Choose("显示图钉盒", "Show pin box");
    public static string Empty => Choose("（还没有钉住的窗口）", "(No pinned windows)");
    public static string Follow => Choose("跟随墙一起移动", "Move with the wall");
    public static string Minimize => Choose("墙最小化时一起最小化", "Minimize with the wall");
    public static string Remove => Choose("拔出图钉", "Remove pin");
    public static string RemoveAll => Choose("全部拔出", "Remove all pins");
    public static string Exit => Choose("退出", "Exit");
    public static string Started => Choose("Pin 已启动", "Pin is running");
    public static string Welcome => Choose("从右下角的图钉盒拖出图钉，松手钉在窗口上。Ctrl+拖动可移动图钉盒。", "Drag a pin from the box onto a window. Ctrl+drag to move the box.");
    public static string NoPins => Choose("Pin — 没有钉住的窗口", "Pin — No pinned windows");
    public static string Invalid => Choose("无效的目标窗口", "Invalid target window");
    public static string Cycle => Choose("不能把窗口钉到它自己的下层（会形成循环）", "This pin would create a cycle.");
    public static string Elevated => Choose("目标窗口以管理员身份运行，请以管理员身份启动 Pin 后再钉", "Run Pin as administrator to pin this elevated window.");
    public static string Widget => Choose("桌面挂件模式（隐藏独立任务栏入口）", "Desktop widget mode (hide separate taskbar button)");
    public static string WidgetFailed => Choose("无法启用挂件。请确认窗口没有所有者，且桌面和挂件清理进程可用。", "Could not enable widget mode. Check that the window has no owner and that the desktop and widget cleanup process are available.");
    public static string ColorMenu => Choose("图钉颜色", "Pin color");
    public static string ModeColor => Choose("跟随模式", "Use mode color");
    public static string Red => Choose("红色", "Red");
    public static string Green => Choose("绿色", "Green");
    public static string Blue => Choose("蓝色", "Blue");
    public static string Yellow => Choose("黄色", "Yellow");
    public static string ReturnPin => Choose("松手：拔出，放回图钉盒", "Release to unpin and return to box");
    public static string ReturnBox => Choose("松手：放回图钉盒", "Release to return to box");
    public static string Release => Choose("松手：拔出图钉", "Release to remove pin");
    public static string Drag => Choose("拖到要钉住的窗口上", "Drag onto a window to pin it");
    public static string CycleHint => Choose("不能钉到它下层的窗口上", "Cannot pin to a descendant");
    public static string PinTo => Choose("钉到  ", "Pin to  ");
    public static string Wall => Choose("墙 · ", "Wall · ");
    public static string Paper => Choose("纸 · ", "Paper · ");
    public static string OnDesktop => Choose("  （钉在桌面）", "  (pinned to desktop)");
    public static string SystemLabel => Choose("跟随系统", "Follow system");
}
