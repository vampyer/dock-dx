using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DockDX;

/// <summary>Live App micro-widgets rendered natively inside the dock.</summary>
public abstract class Widget
{
    public FrameworkElement Root;
    public virtual void Dispose() { }

    public static Widget Create(Item it, double b, double w) => w < b * 1.5 ? new CompactWidget(it.Live, b) : it.Live switch
    {
        "media" => new MediaWidget(b, w),
        "perf" => new PerfWidget(b, w),
        _ => new ClockWidget(b, w)
    };

    protected static TextBlock Text(string s, double size, Brush fg, FontWeight? weight = null) => new()
    {
        Text = s, FontSize = size, Foreground = fg, FontWeight = weight ?? FontWeights.Normal, FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
        TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap
    };
    protected static Geometry G(string d) { var g = Geometry.Parse(d); g.Freeze(); return g; }
}

/// <summary>Square, icon-sized version of the widgets, used on left/right docks where a wide widget would not fit.</summary>
sealed class CompactWidget : Widget
{
    readonly string kind; readonly Grid g;
    // media
    readonly Border art; readonly TextBlock note; readonly System.Windows.Shapes.Path badge; readonly Border badgeBg;
    // perf / clock
    readonly TextBlock l1, l2, l3;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    bool colon;

    public CompactWidget(string kind, double b)
    {
        this.kind = kind; double fs = b * 0.19;
        g = new Grid { Width = b, Height = b, Background = Brushes.Transparent };
        if (kind == "media")
        {
            art = new Border { Margin = new Thickness(3), CornerRadius = new CornerRadius(b * 0.18), Background = new LinearGradientBrush(Color.FromRgb(70, 76, 106), Color.FromRgb(35, 39, 58), 45) };
            note = new TextBlock { Text = "♪", FontSize = b * 0.5, Foreground = new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            art.Child = note; g.Children.Add(art);
            badge = new System.Windows.Shapes.Path { Fill = Brushes.White, Stretch = Stretch.Uniform, Width = b * 0.2, Height = b * 0.2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            badgeBg = new Border { Width = b * 0.44, Height = b * 0.44, CornerRadius = new CornerRadius(b * 0.22), Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), Child = badge, Opacity = 0, IsHitTestVisible = false };
            g.Children.Add(badgeBg);
            g.MouseEnter += (_, _) => badgeBg.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
            g.MouseLeave += (_, _) => badgeBg.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)));
            g.MouseLeftButtonUp += (_, e) => { e.Handled = true; Live.MediaCommand("play"); };
            g.MouseRightButtonUp += (_, e) => { e.Handled = true; Live.MediaCommand("next"); };
            g.ToolTip = "Click: play / pause    Right-click: next";
            Live.MediaChanged += Media; Media();
        }
        else
        {
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            l1 = Text("", kind == "clock" ? fs * 1.55 : fs, Theme.Ink, FontWeights.SemiBold); l2 = Text("", fs, Theme.Ink2); l3 = Text("", fs, Theme.Ink2);
            foreach (var t in new[] { l1, l2, l3 }) { t.HorizontalAlignment = HorizontalAlignment.Center; sp.Children.Add(t); }
            g.Children.Add(sp);
            if (kind == "perf") { Live.StatsChanged += Perf; Perf(); }
            else { timer.Tick += (_, _) => Clock(); timer.Start(); Live.WeatherChanged += Clock; Clock(); }
        }
        Root = g;
    }

    void Media()
    {
        var m = Live.Media;
        if (m.Art != null) { art.Background = new ImageBrush(m.Art) { Stretch = Stretch.UniformToFill }; note.Visibility = Visibility.Collapsed; }
        else { art.Background = new LinearGradientBrush(Color.FromRgb(70, 76, 106), Color.FromRgb(35, 39, 58), 45); note.Visibility = Visibility.Visible; }
        badge.Data = G(m.Active && m.Playing ? "M6,4 H10 V20 H6 Z M14,4 H18 V20 H14 Z" : "M7,4.5 V19.5 L20,12 Z");
        g.ToolTip = (m.Active ? m.Title + (string.IsNullOrEmpty(m.Artist) ? "" : " — " + m.Artist) + "\n" : "") + "Click: play / pause    Right-click: next";
    }

    void Perf()
    {
        l1.Text = "CPU " + Math.Round(Live.Cpu); l2.Text = "GPU " + (Live.Gpu < 0 ? "—" : Math.Round(Live.Gpu).ToString()); l3.Text = "RAM " + Math.Round(Live.Ram);
    }

    void Clock()
    {
        var d = DateTime.Now; colon = !colon;
        l1.Text = d.ToString("HH") + (colon ? ":" : " ") + d.ToString("mm");
        var w = Live.Weather;
        l2.Text = d.ToString("ddd d");
        l3.Text = w?.TempC == null ? "" : Math.Round(Store.S.Units == "f" ? w.TempC.Value * 1.8 + 32 : w.TempC.Value) + "°";
    }

    public override void Dispose() { timer.Stop(); Live.MediaChanged -= Media; Live.StatsChanged -= Perf; Live.WeatherChanged -= Clock; }
}

sealed class MediaWidget : Widget
{
    const string Prev = "M6,5 H8 V19 H6 Z M20,5 V19 L9,12 Z", Next = "M16,5 H18 V19 H16 Z M4,5 V19 L15,12 Z",
                 Play = "M7,4.5 V19.5 L20,12 Z", Pause = "M6,4 H10 V20 H6 Z M14,4 H18 V20 H14 Z";
    readonly Border art; readonly TextBlock title, artist, noteGlyph; readonly Border barFill; readonly Path playIcon; readonly Grid ctl, meta;

    public MediaWidget(double b, double w)
    {
        double fs = b * 0.2;
        var g = new Grid { Width = w, Height = b, Background = Brushes.Transparent };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        art = new Border
        {
            Width = b - 10, Height = b - 10, Margin = new Thickness(5), CornerRadius = new CornerRadius(b * 0.15),
            Background = new LinearGradientBrush(Color.FromRgb(70, 76, 106), Color.FromRgb(35, 39, 58), 45)
        };
        noteGlyph = new TextBlock { Text = "♪", FontSize = b * 0.45, Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        art.Child = noteGlyph;
        g.Children.Add(art);

        title = Text("Nothing playing", fs * 1.1, Theme.Ink, FontWeights.SemiBold);
        artist = Text("Start a player", fs * 0.95, Theme.Ink2);
        barFill = new Border { Height = Math.Max(2, fs * 0.28), HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2), Background = Theme.Acc, Width = 0 };
        var track = new Border { Height = barFill.Height, CornerRadius = new CornerRadius(2), Background = Theme.Fill2, Margin = new Thickness(0, fs * 0.5, 8, 0), Child = barFill };
        track.SizeChanged += (_, e) => trackW = e.NewSize.Width;
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 4, 0) };
        sp.Children.Add(title); sp.Children.Add(artist); sp.Children.Add(track);
        meta = new Grid(); meta.Children.Add(sp); Grid.SetColumn(meta, 1); g.Children.Add(meta);

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Btn(Prev, fs * 2.1, () => Live.MediaCommand("prev"), false, out _));
        row.Children.Add(Btn(Play, fs * 2.7, () => Live.MediaCommand("play"), true, out playIcon));
        row.Children.Add(Btn(Next, fs * 2.1, () => Live.MediaCommand("next"), false, out _));
        ctl = new Grid { Opacity = 0, IsHitTestVisible = false };
        ctl.Children.Add(row); Grid.SetColumn(ctl, 1); g.Children.Add(ctl);

        g.MouseEnter += (_, _) => Hover(true);
        g.MouseLeave += (_, _) => Hover(false);
        Root = g;
        Live.MediaChanged += Refresh; Refresh();
    }

    double trackW = 60;

    void Hover(bool on)
    {
        ctl.IsHitTestVisible = on;
        ctl.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(180)));
        meta.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(on ? 0.1 : 1, TimeSpan.FromMilliseconds(180)));
    }

    FrameworkElement Btn(string path, double size, Action click, bool primary, out Path icon)
    {
        var p = new Path { Data = G(path), Fill = primary ? Theme.OnAcc : Theme.Ink, Stretch = Stretch.Uniform, Width = size * 0.42, Height = size * 0.42, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        var bd = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Margin = new Thickness(size * 0.12, 0, size * 0.12, 0), Background = primary ? Theme.Acc : Theme.Fill2, Child = p, Cursor = System.Windows.Input.Cursors.Hand };
        bd.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        bd.MouseLeftButtonDown += (_, e) => e.Handled = true;
        bd.MouseEnter += (_, _) => bd.RenderTransform = new ScaleTransform(1.12, 1.12, size / 2, size / 2);
        bd.MouseLeave += (_, _) => bd.RenderTransform = null;
        icon = p; return bd;
    }

    void Refresh()
    {
        var m = Live.Media;
        title.Text = m.Active ? (string.IsNullOrEmpty(m.Title) ? "Unknown" : m.Title) : "Nothing playing";
        artist.Text = m.Active ? m.Artist : "Start a player";
        if (m.Art != null) { art.Background = new ImageBrush(m.Art) { Stretch = Stretch.UniformToFill }; noteGlyph.Visibility = Visibility.Collapsed; }
        else { art.Background = new LinearGradientBrush(Color.FromRgb(70, 76, 106), Color.FromRgb(35, 39, 58), 45); noteGlyph.Visibility = Visibility.Visible; }
        playIcon.Data = G(m.Active && m.Playing ? Pause : Play);
        barFill.Width = m.Active && m.Dur > 0 ? Math.Max(0, Math.Min(1, m.Pos / m.Dur)) * trackW : 0;
    }

    public override void Dispose() => Live.MediaChanged -= Refresh;
}

sealed class PerfWidget : Widget
{
    readonly Canvas canvas = new() { ClipToBounds = true };
    readonly TextBlock cpu, gpu, ram;
    readonly Polyline lc = Line(Color.FromRgb(92, 200, 255)), lg = Line(Color.FromRgb(181, 140, 255)), lr = Line(Color.FromRgb(93, 255, 176));

    static Polyline Line(Color c) => new() { Stroke = new SolidColorBrush(c), StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round };

    public PerfWidget(double b, double w)
    {
        double fs = b * 0.18;
        var g = new Grid { Width = w, Height = b, Background = Brushes.Transparent };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var gb = new Border { Margin = new Thickness(6), CornerRadius = new CornerRadius(5), Background = Theme.Fill, Child = canvas };
        g.Children.Add(gb);
        canvas.Children.Add(lr); canvas.Children.Add(lg); canvas.Children.Add(lc);
        canvas.SizeChanged += (_, _) => Draw();

        var rows = new Grid { Margin = new Thickness(0, 5, 8, 5), MinWidth = fs * 5.4 };
        for (int i = 0; i < 3; i++) rows.RowDefinitions.Add(new RowDefinition());
        cpu = Row(rows, 0, "CPU", Color.FromRgb(92, 200, 255), fs); gpu = Row(rows, 1, "GPU", Color.FromRgb(181, 140, 255), fs); ram = Row(rows, 2, "RAM", Color.FromRgb(93, 255, 176), fs);
        Grid.SetColumn(rows, 1); g.Children.Add(rows);
        Root = g; Live.StatsChanged += Draw; Draw();
    }

    static TextBlock Row(Grid host, int r, string name, Color dot, double fs)
    {
        var row = new Grid { VerticalAlignment = VerticalAlignment.Center };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var e = new Ellipse { Width = fs * 0.5, Height = fs * 0.5, Fill = new SolidColorBrush(dot), Margin = new Thickness(0, 0, fs * 0.4, 0) };
        var n = Text(name, fs * 0.95, Theme.Ink2);
        var v = Text("0%", fs, Theme.Ink, FontWeights.SemiBold); v.HorizontalAlignment = HorizontalAlignment.Right; v.Margin = new Thickness(fs * 0.5, 0, 0, 0);
        Grid.SetColumn(n, 0); Grid.SetColumn(e, 0);
        var np = new StackPanel { Orientation = Orientation.Horizontal }; np.Children.Add(e); np.Children.Add(n);
        row.Children.Add(np); Grid.SetColumn(v, 2); row.Children.Add(v);
        Grid.SetRow(row, r); host.Children.Add(row); return v;
    }

    void Plot(Polyline pl, System.Collections.Generic.List<double> data, bool show)
    {
        pl.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        double W = canvas.ActualWidth, H = canvas.ActualHeight; if (W < 2 || H < 2) return;
        var pts = new PointCollection();
        for (int i = 0; i < data.Count; i++) pts.Add(new Point(i * W / (Live.Hist - 1), H - 3 - data[i] / 100.0 * (H - 6)));
        pl.Points = pts;
    }

    void Draw()
    {
        Plot(lr, Live.HRam, true); Plot(lg, Live.HGpu, Live.Gpu >= 0); Plot(lc, Live.HCpu, true);
        cpu.Text = Math.Round(Live.Cpu) + "%"; ram.Text = Math.Round(Live.Ram) + "%"; gpu.Text = Live.Gpu < 0 ? "—" : Math.Round(Live.Gpu) + "%";
    }

    public override void Dispose() => Live.StatsChanged -= Draw;
}

sealed class ClockWidget : Widget
{
    readonly TextBlock time, date, temp, icon;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly TranslateTransform bob = new();
    bool colon;

    public ClockWidget(double b, double w)
    {
        double fs = b * 0.2;
        var g = new Grid { Width = w, Height = b, Background = Brushes.Transparent };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        time = Text("--:--", fs * 1.95, Theme.Ink, FontWeights.SemiBold); date = Text("", fs * 0.88, Theme.Ink2);
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(b * 0.16, 0, 0, 0) };
        sp.Children.Add(time); sp.Children.Add(date); g.Children.Add(sp);

        icon = new TextBlock { FontSize = fs * 2.0, FontFamily = new FontFamily("Segoe UI Emoji"), HorizontalAlignment = HorizontalAlignment.Center, RenderTransform = bob };
        temp = Text("--°", fs * 1.05, Theme.Ink, FontWeights.SemiBold); temp.HorizontalAlignment = HorizontalAlignment.Center;
        var wx = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, b * 0.16, 0) };
        wx.Children.Add(icon); wx.Children.Add(temp); Grid.SetColumn(wx, 1); g.Children.Add(wx);
        bob.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-2, 2, TimeSpan.FromSeconds(2.2)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });

        Root = g;
        timer.Tick += (_, _) => Tick(); timer.Start(); Tick();
        Live.WeatherChanged += Wx; Wx();
    }

    void Tick()
    {
        var d = DateTime.Now; colon = !colon;
        time.Text = d.ToString("HH") + (colon ? ":" : " ") + d.ToString("mm");
        date.Text = d.ToString("ddd, d MMM");
    }

    void Wx()
    {
        var w = Live.Weather; if (w == null) return;
        icon.Text = w.Kind switch
        {
            "sun" => w.Night ? "🌙" : "☀️", "partly" => w.Night ? "☁️" : "⛅", "fog" => "🌫️", "rain" => "🌧️", "snow" => "❄️", "storm" => "⛈️", _ => "☁️"
        };
        if (w.TempC == null) temp.Text = "--°";
        else temp.Text = Math.Round(Store.S.Units == "f" ? w.TempC.Value * 1.8 + 32 : w.TempC.Value) + "°";
    }

    public override void Dispose() { timer.Stop(); Live.WeatherChanged -= Wx; }
}
