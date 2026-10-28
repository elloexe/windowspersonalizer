using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using WindowsPersonalizer.Model;
using static WindowsPersonalizer.Native.NativeMethods;

namespace WindowsPersonalizer.Services
{
    /// <summary>Generates gradient wallpapers and sets the desktop background.</summary>
    public static class WallpaperService
    {
        public static string GetCurrentWallpaperPath()
        {
            var sb = new StringBuilder(1024);
            if (SystemParametersInfo(SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0))
                return sb.ToString();
            return Reg.Wallpaper.Get() as string;
        }

        public static void SetWallpaper(string path)
        {
            // 10 = "Fill" (scale to cover the screen, keep aspect ratio).
            Reg.WallpaperStyle.SetString("10");
            Reg.TileWallpaper.SetString("0");
            if (!SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE))
                throw new IOException("Windows refused to set the wallpaper.");
        }

        /// <summary>Applies the wallpaper part of a theme. Must run on the UI thread (uses WPF rendering).</summary>
        public static void Apply(WallpaperSpec spec, int widthPx, int heightPx)
        {
            switch (spec.Kind)
            {
                case WallpaperKind.Keep:
                    return;
                case WallpaperKind.Image:
                    if (!File.Exists(spec.ImagePath))
                        throw new FileNotFoundException("Wallpaper image not found.", spec.ImagePath);
                    SetWallpaper(spec.ImagePath);
                    return;
                case WallpaperKind.Generated:
                    Directory.CreateDirectory(ConfigStore.WallpaperFolder);
                    // A new file name each time, otherwise Windows may show its cached copy.
                    foreach (var old in Directory.GetFiles(ConfigStore.WallpaperFolder, "generated-*.png"))
                        try { File.Delete(old); } catch { /* in use */ }
                    string file = Path.Combine(ConfigStore.WallpaperFolder, $"generated-{DateTime.Now:yyyyMMddHHmmss}.png");
                    // Soft gradients look identical when Windows scales them up, and big blurs are slow,
                    // so render at most ~1080p and let the "Fill" mode stretch it to the screen.
                    double scale = Math.Min(1.0, 1920.0 / Math.Max(widthPx, heightPx));
                    if (spec.Style == WallpaperStyle.Linear || spec.Style == WallpaperStyle.Solid) scale = 1.0;
                    var bmp = Render(spec, Math.Max(1, (int)(widthPx * scale)), Math.Max(1, (int)(heightPx * scale)));
                    using (var fs = File.Create(file))
                    {
                        var enc = new PngBitmapEncoder();
                        enc.Frames.Add(BitmapFrame.Create(bmp));
                        enc.Save(fs);
                    }
                    SetWallpaper(file);
                    return;
            }
        }

        private static List<Color> ColorsOf(WallpaperSpec spec)
        {
            var list = spec.Colors.Select(h => ColorUtil.Parse(h, Colors.Transparent))
                                  .Where(c => c.A > 0).ToList();
            if (list.Count == 0) list.Add(Color.FromRgb(0x1E, 0x3C, 0x72));
            while (list.Count < 4) list.Add(list[list.Count % Math.Max(1, list.Count)]);
            return list;
        }

        /// <summary>Renders the wallpaper. Also used at small sizes for previews.</summary>
        public static BitmapSource Render(WallpaperSpec spec, int w, int h)
        {
            bool blur = spec.Style == WallpaperStyle.Mesh || spec.Style == WallpaperStyle.Waves;

            var content = new DrawingVisual();
            using (var dc = content.RenderOpen())
            {
                // Draw slightly oversized when blurring so the blurred edges stay fully opaque.
                if (blur) dc.PushTransform(new ScaleTransform(1.2, 1.2, w / 2.0, h / 2.0));
                Draw(dc, spec, w, h);
                if (blur) dc.Pop();
            }

            var root = new ContainerVisual();
            var baseLayer = new DrawingVisual();
            using (var dc = baseLayer.RenderOpen())
                dc.DrawRectangle(new SolidColorBrush(ColorsOf(spec)[0]), null, new Rect(0, 0, w, h));
            root.Children.Add(baseLayer);

            if (blur)
            {
                // Soft blur makes the shapes melt into a smooth, modern gradient.
                content.Effect = new BlurEffect
                {
                    Radius = Math.Max(2, Math.Min(w, h) * (spec.Style == WallpaperStyle.Mesh ? 0.10 : 0.015)),
                    KernelType = KernelType.Gaussian,
                    RenderingBias = RenderingBias.Quality,
                };
            }
            root.Children.Add(content);

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(root);
            rtb.Freeze();
            return rtb;
        }

        private static void Draw(DrawingContext dc, WallpaperSpec spec, double w, double h)
        {
            var c = ColorsOf(spec);
            var full = new Rect(0, 0, w, h);

            switch (spec.Style)
            {
                case WallpaperStyle.Solid:
                    dc.DrawRectangle(new SolidColorBrush(c[0]), null, full);
                    break;

                case WallpaperStyle.Linear:
                {
                    var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                    var distinct = spec.Colors.Count >= 2 ? c.Take(Math.Max(2, Math.Min(spec.Colors.Count, 4))).ToList() : c.Take(2).ToList();
                    for (int i = 0; i < distinct.Count; i++)
                        g.GradientStops.Add(new GradientStop(distinct[i], i / (double)(distinct.Count - 1)));
                    dc.DrawRectangle(g, null, full);
                    break;
                }

                case WallpaperStyle.Mesh:
                {
                    dc.DrawRectangle(new LinearGradientBrush(c[0], c[1], 45), null, full);
                    double r = Math.Max(w, h);
                    Blob(dc, c[1], new Point(w * 0.15, h * 0.2), r * 0.55);
                    Blob(dc, c[2], new Point(w * 0.85, h * 0.35), r * 0.5);
                    Blob(dc, c[3], new Point(w * 0.45, h * 0.95), r * 0.55);
                    Blob(dc, c[2], new Point(w * 0.05, h * 0.9), r * 0.3);
                    break;
                }

                case WallpaperStyle.Waves:
                {
                    var bg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                    bg.GradientStops.Add(new GradientStop(c[0], 0));
                    bg.GradientStops.Add(new GradientStop(c[1], 1));
                    dc.DrawRectangle(bg, null, full);

                    // Layered flowing waves, like recent macOS / Windows wallpapers.
                    for (int i = 0; i < 4; i++)
                    {
                        double baseY = h * (0.35 + i * 0.16);
                        double amp = h * (0.16 - i * 0.025);
                        var geo = new StreamGeometry();
                        using (var g = geo.Open())
                        {
                            g.BeginFigure(new Point(-w * 0.1, baseY), true, true);
                            g.BezierTo(new Point(w * 0.25, baseY - amp * (i % 2 == 0 ? 1.4 : -1.0)),
                                       new Point(w * 0.55, baseY + amp * 1.3),
                                       new Point(w * 0.8, baseY - amp * 0.4), true, true);
                            g.BezierTo(new Point(w * 0.95, baseY - amp), new Point(w * 1.05, baseY + amp * 0.5),
                                       new Point(w * 1.1, baseY), true, true);
                            g.LineTo(new Point(w * 1.1, h * 1.1), true, true);
                            g.LineTo(new Point(-w * 0.1, h * 1.1), true, true);
                        }
                        geo.Freeze();

                        Color from = c[(i + 1) % c.Count], to = c[(i + 2) % c.Count];
                        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                        brush.GradientStops.Add(new GradientStop(ColorUtil.WithAlpha(from, 220), 0));
                        brush.GradientStops.Add(new GradientStop(ColorUtil.WithAlpha(to, 200), 1));
                        dc.DrawGeometry(brush, null, geo);
                    }
                    break;
                }
            }
        }

        private static void Blob(DrawingContext dc, Color color, Point center, double radius)
        {
            var b = new RadialGradientBrush();
            b.GradientStops.Add(new GradientStop(ColorUtil.WithAlpha(color, 255), 0));
            b.GradientStops.Add(new GradientStop(ColorUtil.WithAlpha(color, 140), 0.5));
            b.GradientStops.Add(new GradientStop(ColorUtil.WithAlpha(color, 0), 1));
            dc.DrawEllipse(b, null, center, radius, radius);
        }
    }
}
