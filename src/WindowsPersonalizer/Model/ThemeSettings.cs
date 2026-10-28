using System.Collections.Generic;
using WindowsPersonalizer.Native;

namespace WindowsPersonalizer.Model
{
    public enum TaskbarMode
    {
        /// <summary>The normal, always-visible Windows taskbar.</summary>
        Normal,
        /// <summary>Taskbar slides in when the mouse touches the screen edge.</summary>
        AutoHide,
        /// <summary>Taskbar is fully hidden (use the Dock / Top bar instead).</summary>
        Hidden,
    }

    public enum DockStyle
    {
        /// <summary>Floating rounded dock, centered (macOS).</summary>
        Floating,
        /// <summary>Full-length panel along the screen edge (Ubuntu / GNOME).</summary>
        Panel,
    }

    public enum TopBarStyle
    {
        /// <summary>macOS menu bar: app name on the left, clock on the right.</summary>
        MacOS,
        /// <summary>GNOME panel: "Activities" on the left, clock in the middle.</summary>
        Gnome,
    }

    public enum WallpaperKind
    {
        /// <summary>Leave the current wallpaper alone.</summary>
        Keep,
        /// <summary>Generate a gradient wallpaper from colors.</summary>
        Generated,
        /// <summary>Use an image file.</summary>
        Image,
    }

    public enum WallpaperStyle
    {
        Waves,
        Mesh,
        Linear,
        Solid,
    }

    public class WallpaperSpec
    {
        public WallpaperKind Kind { get; set; } = WallpaperKind.Keep;
        public WallpaperStyle Style { get; set; } = WallpaperStyle.Mesh;
        public List<string> Colors { get; set; } = new List<string> { "#1E3C72", "#2A5298", "#6DD5FA" };
        public string ImagePath { get; set; } = "";

        public WallpaperSpec Clone() => new WallpaperSpec
        {
            Kind = Kind,
            Style = Style,
            Colors = new List<string>(Colors),
            ImagePath = ImagePath,
        };
    }

    /// <summary>Everything that makes up a "look". Presets are just instances of this.</summary>
    public class ThemeSettings
    {
        public string Name { get; set; } = "Custom";
        public string Description { get; set; } = "";

        // Appearance
        public bool DarkApps { get; set; }
        public bool DarkSystem { get; set; }
        public string AccentColor { get; set; } = "#0078D4";
        public bool Transparency { get; set; } = true;
        /// <summary>Frosted-glass blur behind the Dock and Top bar.</summary>
        public bool GlassEffect { get; set; } = true;
        public bool AccentOnTaskbar { get; set; }
        public bool AccentOnTitleBars { get; set; }

        // Taskbar & desktop
        public TaskbarMode TaskbarMode { get; set; } = TaskbarMode.Normal;
        public bool TaskbarCentered { get; set; } = true;
        public bool ShowSearch { get; set; } = true;
        public bool ShowTaskView { get; set; } = true;
        public bool ShowWidgets { get; set; } = true;
        public bool ShowDesktopIcons { get; set; } = true;

        // Wallpaper
        public WallpaperSpec Wallpaper { get; set; } = new WallpaperSpec();

        // Dock
        public bool DockEnabled { get; set; }
        public ScreenEdge DockPosition { get; set; } = ScreenEdge.Bottom;
        public DockStyle DockStyle { get; set; } = DockStyle.Floating;
        public int DockIconSize { get; set; } = 48;
        public bool DockMagnify { get; set; } = true;
        public double DockMagnification { get; set; } = 1.7;
        public bool DockReserveSpace { get; set; } = true;
        public bool DockShowRunning { get; set; } = true;
        public bool DockShowSearch { get; set; } = true;
        /// <summary>Show open apps that aren't pinned, like the Windows taskbar does.</summary>
        public bool DockShowOpenApps { get; set; } = true;

        // Top bar
        public bool TopBarEnabled { get; set; }
        public TopBarStyle TopBarStyle { get; set; } = TopBarStyle.MacOS;
        public bool Clock24h { get; set; }
        public bool ClockShowDate { get; set; } = true;

        public ThemeSettings Clone()
        {
            var t = (ThemeSettings)MemberwiseClone();
            t.Wallpaper = Wallpaper.Clone();
            return t;
        }
    }

    public class DockItem
    {
        public string Name { get; set; }
        /// <summary>A file path (.exe, .lnk, folder, document) or a shell: location.</summary>
        public string Target { get; set; }
        public string Arguments { get; set; } = "";
    }

    /// <summary>What is saved to %AppData%\WindowsPersonalizer\settings.json.</summary>
    public class AppConfig
    {
        public ThemeSettings Theme { get; set; } = Presets.Windows11().Clone();
        public List<DockItem> DockItems { get; set; }
        public bool StartWithWindows { get; set; } = true;
        /// <summary>True once the user pressed Apply (dock/top bar should run).</summary>
        public bool Applied { get; set; }
        public bool ShownTrayHint { get; set; }
        /// <summary>Edge used to be pinned to the Dock by default; it is removed once from older setups.</summary>
        public bool EdgeDefaultRemoved { get; set; }
        /// <summary>Set once the Dock has been filled from the user's Windows taskbar.</summary>
        public bool ImportedTaskbarPins { get; set; }
    }
}
