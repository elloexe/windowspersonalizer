using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using WindowsPersonalizer.Model;
using static WindowsPersonalizer.Native.NativeMethods;

namespace WindowsPersonalizer.Services
{
    /// <summary>Launching apps, switching to them, Windows key shortcuts and power actions.</summary>
    public static class ShellActions
    {
        public const string RecycleBin = "shell:RecycleBinFolder";

        /// <summary>Converts "shell:" style targets into something SHCreateItemFromParsingName accepts.</summary>
        public static string ToParsingName(string target)
        {
            if (string.Equals(target, RecycleBin, StringComparison.OrdinalIgnoreCase))
                return "::{645FF040-5081-101B-9F08-00AA002F954E}";
            return Environment.ExpandEnvironmentVariables(target ?? "");
        }

        public static void Launch(DockItem item)
        {
            string target = Environment.ExpandEnvironmentVariables(item.Target);
            ProcessStartInfo psi;
            if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                psi = new ProcessStartInfo("explorer.exe", target);
            else
                psi = new ProcessStartInfo(target, item.Arguments ?? "");
            psi.UseShellExecute = true;
            if (File.Exists(target))
                psi.WorkingDirectory = Path.GetDirectoryName(target);
            Process.Start(psi);
        }

        /// <summary>The process name a dock item corresponds to (for running dots), or null.</summary>
        public static string ProcessNameOf(DockItem item)
        {
            string t = Environment.ExpandEnvironmentVariables(item.Target ?? "");
            if (t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return Path.GetFileNameWithoutExtension(t);
            if (t.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                string resolved = ShortcutResolver.GetTarget(t);
                if (resolved != null && resolved.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    return Path.GetFileNameWithoutExtension(resolved);
            }
            return null;
        }

        /// <summary>Brings an app's existing window to the front. Returns false if it has none.</summary>
        public static bool Activate(string processName)
        {
            if (string.IsNullOrEmpty(processName) ||
                processName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                return false;

            foreach (var p in Process.GetProcessesByName(processName))
            {
                try
                {
                    IntPtr h = p.MainWindowHandle;
                    if (h == IntPtr.Zero) continue;
                    if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
                    SetForegroundWindow(h);
                    return true;
                }
                catch
                {
                    // Process exited or access denied: try the next one.
                }
            }
            return false;
        }

        public static HashSet<string> RunningProcessNames()
        {
            // Processes that own at least one visible top-level window.
            var pids = new HashSet<uint>();
            EnumWindows((h, _) =>
            {
                if (IsWindowVisible(h))
                {
                    GetWindowThreadProcessId(h, out uint pid);
                    pids.Add(pid);
                }
                return true;
            }, IntPtr.Zero);

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (pids.Contains((uint)p.Id)) set.Add(p.ProcessName);
                }
                catch { }
                finally { p.Dispose(); }
            }
            return set;
        }

        /// <summary>Presses Win (+ an optional key), e.g. Win alone opens Start, Win+A opens Quick Settings.</summary>
        public static void WinKey(char? key = null)
        {
            keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            if (key.HasValue)
            {
                byte vk = (byte)char.ToUpperInvariant(key.Value);
                keybd_event(vk, 0, 0, UIntPtr.Zero);
                keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        public static void OpenStart() => WinKey();
        public static void OpenSearch() => WinKey('S');
        public static void OpenQuickSettings() => WinKey('A');
        public static void OpenNotifications() => WinKey('N');

        public static void OpenTaskView()
        {
            // Win+Tab
            keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            keybd_event(0x09, 0, 0, UIntPtr.Zero);
            keybd_event(0x09, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        public static void Open(string uriOrPath)
        {
            try
            {
                Process.Start(new ProcessStartInfo(uriOrPath) { UseShellExecute = true });
            }
            catch
            {
                // Nothing registered for it; ignore.
            }
        }

        public static void Sleep() => System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Suspend, false, false);
        public static void Lock() => LockWorkStation();
        public static void Restart() => Run("shutdown", "/r /t 0");
        public static void ShutDown() => Run("shutdown", "/s /t 0");
        public static void SignOut() => Run("shutdown", "/l");

        private static void Run(string exe, string args) =>
            Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false });

        /// <summary>Apps pinned to a fresh dock, keeping only the ones installed on this PC.</summary>
        public static List<DockItem> DefaultDockItems()
        {
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var candidates = new List<DockItem>
            {
                new DockItem { Name = "File Explorer", Target = Path.Combine(win, "explorer.exe") },
                new DockItem { Name = "Microsoft Edge", Target = Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe") },
                new DockItem { Name = "Google Chrome", Target = Path.Combine(pf, @"Google\Chrome\Application\chrome.exe") },
                new DockItem { Name = "Google Chrome", Target = Path.Combine(local, @"Google\Chrome\Application\chrome.exe") },
                new DockItem { Name = "Firefox", Target = Path.Combine(pf, @"Mozilla Firefox\firefox.exe") },
                new DockItem { Name = "Mail", Target = @"shell:AppsFolder\microsoft.windowscommunicationsapps_8wekyb3d8bbwe!microsoft.windowslive.mail" },
                new DockItem { Name = "Calendar", Target = @"shell:AppsFolder\microsoft.windowscommunicationsapps_8wekyb3d8bbwe!microsoft.windowslive.calendar" },
                new DockItem { Name = "Photos", Target = @"shell:AppsFolder\Microsoft.Windows.Photos_8wekyb3d8bbwe!App" },
                new DockItem { Name = "Media Player", Target = @"shell:AppsFolder\Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic" },
                new DockItem { Name = "Notepad", Target = Path.Combine(win, @"System32\notepad.exe") },
                new DockItem { Name = "Calculator", Target = @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" },
                new DockItem { Name = "Terminal", Target = @"shell:AppsFolder\Microsoft.WindowsTerminal_8wekyb3d8bbwe!App" },
                new DockItem { Name = "Microsoft Store", Target = @"shell:AppsFolder\Microsoft.WindowsStore_8wekyb3d8bbwe!App" },
                new DockItem { Name = "Settings", Target = @"shell:AppsFolder\windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel" },
            };

            var result = new List<DockItem>();
            foreach (var c in candidates)
            {
                if (result.Any(r => r.Name == c.Name)) continue;
                bool ok;
                try { ok = IconService.Exists(c.Target); }
                catch { ok = false; }
                if (ok) result.Add(c);
            }
            return result;
        }
    }
}
