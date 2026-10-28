using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowsPersonalizer.Model
{
    /// <summary>Loads and saves settings under %AppData%\WindowsPersonalizer.</summary>
    public static class ConfigStore
    {
        public static string Folder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowsPersonalizer");

        public static string ConfigPath => Path.Combine(Folder, "settings.json");
        public static string BackupPath => Path.Combine(Folder, "original-look-backup.json");
        public static string WallpaperFolder => Path.Combine(Folder, "wallpapers");

        public static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), Json);
                    if (cfg != null)
                    {
                        cfg.Theme ??= Presets.Windows11();
                        cfg.Theme.Wallpaper ??= new WallpaperSpec();
                        return cfg;
                    }
                }
            }
            catch
            {
                // Corrupt settings: fall back to defaults.
            }
            return new AppConfig();
        }

        public static void Save(AppConfig cfg)
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(cfg, Json));
        }

        public static void ExportTheme(ThemeSettings theme, string path) =>
            File.WriteAllText(path, JsonSerializer.Serialize(theme, Json));

        public static ThemeSettings ImportTheme(string path)
        {
            var t = JsonSerializer.Deserialize<ThemeSettings>(File.ReadAllText(path), Json)
                    ?? throw new InvalidDataException("Not a theme file.");
            t.Wallpaper ??= new WallpaperSpec();
            return t;
        }
    }
}
