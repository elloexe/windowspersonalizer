using System.Collections.Generic;
using WindowsPersonalizer.Native;

namespace WindowsPersonalizer.Model
{
    /// <summary>The built-in looks.</summary>
    public static class Presets
    {
        public static IReadOnlyList<ThemeSettings> All { get; } = new List<ThemeSettings>
        {
            MacOSLight(),
            MacOSDark(),
            Ubuntu(),
            KdePlasma(),
            LinuxMint(),
            Elementary(),
            ChromeOS(),
            Windows11(),
            Windows10(),
        };

        private static WallpaperSpec Gen(WallpaperStyle style, params string[] colors) => new WallpaperSpec
        {
            Kind = WallpaperKind.Generated,
            Style = style,
            Colors = new List<string>(colors),
        };

        public static ThemeSettings MacOSLight() => new ThemeSettings
        {
            Name = "macOS",
            Description = "Menu bar on top, magnifying dock at the bottom, light mode.",
            DarkApps = false,
            DarkSystem = false,
            AccentColor = "#007AFF",
            Transparency = true,
            TaskbarMode = TaskbarMode.Hidden,
            TaskbarCentered = true,
            ShowSearch = false,
            ShowTaskView = false,
            ShowWidgets = false,
            ShowDesktopIcons = false,
            Wallpaper = Gen(WallpaperStyle.Waves, "#3A7BD5", "#9F7AEA", "#F6A6C1", "#FFD3A5"),
            DockEnabled = true,
            DockPosition = ScreenEdge.Bottom,
            DockStyle = DockStyle.Floating,
            DockIconSize = 52,
            DockMagnify = true,
            DockMagnification = 1.7,
            TopBarEnabled = true,
            TopBarStyle = TopBarStyle.MacOS,
            Clock24h = false,
            ClockShowDate = true,
        };

        public static ThemeSettings MacOSDark()
        {
            var t = MacOSLight();
            t.Name = "macOS Dark";
            t.Description = "The macOS look in dark mode.";
            t.DarkApps = true;
            t.DarkSystem = true;
            t.AccentColor = "#0A84FF";
            t.Wallpaper = Gen(WallpaperStyle.Waves, "#0F0C29", "#302B63", "#6A3093", "#24243E");
            return t;
        }

        public static ThemeSettings Ubuntu() => new ThemeSettings
        {
            Name = "Ubuntu",
            Description = "GNOME top panel with Activities, dock along the left edge.",
            DarkApps = true,
            DarkSystem = true,
            AccentColor = "#E95420",
            Transparency = true,
            TaskbarMode = TaskbarMode.Hidden,
            TaskbarCentered = false,
            ShowSearch = false,
            ShowTaskView = false,
            ShowWidgets = false,
            ShowDesktopIcons = true,
            Wallpaper = Gen(WallpaperStyle.Mesh, "#2C001E", "#77216F", "#E95420", "#5E2750"),
            DockEnabled = true,
            DockPosition = ScreenEdge.Left,
            DockStyle = DockStyle.Panel,
            DockIconSize = 44,
            DockMagnify = false,
            TopBarEnabled = true,
            TopBarStyle = TopBarStyle.Gnome,
            Clock24h = true,
            ClockShowDate = true,
        };

        public static ThemeSettings KdePlasma() => new ThemeSettings
        {
            Name = "KDE Plasma",
            Description = "Dark Breeze colors, left-aligned bottom panel.",
            DarkApps = true,
            DarkSystem = true,
            AccentColor = "#3DAEE9",
            Transparency = true,
            AccentOnTaskbar = false,
            TaskbarMode = TaskbarMode.Normal,
            TaskbarCentered = false,
            ShowSearch = false,
            ShowTaskView = false,
            ShowWidgets = false,
            ShowDesktopIcons = true,
            Wallpaper = Gen(WallpaperStyle.Linear, "#0B1A3A", "#1D4E89", "#3DAEE9"),
            DockEnabled = false,
            TopBarEnabled = false,
            Clock24h = true,
        };

        public static ThemeSettings LinuxMint() => new ThemeSettings
        {
            Name = "Linux Mint",
            Description = "Cinnamon-style: mint green, classic left-aligned panel.",
            DarkApps = true,
            DarkSystem = true,
            AccentColor = "#8BB158",
            Transparency = true,
            TaskbarMode = TaskbarMode.Normal,
            TaskbarCentered = false,
            ShowSearch = false,
            ShowTaskView = false,
            ShowWidgets = false,
            ShowDesktopIcons = true,
            Wallpaper = Gen(WallpaperStyle.Mesh, "#1B2A1B", "#3E6B35", "#8BB158", "#203A43"),
            DockEnabled = false,
            TopBarEnabled = false,
            Clock24h = true,
        };

        public static ThemeSettings Elementary() => new ThemeSettings
        {
            Name = "elementary OS",
            Description = "Slim top panel with centered clock and a simple dock.",
            DarkApps = false,
            DarkSystem = false,
            AccentColor = "#3689E6",
            Transparency = true,
            TaskbarMode = TaskbarMode.Hidden,
            TaskbarCentered = true,
            ShowSearch = false,
            ShowTaskView = false,
            ShowWidgets = false,
            ShowDesktopIcons = false,
            Wallpaper = Gen(WallpaperStyle.Waves, "#A1C4FD", "#C2E9FB", "#FBC2EB", "#E0C3FC"),
            DockEnabled = true,
            DockPosition = ScreenEdge.Bottom,
            DockStyle = DockStyle.Floating,
            DockIconSize = 48,
            DockMagnify = false,
            TopBarEnabled = true,
            TopBarStyle = TopBarStyle.Gnome,
            Clock24h = false,
            ClockShowDate = true,
        };

        public static ThemeSettings ChromeOS() => new ThemeSettings
        {
            Name = "ChromeOS",
            Description = "Light, airy, centered shelf.",
            DarkApps = false,
            DarkSystem = false,
            AccentColor = "#1A73E8",
            Transparency = true,
            TaskbarMode = TaskbarMode.Normal,
            TaskbarCentered = true,
            ShowSearch = false,
            ShowTaskView = false,
            ShowWidgets = false,
            ShowDesktopIcons = false,
            Wallpaper = Gen(WallpaperStyle.Mesh, "#E8F0FE", "#A8C7FA", "#FDE293", "#F6AEA9"),
            DockEnabled = false,
            TopBarEnabled = false,
        };

        public static ThemeSettings Windows11() => new ThemeSettings
        {
            Name = "Windows 11",
            Description = "The standard Windows 11 look.",
            DarkApps = false,
            DarkSystem = false,
            AccentColor = "#0078D4",
            Transparency = true,
            TaskbarMode = TaskbarMode.Normal,
            TaskbarCentered = true,
            ShowSearch = true,
            ShowTaskView = true,
            ShowWidgets = true,
            ShowDesktopIcons = true,
            Wallpaper = Gen(WallpaperStyle.Waves, "#0A1E5E", "#0067C0", "#4CC2FF", "#99EBFF"),
            DockEnabled = false,
            TopBarEnabled = false,
        };

        public static ThemeSettings Windows10() => new ThemeSettings
        {
            Name = "Windows 10",
            Description = "Dark, left-aligned taskbar like Windows 10.",
            DarkApps = true,
            DarkSystem = true,
            AccentColor = "#0078D7",
            Transparency = true,
            TaskbarMode = TaskbarMode.Normal,
            TaskbarCentered = false,
            ShowSearch = true,
            ShowTaskView = true,
            ShowWidgets = false,
            ShowDesktopIcons = true,
            Wallpaper = Gen(WallpaperStyle.Linear, "#001A33", "#00427A", "#0078D7"),
            DockEnabled = false,
            TopBarEnabled = false,
        };
    }
}
