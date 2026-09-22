using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DockDX;

sealed class SheetItem
{
    public Item Data; public Grid El; public FrameworkElement Face; public Ellipse Run; public Widget W;
    public ScaleTransform Sc = new(1, 1); public TranslateTransform Tr = new();
    public double S = 1, Sv, Intro, Iv, P, Pv, Delay, Rs, Rl;
    public Rect Rect;
}

sealed class Sheet
{
    public string PageId; public Canvas El = new(); public List<SheetItem> Items = new();
    public double RestLen, Tx, Txv, Off, Tail, Tailv; public double? Drag; public bool Leaving; public int Dir, DropIndex = -1;
    public TranslateTransform Slide = new();
}

/// <summary>The dock: a transparent overlay drawing the icons over a separate acrylic backdrop window.</summary>
public sealed class DockWindow : Window
{
    const double SLIDE = 90, P = 10, Q = 26, E = 2, EDGE = 10;
    const int N = Store.PageCount;

    readonly BackdropWindow bd; readonly PreviewWindow pv;
    readonly Canvas root = new(), stage = new();
    readonly Border glass = new() { IsHitTestVisible = false };
    readonly Border rain = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed, Opacity = 0.9 };
    readonly Border fire = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly Rectangle zone = new() { Fill = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255)) };
    readonly StackPanel pager = new();
    readonly Border grip = new() { Width = 44, Height = 14, Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255)), Cursor = Cursors.SizeAll, Visibility = Visibility.Collapsed };
    readonly Border tip = new() { Opacity = 0, IsHitTestVisible = false }, chip = new() { Opacity = 0, IsHitTestVisible = false };
    readonly TextBlock tipText = new(), chipText = new();
    readonly TextBlock emptyHint = new() { Text = "Drop apps & files here", IsHitTestVisible = false, FontSize = 12, Opacity = 0 };
    readonly Border arrowPrev, arrowNext; readonly RotateTransform rotPrev = new(), rotNext = new();
    readonly Stopwatch sw = Stopwatch.StartNew();

    // configuration derived from settings
    double B, Gap, Zoom, R, Cross, RowStart, Room, Radius; bool H, AnchorEnd; string Pos;
    double OW, OH, scale = 1; IntPtr hwnd; int oxPx, oyPx;
    Native.RECT reserved; bool hasReserved;

    // auto-hide: the window slides physically off-screen except a thin sliver, sliding back in on hover
    int shownX, shownY, shownW, shownH, winT;
    double hide, hideV, hideCountdown;
    public bool SuppressAutoHide;
    bool hoverPolled;
    readonly DispatcherTimer autohidePoll = new() { Interval = TimeSpan.FromMilliseconds(70) };

    // state
    readonly List<Sheet> sheets = new();
    double L, Lv, last; bool active, dragActive, attached; int idle;
    SheetItem hovered; string dragId; bool dropped;
    (int id, Point start, Point origin)? swipe; bool swiped; Point downPt; SheetItem downItem;
    double wheelAcc; DateTime lastNav = DateTime.MinValue, lastWheel = DateTime.MinValue;
    readonly DispatcherTimer previewTimer = new() { Interval = TimeSpan.FromMilliseconds(320) };
    readonly DispatcherTimer previewHide = new() { Interval = TimeSpan.FromMilliseconds(320) };
    readonly DispatcherTimer dotTimer = new() { Interval = TimeSpan.FromMilliseconds(550) };
    readonly DispatcherTimer chipTimer = new() { Interval = TimeSpan.FromMilliseconds(1300) };
    readonly DispatcherTimer displayTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    int dotTarget = -1; bool keysRegistered;
    public event Action OpenPanel;

    public DockWindow(BackdropWindow backdrop, PreviewWindow preview)
    {
        bd = backdrop; pv = preview;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = null;
        ShowInTaskbar = false; Topmost = true; ShowActivated = false; Owner = bd; Width = 400; Height = 200; Left = -20000; Top = -20000;
        UseLayoutRounding = false; SnapsToDevicePixels = false;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        RenderOptions.SetEdgeMode(this, EdgeMode.Unspecified);

        root.Background = Brushes.Transparent;   // makes the whole window hit-testable, including blank areas (needed so hovering the auto-hide sliver is detected)
        root.Children.Add(glass); root.Children.Add(rain); root.Children.Add(fire); root.Children.Add(zone); root.Children.Add(emptyHint); root.Children.Add(stage); root.Children.Add(pager); root.Children.Add(grip);
        root.Children.Add(tip); root.Children.Add(chip);
        arrowPrev = MakeArrow(rotPrev, Prev); arrowNext = MakeArrow(rotNext, Next);
        root.Children.Add(arrowPrev); root.Children.Add(arrowNext);
        tip.Child = tipText; chip.Child = chipText;
        root.SizeChanged += (_, e) => { OW = e.NewSize.Width; OH = e.NewSize.Height; Wake(); };
        Content = root;

        SourceInitialized += OnSourceInitialized;
        Wire();
        previewTimer.Tick += (_, _) => { previewTimer.Stop(); if (hovered != null) ShowPreview(hovered); };
        previewHide.Tick += (_, _) => { previewHide.Stop(); if (!pv.Hovering && hovered == null) pv.HidePreview(); };
        pv.Departed += () => { previewHide.Stop(); previewHide.Start(); };
        chipTimer.Tick += (_, _) => { chipTimer.Stop(); chip.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(250))); };
        displayTimer.Tick += (_, _) => { displayTimer.Stop(); PositionWindow(); };
        autohidePoll.Tick += (_, _) =>
        {
            if (!Store.S.AutoHide || Pos == "floating" || hwnd == IntPtr.Zero) { hoverPolled = false; return; }
            Native.GetCursorPos(out var cp);
            bool over = cp.X >= oxPx && cp.X < oxPx + shownW && cp.Y >= oyPx && cp.Y < oyPx + shownH;
            if (over && !hoverPolled) Wake();
            hoverPolled = over;
        };
        autohidePoll.Start();

        Store.PagesChanged += p => { if (p == Store.St.Current) RebuildCurrent(); else BuildPager(); };
        Store.LayoutChanged += BuildPager;
        Store.SettingChanged += k => { if (k == "autostart") { Store.SetAutostart(Store.S.Autostart); return; } if (k is "units" or "labels" or "previews" or "wheel") { if (k == "units") RebuildCurrent(); return; } ApplySettings(); };
        Store.Reset += () => { ApplySettings(); };
        Live.WindowsChanged += RefreshRunning;
        Closed += (_, _) => Native.AppBarRemove(hwnd);
    }

    /* ============================================================ setup */
    void OnSourceInitialized(object s, EventArgs e)
    {
        hwnd = new WindowInteropHelper(this).Handle;
        Native.AddExStyle(hwnd, Native.WS_EX_TOOLWINDOW);
        var src = HwndSource.FromHwnd(hwnd);
        src.AddHook(WndProc);
        scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Native.RegisterHotKey(hwnd, 10, 0x1 | 0x2, 0x20);   // Ctrl+Alt+Space toggle
        Native.RegisterHotKey(hwnd, 11, 0x1 | 0x2, 0x25);   // Ctrl+Alt+Left
        Native.RegisterHotKey(hwnd, 12, 0x1 | 0x2, 0x27);   // Ctrl+Alt+Right
    }

    IntPtr WndProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        switch (msg)
        {
            case Native.WM_MOUSEACTIVATE: handled = true; return new IntPtr(3);
            case Native.WM_HOTKEY:
                switch (w.ToInt32())
                {
                    case 10: ToggleVisible(); break;
                    case 11: Prev(); break;
                    case 12: Next(); break;
                    case 20: case 22: Prev(); break;      // plain arrows while hovering
                    case 21: case 23: Next(); break;
                }
                handled = true; break;
            case Native.WM_DISPLAYCHANGE: case Native.WM_SETTINGCHANGE:
                displayTimer.Stop(); displayTimer.Start(); break;
        }
        return IntPtr.Zero;
    }

    /// <summary>Debug aid (set DOCKDX_DEBUG=1): create %TEMP%\dockdx.snap to get %TEMP%\dockdx.png rendered over a mock wallpaper.</summary>
    void EnableSnapshots()
    {
        if (Environment.GetEnvironmentVariable("DOCKDX_DEBUG") != "1") return;
        string req = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dockdx.snap"), png = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dockdx.png");
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        t.Tick += (_, _) =>
        {
            string pr = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dockdx.panel"); if (File.Exists(pr)) { File.Delete(pr); OpenPanel?.Invoke(); return; }
            if (!File.Exists(req)) return; File.Delete(req);
            int w = (int)Math.Ceiling(root.ActualWidth * scale), h = (int)Math.Ceiling(root.ActualHeight * scale);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(70, 50, 160), Color.FromRgb(200, 60, 120), 20), null, new Rect(0, 0, w, h));
                var g = glass; dc.PushTransform(new ScaleTransform(scale, scale));
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(Store.S.Opacity * 255), Theme.Dark ? (byte)(Store.S.Preset == "matrix" ? 1 : 14) : (byte)255, Theme.Dark ? (byte)(Store.S.Preset == "matrix" ? 6 : 17) : (byte)255, Theme.Dark ? (byte)(Store.S.Preset == "matrix" ? 3 : 28) : (byte)255)), null, new Rect(Canvas.GetLeft(g), Canvas.GetTop(g), g.Width, g.Height));
                dc.Pop();
            }
            var rtb = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32); rtb.Render(dv); rtb.Render(root);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(png); enc.Save(fs);
        };
        t.Start();
    }

    public void Start()
    {
        EnableSnapshots();
        Show();
        // at logon the taskbar may not have claimed its work area yet, so re-place the dock shortly after start
        foreach (int ms in new[] { 3000, 9000 }) { var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) }; t.Tick += (_, _) => { t.Stop(); PositionWindow(); }; t.Start(); }
        ApplySettings(); Wake();
    }

    public void ToggleVisible()
    {
        if (IsVisible) { pv.HidePreview(); bd.Hide(); Hide(); }
        else { bd.Show(); Show(); PositionWindow(); Wake(); }
    }

    void SetArrowHotkeys(bool on)
    {
        if (on == keysRegistered) return; keysRegistered = on;
        uint[] vk = { 0x25, 0x27, 0x26, 0x28 }; int[] id = { 20, 21, 22, 23 };   // Left/Right/Up/Down
        for (int i = 0; i < 4; i++) { if (on) Native.RegisterHotKey(hwnd, id[i], 0, vk[i]); else Native.UnregisterHotKey(hwnd, id[i]); }
    }

    /* ============================================================ configuration + placement */
    void ReadConfig()
    {
        var s = Store.S; Pos = s.Position; B = s.IconSize; Zoom = s.Zoom; R = s.Spread * B;
        H = Pos != "left" && Pos != "right";
        AnchorEnd = Pos is "bottom" or "floating" or "right";
        Gap = Math.Max(5, Math.Round(B * 0.12));
        Cross = P + B + Q + E; RowStart = AnchorEnd ? P : E + Q; Room = B * (Zoom - 1) + 12; Radius = B * 0.42 + 2;
    }

    public void ApplySettings()
    {
        var s = Store.S;
        bool dark = s.Theme == "auto" ? SystemUsesDark() : s.Theme == "dark";
        Theme.Apply(dark, s.Hue);
        bd.ApplyTint();
        ReadConfig();
        tipText.Foreground = Theme.Ink; tipText.FontSize = 12.5; tipText.Margin = new Thickness(10, 4, 10, 5);
        tip.Background = Theme.PanelBg; tip.BorderBrush = Theme.Line; tip.BorderThickness = new Thickness(1); tip.CornerRadius = new CornerRadius(9);
        chipText.Foreground = Theme.Ink; chipText.FontSize = 12.5; chipText.Margin = new Thickness(14, 5, 14, 6);
        chip.Background = Theme.PanelBg; chip.BorderBrush = Theme.Line; chip.BorderThickness = new Thickness(1); chip.CornerRadius = new CornerRadius(15);
        emptyHint.Foreground = Theme.Ink3;
        glass.CornerRadius = new CornerRadius(Radius); glass.BorderBrush = Theme.Line; glass.BorderThickness = new Thickness(1);
        glass.Background = new LinearGradientBrush(Color.FromArgb(Theme.Dark ? (byte)46 : (byte)120, 255, 255, 255), Color.FromArgb(4, 255, 255, 255), 90);
        bool matrix = dark && s.Preset == "matrix";
        rain.Visibility = matrix ? Visibility.Visible : Visibility.Collapsed;
        if (matrix && rain.Background == null) rain.Background = MakeRain(1);
        if (matrix && rain.Child == null) { rain.Child = new Border { Background = MakeRain(2), Opacity = 0.6 }; }
        rain.CornerRadius = new CornerRadius(Radius);
        bool fireOn = dark && s.Preset == "fire";
        fire.Visibility = fireOn ? Visibility.Visible : Visibility.Collapsed;
        if (fireOn && fire.Child == null) BuildFire();
        rain.Clip = null; fire.Clip = null;   // re-clipped to the rounded glass on the next frame
        rotPrev.Angle = H ? 0 : 90; rotNext.Angle = H ? 180 : 270;
        grip.Visibility = Pos == "floating" ? Visibility.Visible : Visibility.Collapsed;
        Store.SetAutostart(s.Autostart);
        foreach (var sh in sheets.ToList()) if (!sh.Leaving) { }
        RebuildAll();
        PositionWindow();
    }

    /// <summary>Sizes a glass effect layer to the glass and clips it to the rounded corners (only when the size changes).</summary>
    void FitFx(Border b, double x, double y, double w, double h)
    {
        Canvas.SetLeft(b, x); Canvas.SetTop(b, y);
        if (b.Clip == null || Math.Abs(b.Width - w) > 0.4 || Math.Abs(b.Height - h) > 0.4)
        {
            b.Width = w; b.Height = h;
            var clip = new RectangleGeometry(new Rect(0, 0, w, h), Radius, Radius); clip.Freeze(); b.Clip = clip;
        }
    }

    /// <summary>Fire theme: a flickering ember glow, a wavering row of flame tongues along the base, and rising sparks.</summary>
    void BuildFire()
    {
        var grid = new Grid();
        var glow = new Border { Background = new LinearGradientBrush(new GradientStopCollection {
            new(Color.FromArgb(190, 255, 84, 0), 0), new(Color.FromArgb(80, 255, 40, 0), 0.5), new(Color.FromArgb(0, 255, 40, 0), 1) }, new Point(0, 1), new Point(0, 0)) };
        glow.BeginAnimation(OpacityProperty, new DoubleAnimation(0.62, 1, TimeSpan.FromMilliseconds(520)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        grid.Children.Add(glow);

        var flames = new Border { Height = 50, VerticalAlignment = VerticalAlignment.Bottom, Background = MakeFlames(), Opacity = 0.9 };
        flames.BeginAnimation(OpacityProperty, new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(380)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        grid.Children.Add(flames);
        grid.Children.Add(new Border { Background = MakeEmbers(1) });
        grid.Children.Add(new Border { Background = MakeEmbers(2) });
        fire.Child = grid;
    }

    Brush MakeFlames()
    {
        const double tw = 168, th = 50; var rnd = new Random(11);
        var dg = new DrawingGroup();
        using (var dc = dg.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, tw, th));
            // outer red, mid orange, inner yellow tongues; heights vary so the skyline looks organic
            double[] xs = { 8, 50, 92, 134 }; double[] hs = { 0.78, 0.55, 0.92, 0.66 };
            for (int t = 0; t < xs.Length; t++)
            {
                double cx = xs[t] + 14, top = th * (1 - hs[t]), w = 46;
                foreach (var (scale, c1, c2) in new[] { (1.0, Color.FromArgb(0, 255, 30, 0), Color.FromArgb(215, 235, 50, 0)), (0.74, Color.FromArgb(0, 255, 120, 0), Color.FromArgb(235, 255, 128, 10)), (0.46, Color.FromArgb(0, 255, 220, 60), Color.FromArgb(250, 255, 236, 120)) })
                {
                    double h = (th - top) * scale, y0 = th, y1 = th - h, hw = w * scale / 2;
                    var sg = new StreamGeometry();
                    using (var g = sg.Open())
                    {
                        g.BeginFigure(new Point(cx - hw, y0), true, true);
                        g.BezierTo(new Point(cx - hw * 1.1, y0 - h * 0.45), new Point(cx - hw * 0.25 + rnd.Next(-4, 5), y1 + h * 0.34), new Point(cx + rnd.Next(-3, 4), y1), true, true);
                        g.BezierTo(new Point(cx + hw * 0.3, y1 + h * 0.34), new Point(cx + hw * 1.1, y0 - h * 0.45), new Point(cx + hw, y0), true, true);
                    }
                    sg.Freeze();
                    dc.DrawGeometry(new LinearGradientBrush(c1, c2, new Point(0, 0), new Point(0, 1)), null, sg);
                }
            }
        }
        var brush = new DrawingBrush(dg) { TileMode = TileMode.Tile, Stretch = Stretch.None, Viewport = new Rect(0, 0, tw, th), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, tw, th), ViewboxUnits = BrushMappingMode.Absolute };
        var tr = new TranslateTransform(); brush.Transform = tr;
        tr.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, tw, TimeSpan.FromSeconds(7)) { RepeatBehavior = RepeatBehavior.Forever });
        return brush;
    }

    Brush MakeEmbers(int layer)
    {
        const double tw = 240, th = 210; var rnd = new Random(layer * 31);
        var dg = new DrawingGroup();
        using (var dc = dg.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, tw, th));
            for (int i = 0; i < (layer == 1 ? 22 : 14); i++)
            {
                double r = (layer == 1 ? 1.2 : 2) + rnd.NextDouble() * (layer == 1 ? 1.6 : 2.4), x = rnd.NextDouble() * tw, y = rnd.NextDouble() * th;
                byte a = (byte)rnd.Next(120, 255); var core = i % 3 == 0 ? Color.FromArgb(a, 255, 230, 120) : Color.FromArgb(a, 255, 130, 30);
                var rg = new RadialGradientBrush(core, Color.FromArgb(0, 255, 60, 0));
                dc.DrawEllipse(rg, null, new Point(x, y), r * 2.6, r * 2.6);
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(a, 255, 245, 190)), null, new Point(x, y), r * 0.55, r * 0.55);
            }
        }
        var brush = new DrawingBrush(dg) { TileMode = TileMode.Tile, Stretch = Stretch.None, Viewport = new Rect(0, 0, tw, th), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, tw, th), ViewboxUnits = BrushMappingMode.Absolute };
        var tr = new TranslateTransform(); brush.Transform = tr;
        tr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -th, TimeSpan.FromSeconds(layer == 1 ? 6.5 : 4)) { RepeatBehavior = RepeatBehavior.Forever });
        return brush;
    }

    /// <summary>Seamless tiling "digital rain" brush; the tile scrolls forever via a brush transform (cheap, no per-frame work).</summary>
    Brush MakeRain(int layer)
    {
        const double tw = 264, th = 220; double fs = layer == 1 ? 13 : 17, colW = fs * 1.05;
        var rnd = new Random(layer * 7919);
        const string chars = "ｦｱｳｴｵｶｷｸｹｺｻｼｽｾｿﾀﾁﾂﾃﾅﾆﾇﾈﾊﾋﾎﾏﾐﾑﾒﾓﾔﾕﾗﾘﾜ0123456789";
        var face = new Typeface("MS Gothic, Consolas");
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var dg = new DrawingGroup();
        using (var dc = dg.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, tw, th));
            for (int c = 0; c < (int)(tw / colW); c++)
            {
                int len = rnd.Next(8, 18); double head = rnd.NextDouble() * th;
                for (int i = 0; i < len; i++)
                {
                    double y = head - i * fs * 1.1; byte a = (byte)(255 * Math.Pow(1 - i / (double)len, 1.6));
                    var col = i == 0 ? Color.FromArgb(255, 220, 255, 230) : Color.FromArgb(a, 40, 255, 100);
                    var ft = new FormattedText(chars[rnd.Next(chars.Length)].ToString(), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, fs, new SolidColorBrush(col), ppd);
                    foreach (double yy in new[] { y, y + th }) if (yy > -fs && yy < th + fs) dc.DrawText(ft, new Point(c * colW, ((yy % th) + th) % th));
                }
            }
        }
        var brush = new DrawingBrush(dg) { TileMode = TileMode.Tile, Stretch = Stretch.None, Viewport = new Rect(0, 0, tw, th), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, tw, th), ViewboxUnits = BrushMappingMode.Absolute };
        var tr = new TranslateTransform(); brush.Transform = tr;
        tr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, th, TimeSpan.FromSeconds(layer == 1 ? 9 : 5.5)) { RepeatBehavior = RepeatBehavior.Forever });
        return brush;
    }

    static bool SystemUsesDark()
    {
        try { return (int)(Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) ?? 1) == 0; }
        catch { return true; }
    }

    void PositionWindow()
    {
        if (hwnd == IntPtr.Zero) return;
        scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var scr = System.Windows.Forms.Screen.PrimaryScreen;
        var area = scr.WorkingArea;
        int t = (int)Math.Ceiling((Cross + EDGE + Room + 70) * scale);
        int reservePx = (int)Math.Ceiling((Cross + EDGE * 2) * scale);
        hasReserved = false;
        if (Store.S.Reserve && Pos != "floating")
        {
            var b = scr.Bounds; var rc = new Native.RECT { L = b.Left, T = b.Top, R = b.Right, B = b.Bottom };
            switch (Pos) { case "top": rc.B = rc.T + reservePx; break; case "left": rc.R = rc.L + reservePx; break; case "right": rc.L = rc.R - reservePx; break; default: rc.T = rc.B - reservePx; break; }
            reserved = Native.AppBarSet(hwnd, Pos, rc); hasReserved = true;
        }
        else Native.AppBarRemove(hwnd);

        int x, y, w, h;
        if (hasReserved)
        {
            // strip is anchored to the outer edge of the reserved band
            switch (Pos)
            {
                case "top": x = area.Left; w = area.Width; y = reserved.T; h = t; break;
                case "left": y = area.Top; h = area.Height; x = reserved.L; w = t; break;
                case "right": y = area.Top; h = area.Height; x = reserved.R - t; w = t; break;
                default: x = area.Left; w = area.Width; y = reserved.B - t; h = t; break;
            }
            if (Pos is "top" or "bottom") { x = scr.Bounds.Left; w = scr.Bounds.Width; }
        }
        else
        {
            switch (Pos)
            {
                case "top": x = area.Left; y = area.Top; w = area.Width; h = t; break;
                case "left": x = area.Left; y = area.Top; w = t; h = area.Height; break;
                case "right": x = area.Right - t; y = area.Top; w = t; h = area.Height; break;
                case "floating": x = area.Left; y = area.Top; w = area.Width; h = area.Height; break;
                default: x = area.Left; y = area.Bottom - t; w = area.Width; h = t; break;
            }
        }
        shownX = x; shownY = y; shownW = w; shownH = h; winT = t;
        ApplyWindowPlacement();
        Wake();
    }

    /// <summary>Places the native window at its normal ("shown") position, offset by the current auto-hide slide.</summary>
    void ApplyWindowPlacement()
    {
        if (hwnd == IntPtr.Zero) return;
        int x = shownX, y = shownY;
        if (hide > 0.0004 && Pos != "floating")
        {
            int sliverPx = Math.Max(1, (int)Math.Round(5 * scale));
            int shift = (int)Math.Round(hide * Math.Max(0, winT - sliverPx));
            switch (Pos)
            {
                case "bottom": y = shownY + shift; break;
                case "top": y = shownY - shift; break;
                case "right": x = shownX + shift; break;
                case "left": x = shownX - shift; break;
            }
        }
        oxPx = x; oyPx = y;
        Native.Place(hwnd, x, y, shownW, shownH);
    }

    /* ============================================================ sheets */
    Sheet CurSheet() { for (int i = sheets.Count - 1; i >= 0; i--) if (!sheets[i].Leaving) return sheets[i]; return null; }

    SheetItem MakeItem(Item d, SheetItem old)
    {
        double span = d.Span < 1 || (!H && d.Type == "live") ? 1 : d.Span, rl = d.Type == "sep" ? 8 : span * B + (span - 1) * Gap;   // vertical docks use compact square widgets
        var it = new SheetItem { Data = d, Rl = rl };
        var g = new Grid { Width = rl, Height = B, RenderTransformOrigin = new Point(0, 0), Tag = it };
        var tg = new TransformGroup(); tg.Children.Add(it.Sc); tg.Children.Add(it.Tr); g.RenderTransform = tg;

        if (d.Type == "sep")
        {
            it.Face = new Border { Width = 2, Height = B * 0.62, CornerRadius = new CornerRadius(1), Background = Theme.Ink3, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        }
        else if (d.Type == "live")
        {
            it.W = Widget.Create(d, B, rl);
            it.Face = new Border { Width = rl, Height = B, CornerRadius = new CornerRadius(B * 0.24), Background = Theme.Widget, BorderBrush = Theme.Line, BorderThickness = new Thickness(1), Child = it.W.Root, SnapsToDevicePixels = false };
            ((Border)it.Face).ClipToBounds = true;
        }
        else
        {
            var img = Icons.For(d);
            if (img != null) { var im = new Image { Source = img, Width = B, Height = B, Stretch = Stretch.Uniform, IsHitTestVisible = true }; RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality); it.Face = im; }
            else
            {
                double hue = d.Hue >= 0 ? d.Hue : Math.Abs(d.Name.Aggregate(0, (a, c) => (a * 31 + c) % 360));
                var tile = new Border
                {
                    Width = B, Height = B, CornerRadius = new CornerRadius(B * 0.22),
                    Background = new LinearGradientBrush(Theme.Hsl(hue, 0.82, 0.62), Theme.Hsl(hue + 42, 0.78, 0.42), 55),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), BorderThickness = new Thickness(1)
                };
                var gl = new Grid();
                gl.Children.Add(new Border { CornerRadius = new CornerRadius(B * 0.22), Background = new LinearGradientBrush(Color.FromArgb(105, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 70), IsHitTestVisible = false });
                gl.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(d.Glyph) ? d.Name.Trim().Substring(0, Math.Min(1, d.Name.Trim().Length)).ToUpper() : d.Glyph, FontSize = B * 0.48, Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false });
                tile.Child = gl; it.Face = tile;
            }
        }
        g.Children.Add(it.Face);
        it.Run = new Ellipse { Width = 5, Height = 5, Fill = Theme.Acc, Opacity = 0, IsHitTestVisible = false };
        switch (Pos)
        {
            case "top": it.Run.VerticalAlignment = VerticalAlignment.Top; it.Run.HorizontalAlignment = HorizontalAlignment.Center; it.Run.Margin = new Thickness(0, -9, 0, 0); break;
            case "left": it.Run.VerticalAlignment = VerticalAlignment.Center; it.Run.HorizontalAlignment = HorizontalAlignment.Left; it.Run.Margin = new Thickness(-9, 0, 0, 0); break;
            case "right": it.Run.VerticalAlignment = VerticalAlignment.Center; it.Run.HorizontalAlignment = HorizontalAlignment.Right; it.Run.Margin = new Thickness(0, 0, -9, 0); break;
            default: it.Run.VerticalAlignment = VerticalAlignment.Bottom; it.Run.HorizontalAlignment = HorizontalAlignment.Center; it.Run.Margin = new Thickness(0, 0, 0, -9); break;
        }
        g.Children.Add(it.Run);
        it.El = g;
        it.Run.Opacity = IsRunning(d) ? 1 : 0;

        g.MouseLeftButtonDown += (_, e) => { downPt = e.GetPosition(root); downItem = it; };
        g.MouseMove += ItemMouseMove;
        g.MouseLeftButtonUp += (_, e) => { if (!swiped && downItem == it && d.Type == "app") Launch(it); downItem = null; };
        g.MouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Middle && d.Type == "app") Launch(it, true); };

        if (old != null) { it.S = old.S; it.Sv = old.Sv; it.Intro = old.Intro; it.Iv = old.Iv; it.P = old.P; it.Pv = old.Pv; }
        return it;
    }

    void Measure(Sheet sh)
    {
        double x = 0;
        foreach (var it in sh.Items) { it.Rs = x; x += it.Rl + Gap; }
        sh.RestLen = Math.Max(0, x - Gap);
    }

    Sheet BuildSheet(int page, int dir, double stagger, double baseDelay)
    {
        var pg = Store.Pages[page];
        var sh = new Sheet { PageId = pg.Id, Dir = dir, Tx = dir * SLIDE };
        sh.El.RenderTransform = sh.Slide;
        int i = 0;
        foreach (var d in ItemsFor(page)) { var it = MakeItem(d, null); it.Delay = baseDelay + i++ * stagger; sh.Items.Add(it); sh.El.Children.Add(it.El); }
        Measure(sh); stage.Children.Add(sh.El); sheets.Add(sh);
        return sh;
    }

    void DestroySheet(Sheet sh) { foreach (var it in sh.Items) it.W?.Dispose(); stage.Children.Remove(sh.El); }

    void RebuildCurrent()
    {
        var sh = CurSheet(); if (sh == null) return;
        var old = sh.Items.ToDictionary(x => x.Data.Id);
        foreach (var it in sh.Items) it.W?.Dispose();
        sh.El.Children.Clear(); sh.Items.Clear();
        foreach (var d in ItemsFor(Store.St.Current))
        {
            old.TryGetValue(d.Id, out var prev);
            var it = MakeItem(d, prev);
            if (prev == null) { it.Intro = 0; it.Delay = 0; it.S = 1; }
            sh.Items.Add(it); sh.El.Children.Add(it.El);
        }
        Measure(sh); BuildPager(); Wake();
    }

    void RebuildAll()
    {
        var sh = CurSheet();
        foreach (var s in sheets.Where(x => x != sh).ToList()) { DestroySheet(s); sheets.Remove(s); }
        if (sh == null) { BuildSheet(Store.St.Current, 0, 0.06, 0.25); } else RebuildCurrent();
        BuildPager(); Wake();
    }

    /* ============================================================ physics */
    static void Spring(ref double p, ref double v, double target, double k, double c, double dt)
    {
        int n = Math.Max(1, (int)Math.Ceiling(dt / 0.008)); double d = dt / n;
        for (int i = 0; i < n; i++) { v += (-k * (p - target) - c * v) * d; p += v * d; }
    }
    static double Bell(double x) => x >= 1 ? 0 : Math.Pow((1 + Math.Cos(Math.PI * x)) / 2, 1.15);

    void Wake() { idle = 0; if (!attached) { attached = true; last = sw.Elapsed.TotalSeconds; CompositionTarget.Rendering += OnRender; } }
    void OnRender(object s, EventArgs e)
    {
        double now = sw.Elapsed.TotalSeconds, dt = Math.Clamp(now - last, 0.001, 0.033); last = now;
        if (!Frame(dt)) { attached = false; CompositionTarget.Rendering -= OnRender; }
    }

    (double x, double y) DockOrigin()
    {
        switch (Pos)
        {
            case "top": return ((OW - L) / 2, EDGE);
            case "left": return (EDGE, (OH - L) / 2);
            case "right": return (OW - EDGE - Cross, (OH - L) / 2);
            case "floating": return (Store.S.FloatX * OW - L / 2, Store.S.FloatY * OH - Cross / 2);
            default: return ((OW - L) / 2, OH - EDGE - Cross);
        }
    }

    bool Frame(double dt)
    {
        var cur = CurSheet(); if (cur == null || OW < 10) return false;
        double energy = 0;

        if (Store.S.AutoHide && Pos != "floating")
        {
            // hover is detected by polling the real cursor position (autohidePoll), not WPF's IsMouseOver: the dock is
            // a non-activated background window, and while mostly off-screen only a thin sliver can ever be hovered.
            bool hovering = hoverPolled || root.IsMouseOver || dragActive || swipe != null || SuppressAutoHide;
            hideCountdown = hovering ? 0.5 : Math.Max(0, hideCountdown - dt);
            double target = hideCountdown <= 0 ? 1 : 0;
            Spring(ref hide, ref hideV, target, 220, 26, dt);
            energy += Math.Abs(hideV) * 3 + Math.Abs(hide - target) * 3;
        }
        else if (hide > 0.0004 || Math.Abs(hideV) > 0.0004) { Spring(ref hide, ref hideV, 0, 220, 26, dt); energy += Math.Abs(hideV) * 3 + hide * 3; }
        else { hide = 0; hideV = 0; }
        ApplyWindowPlacement();

        double targetL = Math.Max(cur.RestLen, B * 3) + P * 2;
        if (L <= 0) L = targetL * 0.6;
        Spring(ref L, ref Lv, targetL, 260, 30, dt); energy += Math.Abs(Lv) + Math.Abs(L - targetL);

        var (dx, dy) = DockOrigin();
        var m = Mouse.GetPosition(root);
        double mMain = H ? m.X - dx : m.Y - dy, mCross = H ? m.Y - dy : m.X - dx;
        bool inZone = root.IsMouseOver && mMain > -R * 0.5 - 12 && mMain < L + R * 0.5 + 12 &&
                      (AnchorEnd ? mCross > -Room - 4 && mCross < Cross + 6 : mCross > -6 && mCross < Cross + Room + 4);
        active = inZone && !dragActive && swipe == null;
        if (active != keysRegistered) SetArrowHotkeys(active);

        double glassL = 0, glassR = L;
        SheetItem hit = null;
        for (int si = sheets.Count - 1; si >= 0; si--)
        {
            var sh = sheets[si]; bool isCur = sh == cur;
            double trackStart = (L - sh.RestLen) / 2, u = mMain - trackStart;
            bool magnify = isCur && active;

            double tTx = swipe != null && isCur ? (sh.Drag ?? 0) : sh.Leaving ? -sh.Dir * SLIDE : 0;
            Spring(ref sh.Tx, ref sh.Txv, tTx, 300, 30, dt);
            if (H) { sh.Slide.X = sh.Tx; sh.Slide.Y = 0; } else { sh.Slide.X = 0; sh.Slide.Y = sh.Tx; }
            sh.El.Opacity = Math.Clamp(1 - Math.Abs(sh.Tx) / SLIDE, 0, 1);
            sh.El.IsHitTestVisible = !sh.Leaving;
            energy += Math.Abs(sh.Txv) + Math.Abs(sh.Tx - tTx);

            int n = sh.Items.Count; var Lx = new double[n]; double cursor = 0;
            for (int i = 0; i < n; i++)
            {
                var it = sh.Items[i];
                double center = it.Rs + it.Rl / 2, g = magnify ? Bell(Math.Abs(u - center) / R) : 0, tgt = 1 + (Zoom - 1) * g;
                Spring(ref it.S, ref it.Sv, tgt, 420, 34, dt);
                if (it.Delay > 0) it.Delay -= dt; else Spring(ref it.Intro, ref it.Iv, 1, 240, 21, dt);
                Spring(ref it.P, ref it.Pv, sh.DropIndex == i ? B + Gap : 0, 420, 40, dt);
                energy += Math.Abs(it.Sv) * 0.02 + Math.Abs(it.S - tgt) + Math.Abs(it.Iv) * 0.02 + Math.Abs(1 - it.Intro) + Math.Abs(it.Pv) * 0.01 + (sh.DropIndex == i ? 0 : Math.Abs(it.P) * 0.01);
                cursor += it.P; Lx[i] = cursor;
                cursor += it.Rl * it.S * (0.6 + 0.4 * Math.Min(1, it.Intro)) + Gap;
            }
            Spring(ref sh.Tail, ref sh.Tailv, sh.DropIndex == n ? B + Gap : 0, 420, 40, dt);
            cursor += sh.Tail; energy += Math.Abs(sh.Tailv) * 0.01 + (sh.DropIndex == n ? 0 : Math.Abs(sh.Tail) * 0.01);

            double offT = 0;
            if (magnify && n > 0)
            {
                int k = n - 1;
                for (int i = 0; i < n; i++) if (u < sh.Items[i].Rs + sh.Items[i].Rl + Gap / 2) { k = i; break; }
                var ik = sh.Items[k]; double frac = Math.Clamp((u - ik.Rs) / (ik.Rl + Gap), 0, 1), eff = ik.S * (0.6 + 0.4 * Math.Min(1, ik.Intro));
                offT = u - (Lx[k] + frac * (ik.Rl * eff + Gap));
            }
            sh.Off += (offT - sh.Off) * (1 - Math.Exp(-dt * (magnify ? 45 : 14)));
            energy += Math.Abs(offT - sh.Off) * 0.2;

            for (int i = 0; i < n; i++)
            {
                var it = sh.Items[i];
                double intro = Math.Clamp(it.Intro, 0, 1), eff = it.S * (0.6 + 0.4 * it.Intro);
                double main = (H ? dx : dy) + trackStart + sh.Off + Lx[i];
                double hCross = B * eff, cross = (AnchorEnd ? RowStart + B - hCross : RowStart) + (1 - it.Intro) * 14 * (AnchorEnd ? 1 : -1);
                double x = H ? main : dx + cross, y = H ? dy + cross : main;
                it.Tr.X = x; it.Tr.Y = y; it.Sc.ScaleX = it.Sc.ScaleY = eff; it.El.Opacity = intro;
                it.Rect = new Rect(x, y, it.Rl * eff, B * eff);
                if (isCur && active && it.Data.Type != "sep" && it.Rect.Contains(m) && (hit == null || eff > hit.S)) hit = it;
            }
            if (isCur)
            {
                if (n > 0) { glassL = Math.Min(0, trackStart + sh.Off + Lx[0] - P); glassR = Math.Max(L, trackStart + sh.Off + cursor - Gap + P); }
                emptyHint.Opacity = n == 0 ? 1 : 0;
            }
            if (sh.Leaving && Math.Abs(sh.Tx - tTx) < 1 && Math.Abs(sh.Txv) < 5) { DestroySheet(sh); sheets.RemoveAt(si); }
        }
        if (active) SetHover(hit); else if (hovered != null && !dragActive) SetHover(null);

        // glass, zone, pager, hint follow the strip
        double gx = H ? dx + glassL : dx, gy = H ? dy : dy + glassL, gw = H ? glassR - glassL : Cross, gh = H ? Cross : glassR - glassL;
        Canvas.SetLeft(glass, gx); Canvas.SetTop(glass, gy); glass.Width = Math.Max(1, gw); glass.Height = Math.Max(1, gh);
        if (rain.Visibility == Visibility.Visible) FitFx(rain, gx, gy, glass.Width, glass.Height);
        if (fire.Visibility == Visibility.Visible) FitFx(fire, gx, gy, glass.Width, glass.Height);
        double zx = H ? gx - 12 : (AnchorEnd ? dx - Room - 4 : dx - 6), zy = H ? (AnchorEnd ? dy - Room - 4 : dy - 6) : gy - 12;
        double zw = H ? gw + 24 : Cross + Room + 10, zh = H ? Cross + Room + 10 : gh + 24;
        Canvas.SetLeft(zone, zx); Canvas.SetTop(zone, zy); zone.Width = Math.Max(1, zw); zone.Height = Math.Max(1, zh);
        Canvas.SetLeft(emptyHint, dx + L / 2 - 60); Canvas.SetTop(emptyHint, dy + Cross / 2 - 8);
        PlacePager(dx, dy, glassL, glassR);
        PlaceArrows(dx, dy, glassL, glassR);
        if (Pos == "floating") { Canvas.SetLeft(grip, dx + L / 2 - 22); Canvas.SetTop(grip, dy - 12); }
        if (hovered != null && tip.Opacity > 0.01) PlaceTip(hovered);

        bd.SetRect((int)Math.Round(oxPx + gx * scale), (int)Math.Round(oyPx + gy * scale), (int)Math.Round(gw * scale), (int)Math.Round(gh * scale), (int)Math.Round(Radius * scale));

        if (energy < 0.03 && !active && !dragActive && swipe == null && sheets.Count == 1) return ++idle <= 4;
        idle = 0; return true;
    }

    /// <summary>Round chevron button at each end of the dock that steps to the previous / next page (wraps around).</summary>
    Border MakeArrow(RotateTransform rot, Action go)
    {
        const double S = 28;
        var chev = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 11,5 L 5,11 L 11,17"), Stroke = Theme.Ink, StrokeThickness = 2.2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            Width = 16, Height = 22, Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = HorizontalAlignment.Center == 0 ? VerticalAlignment.Center : VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = rot, IsHitTestVisible = false
        };
        var b = new Border { Width = S, Height = S, CornerRadius = new CornerRadius(S / 2), Background = Theme.PanelBg, BorderBrush = Theme.Line, BorderThickness = new Thickness(1), Child = chev, Opacity = 0.6, Cursor = Cursors.Hand };
        b.MouseEnter += (_, _) => { b.Opacity = 1; b.Background = Theme.AccBg; };
        b.MouseLeave += (_, _) => { b.Opacity = 0.6; b.Background = Theme.PanelBg; };
        b.MouseLeftButtonDown += (_, e) => e.Handled = true;
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; go(); };
        return b;
    }

    void PlaceArrows(double dx, double dy, double gl, double gr)
    {
        const double S = 28, gap = 8; double mid = RowStart + B / 2 - S / 2;
        double a = H ? dx + gl - gap - S : dy + gl - gap - S, z = H ? dx + gr + gap : dy + gr + gap;
        double lim = H ? OW : OH;
        a = Math.Max(2, a); z = Math.Min(lim - S - 2, z);
        if (H) { Canvas.SetLeft(arrowPrev, a); Canvas.SetTop(arrowPrev, dy + mid); Canvas.SetLeft(arrowNext, z); Canvas.SetTop(arrowNext, dy + mid); }
        else { Canvas.SetTop(arrowPrev, a); Canvas.SetLeft(arrowPrev, dx + mid); Canvas.SetTop(arrowNext, z); Canvas.SetLeft(arrowNext, dx + mid); }
    }

    void PlacePager(double dx, double dy, double gl, double gr)
    {
        pager.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var ps = pager.DesiredSize; double mid = (gl + gr) / 2;
        if (H) { Canvas.SetLeft(pager, dx + mid - ps.Width / 2); Canvas.SetTop(pager, AnchorEnd ? dy + Cross - E - ps.Height - 1 : dy + E + 1); }
        else { Canvas.SetTop(pager, dy + mid - ps.Height / 2); Canvas.SetLeft(pager, AnchorEnd ? dx + Cross - E - ps.Width - 1 : dx + E + 1); }
    }

    /* ============================================================ hover, labels, previews */
    void SetHover(SheetItem it)
    {
        if (hovered == it) return;
        hovered = it; previewTimer.Stop();
        if (it == null || dragActive) { tip.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120))); previewHide.Stop(); previewHide.Start(); return; }
        tipText.Text = it.Data.Name;
        tip.BeginAnimation(OpacityProperty, new DoubleAnimation(Store.S.Labels ? 1 : 0, TimeSpan.FromMilliseconds(140)));
        previewHide.Stop();
        if (Store.S.Previews && MatchWindows(it.Data).Count > 0) previewTimer.Start(); else pv.HidePreview();
    }

    void PlaceTip(SheetItem it)
    {
        tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); var s = tip.DesiredSize; var r = it.Rect; double g = 10, x, y;
        if (H) { x = r.X + r.Width / 2 - s.Width / 2; y = AnchorEnd ? r.Top - g - s.Height : r.Bottom + g; }
        else { y = r.Y + r.Height / 2 - s.Height / 2; x = AnchorEnd ? r.Left - g - s.Width : r.Right + g; }
        Canvas.SetLeft(tip, x); Canvas.SetTop(tip, y);
    }

    static List<Native.WinInfo> MatchWindows(Item d)
    {
        if (d.Type != "app" || string.IsNullOrEmpty(d.Exe)) return new List<Native.WinInfo>();
        string name = System.IO.Path.GetFileName(d.Exe);
        return Live.Windows.Where(w => w.Exe != null && string.Equals(System.IO.Path.GetFileName(w.Exe), name, StringComparison.OrdinalIgnoreCase)).ToList();
    }
    /* ---- running apps section (page 1): apps with open windows that aren't pinned there, after a divider ---- */
    static readonly HashSet<string> HostExes = new(StringComparer.OrdinalIgnoreCase)
    { "ApplicationFrameHost.exe", "TextInputHost.exe", "ShellExperienceHost.exe", "SearchHost.exe", "StartMenuExperienceHost.exe", "LockApp.exe", "SystemSettings.exe" };
    static readonly Dictionary<string, string> DescCache = new(StringComparer.OrdinalIgnoreCase);
    static readonly List<string> runOrder = new();      // first-seen order keeps the section from reshuffling as focus changes

    static string Describe(string exe)
    {
        if (DescCache.TryGetValue(exe, out var d)) return d;
        try { var v = FileVersionInfo.GetVersionInfo(exe); d = !string.IsNullOrWhiteSpace(v.FileDescription) ? v.FileDescription.Trim() : null; } catch { }
        return DescCache[exe] = d ?? System.IO.Path.GetFileNameWithoutExtension(exe);
    }

    List<Item> ItemsFor(int page)
    {
        var list = new List<Item>(Store.Pages[page].Items);
        if (page != 0 || !Store.S.ShowRunning) return list;
        var pinned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in list) if (i.Type == "app") { if (i.Exe == null) Icons.For(i); if (!string.IsNullOrEmpty(i.Exe)) pinned.Add(System.IO.Path.GetFileName(i.Exe)); }
        var byExe = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in Live.Windows)
        {
            if (w.Exe == null) continue; string n = System.IO.Path.GetFileName(w.Exe);
            if (HostExes.Contains(n) || pinned.Contains(n) || byExe.ContainsKey(n)) continue;
            byExe[n] = w.Exe;
        }
        runOrder.RemoveAll(k => !byExe.ContainsKey(k));
        foreach (var k in byExe.Keys) if (!runOrder.Contains(k)) runOrder.Add(k);
        if (runOrder.Count == 0) return list;
        // only show as many running apps as fit along the dock (arrows and margins need ~180 px)
        double avail = (H ? OW : OH) - 180, used = 0;
        foreach (var i in list) used += (i.Type == "live" && !H ? 1 : Math.Max(1, i.Span)) * (B + Gap);
        int fit = avail > 0 ? Math.Max(0, (int)((avail - used - 14) / (B + Gap))) : int.MaxValue;
        if (fit == 0) return list;
        list.Add(new Item { Id = "run:sep", Type = "sep", Name = "" });
        foreach (var k in runOrder.Take(fit))
            list.Add(new Item { Id = "run:" + k.ToLowerInvariant(), Type = "app", Name = Describe(byExe[k]), Path = byExe[k], Exe = byExe[k], Running = true });
        return list;
    }

    static bool IsRunning(Item d) => MatchWindows(d).Count > 0;

    void RefreshRunning()
    {
        if (Store.St.Current == 0 && Store.S.ShowRunning && CurSheet() is { } cs0 && string.Join(",", ItemsFor(0).Select(i => i.Id)) != string.Join(",", cs0.Items.Select(i => i.Data.Id))) { RebuildCurrent(); return; }
        foreach (var sh in sheets) foreach (var it in sh.Items)
            if (it.Data.Type == "app") { bool on = IsRunning(it.Data); double to = on ? 1 : 0; if (Math.Abs(it.Run.Opacity - to) > 0.01) it.Run.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(300))); }
    }

    void ShowPreview(SheetItem it)
    {
        var wins = MatchWindows(it.Data).Take(3).ToList(); if (wins.Count == 0) return;
        var r = it.Rect;
        var itemPx = new Rect(oxPx + r.X * scale, oyPx + r.Y * scale, r.Width * scale, r.Height * scale);
        pv.ShowFor(it.Data.Name, wins, itemPx, Pos, scale);
    }

    /* ============================================================ launching */
    void Launch(SheetItem it, bool forceNew = false)
    {
        var d = it.Data;
        var bounce = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(720) };
        double a = -B * 0.3 * (AnchorEnd ? 1 : -1);
        foreach (var (t, v) in new[] { (0.0, 0.0), (0.2, a), (0.4, 0.0), (0.6, a * 0.5), (0.8, 0.0) }) bounce.KeyFrames.Add(new EasingDoubleKeyFrame(v, KeyTime.FromPercent(t + 0.0001 > 1 ? 1 : t), new SineEase()));
        it.Face.RenderTransform = new TranslateTransform();
        ((TranslateTransform)it.Face.RenderTransform).BeginAnimation(H ? TranslateTransform.YProperty : TranslateTransform.XProperty, bounce);

        var wins = forceNew ? new List<Native.WinInfo>() : MatchWindows(d);
        if (wins.Count > 0) { Native.Focus(wins[0].Hwnd); pv.HidePreview(); return; }
        try
        {
            var psi = new ProcessStartInfo(d.Path) { UseShellExecute = true, Arguments = d.Args ?? "" };
            Process.Start(psi);
        }
        catch (Exception ex) { Toast.Show("Could not launch " + d.Name + ": " + ex.Message); }
    }

    /* ============================================================ pages */
    public void GoTo(int i, int dir = 0, bool silent = false)
    {
        i = ((i % N) + N) % N;
        var cur = CurSheet();
        if (cur != null && i == Store.St.Current) return;
        if (dir == 0) dir = i > Store.St.Current ? 1 : -1;
        if (cur != null) { cur.Leaving = true; cur.Dir = dir; }
        Store.SetCurrent(i);
        SetHover(null); pv.HidePreview();
        BuildSheet(i, dir, 0.03, 0.04);
        BuildPager(); Wake();
        if (!silent) ShowChip();
    }
    public void Next() => GoTo(Store.St.Current + 1, 1);
    public void Prev() => GoTo(Store.St.Current - 1, -1);

    void ShowChip()
    {
        chipText.Text = $"{Store.CurrentPage.Name}   {Store.St.Current + 1} / {N}";
        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); var s = chip.DesiredSize;
        var (dx, dy) = DockOrigin(); double off = Room + 40, x, y;
        if (H) { x = dx + L / 2 - s.Width / 2; y = AnchorEnd ? dy - off - s.Height / 2 : dy + Cross + off - s.Height / 2; }
        else { y = dy + L / 2 - s.Height / 2; x = AnchorEnd ? dx - off - s.Width - 20 : dx + Cross + off + 20; }
        Canvas.SetLeft(chip, Math.Clamp(x, 4, Math.Max(4, OW - s.Width - 4))); Canvas.SetTop(chip, Math.Clamp(y, 4, Math.Max(4, OH - s.Height - 4)));
        chip.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
        chipTimer.Stop(); chipTimer.Start();
    }

    void BuildPager()
    {
        pager.Children.Clear(); pager.Orientation = H ? Orientation.Horizontal : Orientation.Vertical;
        for (int i = 0; i < N; i++)
        {
            int idx = i; bool act = i == Store.St.Current, filled = Store.Pages[i].Items.Count > 0;
            var dot = new Border
            {
                Width = H ? (act ? 18 : 6) : 6, Height = H ? 6 : (act ? 18 : 6), CornerRadius = new CornerRadius(3), Margin = H ? new Thickness(3, 4, 3, 4) : new Thickness(4, 3, 4, 3),
                Background = act ? Theme.Acc : Store.S.Preset is "fire" or "matrix" ? Theme.Ink : Theme.Ink3, Opacity = act ? 1 : filled ? 0.85 : 0.5, Cursor = Cursors.Hand, ToolTip = $"{i + 1} · {Store.Pages[i].Name}", AllowDrop = true
            };
            dot.MouseLeftButtonUp += (_, _) => GoTo(idx);
            dot.DragOver += (_, e) => { e.Effects = DragDropEffects.Move; e.Handled = true; if (dotTarget != idx) { dotTarget = idx; dotTimer.Stop(); dotTimer.Start(); } };
            dot.DragLeave += (_, _) => { dotTimer.Stop(); dotTarget = -1; };
            dot.Drop += (_, e) =>
            {
                dotTimer.Stop(); dotTarget = -1; e.Handled = true;
                if (e.Data.GetDataPresent("dockitem")) { Store.MoveItem((string)e.Data.GetData("dockitem"), idx); Toast.Show($"Moved to “{Store.Pages[idx].Name}”"); }
                else HandleExternalDrop(e, idx, -1);
            };
            pager.Children.Add(dot);
        }
        var gear = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M12,8.5 A3.5,3.5 0 1 0 12,15.5 A3.5,3.5 0 1 0 12,8.5 Z M20.5,13.1 V10.9 L18.5,10.3 A6.6,6.6 0 0 0 17.8,8.6 L18.8,6.8 L17.2,5.2 L15.4,6.2 A6.6,6.6 0 0 0 13.7,5.5 L13.1,3.5 H10.9 L10.3,5.5 A6.6,6.6 0 0 0 8.6,6.2 L6.8,5.2 L5.2,6.8 L6.2,8.6 A6.6,6.6 0 0 0 5.5,10.3 L3.5,10.9 V13.1 L5.5,13.7 C5.7,14.3 5.9,14.9 6.2,15.4 L5.2,17.2 L6.8,18.8 L8.6,17.8 C9.1,18.1 9.7,18.3 10.3,18.5 L10.9,20.5 H13.1 L13.7,18.5 C14.3,18.3 14.9,18.1 15.4,17.8 L17.2,18.8 L18.8,17.2 L17.8,15.4 C18.1,14.9 18.3,14.3 18.5,13.7 Z"),
            Stroke = Theme.Ink2, StrokeThickness = 1.5, Width = 14, Height = 14, Stretch = Stretch.Uniform, Margin = H ? new Thickness(6, 0, 0, 0) : new Thickness(0, 6, 0, 0), Cursor = Cursors.Hand, ToolTip = "Layout & settings", Fill = Brushes.Transparent
        };
        gear.MouseLeftButtonUp += (_, _) => OpenPanel?.Invoke();
        pager.Children.Add(gear);
        PlacePager(DockOrigin().x, DockOrigin().y, 0, L); Wake();
    }

    /* ============================================================ input wiring */
    void Wire()
    {
        dotTimer.Tick += (_, _) => { dotTimer.Stop(); if (dotTarget >= 0) GoTo(dotTarget); };
        root.AllowDrop = true;
        root.MouseEnter += (_, _) => Wake();
        root.MouseMove += (_, _) => Wake();
        root.MouseLeave += (_, _) => { if (swipe == null) Wake(); };

        root.PreviewMouseWheel += (_, e) =>
        {
            if (!Store.S.Wheel) return;
            e.Handled = true; var now = DateTime.Now;
            if ((now - lastWheel).TotalMilliseconds > 220) wheelAcc = 0;
            lastWheel = now; if ((now - lastNav).TotalMilliseconds < 320) return;
            wheelAcc += -e.Delta;
            if (Math.Abs(wheelAcc) >= 100) { if (wheelAcc > 0) Next(); else Prev(); lastNav = now; wheelAcc = 0; }
        };

        // swipe from dock chrome (mouse) or anywhere (touch)
        root.PreviewMouseLeftButtonDown += (s, e) =>
        {
            if (e.OriginalSource is DependencyObject o && (ItemFrom(o) != null && e.StylusDevice == null || IsChild(o, pager) || IsChild(o, grip) || IsChild(o, arrowPrev) || IsChild(o, arrowNext))) return;
            swipe = (0, e.GetPosition(root), e.GetPosition(root)); swiped = false; root.CaptureMouse(); Wake();
        };
        root.PreviewMouseMove += (s, e) =>
        {
            if (swipe == null) return;
            var p = e.GetPosition(root); double d = H ? p.X - swipe.Value.start.X : p.Y - swipe.Value.start.Y;
            if (Math.Abs(d) > 6) swiped = true;
            var sh = CurSheet(); if (sh != null && swiped) sh.Drag = Math.Clamp(d * 0.55, -SLIDE * 0.9, SLIDE * 0.9);
        };
        root.PreviewMouseLeftButtonUp += (s, e) =>
        {
            if (swipe == null) return;
            var p = e.GetPosition(root); double d = H ? p.X - swipe.Value.start.X : p.Y - swipe.Value.start.Y;
            swipe = null; root.ReleaseMouseCapture(); var sh = CurSheet(); if (sh != null) sh.Drag = null;
            if (Math.Abs(d) > 50) { if (d < 0) Next(); else Prev(); }
            Wake();
            if (swiped) Dispatcher.BeginInvoke(new Action(() => swiped = false), DispatcherPriority.Background);
        };

        root.MouseRightButtonUp += (_, e) => { e.Handled = true; ShowMenu(e.OriginalSource as DependencyObject, e.GetPosition(root)); };

        // drag & drop (reorder, pin files / links, move across pages)
        root.DragOver += (_, e) =>
        {
            if (!Accepts(e.Data)) { e.Effects = DragDropEffects.None; return; }
            e.Effects = e.Data.GetDataPresent("dockitem") ? DragDropEffects.Move : DragDropEffects.Copy; e.Handled = true;
            dragActive = true; var sh = CurSheet(); if (sh != null) sh.DropIndex = IndexAt(e.GetPosition(root)); Wake();
        };
        root.DragLeave += (_, _) => { var sh = CurSheet(); if (sh != null) sh.DropIndex = -1; if (dragId == null) dragActive = false; Wake(); };
        root.Drop += (_, e) =>
        {
            var sh = CurSheet(); int idx = sh.DropIndex >= 0 ? sh.DropIndex : IndexAt(e.GetPosition(root)); sh.DropIndex = -1; dropped = true; if (dragId == null) dragActive = false;
            if (e.Data.GetDataPresent("dockitem")) Store.MoveItem((string)e.Data.GetData("dockitem"), Store.St.Current, idx);
            else HandleExternalDrop(e, Store.St.Current, idx);
            Wake();
        };

        // floating drag handle
        Point gOff = default; bool gDrag = false;
        grip.MouseLeftButtonDown += (_, e) => { gDrag = true; var (dx, dy) = DockOrigin(); var p = e.GetPosition(root); gOff = new Point(p.X - (dx + L / 2), p.Y - (dy + Cross / 2)); grip.CaptureMouse(); e.Handled = true; };
        grip.MouseMove += (_, e) =>
        {
            if (!gDrag) return; var p = e.GetPosition(root);
            double fx = (p.X - gOff.X) / OW, fy = (p.Y - gOff.Y) / OH, mx = (L / 2 + 12) / OW, my = (Cross / 2 + Room + 12) / OH;
            Store.S.FloatX = Math.Clamp(fx, mx, 1 - mx); Store.S.FloatY = Math.Clamp(fy, my, 1 - (Cross / 2 + 12) / OH); Wake();
        };
        grip.MouseLeftButtonUp += (_, _) => { if (gDrag) { gDrag = false; grip.ReleaseMouseCapture(); Store.Save(); } };
    }

    static bool IsChild(DependencyObject o, DependencyObject parent)
    {
        while (o != null) { if (o == parent) return true; o = o is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(o) : LogicalTreeHelper.GetParent(o); }
        return false;
    }
    static SheetItem ItemFrom(DependencyObject o)
    {
        while (o != null) { if (o is FrameworkElement fe && fe.Tag is SheetItem si) return si; o = o is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(o) : LogicalTreeHelper.GetParent(o); }
        return null;
    }

    void ItemMouseMove(object s, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || downItem == null || dragActive || downItem.Data.Running || downItem.Data.Type == "sep") return;
        var p = e.GetPosition(root);
        if (Math.Abs(p.X - downPt.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - downPt.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var it = downItem; downItem = null;
        dragId = it.Data.Id; dragActive = true; dropped = false; it.El.Opacity = 0.3; SetHover(null); pv.HidePreview(); Wake();
        var data = new DataObject("dockitem", it.Data.Id);
        var eff = DragDrop.DoDragDrop(it.El, data, DragDropEffects.Move);
        var id = dragId; dragId = null; dragActive = false; it.El.Opacity = 1;
        var sh = CurSheet(); if (sh != null) sh.DropIndex = -1;
        if (!dropped && eff == DragDropEffects.None && id != null)
        {
            Native.GetCursorPos(out var cp);
            var pt = PointFromScreen(new Point(cp.X, cp.Y)); // device px -> DIPs of this window
            var (dx, dy) = DockOrigin();
            double m = 50, mm = H ? pt.X - dx : pt.Y - dy, cc = H ? pt.Y - dy : pt.X - dx;
            if (mm < -m || mm > L + m || cc < -Room - m || cc > Cross + m) RemoveWithUndo(id);
        }
        Wake();
    }

    static bool Accepts(IDataObject d) => d.GetDataPresent("dockitem") || d.GetDataPresent(DataFormats.FileDrop) || (d.GetDataPresent(DataFormats.UnicodeText) && ((string)d.GetData(DataFormats.UnicodeText) ?? "").StartsWith("http"));

    int IndexAt(Point p)
    {
        var sh = CurSheet(); var (dx, dy) = DockOrigin();
        double u = (H ? p.X - dx : p.Y - dy) - (L - sh.RestLen) / 2;
        for (int i = 0; i < sh.Items.Count; i++) if (u < sh.Items[i].Rs + sh.Items[i].Rl / 2) return i;
        return sh.Items.Count;
    }

    void HandleExternalDrop(DragEventArgs e, int page, int index)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop)) { AddPaths((string[])e.Data.GetData(DataFormats.FileDrop), page, index); return; }
        string url = ((string)e.Data.GetData(DataFormats.UnicodeText) ?? "").Trim();
        if (url.StartsWith("http")) { string host = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host.Replace("www.", "") : url; Store.AddItem(page, Store.App(host, url, "🔗"), index); }
    }

    public void AddPaths(IEnumerable<string> paths, int page, int index = -1)
    {
        foreach (var p in paths)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(p); if (string.IsNullOrEmpty(name)) name = p;
            Store.AddItem(page, Store.App(name, p), index); if (index >= 0) index++;
        }
    }

    /* ============================================================ menus */
    void ShowMenu(DependencyObject src, Point p)
    {
        var it = src != null ? ItemFrom(src) : null;
        var scr = PointToScreen(p); var pos = new Point(scr.X / scale, scr.Y / scale);
        var list = new List<PopupMenu.Entry>();
        if (it == null)
        {
            list.Add(new() { Label = "Layout & pages…", Fn = () => OpenPanel?.Invoke() });
            list.Add(new() { Label = "Move dock to", Sub = new[] { ("bottom", "Bottom edge"), ("top", "Top edge"), ("left", "Left edge"), ("right", "Right edge"), ("floating", "Floating") }
                .Select(o => new PopupMenu.Entry { Label = (Store.S.Position == o.Item1 ? "✓  " : "     ") + o.Item2, Fn = () => Store.Set("position", x => x.Position = o.Item1) }).ToList() });
            list.Add(new() { Label = "Hide dock  (Ctrl+Alt+Space)", Fn = ToggleVisible });
            list.Add(new() { Sep = true });
            list.Add(new() { Label = "Clear this page", Danger = true, Fn = () => { int pg = Store.St.Current; var items = Store.Pages[pg].Items.ToList(); Store.ClearPage(pg); Toast.Show("Page cleared", "Undo", () => { foreach (var x in items) Store.AddItem(pg, x); }); } });
            list.Add(new() { Label = "Exit Dock DX", Danger = true, Fn = () => Application.Current.Shutdown() });
        }
        else
        {
            var d = it.Data;
            if (d.Type == "sep") return;
            if (d.Running)
            {
                list.Add(new() { Label = "Switch to window", Fn = () => Launch(it) });
                list.Add(new() { Label = "Keep in dock", Fn = () => { Store.AddItem(0, new Item { Type = "app", Name = d.Name, Path = d.Path, Exe = d.Exe }); Toast.Show($"Pinned “{d.Name}”"); } });
                if (File.Exists(d.Exe)) list.Add(new() { Label = "Show in folder", Fn = () => Process.Start("explorer.exe", "/select,\"" + d.Exe + "\"") });
                PopupMenu.Show(pos, list); return;
            }
            if (d.Type == "app") list.Add(new() { Label = "Open", Fn = () => Launch(it, true) });
            if (d.Type == "app" && !string.IsNullOrEmpty(d.Exe) && File.Exists(d.Exe)) list.Add(new() { Label = "Show in folder", Fn = () => Process.Start("explorer.exe", "/select,\"" + d.Exe + "\"") });
            list.Add(new() { Label = "Rename…", Fn = () => { var n = InputBox.Ask("Rename shortcut", d.Name); if (!string.IsNullOrWhiteSpace(n)) { d.Name = n.Trim(); Store.Touch(Store.St.Current); } } });
            list.Add(new() { Label = "Move to page", Sub = Store.Pages.Select((pg, i) => new PopupMenu.Entry { Label = $"{i + 1}   {pg.Name}", Fn = () => { Store.MoveItem(d.Id, i); GoTo(i); } }).ToList() });
            list.Add(new() { Sep = true });
            list.Add(new() { Label = "Remove from dock", Danger = true, Fn = () => RemoveWithUndo(d.Id) });
        }
        PopupMenu.Show(pos, list);
    }

    void RemoveWithUndo(string id)
    {
        var f = Store.Find(id); if (f == null) return;
        var (pg, idx, item) = f.Value;
        Store.RemoveItem(id);
        Toast.Show($"Removed “{item.Name}”", "Undo", () => Store.AddItem(pg, item, idx));
    }
}
