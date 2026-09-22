using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace DockDX;

public static class Program
{
    static NotifyIconHost tray;

    [STAThread]
    public static void Main(string[] args)
    {
        // CLI: DockDX.exe --autostart on|off   (no UI; handy for installers and scripts)
        int ai = Array.IndexOf(args, "--autostart");
        if (ai >= 0 && ai + 1 < args.Length)
        {
            bool on = args[ai + 1].Equals("on", StringComparison.OrdinalIgnoreCase);
            Store.Load(); Store.S.Autostart = on; Store.SetAutostart(on); Store.SaveNow();
            return;
        }
        try { Run(); }
        catch (Exception ex)
        {
            try { Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DockDX")); File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DockDX", "error.log"), DateTime.Now + "\n" + ex + "\n\n"); } catch { }
        }
    }

    static void Run()
    {
        using var mutex = new Mutex(true, "DockDX.SingleInstance", out bool first);
        if (!first) return;

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            try { File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DockDX", "error.log"), DateTime.Now + "\n" + e.Exception + "\n\n"); } catch { }
            e.Handled = true;
        };

        Store.Load();
        Theme.Apply(Store.S.Theme != "light", Store.S.Hue);
        PanelWindow.InstallStyles();
        Live.Start(app.Dispatcher);

        var backdrop = new BackdropWindow(); var preview = new PreviewWindow();
        backdrop.Show();
        var dock = new DockWindow(backdrop, preview);
        var panel = new PanelWindow(dock);
        dock.OpenPanel += () => panel.Open();
        panel.IsVisibleChanged += (_, _) => dock.SuppressAutoHide = panel.IsVisible;   // don't hide the dock while its own settings are open
        dock.Start();

        tray = new NotifyIconHost(dock, panel);
        app.Exit += (_, _) => { tray.Dispose(); Store.SaveNow(); dock.Close(); };
        app.Run();
    }
}

sealed class NotifyIconHost : IDisposable
{
    readonly System.Windows.Forms.NotifyIcon icon;

    public NotifyIconHost(DockWindow dock, PanelWindow panel)
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Show / hide dock", null, (_, _) => dock.ToggleVisible());
        menu.Items.Add("Layout && settings…", null, (_, _) => panel.Open());
        var auto = new System.Windows.Forms.ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        auto.Click += (_, _) => Store.Set("autostart", x => x.Autostart = auto.Checked);
        menu.Opening += (_, _) => auto.Checked = Store.S.Autostart;
        var posMenu = new System.Windows.Forms.ToolStripMenuItem("Dock position");
        foreach (var (id, name) in new[] { ("bottom", "Bottom edge"), ("top", "Top edge"), ("left", "Left edge"), ("right", "Right edge"), ("floating", "Floating") })
        {
            var mi = new System.Windows.Forms.ToolStripMenuItem(name) { Tag = id };
            mi.Click += (_, _) => Store.Set("position", x => x.Position = id);
            posMenu.DropDownItems.Add(mi);
        }
        menu.Opening += (_, _) => { foreach (System.Windows.Forms.ToolStripMenuItem mi in posMenu.DropDownItems) mi.Checked = (string)mi.Tag == Store.S.Position; };
        var hide = new System.Windows.Forms.ToolStripMenuItem("Auto-hide dock") { CheckOnClick = true };
        hide.Click += (_, _) => Store.Set("autohide", x => x.AutoHide = hide.Checked);
        menu.Opening += (_, _) => hide.Checked = Store.S.AutoHide;
        menu.Items.Add(posMenu);
        menu.Items.Add(hide);
        menu.Items.Add(auto);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit Dock DX", null, (_, _) => Application.Current.Shutdown());
        icon = new System.Windows.Forms.NotifyIcon
        {
            Text = "Dock DX", Visible = true, ContextMenuStrip = menu,
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath)
        };
        icon.MouseClick += (_, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) dock.ToggleVisible(); };
    }

    public void Dispose() { icon.Visible = false; icon.Dispose(); }
}
