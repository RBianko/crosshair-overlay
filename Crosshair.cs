using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class Settings
{
    public int Length = 6;
    public int Gap = 3;
    public int Thickness = 2;
    public bool Dot = false;
    public int Outline = 1;           // 0 = no outline
    public Color Color = Color.FromArgb(0, 255, 0);
    public Color OutlineColor = Color.Black;
    public int OffsetX = 0;
    public int OffsetY = 0;
    public int Monitor = 0;           // 0 = primary, 1..N = screen index
    public Keys ToggleKey = Keys.F8;

    // %APPDATA%\CrosshairOverlay\crosshair.ini - writable even when the exe sits in Program Files.
    public static string PathOf()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CrosshairOverlay");
        return Path.Combine(dir, "crosshair.ini");
    }

    public static Settings Load()
    {
        var s = new Settings();
        string p = PathOf();
        string[] lines;
        try
        {
            if (!File.Exists(p))
            {
                // Carry over settings from older builds that kept the ini next to the exe.
                string old = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crosshair.ini");
                if (!File.Exists(old)) { s.Save(); return s; }
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.Copy(old, p);
            }
            lines = File.ReadAllLines(p);
        }
        catch { return s; }
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
            int eq = line.IndexOf('=');
            if (eq < 0) continue;
            string k = line.Substring(0, eq).Trim().ToLowerInvariant();
            string v = line.Substring(eq + 1).Trim();
            try
            {
                switch (k)
                {
                    case "length": s.Length = int.Parse(v); break;
                    case "gap": s.Gap = int.Parse(v); break;
                    case "thickness": s.Thickness = Math.Max(1, int.Parse(v)); break;
                    case "dot": s.Dot = v == "1" || v.ToLowerInvariant() == "true"; break;
                    case "outline": s.Outline = Math.Max(0, int.Parse(v)); break;
                    case "color": s.Color = ParseColor(v); break;
                    case "outlinecolor": s.OutlineColor = ParseColor(v); break;
                    case "offsetx": s.OffsetX = int.Parse(v); break;
                    case "offsety": s.OffsetY = int.Parse(v); break;
                    case "monitor": s.Monitor = int.Parse(v); break;
                    case "togglekey": s.ToggleKey = (Keys)Enum.Parse(typeof(Keys), v, true); break;
                }
            }
            catch { }
        }
        return s;
    }

    static Color ParseColor(string v)
    {
        v = v.TrimStart('#');
        int rgb = int.Parse(v, NumberStyles.HexNumber);
        var c = Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
        // Magenta is the transparency key - nudge it so it stays visible.
        if (c.R == 255 && c.G == 0 && c.B == 255) c = Color.FromArgb(254, 0, 255);
        return c;
    }

    static string Hex(Color c) { return string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B); }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathOf()));
            File.WriteAllText(PathOf(), Serialize());
        }
        catch { }   // settings just won't persist; the app keeps working
    }

    string Serialize()
    {
        return
            "; Crosshair overlay settings (pixels). Edit while the app is closed.\r\n" +
            "length=" + Length + "\r\n" +
            "gap=" + Gap + "\r\n" +
            "thickness=" + Thickness + "\r\n" +
            "dot=" + (Dot ? 1 : 0) + "\r\n" +
            "outline=" + Outline + "\r\n" +
            "color=" + Hex(Color) + "\r\n" +
            "outlinecolor=" + Hex(OutlineColor) + "\r\n" +
            "offsetx=" + OffsetX + "\r\n" +
            "offsety=" + OffsetY + "\r\n" +
            "; 0 = primary monitor, 1..N = monitor number\r\n" +
            "monitor=" + Monitor + "\r\n" +
            "; key name from System.Windows.Forms.Keys (F8, F9, Insert, Home...)\r\n" +
            "togglekey=" + ToggleKey + "\r\n";
    }
}

class Overlay : Form
{
    const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80,
              WS_EX_NOACTIVATE = 0x8000000, WS_EX_TOPMOST = 0x8;
    const int WM_HOTKEY = 0x312, HOTKEY_ID = 1;
    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;

    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);

    static readonly Color Key = Color.Magenta;
    Settings s;
    NotifyIcon tray;
    Timer topTimer;
    ControlPanel panel;
    int center;

    public event EventHandler CrosshairToggled;
    public Settings Current { get { return s; } }

    public Overlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Key;
        TransparencyKey = Key;
        DoubleBuffered = true;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Настройки", null, delegate { ShowPanel(); });
        menu.Items.Add("ON / OFF", null, delegate { ToggleVisible(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, delegate { Close(); });

        tray = new NotifyIcon();
        tray.Icon = MakeIcon(SystemInformation.SmallIconSize);
        tray.Text = "Crosshair overlay";
        tray.ContextMenuStrip = menu;
        tray.Visible = true;
        tray.DoubleClick += delegate { ShowPanel(); };

        // Some games steal topmost when they gain focus - re-assert periodically.
        topTimer = new Timer();
        topTimer.Interval = 1000;
        topTimer.Tick += delegate { if (Visible) SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE); };
        topTimer.Start();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        s = Settings.Load();
        Apply();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ShowPanel();
    }

    public void ShowPanel()
    {
        if (panel == null || panel.IsDisposed) panel = new ControlPanel(this);
        panel.Show();
        if (panel.WindowState == FormWindowState.Minimized) panel.WindowState = FormWindowState.Normal;
        panel.Activate();
    }

    // Re-layout and repaint from the current settings (called live from the panel).
    public void Apply()
    {
        int reach = Math.Max(s.Gap + s.Length, s.Thickness) + s.Outline + 2;
        int size = reach * 2 + s.Thickness + 1;
        center = size / 2;

        var screens = Screen.AllScreens;
        Screen scr = (s.Monitor >= 1 && s.Monitor <= screens.Length) ? screens[s.Monitor - 1] : Screen.PrimaryScreen;
        var b = scr.Bounds;
        int cx = b.Left + b.Width / 2 + s.OffsetX;
        int cy = b.Top + b.Height / 2 + s.OffsetY;

        SetWindowPos(Handle, HWND_TOPMOST, cx - center, cy - center, size, size, SWP_NOACTIVATE);

        UnregisterHotKey(Handle, HOTKEY_ID);
        RegisterHotKey(Handle, HOTKEY_ID, 0x4000 /* MOD_NOREPEAT */, (uint)s.ToggleKey);

        Invalidate();
    }

    public void ToggleVisible()
    {
        if (Visible) Hide();
        else { Show(); SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW); }
        if (CrosshairToggled != null) CrosshairToggled(this, EventArgs.Empty);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && (int)m.WParam == HOTKEY_ID) ToggleVisible();
        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Key);
        if (s != null) CrosshairPainter.Draw(e.Graphics, s, center, center);
    }

    public static Icon MakeIcon(Size size)
    {
        using (var st = typeof(Overlay).Assembly.GetManifestResourceStream("crosshair.ico"))
            return new Icon(st, size);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        UnregisterHotKey(Handle, HOTKEY_ID);
        tray.Visible = false;
        tray.Dispose();
        base.OnFormClosed(e);
    }
}

static class Program
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main()
    {
        bool created;
        using (new System.Threading.Mutex(true, "CrosshairOverlay_SingleInstance", out created))
        {
            if (!created) return;
            SetProcessDPIAware();     // real pixel coordinates on scaled displays
            Application.EnableVisualStyles();
            Application.Run(new Overlay());
        }
    }
}
