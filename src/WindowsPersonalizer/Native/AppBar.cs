using System;
using System.Windows;
using System.Windows.Interop;
using static WindowsPersonalizer.Native.NativeMethods;

namespace WindowsPersonalizer.Native
{
    public enum ScreenEdge
    {
        Left = 0,
        Top = 1,
        Right = 2,
        Bottom = 3,
    }

    /// <summary>
    /// Registers a WPF window as a shell "AppBar" so Windows reserves screen space for it
    /// (maximized windows stop at its edge, exactly like the real taskbar).
    /// </summary>
    internal sealed class AppBar : IDisposable
    {
        private static readonly uint TaskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");

        private readonly Window _window;
        private readonly ScreenEdge _edge;
        private readonly int _thicknessPx;
        private readonly bool _reserve;
        private HwndSource _source;
        private uint _callbackMsg;
        private bool _registered;

        /// <summary>Raised with the final rectangle (physical pixels) after (re)positioning.</summary>
        public event Action<RECT> Positioned;

        /// <summary>Raised when Explorer restarts (the taskbar is re-created).</summary>
        public event Action ExplorerRestarted;

        /// <summary>Raised when a full-screen app (video, game) opens (true) or closes (false).</summary>
        public event Action<bool> FullScreenChanged;

        public AppBar(Window window, ScreenEdge edge, double thicknessDip, bool reserveSpace)
        {
            _window = window;
            _edge = edge;
            _thicknessPx = Dpi.ToPx(thicknessDip);
            _reserve = true;
            // Even when not reserving space we still register (with zero thickness): the shell then
            // tells us where the taskbar is and when full-screen apps open.
            if (!reserveSpace) _thicknessPx = 0;
        }

        /// <summary>Call after the window's handle exists (SourceInitialized).</summary>
        public void Attach()
        {
            var hwnd = new WindowInteropHelper(_window).Handle;
            _source = HwndSource.FromHwnd(hwnd);
            _source.AddHook(WndProc);
            Register();
        }

        private IntPtr Hwnd => new WindowInteropHelper(_window).Handle;

        private void Register()
        {
            if (_reserve && !_registered)
            {
                _callbackMsg = RegisterWindowMessage("WindowsPersonalizerAppBar_" + Guid.NewGuid());
                var abd = NewData();
                abd.uCallbackMessage = _callbackMsg;
                SHAppBarMessage(ABM_NEW, ref abd);
                _registered = true;
            }
            UpdatePosition();
        }

        private APPBARDATA NewData()
        {
            var abd = new APPBARDATA();
            abd.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(APPBARDATA));
            abd.hWnd = Hwnd;
            abd.uEdge = (uint)_edge;
            return abd;
        }

        public void UpdatePosition()
        {
            int w = Dpi.ScreenWidthPx, h = Dpi.ScreenHeightPx;
            var abd = NewData();
            abd.rc = new RECT { Left = 0, Top = 0, Right = w, Bottom = h };
            switch (_edge)
            {
                case ScreenEdge.Top: abd.rc.Bottom = _thicknessPx; break;
                case ScreenEdge.Bottom: abd.rc.Top = h - _thicknessPx; break;
                case ScreenEdge.Left: abd.rc.Right = _thicknessPx; break;
                case ScreenEdge.Right: abd.rc.Left = w - _thicknessPx; break;
            }

            if (_registered)
            {
                // Let the shell move us past the taskbar and other app bars, then fix the thickness.
                SHAppBarMessage(ABM_QUERYPOS, ref abd);
                switch (_edge)
                {
                    case ScreenEdge.Top: abd.rc.Bottom = abd.rc.Top + _thicknessPx; break;
                    case ScreenEdge.Bottom: abd.rc.Top = abd.rc.Bottom - _thicknessPx; break;
                    case ScreenEdge.Left: abd.rc.Right = abd.rc.Left + _thicknessPx; break;
                    case ScreenEdge.Right: abd.rc.Left = abd.rc.Right - _thicknessPx; break;
                }
                SHAppBarMessage(ABM_SETPOS, ref abd);
            }

            Positioned?.Invoke(abd.rc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_registered && msg == _callbackMsg)
            {
                int code = wParam.ToInt32();
                if (code == ABN_POSCHANGED)
                    UpdatePosition();
                else if (code == ABN_FULLSCREENAPP)
                    FullScreenChanged?.Invoke(lParam != IntPtr.Zero);
            }
            else if (msg == TaskbarCreatedMsg)
            {
                // Explorer restarted: our registration is gone, register again.
                _registered = false;
                Register();
                ExplorerRestarted?.Invoke();
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_registered)
            {
                var abd = NewData();
                SHAppBarMessage(ABM_REMOVE, ref abd);
                _registered = false;
            }
            _source?.RemoveHook(WndProc);
            _source = null;
        }
    }
}
