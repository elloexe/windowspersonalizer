using System;
using WindowsPersonalizer.Model;
using WindowsPersonalizer.Services;

namespace WindowsPersonalizer.Shell
{
    /// <summary>Owns the dock and top bar windows and keeps them in sync with the applied theme.</summary>
    public sealed class ShellManager
    {
        private readonly AppConfig _config;
        private readonly Action _openSettings;
        private DockWindow _dock;
        private TopBarWindow _topBar;

        public ShellManager(AppConfig config, Action openSettings)
        {
            _config = config;
            _openSettings = openSettings;
        }

        public bool IsRunning => _dock != null || _topBar != null || TaskbarService.IsHiddenByUs;

        public void Apply()
        {
            Stop(restoreTaskbar: false);
            var t = _config.Theme;

            if (t.TaskbarMode == TaskbarMode.Hidden)
                TaskbarService.SetHidden(true);

            // Top bar first so the dock's space is calculated below it.
            if (t.TopBarEnabled)
            {
                _topBar = new TopBarWindow(t.Clone(), _openSettings);
                _topBar.Show();
            }

            if (t.DockEnabled)
            {
                _config.DockItems ??= ShellActions.DefaultDockItems();
                _dock = new DockWindow(t.Clone(), _config, () => ConfigStore.Save(_config), _openSettings);
                _dock.Show();
            }
        }

        public void Stop(bool restoreTaskbar)
        {
            _dock?.Close();
            _dock = null;
            _topBar?.Close();
            _topBar = null;
            if (restoreTaskbar && TaskbarService.IsHiddenByUs)
                TaskbarService.SetHidden(false);
        }
    }
}
