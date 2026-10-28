using System;

namespace WindowsPersonalizer.Native
{
    /// <summary>Converts between WPF device-independent units and physical pixels (system DPI).</summary>
    internal static class Dpi
    {
        private static double _scale;

        public static double Scale
        {
            get
            {
                if (_scale > 0) return _scale;
                IntPtr dc = NativeMethods.GetDC(IntPtr.Zero);
                try
                {
                    int dpi = NativeMethods.GetDeviceCaps(dc, NativeMethods.LOGPIXELSX);
                    _scale = dpi > 0 ? dpi / 96.0 : 1.0;
                }
                finally
                {
                    NativeMethods.ReleaseDC(IntPtr.Zero, dc);
                }
                return _scale;
            }
        }

        public static double ToDip(int px) => px / Scale;
        public static int ToPx(double dip) => (int)Math.Round(dip * Scale);

        public static int ScreenWidthPx => NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        public static int ScreenHeightPx => NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
    }
}
