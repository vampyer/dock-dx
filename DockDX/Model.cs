using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace DockDX;

public class Item
{
    public string Id { get; set; } = Store.Uid();
    public string Type { get; set; } = "app";      // app | live
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Args { get; set; } = "";
    public string Glyph { get; set; }
    public string Live { get; set; }               // media | perf | clock
    public string Exe { get; set; }                // resolved executable (used for running-window matching)
    public double Hue { get; set; } = -1;
    public int Span { get; set; } = 1;
    [System.Text.Json.Serialization.JsonIgnore] public bool Running { get; set; }   // transient entry in the running-apps section
}

public class Page
{
    public string Id { get; set; } = Store.Uid();
    public string Name { get; set; } = "";
    public List<Item> Items { get; set; } = new();
}

public class Settings
{
    public string Position { get; set; } = "bottom";   // bottom | top | left | right | floating
    public string Theme { get; set; } = "dark";        // dark | light | auto
    public double Hue { get; set; } = 215;
    public double Tint { get; set; } = 0.30;
    public double Opacity { get; set; } = 0.55;
    public double IconSize { get; set; } = 54;
    public double Zoom { get; set; } = 1.8;
    public double Spread { get; set; } = 2.6;
    public bool Labels { get; set; } = true;
    public bool Previews { get; set; } = true;
    public bool Wheel { get; set; } = true;
    public bool ShowRunning { get; set; } = true;      // running apps section on the first page
    public bool Reserve { get; set; } = false;         // register as an AppBar and reserve screen space
    public bool AutoHide { get; set; } = false;        // slide off-screen except a thin sliver until hovered
    public bool Autostart { get; set; } = false;
    public string Units { get; set; } = "c";
    public string Preset { get; set; } = "midnight";
    public double FloatX { get; set; } = 0.5;
    public double FloatY { get; set; } = 0.78;
}

public class State
{
    public int Current { get; set; }
    public Settings S { get; set; } = new();
    public List<Page> Pages { get; set; } = new();
}

public static class Store
{
    public const int PageCount = 10;
    public static State St = new();
    public static Settings S => St.S;
    public static List<Page> Pages => St.Pages;
    public static Page CurrentPage => St.Pages[St.Current];

    public static event Action<int> PagesChanged;      // item list of a page changed
    public static event Action LayoutChanged;          // names / order changed
    public static event Action<string> SettingChanged;
    public static event Action CurrentChanged;
    public static event Action Reset;

    public static string Uid() => Guid.NewGuid().ToString("N").Substring(0, 8);
    static string FilePath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DockDX", "state.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath));
                if (s != null && s.Pages.Count == PageCount) { St = s; St.Current = Math.Clamp(St.Current, 0, PageCount - 1); return; }
            }
        }
        catch { /* corrupt file: fall back to defaults */ }
        St = Fresh();
    }

    static System.Threading.Timer _t;
    public static void Save()
    {
        _t?.Dispose();
        _t = new System.Threading.Timer(_ =>
        {
            try { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)); File.WriteAllText(FilePath, JsonSerializer.Serialize(St, Json)); } catch { }
        }, null, 300, System.Threading.Timeout.Infinite);
    }
    public static void SaveNow()
    {
        try { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)); File.WriteAllText(FilePath, JsonSerializer.Serialize(St, Json)); } catch { }
    }

    public static Item App(string name, string path, string glyph = null, double hue = -1) => new() { Type = "app", Name = name, Path = path, Glyph = glyph, Hue = hue };
    public static Item LiveItem(string kind) => new()
    {
        Type = "live", Live = kind, Span = kind == "media" ? 3 : 2,
        Name = kind switch { "media" => "Media Controller", "perf" => "Performance Monitor", _ => "Clock & Weather" }
    };

    static State Fresh()
    {
        var names = new[] { "Home", "Work", "Create", "Media", "Play", "Develop", "Social", "System", "Widgets", "Scratch" };
        var st = new State();
        foreach (var n in names) st.Pages.Add(new Page { Name = n });
        var p = st.Pages;
        p[0].Items.AddRange(new[] {
            App("File Explorer", "explorer.exe", "📁", 45), App("Edge", "msedge.exe", "🌐", 200), App("Notepad", "notepad.exe", "📝", 55),
            App("Calculator", "calc.exe", "🧮", 150), App("Terminal", "wt.exe", "⌨️", 260), App("Settings", "ms-settings:", "⚙️", 220), LiveItem("clock") });
        p[1].Items.AddRange(new[] {
            App("Task Manager", "taskmgr.exe", "📊", 165), App("Snipping Tool", "snippingtool.exe", "✂️", 330),
            App("Paint", "mspaint.exe", "🎨", 20), App("Command Prompt", "cmd.exe", "▮", 240), App("PowerShell", "powershell.exe", "❯", 215) });
        p[2].Items.AddRange(new[] { App("Paint", "mspaint.exe", "🎨", 340), App("Snipping Tool", "snippingtool.exe", "✂️", 190) });
        p[3].Items.AddRange(new[] { LiveItem("media"), App("Sound Settings", "ms-settings:sound", "🔊", 280) });
        p[5].Items.AddRange(new[] { App("Terminal", "wt.exe", "⌨️", 140), App("PowerShell", "powershell.exe", "❯", 215), App("Command Prompt", "cmd.exe", "▮", 240) });
        p[7].Items.AddRange(new[] { LiveItem("perf"), App("Task Manager", "taskmgr.exe", "📊", 165), App("Device Manager", "devmgmt.msc", "🖥️", 200) });
        p[8].Items.AddRange(new[] { LiveItem("clock"), LiveItem("perf"), LiveItem("media") });
        return st;
    }

    /* ------------------------------------------------------------ mutations */
    public static void Set(string key, Action<Settings> apply) { apply(S); Save(); SettingChanged?.Invoke(key); }
    public static void SetCurrent(int i) { St.Current = i; Save(); CurrentChanged?.Invoke(); }

    public static (int page, int index, Item item)? Find(string id)
    {
        for (int p = 0; p < Pages.Count; p++)
        {
            int i = Pages[p].Items.FindIndex(x => x.Id == id);
            if (i >= 0) return (p, i, Pages[p].Items[i]);
        }
        return null;
    }
    public static void AddItem(int page, Item it, int index = -1)
    {
        var list = Pages[page].Items;
        list.Insert(index < 0 || index > list.Count ? list.Count : index, it);
        Save(); PagesChanged?.Invoke(page);
    }
    public static void RemoveItem(string id)
    {
        var f = Find(id); if (f == null) return;
        Pages[f.Value.page].Items.RemoveAt(f.Value.index); Save(); PagesChanged?.Invoke(f.Value.page);
    }
    public static void MoveItem(string id, int toPage, int index = -1)
    {
        var f = Find(id); if (f == null) return;
        Pages[f.Value.page].Items.RemoveAt(f.Value.index);
        var dest = Pages[toPage].Items;
        int at = index < 0 ? dest.Count : index;
        if (f.Value.page == toPage && f.Value.index < at) at--;
        dest.Insert(Math.Clamp(at, 0, dest.Count), f.Value.item);
        Save(); PagesChanged?.Invoke(toPage);
        if (f.Value.page != toPage) PagesChanged?.Invoke(f.Value.page);
        LayoutChanged?.Invoke();
    }
    public static void Touch(int page) { Save(); PagesChanged?.Invoke(page); }
    public static void RenamePage(int i, string name) { Pages[i].Name = string.IsNullOrWhiteSpace(name) ? $"Page {i + 1}" : name.Trim(); Save(); LayoutChanged?.Invoke(); }
    public static void MovePage(int from, int to)
    {
        if (to < 0 || to >= PageCount || from == to) return;
        string cur = CurrentPage.Id;
        var p = Pages[from]; Pages.RemoveAt(from); Pages.Insert(to, p);
        St.Current = Pages.FindIndex(x => x.Id == cur);
        Save(); LayoutChanged?.Invoke();
    }
    public static void ClearPage(int i) { Pages[i].Items.Clear(); Save(); PagesChanged?.Invoke(i); LayoutChanged?.Invoke(); }
    public static void ClearAll() { foreach (var p in Pages) p.Items.Clear(); Save(); PagesChanged?.Invoke(St.Current); LayoutChanged?.Invoke(); }
    public static void ResetAll() { St = Fresh(); Save(); Reset?.Invoke(); }

    public static void SetAutostart(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (on) k.SetValue("DockDX", "\"" + Environment.ProcessPath + "\" --startup"); else k.DeleteValue("DockDX", false);
        }
        catch { }
    }
}
