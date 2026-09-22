using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DockDX;

/// <summary>Shared, mutable brushes so every window restyles instantly when the theme or tint changes.</summary>
public static class Theme
{
    static SolidColorBrush B() => new(Colors.Transparent);
    public static readonly SolidColorBrush Ink = B(), Ink2 = B(), Ink3 = B(), Fill = B(), Fill2 = B(), Line = B(), Acc = B(), AccBg = B(), PanelBg = B(), Widget = B(), OnAcc = B();
    public static bool Dark = true;

    public sealed record Preset(string Id, string Name, string Mode, double Hue, double Tint, double Opacity);
    public static readonly Preset[] Presets =
    {
        new("midnight", "Midnight", "dark", 215, 0.30, 0.55), new("aurora", "Aurora", "dark", 165, 0.40, 0.50),
        new("sunset", "Sunset", "dark", 18, 0.46, 0.50), new("rose", "Rose", "dark", 335, 0.42, 0.52),
        new("neon", "Neon", "dark", 285, 0.58, 0.45), new("graphite", "Graphite", "dark", 220, 0.00, 0.72),
        new("matrix", "Matrix", "dark", 130, 0.08, 0.90), new("fire", "Fire", "dark", 22, 0.42, 0.86), new("frost", "Frost", "light", 205, 0.25, 0.55), new("blush", "Blush", "light", 340, 0.30, 0.60), new("mint", "Mint", "light", 150, 0.30, 0.58)
    };

    public static Color Hsl(double h, double s, double l, double a = 1)
    {
        h = ((h % 360) + 360) % 360 / 360.0;
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        double F(double t) { if (t < 0) t += 1; if (t > 1) t -= 1; return t < 1 / 6.0 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2 / 3.0 ? p + (q - p) * (2 / 3.0 - t) * 6 : p; }
        return Color.FromArgb((byte)(a * 255), (byte)(F(h + 1 / 3.0) * 255), (byte)(F(h) * 255), (byte)(F(h - 1 / 3.0) * 255));
    }

    public static void Apply(bool dark, double hue)
    {
        Dark = dark;
        if (dark)
        {
            Ink.Color = Color.FromArgb(240, 255, 255, 255); Ink2.Color = Color.FromArgb(158, 255, 255, 255); Ink3.Color = Color.FromArgb(96, 255, 255, 255);
            Fill.Color = Color.FromArgb(18, 255, 255, 255); Fill2.Color = Color.FromArgb(34, 255, 255, 255); Line.Color = Color.FromArgb(44, 255, 255, 255);
            PanelBg.Color = Color.FromArgb(232, 18, 21, 33); Widget.Color = Color.FromArgb(128, 10, 12, 22);
            Acc.Color = Hsl(hue, 0.95, 0.68); AccBg.Color = Hsl(hue, 0.9, 0.6, 0.26);
        }
        else
        {
            Ink.Color = Color.FromRgb(15, 19, 32); Ink2.Color = Color.FromArgb(168, 15, 19, 32); Ink3.Color = Color.FromArgb(102, 15, 19, 32);
            Fill.Color = Color.FromArgb(14, 15, 19, 32); Fill2.Color = Color.FromArgb(26, 15, 19, 32); Line.Color = Color.FromArgb(200, 255, 255, 255);
            PanelBg.Color = Color.FromArgb(238, 248, 250, 255); Widget.Color = Color.FromArgb(158, 255, 255, 255);
            Acc.Color = Hsl(hue, 0.8, 0.42); AccBg.Color = Hsl(hue, 0.85, 0.55, 0.2);
        }
        if (dark && Store.S.Preset == "matrix")
        {
            Ink.Color = Color.FromArgb(240, 205, 255, 218); Ink2.Color = Color.FromArgb(175, 120, 255, 160); Ink3.Color = Color.FromArgb(105, 80, 255, 130);
            Fill.Color = Color.FromArgb(24, 70, 255, 130); Fill2.Color = Color.FromArgb(44, 70, 255, 130); Line.Color = Color.FromArgb(90, 60, 255, 120);
            PanelBg.Color = Color.FromArgb(238, 3, 12, 7); Widget.Color = Color.FromArgb(150, 2, 12, 6);
            Acc.Color = Hsl(130, 1, 0.6); AccBg.Color = Hsl(130, 1, 0.5, 0.25);
        }
        if (dark && Store.S.Preset == "fire")
        {
            Ink.Color = Color.FromArgb(242, 255, 238, 216); Ink2.Color = Color.FromArgb(178, 255, 196, 140); Ink3.Color = Color.FromArgb(108, 255, 150, 80);
            Fill.Color = Color.FromArgb(26, 255, 120, 40); Fill2.Color = Color.FromArgb(46, 255, 120, 40); Line.Color = Color.FromArgb(95, 255, 130, 50);
            PanelBg.Color = Color.FromArgb(238, 17, 6, 3); Widget.Color = Color.FromArgb(150, 16, 5, 2);
            Acc.Color = Hsl(24, 1, 0.58); AccBg.Color = Hsl(20, 1, 0.5, 0.27);
        }
        OnAcc.Color = Color.FromRgb(11, 14, 24);
        var r = Application.Current?.Resources;
        // XAML styles freeze whatever they reference, so hand them copies; code-built UI keeps the live mutable brushes.
        if (r != null) { r["Ink"] = Ink.Clone(); r["Ink2"] = Ink2.Clone(); r["Ink3"] = Ink3.Clone(); r["Fill"] = Fill.Clone(); r["Fill2"] = Fill2.Clone(); r["Acc"] = Acc.Clone(); r["AccBg"] = AccBg.Clone(); r["OnAcc"] = OnAcc.Clone(); r["PanelBg"] = PanelBg.Clone(); }
    }

    /// <summary>Acrylic tint as ABGR: base colour blended toward the hue by <c>tint</c>, alpha from <c>opacity</c>.</summary>
    public static uint AcrylicColor(bool dark, double hue, double tint, double opacity)
    {
        Color b = dark ? (Store.S.Preset == "matrix" ? Color.FromRgb(1, 6, 3) : Store.S.Preset == "fire" ? Color.FromRgb(22, 6, 3) : Color.FromRgb(14, 17, 28)) : Color.FromRgb(255, 255, 255), t = Hsl(hue, 0.85, dark ? 0.5 : 0.62);
        byte L(byte x, byte y) => (byte)(x + (y - x) * tint * 0.9);
        byte a = (byte)Math.Clamp(opacity * 255, 8, 250);
        return ((uint)a << 24) | ((uint)L(b.B, t.B) << 16) | ((uint)L(b.G, t.G) << 8) | L(b.R, t.R);
    }
}

/// <summary>Icon loading (256px shell icons) with a path resolver for bare names like "notepad.exe".</summary>
public static class Icons
{
    static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string ResolvePath(string p)
    {
        if (string.IsNullOrWhiteSpace(p) || p.Contains("://") || (p.Contains(':') && !Path.IsPathRooted(p))) return null; // URIs like ms-settings:
        if (Path.IsPathRooted(p)) return File.Exists(p) || Directory.Exists(p) ? p : null;
        var dirs = new List<string> { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) };
        dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries));
        dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps"));
        foreach (var d in dirs) { try { var f = Path.Combine(d.Trim(), p); if (File.Exists(f)) return f; } catch { } }
        return null;
    }

    public static string ShortcutTarget(string lnk)
    {
        try
        {
            dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            string t = sh.CreateShortcut(lnk).TargetPath; return string.IsNullOrEmpty(t) ? null : t;
        }
        catch { return null; }
    }

    /// <summary>Fills <c>Exe</c> and returns the shell icon (null → glyph tile).</summary>
    public static ImageSource For(Item it)
    {
        if (it.Type != "app") return null;
        string full = ResolvePath(it.Path);
        if (full != null && full.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) it.Exe = ShortcutTarget(full) ?? full;
        else it.Exe ??= full ?? it.Path;
        if (full == null) return null;
        if (Cache.TryGetValue(full, out var c)) return c;
        ImageSource img = Native.ShellIcon(full);
        if (img == null)
        {
            try
            {
                using var ic = System.Drawing.Icon.ExtractAssociatedIcon(full);
                if (ic != null) { var bs = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(ic.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); bs.Freeze(); img = bs; }
            }
            catch { }
        }
        Cache[full] = img;
        return img;
    }
}
