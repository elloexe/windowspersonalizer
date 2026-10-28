using System;
using System.Text;
using static WindowsPersonalizer.Native.NativeMethods;

namespace WindowsPersonalizer.Services
{
    /// <summary>Shows or hides the desktop icons live (no Explorer restart needed).</summary>
    public static class DesktopIconsService
    {
        private static IntPtr FindDesktopListView()
        {
            IntPtr defView = IntPtr.Zero;
            IntPtr progman = FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
                defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

            if (defView == IntPtr.Zero)
            {
                // With some wallpaper modes the icons live under a WorkerW window instead.
                EnumWindows((h, _) =>
                {
                    var sb = new StringBuilder(32);
                    GetClassName(h, sb, sb.Capacity);
                    if (sb.ToString() == "WorkerW")
                    {
                        var dv = FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
                        if (dv != IntPtr.Zero)
                        {
                            defView = dv;
                            return false;
                        }
                    }
                    return true;
                }, IntPtr.Zero);
            }

            return defView == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
        }

        public static void SetVisible(bool visible)
        {
            try
            {
                Reg.HideDesktopIcons.SetDword(!visible);
            }
            catch
            {
                // Ignore; the live toggle below still works for this session.
            }

            var lv = FindDesktopListView();
            if (lv != IntPtr.Zero)
                ShowWindow(lv, visible ? SW_SHOW : SW_HIDE);
        }
    }
}
