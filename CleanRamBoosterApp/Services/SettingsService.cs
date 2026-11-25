using System;
using System.IO;
using System.Text.Json;

namespace CleanRamBoosterApp.Services
{
    public class AppSettings
    {
        public const int DefaultAutoCleanCooldownSeconds = 900; // 15 minutes (SSD/NVMe/HDD friendly)

        public bool IsDarkMode { get; set; } = false;
        public bool StartWithWindows { get; set; } = false;
        public bool RunAsAdminOnBoot { get; set; } = false;
        public bool MinimizeToTray { get; set; } = true;
        public bool AutoCleanEnabled { get; set; } = true;
        public int AutoCleanThresholdPercent { get; set; } = 85;
        public int AutoCleanIntervalSeconds { get; set; } = DefaultAutoCleanCooldownSeconds;
        public bool TurboGameMode { get; set; } = false;
        public bool DisableRamCleanNotifications { get; set; } = false;
        public bool StartupAlertEnabled { get; set; } = true;
        public bool IgnoreTurboDashboardCard { get; set; } = false;
        public bool IgnoreStartupDashboardCard { get; set; } = false;
        public bool CreateRestorePointBeforeCare { get; set; } = false;

        [System.Text.Json.Serialization.JsonIgnore]
        public bool ShowTrayNotifications
        {
            get => !DisableRamCleanNotifications;
            set => DisableRamCleanNotifications = !value;
        }
        public string StartupSortColumn { get; set; } = "Status";
        public bool StartupSortAscending { get; set; } = true;
        public string Language { get; set; } = "en";
        public bool IsArabic => string.Equals(Language, "ar", StringComparison.OrdinalIgnoreCase);
    }

    public class SettingsService
    {
        private static SettingsService? _instance;
        public static SettingsService Instance => _instance ??= new SettingsService();

        private readonly string configFilePath;
        public AppSettings Current { get; private set; }

        public event Action? OnSettingsChanged;

        public SettingsService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appDir = Path.Combine(appData, "CleanRamBooster");
            Directory.CreateDirectory(appDir);
            configFilePath = Path.Combine(appDir, "settings.json");

            Current = LoadSettings();
        }

        private static readonly object _saveSync = new object();

        public AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(configFilePath))
                {
                    string json = File.ReadAllText(configFilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null) return NormalizeSettings(settings);
                }
            }
            catch (Exception ex)
            {
                DiagnosticLogger.LogWarning(nameof(SettingsService), $"Failed to load settings from {configFilePath}, falling back to defaults", ex);
                // Try backup file if main file got corrupted
                try
                {
                    string bak = configFilePath + ".bak";
                    if (File.Exists(bak))
                    {
                        var recovered = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(bak));
                        if (recovered != null) return NormalizeSettings(recovered);
                    }
                }
                catch { }
            }

            return new AppSettings();
        }

        public static AppSettings NormalizeSettings(AppSettings settings)
        {
            // Upgrade legacy 60s cooldown or invalid values to the 15-minute (900s) minimum cooldown
            if (settings.AutoCleanIntervalSeconds < AppSettings.DefaultAutoCleanCooldownSeconds)
            {
                settings.AutoCleanIntervalSeconds = AppSettings.DefaultAutoCleanCooldownSeconds;
            }
            if (settings.AutoCleanThresholdPercent < 70 || settings.AutoCleanThresholdPercent > 90)
            {
                settings.AutoCleanThresholdPercent = 85;
            }
            return settings;
        }

        public void SaveSettings()
        {
            lock (_saveSync)
            {
                try
                {
                    string? dir = Path.GetDirectoryName(configFilePath);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);

                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string json = JsonSerializer.Serialize(Current, options);

                    string tmpPath = configFilePath + ".tmp";
                    string bakPath = configFilePath + ".bak";

                    File.WriteAllText(tmpPath, json);
                    if (File.Exists(configFilePath))
                    {
                        try { File.Copy(configFilePath, bakPath, overwrite: true); } catch { }
                    }
                    File.Move(tmpPath, configFilePath, overwrite: true);

                    OnSettingsChanged?.Invoke();
                }
                catch (Exception ex)
                {
                    DiagnosticLogger.LogError(nameof(SettingsService), $"Failed to save settings to {configFilePath}", ex);
                }
            }
        }

        public void Update(Action<AppSettings> updateAction)
        {
            updateAction(Current);
            SaveSettings();
        }
    }
}
