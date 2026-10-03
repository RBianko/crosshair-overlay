using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Shared crosshair renderer: used by the overlay and the preview.
static class CrosshairPainter
{
    public static void Draw(Graphics g, Settings s, int cx, int cy)
    {
        var oldSmooth = g.SmoothingMode;
        var oldOffset = g.PixelOffsetMode;
        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.None;

        int t = s.Thickness, half = t / 2;
        int ax = cx - half, ay = cy - half;   // top/left edge of a line crossing the center
        Rectangle[] parts =
        {
            new Rectangle(cx - s.Gap - s.Length, ay, s.Length, t),          // left
            new Rectangle(cx + s.Gap + (t % 2), ay, s.Length, t),           // right
            new Rectangle(ax, cy - s.Gap - s.Length, t, s.Length),          // top
            new Rectangle(ax, cy + s.Gap + (t % 2), t, s.Length),           // bottom
        };
        var dot = new Rectangle(ax, ay, t, t);

        int o = s.Outline;
        using (var ob = new SolidBrush(s.OutlineColor))
        using (var fb = new SolidBrush(s.Color))
        {
            if (o > 0)
            {
                if (s.Length > 0) foreach (var r in parts) g.FillRectangle(ob, Grow(r, o));
                if (s.Dot) g.FillRectangle(ob, Grow(dot, o));
            }
            if (s.Length > 0) foreach (var r in parts) g.FillRectangle(fb, r);
            if (s.Dot) g.FillRectangle(fb, dot);
        }

        g.SmoothingMode = oldSmooth;
        g.PixelOffsetMode = oldOffset;
    }

    static Rectangle Grow(Rectangle r, int o) { r.Inflate(o, o); return r; }
}

// Theme, DPI scaling and drawing helpers.
static class UI
{
    public static readonly float S = GetScale();
    public static readonly Color Bg = Color.FromArgb(15, 17, 21);
    public static readonly Color Header = Color.FromArgb(20, 23, 28);
    public static readonly Color Card = Color.FromArgb(26, 29, 35);
    public static readonly Color Line = Color.FromArgb(40, 44, 52);
    public static readonly Color LineHover = Color.FromArgb(62, 68, 80);
    public static readonly Color Text = Color.FromArgb(232, 234, 237);
    public static readonly Color Muted = Color.FromArgb(128, 136, 148);
    public static readonly Color On = Color.FromArgb(34, 197, 94);
    public static readonly Color Danger = Color.FromArgb(230, 72, 72);

    static float GetScale()
    {
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96f;
    }

    public static int D(float px) { return (int)Math.Round(px * S); }

    static readonly string[] Installed = FontFamily.Families.Select(f => f.Name).ToArray();

    public static Font Font(float pt, bool bold)
    {
        string fam = bold ? "Bahnschrift SemiBold" : "Bahnschrift";
        if (Installed.Contains(fam)) return new Font(fam, pt);
        return new Font("Segoe UI", pt, bold ? FontStyle.Bold : FontStyle.Regular);
    }

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void Smooth(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    public static Color Mix(Color a, Color b, float t)
    {
        t = Math.Max(0, Math.Min(1, t));
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    public static Color Hue(float h)
    {
        h = ((h % 360) + 360) % 360 / 60f;
        int i = (int)h; float f = h - i;
        int q = (int)(255 * (1 - f)), t = (int)(255 * f);
        switch (i)
        {
            case 0: return Color.FromArgb(255, t, 0);
            case 1: return Color.FromArgb(q, 255, 0);
            case 2: return Color.FromArgb(0, 255, t);
            case 3: return Color.FromArgb(0, q, 255);
            case 4: return Color.FromArgb(t, 0, 255);
            default: return Color.FromArgb(255, 0, q);
        }
    }

    // Pure magenta is the overlay's transparency key.
    public static Color Safe(Color c)
    {
        if (c.R == 255 && c.G == 0 && c.B == 255) return Color.FromArgb(254, 0, 255);
        return Color.FromArgb(c.R, c.G, c.B);
    }
}

// Base for owner-drawn controls: double buffered, hover tracking, no focus cues.
class FlatControl : Control
{
    protected bool Hover, Pressed;

    public FlatControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = UI.Bg;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { Pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { Pressed = false; Invalidate(); base.OnMouseUp(e); }
}

class FlatButton : FlatControl
{
    public Color Fore = UI.Text, Border = Color.Empty, HoverFill = UI.Line, HoverFore = UI.Text;
    public float Radius = 6;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        UI.Smooth(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var p = UI.Round(r, UI.D(Radius)))
        {
            if (Hover)
                using (var b = new SolidBrush(Pressed ? UI.Mix(HoverFill, Color.Black, 0.2f) : HoverFill)) g.FillPath(b, p);
            if (!Border.IsEmpty && !Hover)
                using (var pen = new Pen(Border, Math.Max(1, UI.S))) g.DrawPath(pen, p);
        }
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, Hover ? HoverFore : Fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

// Crosshair preview at 1:1 scale over a dark/bright split backdrop, to judge contrast.
class Preview : FlatControl
{
    public Settings Settings;

    public Preview() { Cursor = Cursors.Default; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(UI.Bg);
        UI.Smooth(g);
        var r = new RectangleF(0, 0, Width - 1, Height - 1);
        using (var clip = UI.Round(r, UI.D(10)))
        {
            g.SetClip(clip);
            int mid = Width / 2;
            using (var dark = new LinearGradientBrush(new Rectangle(0, 0, mid, Height), Color.FromArgb(58, 52, 44), Color.FromArgb(30, 28, 25), 90f))
                g.FillRectangle(dark, 0, 0, mid, Height);
            using (var light = new LinearGradientBrush(new Rectangle(mid, 0, Width - mid, Height), Color.FromArgb(150, 178, 205), Color.FromArgb(205, 180, 135), 90f))
                g.FillRectangle(light, mid, 0, Width - mid, Height);
            // soft vignette
            using (var vg = new LinearGradientBrush(new Rectangle(0, -1, Width, Height / 3 + 2), Color.FromArgb(70, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 90f))
                g.FillRectangle(vg, 0, 0, Width, Height / 3);

            if (Settings != null) CrosshairPainter.Draw(g, Settings, Width / 2, Height / 2);
            g.ResetClip();
            using (var pen = new Pen(UI.Line, Math.Max(1, UI.S))) g.DrawPath(pen, clip);
        }
        using (var f = UI.Font(7.5f, true))
        using (var bg = new SolidBrush(Color.FromArgb(150, 10, 12, 15)))
        {
            string tag = "ПРЕДПРОСМОТР  1:1";
            var sz = TextRenderer.MeasureText(g, tag, f, Size.Empty, TextFormatFlags.NoPadding);
            var tr = new RectangleF(UI.D(8), UI.D(8), sz.Width + UI.D(12), sz.Height + UI.D(6));
            using (var p = UI.Round(tr, UI.D(4))) g.FillPath(bg, p);
            TextRenderer.DrawText(g, tag, f, Rectangle.Round(tr), Color.FromArgb(200, 205, 212),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}

// Caption + preset swatches + hue strip.
class ColorPicker : FlatControl
{
    static readonly Color[] Presets =
    {
        Color.FromArgb(0, 255, 0), Color.FromArgb(0, 255, 255), Color.FromArgb(255, 255, 0),
        Color.FromArgb(255, 40, 40), Color.FromArgb(254, 0, 255), Color.White,
    };

    public Color Value;
    public event EventHandler ValueChanged;
    int hoverSwatch = -1;
    bool dragHue;

    int Sw { get { return UI.D(30); } }
    int Gapw { get { return UI.D(10); } }
    int SwTop { get { return UI.D(26); } }
    Rectangle HueRect { get { return new Rectangle(UI.D(4), SwTop + Sw + UI.D(16), Width - UI.D(8), UI.D(10)); } }

    Rectangle SwRect(int i) { return new Rectangle(UI.D(4) + i * (Sw + Gapw), SwTop, Sw, Sw); }

    public ColorPicker() { Height = UI.D(26) + UI.D(30) + UI.D(36); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        UI.Smooth(g);

        using (var f = UI.Font(8.5f, true))
            TextRenderer.DrawText(g, "ЦВЕТ", f, new Point(0, 0), UI.Muted, TextFormatFlags.NoPadding);
        using (var f = UI.Font(9f, false))
        {
            string hex = string.Format("#{0:X2}{1:X2}{2:X2}", Value.R, Value.G, Value.B);
            var sz = TextRenderer.MeasureText(g, hex, f, Size.Empty, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, hex, f, new Point(Width - sz.Width, 0), UI.Text, TextFormatFlags.NoPadding);
        }

        for (int i = 0; i < Presets.Length; i++)
        {
            var r = SwRect(i);
            bool sel = Presets[i].ToArgb() == Value.ToArgb();
            if (sel || i == hoverSwatch)
            {
                var ring = RectangleF.Inflate(r, UI.D(3.5f), UI.D(3.5f));
                using (var p = UI.Round(ring, UI.D(9)))
                using (var pen = new Pen(sel ? UI.Text : UI.LineHover, UI.D(2))) g.DrawPath(pen, p);
            }
            using (var p = UI.Round(r, UI.D(7)))
            using (var b = new SolidBrush(Presets[i])) g.FillPath(b, p);
        }

        // hue strip
        var h = HueRect;
        using (var p = UI.Round(h, h.Height / 2f))
        using (var lb = new LinearGradientBrush(h, Color.Red, Color.Red, 0f))
        {
            var cb = new ColorBlend(7);
            for (int i = 0; i < 7; i++) { cb.Colors[i] = UI.Hue(i * 60); cb.Positions[i] = i / 6f; }
            lb.InterpolationColors = cb;
            g.FillPath(lb, p);
        }
        float sat = Value.GetSaturation();
        bool onStrip = Value.GetBrightness() > 0.2f && Value.GetBrightness() < 0.8f && sat > 0.6f
                       && Array.IndexOf(Presets, Value) < 0 || dragHue;
        if (onStrip)
        {
            float x = h.X + h.Width * (Value.GetHue() / 360f);
            float kr = UI.D(9);
            var kc = new RectangleF(x - kr, h.Y + h.Height / 2f - kr, kr * 2, kr * 2);
            using (var b = new SolidBrush(Value)) g.FillEllipse(b, kc);
            using (var pen = new Pen(Color.White, UI.D(2.5f))) g.DrawEllipse(pen, kc);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragHue) { PickHue(e.X); return; }
        int hs = -1;
        for (int i = 0; i < Presets.Length; i++) if (SwRect(i).Contains(e.Location)) hs = i;
        bool overHue = Rectangle.Inflate(HueRect, 0, UI.D(6)).Contains(e.Location);
        Cursor = hs >= 0 || overHue ? Cursors.Hand : Cursors.Default;
        if (hs != hoverSwatch) { hoverSwatch = hs; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { hoverSwatch = -1; base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        for (int i = 0; i < Presets.Length; i++)
            if (SwRect(i).Contains(e.Location)) { Set(Presets[i]); return; }
        if (Rectangle.Inflate(HueRect, 0, UI.D(6)).Contains(e.Location)) { dragHue = true; Capture = true; PickHue(e.X); }
    }

    protected override void OnMouseUp(MouseEventArgs e) { dragHue = false; Capture = false; base.OnMouseUp(e); }

    void PickHue(int x)
    {
        var h = HueRect;
        float t = Math.Max(0, Math.Min(1, (x - h.X) / (float)h.Width));
        Set(UI.Hue(t * 359.9f));
    }

    void Set(Color c)
    {
        Value = UI.Safe(c);
        Invalidate();
        if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
    }
}

class Slider : FlatControl
{
    public string Caption = "";
    public string Unit = "px";
    public int Min = 0, Max = 10;
    public Color Accent = UI.On;
    public event EventHandler ValueChanged;
    int value;
    bool drag;

    public Slider() { Height = UI.D(50); }

    public int Value
    {
        get { return value; }
        set
        {
            int v = Math.Max(Min, Math.Min(Max, value));
            if (v == this.value) return;
            this.value = v;
            Invalidate();
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }
    }

    int Pad { get { return UI.D(10); } }
    int TrackY { get { return UI.D(36); } }

    float KnobX { get { return Pad + (Width - 2 * Pad) * (Max == Min ? 0 : (value - Min) / (float)(Max - Min)); } }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        UI.Smooth(g);

        using (var f = UI.Font(8.5f, true))
            TextRenderer.DrawText(g, Caption.ToUpperInvariant(), f, new Point(0, 0), UI.Muted, TextFormatFlags.NoPadding);
        using (var f = UI.Font(10.5f, true))
        using (var fu = UI.Font(8.5f, false))
        {
            string v = value.ToString();
            var su = TextRenderer.MeasureText(g, Unit, fu, Size.Empty, TextFormatFlags.NoPadding);
            var sv = TextRenderer.MeasureText(g, v, f, Size.Empty, TextFormatFlags.NoPadding);
            int right = Width;
            TextRenderer.DrawText(g, Unit, fu, new Point(right - su.Width, sv.Height - su.Height), UI.Muted, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, v, f, new Point(right - su.Width - UI.D(3) - sv.Width, 0), UI.Text, TextFormatFlags.NoPadding);
        }

        float th = UI.D(5), y = TrackY;
        var track = new RectangleF(Pad, y - th / 2, Width - 2 * Pad, th);
        using (var p = UI.Round(track, th / 2))
        using (var b = new SolidBrush(UI.Line)) g.FillPath(b, p);
        float kx = KnobX;
        if (kx - Pad > 0.5f)
            using (var p = UI.Round(new RectangleF(Pad, y - th / 2, kx - Pad, th), th / 2))
            using (var b = new SolidBrush(Accent)) g.FillPath(b, p);

        // ticks
        if (Max - Min <= 12)
            using (var b = new SolidBrush(Color.FromArgb(70, UI.Muted)))
                for (int i = Min; i <= Max; i++)
                {
                    float tx = Pad + (Width - 2 * Pad) * (i - Min) / (float)(Max - Min);
                    g.FillEllipse(b, tx - UI.D(1.5f), y + UI.D(8), UI.D(3), UI.D(3));
                }

        float kr = UI.D(8);
        if (Hover || drag)
            using (var b = new SolidBrush(Color.FromArgb(55, Accent)))
                g.FillEllipse(b, kx - kr * 1.7f, y - kr * 1.7f, kr * 3.4f, kr * 3.4f);
        using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kx - kr, y - kr, kr * 2, kr * 2);
        using (var pen = new Pen(Accent, UI.D(2.5f))) g.DrawEllipse(pen, kx - kr + UI.D(1.25f), y - kr + UI.D(1.25f), kr * 2 - UI.D(2.5f), kr * 2 - UI.D(2.5f));
    }

    void FromX(int x)
    {
        float t = (x - Pad) / (float)(Width - 2 * Pad);
        Value = Min + (int)Math.Round(Math.Max(0, Math.Min(1, t)) * (Max - Min));
    }

    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); drag = true; Capture = true; FromX(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (drag) FromX(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); drag = false; Capture = false; Invalidate(); }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Value += Math.Sign(e.Delta); }
}

// Card with title, hint and an animated pill switch.
class ToggleCard : FlatControl
{
    public event EventHandler Toggle;
    bool on;
    float anim;
    Timer timer = new Timer();

    public ToggleCard()
    {
        Height = UI.D(64);
        timer.Interval = 15;
        timer.Tick += delegate
        {
            float target = on ? 1 : 0;
            anim += (target - anim) * 0.35f;
            if (Math.Abs(target - anim) < 0.01f) { anim = target; timer.Stop(); }
            Invalidate();
        };
    }

    public bool On
    {
        get { return on; }
        set { if (on == value) return; on = value; if (IsHandleCreated) timer.Start(); else anim = on ? 1 : 0; }
    }

    protected override void OnClick(EventArgs e) { base.OnClick(e); if (Toggle != null) Toggle(this, EventArgs.Empty); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        UI.Smooth(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var p = UI.Round(r, UI.D(10)))
        {
            using (var b = new SolidBrush(Hover ? UI.Mix(UI.Card, Color.White, 0.03f) : UI.Card)) g.FillPath(b, p);
            using (var pen = new Pen(Hover ? UI.LineHover : UI.Line, Math.Max(1, UI.S))) g.DrawPath(pen, p);
        }

        int x = UI.D(16);
        using (var f = UI.Font(11f, true))
            TextRenderer.DrawText(g, "ПРИЦЕЛ", f, new Point(x, UI.D(13)), UI.Text, TextFormatFlags.NoPadding);
        using (var f = UI.Font(8.5f, false))
            TextRenderer.DrawText(g, "F8 — быстрое вкл / выкл", f, new Point(x, UI.D(36)), UI.Muted, TextFormatFlags.NoPadding);

        float pw = UI.D(54), ph = UI.D(28);
        var pill = new RectangleF(Width - UI.D(16) - pw, (Height - ph) / 2f, pw, ph);
        Color pc = UI.Mix(Color.FromArgb(58, 63, 72), UI.On, anim);
        using (var p = UI.Round(pill, ph / 2))
        using (var b = new SolidBrush(pc)) g.FillPath(b, p);
        float kr = ph / 2 - UI.D(3);
        float kx = pill.X + ph / 2 + (pw - ph) * anim;
        using (var b = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
            g.FillEllipse(b, kx - kr, pill.Y + ph / 2 - kr + UI.D(1), kr * 2, kr * 2);
        using (var b = new SolidBrush(Color.White))
            g.FillEllipse(b, kx - kr, pill.Y + ph / 2 - kr, kr * 2, kr * 2);

        using (var f = UI.Font(9.5f, true))
        {
            string st = on ? "ON" : "OFF";
            var sz = TextRenderer.MeasureText(g, st, f, Size.Empty, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, st, f, new Point((int)pill.X - UI.D(10) - sz.Width, (Height - sz.Height) / 2),
                on ? UI.On : UI.Muted, TextFormatFlags.NoPadding);
        }
    }

    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
}

class ControlPanel : Form
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

    Overlay ov;
    Preview preview;
    ColorPicker picker;
    Slider thick, len;
    ToggleCard toggle;
    Icon smallIcon;
    int headerH;

    public ControlPanel(Overlay ov)
    {
        this.ov = ov;
        Text = "Crosshair";
        Icon = Overlay.MakeIcon(new Size(32, 32));
        smallIcon = Overlay.MakeIcon(new Size(UI.D(20), UI.D(20)));
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = UI.Bg;
        Font = UI.Font(9.5f, false);
        DoubleBuffered = true;

        int W = UI.D(360), pad = UI.D(20);
        headerH = UI.D(52);
        int inner = W - 2 * pad;

        var close = TitleButton("✕", W - UI.D(46));
        close.HoverFill = Color.FromArgb(196, 43, 28);
        close.HoverFore = Color.White;
        close.Click += delegate { Hide(); };
        var min = TitleButton("—", W - UI.D(88));
        min.Click += delegate { WindowState = FormWindowState.Minimized; };

        int y = headerH + UI.D(18);

        preview = new Preview();
        preview.Settings = ov.Current;
        preview.SetBounds(pad, y, inner, UI.D(130));
        Controls.Add(preview);
        y += preview.Height + UI.D(20);

        picker = new ColorPicker();
        picker.Value = ov.Current.Color;
        picker.SetBounds(pad, y, inner, picker.Height);
        picker.ValueChanged += delegate { ov.Current.Color = picker.Value; Commit(); };
        Controls.Add(picker);
        y += picker.Height + UI.D(14);

        thick = new Slider();
        thick.Caption = "Толщина";
        thick.Min = 1; thick.Max = 10;
        thick.Value = ov.Current.Thickness;
        thick.SetBounds(pad, y, inner, thick.Height);
        thick.ValueChanged += delegate { ov.Current.Thickness = thick.Value; Commit(); };
        Controls.Add(thick);
        y += thick.Height + UI.D(10);

        len = new Slider();
        len.Caption = "Длина риски";
        len.Min = 0; len.Max = 40;
        len.Value = ov.Current.Length;
        len.SetBounds(pad, y, inner, len.Height);
        len.ValueChanged += delegate { ov.Current.Length = len.Value; Commit(); };
        Controls.Add(len);
        y += len.Height + UI.D(18);

        toggle = new ToggleCard();
        toggle.On = ov.Visible;
        toggle.SetBounds(pad, y, inner, toggle.Height);
        toggle.Toggle += delegate { ov.ToggleVisible(); };
        Controls.Add(toggle);
        y += toggle.Height + UI.D(12);

        var exit = new FlatButton();
        exit.Text = "EXIT";
        exit.Font = UI.Font(10f, true);
        exit.Fore = UI.Danger;
        exit.Border = Color.FromArgb(90, 40, 40);
        exit.HoverFill = Color.FromArgb(200, 50, 50);
        exit.HoverFore = Color.White;
        exit.Radius = 10;
        exit.SetBounds(pad, y, inner, UI.D(44));
        exit.Click += delegate { ov.Close(); };
        Controls.Add(exit);
        y += exit.Height + pad;

        ClientSize = new Size(W, y);
        UpdateAccent();

        ov.CrosshairToggled += OnToggled;
    }

    FlatButton TitleButton(string text, int x)
    {
        var b = new FlatButton();
        b.Text = text;
        b.Font = new Font("Segoe UI", 10f);
        b.Fore = UI.Muted;
        b.BackColor = UI.Header;
        b.Radius = 6;
        b.SetBounds(x, UI.D(10), UI.D(36), UI.D(32));
        Controls.Add(b);
        return b;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style |= 0x20000;            // WS_MINIMIZEBOX: allow minimize / taskbar click
            cp.ClassStyle |= 0x20000;       // CS_DROPSHADOW
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int round = 2;                                      // DWMWCP_ROUND (Windows 11)
        DwmSetWindowAttribute(Handle, 33, ref round, 4);
        int border = UI.Line.R | (UI.Line.G << 8) | (UI.Line.B << 16);
        DwmSetWindowAttribute(Handle, 34, ref border, 4);   // DWMWA_BORDER_COLOR
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        // Drag the window by its header.
        if (m.Msg == 0x84 && (int)m.Result == 1)
        {
            var pt = PointToClient(new Point((short)(m.LParam.ToInt64() & 0xFFFF), (short)((m.LParam.ToInt64() >> 16) & 0xFFFF)));
            if (pt.Y < headerH) m.Result = (IntPtr)2;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        UI.Smooth(g);
        g.Clear(UI.Bg);
        using (var b = new SolidBrush(UI.Header)) g.FillRectangle(b, 0, 0, Width, headerH);
        using (var pen = new Pen(UI.Line)) g.DrawLine(pen, 0, headerH - 1, Width, headerH - 1);

        // accent stripe under the header in the crosshair color
        using (var lb = new LinearGradientBrush(new Rectangle(0, 0, Width, 2), Color.FromArgb(0, ov.Current.Color), ov.Current.Color, 0f))
        {
            var cb = new ColorBlend(3);
            cb.Colors = new[] { Color.FromArgb(0, ov.Current.Color), Color.FromArgb(200, ov.Current.Color), Color.FromArgb(0, ov.Current.Color) };
            cb.Positions = new[] { 0f, 0.5f, 1f };
            lb.InterpolationColors = cb;
            g.FillRectangle(lb, 0, headerH - UI.D(2), Width, UI.D(2));
        }

        g.DrawIcon(smallIcon, new Rectangle(UI.D(18), (headerH - UI.D(20)) / 2, UI.D(20), UI.D(20)));
        using (var f = UI.Font(12.5f, true))
        using (var fs = UI.Font(8.5f, false))
        {
            var sz = TextRenderer.MeasureText(g, "CROSSHAIR", f, Size.Empty, TextFormatFlags.NoPadding);
            int tx = UI.D(48), ty = (headerH - sz.Height) / 2;
            TextRenderer.DrawText(g, "CROSSHAIR", f, new Point(tx, ty), UI.Text, TextFormatFlags.NoPadding);
            var ss = TextRenderer.MeasureText(g, "OVERLAY", fs, Size.Empty, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "OVERLAY", fs, new Point(tx + sz.Width + UI.D(8), ty + sz.Height - ss.Height - UI.D(1)), UI.Muted, TextFormatFlags.NoPadding);
        }
    }

    void UpdateAccent()
    {
        thick.Accent = len.Accent = ov.Current.Color;
        thick.Invalidate();
        len.Invalidate();
        preview.Invalidate();
        Invalidate(new Rectangle(0, headerH - UI.D(2), Width, UI.D(2)));
    }

    void Commit()
    {
        ov.Apply();
        ov.Current.Save();
        UpdateAccent();
    }

    void OnToggled(object sender, EventArgs e) { toggle.On = ov.Visible; }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Alt+F4 just hides the panel; the overlay keeps running in the tray.
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        ov.CrosshairToggled -= OnToggled;
        base.OnFormClosed(e);
    }
}
