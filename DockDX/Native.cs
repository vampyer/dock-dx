using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DockDX;

/// <summary>Win32 / DWM / shell interop used for blur-behind, thumbnails, AppBar docking and launching.</summary>
public static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }

    /* ---------------------------------------------------------- window styles / position */
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOACTIVATE = 0x10, SWP_NOZORDER = 0x4, SWP_SHOWWINDOW = 0x40;
    public const int WM_MOUSEACTIVATE = 0x21, WM_HOTKEY = 0x312, WM_DISPLAYCHANGE = 0x7E, WM_SETTINGCHANGE = 0x1A;

    public static void AddExStyle(IntPtr h, int flags) => SetWindowLong(h, -20, GetWindowLong(h, -20) | flags);
    public const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

    public static void Place(IntPtr h, int x, int y, int w, int hgt, bool topmost = true) =>
        SetWindowPos(h, topmost ? HWND_TOPMOST : IntPtr.Zero, x, y, w, hgt, SWP_NOACTIVATE | (topmost ? 0u : SWP_NOZORDER));

    public static void Round(IntPtr h, int w, int hgt, int radius) => SetWindowRgn(h, CreateRoundRectRgn(0, 0, w + 1, hgt + 1, radius * 2, radius * 2), true);

    /* ---------------------------------------------------------- acrylic / DWM */
    [StructLayout(LayoutKind.Sequential)] struct AccentPolicy { public int State, Flags; public uint Color; public int Anim; }
    [StructLayout(LayoutKind.Sequential)] struct WCAD { public int Attr; public IntPtr Data; public int Size; }
    [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr h, ref WCAD d);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);

    /// <summary>state 4 = acrylic blur-behind, 3 = plain blur, 0 = off. abgr is the tint (alpha = strength).</summary>
    public static void SetAccent(IntPtr h, int state, uint abgr)
    {
        var p = new AccentPolicy { State = state, Flags = 2, Color = abgr };
        int sz = Marshal.SizeOf(p);
        IntPtr ptr = Marshal.AllocHGlobal(sz);
        try
        {
            Marshal.StructureToPtr(p, ptr, false);
            var d = new WCAD { Attr = 19, Data = ptr, Size = sz };
            SetWindowCompositionAttribute(h, ref d);
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }
    public static void RoundCorners(IntPtr h) { int v = 2; DwmSetWindowAttribute(h, 33, ref v, 4); }
    public static void DarkTitle(IntPtr h, bool dark) { int v = dark ? 1 : 0; DwmSetWindowAttribute(h, 20, ref v, 4); }
    public static bool Cloaked(IntPtr h) => DwmGetWindowAttribute(h, 14, out int v, 4) == 0 && v != 0;

    /* ---------------------------------------------------------- DWM live thumbnails */
    [StructLayout(LayoutKind.Sequential)]
    public struct ThumbProps { public uint Flags; public RECT Dest; public RECT Src; public byte Opacity; public int Visible; public int ClientOnly; }
    [DllImport("dwmapi.dll")] public static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
    [DllImport("dwmapi.dll")] public static extern int DwmUnregisterThumbnail(IntPtr thumb);
    [DllImport("dwmapi.dll")] public static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref ThumbProps p);
    [DllImport("dwmapi.dll")] public static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out SIZE size);

    /* ---------------------------------------------------------- windows / processes */
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder s, ref int n);

    public sealed class WinInfo { public IntPtr Hwnd; public string Title; public string Exe; }

    public static List<WinInfo> EnumTopLevel()
    {
        var list = new List<WinInfo>();
        uint self = (uint)Environment.ProcessId;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || GetWindow(h, 4) != IntPtr.Zero) return true;
            if ((GetWindowLong(h, -20) & WS_EX_TOOLWINDOW) != 0 || Cloaked(h)) return true;
            int len = GetWindowTextLength(h); if (len == 0) return true;
            GetWindowThreadProcessId(h, out uint pid); if (pid == self) return true;
            var sb = new StringBuilder(len + 1); GetWindowText(h, sb, sb.Capacity);
            string title = sb.ToString(); if (title == "Program Manager") return true;
            string exe = null;
            IntPtr ph = OpenProcess(0x1000, false, pid);
            if (ph != IntPtr.Zero)
            {
                var pb = new StringBuilder(1024); int n = pb.Capacity;
                if (QueryFullProcessImageName(ph, 0, pb, ref n)) exe = pb.ToString();
                CloseHandle(ph);
            }
            list.Add(new WinInfo { Hwnd = h, Title = title, Exe = exe });
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static void Focus(IntPtr h) { if (IsIconic(h)) ShowWindow(h, 9); SetForegroundWindow(h); }

    /* ---------------------------------------------------------- system stats */
    [StructLayout(LayoutKind.Sequential)] struct FT { public uint Lo, Hi; public ulong V => ((ulong)Hi << 32) | Lo; }
    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out FT idle, out FT kernel, out FT user);
    [StructLayout(LayoutKind.Sequential)]
    struct MEMSTATUS { public uint Len, Load; public ulong TotalPhys, AvailPhys, TotalPage, AvailPage, TotalVirt, AvailVirt; }
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMSTATUS m);

    static ulong _idle, _total;
    public static double CpuPercent()
    {
        if (!GetSystemTimes(out var i, out var k, out var u)) return 0;
        ulong idle = i.V, total = k.V + u.V;
        double r = _total == 0 || total == _total ? 0 : 100.0 * (1.0 - (double)(idle - _idle) / (total - _total));
        _idle = idle; _total = total;
        return Math.Max(0, Math.Min(100, r));
    }
    public static double RamPercent() { var m = new MEMSTATUS { Len = (uint)Marshal.SizeOf<MEMSTATUS>() }; GlobalMemoryStatusEx(ref m); return m.Load; }

    /* ---------------------------------------------------------- AppBar (reserve screen space) */
    [StructLayout(LayoutKind.Sequential)]
    struct APPBARDATA { public int Size; public IntPtr Hwnd; public uint Msg; public uint Edge; public RECT Rc; public IntPtr LParam; }
    [DllImport("shell32.dll")] static extern uint SHAppBarMessage(uint msg, ref APPBARDATA d);
    static bool _bar;

    public static RECT AppBarSet(IntPtr h, string edge, RECT rc)
    {
        var d = new APPBARDATA { Size = Marshal.SizeOf<APPBARDATA>(), Hwnd = h };
        if (!_bar) { SHAppBarMessage(0, ref d); _bar = true; }
        d.Edge = edge switch { "left" => 0u, "top" => 1u, "right" => 2u, _ => 3u };
        d.Rc = rc;
        SHAppBarMessage(2, ref d);
        SHAppBarMessage(3, ref d);
        return d.Rc;
    }
    public static void AppBarRemove(IntPtr h)
    {
        if (!_bar) return;
        var d = new APPBARDATA { Size = Marshal.SizeOf<APPBARDATA>(), Hwnd = h };
        SHAppBarMessage(1, ref d); _bar = false;
    }

    /* ---------------------------------------------------------- 256px shell icons */
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr hbm); }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IShellItemImageFactory ppv);

    public static BitmapSource ShellIcon(string path, int size = 256)
    {
        try
        {
            var iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var f);
            if (f.GetImage(new SIZE { cx = size, cy = size }, 1 | 4, out IntPtr hbm) != 0 || hbm == IntPtr.Zero) return null;
            try
            {
                var src = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                var fixedAlpha = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0); fixedAlpha.Freeze();
                return fixedAlpha;
            }
            finally { DeleteObject(hbm); }
        }
        catch { return null; }
    }
}

