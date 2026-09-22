using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace DockDX;

/// <summary>Borderless window that only exists to be the acrylic blur-behind surface under the dock's glass.</summary>
public sealed class BackdropWindow : Window
{
    IntPtr hwnd; int lw, lh, lr = -1;
    public BackdropWindow()
    {
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; ShowActivated = false;
        Background = Brushes.Transparent; Width = 10; Height = 10; Left = -20000; Top = -20000;
        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;
            Native.AddExStyle(hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
            HwndSource.FromHwnd(hwnd).AddHook((IntPtr h, int m, IntPtr w, IntPtr l, ref bool handled) =>
            { if (m == Native.WM_MOUSEACTIVATE) { handled = true; return new IntPtr(3); } return IntPtr.Zero; });
            ApplyTint();
        };
    }
    public IntPtr Handle => hwnd;
    public void ApplyTint()
    {
        if (hwnd == IntPtr.Zero) return;
        var s = Store.S;
        Native.SetAccent(hwnd, 4, Theme.AcrylicColor(Theme.Dark, s.Hue, s.Tint, s.Opacity));
    }
    public void SetRect(int x, int y, int w, int h, int radius)
    {
        if (hwnd == IntPtr.Zero || w < 4 || h < 4) return;
        Native.Place(hwnd, x, y, w, h);
        if (w != lw || h != lh || radius != lr) { Native.Round(hwnd, w, h, radius); lw = w; lh = h; lr = radius; }
    }
}

/// <summary>Popup with live DWM thumbnails of the running windows of the hovered app.</summary>
public sealed class PreviewWindow : Window
{
    IntPtr hwnd; readonly List<IntPtr> thumbs = new();
    readonly StackPanel row = new() { Orientation = Orientation.Horizontal };
    readonly TextBlock head = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Theme.Ink2, Margin = new Thickness(4, 0, 0, 8) };
    readonly List<(Border ph, Native.WinInfo w)> cards = new();
    public bool Hovering;
    public event Action Departed;

    public PreviewWindow()
    {
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; ShowActivated = false;
        Background = Brushes.Transparent; Left_(); Width = 240; Height = 200;
        var sp = new StackPanel { Margin = new Thickness(12) }; sp.Children.Add(head); sp.Children.Add(row);
        Content = new Border { Child = sp, BorderBrush = Theme.Line, BorderThickness = new Thickness(1) };
        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;
            Native.AddExStyle(hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
            Native.RoundCorners(hwnd);
            Native.SetAccent(hwnd, 4, Theme.AcrylicColor(Theme.Dark, Store.S.Hue, Store.S.Tint, 0.7));
        };
        MouseEnter += (_, _) => Hovering = true;
        MouseLeave += (_, _) => { Hovering = false; Departed?.Invoke(); };
    }
    void Left_() { base.Left = -20000; base.Top = -20000; }

    public void ShowFor(string name, List<Native.WinInfo> wins, Rect itemPx, string pos, double scale)
    {
        Clear();
        head.Text = name;
        row.Children.Clear();
        foreach (var w in wins)
        {
            var ph = new Border { Width = 200, Height = 125, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)) };
            var t = new TextBlock { Text = w.Title, FontSize = 11.5, Foreground = Theme.Ink, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 6, 0, 0), Width = 200 };
            var sp = new StackPanel(); sp.Children.Add(ph); sp.Children.Add(t);
            var card = new Border { Child = sp, Padding = new Thickness(5), CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 8, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            card.MouseEnter += (_, _) => card.Background = Theme.AccBg;
            card.MouseLeave += (_, _) => card.Background = Brushes.Transparent;
            var win = w; card.MouseLeftButtonUp += (_, _) => { Native.Focus(win.Hwnd); HidePreview(); };
            row.Children.Add(card); cards.Add((ph, w));
        }
        var c = (UIElement)Content; c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Width = c.DesiredSize.Width; Height = c.DesiredSize.Height;
        int wpx = (int)Math.Ceiling(Width * scale), hpx = (int)Math.Ceiling(Height * scale), gap = (int)(14 * scale);
        var scr = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
        int x, y;
        switch (pos)
        {
            case "left": x = (int)itemPx.Right + gap; y = (int)(itemPx.Top + itemPx.Height / 2 - hpx / 2.0); break;
            case "right": x = (int)itemPx.Left - gap - wpx; y = (int)(itemPx.Top + itemPx.Height / 2 - hpx / 2.0); break;
            case "top": x = (int)(itemPx.Left + itemPx.Width / 2 - wpx / 2.0); y = (int)itemPx.Bottom + gap; break;
            default: x = (int)(itemPx.Left + itemPx.Width / 2 - wpx / 2.0); y = (int)itemPx.Top - gap - hpx; break;
        }
        x = Math.Clamp(x, scr.Left + 8, Math.Max(scr.Left + 8, scr.Right - wpx - 8)); y = Math.Clamp(y, scr.Top + 8, Math.Max(scr.Top + 8, scr.Bottom - hpx - 8));
        if (!IsVisible) Show();
        Native.Place(hwnd, x, y, wpx, hpx);
        Dispatcher.BeginInvoke(new Action(() => Register(scale)), DispatcherPriority.Background);
    }

    void Register(double scale)
    {
        foreach (var (ph, w) in cards)
        {
            if (Native.DwmRegisterThumbnail(hwnd, w.Hwnd, out var th) != 0) continue;
            Native.DwmQueryThumbnailSourceSize(th, out var sz);
            var p = ph.TransformToAncestor(this).Transform(new Point(0, 0));
            double bx = p.X * scale, by = p.Y * scale, bw = ph.Width * scale, bh = ph.Height * scale;
            double k = sz.cx > 0 && sz.cy > 0 ? Math.Min(bw / sz.cx, bh / sz.cy) : 1, dw = sz.cx * k, dh = sz.cy * k;
            var props = new Native.ThumbProps
            {
                Flags = 1 | 4 | 8, Opacity = 255, Visible = 1,
                Dest = new Native.RECT { L = (int)(bx + (bw - dw) / 2), T = (int)(by + (bh - dh) / 2), R = (int)(bx + (bw + dw) / 2), B = (int)(by + (bh + dh) / 2) }
            };
            Native.DwmUpdateThumbnailProperties(th, ref props); thumbs.Add(th);
        }
    }

    void Clear() { foreach (var t in thumbs) Native.DwmUnregisterThumbnail(t); thumbs.Clear(); cards.Clear(); }
    public void HidePreview() { Clear(); Hovering = false; if (IsVisible) Hide(); }
}

/// <summary>Themed context menu with a "Move to page" sub-list, hosted in a transparent popup.</summary>
public static class PopupMenu
{
    public sealed class Entry { public string Label; public Action Fn; public bool Danger, Sep; public List<Entry> Sub; }
    static Popup pop;

    public static void Show(Point screenDip, List<Entry> entries)
    {
        pop?.SetCurrentValue(Popup.IsOpenProperty, false);
        pop = new Popup { AllowsTransparency = true, StaysOpen = false, Placement = PlacementMode.Absolute, HorizontalOffset = screenDip.X, VerticalOffset = screenDip.Y, PopupAnimation = PopupAnimation.Fade };
        var host = new Border { Background = Theme.PanelBg, BorderBrush = Theme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(5), Margin = new Thickness(14), MinWidth = 190,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 20, ShadowDepth = 4, Opacity = 0.4 } };
        pop.Child = host;
        Fill(host, entries);
        pop.IsOpen = true;
    }

    static void Fill(Border host, List<Entry> entries)
    {
        var sp = new StackPanel(); host.Child = sp;
        foreach (var e in entries)
        {
            if (e.Sep) { sp.Children.Add(new Border { Height = 1, Background = Theme.Fill2, Margin = new Thickness(6, 4, 6, 4) }); continue; }
            var t = new TextBlock { Text = e.Label + (e.Sub != null ? "   ▸" : ""), Foreground = e.Danger ? new SolidColorBrush(Color.FromRgb(255, 107, 122)) : Theme.Ink, FontSize = 13 };
            var row = new Border { Child = t, Padding = new Thickness(10, 7, 10, 7), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            row.MouseEnter += (_, _) => row.Background = Theme.AccBg; row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
            var en = e;
            row.MouseLeftButtonUp += (_, _) =>
            {
                if (en.Sub != null) { var back = new List<Entry> { new() { Label = "‹  Back", Fn = () => { } } }; back[0].Fn = () => Fill(host, entries); var l = new List<Entry>(back) { new() { Sep = true } }; l.AddRange(en.Sub); Fill(host, l); }
                else { if (en.Fn != null && !en.Label.StartsWith("‹")) { pop.IsOpen = false; en.Fn(); } else en.Fn?.Invoke(); }
            };
            sp.Children.Add(row);
        }
    }
}

public static class Toast
{
    static Popup pop; static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromSeconds(5) };
    static Toast() { Timer.Tick += (_, _) => { Timer.Stop(); if (pop != null) pop.IsOpen = false; }; }

    public static void Show(string msg, string action = null, Action fn = null)
    {
        if (pop != null) pop.IsOpen = false;
        var scr = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
        double scale = DpiScale.Value;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = msg, Foreground = Theme.Ink, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        if (action != null)
        {
            var b = new Border { Child = new TextBlock { Text = action, Foreground = Theme.Acc, FontWeight = FontWeights.SemiBold, FontSize = 13 }, Background = Theme.AccBg, CornerRadius = new CornerRadius(8), Padding = new Thickness(11, 4, 11, 4), Margin = new Thickness(16, 0, 0, 0), Cursor = Cursors.Hand };
            b.MouseLeftButtonUp += (_, _) => { pop.IsOpen = false; fn?.Invoke(); };
            row.Children.Add(b);
        }
        var host = new Border { Child = row, Background = Theme.PanelBg, BorderBrush = Theme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 9, 12, 9), Margin = new Thickness(16),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 20, ShadowDepth = 4, Opacity = 0.4 } };
        pop = new Popup { AllowsTransparency = true, StaysOpen = true, Placement = PlacementMode.Absolute, Child = host, PopupAnimation = PopupAnimation.Slide };
        host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        pop.HorizontalOffset = scr.Left / scale + (scr.Width / scale - host.DesiredSize.Width) / 2; pop.VerticalOffset = scr.Top / scale + 12;
        pop.IsOpen = true; Timer.Stop(); Timer.Start();
    }
}

public static class DpiScale
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetDpiForSystem();
    public static double Value => GetDpiForSystem() / 96.0;
}

public sealed class InputBox : Window
{
    readonly TextBox box = new();
    public string Result;
    InputBox(string title, string initial)
    {
        Title = title; WindowStyle = WindowStyle.ToolWindow; ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight; Topmost = true; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Theme.Dark ? new SolidColorBrush(Color.FromRgb(24, 27, 40)) : Brushes.White;
        var sp = new StackPanel { Margin = new Thickness(18), Width = 300 };
        sp.Children.Add(new TextBlock { Text = title, Foreground = Theme.Ink, Margin = new Thickness(0, 0, 0, 8) });
        box.Text = initial; box.Padding = new Thickness(6); sp.Children.Add(box);
        var ok = new Button { Content = "OK", IsDefault = true, Width = 80, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(4) };
        ok.Click += (_, _) => { Result = box.Text; DialogResult = true; };
        sp.Children.Add(ok); Content = sp;
        Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
    }
    public static string Ask(string title, string initial) { var d = new InputBox(title, initial); return d.ShowDialog() == true ? d.Result : null; }
}

