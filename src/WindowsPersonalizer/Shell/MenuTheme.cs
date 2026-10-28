using System.Windows.Controls;
using System.Windows.Media;

namespace WindowsPersonalizer.Shell
{
    /// <summary>Creates context menus using the rounded style in Styles.xaml, in light or dark colors.</summary>
    internal static class MenuTheme
    {
        public static ContextMenu Create(bool dark, Color accent)
        {
            var m = new ContextMenu();
            if (dark)
            {
                m.Resources["Menu.Bg"] = new SolidColorBrush(Color.FromArgb(0xF5, 0x2B, 0x2B, 0x30));
                m.Resources["Menu.Border"] = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
                m.Resources["Menu.Fg"] = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF5));
                m.Resources["Menu.Separator"] = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            }
            m.Resources["Menu.Hover"] = new SolidColorBrush(accent);
            return m;
        }
    }
}
