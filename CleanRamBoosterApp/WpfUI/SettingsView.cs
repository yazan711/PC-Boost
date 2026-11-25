using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CleanRamBoosterApp.Services;
using CleanRamBoosterApp.UI;
using Microsoft.Win32;

namespace CleanRamBoosterApp.WpfUI
{
    public class SettingsView : UserControl
    {
        private readonly HardwareInfoService hwService = new();
        private DetailedSystemReport? cachedReport;

        // UI Controls
        private readonly TextBlock lblHead;
        private readonly TextBlock lblSub;

        // Settings Controls
        private readonly Border cardPref;
        private readonly TextBlock lblPrefTitle;
        private readonly CheckBox chkStartWithWindows;
        private readonly CheckBox chkRunAsAdminOnBoot;
        private readonly CheckBox chkMinTray;
        private readonly CheckBox chkDisableRamNotif;
        private readonly CheckBox chkStartupAlert;
        private readonly TextBlock lblLang;
        private readonly ComboBox cbLang;
        private readonly Button btnElevate;
        private readonly TextBlock lblSettingsFile;

        // Hardware Controls
        private readonly Border cardHw;
        private readonly TextBlock lblHwTitle;
        private readonly TextBlock lblCpu;
        private readonly TextBlock lblGpu;
        private readonly TextBlock lblIgpu;
        private readonly TextBlock lblVram;
        private readonly TextBlock lblRam;
        private readonly TextBlock lblOs;
        private readonly TextBlock lblUptime;
        private readonly System.Windows.Threading.DispatcherTimer vramLiveTimer;
        private bool isRefreshingVram;
        private readonly Button btnViewDetails;
        private readonly Button btnExportHtml;
        private readonly Button btnOpenLogs;
        private readonly Button btnExportDiagnostics;
        private readonly Button btnRestartApp;

        private readonly bool isAdmin;

        public SettingsView()
        {
            isAdmin = CheckIfAdmin();

            var rootGrid = new Grid
            {
                Margin = new Thickness(24, 20, 24, 20)
            };
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Scrollable Content

            // 1. Header
            var headRow = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var headBadge = WpfIcons.CreateHeaderBadge("Settings", Color.FromRgb(59, 130, 246), 42, 22);
            headBadge.Margin = new Thickness(0, 0, 14, 0);
            Grid.SetColumn(headBadge, 0);
            headRow.Children.Add(headBadge);

            var headStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            lblHead = new TextBlock
            {
                Text = UITheme.StripLeadingIcon(LocalizationService.T("SettingsHeader", "⚙️ Application Settings & System Specs")),
                FontSize = 20,
                FontWeight = FontWeights.SemiBold
            };
            lblSub = new TextBlock
            {
                Text = LocalizationService.T("SettingsSub", "Configure PC Boost behavior, background automation, and system integration."),
                FontSize = 13,
                Margin = new Thickness(0, 3, 0, 0)
            };
            headStack.Children.Add(lblHead);
            headStack.Children.Add(lblSub);
            Grid.SetColumn(headStack, 1);
            headRow.Children.Add(headStack);
            Grid.SetRow(headRow, 0);
            rootGrid.Children.Add(headRow);

            // 2. Scrollable Cards Container
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var cardsStack = new StackPanel();

            // -------------------------------------------------------------
            // Card 1: Preferences & Automation
            // -------------------------------------------------------------
            cardPref = CreateCardBorder();
            cardPref.Margin = new Thickness(0, 0, 0, 16);
            var prefGrid = new Grid { Margin = new Thickness(18, 16, 18, 16) };
            prefGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Title
            prefGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Start with windows
            prefGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Run as Administrator on next reboot
            prefGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Minimize to tray
            prefGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Disable notifications (REQUESTED)
            prefGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // New Startup Alert (Desktop + Store)
            prefGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Language row
            prefGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Elevation & Config path

            lblPrefTitle = new TextBlock
            {
                Text = LocalizationService.IsArabic ? "إعدادات التطبيق وتفضيلات النظام" : "Application Settings & Persistence",
                FontSize = 15,
                FontWeight = FontWeights.Medium,
                Margin = new Thickness(0, 0, 0, 14)
            };
            Grid.SetRow(lblPrefTitle, 0);
            prefGrid.Children.Add(lblPrefTitle);

            // Start with Windows
            chkStartWithWindows = new CheckBox
            {
                Content = LocalizationService.T("SettingStartWindows", "Start automatically with Windows boot (in System Tray)"),
                FontSize = 13,
                FontWeight = FontWeights.Normal,
                Foreground = WpfTheme.TextPrimaryBrush,
                IsChecked = SettingsService.Instance.Current.StartWithWindows,
                Margin = new Thickness(0, 0, 0, 11)
            };
            chkStartWithWindows.Click += (s, e) => OnBootCheckboxesClicked(toggledAdminBox: false);
            Grid.SetRow(chkStartWithWindows, 1);
            prefGrid.Children.Add(chkStartWithWindows);

            // Run as Administrator on next reboot
            chkRunAsAdminOnBoot = new CheckBox
            {
                Content = LocalizationService.T("SettingRunAsAdminOnBoot", "Run as Administrator on next reboot (no UAC prompt)"),
                FontSize = 13,
                FontWeight = FontWeights.Normal,
                Foreground = WpfTheme.TextPrimaryBrush,
                IsChecked = SettingsService.Instance.Current.RunAsAdminOnBoot,
                Margin = new Thickness(24, 0, 0, 11)
            };
            chkRunAsAdminOnBoot.Click += (s, e) => OnBootCheckboxesClicked(toggledAdminBox: true);
            Grid.SetRow(chkRunAsAdminOnBoot, 2);
            prefGrid.Children.Add(chkRunAsAdminOnBoot);

            // Minimize to Tray
            chkMinTray = new CheckBox
            {
                Content = LocalizationService.T("SettingMinimizeTray", "Minimize to System Tray on close"),
                FontSize = 13,
                FontWeight = FontWeights.Normal,
                Foreground = WpfTheme.TextPrimaryBrush,
                IsChecked = SettingsService.Instance.Current.MinimizeToTray,
                Margin = new Thickness(0, 0, 0, 11)
            };
            chkMinTray.Click += (s, e) =>
            {
                SettingsService.Instance.Update(st => st.MinimizeToTray = chkMinTray.IsChecked == true);
            };
            Grid.SetRow(chkMinTray, 3);
            prefGrid.Children.Add(chkMinTray);

            // Disable RAM Clean Notification (USER REQUEST: DEFAULT IS UNCHECKED)
            chkDisableRamNotif = new CheckBox
            {
                Content = LocalizationService.T("SettingDisableRamNotif", "Disable notifications when RAM is cleaned"),
                FontSize = 13,
                FontWeight = FontWeights.Normal,
                Foreground = WpfTheme.TextPrimaryBrush,
                IsChecked = SettingsService.Instance.Current.DisableRamCleanNotifications,
                Margin = new Thickness(0, 0, 0, 11)
            };
            chkDisableRamNotif.Click += (s, e) =>
            {
                SettingsService.Instance.Update(st => st.DisableRamCleanNotifications = chkDisableRamNotif.IsChecked == true);
            };
            Grid.SetRow(chkDisableRamNotif, 4);
            prefGrid.Children.Add(chkDisableRamNotif);

            // New Startup Item & Store App Alert Toggle
            chkStartupAlert = new CheckBox
            {
                Content = LocalizationService.T("SettingStartupAlert", "Alert when a new desktop app or Microsoft Store app is added to startup"),
                FontSize = 13,
                FontWeight = FontWeights.Normal,
                Foreground = WpfTheme.TextPrimaryBrush,
                IsChecked = SettingsService.Instance.Current.StartupAlertEnabled,
                Margin = new Thickness(0, 0, 0, 15)
            };
            chkStartupAlert.Click += (s, e) =>
            {
                SettingsService.Instance.Update(st => st.StartupAlertEnabled = chkStartupAlert.IsChecked == true);
            };
            Grid.SetRow(chkStartupAlert, 5);
            prefGrid.Children.Add(chkStartupAlert);

            // Language Row
            var langGrid = new Grid { Margin = new Thickness(0, 0, 0, 15) };
            langGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            langGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });

            lblLang = new TextBlock
            {
                Text = LocalizationService.T("SettingLangLabel", "Application Language:"),
                FontSize = 13,
                FontWeight = FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            Grid.SetColumn(lblLang, 0);
            langGrid.Children.Add(lblLang);

            cbLang = new ComboBox
            {
                Height = 32,
                FontSize = 12.5,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            cbLang.Items.Add("English (US)");
            cbLang.Items.Add("العربية (Arabic)");
            cbLang.SelectedIndex = LocalizationService.IsArabic ? 1 : 0;
            cbLang.SelectionChanged += (s, e) =>
            {
                var targetLang = cbLang.SelectedIndex == 1 ? AppLanguage.Arabic : AppLanguage.English;
                if (targetLang != LocalizationService.CurrentLanguage)
                {
                    LocalizationService.SetLanguage(targetLang);
                }
            };
            Grid.SetColumn(cbLang, 1);
            langGrid.Children.Add(cbLang);

            Grid.SetRow(langGrid, 6);
            prefGrid.Children.Add(langGrid);

            // Elevate & Config File Path
            var bottomPrefGrid = new Grid();
            bottomPrefGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bottomPrefGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomPrefGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            btnElevate = new Button
            {
                Height = 34,
                Padding = new Thickness(14, 0, 14, 0),
                FontSize = 12.5,
                FontWeight = FontWeights.Normal,
                IsEnabled = !isAdmin
            };
            ApplySecondaryButtonStyle(btnElevate);
            btnElevate.Click += (s, e) => RestartElevated();
            Grid.SetColumn(btnElevate, 0);
            bottomPrefGrid.Children.Add(btnElevate);

            lblSettingsFile = new TextBlock
            {
                Text = LocalizationService.IsArabic ? "ملف الإعدادات: \u200E%AppData%\\CleanRamBooster\\settings.json" : "Config: %AppData%\\CleanRamBooster\\settings.json",
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(lblSettingsFile, 2);
            bottomPrefGrid.Children.Add(lblSettingsFile);

            Grid.SetRow(bottomPrefGrid, 7);
            prefGrid.Children.Add(bottomPrefGrid);

            cardPref.Child = prefGrid;
            cardsStack.Children.Add(cardPref);

            // -------------------------------------------------------------
            // Card 2: Quick Hardware Overview
            // -------------------------------------------------------------
            cardHw = CreateCardBorder();
            cardHw.Margin = new Thickness(0, 0, 0, 16);
            var hwGrid = new Grid { Margin = new Thickness(18, 16, 18, 16) };
            hwGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Title
            hwGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Specs list
            hwGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Action buttons

            lblHwTitle = new TextBlock
            {
                Text = UITheme.StripLeadingIcon(LocalizationService.T("SettingHardwareTitle", "🖥️ Quick Hardware Overview")),
                FontSize = 15,
                FontWeight = FontWeights.Medium,
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid.SetRow(lblHwTitle, 0);
            hwGrid.Children.Add(lblHwTitle);

            var specsStack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            lblCpu = CreateSpecLine("CPU:", "Loading...");
            lblGpu = CreateSpecLine("GPU (Dedicated):", "Loading...");
            lblIgpu = CreateSpecLine("iGPU (Integrated):", "Loading...");
            lblVram = CreateSpecLine("VRAM:", "Loading...");
            lblRam = CreateSpecLine("RAM:", "Loading...");
            lblOs = CreateSpecLine("OS:", "Loading...");
            lblUptime = CreateSpecLine("Uptime:", "Loading...");

            specsStack.Children.Add(lblCpu);
            specsStack.Children.Add(lblGpu);
            specsStack.Children.Add(lblIgpu);
            specsStack.Children.Add(lblVram);
            specsStack.Children.Add(lblRam);
            specsStack.Children.Add(lblOs);
            specsStack.Children.Add(lblUptime);

            Grid.SetRow(specsStack, 1);
            hwGrid.Children.Add(specsStack);

            // Action Buttons (WrapPanel so buttons never clip on narrower widths or Arabic RTL)
            var hwActions = new WrapPanel { Orientation = Orientation.Horizontal };
            btnViewDetails = new Button
            {
                Height = 34,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 10, 8),
                FontSize = 12.5,
                FontWeight = FontWeights.Normal
            };
            ApplySecondaryButtonStyle(btnViewDetails);
            btnViewDetails.Click += (s, e) =>
            {
                var rep = cachedReport ?? hwService.GetDetailedReport();
                var sysWin = new Toolbox.ToolboxSystemInfoWindow(rep);
                sysWin.Show();
                sysWin.Activate();
            };

            btnExportHtml = new Button
            {
                Height = 34,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 10, 8),
                FontSize = 12.5,
                FontWeight = FontWeights.Normal
            };
            ApplySecondaryButtonStyle(btnExportHtml);
            btnExportHtml.Click += (s, e) => ExportHtmlReport();

            btnOpenLogs = new Button
            {
                Height = 34,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 10, 8),
                FontSize = 12.5,
                FontWeight = FontWeights.Normal
            };
            ApplySecondaryButtonStyle(btnOpenLogs);
            btnOpenLogs.Click += (s, e) => DiagnosticLogger.OpenLogFolder();

            btnExportDiagnostics = new Button
            {
                Height = 34,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 10, 8),
                FontSize = 12.5,
                FontWeight = FontWeights.Normal
            };
            ApplySecondaryButtonStyle(btnExportDiagnostics);
            btnExportDiagnostics.Click += (s, e) => ExportDiagnosticsBundle();

            btnRestartApp = new Button
            {
                Height = 34,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 0, 8),
                FontSize = 12.5,
                FontWeight = FontWeights.Normal
            };
            ApplySecondaryButtonStyle(btnRestartApp);
            btnRestartApp.Click += (s, e) => RestartApp();

            hwActions.Children.Add(btnViewDetails);
            hwActions.Children.Add(btnExportHtml);
            hwActions.Children.Add(btnOpenLogs);
            hwActions.Children.Add(btnExportDiagnostics);
            hwActions.Children.Add(btnRestartApp);
            Grid.SetRow(hwActions, 2);
            hwGrid.Children.Add(hwActions);

            cardHw.Child = hwGrid;
            cardsStack.Children.Add(cardHw);

            scroll.Content = cardsStack;
            Grid.SetRow(scroll, 1);
            rootGrid.Children.Add(scroll);

            Content = rootGrid;

            ApplyLanguage();
            RefreshTheme();

            vramLiveTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            vramLiveTimer.Tick += async (s, e) => await RefreshLiveVramAsync();

            Loaded += async (s, e) =>
            {
                SyncBootCheckboxesFromSystemAsync();
                if (cachedReport == null) await LoadHardwareAsync();
                else _ = RefreshLiveVramAsync();
                vramLiveTimer.Start();
            };
            Unloaded += (s, e) => vramLiveTimer.Stop();
        }

        private TextBlock CreateSpecLine(string label, string value)
        {
            return new TextBlock
            {
                Text = $"{label} {value}",
                FontSize = 12.5,
                FontWeight = FontWeights.Normal,
                Margin = new Thickness(0, 0, 0, 7)
            };
        }

        private async Task LoadHardwareAsync()
        {
            cachedReport = await Task.Run(() => hwService.GetDetailedReport());
            UpdateHardwareLabels();
        }

        private async Task RefreshLiveVramAsync()
        {
            if (isRefreshingVram || cachedReport == null) return;
            isRefreshingVram = true;
            try
            {
                var adapters = await Task.Run(() => HardwareInfoService.GetAllLiveGpuAdapters());
                if (cachedReport != null && adapters.Count > 0)
                {
                    cachedReport.GpuAdapters = adapters;
                    var primary = cachedReport.DedicatedGpu ?? cachedReport.IntegratedGpu ?? adapters[0];
                    cachedReport.GpuVramTotalBytes = primary.DedicatedTotalBytes;
                    cachedReport.GpuVramUsedBytes = primary.DedicatedUsedBytes;
                    cachedReport.GpuSharedTotalBytes = primary.SharedTotalBytes;
                    cachedReport.GpuSharedUsedBytes = primary.SharedUsedBytes;
                    UpdateHardwareLabels();
                }
            }
            catch { }
            finally
            {
                isRefreshingVram = false;
            }
        }

        private static string FormatAdapterVramInline(HardwareInfoService.GpuAdapterSnapshot adp, bool isAr)
        {
            if (adp.DedicatedTotalBytes > 0)
            {
                string used = HardwareInfoService.FormatVramCompact(adp.DedicatedUsedBytes);
                string total = HardwareInfoService.FormatVramCompact(adp.DedicatedTotalBytes);
                int pct = (int)Math.Clamp(Math.Round(adp.DedicatedUsedBytes * 100.0 / adp.DedicatedTotalBytes), 0, 100);
                return isAr
                    ? $"VRAM: \u200E{used} / {total} ({pct}%)\u200E"
                    : $"VRAM: {used} / {total} ({pct}% Used)";
            }
            if (adp.SharedTotalBytes > 0)
            {
                string used = HardwareInfoService.FormatVramCompact(adp.SharedUsedBytes);
                string total = HardwareInfoService.FormatVramCompact(adp.SharedTotalBytes);
                return isAr
                    ? $"VRAM مشتركة: \u200E{used} / {total}\u200E"
                    : $"Shared VRAM: {used} / {total}";
            }
            return string.Empty;
        }

        private void UpdateHardwareLabels()
        {
            if (cachedReport == null) return;

            bool isAr = LocalizationService.IsArabic;
            string osName = !string.IsNullOrWhiteSpace(cachedReport.OsName) ? cachedReport.OsName : Environment.OSVersion.VersionString;
            string ramText = HardwareInfoService.FormatInstalledRamGB(cachedReport.TotalMemoryGB);

            string cpuTopologyEn = cachedReport.CpuPhysicalCores > 0
                ? $"{cachedReport.CpuPhysicalCores} Cores / {cachedReport.CpuLogicalCores} Threads"
                : $"{cachedReport.CpuLogicalCores} Threads";
            string cpuTopologyAr = cachedReport.CpuPhysicalCores > 0
                ? $"{cachedReport.CpuPhysicalCores} أنوية / {cachedReport.CpuLogicalCores} مسار"
                : $"{cachedReport.CpuLogicalCores} مسار";

            var dgpu = cachedReport.DedicatedGpu;
            var igpu = cachedReport.IntegratedGpu;

            bool hasBothGpus = dgpu != null && igpu != null;

            // Clearly separate Dedicated GPU (dGPU) and Integrated GPU (iGPU)
            if (dgpu != null)
            {
                string vramTag = hasBothGpus ? FormatAdapterVramInline(dgpu, isAr) : string.Empty;
                lblGpu.Text = isAr
                    ? $"• كرت الشاشة المنفصل (GPU): \u200E{dgpu.AdapterName}\u200E" + (string.IsNullOrEmpty(vramTag) ? "" : $"  •  {vramTag}")
                    : $"• GPU (Dedicated): {dgpu.AdapterName}" + (string.IsNullOrEmpty(vramTag) ? "" : $"  •  {vramTag}");
            }
            else
            {
                lblGpu.Text = isAr
                    ? "• كرت الشاشة المنفصل (GPU): غير موجود (رسوميات مدمجة فقط)"
                    : "• GPU (Dedicated): None (Integrated Graphics Only)";
            }

            if (igpu != null)
            {
                string igpuVramTag = hasBothGpus ? FormatAdapterVramInline(igpu, isAr) : string.Empty;
                lblIgpu.Text = isAr
                    ? $"• كرت الشاشة المدمج (iGPU): \u200E{igpu.AdapterName}\u200E" + (string.IsNullOrEmpty(igpuVramTag) ? "" : $"  •  {igpuVramTag}")
                    : $"• iGPU (Integrated): {igpu.AdapterName}" + (string.IsNullOrEmpty(igpuVramTag) ? "" : $"  •  {igpuVramTag}");
            }
            else
            {
                lblIgpu.Text = isAr
                    ? "• كرت الشاشة المدمج (iGPU): غير موجود / معطّل"
                    : "• iGPU (Integrated): None / Disabled";
            }

            // Dedicated live VRAM summary line showing Current Use / Current Existing Dedicated + Shared GPU memory
            var activeVramAdp = dgpu ?? igpu;
            if (!hasBothGpus && activeVramAdp != null && activeVramAdp.DedicatedTotalBytes > 0)
            {
                string usedStr = HardwareInfoService.FormatVramCompact(activeVramAdp.DedicatedUsedBytes);
                string totalStr = HardwareInfoService.FormatVramCompact(activeVramAdp.DedicatedTotalBytes);
                int pct = (int)Math.Clamp(Math.Round(activeVramAdp.DedicatedUsedBytes * 100.0 / activeVramAdp.DedicatedTotalBytes), 0, 100);
                string sharedPartEn = activeVramAdp.SharedTotalBytes > 0
                    ? $"  •  Shared: {HardwareInfoService.FormatVramCompact(activeVramAdp.SharedUsedBytes)} / {HardwareInfoService.FormatVramCompact(activeVramAdp.SharedTotalBytes)}"
                    : string.Empty;
                string sharedPartAr = activeVramAdp.SharedTotalBytes > 0
                    ? $"  •  مشتركة: \u200E{HardwareInfoService.FormatVramCompact(activeVramAdp.SharedUsedBytes)} / {HardwareInfoService.FormatVramCompact(activeVramAdp.SharedTotalBytes)}\u200E"
                    : string.Empty;
                string labelPrefixEn = activeVramAdp.IsIntegrated ? "iGPU VRAM" : "GPU VRAM";
                string labelPrefixAr = activeVramAdp.IsIntegrated ? "ذاكرة الفيديو المدمجة (iGPU VRAM)" : "ذاكرة الفيديو المنفصلة (GPU VRAM)";
                lblVram.Text = isAr
                    ? $"• {labelPrefixAr}: \u200E{usedStr} / {totalStr} ({pct}%)\u200E{sharedPartAr}"
                    : $"• {labelPrefixEn}: {usedStr} / {totalStr} ({pct}% Used){sharedPartEn}";
                lblVram.Visibility = Visibility.Visible;
            }
            else if (cachedReport.GpuVramTotalBytes > 0)
            {
                string usedStr = HardwareInfoService.FormatVramCompact(cachedReport.GpuVramUsedBytes);
                string totalStr = HardwareInfoService.FormatVramCompact(cachedReport.GpuVramTotalBytes);
                int pct = (int)Math.Clamp(Math.Round(cachedReport.GpuVramUsedBytes * 100.0 / cachedReport.GpuVramTotalBytes), 0, 100);
                lblVram.Text = isAr
                    ? $"• ذاكرة الفيديو (VRAM): \u200E{usedStr} / {totalStr} ({pct}%)\u200E"
                    : $"• VRAM: {usedStr} / {totalStr} ({pct}% Used)";
                lblVram.Visibility = Visibility.Visible;
            }
            else
            {
                lblVram.Visibility = Visibility.Collapsed;
            }

            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            if (isAr)
            {
                lblCpu.Text = $"• المعالج: \u200E{cachedReport.CpuName}\u200E \u200F({cpuTopologyAr})";
                lblRam.Text = $"• الذاكرة المثبتة: \u200E{ramText}\u200E";
                lblOs.Text = $"• نظام التشغيل: \u200E{osName}\u200E";
                lblUptime.Text = $"• مدة التشغيل: \u200E{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m\u200E";
            }
            else
            {
                lblCpu.Text = $"• CPU: {cachedReport.CpuName} ({cpuTopologyEn})";
                lblRam.Text = $"• RAM: {ramText} Physical Memory Installed";
                lblOs.Text = $"• OS: {osName}";
                lblUptime.Text = $"• Uptime: {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m";
            }
        }

        private void ExportHtmlReport()
        {
            if (cachedReport == null) return;

            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "HTML Report (*.html)|*.html",
                FileName = $"PCBoost_HardwareReport_{DateTime.Now:yyyyMMdd_HHmmss}.html"
            };

            if (sfd.ShowDialog() == true)
            {
                HardwareInfoService.ExportToHtml(cachedReport, sfd.FileName);
                if (AppDialog.Confirm(this,
                    LocalizationService.IsArabic ? "تم حفظ التقرير بنجاح! هل ترغب بفتحه الآن؟" : "Report exported successfully! Would you like to view it now?",
                    LocalizationService.T("AppTitle", "PC Boost"), AppDialogTone.Success))
                {
                    Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
                }
            }
        }

        private static void RestartApp()
        {
            RestartWithOptions(elevate: true);
        }

        private static void RestartElevated()
        {
            RestartWithOptions(elevate: true);
        }

        private static void RestartWithOptions(bool elevate)
        {
            string exe = Environment.ProcessPath ?? string.Empty;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return;

            try
            {
                var psi = new ProcessStartInfo(exe)
                {
                    Arguments = $"--replace-pid {Environment.ProcessId}",
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory,
                    UseShellExecute = true
                };
                if (elevate)
                {
                    psi.Verb = "runas";
                }

                var proc = Process.Start(psi);
                if (proc == null) return;

                if (Application.Current?.MainWindow is WpfMainWindow mainWin)
                {
                    mainWin.RequestExplicitExit();
                }
                else
                {
                    Program.ReleaseAppMutex();
                    Application.Current?.Shutdown();
                }
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // User cancelled UAC prompt -> keep current instance running cleanly.
            }
            catch (Exception ex)
            {
                AppDialog.Show(ex.Message, LocalizationService.T("AppTitle", "PC Boost"), AppDialogTone.Warning);
            }
        }

        private void OnBootCheckboxesClicked(bool toggledAdminBox)
        {
            var cur = SettingsService.Instance.Current;
            bool prevStart = cur.StartWithWindows;
            bool prevAdmin = cur.RunAsAdminOnBoot;

            bool wantStart = chkStartWithWindows.IsChecked == true;
            bool wantAdmin = chkRunAsAdminOnBoot.IsChecked == true;

            if (toggledAdminBox && wantAdmin && !wantStart)
            {
                wantStart = true;
                chkStartWithWindows.IsChecked = true;
            }
            else if (toggledAdminBox && !wantAdmin && wantStart)
            {
                wantStart = false;
                chkStartWithWindows.IsChecked = false;
            }
            else if (!toggledAdminBox)
            {
                wantAdmin = wantStart;
                chkRunAsAdminOnBoot.IsChecked = wantStart;
            }

            bool ok = AutoStartService.ConfigureBootStartup(wantStart, wantAdmin, out bool cancelledByUser);
            if (ok)
            {
                SettingsService.Instance.Update(st =>
                {
                    st.StartWithWindows = wantStart;
                    st.RunAsAdminOnBoot = wantAdmin;
                });
            }
            else
            {
                chkStartWithWindows.IsChecked = prevStart;
                chkRunAsAdminOnBoot.IsChecked = prevAdmin;
                if (!cancelledByUser)
                {
                    AppDialog.Show(this,
                        LocalizationService.IsArabic
                            ? "تعذر تحديث إعدادات بدء التشغيل كمسؤول. يرجى المحاولة بصلاحيات المسؤول."
                            : "Could not update Windows Administrator startup settings. Please try again with Administrator permissions.",
                        LocalizationService.T("AppTitle", "PC Boost"),
                        AppDialogTone.Warning);
                }
            }
        }

        private void SyncBootCheckboxesFromSystemAsync()
        {
            _ = Task.Run(() =>
            {
                bool hasAdminTask = AutoStartService.IsAdminTaskRegistered();
                if (hasAdminTask)
                {
                    AutoStartService.EnsureBootTaskHasTrayIfRegistered();
                }
                bool hasRunKey = AutoStartService.IsRegistryRunPresent();
                if (hasRunKey && !hasAdminTask && AutoStartService.IsCurrentProcessAdmin())
                {
                    AutoStartService.ConfigureBootStartup(true, true, out _);
                    hasAdminTask = AutoStartService.IsAdminTaskRegistered();
                }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (hasAdminTask)
                    {
                        chkStartWithWindows.IsChecked = true;
                        chkRunAsAdminOnBoot.IsChecked = true;
                        SettingsService.Instance.Current.StartWithWindows = true;
                        SettingsService.Instance.Current.RunAsAdminOnBoot = true;
                    }
                    else if (!hasAdminTask && (SettingsService.Instance.Current.StartWithWindows || SettingsService.Instance.Current.RunAsAdminOnBoot))
                    {
                        chkStartWithWindows.IsChecked = false;
                        chkRunAsAdminOnBoot.IsChecked = false;
                        SettingsService.Instance.Current.StartWithWindows = false;
                        SettingsService.Instance.Current.RunAsAdminOnBoot = false;
                    }
                }));
            });
        }

        private static bool CheckIfAdmin()
        {
            try
            {
                using var id = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(id);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private static Border CreateCardBorder()
        {
            return new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1)
            };
        }

        private static void ApplySecondaryButtonStyle(Button btn)
        {
            bool isDark = WpfTheme.IsDarkMode;
            btn.Background = new SolidColorBrush(isDark ? Color.FromRgb(33, 38, 45) : Color.FromRgb(241, 245, 249));
            btn.Foreground = new SolidColorBrush(isDark ? Color.FromRgb(201, 209, 217) : Color.FromRgb(30, 41, 59));
            btn.BorderBrush = new SolidColorBrush(isDark ? Color.FromRgb(48, 54, 61) : Color.FromRgb(226, 232, 240));
            btn.BorderThickness = new Thickness(1);
        }

        private void ExportDiagnosticsBundle()
        {
            bool isAr = LocalizationService.IsArabic;
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Diagnostics Log (*.txt)|*.txt",
                FileName = $"PCBoost_Diagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };
            if (sfd.ShowDialog() != true) return;

            try
            {
                string content = DiagnosticLogger.ExportSupportDiagnosticsText();
                File.WriteAllText(sfd.FileName, content, System.Text.Encoding.UTF8);
                if (AppDialog.Confirm(this,
                    isAr ? "تم تصدير ملف التشخيص بنجاح! هل ترغب بفتحه الآن؟" : "Diagnostics bundle exported successfully! Would you like to open it now?",
                    LocalizationService.T("AppTitle", "PC Boost"),
                    AppDialogTone.Success))
                {
                    Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                DiagnosticLogger.LogError(nameof(SettingsView), "Failed to export diagnostics bundle", ex);
                AppDialog.Show(this, ex.Message, LocalizationService.T("AppTitle", "PC Boost"), AppDialogTone.Error);
            }
        }

        private void UpdateActionButtonContents()
        {
            string elevateText = isAdmin
                ? UITheme.StripLeadingIcon(LocalizationService.IsArabic ? "🛡️ يعمل بصلاحيات المسؤول" : "🛡️ Running as Administrator")
                : UITheme.StripLeadingIcon(LocalizationService.IsArabic ? "⚡ التشغيل كمسؤول (صلاحيات كاملة)" : "⚡ Elevate to Administrator");
            btnElevate.Content = WpfIcons.CreateIconTextContent(isAdmin ? "Shield" : "Lightning", elevateText, btnElevate.Foreground, 15, 7);
            btnViewDetails.Content = WpfIcons.CreateIconTextContent("SearchFiles", UITheme.StripLeadingIcon(LocalizationService.T("BtnViewDetails", "🔍 View Detailed Specs")), btnViewDetails.Foreground, 15, 7);
            btnExportHtml.Content = WpfIcons.CreateIconTextContent("ExportDoc", UITheme.StripLeadingIcon(LocalizationService.T("BtnExportHtml", "📄 Export Full HTML Report")), btnExportHtml.Foreground, 15, 7);
            btnOpenLogs.Content = WpfIcons.CreateIconTextContent("Folder", LocalizationService.T("BtnOpenLogs", "Open Logs Folder"), btnOpenLogs.Foreground, 15, 7);
            btnExportDiagnostics.Content = WpfIcons.CreateIconTextContent("Activity", LocalizationService.T("BtnExportDiagnostics", "Export Diagnostics"), btnExportDiagnostics.Foreground, 15, 7);
            btnRestartApp.Content = WpfIcons.CreateIconTextContent("Refresh", UITheme.StripLeadingIcon(LocalizationService.T("BtnRestartApp", "🔄 Restart PC Boost")), btnRestartApp.Foreground, 15, 7);
        }

        public void ApplyLanguage()
        {
            lblHead.Text = UITheme.StripLeadingIcon(LocalizationService.T("SettingsHeader", "⚙️ Application Settings & System Specs"));
            lblSub.Text = LocalizationService.T("SettingsSub", "Configure PC Boost behavior, background automation, and system integration.");

            lblPrefTitle.Text = LocalizationService.IsArabic ? "إعدادات التطبيق وتفضيلات النظام" : "Application Settings & Persistence";
            chkStartWithWindows.Content = LocalizationService.T("SettingStartWindows", "Start automatically with Windows boot (in System Tray)");
            chkRunAsAdminOnBoot.Content = LocalizationService.T("SettingRunAsAdminOnBoot", "Run as Administrator on next reboot (no UAC prompt)");
            chkMinTray.Content = LocalizationService.T("SettingMinimizeTray", "Minimize to System Tray on close");
            chkDisableRamNotif.Content = LocalizationService.T("SettingDisableRamNotif", "Disable notifications when RAM is cleaned");
            chkStartupAlert.Content = LocalizationService.T("SettingStartupAlert", "Alert when a new desktop app or Microsoft Store app is added to startup");
            lblLang.Text = LocalizationService.T("SettingLangLabel", "Application Language:");
            cbLang.SelectedIndex = LocalizationService.IsArabic ? 1 : 0;

            lblHwTitle.Text = UITheme.StripLeadingIcon(LocalizationService.T("SettingHardwareTitle", "🖥️ Quick Hardware Overview"));
            lblSettingsFile.Text = LocalizationService.IsArabic
                ? "ملف الإعدادات: \u200E%AppData%\\CleanRamBooster\\settings.json"
                : "Config: %AppData%\\CleanRamBooster\\settings.json";

            UpdateActionButtonContents();
            UpdateHardwareLabels();
        }

        public void RefreshTheme()
        {
            lblHead.Foreground = WpfTheme.TextPrimaryBrush;
            lblSub.Foreground = WpfTheme.TextSecondaryBrush;
            lblPrefTitle.Foreground = WpfTheme.TextPrimaryBrush;
            lblLang.Foreground = WpfTheme.TextPrimaryBrush;
            lblSettingsFile.Foreground = WpfTheme.TextSecondaryBrush;

            chkStartWithWindows.Foreground = WpfTheme.TextPrimaryBrush;
            chkRunAsAdminOnBoot.Foreground = WpfTheme.TextPrimaryBrush;
            chkMinTray.Foreground = WpfTheme.TextPrimaryBrush;
            chkDisableRamNotif.Foreground = WpfTheme.TextPrimaryBrush;
            chkStartupAlert.Foreground = WpfTheme.TextPrimaryBrush;

            cardPref.Background = WpfTheme.CardBrush;
            cardPref.BorderBrush = WpfTheme.BorderBrush;
            cardHw.Background = WpfTheme.CardBrush;
            cardHw.BorderBrush = WpfTheme.BorderBrush;

            lblHwTitle.Foreground = WpfTheme.TextPrimaryBrush;
            lblCpu.Foreground = WpfTheme.TextPrimaryBrush;
            lblGpu.Foreground = WpfTheme.TextPrimaryBrush;
            lblIgpu.Foreground = WpfTheme.TextPrimaryBrush;
            lblVram.Foreground = WpfTheme.TextPrimaryBrush;
            lblRam.Foreground = WpfTheme.TextPrimaryBrush;
            lblOs.Foreground = WpfTheme.TextPrimaryBrush;
            lblUptime.Foreground = WpfTheme.TextPrimaryBrush;

            ApplySecondaryButtonStyle(btnElevate);
            ApplySecondaryButtonStyle(btnViewDetails);
            ApplySecondaryButtonStyle(btnExportHtml);
            ApplySecondaryButtonStyle(btnOpenLogs);
            ApplySecondaryButtonStyle(btnExportDiagnostics);
            ApplySecondaryButtonStyle(btnRestartApp);
            UpdateActionButtonContents();
        }
    }
}
