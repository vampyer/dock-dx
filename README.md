# Dock DX

A native Windows launcher dock in C# / WPF (.NET 8): 10 pages, live widgets, fluid magnification, acrylic glass,
themes, auto-hide, and edge/floating docking.

## Run / build

```powershell
cd DockDX
dotnet run -c Release                                   # run

# standalone, self-contained single-file exe (no .NET install required to run it)
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -o ..\dist
```

State is stored in `%APPDATA%\DockDX\state.json`; crashes are logged to `%APPDATA%\DockDX\error.log`.

## Architecture

| File | Role |
|---|---|
| `Native.cs` | P/Invoke: acrylic (`SetWindowCompositionAttribute`), DWM thumbnails, AppBar, EnumWindows, 256px shell icons |
| `DockWindow.cs` | The dock window: paging, spring-based parabolic magnification, drag & drop, previews, menus, auto-hide slide, theme overlay effects (rain/fire) |
| `Ui.cs` | Acrylic backdrop window, live preview window, popup menu, toast |
| `Widgets.cs` / `Live.cs` | Media, Performance and Clock/Weather widgets (plus compact square variants for side docks) and their data sources |
| `PanelWindow.cs` | Settings: Dock (position/behaviour), Appearance (themes/glass), Pages (layout manager), Add, About |
| `Model.cs` / `Theme.cs` | Persistent state (10 pages, each with its own items) and themed brushes/presets |
| `Program.cs` | App entry point, tray icon and its quick-toggle menu |

The glass is a separate **acrylic backdrop window** that follows the magnified strip each frame, with the icon overlay
above it; that is how a real blur-behind is achieved while the icons can still overflow the glass.

## Features

* **10 pages** of shortcuts, each independently named/reordered/cleared from Settings → Pages.
* **Live widgets**: Media Controller (Windows media session), Performance Monitor (CPU/GPU/RAM), Clock & Weather.
* **Hover magnification** with a parabolic falloff, spring physics, and live DWM window thumbnail previews.
* **Running apps section** on page 1 — open windows not already pinned there show up automatically.
* **Dock position**: bottom, top, left, right, or floating, picked from a visual edge picker in Settings → Dock,
  the tray icon's menu, or the dock's own right-click menu.
* **Auto-hide**: slides almost fully off-screen, leaving a thin sliver that reveals it on hover.
* **Themes**: Midnight, Aurora, Sunset, Rose, Neon, Graphite, Matrix (animated digital rain), Fire (animated flames
  and embers), Frost, Blush, Mint — plus manual hue/tint/opacity sliders.
* **Start with Windows**, toggleable from Settings, the tray menu, or `DockDX.exe --autostart on|off`.

## Controls

Arrow keys (pointer over dock), `Ctrl+Alt+←/→`, mouse wheel, swipe, or the page dots switch pages.
`Ctrl+Alt+Space` shows/hides the dock. Drag files or links onto the dock to pin them, drag an icon onto a page dot to move it,
drag it off the dock to remove it (with Undo). Right-click for menus; the gear opens Settings.

## Notes

* Acrylic blur radius is fixed by Windows; the Opacity and Tint sliders control how frosted it looks.
* "Reserve screen space" registers the dock as an AppBar so maximized windows don't cover it.
* Media widget uses `scripts/media.ps1` (Windows media session); GPU load uses the `GPU Engine` performance counters via `typeperf`.
* Debug aids (set `DOCKDX_DEBUG=1`): create `%TEMP%\dockdx.snap` to render the dock overlay to `%TEMP%\dockdx.png`, or
  `%TEMP%\dockdx.panel` / `%TEMP%\dockdx.psnap` to open and render the Settings panel.
