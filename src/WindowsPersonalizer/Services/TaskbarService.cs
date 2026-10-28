using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using static WindowsPersonalizer.Native.NativeMethods;

namespace WindowsPersonalizer.Services
{
    /// <summary>Controls the real Windows taskbar: auto-hide, and fully hiding it while a dock replaces it.</summary>
    public static class TaskbarService
    {
        private static DispatcherTimer _keepHiddenTimer;

        private static IntPtr MainTaskbar => FindWindow("Shell_TrayWnd", null);

        private static List<IntPtr> AllTaskbars()
        {
            var list = new List<IntPtr>();
            var main = MainTaskbar;
            if (main != IntPtr.Zero) list.Add(main);
            EnumWindows((h, _) =>
            {
                var sb = new StringBuilder(64);
                GetClassName(h, sb, sb.Capacity);
                if (sb.ToString() == "Shell_SecondaryTrayWnd") list.Add(h);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        private static APPBARDATA TaskbarData()
        {
            var abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
            abd.hWnd = MainTaskbar;
            return abd;
        }

        public static bool IsAutoHide()
        {
            var abd = TaskbarData();
            var state = (long)SHAppBarMessage(ABM_GETSTATE, ref abd).ToUInt64();
            return (state & ABS_AUTOHIDE) != 0;
        }

        /// <summary>Same as the "Automatically hide the taskbar" switch in Settings; applies instantly.</summary>
        public static void SetAutoHide(bool autoHide)
        {
            var abd = TaskbarData();
            abd.lParam = (IntPtr)(autoHide ? ABS_AUTOHIDE : ABS_ALWAYSONTOP);
            SHAppBarMessage(ABM_SETSTATE, ref abd);
        }

        /// <summary>
        /// Completely hides (or shows) the taskbar on every monitor. Windows does not remember
        /// this, so while hidden a timer re-hides it if Explorer brings it back (e.g. after a restart).
        /// </summary>
        public static void SetHidden(bool hidden)
        {
            if (hidden)
            {
                // Auto-hide first so Windows gives the reserved space back to apps.
                SetAutoHide(true);
                HideNow();
                if (_keepHiddenTimer == null)
                {
                    _keepHiddenTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                    _keepHiddenTimer.Tick += (_, __) => HideNow();
                }
                _keepHiddenTimer.Start();
            }
            else
            {
                _keepHiddenTimer?.Stop();
                foreach (var h in AllTaskbars())
                    ShowWindow(h, SW_SHOW);
            }
        }

        public static bool IsHiddenByUs => _keepHiddenTimer?.IsEnabled == true;

        private static void HideNow()
        {
            foreach (var h in AllTaskbars())
                if (IsWindowVisible(h))
                    ShowWindow(h, SW_HIDE);
        }
    }
}
