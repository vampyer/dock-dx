using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace DockDX;

/// <summary>Layout manager / settings window.</summary>
public sealed class PanelWindow : Window
{
    const string StylesXaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style x:Key='DockBtn' TargetType='Button'>
    <Setter Property='Foreground' Value='{DynamicResource Ink}'/><Setter Property='Background' Value='{DynamicResource Fill2}'/>
    <Setter Property='Padding' Value='14,8'/><Setter Property='Cursor' Value='Hand'/><Setter Property='FontSize' Value='13'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
      <Border x:Name='b' Background='{TemplateBinding Background}' CornerRadius='10' Padding='{TemplateBinding Padding}'>
        <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='{DynamicResource AccBg}'/></Trigger>
        <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.35'/></Trigger>
      </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='DockSlider' TargetType='Slider'>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Slider'>
      <Grid Height='24' Background='Transparent'>
        <Border Height='5' CornerRadius='3' Background='{DynamicResource Fill2}' VerticalAlignment='Center'/>
        <Track x:Name='PART_Track'>
          <Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'>
            <Border Height='5' CornerRadius='3' Background='{DynamicResource Acc}' VerticalAlignment='Center'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
          <Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'>
            <Border Background='Transparent' Height='24'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
          <Track.Thumb><Thumb Width='18' Height='18'><Thumb.Template><ControlTemplate TargetType='Thumb'>
            <Ellipse Width='18' Height='18' Fill='White' Stroke='#40000000'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
        </Track>
      </Grid></ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='DockToggle' TargetType='CheckBox'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'>
      <Border x:Name='track' Width='40' Height='24' CornerRadius='12' Background='{DynamicResource Fill2}'>
        <Ellipse x:Name='knob' Width='18' Height='18' Fill='White' HorizontalAlignment='Left' Margin='3,0,0,0'/></Border>
      <ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'>
        <Setter TargetName='track' Property='Background' Value='{DynamicResource Acc}'/>
        <Setter TargetName='knob' Property='HorizontalAlignment' Value='Right'/><Setter TargetName='knob' Property='Margin' Value='0,0,3,0'/></Trigger></ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='DockText' TargetType='TextBox'>
    <Setter Property='Foreground' Value='{DynamicResource Ink}'/><Setter Property='CaretBrush' Value='{DynamicResource Ink}'/>
    <Setter Property='Background' Value='{DynamicResource Fill}'/><Setter Property='FontSize' Value='13'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TextBox'>
      <Border x:Name='b' Background='{TemplateBinding Background}' CornerRadius='9' BorderThickness='1' BorderBrush='Transparent' Padding='8,6'>
        <ScrollViewer x:Name='PART_ContentHost'/></Border>
      <ControlTemplate.Triggers><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='b' Property='BorderBrush' Value='{DynamicResource Acc}'/></Trigger></ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
</ResourceDictionary>";

    public static void InstallStyles() => Application.Current.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(StylesXaml));

    readonly DockWindow dock; readonly StackPanel body = new(); readonly ScrollViewer scroll; readonly StackPanel tabs = new();
    string tab = Environment.GetEnvironmentVariable("DOCKDX_TAB") ?? "dock", confirmKey; int target = -1; IntPtr hwnd;
    readonly DispatcherTimer confirmTimer = new() { Interval = TimeSpan.FromSeconds(2.6) };

    public PanelWindow(DockWindow d)
    {
        dock = d;
        Title = "Dock DX"; Width = Math.Min(900, SystemParameters.WorkArea.Width * 0.92); Height = Math.Min(640, SystemParameters.WorkArea.Height * 0.88); WindowStartupLocation = WindowStartupLocation.CenterScreen; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; Topmost = true; Background = Brushes.Transparent; Foreground = Theme.Ink; FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        var side = new Border { Background = Theme.Fill, Child = tabs, Padding = new Thickness(12, 20, 12, 12) };
        grid.Children.Add(side);
        scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(28, 26, 22, 24) };
        Grid.SetColumn(scroll, 1); grid.Children.Add(scroll);
        var close = new Button { Content = "✕", Style = (Style)Application.Current.Resources["DockBtn"], Width = 32, Height = 32, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 12, 12, 0) };
        close.Click += (_, _) => Hide();
        Grid.SetColumn(close, 1); grid.Children.Add(close);
        var drag = new Border { Height = 34, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 56, 0) };
        Grid.SetColumn(drag, 1); drag.MouseLeftButtonDown += (_, _) => DragMove(); grid.Children.Add(drag);
        Content = new Border { Child = grid, BorderBrush = Theme.Line, BorderThickness = new Thickness(1) };

        SourceInitialized += (_, _) => { hwnd = new WindowInteropHelper(this).Handle; HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent; Native.RoundCorners(hwnd); Tint(); };
        Closing += (s, e) => { e.Cancel = true; Hide(); };
        confirmTimer.Tick += (_, _) => { confirmTimer.Stop(); confirmKey = null; Render(); };
        Store.LayoutChanged += Refresh; Store.PagesChanged += _ => Refresh(); Store.CurrentChanged += Refresh; Store.Reset += Render;
        Store.SettingChanged += k => { if (IsVisible) Tint(); };
        BuildTabs();

        if (Environment.GetEnvironmentVariable("DOCKDX_DEBUG") == "1")   // debug aid: create %TEMP%\dockdx.psnap to get %TEMP%\dockdx_panel.png
        {
            string req = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dockdx.psnap"), png = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dockdx_panel.png");
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            t.Tick += (_, _) =>
            {
                if (!System.IO.File.Exists(req) || !IsVisible) return; System.IO.File.Delete(req);
                var fe = (FrameworkElement)Content; double sc = VisualTreeHelper.GetDpi(this).DpiScaleX;
                var dv = new DrawingVisual(); using (var dc = dv.RenderOpen()) dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(24, 27, 42)), null, new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(fe.ActualWidth * sc), (int)(fe.ActualHeight * sc), 96 * sc, 96 * sc, PixelFormats.Pbgra32); rtb.Render(dv); rtb.Render(fe);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                using var fs = System.IO.File.Create(png); enc.Save(fs);
            };
            t.Start();
        }
    }

    void Tint() { if (hwnd != IntPtr.Zero) Native.SetAccent(hwnd, 4, Theme.AcrylicColor(Theme.Dark, Store.S.Hue, 0.18, Theme.Dark ? 0.82 : 0.86)); }
    void Refresh() { if (IsVisible && (tab == "pages" || tab == "add")) Render(); }

    public void Open(string t = null)
    {
        if (t != null) tab = t; Render(); BuildTabs();
        if (!IsVisible) Show(); Activate(); Tint();
    }

    void BuildTabs()
    {
        tabs.Children.Clear();
        var brand = new TextBlock { Margin = new Thickness(10, 4, 0, 18), FontSize = 18 };
        brand.Inlines.Add(new System.Windows.Documents.Run("Dock ") { FontWeight = FontWeights.Bold, Foreground = Theme.Acc }); brand.Inlines.Add(new System.Windows.Documents.Run("DX") { Foreground = Theme.Ink });
        tabs.Children.Add(brand);
        foreach (var (k, name, ico) in new[] { ("dock", "Dock", "▭"), ("pages", "Pages", "▦"), ("look", "Appearance", "◐"), ("add", "Add", "＋"), ("about", "About", "ⓘ") })
        {
            bool on = tab == k;
            var t = new Border { Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 0, 3), Background = on ? Theme.AccBg : Brushes.Transparent, Cursor = Cursors.Hand };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = ico, Width = 24, Foreground = on ? Theme.Ink : Theme.Ink2 }); sp.Children.Add(new TextBlock { Text = name, Foreground = on ? Theme.Ink : Theme.Ink2 });
            t.Child = sp; var key = k;
            t.MouseLeftButtonUp += (_, _) => { tab = key; BuildTabs(); Render(); };
            tabs.Children.Add(t);
        }
    }

    /* ------------------------------------------------------------ builders */
    Style St(string k) => (Style)Application.Current.Resources[k];
    TextBlock T(string s, double size = 13, Brush fg = null, FontWeight? w = null) => new() { Text = s, FontSize = size, Foreground = fg ?? Theme.Ink, FontWeight = w ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap };

    void Head(string h, string sub) { body.Children.Add(T(h, 22, null, FontWeights.SemiBold)); body.Children.Add(new TextBlock { Text = sub, FontSize = 13, Foreground = Theme.Ink2, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 18), MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left }); }

    StackPanel Group() { var g = new StackPanel(); body.Children.Add(new Border { Child = g, Background = Theme.Fill, CornerRadius = new CornerRadius(16), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 14) }); return g; }

    Button Btn(string text, Action a, bool primary = false, bool danger = false)
    {
        var b = new Button { Content = text, Style = St("DockBtn"), Margin = new Thickness(0, 0, 8, 0) };
        if (primary) { b.Background = Theme.Acc; b.Foreground = Theme.OnAcc; b.FontWeight = FontWeights.SemiBold; }
        if (danger) b.Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 122));
        b.Click += (_, _) => a(); return b;
    }

    void SliderRow(Panel host, string label, double min, double max, Func<double> get, Action<double> set, Func<double, string> fmt)
    {
        var g = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        var val = T(fmt(get()), 12.5, Theme.Ink2); val.TextAlignment = TextAlignment.Right; val.VerticalAlignment = VerticalAlignment.Center;
        var s = new Slider { Minimum = min, Maximum = max, Value = get(), Style = St("DockSlider"), Margin = new Thickness(0, 0, 10, 0) };
        s.ValueChanged += (_, e) => { val.Text = fmt(e.NewValue); set(e.NewValue); };
        var l = T(label); l.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(s, 1); Grid.SetColumn(val, 2); g.Children.Add(l); g.Children.Add(s); g.Children.Add(val); host.Children.Add(g);
    }

    void Seg(Panel host, string label, string[] values, string[] names, string current, Action<string> pick)
    {
        var row = new DockPanel { Margin = new Thickness(0, 5, 0, 5) };
        var l = T(label); l.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(l, Dock.Left); row.Children.Add(l);
        var seg = new Border { Background = Theme.Fill2, CornerRadius = new CornerRadius(11), Padding = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Right };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < values.Length; i++)
        {
            string v = values[i]; bool on = v == current;
            var b = new Border { Padding = new Thickness(12, 6, 12, 6), CornerRadius = new CornerRadius(8), Background = on ? Theme.Acc : Brushes.Transparent, Cursor = Cursors.Hand, Child = T(names[i], 12.5, on ? Theme.OnAcc : Theme.Ink2, on ? FontWeights.SemiBold : FontWeights.Normal) };
            b.MouseLeftButtonUp += (_, _) => { pick(v); Render(); }; sp.Children.Add(b);
        }
        seg.Child = sp; row.Children.Add(seg); host.Children.Add(row);
    }

    void Toggle(Panel host, string label, bool on, Action<bool> set)
    {
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6), LastChildFill = false };
        var l = T(label); l.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(l);
        var c = new CheckBox { Style = St("DockToggle"), IsChecked = on }; DockPanel.SetDock(c, Dock.Right);
        c.Click += (_, _) => set(c.IsChecked == true); row.Children.Add(c); host.Children.Add(row);
    }

    void Arm(string key, Action a) { if (confirmKey == key) { confirmTimer.Stop(); confirmKey = null; a(); return; } confirmKey = key; confirmTimer.Stop(); confirmTimer.Start(); Render(); }

    void Render()
    {
        if (body == null) return;
        double y = scroll.VerticalOffset; body.Children.Clear();
        switch (tab) { case "look": LookTab(); break; case "dock": DockTab(); break; case "add": AddTab(); break; case "about": AboutTab(); break; default: PagesTab(); break; }
        scroll.ScrollToVerticalOffset(y);
    }

    /* ------------------------------------------------------------ pages */
    void PagesTab()
    {
        Head("Pages", "Ten pages, each with its own shortcuts and live widgets. Rename them, reorder with the arrows, or clear them.");
        for (int i = 0; i < Store.PageCount; i++)
        {
            int idx = i; var p = Store.Pages[i];
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var w in new[] { 28.0, 0, 70, 0, 34, 34, 0 }) row.ColumnDefinitions.Add(new ColumnDefinition { Width = w == 0 ? GridLength.Auto : new GridLength(w) });
            row.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            var card = new Border { Child = row, Background = idx == Store.St.Current ? Theme.AccBg : Theme.Fill, CornerRadius = new CornerRadius(13), Padding = new Thickness(10, 6, 8, 6), Margin = new Thickness(0, 0, 0, 0) };
            var n = T((i + 1).ToString(), 12, Theme.Ink3); n.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(n);
            var name = new TextBox { Text = p.Name, MaxLength = 24, Style = St("DockText"), Background = Brushes.Transparent, Margin = new Thickness(0, 0, 8, 0) };
            name.LostFocus += (_, _) => { if (name.Text != p.Name) Store.RenamePage(idx, name.Text); };
            name.KeyDown += (_, e) => { if (e.Key == Key.Enter) Keyboard.ClearFocus(); };
            Grid.SetColumn(name, 1); row.Children.Add(name);
            var cnt = T(p.Items.Count == 0 ? "empty" : p.Items.Count + (p.Items.Count == 1 ? " item" : " items"), 12, Theme.Ink3); cnt.VerticalAlignment = VerticalAlignment.Center; cnt.TextAlignment = TextAlignment.Right; cnt.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(cnt, 2); row.Children.Add(cnt);
            var btns = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(btns, 3); row.Children.Add(btns);
            var show = SmallBtn("Show", () => dock.GoTo(idx)); btns.Children.Add(show);
            var up = SmallBtn("↑", () => Store.MovePage(idx, idx - 1)); up.IsEnabled = idx > 0; Grid.SetColumn(up, 4); row.Children.Add(up);
            var dn = SmallBtn("↓", () => Store.MovePage(idx, idx + 1)); dn.IsEnabled = idx < Store.PageCount - 1; Grid.SetColumn(dn, 5); row.Children.Add(dn);
            string key = "clear" + p.Id; bool armed = confirmKey == key;
            var clr = SmallBtn(armed ? "Confirm?" : "Clear", () => Arm(key, () => Store.ClearPage(idx))); clr.IsEnabled = p.Items.Count > 0;
            if (armed) { clr.Background = new SolidColorBrush(Color.FromRgb(255, 77, 99)); clr.Foreground = Brushes.White; } else clr.Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 122));
            Grid.SetColumn(clr, 6); row.Children.Add(clr);
            body.Children.Add(card);
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        bool all = confirmKey == "all", rs = confirmKey == "reset";
        var ba = Btn(all ? "Click again to clear every page" : "Clear all pages", () => Arm("all", () => { Store.ClearAll(); Toast.Show("All pages cleared"); }), false, true); if (all) { ba.Background = new SolidColorBrush(Color.FromRgb(255, 77, 99)); ba.Foreground = Brushes.White; }
        var br = Btn(rs ? "Click again to reset everything" : "Reset to defaults", () => Arm("reset", () => Store.ResetAll())); if (rs) { br.Background = new SolidColorBrush(Color.FromRgb(255, 77, 99)); br.Foreground = Brushes.White; }
        actions.Children.Add(ba); actions.Children.Add(br); body.Children.Add(actions);
    }

    Button SmallBtn(string text, Action a) { var b = new Button { Content = text, Style = St("DockBtn"), Padding = new Thickness(8, 4, 8, 4), FontSize = 12, Margin = new Thickness(0, 0, 4, 0), MinWidth = 28 }; b.Click += (_, _) => a(); return b; }

    /* ------------------------------------------------------------ dock (position + behaviour) */
    void DockTab()
    {
        Head("Dock", "Choose which edge of the screen the dock sits on, or let it float. Changes apply instantly.");
        var s = Store.S; PositionPicker(s);
        var g3 = Group();
        SliderRow(g3, "Icon size", 36, 96, () => s.IconSize, v => Store.Set("iconSize", x => x.IconSize = Math.Round(v)), v => Math.Round(v) + " px");
        SliderRow(g3, "Magnification", 1, 2.6, () => s.Zoom, v => Store.Set("zoom", x => x.Zoom = Math.Round(v, 2)), v => v.ToString("0.00") + "×");
        SliderRow(g3, "Influence radius", 1.2, 5, () => s.Spread, v => Store.Set("spread", x => x.Spread = Math.Round(v, 1)), v => v.ToString("0.0") + " icons");
        var g4 = Group();
        Toggle(g4, "Hover labels", s.Labels, v => Store.Set("labels", x => x.Labels = v));
        Toggle(g4, "Live window previews on hover", s.Previews, v => Store.Set("previews", x => x.Previews = v));
        Toggle(g4, "Show running apps on page 1", s.ShowRunning, v => Store.Set("running", x => x.ShowRunning = v));
        Toggle(g4, "Mouse wheel changes page", s.Wheel, v => Store.Set("wheel", x => x.Wheel = v));
        Toggle(g4, "Reserve screen space (windows won't cover the dock)", s.Reserve, v => Store.Set("reserve", x => x.Reserve = v));
        Toggle(g4, "Auto-hide (slide away, show on hover)", s.AutoHide, v => Store.Set("autohide", x => x.AutoHide = v));
        Toggle(g4, "Start with Windows", s.Autostart, v => Store.Set("autostart", x => x.Autostart = v));
    }

    /* ------------------------------------------------------------ appearance */
    void LookTab()
    {
        Head("Appearance", "Themes, glass and tint update live. Blur is Windows acrylic, so its radius is fixed by the system; opacity and tint control how frosted it looks.");
        var s = Store.S; PresetRow(s);
        var g1 = Group();
        Seg(g1, "Theme", new[] { "dark", "light", "auto" }, new[] { "Dark", "Light", "System" }, s.Theme, v => Store.Set("theme", x => { x.Theme = v; x.Preset = ""; }));
        Seg(g1, "Temperature", new[] { "c", "f" }, new[] { "°C", "°F" }, s.Units, v => Store.Set("units", x => x.Units = v));
        var g2 = Group();
        SliderRow(g2, "Tint colour", 0, 360, () => s.Hue, v => Store.Set("hue", x => { x.Hue = Math.Round(v); x.Preset = ""; }), v => Math.Round(v) + "°");
        SliderRow(g2, "Tint strength", 0, 1, () => s.Tint, v => Store.Set("tint", x => { x.Tint = v; x.Preset = ""; }), v => Math.Round(v * 100) + "%");
        SliderRow(g2, "Glass opacity", 0.05, 0.95, () => s.Opacity, v => Store.Set("opacity", x => { x.Opacity = v; x.Preset = ""; }), v => Math.Round(v * 100) + "%");
    }
    /// <summary>Miniature screen: click an edge to dock there, or the centre pill for a free-floating dock.</summary>
    void PositionPicker(Settings s)
    {
        body.Children.Add(T("Dock position", 12.5, Theme.Ink2));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 18) };
        var screen = new Grid { Width = 270, Height = 158 };
        screen.Children.Add(new Border { CornerRadius = new CornerRadius(12), Background = Theme.Fill, BorderBrush = Theme.Line, BorderThickness = new Thickness(1.5) });
        var label = T("", 12.5, Theme.Ink2); label.VerticalAlignment = VerticalAlignment.Center; label.Margin = new Thickness(22, 0, 0, 0);

        void Zone(string id, string name, double w, double h, HorizontalAlignment ha, VerticalAlignment va, Thickness m)
        {
            bool on = s.Position == id;
            var b = new Border
            {
                Width = w, Height = h, HorizontalAlignment = ha, VerticalAlignment = va, Margin = m, CornerRadius = new CornerRadius(Math.Min(w, h) / 2),
                Background = on ? Theme.Acc : Theme.Fill2, Cursor = Cursors.Hand, ToolTip = name + (id == "floating" ? "  (drag the handle to move it)" : " edge")
            };
            b.MouseEnter += (_, _) => { if (s.Position != id) b.Background = Theme.AccBg; label.Text = name; };
            b.MouseLeave += (_, _) => { if (s.Position != id) b.Background = Theme.Fill2; label.Text = Names(s.Position); };
            b.MouseLeftButtonUp += (_, _) => { Store.Set("position", x => x.Position = id); Render(); };
            screen.Children.Add(b);
        }
        Zone("top", "Top edge", 120, 16, HorizontalAlignment.Center, VerticalAlignment.Top, new Thickness(0, 9, 0, 0));
        Zone("bottom", "Bottom edge", 120, 16, HorizontalAlignment.Center, VerticalAlignment.Bottom, new Thickness(0, 0, 0, 9));
        Zone("left", "Left edge", 16, 70, HorizontalAlignment.Left, VerticalAlignment.Center, new Thickness(9, 0, 0, 0));
        Zone("right", "Right edge", 16, 70, HorizontalAlignment.Right, VerticalAlignment.Center, new Thickness(0, 0, 9, 0));
        Zone("floating", "Floating", 84, 22, HorizontalAlignment.Center, VerticalAlignment.Center, new Thickness(0, 24, 0, 0));
        label.Text = Names(s.Position);
        row.Children.Add(screen); row.Children.Add(label); body.Children.Add(row);
    }
    static string Names(string id) => id switch { "top" => "Top edge", "left" => "Left edge", "right" => "Right edge", "floating" => "Floating", _ => "Bottom edge" };

    void PresetRow(Settings s)
    {
        body.Children.Add(T("Themes", 12.5, Theme.Ink2));
        var wrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 16) };
        foreach (var p in Theme.Presets)
        {
            bool on = s.Preset == p.Id, light = p.Mode == "light";
            var sw = new Border
            {
                Width = 88, Height = 54, CornerRadius = new CornerRadius(10),
                Background = new LinearGradientBrush(Theme.Hsl(p.Hue, 0.8, light ? 0.78 : 0.42), Theme.Hsl(p.Hue + 45, 0.8, light ? 0.9 : 0.24), 60)
            };
            var pill = new Border { Height = 16, Margin = new Thickness(10, 0, 10, 8), VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(light ? Color.FromArgb(190, 255, 255, 255) : Color.FromArgb(150, 12, 14, 24)), BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), BorderThickness = new Thickness(1) };
            sw.Child = pill;
            var name = T(p.Name, 11.5, on ? Theme.Ink : Theme.Ink2, on ? FontWeights.SemiBold : FontWeights.Normal); name.Margin = new Thickness(2, 5, 0, 0);
            var sp = new StackPanel(); sp.Children.Add(sw); sp.Children.Add(name);
            var card = new Border { Child = sp, Padding = new Thickness(6), Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(14), Cursor = Cursors.Hand, Background = on ? Theme.AccBg : Brushes.Transparent, BorderThickness = new Thickness(1), BorderBrush = on ? Theme.Acc : Brushes.Transparent };
            var preset = p;
            card.MouseLeftButtonUp += (_, _) => { Store.Set("preset", x => { x.Theme = preset.Mode; x.Hue = preset.Hue; x.Tint = preset.Tint; x.Opacity = preset.Opacity; x.Preset = preset.Id; }); Render(); };
            wrap.Children.Add(card);
        }
        body.Children.Add(wrap);
    }

    /* ------------------------------------------------------------ add */
    void AddTab()
    {
        if (target < 0) target = Store.St.Current;
        Head("Add to dock", "Widgets and shortcuts go to the page you choose. You can also drag files or links straight onto the dock.");
        body.Children.Add(T("Target page", 12.5, Theme.Ink2));
        var chips = new WrapPanel { Margin = new Thickness(0, 6, 0, 16) };
        for (int i = 0; i < Store.PageCount; i++)
        {
            int idx = i; bool on = idx == target;
            var c = new Border { Padding = new Thickness(11, 6, 11, 6), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(10), Background = on ? Theme.Acc : Theme.Fill2, Cursor = Cursors.Hand, ToolTip = Store.Pages[i].Name, Child = T((i + 1) + "  " + Store.Pages[i].Name, 12, on ? Theme.OnAcc : Theme.Ink, on ? FontWeights.SemiBold : FontWeights.Normal) };
            c.MouseLeftButtonUp += (_, _) => { target = idx; Render(); }; chips.Children.Add(c);
        }
        body.Children.Add(chips);

        var cards = new WrapPanel(); body.Children.Add(cards);
        foreach (var (k, name, desc) in new[] { ("media", "Media Controller", "Live album art and playback controls for Spotify, browsers and more."), ("perf", "Performance Monitor", "Real-time CPU, GPU and RAM micro-graph."), ("clock", "Clock & Weather", "Local time with animated current conditions.") })
        {
            var sp = new StackPanel { Width = 190 };
            sp.Children.Add(T(name, 14, null, FontWeights.SemiBold)); sp.Children.Add(new TextBlock { Text = desc, FontSize = 12, Foreground = Theme.Ink2, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10), MinHeight = 48 });
            string kind = k, nm = name; sp.Children.Add(Btn("Add widget", () => { Store.AddItem(target, Store.LiveItem(kind)); Toast.Show($"{nm} added to “{Store.Pages[target].Name}”"); }));
            cards.Children.Add(new Border { Child = sp, Background = Theme.Fill, CornerRadius = new CornerRadius(16), Padding = new Thickness(16), Margin = new Thickness(0, 0, 12, 12) });
        }

        var g = Group(); g.Children.Add(T("Add an application, file or link", 13, Theme.Ink2, FontWeights.SemiBold));
        var nmBox = new TextBox { Style = St("DockText"), Margin = new Thickness(0, 10, 0, 0) }; var pathBox = new TextBox { Style = St("DockText"), Margin = new Thickness(0, 8, 0, 0) }; var glyph = new TextBox { Style = St("DockText"), Margin = new Thickness(0, 8, 0, 0), MaxLength = 2, Width = 70, HorizontalAlignment = HorizontalAlignment.Left };
        g.Children.Add(Labeled("Name", nmBox)); g.Children.Add(Labeled("Path or URL", pathBox)); g.Children.Add(Labeled("Emoji (optional)", glyph));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        row.Children.Add(Btn("Browse…", () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Choose programs, shortcuts or files", Filter = "Programs & shortcuts|*.exe;*.lnk;*.bat;*.cmd;*.url|All files|*.*" };
            if (dlg.ShowDialog(this) == true) { dock.AddPaths(dlg.FileNames, target); Toast.Show($"Added {dlg.FileNames.Length} item(s)"); }
        }));
        row.Children.Add(Btn("Add shortcut", () =>
        {
            string p = pathBox.Text.Trim(); if (p.Length == 0) { pathBox.Focus(); return; }
            string n = nmBox.Text.Trim(); if (n.Length == 0) n = System.IO.Path.GetFileNameWithoutExtension(p); if (n.Length == 0) n = p;
            Store.AddItem(target, Store.App(n, p, glyph.Text.Trim().Length > 0 ? glyph.Text.Trim() : (p.StartsWith("http") ? "🔗" : null)));
            nmBox.Text = pathBox.Text = glyph.Text = ""; Toast.Show($"Added “{n}”");
        }, true));
        g.Children.Add(row);
    }

    FrameworkElement Labeled(string label, FrameworkElement box) { var sp = new StackPanel(); sp.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Foreground = Theme.Ink3, Margin = new Thickness(0, 8, 0, 0) }); box.Margin = new Thickness(0, 3, 0, 0); sp.Children.Add(box); return sp; }

    /* ------------------------------------------------------------ about */
    void AboutTab()
    {
        Head("Shortcuts & about", "Native C# / WPF dock with DWM acrylic blur, live DWM window thumbnails and AppBar integration.");
        int n = 0;
        foreach (var (k, d) in new[] { ("← → ↑ ↓", "Previous / next page (while the pointer is over the dock)"), ("Ctrl + Alt + ← →", "Previous / next page from anywhere"), ("Ctrl + Alt + Space", "Show / hide the dock"), ("Mouse wheel / swipe", "Cycle pages"), ("Right-click", "Item and dock menus"), ("Drag files or links onto the dock", "Pin an app, file or link"), ("Drag an icon onto a page dot", "Move it to that page"), ("Drag an icon off the dock", "Remove (with Undo)") })
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 0) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(T(k, 12.5, Theme.Acc, FontWeights.SemiBold)); var t = T(d, 12.5, Theme.Ink2); Grid.SetColumn(t, 1); row.Children.Add(t);
            body.Children.Add(new Border { Child = row, Padding = new Thickness(12, 8, 12, 8), CornerRadius = new CornerRadius(9), Background = n++ % 2 == 0 ? Theme.Fill : Brushes.Transparent });
        }
    }
}
