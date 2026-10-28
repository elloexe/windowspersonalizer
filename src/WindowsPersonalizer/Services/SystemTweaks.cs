using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Media;
using WindowsPersonalizer.Model;
using static WindowsPersonalizer.Native.NativeMethods;

namespace WindowsPersonalizer.Services
{
    /// <summary>Writes a theme's Windows settings (colors, taskbar buttons, ...) to the registry.</summary>
    public static class SystemTweaks
    {
        /// <summary>Applies the registry part of a theme. Returns human-readable warnings for anything that failed.</summary>
        public static List<string> Apply(ThemeSettings t)
        {
            var warnings = new List<string>();

            void Try(string what, Action action)
            {
                try { action(); }
                catch (Exception ex) { warnings.Add($"{what}: {ex.Message}"); }
            }

            // Light / dark mode and transparency.
            Try("Light/dark mode", () =>
            {
                Reg.AppsUseLightTheme.SetDword(!t.DarkApps);
                Reg.SystemUsesLightTheme.SetDword(!t.DarkSystem);
            });
            Try("Transparency", () => Reg.EnableTransparency.SetDword(t.Transparency));

            // Accent color.
            Try("Accent color", () => SetAccent(ColorUtil.Parse(t.AccentColor, Color.FromRgb(0, 120, 212))));
            Try("Accent on taskbar", () => Reg.AccentOnTaskbar.SetDword(t.AccentOnTaskbar && t.DarkSystem));
            Try("Accent on title bars", () => Reg.AccentOnTitleBars.SetDword(t.AccentOnTitleBars));

            // Taskbar layout (Windows 11 settings; harmless on Windows 10).
            Try("Taskbar alignment", () => Reg.TaskbarAlignment.SetDword(t.TaskbarCentered));
            Try("Task View button", () => Reg.ShowTaskViewButton.SetDword(t.ShowTaskView));
            Try("Search box", () => Reg.SearchboxTaskbarMode.SetDword(t.ShowSearch ? 1 : 0));
            // Recent Windows 11 builds block writing this one; that is fine, it is cosmetic.
            Try("Widgets button", () => Reg.TaskbarWidgets.SetDword(t.ShowWidgets));

            Try("Taskbar visibility", () =>
            {
                switch (t.TaskbarMode)
                {
                    case TaskbarMode.Normal:
                        TaskbarService.SetHidden(false);
                        TaskbarService.SetAutoHide(false);
                        break;
                    case TaskbarMode.AutoHide:
                        TaskbarService.SetHidden(false);
                        TaskbarService.SetAutoHide(true);
                        break;
                    case TaskbarMode.Hidden:
                        TaskbarService.SetHidden(true);
                        break;
                }
            });

            Try("Desktop icons", () => DesktopIconsService.SetVisible(t.ShowDesktopIcons));

            Broadcast();
            return warnings;
        }

        private static void SetAccent(Color accent)
        {
            // Turn off "pick an accent color from my background" so ours sticks.
            Reg.AutoColorization.SetDword(0);

            // 8-entry palette, lightest to darkest, as RGBA bytes (what Settings writes).
            var shades = new[]
            {
                ColorUtil.Shade(accent, 0.60), ColorUtil.Shade(accent, 0.40), ColorUtil.Shade(accent, 0.20), accent,
                ColorUtil.Shade(accent, -0.20), ColorUtil.Shade(accent, -0.40), ColorUtil.Shade(accent, -0.60),
                Color.FromRgb(0x88, 0x17, 0x98),
            };
            var palette = new byte[32];
            for (int i = 0; i < 8; i++)
            {
                palette[i * 4] = shades[i].R;
                palette[i * 4 + 1] = shades[i].G;
                palette[i * 4 + 2] = shades[i].B;
                palette[i * 4 + 3] = 0xFF;
            }
            Reg.AccentPalette.SetBinary(palette);
            Reg.AccentColorMenu.SetDword(ColorUtil.ToAbgr(shades[4]));
            Reg.StartColorMenu.SetDword(ColorUtil.ToAbgr(shades[5]));
            Reg.DwmAccentColor.SetDword(ColorUtil.ToAbgr(accent));
            Reg.DwmColorizationColor.SetDword(ColorUtil.ToArgb(accent, 0xC4));
            Reg.DwmColorizationAfterglow.SetDword(ColorUtil.ToArgb(accent, 0xC4));
        }

        /// <summary>Tells Windows and running apps that theme settings changed.</summary>
        public static void Broadcast()
        {
            foreach (var area in new[] { "ImmersiveColorSet", "WindowsThemeElement", "TraySettings", "Policy", null })
                SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, area, SMTO_ABORTIFHUNG, 500, out _);
        }

        /// <summary>Restarts Explorer so every taskbar / accent change is picked up.</summary>
        public static void RestartExplorer()
        {
            foreach (var p in Process.GetProcessesByName("explorer"))
            {
                try { p.Kill(); p.WaitForExit(5000); }
                catch { /* already gone */ }
            }

            // Windows usually relaunches the shell by itself; start it if it did not.
            System.Threading.Thread.Sleep(1500);
            if (Process.GetProcessesByName("explorer").Length == 0)
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
        }
    }
}
