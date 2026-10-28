using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WindowsPersonalizer.Model;
using WindowsPersonalizer.Native;
using WindowsPersonalizer.Services;
using WinForms = System.Windows.Forms;

namespace WindowsPersonalizer
{
    public partial class MainWindow : Window
    {
        private static readonly string[] AccentChoices =
        {
            "#0078D4", "#007AFF", "#0A84FF", "#5B5BD6", "#8E44AD", "#BF5AF2", "#FF2D55", "#E81123",
            "#E95420", "#FF9500", "#FFCC00", "#8BB158", "#34C759", "#00B294", "#3DAEE9", "#1A73E8",
            "#69797E", "#2D2D30",
        };

        private readonly AppConfig _config;
        private readonly DispatcherTimer _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        private readonly Random _random = new Random();
        private string _selectedPreset;
        private bool _loading;

        public MainWindow()
        {
            InitializeComponent();
            _config = App.Current.Config;
            _config.DockItems ??= ShellActions.DefaultDockItems();

            _previewTimer.Tick += (_, __) => { _previewTimer.Stop(); RefreshWallpaperPreview(); };

            BuildPresetCards();
            BuildAccentSwatches();
            LoadToUi(_config.Theme);
            RefreshDockList();
            StartWithWindows.IsChecked = _config.StartWithWindows;
            UpdateBackupText();
            ShowPage("Themes");

            // Apps may have been dragged onto the Dock while this window was in the background.
            Activated += (_, __) =>
            {
                if (DockList.Items.Count != _config.DockItems.Count) RefreshDockList();
            };
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            Dispatcher.BeginInvoke(new Action(() => App.Current.OnMainWindowClosed()));
        }

        // ---- Navigation -----------------------------------------------------------------

        private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Nav.SelectedItem is ListBoxItem item && PageThemes != null)
                ShowPage((string)item.Tag);
        }

        private void ShowPage(string tag)
        {
            var pages = new Dictionary<string, (FrameworkElement page, string title, string hint)>
            {
                ["Themes"] = (PageThemes, "Themes", "Pick a look to start from, tweak anything you like, then press Apply."),
                ["Appearance"] = (PageAppearance, "Colors & mode", "Light or dark, and the accent color used across Windows."),
                ["Taskbar"] = (PageTaskbar, "Taskbar & desktop", "Move, shrink or completely hide the Windows taskbar."),
                ["Dock"] = (PageDock, "Dock", "A macOS / Linux style app dock with magnifying icons."),
                ["TopBar"] = (PageTopBar, "Top bar", "A menu bar across the top of the screen, macOS or GNOME style."),
                ["Wallpaper"] = (PageWallpaper, "Wallpaper", "Generate a matching wallpaper or use your own picture."),
            };
            foreach (var p in pages.Values) p.page.Visibility = Visibility.Collapsed;
            var sel = pages[tag];
            sel.page.Visibility = Visibility.Visible;
            PageTitle.Text = sel.title;
            PageHint.Text = sel.hint;
            if (tag == "Wallpaper") RefreshWallpaperPreview();
        }

        // ---- Preset gallery ------------------------------------------------------------

        private void BuildPresetCards()
        {
            PresetPanel.Children.Clear();
            foreach (var preset in Presets.All)
            {
                var p = preset;
                var card = new Border
                {
                    Width = 236,
                    Margin = new Thickness(0, 0, 14, 14),
                    Padding = new Thickness(10),
                    CornerRadius = new CornerRadius(12),
                    Background = Brushes.White,
                    BorderThickness = new Thickness(2),
                    BorderBrush = (Brush)FindResource("CardBorder"),
                    Cursor = Cursors.Hand,
                    Tag = p.Name,
                };
                var stack = new StackPanel();
                stack.Children.Add(BuildPreview(p, 212, 120));
                stack.Children.Add(new TextBlock
                {
                    Text = p.Name,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 14,
                    Margin = new Thickness(2, 10, 0, 2),
                    Foreground = (Brush)FindResource("TextPrimary"),
                });
                stack.Children.Add(new TextBlock
                {
                    Text = p.Description,
                    Style = (Style)FindResource("Hint"),
                    Margin = new Thickness(2, 0, 0, 2),
                    Height = 32,
                });
                card.Child = stack;
                card.MouseLeftButtonUp += (_, __) => SelectPreset(p);
                card.MouseEnter += (_, __) => { if (_selectedPreset != p.Name) card.BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xF0)); };
                card.MouseLeave += (_, __) => HighlightSelectedCard();
                PresetPanel.Children.Add(card);
            }
        }

        /// <summary>A miniature drawing of the desktop the theme produces.</summary>
        private static FrameworkElement BuildPreview(ThemeSettings t, double w, double h)
        {
            var grid = new Grid { Width = w, Height = h, ClipToBounds = true };
            var clip = new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = grid };

            ImageSource wallpaper = t.Wallpaper.Kind == WallpaperKind.Generated
                ? WallpaperService.Render(t.Wallpaper, (int)w * 2, (int)h * 2)
                : null;
            grid.Children.Add(wallpaper != null
                ? (UIElement)new Image { Source = wallpaper, Stretch = Stretch.UniformToFill }
                : new Border { Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x5A, 0x9E)) });

            var accent = ColorUtil.Parse(t.AccentColor, Colors.DodgerBlue);
            Color surface = t.DarkSystem ? Color.FromArgb(220, 0x20, 0x20, 0x24) : Color.FromArgb(215, 0xF6, 0xF6, 0xF8);

            // A window in the middle.
            grid.Children.Add(new Border
            {
                Width = w * 0.46,
                Height = h * 0.42,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(t.DarkApps ? Color.FromRgb(0x2B, 0x2B, 0x30) : Colors.White),
                BorderBrush = new SolidColorBrush(t.AccentOnTitleBars ? accent : Color.FromArgb(40, 0, 0, 0)),
                BorderThickness = new Thickness(0, 6, 0, 0),
                Margin = new Thickness(0, 0, 0, h * 0.08),
            });

            if (t.TopBarEnabled)
            {
                grid.Children.Add(new Border
                {
                    Height = 7,
                    VerticalAlignment = VerticalAlignment.Top,
                    Background = new SolidColorBrush(t.TopBarStyle == TopBarStyle.Gnome ? Color.FromRgb(0x10, 0x10, 0x10) : surface),
                });
            }

            if (t.DockEnabled)
            {
                bool vertical = t.DockPosition == ScreenEdge.Left || t.DockPosition == ScreenEdge.Right;
                bool panel = t.DockStyle == DockStyle.Panel;
                var icons = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal, Margin = new Thickness(3) };
                var iconColors = new[] { accent, Color.FromRgb(0xFF, 0x5F, 0x57), Color.FromRgb(0xFF, 0xBD, 0x2E), Color.FromRgb(0x28, 0xC9, 0x40), Color.FromRgb(0x0A, 0x84, 0xFF), Color.FromRgb(0xAF, 0x52, 0xDE) };
                foreach (var c in iconColors)
                    icons.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(2.5), Background = new SolidColorBrush(c), Margin = new Thickness(1.5) });
                var dock = new Border
                {
                    Background = new SolidColorBrush(surface),
                    CornerRadius = new CornerRadius(panel ? 0 : 5),
                    Child = icons,
                };
                switch (t.DockPosition)
                {
                    case ScreenEdge.Bottom: dock.VerticalAlignment = VerticalAlignment.Bottom; dock.HorizontalAlignment = panel ? HorizontalAlignment.Stretch : HorizontalAlignment.Center; dock.Margin = new Thickness(0, 0, 0, panel ? 0 : 3); break;
                    case ScreenEdge.Top: dock.VerticalAlignment = VerticalAlignment.Top; dock.HorizontalAlignment = HorizontalAlignment.Center; break;
                    case ScreenEdge.Left: dock.HorizontalAlignment = HorizontalAlignment.Left; dock.VerticalAlignment = panel ? VerticalAlignment.Stretch : VerticalAlignment.Center; dock.Margin = new Thickness(0, t.TopBarEnabled ? 7 : 0, 0, 0); break;
                    case ScreenEdge.Right: dock.HorizontalAlignment = HorizontalAlignment.Right; dock.VerticalAlignment = VerticalAlignment.Center; break;
                }
                if (panel && vertical) icons.VerticalAlignment = VerticalAlignment.Top;
                grid.Children.Add(dock);
            }

            if (t.TaskbarMode == TaskbarMode.Normal)
            {
                var bar = new Grid { Height = 11, VerticalAlignment = VerticalAlignment.Bottom };
                bar.Children.Add(new Border
                {
                    Background = new SolidColorBrush(t.AccentOnTaskbar && t.DarkSystem
                        ? ColorUtil.Shade(accent, -0.4)
                        : (t.DarkSystem ? Color.FromArgb(235, 0x1C, 0x1C, 0x1C) : Color.FromArgb(235, 0xEE, 0xEE, 0xF2))),
                });
                var tbIcons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = t.TaskbarCentered ? HorizontalAlignment.Center : HorizontalAlignment.Left,
                    Margin = new Thickness(4, 0, 4, 0),
                };
                for (int i = 0; i < 5; i++)
                    tbIcons.Children.Add(new Border
                    {
                        Width = 6, Height = 6, Margin = new Thickness(1.5, 2.5, 1.5, 2.5), CornerRadius = new CornerRadius(1.5),
                        Background = new SolidColorBrush(i == 0 ? accent : (t.DarkSystem ? Color.FromRgb(0x9A, 0x9A, 0xA0) : Color.FromRgb(0x70, 0x70, 0x78))),
                    });
                bar.Children.Add(tbIcons);
                grid.Children.Add(bar);
            }

            return clip;
        }

        private void SelectPreset(ThemeSettings preset)
        {
            _selectedPreset = preset.Name;
            HighlightSelectedCard();
            var t = preset.Clone();
            LoadToUi(t);
            SetStatus($"Loaded \"{preset.Name}\". Tweak anything in the other tabs, then press Apply.");
        }

        private void HighlightSelectedCard()
        {
            foreach (Border card in PresetPanel.Children)
                card.BorderBrush = (string)card.Tag == _selectedPreset
                    ? (Brush)FindResource("Accent")
                    : (Brush)FindResource("CardBorder");
        }

        // ---- Accent color --------------------------------------------------------------

        private void BuildAccentSwatches()
        {
            foreach (var hex in AccentChoices)
            {
                var h = hex;
                var b = new Border
                {
                    Width = 30,
                    Height = 30,
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(0, 0, 8, 8),
                    Background = new SolidColorBrush(ColorUtil.Parse(h, Colors.Gray)),
                    Cursor = Cursors.Hand,
                    ToolTip = h,
                };
                b.MouseLeftButtonUp += (_, __) => AccentHex.Text = h;
                AccentSwatches.Children.Add(b);
            }
        }

        private void AccentHex_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (AccentPreview == null) return;
            if (ColorUtil.TryParse(AccentHex.Text, out var c))
                AccentPreview.Background = new SolidColorBrush(c);
        }

        private void PickAccent_Click(object sender, RoutedEventArgs e)
        {
            var current = ColorUtil.Parse(AccentHex.Text, Colors.DodgerBlue);
            using var dlg = new WinForms.ColorDialog
            {
                FullOpen = true,
                Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B),
            };
            if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                AccentHex.Text = ColorUtil.ToHex(Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B));
        }

        // ---- UI <-> settings -------------------------------------------------------------

        private void LoadToUi(ThemeSettings t)
        {
            _loading = true;
            try
            {
                DarkApps.IsChecked = t.DarkApps;
                DarkSystem.IsChecked = t.DarkSystem;
                Transparency.IsChecked = t.Transparency;
                Glass.IsChecked = t.GlassEffect;
                AccentHex.Text = t.AccentColor;
                AccentOnTaskbar.IsChecked = t.AccentOnTaskbar;
                AccentOnTitleBars.IsChecked = t.AccentOnTitleBars;

                TaskbarNormal.IsChecked = t.TaskbarMode == TaskbarMode.Normal;
                TaskbarAutoHide.IsChecked = t.TaskbarMode == TaskbarMode.AutoHide;
                TaskbarHidden.IsChecked = t.TaskbarMode == TaskbarMode.Hidden;
                TaskbarAlign.SelectedIndex = t.TaskbarCentered ? 0 : 1;
                ShowSearch.IsChecked = t.ShowSearch;
                ShowTaskView.IsChecked = t.ShowTaskView;
                ShowWidgets.IsChecked = t.ShowWidgets;
                ShowDesktopIcons.IsChecked = t.ShowDesktopIcons;

                DockEnabled.IsChecked = t.DockEnabled;
                DockPosition.SelectedIndex = t.DockPosition switch
                {
                    ScreenEdge.Bottom => 0,
                    ScreenEdge.Left => 1,
                    ScreenEdge.Right => 2,
                    _ => 3,
                };
                DockStyleBox.SelectedIndex = t.DockStyle == DockStyle.Floating ? 0 : 1;
                DockSize.Value = t.DockIconSize;
                DockMagnify.IsChecked = t.DockMagnify;
                DockMag.Value = t.DockMagnification;
                DockReserve.IsChecked = t.DockReserveSpace;
                DockRunning.IsChecked = t.DockShowRunning;
                DockSearch.IsChecked = t.DockShowSearch;

                TopBarEnabled.IsChecked = t.TopBarEnabled;
                TopBarStyleBox.SelectedIndex = t.TopBarStyle == TopBarStyle.MacOS ? 0 : 1;
                Clock24h.IsChecked = t.Clock24h;
                ClockDate.IsChecked = t.ClockShowDate;

                var w = t.Wallpaper ?? new WallpaperSpec();
                WallKeep.IsChecked = w.Kind == WallpaperKind.Keep;
                WallGenerated.IsChecked = w.Kind == WallpaperKind.Generated;
                WallImage.IsChecked = w.Kind == WallpaperKind.Image;
                WallStyle.SelectedIndex = (int)w.Style;
                var boxes = new[] { WallC1, WallC2, WallC3, WallC4 };
                for (int i = 0; i < boxes.Length; i++)
                    boxes[i].Text = i < w.Colors.Count ? w.Colors[i] : "";
                WallPath.Text = w.ImagePath ?? "";
            }
            finally
            {
                _loading = false;
            }
            Wallpaper_Changed(null, null);
        }

        private ThemeSettings ReadFromUi()
        {
            var t = new ThemeSettings
            {
                Name = _selectedPreset ?? _config.Theme.Name,
                DarkApps = DarkApps.IsChecked == true,
                DarkSystem = DarkSystem.IsChecked == true,
                Transparency = Transparency.IsChecked == true,
                GlassEffect = Glass.IsChecked == true,
                AccentColor = ColorUtil.TryParse(AccentHex.Text, out var ac) ? ColorUtil.ToHex(ac) : "#0078D4",
                AccentOnTaskbar = AccentOnTaskbar.IsChecked == true,
                AccentOnTitleBars = AccentOnTitleBars.IsChecked == true,

                TaskbarMode = TaskbarHidden.IsChecked == true ? TaskbarMode.Hidden
                    : TaskbarAutoHide.IsChecked == true ? TaskbarMode.AutoHide
                    : TaskbarMode.Normal,
                TaskbarCentered = TaskbarAlign.SelectedIndex != 1,
                ShowSearch = ShowSearch.IsChecked == true,
                ShowTaskView = ShowTaskView.IsChecked == true,
                ShowWidgets = ShowWidgets.IsChecked == true,
                ShowDesktopIcons = ShowDesktopIcons.IsChecked == true,

                DockEnabled = DockEnabled.IsChecked == true,
                DockPosition = DockPosition.SelectedIndex switch
                {
                    1 => ScreenEdge.Left,
                    2 => ScreenEdge.Right,
                    3 => ScreenEdge.Top,
                    _ => ScreenEdge.Bottom,
                },
                DockStyle = DockStyleBox.SelectedIndex == 1 ? DockStyle.Panel : DockStyle.Floating,
                DockIconSize = (int)DockSize.Value,
                DockMagnify = DockMagnify.IsChecked == true,
                DockMagnification = Math.Round(DockMag.Value, 1),
                DockReserveSpace = DockReserve.IsChecked == true,
                DockShowRunning = DockRunning.IsChecked == true,
                DockShowSearch = DockSearch.IsChecked == true,

                TopBarEnabled = TopBarEnabled.IsChecked == true,
                TopBarStyle = TopBarStyleBox.SelectedIndex == 1 ? TopBarStyle.Gnome : TopBarStyle.MacOS,
                Clock24h = Clock24h.IsChecked == true,
                ClockShowDate = ClockDate.IsChecked == true,

                Wallpaper = ReadWallpaper(),
            };
            return t;
        }

        private WallpaperSpec ReadWallpaper() => new WallpaperSpec
        {
            Kind = WallGenerated.IsChecked == true ? WallpaperKind.Generated
                : WallImage.IsChecked == true ? WallpaperKind.Image
                : WallpaperKind.Keep,
            Style = WallStyle.SelectedIndex >= 0 ? (WallpaperStyle)WallStyle.SelectedIndex : WallpaperStyle.Mesh,
            Colors = new[] { WallC1, WallC2, WallC3, WallC4 }
                .Select(b => b.Text.Trim())
                .Where(s => ColorUtil.TryParse(s, out _))
                .ToList(),
            ImagePath = WallPath.Text.Trim(),
        };

        // ---- Wallpaper page ------------------------------------------------------------

        private void Wallpaper_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || GeneratedPanel == null || ImagePanel == null || WallPreview == null) return;
            GeneratedPanel.Visibility = WallGenerated.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ImagePanel.Visibility = WallImage.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            _previewTimer.Stop();
            _previewTimer.Start();
        }

        private void RefreshWallpaperPreview()
        {
            if (WallPreview == null) return;
            var spec = ReadWallpaper();
            try
            {
                switch (spec.Kind)
                {
                    case WallpaperKind.Generated:
                        WallPreview.Source = WallpaperService.Render(spec, 960, 540);
                        break;
                    case WallpaperKind.Image when File.Exists(spec.ImagePath):
                        var bmp = new System.Windows.Media.Imaging.BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bmp.DecodePixelWidth = 960;
                        bmp.UriSource = new Uri(spec.ImagePath);
                        bmp.EndInit();
                        WallPreview.Source = bmp;
                        break;
                    default:
                        string current = WallpaperService.GetCurrentWallpaperPath();
                        WallPreview.Source = File.Exists(current)
                            ? new System.Windows.Media.Imaging.BitmapImage(new Uri(current))
                            : null;
                        break;
                }
            }
            catch
            {
                WallPreview.Source = null;
            }
        }

        private void WallShuffle_Click(object sender, RoutedEventArgs e)
        {
            // Random but harmonious: four hues spread around a random starting point.
            double hue = _random.NextDouble() * 360;
            double spread = 25 + _random.NextDouble() * 50;
            var boxes = new[] { WallC1, WallC2, WallC3, WallC4 };
            for (int i = 0; i < boxes.Length; i++)
            {
                double l = i == 0 ? 0.22 : 0.45 + 0.1 * i;
                boxes[i].Text = ColorUtil.ToHex(FromHsl((hue + spread * i) % 360, 0.65, Math.Min(0.8, l)));
            }
            WallGenerated.IsChecked = true;
        }

        private static Color FromHsl(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
            double m = l - c / 2;
            (double r, double g, double b) = h switch
            {
                < 60 => (c, x, 0d),
                < 120 => (x, c, 0d),
                < 180 => (0d, c, x),
                < 240 => (0d, x, c),
                < 300 => (x, 0d, c),
                _ => (c, 0d, x),
            };
            return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }

        private void WallBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Choose a wallpaper",
                Filter = "Pictures (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|All files (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            };
            if (dlg.ShowDialog(this) == true)
            {
                WallPath.Text = dlg.FileName;
                WallImage.IsChecked = true;
            }
        }

        // ---- Dock apps -----------------------------------------------------------------

        private void RefreshDockList(int select = -1)
        {
            DockList.Items.Clear();
            foreach (var item in _config.DockItems)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2), Tag = item };
                row.Children.Add(new Image { Source = IconService.Get(item.Target, 64), Width = 24, Height = 24, Margin = new Thickness(0, 0, 10, 0) });
                row.Children.Add(new TextBlock { Text = item.Name, VerticalAlignment = VerticalAlignment.Center });
                DockList.Items.Add(row);
            }
            if (select >= 0 && select < DockList.Items.Count) DockList.SelectedIndex = select;
        }

        private void DockChanged(int select = -1)
        {
            RefreshDockList(select);
            ConfigStore.Save(_config);
            SetStatus("Dock apps changed. Press Apply to update the Dock.");
        }

        private void DockAdd_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Add apps to the Dock",
                Filter = "Apps and shortcuts (*.exe;*.lnk)|*.exe;*.lnk|All files (*.*)|*.*",
                Multiselect = true,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) + @"\Programs",
            };
            if (dlg.ShowDialog(this) != true) return;
            foreach (var f in dlg.FileNames)
                if (!_config.DockItems.Any(d => string.Equals(d.Target, f, StringComparison.OrdinalIgnoreCase)))
                    _config.DockItems.Add(new DockItem { Name = Path.GetFileNameWithoutExtension(f), Target = f });
            DockChanged(_config.DockItems.Count - 1);
        }

        private void DockAddFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new WinForms.FolderBrowserDialog { Description = "Add a folder to the Dock" };
            if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;
            _config.DockItems.Add(new DockItem { Name = new DirectoryInfo(dlg.SelectedPath).Name, Target = dlg.SelectedPath });
            DockChanged(_config.DockItems.Count - 1);
        }

        private void DockRemove_Click(object sender, RoutedEventArgs e)
        {
            int i = DockList.SelectedIndex;
            if (i < 0) return;
            _config.DockItems.RemoveAt(i);
            DockChanged(Math.Min(i, _config.DockItems.Count - 1));
        }

        private void DockUp_Click(object sender, RoutedEventArgs e) => MoveDockItem(-1);
        private void DockDown_Click(object sender, RoutedEventArgs e) => MoveDockItem(1);

        private void MoveDockItem(int delta)
        {
            int i = DockList.SelectedIndex, j = i + delta;
            if (i < 0 || j < 0 || j >= _config.DockItems.Count) return;
            (_config.DockItems[i], _config.DockItems[j]) = (_config.DockItems[j], _config.DockItems[i]);
            DockChanged(j);
        }

        private void DockReset_Click(object sender, RoutedEventArgs e)
        {
            _config.DockItems = ShellActions.DefaultDockItems();
            DockChanged();
        }

        // ---- Import / export -----------------------------------------------------------

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Theme files (*.json)|*.json", Title = "Import theme" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var t = ConfigStore.ImportTheme(dlg.FileName);
                _selectedPreset = t.Name;
                HighlightSelectedCard();
                LoadToUi(t);
                SetStatus($"Imported \"{t.Name}\". Press Apply to use it.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "That file couldn't be read as a theme.\n\n" + ex.Message, "Import theme");
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var t = ReadFromUi();
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Theme files (*.json)|*.json",
                FileName = (t.Name ?? "My theme") + ".json",
                Title = "Export theme",
            };
            if (dlg.ShowDialog(this) != true) return;
            t.Name = Path.GetFileNameWithoutExtension(dlg.FileName);
            ConfigStore.ExportTheme(t, dlg.FileName);
            SetStatus("Theme exported to " + dlg.FileName);
        }

        // ---- Apply / restore -----------------------------------------------------------

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            var t = ReadFromUi();

            if (t.TaskbarMode == TaskbarMode.Hidden && !t.DockEnabled && !t.TopBarEnabled)
            {
                var answer = MessageBox.Show(this,
                    "You're hiding the taskbar without turning on the Dock or Top bar.\n\n" +
                    "You'll only be able to open apps with the Windows key. Continue anyway?",
                    "No taskbar", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes) return;
            }

            ApplyButton.IsEnabled = false;
            Mouse.OverrideCursor = Cursors.Wait;
            var warnings = new List<string>();
            try
            {
                BackupService.EnsureBackup();

                _config.Theme = t;
                _config.StartWithWindows = StartWithWindows.IsChecked == true;
                _config.Applied = true;

                warnings.AddRange(SystemTweaks.Apply(t));

                try
                {
                    WallpaperService.Apply(t.Wallpaper, Dpi.ScreenWidthPx, Dpi.ScreenHeightPx);
                }
                catch (Exception ex)
                {
                    warnings.Add("Wallpaper: " + ex.Message);
                }

                try
                {
                    App.Current.Shell.Apply();
                }
                catch (Exception ex)
                {
                    warnings.Add("Dock / top bar: " + ex.Message);
                }

                try
                {
                    Reg.SetStartWithWindows(_config.StartWithWindows && App.Current.Shell.IsRunning);
                }
                catch (Exception ex)
                {
                    warnings.Add("Start with Windows: " + ex.Message);
                }

                ConfigStore.Save(_config);
            }
            catch (Exception ex)
            {
                warnings.Add(ex.Message);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                ApplyButton.IsEnabled = true;
                UpdateBackupText();
            }

            // The widgets button is protected on newer Windows 11 builds; not worth bothering the user about.
            var important = warnings.Where(w => !w.StartsWith("Widgets", StringComparison.Ordinal)).ToList();
            if (important.Count > 0)
            {
                MessageBox.Show(this, "Applied, but a few things didn't work:\n\n• " + string.Join("\n• ", important),
                    "Windows Personalizer", MessageBoxButton.OK, MessageBoxImage.Warning);
                SetStatus("Applied with some warnings.");
            }
            else
            {
                SetStatus($"\"{t.Name}\" applied. If the taskbar hasn't fully updated, press Restart Explorer.");
            }
        }

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            if (!BackupService.HasBackup)
            {
                MessageBox.Show(this, "Nothing to restore: you haven't applied a theme yet (or already restored).",
                    "Restore original look");
                return;
            }
            if (MessageBox.Show(this,
                    "Put Windows back exactly how it looked before you first pressed Apply?\n\n" +
                    "This removes the Dock and Top bar and brings back your taskbar, colors and wallpaper.",
                    "Restore original look", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                App.Current.Shell.Stop(restoreTaskbar: true);
                var failures = BackupService.Restore();
                _config.Applied = false;
                _config.Theme.DockEnabled = false;
                _config.Theme.TopBarEnabled = false;
                _config.Theme.TaskbarMode = TaskbarService.IsAutoHide() ? TaskbarMode.AutoHide : TaskbarMode.Normal;
                Reg.SetStartWithWindows(false);
                ConfigStore.Save(_config);
                _selectedPreset = null;
                HighlightSelectedCard();
                LoadToUi(_config.Theme);
                SetStatus(failures.Count == 0
                    ? "Your original look is back. Restart Explorer if the taskbar still looks different."
                    : "Restored, except: " + string.Join(", ", failures));
            }
            finally
            {
                Mouse.OverrideCursor = null;
                UpdateBackupText();
            }
        }

        private void RestartExplorer_Click(object sender, RoutedEventArgs e)
        {
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                SystemTweaks.RestartExplorer();
                SetStatus("Explorer restarted.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Restart Explorer");
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void UpdateBackupText()
        {
            BackupText.Text = BackupService.HasBackup
                ? "Your original look is saved and can be restored any time."
                : "Your current look will be saved the first time you press Apply.";
        }

        private void SetStatus(string text) => StatusText.Text = text;
    }
}
