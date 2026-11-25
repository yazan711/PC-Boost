using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CleanRamBoosterApp.Models;
using CleanRamBoosterApp.Services;
using CleanRamBoosterApp.WpfUI;
using CleanRamBoosterApp.WpfUI.Toolbox;
using Application = System.Windows.Application;

namespace CleanRamBoosterApp.Tests;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    [STAThread]
    private static int Main()
    {
        if (Application.Current == null)
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        }

        Console.WriteLine("============================================================");
        Console.WriteLine("  PC Boost Automated Unit & Integration Test Suite");
        Console.WriteLine("============================================================");

        RunTest("Localization EN/AR Dictionary Parity", TestLocalizationParity);
        RunTest("GPU Classification (dGPU vs iGPU)", TestGpuClassification);
        RunTest("Live Hardware Report (CPU Cores/Threads + VRAM)", TestLiveHardwareReport);
        RunTest("Hardware Report HTML & Text Export", TestHardwareReportExports);
        RunTest("DiskTools Empty Folders (Unlimited 650+)", TestDiskToolsEmptyFolders);
        RunTest("DiskTools Duplicate Finder (Size + Head/Tail MD5)", TestDiskToolsDuplicates);
        RunTest("DiskTools Large File Finder (Unlimited & Ordered)", TestDiskToolsLargeFiles);
        RunTest("StartupHistoryService Persist/Pop/Push/Cap", TestStartupHistoryService);
        RunTest("SettingsService Atomic Save & Reload", TestSettingsAtomicSave);
        RunTest("Auto-Clean 15-Min Cooldown & Dashboard Control Persistence", TestAutoCleanCooldownAndDashboardWiring);
        RunTest("DiagnosticLogger Write & Support Bundle Export", TestDiagnosticLogger);
        RunTest("CareService Deep Scan Determinate Progress", TestCareDeepScan);
        RunTest("WPF Views & All 6 WPF Toolbox Windows (Dark/Light + EN/AR)", TestUiSmokeAndVirtualMode);

        Console.WriteLine("============================================================");
        Console.WriteLine($"  Summary: {_passed} passed, {_failed} failed");
        Console.WriteLine("============================================================");

        return _failed == 0 ? 0 : 1;
    }

    private static void RunTest(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine($"  [PASS] {name}");
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine($"  [FAIL] {name}");
            Console.WriteLine($"         {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void TestLocalizationParity()
    {
        var enField = typeof(LocalizationService).GetField("EnglishStrings", BindingFlags.NonPublic | BindingFlags.Static);
        var arField = typeof(LocalizationService).GetField("ArabicStrings", BindingFlags.NonPublic | BindingFlags.Static);
        Assert(enField != null && arField != null, "Localization dictionaries not found via reflection.");

        var en = (Dictionary<string, string>)enField!.GetValue(null)!;
        var ar = (Dictionary<string, string>)arField!.GetValue(null)!;

        Assert(en.Count >= 160, $"Expected >=160 EN keys, found {en.Count}");
        var missingInAr = en.Keys.Where(k => !ar.ContainsKey(k)).ToList();
        var missingInEn = ar.Keys.Where(k => !en.ContainsKey(k)).ToList();

        Assert(missingInAr.Count == 0, $"Keys missing in ArabicStrings: {string.Join(", ", missingInAr)}");
        Assert(missingInEn.Count == 0, $"Keys missing in EnglishStrings: {string.Join(", ", missingInEn)}");
        Assert(en.Values.All(v => !string.IsNullOrWhiteSpace(v)), "Empty value found in EnglishStrings.");
        Assert(ar.Values.All(v => !string.IsNullOrWhiteSpace(v)), "Empty value found in ArabicStrings.");
    }

    private static void TestGpuClassification()
    {
        Assert(!HardwareInfoService.ClassifyIsIntegratedGpu("NVIDIA GeForce RTX 4060", 0x10DE, 8UL * 1024 * 1024 * 1024), "RTX 4060 must be Dedicated");
        Assert(!HardwareInfoService.ClassifyIsIntegratedGpu("AMD Radeon RX 7800 XT", 0x1002, 16UL * 1024 * 1024 * 1024), "RX 7800 XT must be Dedicated");
        Assert(!HardwareInfoService.ClassifyIsIntegratedGpu("Intel(R) Arc(TM) A770 Graphics", 0x8086, 16UL * 1024 * 1024 * 1024), "Intel Arc A770 must be Dedicated");

        Assert(HardwareInfoService.ClassifyIsIntegratedGpu("AMD Radeon (TM) Graphics", 0x1002, 512UL * 1024 * 1024), "AMD Radeon (TM) Graphics 512MB must be Integrated");
        Assert(HardwareInfoService.ClassifyIsIntegratedGpu("AMD Radeon 780M Graphics", 0x1002, 2UL * 1024 * 1024 * 1024), "Radeon 780M must be Integrated");
        Assert(HardwareInfoService.ClassifyIsIntegratedGpu("Intel(R) Iris(R) Xe Graphics", 0x8086, 128UL * 1024 * 1024), "Intel Iris Xe must be Integrated");
        Assert(HardwareInfoService.ClassifyIsIntegratedGpu("Intel(R) UHD Graphics 770", 0x8086, 128UL * 1024 * 1024), "Intel UHD 770 must be Integrated");

        Assert(HardwareInfoService.FormatVramCompact(512UL * 1024 * 1024) == "512 MB", "512 MB format mismatch");
        Assert(HardwareInfoService.FormatVramCompact(8UL * 1024 * 1024 * 1024) == "8 GB", "8 GB format mismatch");
        Assert(HardwareInfoService.FormatVramCompact((ulong)(1.5 * 1024 * 1024 * 1024)) == "1.5 GB", "1.5 GB format mismatch");
    }

    private static void TestLiveHardwareReport()
    {
        var svc = new HardwareInfoService();
        var rep = svc.GetDetailedReport(forceRefresh: true);
        Assert(!string.IsNullOrWhiteSpace(rep.CpuName), "CpuName must not be empty");
        Assert(rep.CpuPhysicalCores > 0, $"CpuPhysicalCores must be > 0, got {rep.CpuPhysicalCores}");
        Assert(rep.CpuLogicalCores >= rep.CpuPhysicalCores, $"CpuLogicalCores ({rep.CpuLogicalCores}) must be >= CpuPhysicalCores ({rep.CpuPhysicalCores})");
        Assert(rep.TotalMemoryGB > 0, "TotalMemoryGB must be > 0");
        Assert(rep.GpuAdapters.Count >= 1, "Expected at least 1 hardware GPU adapter via DXGI");
        Assert(rep.DedicatedGpu != null || rep.IntegratedGpu != null, "Expected either DedicatedGpu or IntegratedGpu populated");
    }

    private static void TestHardwareReportExports()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "PCBoost_Test_HwExport_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var rep = new HardwareInfoService().GetDetailedReport();
            string htmlPath = Path.Combine(tmpDir, "report.html");
            string txtPath = Path.Combine(tmpDir, "report.txt");

            HardwareInfoService.ExportToHtml(rep, htmlPath);
            HardwareInfoService.ExportToText(rep, txtPath);

            Assert(File.Exists(htmlPath) && new FileInfo(htmlPath).Length > 1000, "HTML report missing or too small");
            Assert(File.Exists(txtPath) && new FileInfo(txtPath).Length > 500, "Text report missing or too small");
            Assert(File.ReadAllText(htmlPath).Contains("System Information Report"), "HTML title missing");
        }
        finally
        {
            try { Directory.Delete(tmpDir, true); } catch { }
        }
    }

    private static void TestDiskToolsEmptyFolders()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "PCBoost_Test_Empty_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            for (int i = 0; i < 650; i++)
                Directory.CreateDirectory(Path.Combine(tmpDir, $"empty_{i:D4}"));

            string nonEmpty = Path.Combine(tmpDir, "non_empty");
            Directory.CreateDirectory(nonEmpty);
            File.WriteAllText(Path.Combine(nonEmpty, "keep.txt"), "keep");

            var disk = new DiskToolsService();
            var syncRes = disk.FindEmptyFolders(tmpDir);
            Assert(syncRes.Count == 650, $"Expected 650 empty folders (unlimited), got {syncRes.Count}");

            var asyncRes = disk.FindEmptyFoldersAsync(tmpDir).GetAwaiter().GetResult();
            Assert(asyncRes.Count == 650, $"Expected 650 empty folders async, got {asyncRes.Count}");
        }
        finally
        {
            try { Directory.Delete(tmpDir, true); } catch { }
        }
    }

    private static void TestDiskToolsDuplicates()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "PCBoost_Test_Dups_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            // Create 200KB identical pair + 1 same-size file with identical first 64KB but different tail!
            byte[] payloadA = new byte[200 * 1024];
            new Random(42).NextBytes(payloadA);

            byte[] payloadSameHeadDiffTail = (byte[])payloadA.Clone();
            payloadSameHeadDiffTail[payloadSameHeadDiffTail.Length - 10] ^= 0xFF;

            File.WriteAllBytes(Path.Combine(tmpDir, "dup1.bin"), payloadA);
            File.WriteAllBytes(Path.Combine(tmpDir, "dup2.bin"), payloadA);
            File.WriteAllBytes(Path.Combine(tmpDir, "not_a_dup.bin"), payloadSameHeadDiffTail);

            var disk = new DiskToolsService();
            var groups = disk.FindDuplicates(tmpDir);

            Assert(groups.Count == 1, $"Expected exactly 1 duplicate group, got {groups.Count}");
            Assert(groups[0].FilePaths.Count == 2, $"Expected 2 files in group (excluding tail-different file), got {groups[0].FilePaths.Count}");
            Assert(groups[0].FilePaths.All(p => !p.EndsWith("not_a_dup.bin", StringComparison.OrdinalIgnoreCase)), "Tail-different file must NOT be marked duplicate");
        }
        finally
        {
            try { Directory.Delete(tmpDir, true); } catch { }
        }
    }

    private static void TestDiskToolsLargeFiles()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "PCBoost_Test_Large_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            // Create 125 files above 16KB threshold (testing no artificial 100-item cap)
            for (int i = 0; i < 125; i++)
            {
                string f = Path.Combine(tmpDir, $"file_{i:D3}.dat");
                File.WriteAllBytes(f, new byte[20000 + i * 10]);
            }
            File.WriteAllBytes(Path.Combine(tmpDir, "small.dat"), new byte[1024]);

            var disk = new DiskToolsService();
            var found = disk.FindLargeFiles(tmpDir, minSizeBytes: 16384);
            Assert(found.Count == 125, $"Expected all 125 matching files (unlimited), got {found.Count}");
            Assert(found[0].SizeBytes >= found[found.Count - 1].SizeBytes, "Results must be ordered descending by size");
        }
        finally
        {
            try { Directory.Delete(tmpDir, true); } catch { }
        }
    }

    private static void TestStartupHistoryService()
    {
        string tmpFile = Path.Combine(Path.GetTempPath(), "PCBoost_Test_History_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var svc = new StartupHistoryService(tmpFile);
            Assert(svc.Count == 0, "Fresh history must be empty");

            var item = new StartupItem
            {
                Name = "TestApp",
                LocationType = StartupType.RegistryUser,
                RegistryValueName = "TestAppRun",
                IsEnabled = true
            };

            svc.RecordChanges("Toggle", new[] { (item, true, false) });
            Assert(svc.Count == 1, "Expected 1 history entry");
            Assert(File.Exists(tmpFile), "History JSON must be persisted");

            // Reload from disk in fresh instance
            var reloaded = new StartupHistoryService(tmpFile);
            Assert(reloaded.Count == 1, "Reloaded history must contain 1 entry");
            var latest = reloaded.PopLatest();
            Assert(latest != null && latest.Changes.Count == 1 && latest.Changes[0].PreviousEnabled, "Popped entry mismatch");
            Assert(reloaded.Count == 0, "History must be empty after pop");

            reloaded.PushBack(latest!);
            Assert(reloaded.Count == 1, "PushBack must restore popped entry");
        }
        finally
        {
            try { if (File.Exists(tmpFile)) File.Delete(tmpFile); } catch { }
        }
    }

    private static void TestSettingsAtomicSave()
    {
        var orig = SettingsService.Instance.Current.CreateRestorePointBeforeCare;
        try
        {
            SettingsService.Instance.Update(s => s.CreateRestorePointBeforeCare = !orig);
            var reloaded = SettingsService.Instance.LoadSettings();
            Assert(reloaded.CreateRestorePointBeforeCare == !orig, "Settings atomic save did not persist updated flag");

            string tmp = Path.Combine(DiagnosticLogger.LogDirectory, "settings.json.tmp");
            Assert(!File.Exists(tmp), "Temporary settings.json.tmp file must not remain after atomic move");
        }
        finally
        {
            SettingsService.Instance.Update(s => s.CreateRestorePointBeforeCare = orig);
        }
    }

    private static void TestAutoCleanCooldownAndDashboardWiring()
    {
        var fresh = new AppSettings();
        Assert(fresh.AutoCleanIntervalSeconds == 900, $"Default AutoCleanIntervalSeconds must be 900s (15 min), got {fresh.AutoCleanIntervalSeconds}");

        var legacy = SettingsService.NormalizeSettings(new AppSettings { AutoCleanIntervalSeconds = 60, AutoCleanThresholdPercent = 80 });
        Assert(legacy.AutoCleanIntervalSeconds == 900, $"Legacy 60s cooldown must upgrade to 900s (15 min), got {legacy.AutoCleanIntervalSeconds}");
        Assert(legacy.AutoCleanThresholdPercent == 80, "Valid 80% threshold must be preserved");

        var cur = SettingsService.Instance.Current;
        bool origAutoClean = cur.AutoCleanEnabled;
        int origThreshold = cur.AutoCleanThresholdPercent;
        bool origMinTray = cur.MinimizeToTray;
        var origLang = LocalizationService.CurrentLanguage;
        try
        {
            var dash = new DashboardView();
            var settingsView = new SettingsView();

            // Select 80% Load (index 2) and verify persistence + survival across language switch
            dash.CbThreshold.SelectedIndex = 2;
            Assert(SettingsService.Instance.Current.AutoCleanThresholdPercent == 80, "Selecting 80% Load on Dashboard must save 80%");

            LocalizationService.SetLanguage(AppLanguage.Arabic);
            dash.ApplyLanguage();
            Assert(SettingsService.Instance.Current.AutoCleanThresholdPercent == 80, "Switching language must preserve 80% threshold");
            Assert(dash.CbThreshold.SelectedIndex == 2, "Arabic threshold dropdown must stay at index 2 (80%)");

            // Toggle Auto-Clean off/on
            dash.ChkAutoClean.IsChecked = false;
            Assert(!SettingsService.Instance.Current.AutoCleanEnabled, "Unchecking Auto-Clean must save false");
            Assert(!dash.CbThreshold.IsEnabled, "Threshold ComboBox must be disabled when Auto-Clean is off");
            dash.ChkAutoClean.IsChecked = true;
            Assert(SettingsService.Instance.Current.AutoCleanEnabled, "Checking Auto-Clean must save true");
            Assert(dash.CbThreshold.IsEnabled, "Threshold ComboBox must re-enable when Auto-Clean is on");

            // Toggle Keep in Tray and verify SettingsView sync
            dash.ChkMinimizeToTray.IsChecked = !origMinTray;
            Assert(SettingsService.Instance.Current.MinimizeToTray == !origMinTray, "Dashboard Keep in Tray toggle must persist");
            settingsView.SyncSettingsControls();
        }
        finally
        {
            LocalizationService.SetLanguage(origLang);
            SettingsService.Instance.Update(st =>
            {
                st.AutoCleanEnabled = origAutoClean;
                st.AutoCleanThresholdPercent = origThreshold;
                st.MinimizeToTray = origMinTray;
            });
        }
    }

    private static void TestDiagnosticLogger()
    {
        string marker = "TestMarker_" + Guid.NewGuid().ToString("N");
        DiagnosticLogger.LogInfo("UnitTest", marker);
        string tail = DiagnosticLogger.ReadRecentLogTail(30);
        Assert(tail.Contains(marker), "DiagnosticLogger tail did not include just-logged marker");

        string bundle = DiagnosticLogger.ExportSupportDiagnosticsText();
        Assert(bundle.Contains("PC Boost Support Diagnostics Bundle"), "Diagnostics bundle header missing");
        Assert(bundle.Contains("Hardware Summary"), "Diagnostics bundle hardware summary missing");
        Assert(bundle.Contains(marker), "Diagnostics bundle did not include recent log entries");
    }

    private static void TestCareDeepScan()
    {
        var svc = new CareService();
        int maxStep = 0;
        int totalSteps = 0;
        var progress = new Progress<CareScanProgress>(p =>
        {
            if (p.StepIndex > maxStep) maxStep = p.StepIndex;
            totalSteps = p.TotalSteps;
        });

        var groups = svc.ScanAllHierarchicalAsync(progress).GetAwaiter().GetResult();
        // Give Progress<T> callbacks a moment if posted
        Thread.Sleep(50);
        Assert(groups.Count >= 5, $"Expected >=5 Care categories, got {groups.Count}");
        Assert(groups.Any(g => g.Category == CareCategory.SystemOptimization), "SystemOptimization category missing");
        Assert(groups.Any(g => g.Category == CareCategory.JunkFiles), "JunkFiles category missing");
        Assert(groups.Any(g => g.Category == CareCategory.PrivacySweep), "PrivacySweep category missing");
    }

    private static void TestUiSmokeAndVirtualMode()
    {
        var origLang = LocalizationService.CurrentLanguage;
        bool origDark = WpfTheme.IsDarkMode;
        try
        {
            foreach (bool dark in new[] { true, false })
            {
                WpfTheme.IsDarkMode = dark;
                foreach (var lang in new[] { AppLanguage.English, AppLanguage.Arabic })
                {
                    LocalizationService.SetLanguage(lang);

                    var settings = new SettingsView();
                    settings.ApplyLanguage();
                    settings.RefreshTheme();

                    var care = new CareView(new CareService());
                    care.ApplyLanguage();
                    care.RefreshTheme();

                    var turbo = new TurboView(new TurboBoostService());
                    turbo.ApplyLanguage();
                    turbo.RefreshTheme();

                    var startup = new StartupView(new StartupService());
                    startup.ApplyLanguage();
                    startup.RefreshTheme();
                }
            }

            // Verify all 6 pure-WPF Toolbox windows instantiate cleanly & disk scanners use row virtualization
            var dupWin = new ToolboxDuplicateFinderWindow();
            Assert(dupWin.IsVirtualized, "ToolboxDuplicateFinderWindow must use WPF row virtualization");
            dupWin.RefreshTheme();

            var largeWin = new ToolboxLargeFilesWindow();
            Assert(largeWin.IsVirtualized, "ToolboxLargeFilesWindow must use WPF row virtualization");
            largeWin.RefreshTheme();

            var emptyWin = new ToolboxEmptyFoldersWindow();
            Assert(emptyWin.IsVirtualized, "ToolboxEmptyFoldersWindow must use WPF row virtualization");
            emptyWin.RefreshTheme();

            var autoWin = new ToolboxAutoShutdownWindow();
            autoWin.RefreshTheme();

            var winFixWin = new ToolboxWinFixWindow();
            winFixWin.RefreshTheme();

            var sysInfoWin = new ToolboxSystemInfoWindow(new HardwareInfoService().GetDetailedReport());
            sysInfoWin.SelectCategory(2); // CPU live chart
            sysInfoWin.SelectCategory(3); // Memory slots visual bar
            sysInfoWin.RefreshTheme();
            sysInfoWin.Close();
        }
        finally
        {
            LocalizationService.SetLanguage(origLang);
            WpfTheme.IsDarkMode = origDark;
        }
    }
}
