using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CleanRamBoosterApp.Models;
using Microsoft.Win32;

namespace CleanRamBoosterApp.Services
{
    public enum StartupOpFailureReason
    {
        None,
        RequiresAdministrator,
        ProtectedByWindows,
        NotSupported,
        NotFound,
        CancelledByUser,
        Error
    }

    public sealed class StartupOperationResult
    {
        public bool Success { get; init; }
        public StartupOpFailureReason FailureReason { get; init; }
        public string Message { get; init; } = string.Empty;
        public int? Win32ErrorCode { get; init; }

        public static StartupOperationResult Ok() => new() { Success = true, FailureReason = StartupOpFailureReason.None };

        public static StartupOperationResult Fail(StartupOpFailureReason reason, string message, int? win32ErrorCode = null) => new()
        {
            Success = false,
            FailureReason = reason,
            Message = message,
            Win32ErrorCode = win32ErrorCode
        };
    }

    public partial class StartupService
    {
        private static readonly HashSet<string> EssentialKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "audio", "sound", "display", "nvidia", "amd", "intel", "realtek", "security",
            "defender", "antivirus", "synaptics", "elan", "touchpad", "bluetooth",
            "system", "windows", "network", "lan", "wifi", "wlan"
        };

        private static readonly HashSet<string> SafeToDisableKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "discord", "spotify", "steam", "epicgames", "origin", "battlenet", "uplay",
            "onedrive", "dropbox", "googledrive", "skype", "teams", "slack", "zoom",
            "update", "updater", "helper", "assistant", "telemetry", "feedback",
            "cortana", "itunes", "gamelauncher", "riotclient"
        };

        public Task<List<StartupItem>> GetAllStartupItemsAsync() => Task.Run(() => GetAllStartupItems());
        public bool SetItemEnabled(StartupItem item, bool enable) => ToggleStartupItemDetailed(item, enable).Success;
        public StartupOperationResult SetItemEnabledDetailed(StartupItem item, bool enable) => ToggleStartupItemDetailed(item, enable);
        public bool DeleteItem(StartupItem item) => DeleteStartupItemDetailed(item).Success;
        public StartupOperationResult DeleteItemDetailed(StartupItem item) => DeleteStartupItemDetailed(item);

        public List<StartupItem> GetAllStartupItems()
        {
            return ScanStartupItems(
                includeServices: true,
                includeScheduledTasks: true,
                cachedShellNames: null,
                previousFamilies: null,
                out _,
                out _);
        }

        public List<StartupItem> GetStartupItemsForMonitor(
            bool includeScheduledTasks,
            Dictionary<string, string>? cachedShellNames,
            HashSet<string>? previousFamilies,
            out Dictionary<string, string> updatedShellNames,
            out HashSet<string> currentFamilies)
        {
            return ScanStartupItems(
                includeServices: false,
                includeScheduledTasks: includeScheduledTasks,
                cachedShellNames: cachedShellNames,
                previousFamilies: previousFamilies,
                out updatedShellNames,
                out currentFamilies);
        }

        private List<StartupItem> ScanStartupItems(
            bool includeServices,
            bool includeScheduledTasks,
            Dictionary<string, string>? cachedShellNames,
            HashSet<string>? previousFamilies,
            out Dictionary<string, string> updatedShellNames,
            out HashSet<string> currentFamilies)
        {
            var list = new List<StartupItem>();

            // 1. Registry HKCU Run
            ReadRegistryRun(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", StartupType.RegistryUser, list);

            // 2. Registry HKLM Run
            ReadRegistryRun(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", StartupType.RegistryMachine, list);

            // 3. Registry HKLM WOW6432Node Run
            ReadRegistryRun(Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", StartupType.RegistryWow64, list);

            // 4. User Startup Folder
            string userStartup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            ReadStartupFolder(userStartup, StartupType.StartupFolderUser, list);

            // 5. Common Startup Folder
            string commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
            ReadStartupFolder(commonStartup, StartupType.StartupFolderCommon, list);

            // 6. Automatic Windows Services (full view only; omitted in lightweight startup monitor)
            if (includeServices)
            {
                ReadAutomaticServices(list);
            }

            // 7. Scheduled Tasks (Boot / Logon)
            if (includeScheduledTasks)
            {
                ReadScheduledTasks(list);
            }

            // 8. Microsoft Store Apps (Windows 11 Background App Permissions)
            ReadMicrosoftStoreApps(list, cachedShellNames, previousFamilies, out updatedShellNames, out currentFamilies);

            // Sanitize all items to ensure no MUI @ resource references remain
            foreach (var item in list)
            {
                if (item.Name.StartsWith("@"))
                {
                    string fallback = !string.IsNullOrEmpty(item.ServiceName) ? item.ServiceName : Path.GetFileNameWithoutExtension(item.FilePath);
                    item.Name = MuiHelper.Resolve(item.Name, fallback);
                    if (item.Name.StartsWith("@"))
                    {
                        item.Name = fallback;
                    }
                }
            }

            return list;
        }

        private static string GetStartupApprovedSubKey(StartupType type) => type switch
        {
            StartupType.RegistryWow64 => @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32",
            StartupType.StartupFolderUser or StartupType.StartupFolderCommon => @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder",
            _ => @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"
        };

        private static bool IsApprovedEnabled(RegistryKey root, string approvedSubKeyPath, string valueName)
        {
            using var approvedKey = root.OpenSubKey(approvedSubKeyPath, false);
            if (approvedKey == null) return true;
            if (approvedKey.GetValue(valueName) is byte[] approvedBytes && approvedBytes.Length > 0)
            {
                // Windows uses even first byte (0x02/0x06) for Enabled and odd (0x03/0x01) for Disabled.
                return (approvedBytes[0] & 0x01) == 0;
            }
            return true;
        }

        private void ReadRegistryRun(RegistryKey root, string subKeyPath, StartupType type, List<StartupItem> list)
        {
            try
            {
                using var key = root.OpenSubKey(subKeyPath, false);
                if (key == null) return;

                string approvedPath = GetStartupApprovedSubKey(type);

                foreach (string valueName in key.GetValueNames())
                {
                    string command = key.GetValue(valueName)?.ToString() ?? string.Empty;
                    bool isEnabled = IsApprovedEnabled(root, approvedPath, valueName);

                    string filePath = ExtractFilePath(command);
                    string friendlyName = GetFriendlyAscName(valueName, filePath);
                    string publisher = GetFilePublisher(filePath);
                    var recommendation = EvaluateRecommendation(friendlyName, command, publisher, filePath);
                    string impact = CalculateBootImpact(friendlyName, command);

                    list.Add(new StartupItem
                    {
                        Name = friendlyName,
                        Command = command,
                        Publisher = publisher,
                        LocationType = type,
                        LocationDisplay = type == StartupType.RegistryUser ? "Registry (Current User)" : "Registry (Local Machine)",
                        IsEnabled = isEnabled,
                        Recommendation = recommendation,
                        BootImpact = impact,
                        OriginalRegistryKey = subKeyPath,
                        RegistryValueName = valueName,
                        FilePath = filePath
                    });
                }
            }
            catch { }
        }

        private void ReadStartupFolder(string folderPath, StartupType type, List<StartupItem> list)
        {
            try
            {
                if (!Directory.Exists(folderPath)) return;

                RegistryKey root = type == StartupType.StartupFolderUser ? Registry.CurrentUser : Registry.LocalMachine;
                string approvedPath = GetStartupApprovedSubKey(type);

                foreach (string file in Directory.GetFiles(folderPath))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext is ".lnk" or ".exe" or ".bat" or ".cmd")
                    {
                        string fileName = Path.GetFileName(file);
                        bool isEnabled = IsApprovedEnabled(root, approvedPath, fileName);

                        string rawName = Path.GetFileNameWithoutExtension(file);
                        string friendlyName = GetFriendlyAscName(rawName, file);
                        string publisher = GetFilePublisher(file);
                        var recommendation = EvaluateRecommendation(friendlyName, file, publisher, file);
                        string impact = CalculateBootImpact(friendlyName, file);

                        list.Add(new StartupItem
                        {
                            Name = friendlyName,
                            Command = file,
                            Publisher = publisher,
                            LocationType = type,
                            LocationDisplay = type == StartupType.StartupFolderUser ? "Startup Folder (User)" : "Startup Folder (All Users)",
                            IsEnabled = isEnabled,
                            Recommendation = recommendation,
                            BootImpact = impact,
                            RegistryValueName = fileName,
                            FilePath = file
                        });
                    }
                }
            }
            catch { }
        }

        private void ReadAutomaticServices(List<StartupItem> list)
        {
            try
            {
                var services = WinServiceManager.GetServices();
                foreach (var svc in services)
                {
                    if (svc.StartType == 2) // Automatic startup
                    {
                        string filePath = ExtractFilePath(svc.ImagePath);
                        string publisher = GetFilePublisher(filePath);
                        string friendlySvc = MuiHelper.Resolve(svc.DisplayName, svc.ServiceName);
                        if (friendlySvc.StartsWith("@")) friendlySvc = svc.ServiceName;

                        var recommendation = EvaluateRecommendation(friendlySvc, svc.ServiceName, publisher, filePath);
                        string impact = CalculateBootImpact(friendlySvc, svc.ImagePath);

                        list.Add(new StartupItem
                        {
                            Name = friendlySvc,
                            Command = svc.ImagePath,
                            Publisher = publisher,
                            LocationType = StartupType.WindowsService,
                            LocationDisplay = $"Windows Service ({svc.ServiceName})",
                            IsEnabled = true,
                            Recommendation = recommendation,
                            BootImpact = impact,
                            ServiceName = svc.ServiceName,
                            FilePath = filePath
                        });
                    }
                }
            }
            catch { }
        }

        private void ReadScheduledTasks(List<StartupItem> list)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = "/query /fo csv /nh",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return;

                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(3000);

                var seenTasks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string tasksRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "Tasks");

                using var reader = new StringReader(output);
                string? line;
                int count = 0;
                while ((line = reader.ReadLine()) != null && count < 200)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    // Format: "TaskName","Next Run Time","Status"
                    string[] parts = line.Split("\",\"");
                    if (parts.Length >= 3)
                    {
                        string taskName = parts[0].Trim('\"', '\\');
                        string status = parts[2].Trim('\"');

                        if (string.IsNullOrWhiteSpace(taskName) || !seenTasks.Add(taskName))
                            continue;

                        // Exclude built-in Windows OS tasks under Microsoft\Windows\
                        if (taskName.StartsWith(@"Microsoft\Windows\", StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Focus on third-party & software updater boot/logon tasks
                        if (taskName.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
                            taskName.Contains("Google", StringComparison.OrdinalIgnoreCase) ||
                            taskName.Contains("Edge", StringComparison.OrdinalIgnoreCase) ||
                            taskName.Contains("Adobe", StringComparison.OrdinalIgnoreCase) ||
                            taskName.Contains("OneDrive", StringComparison.OrdinalIgnoreCase) ||
                            taskName.Contains("Discord", StringComparison.OrdinalIgnoreCase) ||
                            taskName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                            taskName.Contains("Steam", StringComparison.OrdinalIgnoreCase))
                        {
                            bool isEnabled = !status.Equals("Disabled", StringComparison.OrdinalIgnoreCase);
                            string cleanName = Path.GetFileName(taskName);

                            string execFile = string.Empty;
                            string execCommand = taskName;
                            string xmlAuthor = string.Empty;
                            TryReadScheduledTaskDetails(tasksRoot, taskName, out execFile, out execCommand, out xmlAuthor);

                            string publisher = !string.IsNullOrEmpty(execFile) && File.Exists(execFile)
                                ? GetFilePublisher(execFile)
                                : string.Empty;

                            if (string.IsNullOrWhiteSpace(publisher) || publisher == "Unknown Publisher")
                            {
                                publisher = !string.IsNullOrWhiteSpace(xmlAuthor) ? xmlAuthor
                                          : (cleanName.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) || cleanName.Contains("Edge", StringComparison.OrdinalIgnoreCase)) ? "Microsoft Corporation"
                                          : cleanName.Contains("Google", StringComparison.OrdinalIgnoreCase) ? "Google LLC"
                                          : cleanName.Contains("Adobe", StringComparison.OrdinalIgnoreCase) ? "Adobe Inc."
                                          : cleanName.Contains("Brave", StringComparison.OrdinalIgnoreCase) ? "Brave Software, Inc."
                                          : "Third-Party App";
                            }

                            list.Add(new StartupItem
                            {
                                Name = cleanName,
                                Command = string.IsNullOrWhiteSpace(execCommand) ? taskName : execCommand,
                                Publisher = publisher,
                                LocationType = StartupType.ScheduledTask,
                                LocationDisplay = "Scheduled Task",
                                IsEnabled = isEnabled,
                                Recommendation = StartupRecommendation.SafeToDisable,
                                BootImpact = "Low",
                                TaskName = taskName,
                                FilePath = execFile
                            });
                            count++;
                        }
                    }
                }
            }
            catch { }
        }

        private static void TryReadScheduledTaskDetails(string tasksRoot, string taskName, out string filePath, out string command, out string author)
        {
            filePath = string.Empty;
            command = taskName;
            author = string.Empty;
            try
            {
                string xmlPath = Path.Combine(tasksRoot, taskName);
                if (!File.Exists(xmlPath)) return;

                string xml = File.ReadAllText(xmlPath);
                var cmdMatch = Regex.Match(xml, @"<Command>\s*([^<]+?)\s*</Command>", RegexOptions.IgnoreCase);
                if (cmdMatch.Success)
                {
                    string rawCmd = Environment.ExpandEnvironmentVariables(cmdMatch.Groups[1].Value.Trim().Trim('\"'));
                    var argsMatch = Regex.Match(xml, @"<Arguments>\s*([^<]+?)\s*</Arguments>", RegexOptions.IgnoreCase);
                    string rawArgs = argsMatch.Success ? Environment.ExpandEnvironmentVariables(argsMatch.Groups[1].Value.Trim()) : string.Empty;

                    filePath = ExtractFilePath(rawCmd);
                    command = string.IsNullOrEmpty(rawArgs) ? rawCmd : $"\"{rawCmd}\" {rawArgs}";
                }

                var authorMatch = Regex.Match(xml, @"<Author>\s*([^<]+?)\s*</Author>", RegexOptions.IgnoreCase);
                if (authorMatch.Success)
                {
                    string rawAuthor = authorMatch.Groups[1].Value.Trim();
                    if (!rawAuthor.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase) &&
                        !rawAuthor.StartsWith("$(", StringComparison.OrdinalIgnoreCase))
                    {
                        author = rawAuthor;
                    }
                }
            }
            catch { }
        }

        public bool DisableItemByMonitorKey(string monitorKey)
            => ToggleItemByMonitorKey(monitorKey, enable: false);

        public bool ToggleItemByMonitorKey(string monitorKey, bool enable)
        {
            if (string.IsNullOrWhiteSpace(monitorKey)) return false;
            var all = GetAllStartupItems();
            var match = all.FirstOrDefault(x => string.Equals(x.MonitorKey, monitorKey, StringComparison.OrdinalIgnoreCase));
            return match != null && ToggleStartupItemDetailed(match, enable).Success;
        }

        public bool DeleteItemByMonitorKey(string monitorKey)
        {
            if (string.IsNullOrWhiteSpace(monitorKey)) return false;
            var all = GetAllStartupItems();
            var match = all.FirstOrDefault(x => string.Equals(x.MonitorKey, monitorKey, StringComparison.OrdinalIgnoreCase));
            return match != null && DeleteStartupItemDetailed(match).Success;
        }

        private static readonly HashSet<string> ExcludedStorePrefixes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft.NET.Native",
            "Microsoft.VCLibs",
            "Microsoft.UI.Xaml",
            "Microsoft.WindowsAppRuntime",
            "Microsoft.WinAppRuntime",
            "MicrosoftCorporationII.WinAppRuntime",
            "Microsoft.Winget",
            "Microsoft.Ink.Handwriting",
            "Microsoft.LanguageExperiencePack",
            "Microsoft.VP9VideoExtensions",
            "Microsoft.AV1VideoExtension",
            "Microsoft.HEVCVideoExtension",
            "Microsoft.HEIFImageExtension",
            "Microsoft.RawImageExtension",
            "Microsoft.MPEG2VideoExtension",
            "Microsoft.WebpImageExtension",
            "Microsoft.AVCEncoderVideoExtension",
            "Microsoft.AIFabric",
            "Microsoft.Windows.AugLoop",
            "Microsoft.ECApp",
            "Microsoft.AAD.BrokerPlugin",
            "Microsoft.LockApp",
            "Microsoft.Windows.ContentDeliveryManager",
            "Microsoft.Windows.ShellExperienceHost",
            "Microsoft.Windows.StartMenuExperienceHost",
            "Microsoft.Windows.NarratorQuickStart",
            "MicrosoftWindows.Client.OOBE",
            "MicrosoftWindows.Client.Core",
            "MicrosoftWindows.Client.CBS",
            "windows.immersivecontrolpanel",
            "Microsoft.WidgetsPlatformRuntime",
            "Microsoft.Windows.AssignedAccessLockApp",
            "Microsoft.AsyncTextService",
            "Microsoft.BioEnrollment",
            "Microsoft.CredDialogHost",
            "Microsoft.Win32WebViewHost"
        };

        private static readonly Dictionary<string, string> KnownStoreFriendlyNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["MicrosoftWindows.CrossDevice"] = "Cross Device Experience Host",
            ["MicrosoftWindows.Client.WebExperience"] = "Windows Web Experience (Widgets)",
            ["Microsoft.BingSearch"] = "Microsoft Bing Search",
            ["Microsoft.BingWeather"] = "MSN Weather",
            ["Microsoft.BingNews"] = "Microsoft News",
            ["Microsoft.StartExperiencesApp"] = "Start Experiences App",
            ["Microsoft.DesktopAppInstaller"] = "App Installer (WinGet)",
            ["Microsoft.StorePurchaseApp"] = "Store Purchase App",
            ["Microsoft.XboxIdentityProvider"] = "Xbox Identity Provider",
            ["Microsoft.Xbox.TCUI"] = "Xbox Live In-Game Experience",
            ["Microsoft.XboxSpeechToTextOverlay"] = "Xbox Game Speech Overlay",
            ["Microsoft.ApplicationCompatibilityEnhancements"] = "App Compatibility Enhancements",
            ["Microsoft.WebMediaExtensions"] = "Web Media Extensions",
            ["Microsoft.OutlookForWindows"] = "Outlook (New)",
            ["Microsoft.MicrosoftOfficeHub"] = "Copilot / Microsoft 365",
            ["Microsoft.PowerAutomateDesktop"] = "Power Automate"
        };

        private static readonly HashSet<string> StoreSafeToDisableKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "calculator", "alarms", "paint", "photos", "camera", "soundrecorder", "stickynotes",
            "zunemusic", "clipchamp", "todos", "bing", "copilot", "officehub", "devhome",
            "gamingapp", "xbox", "gethelp", "quickassist", "yourphone", "crossdevice",
            "webexperience", "startexperiences", "webmediaextensions", "codex", "chatgpt",
            "commandpalette", "storepurchase", "applicationcompatibility", "teams"
        };

        private void ReadMicrosoftStoreApps(
            List<StartupItem> list,
            Dictionary<string, string>? cachedShellNames,
            HashSet<string>? previousFamilies,
            out Dictionary<string, string> updatedShellNames,
            out HashSet<string> currentFamilies)
        {
            updatedShellNames = cachedShellNames ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            currentFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                const string repoPath = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
                const string bgBasePath = @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications";

                using var repoKey = Registry.CurrentUser.OpenSubKey(repoPath, false);
                if (repoKey == null) return;

                using var bgBaseKey = Registry.CurrentUser.OpenSubKey(bgBasePath, false);

                // Deduplicate packages by PackageFamilyName, preferring entries with an active PackageRootFolder on disk.
                var bestByFamily = new Dictionary<string, (string FullName, string RawDisplay, string RootFolder)>(StringComparer.OrdinalIgnoreCase);

                foreach (string fullPkgName in repoKey.GetSubKeyNames())
                {
                    int firstUnderscore = fullPkgName.IndexOf('_');
                    int lastUnderscore = fullPkgName.LastIndexOf('_');
                    if (firstUnderscore <= 0 || lastUnderscore <= firstUnderscore || lastUnderscore >= fullPkgName.Length - 1)
                        continue;

                    string shortName = fullPkgName.Substring(0, firstUnderscore);
                    string pubId = fullPkgName.Substring(lastUnderscore + 1);
                    string family = $"{shortName}_{pubId}";

                    if (ExcludedStorePrefixes.Any(prefix => shortName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    using var pkgKey = repoKey.OpenSubKey(fullPkgName, false);
                    if (pkgKey == null) continue;

                    string rootFolder = pkgKey.GetValue("PackageRootFolder")?.ToString() ?? string.Empty;
                    string rawDisplay = pkgKey.GetValue("DisplayName")?.ToString() ?? string.Empty;

                    if (bestByFamily.TryGetValue(family, out var existing))
                    {
                        bool existingExists = !string.IsNullOrEmpty(existing.RootFolder) && Directory.Exists(existing.RootFolder);
                        bool newExists = !string.IsNullOrEmpty(rootFolder) && Directory.Exists(rootFolder);
                        if (existingExists && !newExists) continue;
                    }

                    bestByFamily[family] = (fullPkgName, rawDisplay, rootFolder);
                    currentFamilies.Add(family);
                }

                // Refresh COM shell:AppsFolder display names only when null or when the package set changed
                if (cachedShellNames == null || previousFamilies == null || !currentFamilies.SetEquals(previousFamilies))
                {
                    updatedShellNames = GetShellAppsFolderDisplayNames();
                }

                var shellNames = updatedShellNames;
                var added = new List<StartupItem>();

                foreach (var kvp in bestByFamily)
                {
                    string family = kvp.Key;
                    var info = kvp.Value;
                    string shortName = family.Substring(0, family.IndexOf('_'));

                    if (string.IsNullOrWhiteSpace(info.RootFolder) || !Directory.Exists(info.RootFolder))
                        continue;

                    if (info.RootFolder.Contains(@"\Windows\SystemApps\", StringComparison.OrdinalIgnoreCase) ||
                        info.RootFolder.Contains(@"\Windows\ImmersiveControlPanel", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string manifestPath = Path.Combine(info.RootFolder, "AppxManifest.xml");
                    string publisher = string.Empty;
                    string manifestExePath = string.Empty;
                    string manifestLogoPath = string.Empty;
                    bool isFrameworkOrResource = false;

                    if (File.Exists(manifestPath))
                    {
                        try
                        {
                            string manifestXml = File.ReadAllText(manifestPath);
                            if (Regex.IsMatch(manifestXml, @"<(?:Framework|ResourcePackage)>\s*true\s*</(?:Framework|ResourcePackage)>", RegexOptions.IgnoreCase))
                                isFrameworkOrResource = true;

                            var pubMatch = Regex.Match(manifestXml, @"<PublisherDisplayName>([^<]+)</PublisherDisplayName>", RegexOptions.IgnoreCase);
                            if (pubMatch.Success)
                                publisher = pubMatch.Groups[1].Value.Trim();

                            var exeMatch = Regex.Match(manifestXml, @"<Application\b[^>]*\bExecutable=""([^""]+)""", RegexOptions.IgnoreCase);
                            if (exeMatch.Success)
                            {
                                string exeCandidate = Path.Combine(info.RootFolder, exeMatch.Groups[1].Value.Trim().TrimStart('\\', '/'));
                                if (File.Exists(exeCandidate))
                                    manifestExePath = exeCandidate;
                            }

                            var sqLogoMatch = Regex.Match(manifestXml, @"\bSquare44x44Logo=""([^""]+)""", RegexOptions.IgnoreCase);
                            string rawLogoRel = sqLogoMatch.Success ? sqLogoMatch.Groups[1].Value.Trim() : string.Empty;
                            if (string.IsNullOrEmpty(rawLogoRel))
                            {
                                var propLogoMatch = Regex.Match(manifestXml, @"<Logo>([^<]+)</Logo>", RegexOptions.IgnoreCase);
                                if (propLogoMatch.Success)
                                    rawLogoRel = propLogoMatch.Groups[1].Value.Trim();
                            }

                            if (!string.IsNullOrEmpty(rawLogoRel))
                                manifestLogoPath = ResolveStoreLogoPath(info.RootFolder, rawLogoRel);
                        }
                        catch { }
                    }

                    if (isFrameworkOrResource) continue;

                    bool inShellApps = shellNames.TryGetValue(family, out string? shellDisplay) && !string.IsNullOrWhiteSpace(shellDisplay);
                    using var bgKey = bgBaseKey?.OpenSubKey(family, false);
                    bool knownBg = KnownStoreFriendlyNames.ContainsKey(shortName);

                    // Include installed interactive Store apps OR known Store background hosts present in BackgroundAccessApplications.
                    if (!inShellApps && (bgKey == null || (!knownBg && !info.RootFolder.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase))))
                        continue;

                    if (!inShellApps && !knownBg)
                    {
                        // Skip non-user Store extensions unless they have an Application entry or explicit background key with user policy
                        if (shortName.Contains("ContextMenu", StringComparison.OrdinalIgnoreCase) ||
                            shortName.Contains("ShellExtension", StringComparison.OrdinalIgnoreCase) ||
                            shortName.Contains("FilterPackage", StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    string displayName = shellDisplay ?? ResolveStoreDisplayName(info.FullName, shortName, info.RawDisplay);
                    if (string.IsNullOrWhiteSpace(publisher) || publisher.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) || publisher.StartsWith("@"))
                    {
                        publisher = family.EndsWith("_8wekyb3d8bbwe", StringComparison.OrdinalIgnoreCase) ? "Microsoft Corporation"
                                  : family.EndsWith("_cw5n1h2txyewy", StringComparison.OrdinalIgnoreCase) ? "Microsoft Windows"
                                  : "Microsoft Store Publisher";
                    }

                    bool isNever = false;
                    bool isAlways = false;
                    if (bgKey != null)
                    {
                        int disabled = Convert.ToInt32(bgKey.GetValue("Disabled") ?? 0);
                        int disabledByUser = Convert.ToInt32(bgKey.GetValue("DisabledByUser") ?? 0);
                        int sleepDisabled = Convert.ToInt32(bgKey.GetValue("SleepDisabled") ?? 0);
                        int ignoreBatt = Convert.ToInt32(bgKey.GetValue("IgnoreBatterySaver") ?? 0);

                        isNever = disabled == 1 || disabledByUser == 1 || sleepDisabled == 1;
                        isAlways = !isNever && ignoreBatt == 1;
                    }

                    string bgMode = isNever ? "Never" : (isAlways ? "Always" : "Power Optimized");
                    bool isEnabled = !isNever;

                    var rec = EvaluateStoreRecommendation(displayName, family);
                    string impact = CalculateStoreBootImpact(displayName, family);

                    added.Add(new StartupItem
                    {
                        Name = displayName,
                        Publisher = publisher,
                        LocationType = StartupType.MicrosoftStoreApp,
                        LocationDisplay = "Microsoft Store App",
                        IsEnabled = isEnabled,
                        Recommendation = rec,
                        BootImpact = impact,
                        PackageFamilyName = family,
                        BackgroundPermissionMode = bgMode,
                        Command = BuildStoreAppCommandSummary(bgMode, family),
                        OriginalRegistryKey = $@"{bgBasePath}\{family}",
                        InstallFolder = info.RootFolder,
                        IconAssetPath = manifestLogoPath,
                        FilePath = !string.IsNullOrEmpty(manifestExePath) ? manifestExePath : info.RootFolder
                    });
                }

                foreach (var storeItem in added.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    list.Add(storeItem);
                }
            }
            catch { }
        }

        private static string ResolveStoreLogoPath(string rootFolder, string relLogoPath)
        {
            try
            {
                string cleanRel = relLogoPath.TrimStart('\\', '/').Replace('/', '\\');
                string exact = Path.Combine(rootFolder, cleanRel);
                if (File.Exists(exact)) return exact;

                string? dir = Path.GetDirectoryName(exact);
                string stem = Path.GetFileNameWithoutExtension(exact);
                string ext = Path.GetExtension(exact);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir) || string.IsNullOrEmpty(stem))
                    return string.Empty;

                if (string.IsNullOrEmpty(ext)) ext = ".png";
                var candidates = Directory.GetFiles(dir, $"{stem}.*{ext}");
                if (candidates.Length == 0) return string.Empty;

                // Prefer unplated targetsize-48 / targetsize-32 / scale-200 variants for crisp display
                return candidates
                    .OrderByDescending(f => f.Contains("unplated", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                    .ThenByDescending(f => f.Contains("targetsize-48", StringComparison.OrdinalIgnoreCase) || f.Contains("scale-200", StringComparison.OrdinalIgnoreCase) ? 2
                                         : f.Contains("targetsize-32", StringComparison.OrdinalIgnoreCase) || f.Contains("scale-150", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                    .FirstOrDefault() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static Dictionary<string, string> GetShellAppsFolderDisplayNames()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var thread = new Thread(() =>
                {
                    try
                    {
                        Type? shellType = Type.GetTypeFromProgID("Shell.Application");
                        if (shellType == null) return;
                        dynamic? shell = Activator.CreateInstance(shellType);
                        if (shell == null) return;
                        dynamic? folder = shell.NameSpace("shell:AppsFolder");
                        if (folder == null) return;

                        foreach (dynamic item in folder.Items())
                        {
                            try
                            {
                                string path = item.Path as string ?? string.Empty;
                                int bangIdx = path.IndexOf('!');
                                if (bangIdx > 0)
                                {
                                    string family = path.Substring(0, bangIdx);
                                    string name = item.Name as string ?? string.Empty;
                                    if (!string.IsNullOrWhiteSpace(family) && !string.IsNullOrWhiteSpace(name) && !map.ContainsKey(family))
                                    {
                                        map[family] = name.Trim();
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Start();
                thread.Join(2500);
            }
            catch { }
            return map;
        }

        private static string ResolveStoreDisplayName(string fullPkgName, string shortName, string rawDisplay)
        {
            if (KnownStoreFriendlyNames.TryGetValue(shortName, out string? known))
                return known;

            if (!string.IsNullOrWhiteSpace(rawDisplay))
            {
                if (rawDisplay.StartsWith("@{", StringComparison.Ordinal))
                {
                    string resolved = MuiHelper.Resolve(rawDisplay, string.Empty);
                    if (!string.IsNullOrWhiteSpace(resolved) && !resolved.StartsWith("@") && !resolved.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                        return resolved;
                }
                else if (!rawDisplay.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                {
                    return rawDisplay.Trim();
                }
                else
                {
                    string resTail = rawDisplay.Substring("ms-resource:".Length).TrimStart('/');
                    string uri1 = $"@{{{fullPkgName}?ms-resource://{shortName}/Resources/{resTail}}}";
                    string r1 = MuiHelper.Resolve(uri1, string.Empty);
                    if (!string.IsNullOrWhiteSpace(r1) && !r1.StartsWith("@")) return r1;

                    string uri2 = $"@{{{fullPkgName}?ms-resource://{shortName}/{resTail}}}";
                    string r2 = MuiHelper.Resolve(uri2, string.Empty);
                    if (!string.IsNullOrWhiteSpace(r2) && !r2.StartsWith("@")) return r2;
                }
            }

            string clean = shortName;
            if (clean.StartsWith("MicrosoftWindows.", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring("MicrosoftWindows.".Length);
            else if (clean.StartsWith("MicrosoftCorporationII.", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring("MicrosoftCorporationII.".Length);
            else if (clean.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring("Microsoft.".Length);

            int dot = clean.LastIndexOf('.');
            if (dot > 0 && dot < clean.Length - 1 && !char.IsDigit(clean[dot + 1]))
                clean = clean.Substring(dot + 1);

            return Regex.Replace(clean, "(?<!^)([A-Z])", " $1").Trim();
        }

        private static StartupRecommendation EvaluateStoreRecommendation(string name, string family)
        {
            string combined = $"{name} {family}".ToLowerInvariant();
            if (combined.Contains("sechealthui") || combined.Contains("windows security") || combined.Contains("audiocontrol") || combined.Contains("audio control"))
                return StartupRecommendation.Necessary;

            foreach (var kw in StoreSafeToDisableKeywords)
            {
                if (combined.Contains(kw))
                    return StartupRecommendation.SafeToDisable;
            }

            return StartupRecommendation.Optional;
        }

        private static string CalculateStoreBootImpact(string name, string family)
        {
            string combined = $"{name} {family}".ToLowerInvariant();
            if (combined.Contains("teams") || combined.Contains("copilot") || combined.Contains("officehub") ||
                combined.Contains("yourphone") || combined.Contains("crossdevice") || combined.Contains("webexperience") ||
                combined.Contains("gamingapp") || combined.Contains("xbox") || combined.Contains("windowsstore") ||
                combined.Contains("codex") || combined.Contains("todos") || combined.Contains("photos"))
            {
                return "Medium Impact";
            }
            return "Low Impact";
        }

        public static string BuildStoreAppCommandSummary(string bgMode, string family)
        {
            string modeLabel = bgMode switch
            {
                "Never" => LocalizationService.IsArabic ? "في الخلفية: أبداً" : "Bg: Never",
                "Always" => LocalizationService.IsArabic ? "في الخلفية: دائماً" : "Bg: Always",
                _ => LocalizationService.IsArabic ? "في الخلفية: محسن للطاقة" : "Bg: Power Opt."
            };
            return $"{modeLabel} • \u200E{family}\u200E";
        }

        public int DisableAllMicrosoftStoreApps(IEnumerable<StartupItem> items)
        {
            int count = 0;
            foreach (var item in items)
            {
                if (item.LocationType == StartupType.MicrosoftStoreApp &&
                    item.IsEnabled &&
                    item.Recommendation != StartupRecommendation.Necessary)
                {
                    if (ToggleStartupItem(item, false))
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        private static string GetFriendlyAscName(string valueName, string filePath)
        {
            if (valueName.StartsWith("@"))
            {
                string res = MuiHelper.Resolve(valueName, Path.GetFileNameWithoutExtension(filePath));
                if (!res.StartsWith("@") && !string.IsNullOrWhiteSpace(res)) return res;
            }

            try
            {
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    var vi = FileVersionInfo.GetVersionInfo(filePath);
                    if (!string.IsNullOrWhiteSpace(vi.FileDescription) && !vi.FileDescription.StartsWith("@"))
                        return vi.FileDescription.Trim();
                    if (!string.IsNullOrWhiteSpace(vi.ProductName) && !vi.ProductName.StartsWith("@"))
                        return vi.ProductName.Trim();
                }
            }
            catch { }

            if (!string.IsNullOrWhiteSpace(valueName) && !valueName.StartsWith("@"))
                return valueName.Trim();

            if (!string.IsNullOrEmpty(filePath))
                return Path.GetFileNameWithoutExtension(filePath);

            return "Unknown Startup App";
        }

        private static string CalculateBootImpact(string name, string command)
        {
            string combined = $"{name} {command}".ToLowerInvariant();
            if (combined.Contains("discord") || combined.Contains("spotify") || combined.Contains("steam") ||
                combined.Contains("epic") || combined.Contains("onedrive") || combined.Contains("teams") ||
                combined.Contains("chrome") || combined.Contains("edge"))
            {
                return "High Impact";
            }
            if (combined.Contains("nvidia") || combined.Contains("realtek") || combined.Contains("audio") ||
                combined.Contains("synaptics") || combined.Contains("razer") || combined.Contains("icue"))
            {
                return "Medium Impact";
            }
            return "Low Impact";
        }

        private StartupRecommendation EvaluateRecommendation(string name, string command, string publisher, string path)
        {
            if (!string.IsNullOrEmpty(path) && !File.Exists(path) && !path.ToLowerInvariant().Contains("system32") && !path.ToLowerInvariant().Contains("syswow64"))
            {
                return StartupRecommendation.Orphaned;
            }

            string combined = $"{name} {command} {publisher}".ToLowerInvariant();

            foreach (var kw in EssentialKeywords)
            {
                if (combined.Contains(kw))
                    return StartupRecommendation.Necessary;
            }

            foreach (var kw in SafeToDisableKeywords)
            {
                if (combined.Contains(kw))
                    return StartupRecommendation.SafeToDisable;
            }

            return StartupRecommendation.Optional;
        }

    }
}
