# Windows Personalizer

Make Windows look like **macOS**, **Ubuntu**, **KDE Plasma**, **Linux Mint**, **elementary OS**,
**ChromeOS**, or anything you want, in one click. You can always go back.

## Download & run

1. Grab **[`release/WindowsPersonalizer.exe`](release/WindowsPersonalizer.exe)** (one file, nothing to install,
   no admin rights needed).
2. Double-click it. If Windows SmartScreen warns you (the exe isn't code-signed), click
   **More info → Run anyway**.
3. Pick a theme, tweak anything you like, press **Apply**.

Works on Windows 10 and Windows 11 (64-bit).

## What it can change

| | |
|---|---|
| **Theme presets** | macOS, macOS Dark, Ubuntu, KDE Plasma, Linux Mint, elementary OS, ChromeOS, Windows 11, Windows 10 |
| **Dock** | macOS-style floating dock with magnifying icons, or an Ubuntu-style edge panel. Bottom / left / right / top. Drag apps, shortcuts or folders onto it to pin them; right-click to remove. Running apps get a dot, and clicking a running app switches to it. |
| **Top bar** | macOS menu bar (current app name, clock, search, Wi-Fi/sound, notifications) or GNOME panel ("Activities", centered clock). The menu has Settings, Sleep, Restart, Shut Down, Lock and Log Out. |
| **Taskbar** | Always show / auto-hide / **hide completely** (the dock replaces it), centered or left icons, show/hide the Search, Task View and Widgets buttons |
| **Colors** | Light/dark mode (apps and Windows separately), any accent color, transparency, accent on Start/taskbar and title bars |
| **Wallpaper** | Built-in generator (waves, soft mesh, linear, solid) in any colors, or your own picture |
| **Desktop** | Show/hide desktop icons, start with Windows |
| **Share themes** | Export your setup to a `.json` file and import other people's |

The dock and top bar reserve their space on screen like the real taskbar, so maximized windows
don't cover them. They also hide automatically when a full-screen video or game is playing.

## Going back

* **Restore original look** (bottom-left of the app) puts back exactly what you had before
  the first time you pressed Apply: colors, taskbar, wallpaper, everything.
* Closing the app's window keeps the dock/top bar running in the system tray. Right-click the
  tray icon → **Exit** to stop them; your taskbar comes back automatically.
* If the taskbar ever looks off, press **Restart Explorer** in the app.

Everything is per-user (`HKEY_CURRENT_USER`). Nothing system-wide is touched. Settings live in
`%AppData%\WindowsPersonalizer`.

## Build it yourself

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

* **Windows:** double-click `build_windows.bat`
* **Linux / macOS (cross-compile):** `./build.sh`

Either one produces `release/WindowsPersonalizer.exe`, a self-contained single file.

### Project layout

```
src/WindowsPersonalizer/
  MainWindow.xaml(.cs)   settings UI (themes gallery, colors, taskbar, dock, top bar, wallpaper)
  Model/                 ThemeSettings, built-in Presets, JSON config store
  Services/              registry tweaks, backup/restore, taskbar control, wallpaper generator, icons
  Shell/                 DockWindow, TopBarWindow, ShellManager
  Native/                Win32 interop (AppBar space reservation, DPI, P/Invoke)
```

## Notes / limits

* On recent Windows 11 builds, Microsoft blocks apps from toggling the **Widgets** button; that
  one setting may be skipped.
* Some accent/taskbar changes only show everywhere after **Restart Explorer**.
* The dock and top bar run on your main monitor.
