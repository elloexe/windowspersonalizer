using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WindowsPersonalizer.Native;
using static WindowsPersonalizer.Native.NativeMethods;

namespace WindowsPersonalizer.Shell
{
    /// <summary>
    /// An empty window that only provides the frosted-glass blur under the dock's background.
    /// It is kept exactly the size of that background (rounded corners included) while the
    /// dock's icon window, which is larger and mostly transparent, sits on top of it.
    /// </summary>
    internal sealed class BackdropWindow : Window
    {
        private IntPtr _hwnd;
        private int _x, _y, _w, _h, _r;

        public bool BlurEnabled { get; private set; }

        public BackdropWindow()
        {
            Title = "Dock backdrop";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            // Practically invisible, but not fully transparent so the window keeps a surface.
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Left = -32000;
            Top = -32000;
            Width = 1;
            Height = 1;

            SourceInitialized += (_, __) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                long ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
                ex |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
                SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(ex));
                BlurEnabled = EnableBlurBehind(_hwnd);
                try
                {
                    int round = DWMWCP_ROUND;
                    DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
                }
                catch (Exception)
                {
                    // Windows 10: no rounded-corner attribute; the region below handles it.
                }
            };
        }

        /// <summary>Moves/resizes in physical pixels and clips to a rounded rectangle.</summary>
        public void SetBounds(int x, int y, int w, int h, int radius)
        {
            if (_hwnd == IntPtr.Zero || w <= 0 || h <= 0) return;
            if (x == _x && y == _y && w == _w && h == _h && radius == _r) return;
            bool sizeChanged = w != _w || h != _h || radius != _r;
            _x = x; _y = y; _w = w; _h = h; _r = radius;

            SetWindowPos(_hwnd, IntPtr.Zero, x, y, w, h, SWP_NOZORDER | SWP_NOACTIVATE);
            if (sizeChanged && radius > 0)
                SetWindowRgn(_hwnd, CreateRoundRectRgn(0, 0, w + 1, h + 1, radius * 2, radius * 2), true);
        }
    }
}
