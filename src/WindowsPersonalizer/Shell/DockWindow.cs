using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using WindowsPersonalizer.Model;
using WindowsPersonalizer.Native;
using WindowsPersonalizer.Services;

namespace WindowsPersonalizer.Shell
{
    /// <summary>
    /// A macOS / Ubuntu style dock: pinned apps, magnification on hover, running indicators,
    /// drag-and-drop to pin. Lives on a screen edge and reserves space like a real taskbar.
    /// </summary>
    public sealed class DockWindow : Window
    {
        private const double Pad = 8;
        private const double Gap = 6;

        private readonly ThemeSettings _theme;
        private readonly AppConfig _config;
        private readonly Action _saveConfig;
        private readonly Action _openSettings;

        private readonly bool _vertical;
        private readonly double _baseSize;
        private readonly double _maxSize;
        private readonly double _edgeGap;
        private readonly double _thickness;

        private readonly Grid _root = new Grid();
        private readonly Border _background = new Border();
        private readonly StackPanel _panel = new StackPanel();
        private readonly List<Cell> _cells = new List<Cell>();
        private readonly DispatcherTimer _runningTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        private AppBar _appBar;

        private sealed class Cell
        {
            public FrameworkElement Element;
            public Image Icon;
            public Ellipse Dot;
            public DockItem Item;          // null for launcher, recycle bin and separators
            public string ProcessName;
            public bool IsSeparator;
            public double BaseLength;      // length along the dock axis at rest
        }

        public DockWindow(ThemeSettings theme, AppConfig config, Action saveConfig, Action openSettings)
        {
            _theme = theme;
            _config = config;
            _saveConfig = saveConfig;
            _openSettings = openSettings;

            _vertical = theme.DockPosition == ScreenEdge.Left || theme.DockPosition == ScreenEdge.Right;
            _baseSize = Math.Max(24, Math.Min(128, theme.DockIconSize));
            bool magnify = theme.DockMagnify && theme.DockStyle == DockStyle.Floating;
            _maxSize = magnify ? _baseSize * Math.Max(1.0, Math.Min(2.5, theme.DockMagnification)) : _baseSize;
            _edgeGap = theme.DockStyle == DockStyle.Floating ? 6 : 0;
            _thickness = _baseSize + Pad * 2 + _edgeGap;

            Title = "Dock";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            AllowDrop = true;

            BuildChrome();
            BuildItems();
            Content = _root;

            _root.MouseMove += (_, e) => Magnify(e.GetPosition(_root));
            _root.MouseLeave += (_, __) => ResetSizes();
            Drop += OnDrop;
            DragOver += (_, e) =>
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
                e.Handled = true;
            };

            _runningTimer.Tick += (_, __) => UpdateRunning();
            SourceInitialized += OnSourceInitialized;
            Closed += (_, __) =>
            {
                _runningTimer.Stop();
            };
            Closing += (_, __) => _appBar?.Dispose();
        }

        // ---- Layout ---------------------------------------------------------------

        private bool Dark => _theme.DarkSystem;

        private void BuildChrome()
        {
            var accent = ColorUtil.Parse(_theme.AccentColor, Colors.DodgerBlue);
            bool panel = _theme.DockStyle == DockStyle.Panel;

            _background.Background = new SolidColorBrush(Dark
                ? Color.FromArgb(panel ? (byte)235 : (byte)190, 0x1E, 0x1E, 0x22)
                : Color.FromArgb(panel ? (byte)235 : (byte)175, 0xF6, 0xF6, 0xF8));
            _background.BorderBrush = new SolidColorBrush(Dark ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(60, 0, 0, 0));
            _background.BorderThickness = new Thickness(panel ? 0 : 1);
            _background.CornerRadius = new CornerRadius(panel ? 0 : 18);
            _background.Effect = panel ? null : new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 2,
                Opacity = 0.25,
                Direction = 270,
            };

            _panel.Orientation = _vertical ? Orientation.Vertical : Orientation.Horizontal;

            double bgThickness = _baseSize + Pad * 2;
            switch (_theme.DockPosition)
            {
                case ScreenEdge.Bottom:
                    _background.VerticalAlignment = VerticalAlignment.Bottom;
                    _background.Height = bgThickness;
                    _background.Margin = new Thickness(0, 0, 0, _edgeGap);
                    _panel.VerticalAlignment = VerticalAlignment.Bottom;
                    _panel.Margin = new Thickness(0, 0, 0, _edgeGap + Pad);
                    break;
                case ScreenEdge.Top:
                    _background.VerticalAlignment = VerticalAlignment.Top;
                    _background.Height = bgThickness;
                    _background.Margin = new Thickness(0, _edgeGap, 0, 0);
                    _panel.VerticalAlignment = VerticalAlignment.Top;
                    _panel.Margin = new Thickness(0, _edgeGap + Pad, 0, 0);
                    break;
                case ScreenEdge.Left:
                    _background.HorizontalAlignment = HorizontalAlignment.Left;
                    _background.Width = bgThickness;
                    _background.Margin = new Thickness(_edgeGap, 0, 0, 0);
                    _panel.HorizontalAlignment = HorizontalAlignment.Left;
                    _panel.Margin = new Thickness(_edgeGap + Pad, panel ? Pad : 0, 0, 0);
                    break;
                case ScreenEdge.Right:
                    _background.HorizontalAlignment = HorizontalAlignment.Right;
                    _background.Width = bgThickness;
                    _background.Margin = new Thickness(0, 0, _edgeGap, 0);
                    _panel.HorizontalAlignment = HorizontalAlignment.Right;
                    _panel.Margin = new Thickness(0, panel ? Pad : 0, _edgeGap + Pad, 0);
                    break;
            }

            if (panel)
            {
                // Ubuntu-style: the background runs along the whole edge, icons start at the top/left.
                if (_vertical) { _background.VerticalAlignment = VerticalAlignment.Stretch; _panel.VerticalAlignment = VerticalAlignment.Top; }
                else { _background.HorizontalAlignment = HorizontalAlignment.Stretch; _panel.HorizontalAlignment = HorizontalAlignment.Left; }
            }
            else
            {
                // Floating: centered, and the background hugs the icons as they grow.
                if (_vertical) { _background.VerticalAlignment = VerticalAlignment.Center; _panel.VerticalAlignment = VerticalAlignment.Center; }
                else { _background.HorizontalAlignment = HorizontalAlignment.Center; _panel.HorizontalAlignment = HorizontalAlignment.Center; }
                _panel.SizeChanged += (_, __) =>
                {
                    if (_vertical) _background.Height = _panel.ActualHeight + Pad * 2;
                    else _background.Width = _panel.ActualWidth + Pad * 2;
                };
            }

            var menu = new ContextMenu();
            menu.Items.Add(MenuItem("Dock settings…", () => _openSettings()));
            menu.Items.Add(MenuItem("Add app…", AddAppWithDialog));
            _background.ContextMenu = menu;

            _root.Children.Add(_background);
            _root.Children.Add(_panel);
        }

        private void BuildItems()
        {
            _panel.Children.Clear();
            _cells.Clear();
            var accent = ColorUtil.Parse(_theme.AccentColor, Colors.DodgerBlue);

            AddCell(null, "Start", IconService.LauncherIcon(accent), ShellActions.OpenStart);
            if (_theme.DockShowSearch)
                AddCell(null, "Search", IconService.SearchIcon(accent), ShellActions.OpenSearch);

            foreach (var item in _config.DockItems ?? new List<DockItem>())
            {
                var it = item;
                AddCell(it, it.Name, IconService.Get(it.Target), () => OpenItem(it));
            }

            AddSeparator();
            AddCell(null, "Recycle Bin", IconService.Get(ShellActions.RecycleBin),
                () => ShellActions.Launch(new DockItem { Target = ShellActions.RecycleBin }));

            UpdateRunning();
            if (_theme.DockShowRunning) _runningTimer.Start();
        }

        private void AddCell(DockItem item, string label, ImageSource icon, Action click)
        {
            var img = new Image
            {
                Source = icon,
                Stretch = Stretch.Uniform,
                IsHitTestVisible = false,
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

            var dot = new Ellipse
            {
                Width = 5,
                Height = 5,
                Fill = new SolidColorBrush(Dark ? Color.FromRgb(230, 230, 230) : Color.FromRgb(40, 40, 40)),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
            };
            double dotOffset = -(Pad - 1);
            switch (_theme.DockPosition)
            {
                case ScreenEdge.Bottom: dot.VerticalAlignment = VerticalAlignment.Bottom; dot.Margin = new Thickness(0, 0, 0, dotOffset); break;
                case ScreenEdge.Top: dot.VerticalAlignment = VerticalAlignment.Top; dot.Margin = new Thickness(0, dotOffset, 0, 0); break;
                case ScreenEdge.Left: dot.HorizontalAlignment = HorizontalAlignment.Left; dot.Margin = new Thickness(dotOffset, 0, 0, 0); break;
                case ScreenEdge.Right: dot.HorizontalAlignment = HorizontalAlignment.Right; dot.Margin = new Thickness(0, 0, dotOffset, 0); break;
            }

            var cell = new Grid
            {
                Width = _baseSize,
                Height = _baseSize,
                // Almost-transparent fill so the whole square is clickable on a layered window.
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                Margin = _vertical ? new Thickness(0, Gap / 2, 0, Gap / 2) : new Thickness(Gap / 2, 0, Gap / 2, 0),
                ToolTip = label,
                Cursor = Cursors.Hand,
                RenderTransform = new TranslateTransform(),
            };
            ToolTipService.SetInitialShowDelay(cell, 150);
            ToolTipService.SetPlacement(cell, _theme.DockPosition switch
            {
                ScreenEdge.Bottom => PlacementMode.Top,
                ScreenEdge.Top => PlacementMode.Bottom,
                ScreenEdge.Left => PlacementMode.Right,
                _ => PlacementMode.Left,
            });
            switch (_theme.DockPosition)
            {
                case ScreenEdge.Bottom: cell.VerticalAlignment = VerticalAlignment.Bottom; break;
                case ScreenEdge.Top: cell.VerticalAlignment = VerticalAlignment.Top; break;
                case ScreenEdge.Left: cell.HorizontalAlignment = HorizontalAlignment.Left; break;
                case ScreenEdge.Right: cell.HorizontalAlignment = HorizontalAlignment.Right; break;
            }
            cell.Children.Add(img);
            cell.Children.Add(dot);

            cell.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                Bounce(cell);
                try { click(); }
                catch (Exception ex)
                {
                    MessageBox.Show($"Couldn't open \"{label}\".\n\n{ex.Message}", "Dock", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };

            if (item != null)
            {
                var menu = new ContextMenu();
                menu.Items.Add(MenuItem("Open", () => ShellActions.Launch(item)));
                string expanded = Environment.ExpandEnvironmentVariables(item.Target);
                if (File.Exists(expanded))
                    menu.Items.Add(MenuItem("Show in File Explorer", () =>
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{expanded}\"")));
                menu.Items.Add(new Separator());
                menu.Items.Add(MenuItem("Remove from Dock", () => RemoveItem(item)));
                cell.ContextMenu = menu;
            }

            _panel.Children.Add(cell);
            _cells.Add(new Cell
            {
                Element = cell,
                Icon = img,
                Dot = dot,
                Item = item,
                ProcessName = item != null ? ShellActions.ProcessNameOf(item) : null,
                BaseLength = _baseSize + Gap,
            });
        }

        private void AddSeparator()
        {
            var line = new Rectangle
            {
                Fill = new SolidColorBrush(Dark ? Color.FromArgb(80, 255, 255, 255) : Color.FromArgb(70, 0, 0, 0)),
                Width = _vertical ? _baseSize * 0.7 : 1,
                Height = _vertical ? 1 : _baseSize * 0.7,
                Margin = _vertical ? new Thickness(0, Gap, 0, Gap) : new Thickness(Gap, 0, Gap, 0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            _panel.Children.Add(line);
            _cells.Add(new Cell { Element = line, IsSeparator = true, BaseLength = 1 + Gap * 2 });
        }

        private static MenuItem MenuItem(string header, Action action)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, __) =>
            {
                try { action(); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Dock", MessageBoxButton.OK, MessageBoxImage.Warning); }
            };
            return mi;
        }

        // ---- Magnification --------------------------------------------------------

        private void Magnify(Point p)
        {
            if (_maxSize <= _baseSize + 0.5) return;

            double total = _cells.Sum(c => c.BaseLength);
            double windowLength = _vertical ? _root.ActualHeight : _root.ActualWidth;
            double pos = _vertical ? p.Y : p.X;
            double cursor = (windowLength - total) / 2; // resting layout is centered
            double range = _baseSize * 2.6;

            foreach (var c in _cells)
            {
                double center = cursor + c.BaseLength / 2;
                cursor += c.BaseLength;
                if (c.IsSeparator) continue;

                double d = Math.Abs(pos - center);
                double t = Math.Max(0, 1 - d / range);
                // Smooth bell curve, like the macOS dock.
                double size = _baseSize + (_maxSize - _baseSize) * (0.5 - 0.5 * Math.Cos(Math.PI * t));
                c.Element.Width = size;
                c.Element.Height = size;
            }
        }

        private void ResetSizes()
        {
            foreach (var c in _cells.Where(c => !c.IsSeparator))
            {
                c.Element.Width = _baseSize;
                c.Element.Height = _baseSize;
            }
        }

        private void Bounce(FrameworkElement cell)
        {
            var tt = (TranslateTransform)cell.RenderTransform;
            double dir = _theme.DockPosition switch
            {
                ScreenEdge.Bottom => -1,
                ScreenEdge.Top => 1,
                ScreenEdge.Left => 1,
                _ => -1,
            };
            var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(600) };
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(dir * _baseSize * 0.35, KeyTime.FromPercent(0.3),
                new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1.0),
                new BounceEase { Bounces = 1, Bounciness = 3, EasingMode = EasingMode.EaseOut }));
            tt.BeginAnimation(_vertical ? TranslateTransform.XProperty : TranslateTransform.YProperty, anim);
        }

        // ---- Apps -----------------------------------------------------------------

        private static void OpenItem(DockItem item)
        {
            // Like macOS: clicking a running app brings it forward, otherwise launch it.
            if (!ShellActions.Activate(ShellActions.ProcessNameOf(item)))
                ShellActions.Launch(item);
        }

        private void UpdateRunning()
        {
            if (!_theme.DockShowRunning)
            {
                foreach (var c in _cells.Where(c => c.Dot != null)) c.Dot.Visibility = Visibility.Collapsed;
                return;
            }
            HashSet<string> running;
            try { running = ShellActions.RunningProcessNames(); }
            catch { return; }
            foreach (var c in _cells.Where(c => c.Dot != null))
                c.Dot.Visibility = c.ProcessName != null && running.Contains(c.ProcessName)
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RemoveItem(DockItem item)
        {
            _config.DockItems.Remove(item);
            _saveConfig();
            BuildItems();
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (!(e.Data.GetData(DataFormats.FileDrop) is string[] files)) return;
            foreach (var f in files) AddPath(f);
            _saveConfig();
            BuildItems();
        }

        private void AddAppWithDialog()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Add an app to the Dock",
                Filter = "Apps and shortcuts (*.exe;*.lnk)|*.exe;*.lnk|All files (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            };
            if (dlg.ShowDialog() == true)
            {
                AddPath(dlg.FileName);
                _saveConfig();
                BuildItems();
            }
        }

        private void AddPath(string path)
        {
            _config.DockItems ??= new List<DockItem>();
            if (_config.DockItems.Any(d => string.Equals(d.Target, path, StringComparison.OrdinalIgnoreCase))) return;
            _config.DockItems.Add(new DockItem
            {
                Name = Directory.Exists(path) ? new DirectoryInfo(path).Name : System.IO.Path.GetFileNameWithoutExtension(path),
                Target = path,
            });
        }

        // ---- Placement ------------------------------------------------------------

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            // Tool window: no Alt+Tab entry. No-activate: clicking the dock doesn't steal focus.
            long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            ex |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));

            _appBar = new AppBar(this, _theme.DockPosition, _thickness, _theme.DockReserveSpace);
            _appBar.Positioned += PlaceWindow;
            _appBar.FullScreenChanged += fullScreen =>
            {
                Topmost = !fullScreen;
                Visibility = fullScreen ? Visibility.Hidden : Visibility.Visible;
            };
            _appBar.Attach();
        }

        private void PlaceWindow(NativeMethods.RECT rc)
        {
            // The window is as long as the screen edge and tall enough for fully magnified icons;
            // its transparent parts let clicks pass through to the windows underneath.
            double windowThickness = _maxSize + Pad * 2 + _edgeGap + 4;
            double left = Dpi.ToDip(rc.Left), top = Dpi.ToDip(rc.Top);
            double right = Dpi.ToDip(rc.Right), bottom = Dpi.ToDip(rc.Bottom);

            switch (_theme.DockPosition)
            {
                case ScreenEdge.Bottom:
                    Left = left; Width = right - left; Height = windowThickness; Top = bottom - windowThickness; break;
                case ScreenEdge.Top:
                    Left = left; Width = right - left; Height = windowThickness; Top = top; break;
                case ScreenEdge.Left:
                    Top = top; Height = bottom - top; Width = windowThickness; Left = left; break;
                case ScreenEdge.Right:
                    Top = top; Height = bottom - top; Width = windowThickness; Left = right - windowThickness; break;
            }
        }
    }
}
