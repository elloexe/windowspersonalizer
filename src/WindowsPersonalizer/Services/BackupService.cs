using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using WindowsPersonalizer.Model;

namespace WindowsPersonalizer.Services
{
    /// <summary>
    /// Snapshots the user's original Windows look the first time a theme is applied,
    /// so "Restore original look" can put every value back exactly as it was.
    /// </summary>
    public static class BackupService
    {
        public class Entry
        {
            public string Key { get; set; }
            public string Name { get; set; }
            public bool Existed { get; set; }
            public string Kind { get; set; }
            public long Number { get; set; }
            public string Text { get; set; }
            public string Base64 { get; set; }
        }

        public class Snapshot
        {
            public DateTime Taken { get; set; }
            public List<Entry> Values { get; set; } = new List<Entry>();
            public bool TaskbarAutoHide { get; set; }
            public string WallpaperPath { get; set; }
        }

        public static bool HasBackup => File.Exists(ConfigStore.BackupPath);

        public static void EnsureBackup()
        {
            if (HasBackup) return;

            var snap = new Snapshot
            {
                Taken = DateTime.Now,
                TaskbarAutoHide = TaskbarService.IsAutoHide(),
                WallpaperPath = WallpaperService.GetCurrentWallpaperPath(),
            };

            foreach (var v in Reg.All)
            {
                var e = new Entry { Key = v.Key, Name = v.Name };
                try
                {
                    using var k = Registry.CurrentUser.OpenSubKey(v.Key, false);
                    object val = k?.GetValue(v.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (val != null)
                    {
                        e.Existed = true;
                        var kind = k.GetValueKind(v.Name);
                        e.Kind = kind.ToString();
                        switch (kind)
                        {
                            case RegistryValueKind.DWord: e.Number = (int)val; break;
                            case RegistryValueKind.QWord: e.Number = (long)val; break;
                            case RegistryValueKind.Binary: e.Base64 = Convert.ToBase64String((byte[])val); break;
                            default: e.Text = val.ToString(); break;
                        }
                    }
                }
                catch
                {
                    // Unreadable value: treat as missing.
                }
                snap.Values.Add(e);
            }

            Directory.CreateDirectory(ConfigStore.Folder);
            File.WriteAllText(ConfigStore.BackupPath, JsonSerializer.Serialize(snap, ConfigStore.Json));
        }

        /// <summary>Puts the original values back. Returns a list of values that could not be restored.</summary>
        public static List<string> Restore()
        {
            var failures = new List<string>();
            if (!HasBackup) return failures;

            var snap = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(ConfigStore.BackupPath), ConfigStore.Json);
            foreach (var e in snap.Values)
            {
                try
                {
                    using var k = Registry.CurrentUser.CreateSubKey(e.Key, true);
                    if (!e.Existed)
                    {
                        k.DeleteValue(e.Name, false);
                        continue;
                    }
                    var kind = Enum.Parse<RegistryValueKind>(e.Kind);
                    switch (kind)
                    {
                        case RegistryValueKind.DWord: k.SetValue(e.Name, (int)e.Number, kind); break;
                        case RegistryValueKind.QWord: k.SetValue(e.Name, e.Number, kind); break;
                        case RegistryValueKind.Binary: k.SetValue(e.Name, Convert.FromBase64String(e.Base64), kind); break;
                        default: k.SetValue(e.Name, e.Text ?? "", kind); break;
                    }
                }
                catch
                {
                    failures.Add(e.Name);
                }
            }

            TaskbarService.SetHidden(false);
            TaskbarService.SetAutoHide(snap.TaskbarAutoHide);
            if (!string.IsNullOrEmpty(snap.WallpaperPath) && File.Exists(snap.WallpaperPath))
                WallpaperService.SetWallpaper(snap.WallpaperPath);
            DesktopIconsService.SetVisible(!(Reg.HideDesktopIcons.Get() is int hide && hide == 1));
            SystemTweaks.Broadcast();

            File.Delete(ConfigStore.BackupPath);
            return failures;
        }
    }
}
