using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CleanRamBoosterApp.Models;

namespace CleanRamBoosterApp.Services
{
    public sealed class StartupHistoryEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string BatchLabel { get; set; } = string.Empty;
        public List<StartupHistoryItemChange> Changes { get; set; } = new();
    }

    public sealed class StartupHistoryItemChange
    {
        public string MonitorKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool PreviousEnabled { get; set; }
        public bool NewEnabled { get; set; }
    }

    /// <summary>
    /// Persists an undo stack of Startup Manager enable/disable actions at
    /// %AppData%\CleanRamBooster\startup_history.json so users can roll back changes in one click.
    /// </summary>
    public sealed class StartupHistoryService
    {
        private static StartupHistoryService? _instance;
        public static StartupHistoryService Instance => _instance ??= new StartupHistoryService();

        private readonly object _sync = new object();
        private readonly string historyFilePath;
        private List<StartupHistoryEntry> entries = new();
        private const int MaxEntries = 30;

        public event Action? HistoryChanged;

        public StartupHistoryService()
            : this(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CleanRamBooster",
                "startup_history.json"))
        {
        }

        public StartupHistoryService(string customPath)
        {
            historyFilePath = customPath;
            Load();
        }

        public int Count
        {
            get { lock (_sync) return entries.Count; }
        }

        public StartupHistoryEntry? PeekLatest()
        {
            lock (_sync)
                return entries.Count > 0 ? entries[entries.Count - 1] : null;
        }

        public List<StartupHistoryEntry> GetHistorySnapshot()
        {
            lock (_sync)
                return entries.AsEnumerable().Reverse().ToList();
        }

        public void RecordChanges(string batchLabel, IEnumerable<(StartupItem Item, bool PreviousEnabled, bool NewEnabled)> changes)
        {
            var list = new List<StartupHistoryItemChange>();
            foreach (var (item, prev, next) in changes)
            {
                if (item == null || prev == next) continue;
                string key = item.MonitorKey;
                if (string.IsNullOrWhiteSpace(key)) continue;

                list.Add(new StartupHistoryItemChange
                {
                    MonitorKey = key,
                    DisplayName = item.Name,
                    PreviousEnabled = prev,
                    NewEnabled = next
                });
            }

            if (list.Count == 0) return;

            lock (_sync)
            {
                entries.Add(new StartupHistoryEntry
                {
                    Timestamp = DateTime.Now,
                    BatchLabel = batchLabel,
                    Changes = list
                });

                while (entries.Count > MaxEntries)
                    entries.RemoveAt(0);

                SaveUnsafe();
            }

            DiagnosticLogger.LogInfo(nameof(StartupHistoryService), $"Recorded '{batchLabel}' ({list.Count} item(s))");
            HistoryChanged?.Invoke();
        }

        public StartupHistoryEntry? PopLatest()
        {
            StartupHistoryEntry? popped = null;
            lock (_sync)
            {
                if (entries.Count == 0) return null;
                popped = entries[entries.Count - 1];
                entries.RemoveAt(entries.Count - 1);
                SaveUnsafe();
            }
            HistoryChanged?.Invoke();
            return popped;
        }

        public void PushBack(StartupHistoryEntry entry)
        {
            if (entry == null) return;
            lock (_sync)
            {
                entries.Add(entry);
                while (entries.Count > MaxEntries)
                    entries.RemoveAt(0);
                SaveUnsafe();
            }
            HistoryChanged?.Invoke();
        }

        public void Clear()
        {
            lock (_sync)
            {
                entries.Clear();
                SaveUnsafe();
            }
            HistoryChanged?.Invoke();
        }

        private void Load()
        {
            lock (_sync)
            {
                try
                {
                    if (File.Exists(historyFilePath))
                    {
                        var loaded = JsonSerializer.Deserialize<List<StartupHistoryEntry>>(File.ReadAllText(historyFilePath));
                        if (loaded != null) entries = loaded;
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticLogger.LogWarning(nameof(StartupHistoryService), "Failed to load startup undo history", ex);
                    entries = new List<StartupHistoryEntry>();
                }
            }
        }

        private void SaveUnsafe()
        {
            try
            {
                string? dir = Path.GetDirectoryName(historyFilePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
                string tmp = historyFilePath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, historyFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.LogError(nameof(StartupHistoryService), "Failed to persist startup undo history", ex);
            }
        }
    }
}
