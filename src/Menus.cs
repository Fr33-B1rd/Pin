using System.Runtime.InteropServices;

namespace PinApp;

internal static class Menus
{
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    public static ToolStripMenuItem PinColor(Pin pin, PinManager manager)
    {
        var menu = new ToolStripMenuItem(UiText.ColorMenu);
        void AddChoice(string label, Color? color)
        {
            var item = new ToolStripMenuItem(label) { Checked = pin.CustomColor == color };
            item.Click += (_, _) =>
            {
                if (manager.Get(pin.Paper) != pin) return;
                pin.CustomColor = color;
                manager.SyncDecal(pin);
            };
            menu.DropDownItems.Add(item);
        }

        AddChoice(UiText.ModeColor, null);
        menu.DropDownItems.Add(new ToolStripSeparator());
        AddChoice(UiText.Red, PinArt.Red);
        AddChoice(UiText.Green, Color.FromArgb(45, 172, 93));
        AddChoice(UiText.Blue, PinArt.PaperBlue);
        AddChoice(UiText.Yellow, Color.FromArgb(247, 203, 50));
        return menu;
    }

    /// <summary>
    /// Show a context menu at the cursor from one of our never-activating windows. The menu is made foreground
    /// so clicking elsewhere closes it, and it is disposed after it closes (deferred, so item handlers run first).
    /// </summary>
    public static void Popup(ContextMenuStrip menu)
    {
        menu.Closed += (_, _) =>
        {
            var t = new System.Windows.Forms.Timer { Interval = 50 };
            t.Tick += (_, _) => { t.Dispose(); menu.Dispose(); };
            t.Start();
        };
        menu.Show(Cursor.Position);
        SetForegroundWindow(menu.Handle);
    }
}
