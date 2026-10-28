using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WindowsPersonalizer.Model;
using WindowsPersonalizer.Native;
using WindowsPersonalizer.Services;

namespace WindowsPersonalizer.Shell
{
    /// <summary>A menu bar across the top of the screen in macOS or GNOME style.</summary>
    public sealed class TopBarWindow : Window
    {
        private const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";

        private readonly ThemeSettings _theme;
        private readonly Action _openSettings;
        private readonly double _height;
        private readonly Brush _foreground;
        private readonly Brush _hover;
        private readonly bool _dark;
        private readonly Color _tint;
        private readonly Border _frame = new Border();
        private readonly TextBlock _clock = new TextBlock();
        private readonly TextBlock _appName = new TextBlock();
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        private readonly Dictionary<uint, string> _appNames = new Dictionary<uint, string>();
        private readonly uint _ownPid = (uint)Environment.ProcessId;
        private AppBar _appBar;

        public TopBarWindow(ThemeSettings theme, Action openSettings)
        {
            _theme = theme;
            _openSettings = openSettings;
            bool gnome = theme.TopBarStyle == TopBarStyle.Gnome;
            _height = gnome ? 30 : 28;

            Color fg;
            _dark = gnome || theme.DarkSystem;
            if (gnome)
            {
                _tint = Color.FromRgb(0x0E, 0x0E, 0x10);
                fg = Color.FromRgb(0xF2, 0xF2, 0xF2);
            }
            else if (theme.DarkSystem)
            {
                _tint = Color.FromRgb(0x1C, 0x1C, 0x20);
                fg = Color.FromRgb(0xF5, 0xF5, 0xF7);
            }
            else
            {
                _tint = Color.FromRgb(0xF6, 0xF6, 0xF8);
                fg = Color.FromRgb(0x1D, 0x1D, 0x1F);
            }
            _foreground = new SolidColorBrush(fg);
            _hover = new SolidColorBrush(Color.FromArgb(50, fg.R, fg.G, fg.B));

            Title = "Top bar";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            Height = _height;
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
            FontSize = 13;
            Foreground = _foreground;

            ApplyMaterial(glass: false);

            // A hairline along the bottom edge separates the bar from windows below it.
            _frame.BorderThickness = new Thickness(0, 0, 0, 1);
            _frame.BorderBrush = new SolidColorBrush(_dark ? Color.FromArgb(120, 0, 0, 0) : Color.FromArgb(28, 0, 0, 0));
            _frame.Child = gnome ? BuildGnome() : BuildMac();
            Content = _frame;

            _timer.Tick += (_, __) => Tick();
            Tick();
            _timer.Start();

            SourceInitialized += OnSourceInitialized;
            Closing += (_, __) => { _timer.Stop(); _appBar?.Dispose(); };
        }

        // ---- Layouts ----------------------------------------------------------------

        private UIElement BuildMac()
        {
            var grid = new DockPanel { LastChildFill = false, Margin = new Thickness(8, 0, 8, 0) };

            var logo = BarButton(LogoGlyph(), null);
            logo.ToolTip = "System menu";
            logo.Click += (_, __) => ShowMenu(logo, SystemMenu());
            DockPanel.SetDock(logo, Dock.Left);
            grid.Children.Add(logo);

            _appName.FontWeight = FontWeights.SemiBold;
            _appName.VerticalAlignment = VerticalAlignment.Center;
            _appName.Margin = new Thickness(8, 0, 8, 0);
            DockPanel.SetDock(_appName, Dock.Left);
            grid.Children.Add(_appName);

            // Right side, added right-to-left.
            var clock = BarButton(_clock, () => ShellActions.OpenNotifications());
            DockPanel.SetDock(clock, Dock.Right);
            grid.Children.Add(clock);

            foreach (var (glyph, tip, action) in new (string, string, Action)[]
            {
                ("", "Notifications", ShellActions.OpenNotifications),
                ("", "Control Center", ShellActions.OpenQuickSettings),
                ("", "Search", ShellActions.OpenSearch),
                ("", "Sound", ShellActions.OpenQuickSettings),
                ("", "Wi-Fi", ShellActions.OpenQuickSettings),
            })
            {
                var b = BarButton(Glyph(glyph), action);
                b.ToolTip = tip;
                DockPanel.SetDock(b, Dock.Right);
                grid.Children.Add(b);
            }
            return grid;
        }

        private UIElement BuildGnome()
        {
            var grid = new Grid { Margin = new Thickness(6, 0, 6, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new StackPanel { Orientation = Orientation.Horizontal };
            var activities = BarButton(new TextBlock { Text = "Activities", FontWeight = FontWeights.SemiBold }, ShellActions.OpenTaskView);
            activities.ToolTip = "Show all windows";
            left.Children.Add(activities);
            var apps = BarButton(Glyph(""), ShellActions.OpenStart);
            apps.ToolTip = "Show Applications";
            left.Children.Add(apps);
            _appName.VerticalAlignment = VerticalAlignment.Center;
            _appName.Margin = new Thickness(8, 0, 0, 0);
            _appName.Opacity = 0.85;
            left.Children.Add(_appName);
            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            _clock.FontWeight = FontWeights.SemiBold;
            var clock = BarButton(_clock, ShellActions.OpenNotifications);
            Grid.SetColumn(clock, 1);
            grid.Children.Add(clock);

            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var search = BarButton(Glyph("\uE721"), ShellActions.OpenSearch);
            search.ToolTip = "Search";
            right.Children.Add(search);
            var status = new StackPanel { Orientation = Orientation.Horizontal };
            status.Children.Add(Glyph(""));
            status.Children.Add(Glyph(""));
            var statusBtn = BarButton(status, ShellActions.OpenQuickSettings);
            statusBtn.ToolTip = "Quick settings";
            right.Children.Add(statusBtn);
            var power = BarButton(Glyph(""), null);
            power.Click += (_, __) => ShowMenu(power, SystemMenu());
            right.Children.Add(power);
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);
            return grid;
        }

        /// <summary>Four small rounded squares: the system menu button.</summary>
        private UIElement LogoGlyph()
        {
            var g = new Grid { Width = 13, Height = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0) };
            for (int i = 0; i < 4; i++)
            {
                g.Children.Add(new Border
                {
                    Width = 5.5,
                    Height = 5.5,
                    CornerRadius = new CornerRadius(1.5),
                    Background = _foreground,
                    HorizontalAlignment = i % 2 == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                    VerticalAlignment = i < 2 ? VerticalAlignment.Top : VerticalAlignment.Bottom,
                });
            }
            return g;
        }

        private void ApplyMaterial(bool glass)
        {
            bool gnome = _theme.TopBarStyle == TopBarStyle.Gnome;
            byte alpha = gnome ? (byte)245 : glass ? (_dark ? (byte)120 : (byte)140) : (byte)235;
            Background = new SolidColorBrush(Color.FromArgb(alpha, _tint.R, _tint.G, _tint.B));
        }

        private TextBlock Glyph(string glyph) => new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily(IconFont),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 2, 0),
        };

        private Button BarButton(UIElement content, Action action)
        {
            var b = new Button
            {
                Content = content,
                Foreground = _foreground,
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(1, 3, 1, 3),
                Cursor = Cursors.Hand,
                Focusable = false,
                Template = ButtonTemplate(),
            };
            if (action != null)
                b.Click += (_, __) => action();
            return b;
        }

        private ControlTemplate ButtonTemplate()
        {
            // Flat button with a rounded hover highlight.
            var border = new FrameworkElementFactory(typeof(Border), "bd");
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);

            var t = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, _hover, "bd"));
            t.Triggers.Add(hover);
            return t;
        }

        // ---- Menus ------------------------------------------------------------------

        private ContextMenu SystemMenu()
        {
            var m = MenuTheme.Create(_dark, ColorUtil.Parse(_theme.AccentColor, Colors.DodgerBlue));
            m.Items.Add(Item("About This PC", () => ShellActions.Open("ms-settings:about")));
            m.Items.Add(new Separator());
            m.Items.Add(Item("System Settings…", () => ShellActions.Open("ms-settings:")));
            m.Items.Add(Item("App Store…", () => ShellActions.Open("ms-windows-store:")));
            m.Items.Add(Item("Personalizer…", _openSettings));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Force Quit… (Task Manager)", () => ShellActions.Open("taskmgr.exe")));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Sleep", ShellActions.Sleep));
            m.Items.Add(Item("Restart…", () => Confirm("restart", ShellActions.Restart)));
            m.Items.Add(Item("Shut Down…", () => Confirm("shut down", ShellActions.ShutDown)));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Lock Screen", ShellActions.Lock));
            m.Items.Add(Item($"Log Out {Environment.UserName}…", () => Confirm("log out", ShellActions.SignOut)));
            return m;
        }

        private static void Confirm(string what, Action action)
        {
            if (MessageBox.Show($"Are you sure you want to {what} your computer now?", "Confirm",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                action();
        }

        private static MenuItem Item(string header, Action action)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, __) =>
            {
                try { action(); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Top bar", MessageBoxButton.OK, MessageBoxImage.Warning); }
            };
            return mi;
        }

        private static void ShowMenu(FrameworkElement anchor, ContextMenu menu)
        {
            menu.PlacementTarget = anchor;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        // ---- Clock and focused app -----------------------------------------------------

        private void Tick()
        {
            var now = DateTime.Now;
            string time = now.ToString(_theme.Clock24h ? "HH:mm" : "h:mm tt", CultureInfo.CurrentCulture);
            string date = _theme.TopBarStyle == TopBarStyle.Gnome
                ? now.ToString("MMM d", CultureInfo.CurrentCulture)
                : now.ToString("ddd MMM d", CultureInfo.CurrentCulture);
            _clock.Text = _theme.ClockShowDate ? $"{date}  {time}" : time;

            try
            {
                string name = ForegroundAppName();
                if (name != null) _appName.Text = name;
            }
            catch
            {
                // Window went away between calls; keep the old name.
            }
        }

        private string ForegroundAppName()
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero) return null;
            NativeMethods.GetWindowThreadProcessId(fg, out uint pid);
            if (pid == _ownPid) return null;

            var cls = new StringBuilder(64);
            NativeMethods.GetClassName(fg, cls, cls.Capacity);
            string c = cls.ToString();
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd") return "Desktop";

            if (_appNames.TryGetValue(pid, out var cached)) return cached;

            string name = null;
            string path = NativeMethods.GetProcessPath(pid);
            if (path != null)
            {
                if (Path.GetFileName(path).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
                    name = "File Explorer";
                else if (Path.GetFileName(path).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
                {
                    // Store apps are hosted; the window title is the best name we have.
                    var title = new StringBuilder(256);
                    NativeMethods.GetWindowText(fg, title, title.Capacity);
                    return title.Length > 0 ? title.ToString() : "App";
                }
                else
                {
                    try { name = FileVersionInfo.GetVersionInfo(path).FileDescription; } catch { }
                    if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(path);
                }
            }
            name ??= "App";
            if (name.Length > 40) name = name.Substring(0, 40) + "…";
            if (_appNames.Count > 200) _appNames.Clear();
            _appNames[pid] = name;
            return name;
        }

        // ---- Placement ------------------------------------------------------------------

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            ex |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));

            if (_theme.GlassEffect && _theme.TopBarStyle == TopBarStyle.MacOS && NativeMethods.EnableBlurBehind(hwnd))
                ApplyMaterial(glass: true);

            _appBar = new AppBar(this, ScreenEdge.Top, _height, reserveSpace: true);
            _appBar.Positioned += rc =>
            {
                Left = Dpi.ToDip(rc.Left);
                Top = Dpi.ToDip(rc.Top);
                Width = Dpi.ToDip(rc.Right - rc.Left);
                Height = _height;
            };
            _appBar.FullScreenChanged += fullScreen =>
            {
                Topmost = !fullScreen;
                Visibility = fullScreen ? Visibility.Hidden : Visibility.Visible;
            };
            _appBar.Attach();
        }
    }
}
