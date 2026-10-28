using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace WindowsPersonalizer.Services
{
    /// <summary>A single HKCU registry value that this app may change.</summary>
    public sealed class RegValue
    {
        public string Key { get; }
        public string Name { get; }

        public RegValue(string key, string name)
        {
            Key = key;
            Name = name;
        }

        public void SetDword(int value)
        {
            using var k = Registry.CurrentUser.CreateSubKey(Key, true);
            k.SetValue(Name, value, RegistryValueKind.DWord);
        }

        public void SetDword(bool value) => SetDword(value ? 1 : 0);

        public void SetString(string value)
        {
            using var k = Registry.CurrentUser.CreateSubKey(Key, true);
            k.SetValue(Name, value, RegistryValueKind.String);
        }

        public void SetBinary(byte[] value)
        {
            using var k = Registry.CurrentUser.CreateSubKey(Key, true);
            k.SetValue(Name, value, RegistryValueKind.Binary);
        }

        public object Get()
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key, false);
            return k?.GetValue(Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }
    }

    /// <summary>Every registry value the app touches. All under HKEY_CURRENT_USER (no admin needed).</summary>
    public static class Reg
    {
        private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string Dwm = @"Software\Microsoft\Windows\DWM";
        private const string Accent = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
        private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        private const string Search = @"Software\Microsoft\Windows\CurrentVersion\Search";
        private const string Desktop = @"Control Panel\Desktop";

        public static readonly RegValue AppsUseLightTheme = new RegValue(Personalize, "AppsUseLightTheme");
        public static readonly RegValue SystemUsesLightTheme = new RegValue(Personalize, "SystemUsesLightTheme");
        public static readonly RegValue EnableTransparency = new RegValue(Personalize, "EnableTransparency");
        public static readonly RegValue AccentOnTaskbar = new RegValue(Personalize, "ColorPrevalence");

        public static readonly RegValue DwmAccentColor = new RegValue(Dwm, "AccentColor");
        public static readonly RegValue DwmColorizationColor = new RegValue(Dwm, "ColorizationColor");
        public static readonly RegValue DwmColorizationAfterglow = new RegValue(Dwm, "ColorizationAfterglow");
        public static readonly RegValue AccentOnTitleBars = new RegValue(Dwm, "ColorPrevalence");

        public static readonly RegValue AccentColorMenu = new RegValue(Accent, "AccentColorMenu");
        public static readonly RegValue StartColorMenu = new RegValue(Accent, "StartColorMenu");
        public static readonly RegValue AccentPalette = new RegValue(Accent, "AccentPalette");

        public static readonly RegValue AutoColorization = new RegValue(Desktop, "AutoColorization");
        public static readonly RegValue Wallpaper = new RegValue(Desktop, "Wallpaper");
        public static readonly RegValue WallpaperStyle = new RegValue(Desktop, "WallpaperStyle");
        public static readonly RegValue TileWallpaper = new RegValue(Desktop, "TileWallpaper");

        public static readonly RegValue TaskbarAlignment = new RegValue(Advanced, "TaskbarAl");
        public static readonly RegValue ShowTaskViewButton = new RegValue(Advanced, "ShowTaskViewButton");
        public static readonly RegValue TaskbarWidgets = new RegValue(Advanced, "TaskbarDa");
        public static readonly RegValue HideDesktopIcons = new RegValue(Advanced, "HideIcons");

        public static readonly RegValue SearchboxTaskbarMode = new RegValue(Search, "SearchboxTaskbarMode");

        public static IReadOnlyList<RegValue> All { get; } = new List<RegValue>
        {
            AppsUseLightTheme, SystemUsesLightTheme, EnableTransparency, AccentOnTaskbar,
            DwmAccentColor, DwmColorizationColor, DwmColorizationAfterglow, AccentOnTitleBars,
            AccentColorMenu, StartColorMenu, AccentPalette,
            AutoColorization, Wallpaper, WallpaperStyle, TileWallpaper,
            TaskbarAlignment, ShowTaskViewButton, TaskbarWidgets, HideDesktopIcons,
            SearchboxTaskbarMode,
        };

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunName = "WindowsPersonalizer";

        public static void SetStartWithWindows(bool enabled)
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (enabled && !string.IsNullOrEmpty(Environment.ProcessPath))
                k.SetValue(RunName, $"\"{Environment.ProcessPath}\" --background");
            else
                k.DeleteValue(RunName, false);
        }
    }
}
