using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CleanRamBoosterApp.Models;

namespace CleanRamBoosterApp.Services
{
    public sealed class StartupMonitorState
    {
        public bool Initialized { get; set; }
        public Dictionary<string, bool> KnownEnabledStates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> IgnoredKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> UnknownKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Watches Windows startup entry points (Registry Run, Startup Folders, Scheduled Tasks)
    /// and Microsoft Store background permissions to alert when a new app is added or re-enabled.
    /// </summary>
    public sealed class StartupMonitorService : IDisposable
    {
        private readonly StartupService startupService;
        private readonly Dispatcher dispatcher;
        private readonly string stateFilePath;
        private readonly object stateLock = new();
        private readonly DispatcherTimer pollTimer;
        private readonly DispatcherTimer debounceTimer;
        private readonly List<FileSystemWatcher> folderWatchers = new();

        private StartupMonitorState state;
        private Dictionary<string, string>? cachedShellNames;
        private HashSet<string>? prevStoreFamilies;
        private int pollCount;
        private int isChecking;
        private bool isDisposed;

        public event Action<StartupItem>? NewStartupItemDetected;
        public event Action? MonitorStateChanged;

        public StartupMonitorService(StartupService service, Dispatcher uiDispatcher)
        {
            startupService = service;
            dispatcher = uiDispatcher;

            string appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CleanRamBooster");
            Directory.CreateDirectory(appDir);
            stateFilePath = Path.Combine(appDir, "startup_monitor.json");
            state = LoadState();

            pollTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.FromSeconds(4)
            };
            pollTimer.Tick += async (s, e) => await CheckForNewStartupItemsAsync();

            debounceTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(900)
            };
            debounceTimer.Tick += async (s, e) =>
            {
                debounceTimer.Stop();
                await CheckForNewStartupItemsAsync();
            };
        }

        public void Start()
        {
            if (isDisposed) return;

            InitStartupFolderWatchers();
            pollTimer.Start();

            // Run an initial check immediately (silently captures baseline on first-ever launch,
            // or surfaces any enabled items added while PC Boost was closed).
            _ = CheckForNewStartupItemsAsync();
        }

        public void Stop()
        {
            pollTimer.Stop();
            debounceTimer.Stop();
            foreach (var watcher in folderWatchers)
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                catch { }
            }
            folderWatchers.Clear();
        }

        public void ApplyMonitorFlags(IEnumerable<StartupItem> items)
        {
            if (items == null) return;
            lock (stateLock)
            {
                foreach (var item in items)
                {
                    string key = item.MonitorKey;
                    item.IsIgnored = state.IgnoredKeys.Contains(key);
                    item.IsUnknown = !item.IsIgnored && item.IsEnabled && state.UnknownKeys.Contains(key);
                }
            }
        }

        public int GetUnknownCount()
        {
            lock (stateLock)
            {
                return state.UnknownKeys.Count(k => !state.IgnoredKeys.Contains(k));
            }
        }

        public int GetIgnoredCount()
        {
            lock (stateLock)
            {
                return state.IgnoredKeys.Count;
            }
        }

        public void MarkKnownState(StartupItem item)
        {
            if (item == null) return;
            lock (stateLock)
            {
                string key = item.MonitorKey;
                state.KnownEnabledStates[key] = item.IsEnabled;
                state.UnknownKeys.Remove(key);
                item.IsUnknown = false;
                SaveStateUnsafe();
            }
            RaiseMonitorStateChanged();
        }

        public void MarkRemoved(StartupItem item)
        {
            if (item == null) return;
            lock (stateLock)
            {
                string key = item.MonitorKey;
                state.KnownEnabledStates.Remove(key);
                state.UnknownKeys.Remove(key);
                item.IsUnknown = false;
                SaveStateUnsafe();
            }
            RaiseMonitorStateChanged();
        }

        public void IgnoreItem(StartupItem item)
        {
            if (item == null) return;
            lock (stateLock)
            {
                string key = item.MonitorKey;
                state.IgnoredKeys.Add(key);
                state.UnknownKeys.Remove(key);
                state.KnownEnabledStates[key] = item.IsEnabled;
                item.IsIgnored = true;
                item.IsUnknown = false;
                SaveStateUnsafe();
            }
            RaiseMonitorStateChanged();
        }

        public void UnignoreItem(StartupItem item)
        {
            if (item == null) return;
            lock (stateLock)
            {
                string key = item.MonitorKey;
                state.IgnoredKeys.Remove(key);
                state.KnownEnabledStates[key] = item.IsEnabled;
                item.IsIgnored = false;
                item.IsUnknown = state.UnknownKeys.Contains(key) && item.IsEnabled;
                SaveStateUnsafe();
            }
            RaiseMonitorStateChanged();
        }

        public void MarkAllUnknownAsKnown()
        {
            lock (stateLock)
            {
                if (state.UnknownKeys.Count == 0) return;
                state.UnknownKeys.Clear();
                SaveStateUnsafe();
            }
            RaiseMonitorStateChanged();
        }

        public void ClearIgnoredItems()
        {
            lock (stateLock)
            {
                state.IgnoredKeys.Clear();
                SaveStateUnsafe();
            }
            RaiseMonitorStateChanged();
        }

        private void RaiseMonitorStateChanged()
        {
            if (isDisposed) return;
            _ = dispatcher.BeginInvoke(new Action(() => MonitorStateChanged?.Invoke()), DispatcherPriority.Normal);
        }

        public async Task CheckForNewStartupItemsAsync()
        {
            if (isDisposed || Interlocked.CompareExchange(ref isChecking, 1, 0) != 0)
                return;

            try
            {
                int tick = Interlocked.Increment(ref pollCount);
                bool includeTasks = (tick == 1) || (tick % 6 == 0);

                Dictionary<string, string>? shellSnapshot;
                HashSet<string>? familySnapshot;
                lock (stateLock)
                {
                    shellSnapshot = cachedShellNames;
                    familySnapshot = prevStoreFamilies;
                }

                Dictionary<string, string> nextShellNames = new(StringComparer.OrdinalIgnoreCase);
                HashSet<string> nextFamilies = new(StringComparer.OrdinalIgnoreCase);

                var currentItems = await Task.Run(() =>
                    startupService.GetStartupItemsForMonitor(
                        includeTasks,
                        shellSnapshot,
                        familySnapshot,
                        out nextShellNames,
                        out nextFamilies));

                if (isDisposed) return;

                List<StartupItem> newlyDetected = new();
                bool alertsEnabled = SettingsService.Instance.Current.StartupAlertEnabled;

                lock (stateLock)
                {
                    cachedShellNames = nextShellNames;
                    prevStoreFamilies = nextFamilies;

                    var presentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    bool stateChanged = false;

                    if (!state.Initialized)
                    {
                        foreach (var item in currentItems)
                        {
                            state.KnownEnabledStates[item.MonitorKey] = item.IsEnabled;
                        }
                        state.Initialized = true;
                        SaveStateUnsafe();
                        return;
                    }

                    foreach (var item in currentItems)
                    {
                        string key = item.MonitorKey;
                        presentKeys.Add(key);

                        bool hadKnown = state.KnownEnabledStates.TryGetValue(key, out bool wasEnabled);
                        bool isIgnored = state.IgnoredKeys.Contains(key);
                        item.IsIgnored = isIgnored;

                        if (item.IsEnabled &&
                            !IsSelfEntry(item) &&
                            !isIgnored &&
                            (!hadKnown || !wasEnabled))
                        {
                            state.UnknownKeys.Add(key);
                            item.IsUnknown = true;
                            newlyDetected.Add(item);
                            stateChanged = true;
                        }
                        else if (!item.IsEnabled || isIgnored)
                        {
                            if (state.UnknownKeys.Remove(key))
                                stateChanged = true;
                            item.IsUnknown = false;
                        }
                        else
                        {
                            item.IsUnknown = state.UnknownKeys.Contains(key);
                        }

                        if (!hadKnown || wasEnabled != item.IsEnabled)
                        {
                            state.KnownEnabledStates[key] = item.IsEnabled;
                            stateChanged = true;
                        }
                    }

                    // Remove entries that are no longer present in the scanned sources
                    // so re-adding an app later triggers an alert again.
                    var missingKeys = state.KnownEnabledStates.Keys
                        .Where(k => !presentKeys.Contains(k) &&
                                    (includeTasks || !k.StartsWith("ScheduledTask|", StringComparison.OrdinalIgnoreCase)))
                        .ToList();

                    foreach (var mk in missingKeys)
                    {
                        state.KnownEnabledStates.Remove(mk);
                        state.UnknownKeys.Remove(mk);
                        stateChanged = true;
                    }

                    if (stateChanged)
                    {
                        SaveStateUnsafe();
                    }

                    if ((newlyDetected.Count > 0 || stateChanged) && !isDisposed)
                    {
                        var detectedCopy = newlyDetected.ToList();
                        _ = dispatcher.BeginInvoke(new Action(() =>
                        {
                            foreach (var item in detectedCopy)
                            {
                                NewStartupItemDetected?.Invoke(item);
                            }
                            MonitorStateChanged?.Invoke();
                        }), DispatcherPriority.Normal);
                    }
                }
            }
            catch { }
            finally
            {
                Interlocked.Exchange(ref isChecking, 0);
            }
        }

        private void InitStartupFolderWatchers()
        {
            if (folderWatchers.Count > 0) return;

            TryWatchFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup));
            TryWatchFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup));
        }

        private void TryWatchFolder(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

                var watcher = new FileSystemWatcher(path)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };

                FileSystemEventHandler onChanged = (s, e) =>
                {
                    if (!isDisposed)
                    {
                        _ = dispatcher.BeginInvoke(new Action(() =>
                        {
                            debounceTimer.Stop();
                            debounceTimer.Start();
                        }));
                    }
                };

                RenamedEventHandler onRenamed = (s, e) =>
                {
                    if (!isDisposed)
                    {
                        _ = dispatcher.BeginInvoke(new Action(() =>
                        {
                            debounceTimer.Stop();
                            debounceTimer.Start();
                        }));
                    }
                };

                watcher.Created += onChanged;
                watcher.Renamed += onRenamed;
                folderWatchers.Add(watcher);
            }
            catch { }
        }

        private static bool IsSelfEntry(StartupItem item)
        {
            string val = item.RegistryValueName ?? string.Empty;
            string name = item.Name ?? string.Empty;
            string file = item.FilePath ?? string.Empty;
            string cmd = item.Command ?? string.Empty;
            string selfExe = Environment.ProcessPath ?? string.Empty;

            if (item.LocationType == StartupType.ScheduledTask &&
                !string.IsNullOrEmpty(item.TaskName) &&
                item.TaskName.StartsWith(@"Microsoft\Windows\", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (val.Equals("CleanRamBoosterApp", StringComparison.OrdinalIgnoreCase) ||
                val.Equals("PCBoost", StringComparison.OrdinalIgnoreCase) ||
                val.Equals(AutoStartService.AdminTaskName, StringComparison.OrdinalIgnoreCase) ||
                (item.TaskName?.Contains(AutoStartService.AdminTaskName, StringComparison.OrdinalIgnoreCase) ?? false) ||
                name.Contains("CleanRamBooster", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(file) && file.EndsWith("CleanRamBoosterApp.exe", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(cmd) && cmd.Contains("CleanRamBoosterApp.exe", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(selfExe) && !string.IsNullOrEmpty(file) &&
                string.Equals(Path.GetFullPath(file), Path.GetFullPath(selfExe), StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private StartupMonitorState LoadState()
        {
            try
            {
                if (File.Exists(stateFilePath))
                {
                    string json = File.ReadAllText(stateFilePath);
                    var loaded = JsonSerializer.Deserialize<StartupMonitorState>(json);
                    if (loaded != null)
                    {
                        loaded.KnownEnabledStates = (loaded.KnownEnabledStates ?? new())
                            .Where(kv => !kv.Key.StartsWith(@"ScheduledTask|microsoft\windows\", StringComparison.OrdinalIgnoreCase))
                            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
                        loaded.IgnoredKeys = new HashSet<string>(
                            loaded.IgnoredKeys ?? new(),
                            StringComparer.OrdinalIgnoreCase);
                        loaded.UnknownKeys = new HashSet<string>(
                            (loaded.UnknownKeys ?? new())
                            .Where(k => !k.StartsWith(@"ScheduledTask|microsoft\windows\", StringComparison.OrdinalIgnoreCase)),
                            StringComparer.OrdinalIgnoreCase);
                        return loaded;
                    }
                }
            }
            catch { }

            return new StartupMonitorState();
        }

        private void SaveStateUnsafe()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(stateFilePath, JsonSerializer.Serialize(state, options));
            }
            catch { }
        }

        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;
            Stop();
        }
    }
}
