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
                DrawTile(dc, Color.FromRgb(0x9A, 0xA4, 0xB2), Color.FromRgb(0x5F, 0x6A, 0x7A));
                // A little app window glyph.
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)), null, new Rect(28, 32, 44, 36), 5, 5);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x5F, 0x6A, 0x7A)), null, new Rect(33, 42, 34, 21), 2, 2);
            }
            _fallback = Freeze(g);
            return _fallback;
        }

        // App-icon shape: rounded square inset a little (like macOS icons), with a soft top highlight.
        private static readonly Rect Tile = new Rect(7, 7, 86, 86);
        private const double TileRadius = 21;

        private static void DrawTile(DrawingContext dc, Color top, Color bottom, Color? outline = null)
        {
            dc.DrawRoundedRectangle(new LinearGradientBrush(top, bottom, 90), null, Tile, TileRadius, TileRadius);
            var gloss = new LinearGradientBrush(Color.FromArgb(70, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90);
            dc.DrawRoundedRectangle(gloss, null, new Rect(Tile.X, Tile.Y, Tile.Width, Tile.Height * 0.55), TileRadius, TileRadius);
            dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(outline ?? Color.FromArgb(40, 0, 0, 0)), 1),
                Tile, TileRadius, TileRadius);
        }

        private static ImageSource Freeze(DrawingGroup g)
        {
            // Pin the drawing to a 100x100 box so tiles line up with real app icons.
            using (var dc = g.Append())
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 100, 100));
            g.Freeze();
            var img = new DrawingImage(g);
            img.Freeze();
            return img;
        }

        /// <summary>Spotlight-style search icon: light tile with a dark magnifying glass.</summary>
        public static ImageSource SearchIcon(Color accent)
        {
            var g = new DrawingGroup();
            using (var dc = g.Open())
            {
                DrawTile(dc, Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xE4, 0xE4, 0xEA), Color.FromArgb(55, 0, 0, 0));
                var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x42)), 7.5)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                };
                dc.DrawEllipse(null, pen, new Point(45, 45), 16, 16);
                dc.DrawLine(pen, new Point(57, 57), new Point(70, 70));
            }
            return Freeze(g);
        }

        /// <summary>Launchpad-style "all apps" icon: dark tile with a grid of colorful app squares.</summary>
        public static ImageSource LauncherIcon(Color accent)
        {
            var colors = new[]
            {
                Color.FromRgb(0xFF, 0x5F, 0x57), Color.FromRgb(0xFF, 0x9F, 0x0A), Color.FromRgb(0xFF, 0xD6, 0x0A),
                Color.FromRgb(0x30, 0xD1, 0x58), Color.FromRgb(0x64, 0xD2, 0xFF), Color.FromRgb(0x0A, 0x84, 0xFF),
                Color.FromRgb(0x5E, 0x5C, 0xE6), Color.FromRgb(0xBF, 0x5A, 0xF2), Color.FromRgb(0xFF, 0x37, 0x5F),
            };
            var g = new DrawingGroup();
            using (var dc = g.Open())
            {
                DrawTile(dc, Color.FromRgb(0x6B, 0x6B, 0x75), Color.FromRgb(0x2E, 0x2E, 0x35));
                for (int r = 0; r < 3; r++)
                    for (int c = 0; c < 3; c++)
                    {
                        var col = colors[r * 3 + c];
                        var brush = new LinearGradientBrush(ColorUtil.Shade(col, 0.15), ColorUtil.Shade(col, -0.1), 90);
                        dc.DrawRoundedRectangle(brush, null, new Rect(25 + c * 18, 25 + r * 18, 14, 14), 4, 4);
                    }
            }
            return Freeze(g);
        }
    }
}
