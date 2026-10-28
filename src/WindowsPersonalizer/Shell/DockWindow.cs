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
        private readonly DispatcherTimer _runningTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private List<WindowList.AppGroup> _apps = new List<WindowList.AppGroup>();
        private string _shownOpenApps = "";
        private readonly Border _inner = new Border();
        private readonly double _radius;
        private AppBar _appBar;
        private BackdropWindow _backdrop;
        private Int32Rect _lastBackdrop;
        private Point? _mouse;
        private bool _animating;

        private sealed class Cell
        {
            public FrameworkElement Element;
            public Image Icon;
            public Ellipse Dot;
            public DockItem Item;          // null for launcher, recycle bin, open apps and separators
            public string AppKey;          // lower-case exe name used to match open windows
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
            _radius = theme.DockStyle == DockStyle.Panel ? 0 : Math.Round(_baseSize * 0.3 + 6);

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
            ApplyMaterial(glass: false);
            RefreshApps();
            BuildItems();
            Content = _root;

            _root.MouseMove += (_, e) => { _mouse = e.GetPosition(_root); StartAnimating(); };
            _root.MouseLeave += (_, __) => { _mouse = null; StartAnimating(); };
            _background.LayoutUpdated += (_, __) => SyncBackdrop();
            LocationChanged += (_, __) => SyncBackdrop();
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
                if (_animating) CompositionTarget.Rendering -= OnFrame;
                _backdrop?.Close();
            };
            Closing += (_, __) => _appBar?.Dispose();
        }

        // ---- Layout ---------------------------------------------------------------

        private bool Dark => _theme.DarkSystem;

        private void BuildChrome()
        {
            bool panel = _theme.DockStyle == DockStyle.Panel;

            // Two borders: a fine dark outline outside, a soft light highlight inside (like macOS).
            _background.CornerRadius = new CornerRadius(_radius);
            _background.BorderThickness = new Thickness(panel ? 0 : 1);
            _inner.CornerRadius = new CornerRadius(Math.Max(0, _radius - 1));
            _inner.BorderThickness = panel
                ? new Thickness(_theme.DockPosition == ScreenEdge.Left ? 0 : 1, 0, _theme.DockPosition == ScreenEdge.Left ? 1 : 0, 0)
                : new Thickness(1);
            _background.Child = _inner;

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

            var menu = NewMenu();
            menu.Items.Add(MenuItem("Add app…", AddAppWithDialog));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("Dock settings…", () => _openSettings()));
            _background.ContextMenu = menu;

            _root.Children.Add(_background);
            _root.Children.Add(_panel);
        }

        /// <summary>
        /// Glass: a light tint over the real blur. Without blur (setting off or unsupported),
        /// a more solid tint so it still looks clean.
        /// </summary>
        private void ApplyMaterial(bool glass)
        {
            bool panel = _theme.DockStyle == DockStyle.Panel;
            byte tint = glass ? (Dark ? (byte)110 : (byte)95) : (Dark ? (byte)225 : (byte)215);
            if (panel) tint = glass ? (byte)150 : (byte)240;
            _inner.Background = new SolidColorBrush(Dark
                ? Color.FromArgb(tint, 0x24, 0x24, 0x28)
                : Color.FromArgb(tint, 0xFA, 0xFA, 0xFC));
            _inner.BorderBrush = new SolidColorBrush(Dark ? Color.FromArgb(38, 255, 255, 255) : Color.FromArgb(110, 255, 255, 255));
            _background.BorderBrush = new SolidColorBrush(Dark ? Color.FromArgb(150, 0, 0, 0) : Color.FromArgb(40, 0, 0, 0));
            _background.Effect = panel ? null : new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 30,
                ShadowDepth = 6,
                Opacity = Dark ? 0.45 : 0.22,
                Direction = 270,
                Color = Colors.Black,
            };
        }

        /// <summary>Shows the dock (and its glass backdrop underneath, if enabled).</summary>
        public void Start()
        {
            if (_theme.GlassEffect)
            {
                _backdrop = new BackdropWindow();
                _backdrop.Show();
                if (_backdrop.BlurEnabled)
                {
                    Owner = _backdrop; // owned windows always stay above their owner
                    ApplyMaterial(glass: true);
                }
                else
                {
                    _backdrop.Close();
                    _backdrop = null;
                }
            }
            Show();
        }

        private void SyncBackdrop()
        {
            if (_backdrop == null || PresentationSource.FromVisual(_background) == null || !_background.IsVisible) return;
            Point tl = _background.PointToScreen(new Point(0, 0));
            Point br = _background.PointToScreen(new Point(_background.ActualWidth, _background.ActualHeight));
            var r = new Int32Rect((int)Math.Round(tl.X), (int)Math.Round(tl.Y),
                (int)Math.Round(br.X - tl.X), (int)Math.Round(br.Y - tl.Y));
            if (r.Equals(_lastBackdrop)) return;
            _lastBackdrop = r;
            _backdrop.SetBounds(r.X, r.Y, r.Width, r.Height, Dpi.ToPx(_radius));
        }

        private ContextMenu NewMenu() =>
            MenuTheme.Create(Dark, ColorUtil.Parse(_theme.AccentColor, Colors.DodgerBlue));

        private void RefreshApps()
        {
            try { _apps = WindowList.GetApps(); }
            catch { _apps = new List<WindowList.AppGroup>(); }
        }

        /// <summary>Open apps that are not pinned (shown in their own section, like the taskbar).</summary>
        private List<WindowList.AppGroup> UnpinnedOpenApps()
        {
            if (!_theme.DockShowOpenApps) return new List<WindowList.AppGroup>();
            var pinned = new HashSet<string>((_config.DockItems ?? new List<DockItem>())
                .Select(KeyOf).Where(k => k != null));
            return _apps.Where(a => !pinned.Contains(a.Key)).ToList();
        }

        private static string KeyOf(DockItem item) => ShellActions.ProcessNameOf(item)?.ToLowerInvariant();

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
                AddCell(it, it.Name, IconService.Get(it.Target), () => OpenItem(it), KeyOf(it));
            }

            var open = UnpinnedOpenApps();
            _shownOpenApps = string.Join("|", open.Select(a => a.Key));
            if (open.Count > 0)
            {
                AddSeparator();
                foreach (var app in open)
                {
                    string key = app.Key, exe = app.ExePath, name = app.Name;
                    AddCell(null, name, IconService.Get(exe), () => SwitchTo(key, exe), key, openApp: app);
                }
            }

            AddSeparator();
            AddCell(null, "Recycle Bin", IconService.Get(ShellActions.RecycleBin),
                () => ShellActions.Launch(new DockItem { Target = ShellActions.RecycleBin }));

            UpdateDots();
            if (_theme.DockShowRunning || _theme.DockShowOpenApps) _runningTimer.Start();
        }

        private void AddCell(DockItem item, string label, ImageSource icon, Action click,
            string appKey = null, WindowList.AppGroup openApp = null)
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
                Width = 4,
                Height = 4,
                Fill = new SolidColorBrush(Dark ? Color.FromRgb(235, 235, 240) : Color.FromRgb(30, 30, 34)),
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
            ToolTipService.SetInitialShowDelay(cell, 100);
            ToolTipService.SetVerticalOffset(cell, _theme.DockPosition == ScreenEdge.Bottom ? -10 : _theme.DockPosition == ScreenEdge.Top ? 10 : 0);
            ToolTipService.SetHorizontalOffset(cell, _theme.DockPosition == ScreenEdge.Left ? 10 : _theme.DockPosition == ScreenEdge.Right ? -10 : 0);
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

            // Pressing an icon darkens it, like macOS.
            cell.MouseLeftButtonDown += (_, __) => img.Opacity = 0.6;
            cell.MouseLeave += (_, __) => img.Opacity = 1;
            cell.MouseLeftButtonUp += (_, e) =>
            {
                img.Opacity = 1;
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
                var menu = NewMenu();
                menu.Items.Add(MenuItem("Open", () => ShellActions.Launch(item)));
                string expanded = Environment.ExpandEnvironmentVariables(item.Target);
                if (File.Exists(expanded))
                    menu.Items.Add(MenuItem("Show in File Explorer", () =>
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{expanded}\"")));
                menu.Items.Add(new Separator());
                menu.Items.Add(MenuItem("Remove from Dock", () => RemoveItem(item)));
                if (appKey != null)
                {
                    var quit = MenuItem("Quit", () => QuitApp(appKey));
                    menu.Items.Add(quit);
                    menu.Opened += (_, __) => quit.Visibility =
                        WindowList.Find(_apps, appKey) != null ? Visibility.Visible : Visibility.Collapsed;
                }
                cell.ContextMenu = menu;
            }
            else if (openApp != null)
            {
                var menu = NewMenu();
                menu.Items.Add(MenuItem("Keep in Dock", () => PinOpenApp(openApp)));
                menu.Items.Add(new Separator());
                menu.Items.Add(MenuItem("Quit", () => QuitApp(appKey)));
                cell.ContextMenu = menu;
            }

            _panel.Children.Add(cell);
            _cells.Add(new Cell
            {
                Element = cell,
                Icon = img,
                Dot = dot,
                Item = item,
                AppKey = appKey,
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

        /// <summary>Icon sizes for a mouse position (null = mouse away, everything at rest).</summary>
        private double[] TargetSizes(Point? p)
        {
            var sizes = new double[_cells.Count];
            double total = _cells.Sum(c => c.BaseLength);
            double windowLength = _vertical ? _root.ActualHeight : _root.ActualWidth;
            double cursor = (windowLength - total) / 2; // resting layout is centered
            double range = _baseSize * 2.6;

            for (int i = 0; i < _cells.Count; i++)
            {
                var c = _cells[i];
                double center = cursor + c.BaseLength / 2;
                cursor += c.BaseLength;
                if (c.IsSeparator || p == null) { sizes[i] = _baseSize; continue; }

                double pos = _vertical ? p.Value.Y : p.Value.X;
                double t = Math.Max(0, 1 - Math.Abs(pos - center) / range);
                // Smooth bell curve, like the macOS dock.
                sizes[i] = _baseSize + (_maxSize - _baseSize) * (0.5 - 0.5 * Math.Cos(Math.PI * t));
            }
            return sizes;
        }

        private void StartAnimating()
        {
            if (_animating || _maxSize <= _baseSize + 0.5) return;
            _animating = true;
            CompositionTarget.Rendering += OnFrame;
        }

        /// <summary>Every frame, icons glide toward their target size, so growing and shrinking is smooth.</summary>
        private void OnFrame(object sender, EventArgs e)
        {
            var targets = TargetSizes(_mouse);
            bool moving = false;
            for (int i = 0; i < _cells.Count; i++)
            {
                var c = _cells[i];
                if (c.IsSeparator) continue;
                double cur = c.Element.Width, target = targets[i];
                double next = cur + (target - cur) * 0.32;
                if (Math.Abs(target - next) < 0.25) next = target;
                else moving = true;
                if (next != cur)
                {
                    c.Element.Width = next;
                    c.Element.Height = next;
                }
            }
            if (!moving)
            {
                CompositionTarget.Rendering -= OnFrame;
                _animating = false;
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

        private void OpenItem(DockItem item)
        {
            // Like the taskbar: a running app comes to the front (or minimizes if it's already
            // in front); otherwise it's launched.
            RefreshApps();
            if (!WindowList.ActivateOrMinimize(WindowList.Find(_apps, KeyOf(item))))
                ShellActions.Launch(item);
        }

        private void SwitchTo(string key, string exePath)
        {
            RefreshApps();
            if (!WindowList.ActivateOrMinimize(WindowList.Find(_apps, key)) && File.Exists(exePath))
                ShellActions.Launch(new DockItem { Target = exePath });
        }

        private void QuitApp(string key)
        {
            RefreshApps();
            var app = WindowList.Find(_apps, key);
            if (app != null) WindowList.Close(app);
        }

        private void PinOpenApp(WindowList.AppGroup app)
        {
            _config.DockItems ??= new List<DockItem>();
            _config.DockItems.Add(new DockItem { Name = app.Name, Target = app.ExePath });
            _saveConfig();
            BuildItems();
        }

        /// <summary>Runs every second: rebuilds the open-apps section if it changed, updates dots.</summary>
        private void UpdateRunning()
        {
            RefreshApps();
            string open = string.Join("|", UnpinnedOpenApps().Select(a => a.Key));
            if (open != _shownOpenApps) BuildItems();
            else UpdateDots();
        }

        private void UpdateDots()
        {
            foreach (var c in _cells.Where(c => c.Dot != null))
                c.Dot.Visibility = _theme.DockShowRunning && c.AppKey != null && WindowList.Find(_apps, c.AppKey) != null
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
                if (_backdrop != null)
                {
                    _backdrop.Topmost = !fullScreen;
                    _backdrop.Visibility = Visibility;
                }
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
