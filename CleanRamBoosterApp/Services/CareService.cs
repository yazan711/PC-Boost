using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CleanRamBoosterApp.Models;
using Microsoft.Win32;

namespace CleanRamBoosterApp.Services
{
    public class CareService
    {
        [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

        [StructLayout(LayoutKind.Sequential)]
        private struct SHQUERYRBINFO
        {
            public uint cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHQueryRecycleBinW(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        private const uint SHERB_NOCONFIRMATION = 0x00000001;
        private const uint SHERB_NOPROGRESSUI = 0x00000002;
        private const uint SHERB_NOSOUND = 0x00000004;

        public Task<List<CareAppGroup>> ScanAllHierarchicalAsync(
            IProgress<CareScanProgress>? progress = null,
            CancellationToken token = default)
        {
            return Task.Run(() => ScanAllHierarchicalCore(progress, token), token);
        }

        public List<CareAppGroup> ScanAllHierarchical()
            => ScanAllHierarchicalCore(null, CancellationToken.None);

        private List<CareAppGroup> ScanAllHierarchicalCore(
            IProgress<CareScanProgress>? progress,
            CancellationToken token)
        {
            var groups = new List<CareAppGroup>();
            bool isAr = LocalizationService.IsArabic;
            const int totalSteps = 12;
            int step = 0;

            void ReportStep(string areaEn, string areaAr)
            {
                step++;
                progress?.Report(new CareScanProgress
                {
                    StepIndex = step,
                    TotalSteps = totalSteps,
                    CurrentArea = isAr ? areaAr : areaEn,
                    FilesFoundSoFar = groups.Sum(g => g.AllScannedFiles),
                    BytesFoundSoFar = groups.Sum(g => g.AllScannedBytes)
                });
            }

            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string progData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            // =========================================================
            // 1. PRIVACY SWEEP (Browsers across all profiles, Explorer)
            // =========================================================

            // Step 1: Google Chrome (all profiles)
            ReportStep("Google Chrome caches & history", "ذاكرة وسجل Google Chrome");
            if (!token.IsCancellationRequested)
            {
                ScanChromiumBrowser(
                    groups,
                    Path.Combine(localApp, @"Google\Chrome\User Data"),
                    isAr ? "متصفح \u200EGoogle Chrome\u200E" : "Google Chrome",
                    isAr ? "ذاكرة التخزين المؤقت للويب وملفات الارتباط والوسائط المؤقتة عبر جميع الملفات الشخصية." : "Internet cache, GPU shaders, cookies, and temporary browser media across all profiles.",
                    "🌐",
                    isAr);
            }

            // Step 2: Microsoft Edge (all profiles)
            ReportStep("Microsoft Edge caches & history", "ذاكرة وسجل Microsoft Edge");
            if (!token.IsCancellationRequested)
            {
                ScanChromiumBrowser(
                    groups,
                    Path.Combine(localApp, @"Microsoft\Edge\User Data"),
                    isAr ? "متصفح \u200EMicrosoft Edge\u200E" : "Microsoft Edge",
                    isAr ? "ذاكرة الويب المؤقتة ومخازن الوسائط وملفات الارتباط في \u200EEdge\u200E عبر جميع الملفات الشخصية." : "Edge web cache, streaming media buffers, GPU shaders, and cookies across all profiles.",
                    "🌐",
                    isAr);
            }

            // Step 3: Brave & Mozilla Firefox
            ReportStep("Brave & Firefox browser caches", "ذاكرة متصفحات Brave و Firefox");
            if (!token.IsCancellationRequested)
            {
                ScanChromiumBrowser(
                    groups,
                    Path.Combine(localApp, @"BraveSoftware\Brave-Browser\User Data"),
                    isAr ? "متصفح \u200EBrave Browser\u200E" : "Brave Browser",
                    isAr ? "ذاكرة التخزين المؤقت للويب وتظليل المعالج الرسومي في \u200EBrave\u200E." : "Brave web cache, code cache, and GPU shader buffers across all profiles.",
                    "🌐",
                    isAr);

                string firefoxDir = Path.Combine(localApp, @"Mozilla\Firefox\Profiles");
                if (Directory.Exists(firefoxDir))
                {
                    var ffGroup = new CareAppGroup
                    {
                        Category = CareCategory.PrivacySweep,
                        AppName = isAr ? "متصفح \u200EMozilla Firefox\u200E" : "Mozilla Firefox",
                        Description = isAr ? "ذاكرة التخزين المؤقت دون اتصال ومخازن الشبكة في \u200EFirefox\u200E." : "Firefox offline web cache and network buffer entries.",
                        IconEmoji = "🦊"
                    };
                    AddDirSubItem(ffGroup, isAr ? "ذاكرة الويب والتصفح دون اتصال" : "Web Cache & Offline Cache", firefoxDir);
                    FinalizeGroupSelection(ffGroup);
                    if (ffGroup.SubItems.Count > 0) groups.Add(ffGroup);
                }
            }

            // Step 4: Windows Explorer & File History
            ReportStep("Windows Explorer & thumbnail cache", "مستكشف ويندوز وذاكرة المصغرات");
            if (!token.IsCancellationRequested)
            {
                var explorerGroup = new CareAppGroup
                {
                    Category = CareCategory.PrivacySweep,
                    AppName = isAr ? "مستكشف ويندوز والملفات الحديثة" : "Windows Explorer & Recent Items",
                    Description = isAr ? "سجل الأوامر السريعة وقوائم الوصول السريع وذاكرة الصور المصغرة." : "Run MRU history, Explorer jump lists, recent files, and thumbnail cache.",
                    IconEmoji = "📁"
                };

                AddDirSubItem(explorerGroup, isAr ? "المستندات الحديثة وقوائم الوصول" : "Recent Documents & Jump Lists", Environment.GetFolderPath(Environment.SpecialFolder.Recent));

                string thumbCache = Path.Combine(localApp, @"Microsoft\Windows\Explorer");
                AddPatternSubItem(explorerGroup, isAr ? "ذاكرة الصور المصغرة (\u200Ethumbcache_*.db\u200E)" : "Explorer Thumbnail Cache (thumbcache_*.db)", thumbCache, "thumbcache_*.db");
                FinalizeGroupSelection(explorerGroup);
                if (explorerGroup.SubItems.Count > 0) groups.Add(explorerGroup);
            }

            // =========================================================
            // 2. JUNK FILE CLEAN (Temp, System, Prefetch, Updates, GPU, Dumps, Recycle Bin)
            // =========================================================

            // Step 5: User Temporary Files
            ReportStep("User temporary files (%TEMP%)", "ملفات المستخدم المؤقتة (%TEMP%)");
            if (!token.IsCancellationRequested)
            {
                var userTempGroup = new CareAppGroup
                {
                    Category = CareCategory.JunkFiles,
                    AppName = isAr ? "ملفات المستخدم المؤقتة (\u200E%TEMP%\u200E)" : "User Temporary Files (%TEMP%)",
                    Description = isAr ? "بقايا ملفات التثبيت وسجلات الأعطال والملفات المؤقتة للبرامج." : "Leftover installer extractions, crash logs, and runtime temp data.",
                    IconEmoji = "🧹"
                };
                AddDirSubItem(userTempGroup, isAr ? "مجلد الملفات المؤقتة للمستخدم" : "User Temp Directory", Path.GetTempPath());
                FinalizeGroupSelection(userTempGroup);
                groups.Add(userTempGroup);
            }

            // Step 6: Windows System Temp & Logs
            ReportStep("Windows System Temp & CBS/DISM logs", "ملفات النظام المؤقتة وسجلات CBS");
            if (!token.IsCancellationRequested)
            {
                var winTempGroup = new CareAppGroup
                {
                    Category = CareCategory.JunkFiles,
                    AppName = isAr ? "سجلات وملفات النظام المؤقتة" : "Windows System Cache & Logs",
                    Description = isAr ? "مجلد ويندوز المؤقت وسجلات صيانة \u200ECBS/DISM\u200E وتقارير التشخيص." : "Windows system temporary folder, CBS/DISM maintenance logs, and diagnostic files.",
                    IconEmoji = "⚙️"
                };
                AddDirSubItem(winTempGroup, isAr ? "مجلد النظام المؤقت (\u200EC:\\Windows\\Temp\u200E)" : "System Temp (C:\\Windows\\Temp)", Path.Combine(winDir, "Temp"), requiresAdmin: true);
                AddDirSubItem(winTempGroup, isAr ? "سجلات خدمة مكونات ويندوز (\u200ECBS\u200E)" : "Windows CBS & Component Logs", Path.Combine(winDir, @"Logs\CBS"), requiresAdmin: true);
                AddDirSubItem(winTempGroup, isAr ? "سجلات صيانة النظام (\u200EDISM\u200E)" : "Windows DISM Logs", Path.Combine(winDir, @"Logs\DISM"), requiresAdmin: true);
                FinalizeGroupSelection(winTempGroup);
                groups.Add(winTempGroup);
            }

            // Step 7: Windows Prefetch
            ReportStep("Windows Prefetch cache", "ذاكرة التحميل المسبق (Prefetch)");
            if (!token.IsCancellationRequested)
            {
                string prefetchDir = Path.Combine(winDir, "Prefetch");
                var prefetchGroup = new CareAppGroup
                {
                    Category = CareCategory.JunkFiles,
                    AppName = isAr ? "ذاكرة التحميل المسبق للنظام (\u200EPrefetch\u200E)" : "Windows Prefetch Cache",
                    Description = isAr ? "آثار الإطلاق المسبق القديمة للتطبيقات (ملفات \u200E.pf\u200E)." : "Stale application launch prefetch traces (.pf files).",
                    IconEmoji = "⚡"
                };
                AddDirSubItem(prefetchGroup, isAr ? "ملفات التحميل المسبق (\u200E.pf\u200E)" : "Prefetch Data Files (.pf)", prefetchDir, requiresAdmin: true);
                FinalizeGroupSelection(prefetchGroup);
                if (prefetchGroup.SubItems.Count > 0) groups.Add(prefetchGroup);
            }

            // Step 8: Windows Update & Delivery Optimization Cache
            ReportStep("Windows Update & Delivery Optimization cache", "ذاكرة تحديثات ويندوز وتحسين التسليم");
            if (!token.IsCancellationRequested)
            {
                var softDistGroup = new CareAppGroup
                {
                    Category = CareCategory.JunkFiles,
                    AppName = isAr ? "ذاكرة تنزيلات تحديثات ويندوز" : "Windows Update & Delivery Cache",
                    Description = isAr ? "حزم التثبيت المنزّلة للتحديثات المكتملة وذاكرة تحسين التسليم." : "Downloaded Windows Update packages and Delivery Optimization peer cache.",
                    IconEmoji = "📦"
                };
                AddDirSubItem(softDistGroup, isAr ? "مخزن تنزيلات \u200ESoftwareDistribution\u200E" : "SoftwareDistribution Download Cache", Path.Combine(winDir, @"SoftwareDistribution\Download"), requiresAdmin: true);
                AddDirSubItem(softDistGroup, isAr ? "ذاكرة تحسين التسليم (\u200EDelivery Optimization\u200E)" : "Delivery Optimization Cache", Path.Combine(winDir, @"SoftwareDistribution\DeliveryOptimization\Cache"), requiresAdmin: true);
                AddDirSubItem(softDistGroup, isAr ? "مخزن تحسين التسليم للخدمة" : "NetworkService Delivery Cache", Path.Combine(winDir, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"), requiresAdmin: true);
                FinalizeGroupSelection(softDistGroup);
                if (softDistGroup.SubItems.Count > 0) groups.Add(softDistGroup);
            }

            // Step 9: DirectX Shader Cache
            ReportStep("DirectX GPU shader cache", "ذاكرة تظليل DirectX");
            if (!token.IsCancellationRequested)
            {
                string d3dCache = Path.Combine(localApp, "D3DSCache");
                if (Directory.Exists(d3dCache))
                {
                    var shaderGroup = new CareAppGroup
                    {
                        Category = CareCategory.JunkFiles,
                        AppName = isAr ? "ذاكرة تظليل الرسوميات (\u200EDirectX\u200E)" : "DirectX Shader Cache",
                        Description = isAr ? "ملفات تظليل الألعاب والرسوميات المؤقتة التي يعيد النظام بناءها عند الحاجة." : "Compiled DirectX shader cache files safely regenerated by apps and games as needed.",
                        IconEmoji = "⚡"
                    };
                    AddDirSubItem(shaderGroup, isAr ? "ذاكرة تظليل \u200ED3DSCache\u200E" : "DirectX Shader Cache (D3DSCache)", d3dCache);
                    FinalizeGroupSelection(shaderGroup);
                    if (shaderGroup.SubItems.Count > 0) groups.Add(shaderGroup);
                }
            }

            // Step 10: Crash Dumps & Error Reports
            ReportStep("Crash dumps & Windows Error Reporting", "ملفات تفريغ الأعطال وتقارير الأخطاء");
            if (!token.IsCancellationRequested)
            {
                var crashGroup = new CareAppGroup
                {
                    Category = CareCategory.JunkFiles,
                    AppName = isAr ? "تفريغ الأعطال وتقارير الأخطاء" : "Crash Dumps & Error Reports",
                    Description = isAr ? "ملفات تفريغ ذاكرة الأعطال (\u200E.dmp\u200E) وسجلات تقارير أخطاء ويندوز." : "Application memory dumps (.dmp) and Windows Error Reporting archives.",
                    IconEmoji = "💥"
                };
                AddDirSubItem(crashGroup, isAr ? "ملفات تفريغ أعطال التطبيقات" : "User Application Crash Dumps", Path.Combine(localApp, "CrashDumps"));
                AddDirSubItem(crashGroup, isAr ? "تقارير أخطاء المستخدم (\u200EWER\u200E)" : "User Windows Error Reporting (WER)", Path.Combine(localApp, @"Microsoft\Windows\WER"));
                AddDirSubItem(crashGroup, isAr ? "أرشيف تقارير أخطاء النظام (\u200EProgramData\u200E)" : "System Error Report Archive (ProgramData)", Path.Combine(progData, @"Microsoft\Windows\WER\ReportArchive"), requiresAdmin: true);
                AddDirSubItem(crashGroup, isAr ? "قائمة انتظار أخطاء النظام (\u200EProgramData\u200E)" : "System Error Report Queue (ProgramData)", Path.Combine(progData, @"Microsoft\Windows\WER\ReportQueue"), requiresAdmin: true);
                FinalizeGroupSelection(crashGroup);
                if (crashGroup.SubItems.Count > 0) groups.Add(crashGroup);
            }

            // Step 11: Real Windows Recycle Bin Query (SHQueryRecycleBinW)
            ReportStep("Windows Recycle Bin across all drives", "سلة محذوفات ويندوز على جميع الأقراص");
            if (!token.IsCancellationRequested)
            {
                var (rbBytes, rbItems) = QueryActualRecycleBin();
                var recycleGroup = new CareAppGroup
                {
                    Category = CareCategory.JunkFiles,
                    AppName = isAr ? "سلة محذوفات ويندوز" : "Windows Recycle Bin",
                    Description = isAr ? "الملفات المحذوفة الفعلية على كافة الأقراص بانتظار المسح النهائي." : "Actual deleted files across all local drives awaiting permanent purge.",
                    IconEmoji = "🗑️"
                };
                recycleGroup.SubItems.Add(new CareSubItem
                {
                    Name = isAr ? "محتويات سلة المحذوفات في جميع الأقراص" : "All Drives Recycle Bin Contents",
                    TargetPath = "RecycleBin",
                    SizeBytes = rbBytes,
                    FileCount = rbItems,
                    IsSelected = rbBytes > 0 || rbItems > 0,
                    CleanAction = () =>
                    {
                        try { SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND); } catch { }
                    },
                    CleanWithStats = () =>
                    {
                        var stats = new CareCleanStats();
                        var (beforeBytes, beforeItems) = QueryActualRecycleBin();
                        if (beforeBytes == 0 && beforeItems == 0) return stats;
                        try
                        {
                            uint hr = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
                            if (hr == 0)
                            {
                                stats.FreedBytes = beforeBytes;
                                stats.FilesDeleted = Math.Max(1, beforeItems);
                            }
                        }
                        catch { }
                        return stats;
                    }
                });
                FinalizeGroupSelection(recycleGroup);
                groups.Add(recycleGroup);
            }

            // Step 12: Real System Performance Registry Inspection
            ReportStep("System latency & responsiveness registry tweaks", "فحص تحسينات استجابة النظام في السجل");
            if (!token.IsCancellationRequested)
            {
                bool netTweakApplied = IsHklmNetworkTweakApplied();
                bool menuDelayApplied = IsMenuShowDelayApplied();
                bool sepProcApplied = IsExplorerSeparateProcessApplied();

                var optimGroup = new CareAppGroup
                {
                    Category = CareCategory.SystemOptimization,
                    AppName = isAr ? "تحسين استجابة النظام وتقليل التأخير" : "System Latency & Responsiveness",
                    Description = isAr ? "تحسينات السجل لرفع سرعة استجابة النواة ومستكشف ويندوز." : "Kernel and Explorer performance registry adjustments.",
                    IconEmoji = "🚀"
                };

                optimGroup.SubItems.Add(new CareSubItem
                {
                    Name = isAr ? "إلغاء تقييد الشبكة لتحسين سرعة الألعاب" : "Disable Network Throttling Index (Low Ping Gaming)",
                    Description = isAr ? "إيقاف تقييد حزم الشبكة في \u200EWindows 10/11\u200E لأقصى تدفق واستجابة." : "Disables Windows 10/11 network throttling for maximum throughput in games.",
                    TargetPath = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
                    SizeBytes = 0,
                    FileCount = 0,
                    IsSystemTweak = true,
                    IsAlreadyApplied = netTweakApplied,
                    IsSelected = !netTweakApplied,
                    RequiresAdmin = true,
                    CleanAction = () => ApplyHklmTweak(),
                    CleanWithStats = () => ApplyHklmTweak()
                });

                optimGroup.SubItems.Add(new CareSubItem
                {
                    Name = isAr ? "تقليل تأخير فتح القوائم (\u200E20ms\u200E)" : "Eliminate Menu Show Delay (20ms UI Response)",
                    Description = isAr ? "تقليل زمن حركة قوائم سطح المكتب وقائمة ابدأ." : "Reduces desktop context menu and Start Menu animation latency.",
                    TargetPath = @"HKCU\Control Panel\Desktop\MenuShowDelay",
                    SizeBytes = 0,
                    FileCount = 0,
                    IsSystemTweak = true,
                    IsAlreadyApplied = menuDelayApplied,
                    IsSelected = !menuDelayApplied,
                    CleanAction = () => ApplyMenuShowDelayTweak(),
                    CleanWithStats = () => ApplyMenuShowDelayTweak()
                });

                optimGroup.SubItems.Add(new CareSubItem
                {
                    Name = isAr ? "تشغيل نوافذ المستكشف بعملية منفصلة لمنع التعليق" : "Enable Explorer Separate Process (Prevent Taskbar Hangs)",
                    Description = isAr ? "تشغيل نوافذ المجلدات بمسار مستقل لمنع تجمد شريط المهام." : "Launches folder windows in a separate thread so browser stalls don't freeze desktop.",
                    TargetPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\SeparateProcess",
                    SizeBytes = 0,
                    FileCount = 0,
                    IsSystemTweak = true,
                    IsAlreadyApplied = sepProcApplied,
                    IsSelected = !sepProcApplied,
                    CleanAction = () => ApplyExplorerSeparateProcessTweak(),
                    CleanWithStats = () => ApplyExplorerSeparateProcessTweak()
                });

                FinalizeGroupSelection(optimGroup);
                groups.Add(optimGroup);
            }

            progress?.Report(new CareScanProgress
            {
                StepIndex = totalSteps,
                TotalSteps = totalSteps,
                CurrentArea = isAr ? "اكتمل الفحص الشامل" : "Deep scan complete",
                FilesFoundSoFar = groups.Sum(g => g.AllScannedFiles),
                BytesFoundSoFar = groups.Sum(g => g.AllScannedBytes)
            });

            return groups;
        }

        private static void ScanChromiumBrowser(
            List<CareAppGroup> groups,
            string userDataDir,
            string appName,
            string description,
            string iconEmoji,
            bool isAr)
        {
            if (!Directory.Exists(userDataDir)) return;

            var profileDirs = new List<(string Name, string Dir)>();
            string defaultDir = Path.Combine(userDataDir, "Default");
            if (Directory.Exists(defaultDir))
            {
                profileDirs.Add(("Default", defaultDir));
            }
            try
            {
                foreach (var dir in Directory.GetDirectories(userDataDir, "Profile *"))
                {
                    profileDirs.Add((Path.GetFileName(dir), dir));
                }
            }
            catch { }

            if (profileDirs.Count == 0) return;

            var browserGroup = new CareAppGroup
            {
                Category = CareCategory.PrivacySweep,
                AppName = appName,
                Description = description,
                IconEmoji = iconEmoji
            };

            bool multiProfile = profileDirs.Count > 1;
            foreach (var (profName, profPath) in profileDirs)
            {
                string suffix = multiProfile ? $" ({profName})" : string.Empty;
                AddDirSubItem(browserGroup, (isAr ? "ذاكرة الويب والصور المؤقتة" : "Web & Image Cache") + suffix, Path.Combine(profPath, "Cache"));
                AddDirSubItem(browserGroup, (isAr ? "ذاكرة الشيفرة البرمجية و\u200EV8\u200E" : "Code Cache & V8 Scripts") + suffix, Path.Combine(profPath, "Code Cache"));
                AddDirSubItem(browserGroup, (isAr ? "ذاكرة تظليل المعالج الرسومي \u200EGPU\u200E" : "GPU Shader Cache") + suffix, Path.Combine(profPath, "GPUCache"));
                AddFileSubItem(browserGroup, (isAr ? "ملفات تعريف الارتباط وتخزين المواقع" : "Cookies & Site Storage") + suffix, Path.Combine(profPath, @"Network\Cookies"));
                AddFileSubItem(browserGroup, (isAr ? "سجل التصفح وجلسات التبويب" : "Browsing History & Tab Sessions") + suffix, Path.Combine(profPath, "History"));
            }

            // Shared Chromium GPU shader caches under User Data root
            AddDirSubItem(browserGroup, isAr ? "ذاكرة تظليل \u200EGrShaderCache\u200E المشتركة" : "Shared GPU Shader Cache (GrShaderCache)", Path.Combine(userDataDir, "GrShaderCache"));
            AddDirSubItem(browserGroup, isAr ? "ذاكرة تظليل \u200EShaderCache\u200E المشتركة" : "Shared Shader Cache", Path.Combine(userDataDir, "ShaderCache"));

            FinalizeGroupSelection(browserGroup);
            if (browserGroup.SubItems.Count > 0) groups.Add(browserGroup);
        }

        private static void FinalizeGroupSelection(CareAppGroup group)
        {
            foreach (var sub in group.SubItems)
            {
                if (sub.IsSystemTweak)
                {
                    sub.IsSelected = !sub.IsAlreadyApplied;
                }
                else
                {
                    sub.IsSelected = sub.SizeBytes > 0 || sub.FileCount > 0;
                }
            }
            group.IsSelected = group.SubItems.Any(s => s.IsSelected);
        }

        public static (long SizeBytes, int ItemCount) QueryActualRecycleBin()
        {
            try
            {
                var info = new SHQUERYRBINFO
                {
                    cbSize = (uint)Marshal.SizeOf<SHQUERYRBINFO>()
                };
                int hr = SHQueryRecycleBinW(null, ref info);
                if (hr == 0)
                {
                    long bytes = Math.Max(0L, info.i64Size);
                    int items = (int)Math.Clamp(info.i64NumItems, 0L, int.MaxValue);
                    return (bytes, items);
                }
            }
            catch { }
            return (0L, 0);
        }

        private static bool IsHklmNetworkTweakApplied()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", false);
                if (key == null) return false;
                object? netIdx = key.GetValue("NetworkThrottlingIndex");
                object? resp = key.GetValue("SystemResponsiveness");
                return netIdx != null && Convert.ToInt32(netIdx) == unchecked((int)0xFFFFFFFF) &&
                       resp != null && Convert.ToInt32(resp) == 0;
            }
            catch { return false; }
        }

        private static bool IsMenuShowDelayApplied()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", false);
                string? val = key?.GetValue("MenuShowDelay")?.ToString();
                return int.TryParse(val, out int ms) && ms <= 20;
            }
            catch { return false; }
        }

        private static bool IsExplorerSeparateProcessApplied()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", false);
                object? val = key?.GetValue("SeparateProcess");
                return val != null && Convert.ToInt32(val) == 1;
            }
            catch { return false; }
        }

        // Backward compatibility method
        public List<CareItem> ScanAll()
        {
            var hierarchical = ScanAllHierarchical();
            var flat = new List<CareItem>();
            foreach (var g in hierarchical)
            {
                flat.Add(new CareItem
                {
                    Category = g.Category,
                    Title = $"{g.IconEmoji} {g.AppName}",
                    Description = g.Description,
                    SizeBytes = g.TotalBytes,
                    FileCount = g.TotalFiles,
                    IsSelected = g.IsSelected,
                    CleanAction = () =>
                    {
                        foreach (var sub in g.SubItems)
                        {
                            if (sub.IsSelected && sub.CleanAction != null)
                            {
                                try { sub.CleanAction(); } catch { }
                            }
                        }
                    }
                });
            }
            return flat;
        }

        public CareCleanSummary CleanSelectedGroups(IEnumerable<CareAppGroup> selectedGroups)
        {
            var summary = new CareCleanSummary();
            foreach (var g in selectedGroups)
            {
                if (!g.IsSelected) continue;
                bool touchedGroup = false;
                foreach (var sub in g.SubItems)
                {
                    if (!sub.IsSelected) continue;
                    touchedGroup = true;
                    if (sub.CleanWithStats != null)
                    {
                        var st = sub.CleanWithStats();
                        summary.ActualFreedBytes += st.FreedBytes;
                        summary.FilesDeleted += st.FilesDeleted;
                        summary.TweaksApplied += st.TweaksApplied;
                        summary.LockedSkipped += st.LockedSkipped;
                        summary.AccessDenied += st.AccessDenied;
                        sub.IsCleaned = st.AccessDenied == 0;
                    }
                    else if (sub.CleanAction != null)
                    {
                        try
                        {
                            sub.CleanAction();
                            sub.IsCleaned = true;
                        }
                        catch (UnauthorizedAccessException)
                        {
                            summary.AccessDenied++;
                        }
                        catch
                        {
                            summary.LockedSkipped++;
                        }
                    }
                }
                if (touchedGroup) summary.CleanedCategories++;
            }
            return summary;
        }

        private static CareCleanStats ApplyHklmTweak()
        {
            var stats = new CareCleanStats();
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", true);
                if (key != null)
                {
                    key.SetValue("NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord);
                    key.SetValue("SystemResponsiveness", 0, RegistryValueKind.DWord);
                    stats.TweaksApplied = 1;
                }
                else
                {
                    stats.AccessDenied = 1;
                }
            }
            catch (UnauthorizedAccessException) { stats.AccessDenied = 1; }
            catch (System.Security.SecurityException) { stats.AccessDenied = 1; }
            catch { stats.LockedSkipped = 1; }
            return stats;
        }

        private static CareCleanStats ApplyMenuShowDelayTweak()
        {
            var stats = new CareCleanStats();
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop", true);
                if (key != null)
                {
                    key.SetValue("MenuShowDelay", "20", RegistryValueKind.String);
                    stats.TweaksApplied = 1;
                }
            }
            catch (UnauthorizedAccessException) { stats.AccessDenied = 1; }
            catch { stats.LockedSkipped = 1; }
            return stats;
        }

        private static CareCleanStats ApplyExplorerSeparateProcessTweak()
        {
            var stats = new CareCleanStats();
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", true);
                if (key != null)
                {
                    key.SetValue("SeparateProcess", 1, RegistryValueKind.DWord);
                    stats.TweaksApplied = 1;
                }
            }
            catch (UnauthorizedAccessException) { stats.AccessDenied = 1; }
            catch { stats.LockedSkipped = 1; }
            return stats;
        }

        private static void AddDirSubItem(CareAppGroup group, string title, string path, bool requiresAdmin = false)
        {
            if (!Directory.Exists(path)) return;

            long size = 0;
            int count = 0;
            try
            {
                var opts = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                };
                foreach (var f in Directory.EnumerateFiles(path, "*.*", opts))
                {
                    try
                    {
                        size += new FileInfo(f).Length;
                        count++;
                    }
                    catch { }
                }
            }
            catch { }

            group.SubItems.Add(new CareSubItem
            {
                Name = title,
                TargetPath = path,
                SizeBytes = size,
                FileCount = count,
                IsSelected = size > 0 || count > 0,
                RequiresAdmin = requiresAdmin,
                CleanAction = () => PurgeDirectoryContents(path),
                CleanWithStats = () => PurgeDirectoryContents(path)
            });
        }

        private static void AddFileSubItem(CareAppGroup group, string title, string filePath, bool requiresAdmin = false)
        {
            if (!File.Exists(filePath)) return;

            long size = 0;
            try { size = new FileInfo(filePath).Length; } catch { }

            group.SubItems.Add(new CareSubItem
            {
                Name = title,
                TargetPath = filePath,
                SizeBytes = size,
                FileCount = 1,
                IsSelected = size > 0,
                RequiresAdmin = requiresAdmin,
                CleanAction = () => DeleteSingleFileWithStats(filePath),
                CleanWithStats = () => DeleteSingleFileWithStats(filePath)
            });
        }

        private static void AddPatternSubItem(CareAppGroup group, string title, string dirPath, string pattern, bool requiresAdmin = false)
        {
            if (!Directory.Exists(dirPath)) return;

            long size = 0;
            int count = 0;
            try
            {
                var files = Directory.GetFiles(dirPath, pattern);
                foreach (var f in files)
                {
                    try
                    {
                        size += new FileInfo(f).Length;
                        count++;
                    }
                    catch { }
                }
            }
            catch { }

            group.SubItems.Add(new CareSubItem
            {
                Name = title,
                TargetPath = dirPath,
                SizeBytes = size,
                FileCount = count,
                IsSelected = size > 0 || count > 0,
                RequiresAdmin = requiresAdmin,
                CleanAction = () => DeletePatternWithStats(dirPath, pattern),
                CleanWithStats = () => DeletePatternWithStats(dirPath, pattern)
            });
        }

        private static CareCleanStats DeleteSingleFileWithStats(string filePath)
        {
            var stats = new CareCleanStats();
            try
            {
                if (!File.Exists(filePath)) return stats;
                long len = new FileInfo(filePath).Length;
                TryClearReadOnly(filePath);
                File.Delete(filePath);
                stats.FreedBytes = len;
                stats.FilesDeleted = 1;
            }
            catch (UnauthorizedAccessException) { stats.AccessDenied = 1; }
            catch (IOException) { stats.LockedSkipped = 1; }
            catch { stats.LockedSkipped = 1; }
            return stats;
        }

        private static CareCleanStats DeletePatternWithStats(string dirPath, string pattern)
        {
            var stats = new CareCleanStats();
            try
            {
                foreach (var f in Directory.GetFiles(dirPath, pattern))
                {
                    var s = DeleteSingleFileWithStats(f);
                    stats.FreedBytes += s.FreedBytes;
                    stats.FilesDeleted += s.FilesDeleted;
                    stats.LockedSkipped += s.LockedSkipped;
                    stats.AccessDenied += s.AccessDenied;
                }
            }
            catch (UnauthorizedAccessException) { stats.AccessDenied++; }
            catch { }
            return stats;
        }

        private static CareCleanStats PurgeDirectoryContents(string dirPath)
        {
            var stats = new CareCleanStats();
            PurgeDirectoryRecursive(dirPath, deleteSelf: false, stats);
            return stats;
        }

        private static void PurgeDirectoryRecursive(string dirPath, bool deleteSelf, CareCleanStats stats)
        {
            try
            {
                if (!Directory.Exists(dirPath)) return;

                var di = new DirectoryInfo(dirPath);
                if ((di.Attributes & FileAttributes.ReparsePoint) != 0) return;

                string[] files;
                try { files = Directory.GetFiles(dirPath); }
                catch (UnauthorizedAccessException) { stats.AccessDenied++; return; }
                catch { return; }

                foreach (var f in files)
                {
                    try
                    {
                        long len = 0;
                        try { len = new FileInfo(f).Length; } catch { }
                        TryClearReadOnly(f);
                        File.Delete(f);
                        stats.FreedBytes += len;
                        stats.FilesDeleted++;
                    }
                    catch (UnauthorizedAccessException) { stats.AccessDenied++; }
                    catch (IOException) { stats.LockedSkipped++; }
                    catch { stats.LockedSkipped++; }
                }

                string[] dirs;
                try { dirs = Directory.GetDirectories(dirPath); }
                catch (UnauthorizedAccessException) { stats.AccessDenied++; return; }
                catch { return; }

                foreach (var d in dirs)
                {
                    PurgeDirectoryRecursive(d, deleteSelf: true, stats);
                }

                if (deleteSelf)
                {
                    try
                    {
                        if (!Directory.EnumerateFileSystemEntries(dirPath).GetEnumerator().MoveNext())
                        {
                            Directory.Delete(dirPath, false);
                        }
                    }
                    catch { }
                }
            }
            catch (UnauthorizedAccessException) { stats.AccessDenied++; }
            catch { }
        }

        private static void TryClearReadOnly(string filePath)
        {
            try
            {
                var attrs = File.GetAttributes(filePath);
                if ((attrs & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != 0)
                {
                    File.SetAttributes(filePath, FileAttributes.Normal);
                }
            }
            catch { }
        }
    }
}
