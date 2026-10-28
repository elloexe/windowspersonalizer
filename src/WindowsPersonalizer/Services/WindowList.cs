using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using static WindowsPersonalizer.Native.NativeMethods;

namespace WindowsPersonalizer.Services
{
    /// <summary>The app windows the Windows taskbar would show, grouped by app.</summary>
    public static class WindowList
    {
        public sealed class AppGroup
        {
            /// <summary>Lower-case exe name without extension, e.g. "chrome".</summary>
            public string Key;
            public string ExePath;
            public string Name;
            /// <summary>Windows in z-order, most recently used first.</summary>
            public List<IntPtr> Windows = new List<IntPtr>();
        }

        private const int GW_OWNER = 4;
        private const int DWMWA_CLOAKED = 14;
        private const uint WM_CLOSE = 0x0010;
        private const int SW_MINIMIZE = 6;

        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc proc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern void SwitchToThisWindow(IntPtr hWnd, bool altTab);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

        private static readonly Dictionary<string, string> NameCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> IgnoredClasses = new HashSet<string>
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
        };

        public static string KeyOf(string exePath) =>
            string.IsNullOrEmpty(exePath) ? null : Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();

        public static List<AppGroup> GetApps()
        {
            uint ownPid = (uint)Environment.ProcessId;
            var groups = new List<AppGroup>();
            var byKey = new Dictionary<string, AppGroup>();

            EnumWindows((h, _) =>
            {
                if (!IsAppWindow(h)) return true;
                GetWindowThreadProcessId(h, out uint pid);
                if (pid == ownPid) return true;

                string path = GetProcessPath(pid);
                // Store apps are hosted by ApplicationFrameHost; the real app owns a child CoreWindow.
                if (path != null && Path.GetFileName(path).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
                    path = HostedAppPath(h, pid) ?? path;

                string key = KeyOf(path);
                if (key == null) return true;
                if (!byKey.TryGetValue(key, out var g))
                {
                    g = new AppGroup { Key = key, ExePath = path, Name = FriendlyName(path, h) };
                    byKey[key] = g;
                    groups.Add(g);
                }
                g.Windows.Add(h);
                return true;
            }, IntPtr.Zero);

            return groups;
        }

        private static bool IsAppWindow(IntPtr h)
        {
            if (!IsWindowVisible(h) || GetWindow(h, GW_OWNER) != IntPtr.Zero) return false;
            long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
            if ((ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0) return false;
            if ((ex & WS_EX_NOACTIVATE) != 0 && (ex & WS_EX_APPWINDOW) == 0) return false;
            if (GetWindowTextLength(h) == 0) return false;

            // Cloaked = hidden by Windows (other virtual desktop, suspended store app).
            try
            {
                if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
            }
            catch (DllNotFoundException) { }

            var cls = new StringBuilder(64);
            GetClassName(h, cls, cls.Capacity);
            return !IgnoredClasses.Contains(cls.ToString());
        }

        private static string HostedAppPath(IntPtr frame, uint framePid)
        {
            string found = null;
            EnumChildWindows(frame, (c, _) =>
            {
                GetWindowThreadProcessId(c, out uint pid);
                if (pid != framePid)
                {
                    found = GetProcessPath(pid);
                    return found == null;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        public static string FriendlyName(string path, IntPtr window = default)
        {
            if (string.IsNullOrEmpty(path)) return "App";
            if (NameCache.TryGetValue(path, out var cached)) return cached;
            string name;
            if (Path.GetFileName(path).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
                name = "File Explorer";
            else
            {
                try { name = FileVersionInfo.GetVersionInfo(path).FileDescription; }
                catch { name = null; }
                if (string.IsNullOrWhiteSpace(name) && window != IntPtr.Zero)
                {
                    var t = new StringBuilder(256);
                    GetWindowText(window, t, t.Capacity);
                    name = t.ToString();
                }
                if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(path);
            }
            name = name.Trim();
            if (name.Length > 40) name = name.Substring(0, 40) + "…";
            NameCache[path] = name;
            return name;
        }

        /// <summary>
        /// Taskbar-style click: bring the app's latest window to the front; if it's already in
        /// front, minimize it. Returns false if the app has no windows.
        /// </summary>
        public static bool ActivateOrMinimize(AppGroup g)
        {
            if (g == null || g.Windows.Count == 0) return false;
            IntPtr top = g.Windows[0];
            if (GetForegroundWindow() == top && !IsIconic(top))
            {
                ShowWindow(top, SW_MINIMIZE);
                return true;
            }
            if (IsIconic(top)) ShowWindow(top, SW_RESTORE);
            if (!SetForegroundWindow(top))
                SwitchToThisWindow(top, true);
            return true;
        }

        public static void Close(AppGroup g)
        {
            foreach (var h in g.Windows)
                PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        public static AppGroup Find(List<AppGroup> apps, string key) =>
            key == null ? null : apps.FirstOrDefault(a => a.Key == key);
    }
}
