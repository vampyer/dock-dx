using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DockDX;

public class MediaInfo
{
    public bool Active; public string Title = "", Artist = ""; public bool Playing; public double Pos, Dur; public ImageSource Art;
}
public class WeatherInfo { public double? TempC; public string Kind = "cloud"; public bool Night; }

/// <summary>Live data feeding the widgets: CPU/GPU/RAM, media session, weather, running windows.</summary>
public static class Live
{
    public const int Hist = 48;
    public static double Cpu, Ram, Gpu = -1;
    public static readonly List<double> HCpu = new(), HGpu = new(), HRam = new();
    public static MediaInfo Media = new();
    public static WeatherInfo Weather;
    public static List<Native.WinInfo> Windows = new();

    public static event Action StatsChanged, MediaChanged, WeatherChanged, WindowsChanged;

    static Dispatcher _ui;
    static string _mediaKey = "";
    static int _mediaBusy;
    static readonly string Script = ExtractScript();

    /// <summary>The media helper is embedded in the exe; write it out once so PowerShell can run it.</summary>
    static string ExtractScript()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DockDX", "media.ps1");
        try
        {
            using var s = typeof(Live).Assembly.GetManifestResourceStream("media.ps1");
            if (s == null) return path;
            using var r = new StreamReader(s); string text = r.ReadToEnd();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (!File.Exists(path) || File.ReadAllText(path) != text) File.WriteAllText(path, text, new System.Text.UTF8Encoding(true));
        }
        catch { }
        return path;
    }

    public static void Start(Dispatcher ui)
    {
        _ui = ui;
        for (int i = 0; i < Hist; i++) { HCpu.Add(0); HGpu.Add(0); HRam.Add(0); }
        Native.CpuPercent();

        var stats = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        stats.Tick += (_, _) =>
        {
            Cpu = Native.CpuPercent(); Ram = Native.RamPercent();
            Push(HCpu, Cpu); Push(HRam, Ram); Push(HGpu, Gpu < 0 ? 0 : Gpu);
            StatsChanged?.Invoke();
        };
        stats.Start();
        StartGpuCounter();

        var wins = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        wins.Tick += (_, _) => { Windows = Native.EnumTopLevel(); WindowsChanged?.Invoke(); };
        wins.Start(); Windows = Native.EnumTopLevel();

        var media = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        media.Tick += async (_, _) => await PollMedia("status");
        media.Start(); _ = PollMedia("status");

        var wx = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        wx.Tick += async (_, _) => await FetchWeather();
        wx.Start(); _ = FetchWeather();
    }

    static void Push(List<double> l, double v) { l.Add(v); if (l.Count > Hist) l.RemoveAt(0); }

    /* GPU utilisation via the "GPU Engine" performance counters (typeperf keeps one cheap process alive). */
    static void StartGpuCounter()
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo("typeperf", "\"\\GPU Engine(*engtype_3D)\\Utilization Percentage\" -si 2")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            if (p == null) return;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { p.Kill(); } catch { } };
            Task.Run(() =>
            {
                string line;
                while ((line = p.StandardOutput.ReadLine()) != null)
                {
                    if (!line.StartsWith("\"") || line.StartsWith("\"(PDH")) continue;
                    double sum = 0;
                    foreach (var f in line.Split(new[] { "\",\"" }, StringSplitOptions.None).Skip(1))
                        if (double.TryParse(f.Trim('"'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) sum += v;
                    Gpu = Math.Min(100, Math.Round(sum));
                }
                Gpu = -1;
            });
        }
        catch { Gpu = -1; }
    }

    /* Windows System Media Transport Controls through the bundled PowerShell helper. */
    static async Task<JsonElement?> RunMedia(string cmd)
    {
        if (!File.Exists(Script)) return null;
        return await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{Script}\" {cmd}")
                { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = System.Text.Encoding.UTF8 };
                using var p = Process.Start(psi);
                string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(8000);
                return (JsonElement?)JsonDocument.Parse(o.Trim()).RootElement.Clone();
            }
            catch { return null; }
        });
    }

    static async Task PollMedia(string cmd)
    {
        if (Interlocked.Exchange(ref _mediaBusy, 1) == 1) return;
        try
        {
            var r = await RunMedia(cmd); if (r == null) return;
            var j = r.Value;
            if (!j.TryGetProperty("active", out var a) || !a.GetBoolean()) { _mediaKey = ""; Media = new MediaInfo(); MediaChanged?.Invoke(); return; }
            var m = new MediaInfo
            {
                Active = true, Title = Str(j, "title"), Artist = Str(j, "artist"),
                Playing = j.TryGetProperty("playing", out var pl) && pl.GetBoolean(),
                Pos = Num(j, "pos"), Dur = Num(j, "dur")
            };
            string key = m.Title + "|" + m.Artist;
            if (key != _mediaKey)
            {
                _mediaKey = key; m.Art = null;
                var t = await RunMedia("thumb");
                if (t != null && t.Value.TryGetProperty("thumb", out var th)) m.Art = DecodeArt(th.GetString());
            }
            else m.Art = Media.Art;
            Media = m; MediaChanged?.Invoke();
        }
        finally { Interlocked.Exchange(ref _mediaBusy, 0); }
    }

    static string Str(JsonElement j, string k) => j.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";
    static double Num(JsonElement j, string k) => j.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    static ImageSource DecodeArt(string dataUri)
    {
        try
        {
            var bytes = Convert.FromBase64String(dataUri.Substring(dataUri.IndexOf(',') + 1));
            var bmp = new BitmapImage(); bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = new MemoryStream(bytes); bmp.EndInit(); bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    public static async void MediaCommand(string cmd) { await Task.Run(async () => { await RunMedia(cmd); }); _mediaBusy = 0; await PollMedia("status"); }

    /* Weather from Open-Meteo, located by IP (no API key). */
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    static async Task FetchWeather()
    {
        try
        {
            using var g = JsonDocument.Parse(await Http.GetStringAsync("https://get.geojs.io/v1/ip/geo.json"));
            string lat = g.RootElement.GetProperty("latitude").GetString(), lon = g.RootElement.GetProperty("longitude").GetString();
            using var w = JsonDocument.Parse(await Http.GetStringAsync($"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,weather_code,is_day"));
            var c = w.RootElement.GetProperty("current");
            Weather = new WeatherInfo { TempC = c.GetProperty("temperature_2m").GetDouble(), Kind = Kind(c.GetProperty("weather_code").GetInt32()), Night = c.GetProperty("is_day").GetInt32() == 0 };
        }
        catch { Weather ??= new WeatherInfo(); }
        WeatherChanged?.Invoke();
    }

    static string Kind(int c) => c == 0 ? "sun" : c <= 2 ? "partly" : c == 3 ? "cloud" : c is 45 or 48 ? "fog"
        : (c >= 51 && c <= 67) || (c >= 80 && c <= 82) ? "rain" : (c >= 71 && c <= 77) || c is 85 or 86 ? "snow" : c >= 95 ? "storm" : "cloud";
}
