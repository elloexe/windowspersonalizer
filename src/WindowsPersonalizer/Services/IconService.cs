using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static WindowsPersonalizer.Native.NativeMethods;

namespace WindowsPersonalizer.Services
{
    /// <summary>Gets large, crisp icons for files, apps and shell locations.</summary>
    public static class IconService
    {
        private static readonly Dictionary<string, ImageSource> Cache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        private static readonly Guid IID_IShellItemImageFactory = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");

        public static ImageSource Get(string target, int size = 128)
        {
            string key = target + "|" + size;
            if (Cache.TryGetValue(key, out var cached)) return cached;
            string parsing = ShellActions.ToParsingName(target);
            var img = Load(parsing, size) ?? LoadAssociated(parsing) ?? Fallback();
            Cache[key] = img;
            return img;
        }

        /// <summary>True when the shell can resolve the target (used to skip default apps that are not installed).</summary>
        public static bool Exists(string target)
        {
            string p = ShellActions.ToParsingName(target);
            if (File.Exists(p) || Directory.Exists(p)) return true;
            var iid = IID_IShellItemImageFactory;
            int hr = SHCreateItemFromParsingName(p, IntPtr.Zero, ref iid, out var factory);
            if (factory != null) Marshal.ReleaseComObject(factory);
            return hr == 0;
        }

        private static ImageSource Load(string parsingName, int size)
        {
            IShellItemImageFactory factory = null;
            IntPtr hbmp = IntPtr.Zero;
            try
            {
                var iid = IID_IShellItemImageFactory;
                if (SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out factory) != 0 || factory == null)
                    return null;
                if (factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF.ICONONLY | SIIGBF.BIGGERSIZEOK, out hbmp) != 0)
                    return null;
                return FromHBitmap(hbmp);
            }
            catch
            {
                return null;
            }
            finally
            {
                if (hbmp != IntPtr.Zero) DeleteObject(hbmp);
                if (factory != null) Marshal.ReleaseComObject(factory);
            }
        }

        /// <summary>Copies a 32-bit HBITMAP keeping its alpha channel (Imaging.CreateBitmapSourceFromHBitmap drops it).</summary>
        private static BitmapSource FromHBitmap(IntPtr hbmp)
        {
            GetObject(hbmp, Marshal.SizeOf(typeof(BITMAP)), out BITMAP bm);
            int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
            var bi = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER)),
                biWidth = w,
                biHeight = -h, // negative = top-down rows
                biPlanes = 1,
                biBitCount = 32,
            };
            var pixels = new byte[w * h * 4];
            IntPtr dc = GetDC(IntPtr.Zero);
            try
            {
                GetDIBits(dc, hbmp, 0, (uint)h, pixels, ref bi, 0);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, dc);
            }

            // Some icons come back with no alpha at all; make those opaque instead of invisible.
            bool anyAlpha = false, anyColor = false;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (pixels[i + 3] != 0) anyAlpha = true;
                if (pixels[i] != 0 || pixels[i + 1] != 0 || pixels[i + 2] != 0) anyColor = true;
            }
            if (!anyAlpha && !anyColor)
                return null; // blank image: let the caller try another way
            if (!anyAlpha)
                for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

            var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, w * 4);
            src.Freeze();
            return src;
        }

        /// <summary>Older API that only gives 32px icons, but works for any file.</summary>
        private static ImageSource LoadAssociated(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon == null) return null;
                var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            catch
            {
                return null;
            }
        }

        private static ImageSource _fallback;

        private static ImageSource Fallback()
        {
            if (_fallback != null) return _fallback;
            var g = new DrawingGroup();
            using (var dc = g.Open())
            {
                dc.DrawRoundedRectangle(new LinearGradientBrush(Color.FromRgb(0x8E, 0x9E, 0xAB), Color.FromRgb(0x5D, 0x6D, 0x7E), 90),
                    null, new Rect(0, 0, 100, 100), 22, 22);
                dc.DrawRoundedRectangle(Brushes.White, null, new Rect(28, 28, 44, 44), 8, 8);
            }
            g.Freeze();
            _fallback = new DrawingImage(g);
            _fallback.Freeze();
            return _fallback;
        }

        /// <summary>A Launchpad-style "all apps" icon (grid of dots).</summary>
        public static ImageSource LauncherIcon(Color accent)
        {
            var g = new DrawingGroup();
            using (var dc = g.Open())
            {
                dc.DrawRoundedRectangle(new LinearGradientBrush(ColorUtil.Shade(accent, 0.25), ColorUtil.Shade(accent, -0.25), 90),
                    null, new Rect(0, 0, 100, 100), 24, 24);
                for (int r = 0; r < 3; r++)
                    for (int c = 0; c < 3; c++)
                        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)), null,
                            new Rect(22 + c * 21, 22 + r * 21, 14, 14), 4, 4);
            }
            g.Freeze();
            var img = new DrawingImage(g);
            img.Freeze();
            return img;
        }
    }
}
