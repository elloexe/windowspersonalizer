using System;
using System.Linq;
using System.Threading;
using System.Windows;
using WindowsPersonalizer.Model;
using WindowsPersonalizer.Services;
using WindowsPersonalizer.Shell;
using WinForms = System.Windows.Forms;

namespace WindowsPersonalizer
{
    public partial class App : Application
    {
        private const string MutexName = "WindowsPersonalizer_SingleInstance";
        private const string ShowEventName = "WindowsPersonalizer_ShowWindow";

        private Mutex _mutex;
        private EventWaitHandle _showEvent;
        private WinForms.NotifyIcon _tray;
        private MainWindow _main;

        public AppConfig Config { get; private set; }
        public ShellManager Shell { get; private set; }

        public static new App Current => (App)Application.Current;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Only one copy: a second launch just brings the first one's window up.
            _mutex = new Mutex(true, MutexName, out bool first);
            if (!first)
            {
                try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
                Shutdown();
                return;
            }
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            var listener = new Thread(() =>
            {
                while (_showEvent.WaitOne())
                    Dispatcher.BeginInvoke(new Action(ShowMainWindow));
            }) { IsBackground = true };
            listener.Start();

            // Never leave the user without a taskbar if something goes badly wrong.
            AppDomain.CurrentDomain.UnhandledException += (_, __) => SafeRestoreTaskbar();
            DispatcherUnhandledException += (_, args) =>
            {
                MessageBox.Show("Something went wrong:\n\n" + args.Exception.Message, "Windows Personalizer",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            Config = ConfigStore.Load();
            MigrateOldDockSettings();
            Shell = new ShellManager(Config, ShowMainWindow);
            CreateTrayIcon();

            bool background = e.Args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));
            if (Config.Applied)
            {
                try { Shell.Apply(); }
                catch (Exception ex)
                {
                    MessageBox.Show("Couldn't start the dock / top bar:\n\n" + ex.Message, "Windows Personalizer");
                }
            }

            if (!background || !Config.Applied)
                ShowMainWindow();
            else if (!Shell.IsRunning)
                ExitApp(); // Started with Windows but there's nothing to run.
        }

        private void MigrateOldDockSettings()
        {
            if (Config.EdgeDefaultRemoved && Config.ImportedTaskbarPins) return;
            Config.DockItems?.RemoveAll(d =>
                (d.Target ?? "").EndsWith(@"\msedge.exe", StringComparison.OrdinalIgnoreCase));
            Config.EdgeDefaultRemoved = true;

            // Older versions started the Dock with a fixed app list; switch once to the user's own taskbar apps.
            if (!Config.ImportedTaskbarPins)
            {
                try
                {
                    if (Config.DockItems != null && ShellActions.TaskbarPins().Count > 0)
                        Config.DockItems = ShellActions.DefaultDockItems();
                }
                catch { }
                Config.ImportedTaskbarPins = true;
            }
            try { ConfigStore.Save(Config); } catch { }
        }

        public void ShowMainWindow()
        {
            if (_main == null)
            {
                _main = new MainWindow();
                _main.Closed += (_, __) => _main = null;
            }
            _main.Show();
            if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
            _main.Activate();
        }

        /// <summary>Called when the main window closes: keep running in the tray if the dock/top bar is active.</summary>
        public void OnMainWindowClosed()
        {
            if (Shell.IsRunning)
            {
                if (!Config.ShownTrayHint)
                {
                    _tray.ShowBalloonTip(4000, "Still running",
                        "Your dock and top bar keep running here. Right-click this icon to open settings or exit.",
                        WinForms.ToolTipIcon.Info);
                    Config.ShownTrayHint = true;
                    ConfigStore.Save(Config);
                }
            }
            else
            {
                ExitApp();
            }
        }

        private void CreateTrayIcon()
        {
            System.Drawing.Icon icon;
            try
            {
                icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath) ?? System.Drawing.SystemIcons.Application;
            }
            catch
            {
                icon = System.Drawing.SystemIcons.Application;
            }

            _tray = new WinForms.NotifyIcon
            {
                Icon = icon,
                Text = "Windows Personalizer",
                Visible = true,
            };
            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("Open Personalizer", null, (_, __) => ShowMainWindow());
            menu.Items.Add("Restart dock && top bar", null, (_, __) => { if (Config.Applied) Shell.Apply(); });
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, __) => ExitApp());
            _tray.ContextMenuStrip = menu;
            _tray.MouseClick += (_, args) =>
            {
                if (args.Button == WinForms.MouseButtons.Left) ShowMainWindow();
            };
        }

        public void ExitApp()
        {
            Shell?.Stop(restoreTaskbar: true);
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }
            Shutdown();
        }

        private static void SafeRestoreTaskbar()
        {
            try
            {
                if (TaskbarService.IsHiddenByUs) TaskbarService.SetHidden(false);
            }
            catch { }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            SafeRestoreTaskbar();
            base.OnExit(e);
        }
    }
}
