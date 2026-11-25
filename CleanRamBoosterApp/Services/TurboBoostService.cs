using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CleanRamBoosterApp.Models;

namespace CleanRamBoosterApp.Services
{
    public class TurboBoostService
    {
        // Windows Critical System Services that must NEVER be stopped
        private static readonly HashSet<string> CriticalWindowsServices = new(StringComparer.OrdinalIgnoreCase)
        {
            "rpcss", "dcomlaunch", "plugplay", "lsass", "samss", "eventlog", "netman",
            "dhcp", "dnscache", "lanmanworkstation", "lanmanserver", "bfe", "mpssvc",
            "cryptsvc", "profsvc", "gpsvc", "schedule", "nsi", "power", "audiosrv",
            "audioendpointbuilder", "winmgmt", "sppsvc", "securityhealthservice", "windefend", "msmpeng",
            "wlansvc", "netprofm", "nlasvc", "termservice", "umrdpservice", "sessionenv",
            "brokerinfrastructure", "systemeventsbroker", "usermgr", "lsm", "msiserver", "trustedinstaller"
        };

        // Core Windows UI / shell processes that must never be closed by Turbo Boost
        private static readonly HashSet<string> ProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "dwm", "csrss", "winlogon", "lsass", "svchost", "sihost",
            "runtimebroker", "textinputhost", "shellexperiencehost", "startmenuexperiencehost",
            "searchhost", "applicationframehost", "ctfmon", "conhost", "taskmgr",
            "cleanramboosterapp", "windowsterminal", "pwsh", "powershell", "cmd"
        };

        // Well-known optional Windows system services
        private static readonly Dictionary<string, string> WindowsOptionalServices = new(StringComparer.OrdinalIgnoreCase)
        {
            { "WSearch", "Windows Search Indexing (High disk and CPU activity)" },
            { "Spooler", "Print Spooler (Not needed during gaming or media sessions)" },
            { "DiagTrack", "Connected User Experiences & Telemetry Logging" },
            { "SysMain", "Superfetch / SysMain Memory Preloading" },
            { "DPS", "Diagnostic Policy Service" },
            { "TrkWks", "Distributed Link Tracking Client" },
            { "WerSvc", "Windows Error Reporting Service" },
            { "TabletInputService", "Touch Keyboard & Handwriting Panel Service" },
            { "RemoteRegistry", "Remote Registry Service" },
            { "CscService", "Offline Files Service" },
            { "SCardSvr", "Smart Card Management Service" },
            { "bthserv", "Bluetooth Support Service" },
            { "Fax", "Windows Fax Service" },
            { "MapsBroker", "Downloaded Maps Manager" },
            { "XboxGipSvc", "Xbox Accessory Management Service" },
            { "XblGameSave", "Xbox Live Game Save Service" },
            { "XboxNetApiSvc", "Xbox Live Networking Service" },
            { "RetailDemo", "Windows Retail Demo Service" },
            { "WMPNetworkSvc", "Windows Media Player Network Sharing Service" },
            { "seclogon", "Secondary Logon Service" },
            { "PcaSvc", "Program Compatibility Assistant Service" },
            { "FontCache", "Windows Font Cache Service" },
            { "WbioSrvc", "Windows Biometric Service" }
        };

        // Common background app keywords
        private static readonly HashSet<string> KnownBackgroundApps = new(StringComparer.OrdinalIgnoreCase)
        {
            "discord", "spotify", "steam", "epicgameslauncher", "origin", "battlenet",
            "onedrive", "dropbox", "googledrivefs", "skype", "teams", "slack", "zoom",
            "torrent", "utorrent", "qbittorrent", "bittorrent", "gameloop", "riotclientux",
            "galaxyclient", "icue", "razer synapse", "armourycraterowire", "cortex",
            "telegram", "whatsapp", "itunes", "creativecloud", "adobedesktopservice",
            "geforceexperience", "nvidia share", "radeonsoftware", "medal", "overwolf"
        };

        private readonly object stateLock = new();
        private readonly SemaphoreSlim transitionGate = new(1, 1);
        private CancellationTokenSource? activeTransitionCts;
        private readonly HashSet<string> previouslyStoppedServices = new(StringComparer.OrdinalIgnoreCase);

        public bool IsBoostActive { get; private set; } = false;
        public long LastSavedRamBytes { get; private set; } = 0;
        public int LastSkippedProtectedCount { get; private set; } = 0;
        public int LastRestoreFailedCount { get; private set; } = 0;

        public List<TurboBoostItem> ScanBoostCandidates(bool isGameMode)
        {
            var results = new List<TurboBoostItem>();
            var procMemByPid = new Dictionary<int, long>();
            var currentPid = Environment.ProcessId;

            // =========================================================
            // 1. SCAN BACKGROUND APPS (Category: BackgroundApps)
            // =========================================================
            foreach (var proc in Process.GetProcesses())
            {
                using (proc)
                {
                    try
                    {
                        if (proc.Id <= 4) continue;
                        long mem = 0;
                        try { mem = proc.WorkingSet64; } catch { }
                        if (mem > 0) procMemByPid[proc.Id] = mem;

                        if (proc.Id == currentPid) continue;
                        string rawName = proc.ProcessName;
                        if (ProtectedProcesses.Contains(rawName)) continue;

                        string name = rawName.ToLowerInvariant();
                        bool isCandidate = KnownBackgroundApps.Contains(name) ||
                                           (isGameMode && (name.Contains("update") || name.Contains("helper") || name.Contains("launcher")));

                        string exePath = string.Empty;
                        try
                        {
                            exePath = proc.MainModule?.FileName ?? string.Empty;
                            if (!isCandidate && !string.IsNullOrEmpty(exePath))
                            {
                                string lowerPath = exePath.ToLowerInvariant();
                                if ((lowerPath.Contains(@"\appdata\local\") || lowerPath.Contains(@"\appdata\roaming\")) &&
                                    !lowerPath.Contains(@"\microsoft\windows\"))
                                {
                                    isCandidate = true;
                                }
                            }
                        }
                        catch { }

                        if (isCandidate)
                        {
                            string displayName = !string.IsNullOrEmpty(proc.MainWindowTitle)
                                ? $"{rawName} ({proc.MainWindowTitle})"
                                : $"{rawName}.exe";

                            results.Add(new TurboBoostItem
                            {
                                Name = rawName,
                                DisplayName = displayName,
                                Description = "Running User-Mode Background Application",
                                Category = TurboCategory.BackgroundApps,
                                IsService = false,
                                ProcessId = proc.Id,
                                MemoryUsageBytes = mem,
                                ExecutablePath = exePath,
                                IsSelected = true
                            });
                        }
                    }
                    catch { }
                }
            }

            // =========================================================
            // 2. SCAN RUNNING, STOPPABLE SERVICES
            // =========================================================
            try
            {
                var services = WinServiceManager.GetServices();
                var rawCandidates = new List<(ServiceInfo Svc, bool IsNonWindows, string? SysDesc)>();

                foreach (var svc in services)
                {
                    if (CriticalWindowsServices.Contains(svc.ServiceName)) continue;

                    string imagePath = svc.ImagePath ?? string.Empty;
                    string lowerPath = imagePath.ToLowerInvariant();

                    bool isNonWindows = (!lowerPath.Contains(@"\windows\system32\") && !lowerPath.Contains(@"\windows\syswow64\")) ||
                                        lowerPath.Contains("google") || lowerPath.Contains("adobe") || lowerPath.Contains("edge") ||
                                        lowerPath.Contains("razer") || lowerPath.Contains("steam") || lowerPath.Contains("epic") ||
                                        lowerPath.Contains("corsair") || lowerPath.Contains("logitech") || lowerPath.Contains("intel") ||
                                        lowerPath.Contains("amd") || lowerPath.Contains("anydesk") || lowerPath.Contains("teamviewer");

                    if (isNonWindows)
                    {
                        rawCandidates.Add((svc, true, null));
                    }
                    else if (WindowsOptionalServices.TryGetValue(svc.ServiceName, out string? sysDesc))
                    {
                        rawCandidates.Add((svc, false, sysDesc));
                    }
                }

                // Filter to ONLY currently running, stoppable services (microsecond native SCM check)
                var runningServices = WinServiceManager.GetRunningStoppableServices(rawCandidates.Select(x => x.Svc.ServiceName));

                foreach (var (svc, isNonWindows, sysDesc) in rawCandidates)
                {
                    if (!runningServices.TryGetValue(svc.ServiceName, out var svcProc)) continue;

                    long svcMem = (svcProc.IsOwnProcess && svcProc.ProcessId > 0 && procMemByPid.TryGetValue(svcProc.ProcessId, out long ws))
                        ? ws
                        : 0L;

                    string cleanName = MuiHelper.Resolve(svc.DisplayName, svc.ServiceName);
                    if (cleanName.StartsWith("@")) cleanName = svc.ServiceName;

                    if (isNonWindows)
                    {
                        string desc = !string.IsNullOrWhiteSpace(svc.Description)
                            ? svc.Description
                            : $"Third-Party Service ({svc.ServiceName})";

                        results.Add(new TurboBoostItem
                        {
                            Name = svc.ServiceName,
                            DisplayName = cleanName,
                            Description = desc,
                            Category = TurboCategory.NonWindowsServices,
                            IsService = true,
                            ServiceName = svc.ServiceName,
                            ProcessId = svcProc.ProcessId,
                            MemoryUsageBytes = svcMem,
                            ExecutablePath = svc.ImagePath ?? string.Empty,
                            IsSelected = true
                        });
                    }
                    else
                    {
                        bool defaultSelected = isGameMode || (svc.ServiceName != "Spooler" && svc.ServiceName != "WSearch");

                        results.Add(new TurboBoostItem
                        {
                            Name = svc.ServiceName,
                            DisplayName = cleanName,
                            Description = sysDesc ?? svc.ServiceName,
                            Category = TurboCategory.SystemServices,
                            IsService = true,
                            ServiceName = svc.ServiceName,
                            ProcessId = svcProc.ProcessId,
                            MemoryUsageBytes = svcMem,
                            ExecutablePath = svc.ImagePath ?? string.Empty,
                            IsSelected = defaultSelected
                        });
                    }
                }
            }
            catch { }

            // Double check: remove any lingering @ prefixes
            foreach (var item in results)
            {
                if (item.DisplayName.StartsWith("@"))
                {
                    string fallback = !string.IsNullOrEmpty(item.ServiceName) ? item.ServiceName : item.Name;
                    item.DisplayName = MuiHelper.Resolve(item.DisplayName, fallback);
                    if (item.DisplayName.StartsWith("@")) item.DisplayName = fallback;
                }
            }

            return results;
        }

        private CancellationToken ResetTransitionIntent(bool desiredActive)
        {
            lock (stateLock)
            {
                IsBoostActive = desiredActive;
                LastSavedRamBytes = 0;
                LastSkippedProtectedCount = 0;
                LastRestoreFailedCount = 0;
                activeTransitionCts?.Cancel();
                activeTransitionCts?.Dispose();
                activeTransitionCts = new CancellationTokenSource();
                return activeTransitionCts.Token;
            }
        }

        private static long ReadAvailablePhysBytes()
        {
            try
            {
                var info = new MemoryOptimizationService().GetMemoryInfo();
                return (long)info.AvailPhysBytes;
            }
            catch
            {
                return 0L;
            }
        }

        private static long TryGetProcessWorkingSet(int pid)
        {
            if (pid <= 4) return 0L;
            try
            {
                using var p = Process.GetProcessById(pid);
                return Math.Max(0L, p.WorkingSet64);
            }
            catch
            {
                return 0L;
            }
        }

        public async Task<(int stoppedServices, int closedApps, long savedRamBytes, bool cancelled)> TurnOnTurboBoostAsync(
            List<TurboBoostItem> selectedItems,
            IProgress<(int done, int total)>? progress = null)
        {
            CancellationToken ct = ResetTransitionIntent(desiredActive: true);
            var targets = (selectedItems ?? new List<TurboBoostItem>())
                .Where(x => x.IsSelected)
                .ToList();

            return await Task.Run(async () =>
            {
                try
                {
                    await transitionGate.WaitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return (0, 0, 0L, true);
                }

                try
                {
                    if (ct.IsCancellationRequested || targets.Count == 0)
                        return (0, 0, 0L, ct.IsCancellationRequested);

                    long availBefore = ReadAvailablePhysBytes();
                    int stoppedServices = 0;
                    int closedApps = 0;
                    int skippedProtected = 0;
                    int done = 0;
                    long targetedSavedBytes = 0;
                    var countedPids = new HashSet<int>();

                    var parallelOptions = new ParallelOptions
                    {
                        MaxDegreeOfParallelism = 6,
                        CancellationToken = ct
                    };

                    try
                    {
                        await Parallel.ForEachAsync(targets, parallelOptions, (item, token) =>
                        {
                            if (token.IsCancellationRequested) return ValueTask.CompletedTask;

                            if (item.IsService && !string.IsNullOrEmpty(item.ServiceName))
                            {
                                long preMem = 0;
                                int pid = 0;
                                if (WinServiceManager.TryGetServiceProcessInfo(item.ServiceName, out int svcPid, out bool isOwn) && isOwn && svcPid > 0)
                                {
                                    pid = svcPid;
                                    preMem = TryGetProcessWorkingSet(svcPid);
                                    if (preMem == 0 && item.MemoryUsageBytes > 0) preMem = item.MemoryUsageBytes;
                                }

                                if (WinServiceManager.StopService(item.ServiceName, token, out int stopErr))
                                {
                                    lock (stateLock)
                                    {
                                        previouslyStoppedServices.Add(item.ServiceName);
                                    }
                                    Interlocked.Increment(ref stoppedServices);
                                    if (pid > 0 && preMem > 0)
                                    {
                                        lock (countedPids)
                                        {
                                            if (countedPids.Add(pid))
                                                Interlocked.Add(ref targetedSavedBytes, preMem);
                                        }
                                    }
                                }
                                else if (stopErr == WinServiceManager.ERROR_ACCESS_DENIED)
                                {
                                    Interlocked.Increment(ref skippedProtected);
                                }
                            }
                            else if (!item.IsService && item.ProcessId > 0)
                            {
                                try
                                {
                                    using var p = Process.GetProcessById(item.ProcessId);
                                    long preMem = 0;
                                    try { preMem = Math.Max(0L, p.WorkingSet64); } catch { preMem = Math.Max(0L, item.MemoryUsageBytes); }
                                    p.Kill();
                                    Interlocked.Increment(ref closedApps);
                                    if (preMem > 0)
                                    {
                                        lock (countedPids)
                                        {
                                            if (countedPids.Add(item.ProcessId))
                                                Interlocked.Add(ref targetedSavedBytes, preMem);
                                        }
                                    }
                                }
                                catch (System.ComponentModel.Win32Exception)
                                {
                                    Interlocked.Increment(ref skippedProtected);
                                }
                                catch (UnauthorizedAccessException)
                                {
                                    Interlocked.Increment(ref skippedProtected);
                                }
                                catch { }
                            }

                            progress?.Report((Interlocked.Increment(ref done), targets.Count));
                            return ValueTask.CompletedTask;
                        }).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return (stoppedServices, closedApps, 0L, true);
                    }

                    if (ct.IsCancellationRequested)
                        return (stoppedServices, closedApps, 0L, true);

                    if (stoppedServices + closedApps > 0)
                    {
                        try { await Task.Delay(120, ct).ConfigureAwait(false); }
                        catch (OperationCanceledException) { return (stoppedServices, closedApps, 0L, true); }
                    }

                    long availAfter = ReadAvailablePhysBytes();
                    long osDelta = availAfter > availBefore ? (availAfter - availBefore) : 0L;
                    long savedBytes = Math.Max(osDelta, Interlocked.Read(ref targetedSavedBytes));

                    lock (stateLock)
                    {
                        if (IsBoostActive && !ct.IsCancellationRequested)
                        {
                            LastSavedRamBytes = savedBytes;
                            LastSkippedProtectedCount = skippedProtected;
                        }
                    }

                    return (stoppedServices, closedApps, savedBytes, ct.IsCancellationRequested);
                }
                finally
                {
                    transitionGate.Release();
                }
            }).ConfigureAwait(false);
        }

        public async Task<(int restoredServices, bool cancelled)> TurnOffTurboBoostAsync(
            IProgress<(int done, int total)>? progress = null)
        {
            CancellationToken ct = ResetTransitionIntent(desiredActive: false);

            return await Task.Run(async () =>
            {
                try
                {
                    await transitionGate.WaitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return (0, true);
                }

                try
                {
                    List<string> toRestore;
                    lock (stateLock)
                    {
                        toRestore = previouslyStoppedServices.ToList();
                    }

                    if (ct.IsCancellationRequested || toRestore.Count == 0)
                    {
                        if (!ct.IsCancellationRequested)
                        {
                            lock (stateLock) { previouslyStoppedServices.Clear(); }
                        }
                        return (0, ct.IsCancellationRequested);
                    }

                    int restored = 0;
                    int restoreFailed = 0;
                    int done = 0;
                    var parallelOptions = new ParallelOptions
                    {
                        MaxDegreeOfParallelism = 6,
                        CancellationToken = ct
                    };

                    try
                    {
                        await Parallel.ForEachAsync(toRestore, parallelOptions, (svcName, token) =>
                        {
                            if (token.IsCancellationRequested) return ValueTask.CompletedTask;

                            if (WinServiceManager.StartService(svcName, token, out _))
                            {
                                lock (stateLock)
                                {
                                    previouslyStoppedServices.Remove(svcName);
                                }
                                Interlocked.Increment(ref restored);
                            }
                            else
                            {
                                Interlocked.Increment(ref restoreFailed);
                            }

                            progress?.Report((Interlocked.Increment(ref done), toRestore.Count));
                            return ValueTask.CompletedTask;
                        }).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return (restored, true);
                    }

                    if (!ct.IsCancellationRequested)
                    {
                        lock (stateLock)
                        {
                            LastRestoreFailedCount = restoreFailed;
                            previouslyStoppedServices.Clear();
                        }
                    }
                    return (restored, ct.IsCancellationRequested);
                }
                finally
                {
                    transitionGate.Release();
                }
            }).ConfigureAwait(false);
        }

        public (int stoppedServices, int closedApps) TurnOnTurboBoost(List<TurboBoostItem> selectedItems)
        {
            var (stopped, closed, _, _) = TurnOnTurboBoostAsync(selectedItems).GetAwaiter().GetResult();
            return (stopped, closed);
        }

        public int TurnOffTurboBoost()
        {
            var (restored, _) = TurnOffTurboBoostAsync().GetAwaiter().GetResult();
            return restored;
        }
    }
}
