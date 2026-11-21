namespace PinApp;

/// <summary>App root: tray icon + pin box + pin manager.</summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly PinManager _mgr = new();
    private readonly NotifyIcon _tray;
    private readonly Dispenser _box;

    public TrayContext()
    {
        _tray = new NotifyIcon
        {
            Icon = PinArt.MakeIcon(32),
            Text = UiText.PinCount(0),
            Visible = true,
        };
        var trayMenu = new ContextMenuStrip();
        trayMenu.Opening += (_, e) =>
        {
            while (trayMenu.Items.Count > 0) trayMenu.Items[0].Dispose();
            PopulateMenu(trayMenu);
            e.Cancel = false;
        };
        _tray.ContextMenuStrip = trayMenu;
        _tray.DoubleClick += (_, _) => _box!.Toggle();

        _mgr.Notice += msg => _tray.ShowBalloonTip(4000, "Pin", msg, ToolTipIcon.Warning);
        _mgr.Changed += RefreshTooltip;

        _box = new Dispenser(_mgr, BuildMenu);
        _box.ShowDefault();
        DragSession.DropBackZone = () => _box.Visible ? new Rectangle(_box.ScreenPos, _box.PixelSize) : Rectangle.Empty;
        _tray.ShowBalloonTip(3000, UiText.Started, UiText.Welcome, ToolTipIcon.Info);
    }

    private ContextMenuStrip BuildMenu()
    {
        var m = new ContextMenuStrip();

        PopulateMenu(m);
        return m;
    }

    private void RefreshTooltip() => _tray.Text = UiText.PinCount(_mgr.All.Count);

    private void PopulateMenu(ContextMenuStrip m)
    {
        var language = new ToolStripMenuItem("语言 / Language");
        void AddLanguage(string label, LanguageMode mode)
        {
            var item = new ToolStripMenuItem(label) { Checked = UiText.Mode == mode };
            item.Click += (_, _) =>
            {
                UiText.Select(mode);
                RefreshTooltip();
            };
            language.DropDownItems.Add(item);
        }
        AddLanguage(UiText.SystemLabel, LanguageMode.System);
        AddLanguage("简体中文", LanguageMode.Chinese);
        AddLanguage("English", LanguageMode.English);
        m.Items.Add(language);
        var showBox = new ToolStripMenuItem(UiText.ShowBox) { Checked = _box?.Visible ?? true };
        showBox.Click += (_, _) => _box!.Toggle();
        m.Items.Add(showBox);
        m.Items.Add(new ToolStripSeparator());

        if (_mgr.All.Count == 0)
        {
            m.Items.Add(new ToolStripMenuItem(UiText.Empty) { Enabled = false });
        }
        else
        {
            foreach (var pin in _mgr.All.ToList())
            {
                var item = new ToolStripMenuItem($"{Win.Label(pin.Paper)}  →  {Win.Label(pin.Wall)}");
                var follow = new ToolStripMenuItem(UiText.Follow) { Checked = pin.FollowMove, Enabled = !pin.OnDesktop };
                follow.Click += (_, _) => pin.FollowMove = !pin.FollowMove;
                var minimize = new ToolStripMenuItem(UiText.Minimize) { Checked = pin.SyncMinimize, Enabled = !pin.OnDesktop };
                minimize.Click += (_, _) => pin.SyncMinimize = !pin.SyncMinimize;
                var pull = new ToolStripMenuItem(UiText.Remove);
                pull.Click += (_, _) => _mgr.Remove(pin.Paper);
                item.DropDownItems.AddRange(new ToolStripItem[] { follow, minimize, new ToolStripSeparator(), pull });
                if (pin.OnDesktop) item.DropDownItems.Insert(0, _mgr.WidgetMenu(pin));
                item.DropDownItems.Insert(0, Menus.PinColor(pin, _mgr));
                m.Items.Add(item);
            }
            var all = new ToolStripMenuItem(UiText.RemoveAll);
            all.Click += (_, _) => _mgr.RemoveAll();
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(all);
        }

        m.Items.Add(new ToolStripSeparator());
        var exit = new ToolStripMenuItem(UiText.Exit);
        exit.Click += (_, _) => Quit();
        m.Items.Add(exit);
    }

    private void Quit()
    {
        _mgr.Dispose();          // pulls every pin out (restores topmost / minimized state)
        _tray.Visible = false;
        _tray.Dispose();
        _box.Close();
        ExitThread();
    }
}

