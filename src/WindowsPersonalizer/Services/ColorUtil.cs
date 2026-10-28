using System;
using System.Globalization;
using System.Windows.Media;

namespace WindowsPersonalizer.Services
{
    public static class ColorUtil
    {
        public static bool TryParse(string hex, out Color color)
        {
            color = Colors.Black;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            hex = hex.Trim().TrimStart('#');
            if (hex.Length == 3)
                hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
            if (hex.Length != 6 && hex.Length != 8) return false;
            if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v)) return false;
            if (hex.Length == 6) v |= 0xFF000000;
            color = Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
            return true;
        }

        public static Color Parse(string hex, Color fallback) => TryParse(hex, out var c) ? c : fallback;

        public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        /// <summary>amount &gt; 0 mixes toward white, &lt; 0 toward black.</summary>
        public static Color Shade(Color c, double amount)
        {
            byte Mix(byte ch) => amount >= 0
                ? (byte)Math.Round(ch + (255 - ch) * amount)
                : (byte)Math.Round(ch * (1 + amount));
            return Color.FromArgb(c.A, Mix(c.R), Mix(c.G), Mix(c.B));
        }

        public static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

        /// <summary>Windows stores many colors as 0xAABBGGRR.</summary>
        public static int ToAbgr(Color c) => unchecked((int)(0xFF000000u | ((uint)c.B << 16) | ((uint)c.G << 8) | c.R));

        public static int ToArgb(Color c, byte alpha) =>
            unchecked((int)(((uint)alpha << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B));

        public static bool IsLight(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 160;
    }
}
