using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CleanRamBoosterApp.Models;
using CleanRamBoosterApp.Services;
using CleanRamBoosterApp.UI;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using MessageBox = System.Windows.MessageBox;
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using Orientation = System.Windows.Controls.Orientation;
using Panel = System.Windows.Controls.Panel;
using ProgressBar = System.Windows.Controls.ProgressBar;
using ToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;

namespace CleanRamBoosterApp.WpfUI
{
    public class WpfMainWindow : Window
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        // Core Services
        private readonly MemoryOptimizationService memService = new();
        private readonly TurboBoostService turboService = new();
        private readonly CareService careService = new();
        private readonly StartupService startupService = new();
        private readonly StartupMonitorService startupMonitor;
        private readonly bool enableStartupMonitor;
        private readonly Queue<StartupItem> pendingAlertQueue = new();
        private StartupAlertWindow? activeAlertWindow;

        // Sub-Views
        private readonly DashboardView viewDashboard;
        private readonly TurboView viewTurbo;
        private readonly CareView viewCare;
        private readonly StartupView viewStartup;
        private readonly ToolboxView viewToolbox;
        private readonly SettingsView viewSettings;

        // UI Components
        private readonly Border sidebarBorder;
        private readonly ContentControl viewHost;
        private readonly Button[] navButtons = new Button[6];
        private readonly Border[] navIndicators = new Border[6];
        private static readonly string[] NavIconKeys = { "Lightning", "Rocket", "Shield", "Startup", "Toolbox", "Settings" };
        private readonly TextBlock[] navLabels = new TextBlock[6];
        private int activeNavIndex = 0;
        private int cachedUnknownCount = 0;

        private readonly Border brandBadge;
        private readonly TextBlock lblAppTitle;
        private readonly Border badgeAdmin;
        private readonly StackPanel adminStack;
        private readonly TextBlock lblAdmin;
        private readonly TextBlock lblMiniRamTitle;
        private readonly ProgressBar pbMiniRam;
        private readonly TextBlock lblMiniRamPercent;
        private readonly Button btnThemeToggle;
        private readonly Button btnLangSwitch;

        // Tray Icon
        private NotifyIcon trayIcon = null!;
        private ContextMenuStrip trayMenu = null!;
        private IntPtr currentHIcon = IntPtr.Zero;
        private System.Drawing.Icon? currentTrayIcon;
        private int lastTrayPercent = -1;

        // Monitoring Timer (1s when visible, 3s when hidden in tray)
        private static readonly TimeSpan ActiveMonitorInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan TrayMonitorInterval = TimeSpan.FromSeconds(3);
        private readonly DispatcherTimer timer;
        private DateTime lastAutoCleanTime = DateTime.MinValue;
        private bool isExplicitExit = false;
        private bool isManualBoostRunning = false;
        private bool isAutoCleanRunning = false;
        private readonly bool startInTray;
        private bool isWndProcHooked = false;
        private bool backgroundWorkStarted = false;

        public WpfMainWindow(bool enableStartupMonitor = true, bool startInTray = false)
        {
            this.enableStartupMonitor = enableStartupMonitor;
            this.startInTray = startInTray;
            Title = "PC Boost";
            Width = 1120;
            Height = 730;
            MinWidth = 940;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            System.Windows.Media.TextOptions.SetTextRenderingMode(this, System.Windows.Media.TextRenderingMode.ClearType);
            System.Windows.Media.TextOptions.SetTextFormattingMode(this, System.Windows.Media.TextFormattingMode.Ideal);
            FontFamily = LocalizationService.ActiveWpfFontFamily;
            FontSize = 13;
            FontWeight = FontWeights.Normal;

            WpfTheme.ApplyWindowResources(Resources);

            // Load Views
            viewDashboard = new DashboardView();
            viewTurbo = new TurboView(turboService);
            viewCare = new CareView(careService);
            viewStartup = new StartupView(startupService);
            viewToolbox = new ToolboxView();
            viewSettings = new SettingsView();

            startupMonitor = new StartupMonitorService(startupService, Dispatcher);
            viewStartup.AttachMonitor(startupMonitor);
            viewStartup.ItemStateModified += item => startupMonitor.MarkKnownState(item);
            viewStartup.ItemRemoved += item => startupMonitor.MarkRemoved(item);
            viewStartup.InventoryUpdated += UpdateQuickAndSidebarStates;
            viewStartup.StatusMessage += msg => viewDashboard.AppendLog(msg);
            viewCare.StatusMessage += msg => viewDashboard.AppendLog(msg);
            viewTurbo.TurboStateChanged += UpdateQuickAndSidebarStates;
            viewTurbo.TurboStatusMessage += msg => viewDashboard.AppendLog(msg);
            startupMonitor.MonitorStateChanged += UpdateQuickAndSidebarStates;
            startupMonitor.NewStartupItemDetected += OnNewStartupItemDetected;

            viewDashboard.BoostClicked += OnBoostClicked;
            viewDashboard.TurboToggleClicked += () => viewTurbo.TriggerToggleFromDashboard();
            viewDashboard.TurboModeSelected += gameMode => viewTurbo.SetTurboMode(gameMode);
            viewDashboard.OpenTurboRequested += () => SwitchNav(1);
            viewDashboard.StartupOptimizeClicked += () =>
            {
                SwitchNav(3);
                viewStartup.SelectFilterTab("Quick");
            };

            // Root Grid (2 Columns: Sidebar + Host)
            var rootGrid = new Grid();
            rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(242) }); // Sidebar
            rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // View Host

            // 1. Sidebar Container
            sidebarBorder = new Border
            {
                BorderThickness = new Thickness(0, 0, 1, 0)
            };
            var sidebarGrid = new Grid { Margin = new Thickness(14, 18, 14, 16) };
            sidebarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Brand
            sidebarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Nav Buttons
            sidebarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Spacer
            sidebarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer info

            // Brand (Upscaled crisp logo asset + sleek SemiBold title)
            var brandStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 22)
            };
            brandBadge = new Border
            {
                Width = 42,
                Height = 42,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(2),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            try
            {
                if (UITheme.AppIconBitmap != null)
                {
                    using var ms = new MemoryStream();
                    UITheme.AppIconBitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Seek(0, SeekOrigin.Begin);
                    var bmpImg = new BitmapImage();
                    bmpImg.BeginInit();
                    bmpImg.DecodePixelWidth = 160;
                    bmpImg.StreamSource = ms;
                    bmpImg.CacheOption = BitmapCacheOption.OnLoad;
                    bmpImg.EndInit();
                    bmpImg.Freeze();
                    var img = new System.Windows.Controls.Image
                    {
                        Source = bmpImg,
                        Width = 38,
                        Height = 38
                    };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                    brandBadge.Child = img;
                }
            }
            catch { }
            brandStack.Children.Add(brandBadge);

            lblAppTitle = new TextBlock
            {
                Text = LocalizationService.T("AppTitle", "PC Boost"),
                FontSize = 19,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            brandStack.Children.Add(lblAppTitle);
            Grid.SetRow(brandStack, 0);
            sidebarGrid.Children.Add(brandStack);

            // Nav Buttons Stack
            var navStack = new StackPanel();
            string[] navKeys = { "NavRam", "NavTurbo", "NavCare", "NavStartup", "NavToolbox", "NavSettings" };
            string[] defaultTitles = {
                "RAM & Speed",
                "Turbo Boost",
                "Care & Cleanup",
                "Startup Manager",
                "Toolbox",
                "Settings & Info"
            };

            for (int i = 0; i < 6; i++)
            {
                int index = i;
                var navBtnGrid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                navBtnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) }); // Glowing bar
                navBtnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Content

                var indicator = new Border
                {
                    Background = WpfTheme.PrimaryBrush,
                    CornerRadius = new CornerRadius(1.5),
                    Visibility = (i == 0) ? Visibility.Visible : Visibility.Hidden
                };
                Grid.SetColumn(indicator, 0);
                navBtnGrid.Children.Add(indicator);
                navIndicators[i] = indicator;

                string navText = UITheme.StripLeadingIcon(LocalizationService.T(navKeys[i], defaultTitles[i]));
                navLabels[i] = new TextBlock
                {
                    Text = navText,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var btn = new Button
                {
                    Height = 42,
                    FontSize = 13.5,
                    FontWeight = FontWeights.Normal,
                    HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
                    Padding = new Thickness(12, 0, 10, 0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    BorderThickness = new Thickness(0)
                };
                System.Windows.Automation.AutomationProperties.SetName(btn, navText);
                btn.Click += (s, e) => SwitchNav(index);
                Grid.SetColumn(btn, 1);
                navBtnGrid.Children.Add(btn);
                navButtons[i] = btn;

                navStack.Children.Add(navBtnGrid);
            }
            Grid.SetRow(navStack, 1);
            sidebarGrid.Children.Add(navStack);

            // Footer
            var footerStack = new StackPanel();

            // Admin Badge
            bool isAdmin = CheckIfAdmin();
            badgeAdmin = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 0, 0, 12),
                Background = isAdmin
                    ? new SolidColorBrush(Color.FromArgb(35, 16, 185, 129))
                    : new SolidColorBrush(Color.FromArgb(35, 245, 158, 11))
            };
            adminStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            lblAdmin = new TextBlock
            {
                Text = UITheme.StripLeadingIcon(isAdmin ? LocalizationService.T("AdminActive", "Administrator Active") : LocalizationService.T("StandardUser", "Standard User (Limited)")),
                FontSize = 11.5,
                FontWeight = FontWeights.Normal,
                Foreground = isAdmin ? WpfTheme.SuccessBrush : new SolidColorBrush(WpfTheme.ColorWarning),
                VerticalAlignment = VerticalAlignment.Center
            };
            badgeAdmin.Child = adminStack;
            if (!isAdmin)
            {
                badgeAdmin.Cursor = System.Windows.Input.Cursors.Hand;
                badgeAdmin.ToolTip = LocalizationService.IsArabic
                    ? "انقر لإعادة التشغيل بصلاحيات المسؤول"
                    : "Click to restart PC Boost as Administrator";
                badgeAdmin.MouseLeftButtonUp += (s, e) =>
                {
                    AdminPrivilegeService.EnsureAdminOrPrompt(this, "PC Boost", "PC Boost");
                };
            }
            footerStack.Children.Add(badgeAdmin);

            // Mini RAM Bar
            var ramHeaderGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            ramHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ramHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            lblMiniRamTitle = new TextBlock
            {
                Text = LocalizationService.T("RamUsageTitle", "RAM USAGE"),
                FontSize = 11.5,
                FontWeight = FontWeights.Normal
            };
            Grid.SetColumn(lblMiniRamTitle, 0);
            ramHeaderGrid.Children.Add(lblMiniRamTitle);

            lblMiniRamPercent = new TextBlock
            {
                Text = "0%",
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                FlowDirection = System.Windows.FlowDirection.LeftToRight,
                Foreground = WpfTheme.PrimaryBrush
            };
            Grid.SetColumn(lblMiniRamPercent, 1);
            ramHeaderGrid.Children.Add(lblMiniRamPercent);
            footerStack.Children.Add(ramHeaderGrid);

            pbMiniRam = new ProgressBar
            {
                Height = 6,
                Margin = new Thickness(0, 0, 0, 14),
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Foreground = WpfTheme.PrimaryBrush,
                Background = WpfTheme.BorderBrush,
                BorderThickness = new Thickness(0)
            };
            footerStack.Children.Add(pbMiniRam);

            // Theme Toggle Button
            btnThemeToggle = new Button
            {
                Height = 36,
                FontSize = 12.5,
                FontWeight = FontWeights.Normal,
                Margin = new Thickness(0, 0, 0, 7)
            };
            ApplySecondaryButtonStyle(btnThemeToggle);
            btnThemeToggle.Click += (s, e) =>
            {
                WpfTheme.IsDarkMode = !WpfTheme.IsDarkMode;
                RefreshAllThemes();
            };
            footerStack.Children.Add(btnThemeToggle);

            // Language Switch Button
            btnLangSwitch = new Button
            {
                Height = 36,
                FontSize = 12.5,
                FontWeight = FontWeights.Normal
            };
            ApplySecondaryButtonStyle(btnLangSwitch);
            btnLangSwitch.Click += (s, e) =>
            {
                var targetLang = LocalizationService.IsArabic ? AppLanguage.English : AppLanguage.Arabic;
                LocalizationService.SetLanguage(targetLang);
                ApplyAllLanguage();
            };
            footerStack.Children.Add(btnLangSwitch);

            Grid.SetRow(footerStack, 3);
            sidebarGrid.Children.Add(footerStack);

            sidebarBorder.Child = sidebarGrid;
            Grid.SetColumn(sidebarBorder, 0);
            rootGrid.Children.Add(sidebarBorder);

            // 2. View Host Area
            viewHost = new ContentControl { Content = viewDashboard };
            Grid.SetColumn(viewHost, 1);
            rootGrid.Children.Add(viewHost);

            Content = rootGrid;

            // System Tray Setup
            InitializeTray();

            // Monitoring Timer
            timer = new DispatcherTimer { Interval = startInTray ? TrayMonitorInterval : ActiveMonitorInterval };
            timer.Tick += (s, e) => UpdateMemoryMonitoring();
            timer.Start();

            // Window Events
            Loaded += (s, e) =>
            {
                EnsureWindowHandleAndHook();
                WpfTheme.ApplyDwmDarkMode(this, WpfTheme.IsDarkMode);
                UpdateMemoryMonitoring();
                StartBackgroundWorkOnce();
            };
            StateChanged += (s, e) =>
            {
                if (IsVisible && WindowState != WindowState.Minimized)
                {
                    if (timer.Interval != ActiveMonitorInterval) timer.Interval = ActiveMonitorInterval;
                    UpdateMemoryMonitoring();
                }
                else
                {
                    if (timer.Interval != TrayMonitorInterval) timer.Interval = TrayMonitorInterval;
                }
            };
            Closing += OnWindowClosing;
            WpfTheme.ThemeChanged += RefreshAllThemes;
            LocalizationService.OnLanguageChanged += ApplyAllLanguage;
            SettingsService.Instance.OnSettingsChanged += OnSettingsChangedHandler;

            ApplyAllLanguage();
            RefreshAllThemes();

            if (startInTray)
            {
                // Create hidden Win32 HWND + message hook so tray-only boot still receives single-instance restore messages
                EnsureWindowHandleAndHook();
                UpdateMemoryMonitoring();
                Dispatcher.BeginInvoke(new Action(StartBackgroundWorkOnce), DispatcherPriority.Background);
            }
        }

        private void EnsureWindowHandleAndHook()
        {
            if (isWndProcHooked) return;
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                IntPtr hwnd = helper.EnsureHandle();
                var source = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
                if (source != null)
                {
                    source.AddHook(WndProc);
                    isWndProcHooked = true;
                }
            }
            catch { }
        }

        private void StartBackgroundWorkOnce()
        {
            if (backgroundWorkStarted) return;
            backgroundWorkStarted = true;

            UpdateQuickAndSidebarStates();
            if (enableStartupMonitor)
            {
                _ = viewStartup.LoadItemsAsync();
                startupMonitor.Start();
            }
            _ = System.Threading.Tasks.Task.Run(() => AutoStartService.EnsureBootTaskHasTrayIfRegistered());
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (Program.RestoreWindowMsg != 0 && msg == Program.RestoreWindowMsg)
            {
                RestoreFromTray();
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void SwitchNav(int index)
        {
            activeNavIndex = index;
            for (int i = 0; i < 6; i++)
            {
                navIndicators[i].Visibility = (i == index) ? Visibility.Visible : Visibility.Hidden;
                UpdateNavButtonStyle(navButtons[i], i == index);
            }

            viewHost.Content = index switch
            {
                0 => viewDashboard,
                1 => viewTurbo,
                2 => viewCare,
                3 => viewStartup,
                4 => viewToolbox,
                5 => viewSettings,
                _ => viewDashboard
            };
        }

        private void UpdateNavButtonStyle(Button btn, bool isActive)
        {
            bool isDark = WpfTheme.IsDarkMode;
            System.Windows.Media.Brush fg;
            if (isActive)
            {
                btn.Background = isDark ? new SolidColorBrush(Color.FromRgb(30, 41, 59)) : new SolidColorBrush(Color.FromRgb(226, 232, 240));
                fg = WpfTheme.PrimaryBrush;
            }
            else
            {
                btn.Background = System.Windows.Media.Brushes.Transparent;
                fg = WpfTheme.TextSecondaryBrush;
            }
            btn.Foreground = fg;

            int idx = Array.IndexOf(navButtons, btn);
            if (idx >= 0)
            {
                string label = navLabels[idx]?.Text ?? "";
                var rowGrid = new Grid { VerticalAlignment = VerticalAlignment.Center };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var sp = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var ico = WpfIcons.CreateIcon(NavIconKeys[idx], fg, 18, 1.7);
                ico.Margin = new Thickness(0, 0, 12, 0);
                sp.Children.Add(ico);

                navLabels[idx] = new TextBlock
                {
                    Text = label,
                    FontSize = 13.5,
                    FontWeight = isActive ? FontWeights.Medium : FontWeights.Normal,
                    Foreground = fg,
                    VerticalAlignment = VerticalAlignment.Center
                };
                sp.Children.Add(navLabels[idx]);
                Grid.SetColumn(sp, 0);
                rowGrid.Children.Add(sp);

                if (idx == 3 && cachedUnknownCount > 0)
                {
                    var badge = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(217, 119, 6)),
                        CornerRadius = new CornerRadius(9),
                        Padding = new Thickness(7, 1, 7, 1),
                        Margin = new Thickness(6, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock
                        {
                            Text = cachedUnknownCount > 99 ? "99+" : cachedUnknownCount.ToString(),
                            FontSize = 11,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = System.Windows.Media.Brushes.White
                        }
                    };
                    Grid.SetColumn(badge, 1);
                    rowGrid.Children.Add(badge);
                }

                btn.Content = rowGrid;
            }
        }

        private void UpdateQuickAndSidebarStates()
        {
            var (optCount, unkCount) = viewStartup.GetSummaryCounts();
            viewDashboard.UpdateQuickStates(viewTurbo.IsTurboActive, viewTurbo.IsGameMode, optCount, unkCount, viewTurbo.LastSavedRamBytes);
            if (cachedUnknownCount != unkCount)
            {
                cachedUnknownCount = unkCount;
                UpdateNavButtonStyle(navButtons[3], activeNavIndex == 3);
            }
        }

        private async void OnBoostClicked()
        {
            if (isManualBoostRunning || isExplicitExit) return;
            isManualBoostRunning = true;
            viewDashboard.SetBoostBusy(true);
            viewDashboard.AppendLog(LocalizationService.IsArabic ? "بدء عملية تفريغ وتحسين الذاكرة الذكية..." : "Initiating intelligent RAM purge and working set trimming...");

            try
            {
                var (scanned, cleaned, accessDenied, totalFreed) = await memService.BoostAndOptimizeAsync();
                if (isExplicitExit) return;

                string freedStr = WpfTheme.FormatBytes(totalFreed);
                string logMsg = LocalizationService.IsArabic
                    ? $"اكتمل التحسين: تم فحص {scanned} عملية، تنظيف {cleaned} عملية، وتحرير {freedStr} من الذاكرة."
                    : $"Optimization Complete: Scanned {scanned} processes, trimmed {cleaned}, freed {freedStr}.";
                viewDashboard.AppendLog(logMsg);

                // Notify user if notifications not disabled
                if (!SettingsService.Instance.Current.DisableRamCleanNotifications)
                {
                    string tip = LocalizationService.IsArabic ? $"تم تحرير {freedStr} من الذاكرة بنجاح!" : $"Freed {freedStr} of RAM successfully!";
                    trayIcon.ShowBalloonTip(2000, "PC Boost", tip, ToolTipIcon.Info);
                }

                UpdateMemoryMonitoring();
            }
            catch { }
            finally
            {
                isManualBoostRunning = false;
                viewDashboard.SetBoostBusy(false);
            }
        }

        private void UpdateMemoryMonitoring()
        {
            try
            {
                var info = memService.GetMemoryInfo();
                bool isUiVisible = IsVisible && WindowState != WindowState.Minimized;
                var desiredInterval = isUiVisible ? ActiveMonitorInterval : TrayMonitorInterval;
                if (timer.Interval != desiredInterval)
                {
                    timer.Interval = desiredInterval;
                }

                // Skip hidden Dashboard / sidebar WPF updates while minimized to tray
                if (isUiVisible)
                {
                    // Dashboard elements
                    viewDashboard.Gauge.Percentage = info.LoadPercentage;
                    viewDashboard.RamBar.SetValues(info.UsedPhysBytes, info.AvailPhysBytes);

                    viewDashboard.CardUsed.Value = WpfTheme.FormatBytes((long)info.UsedPhysBytes);
                    viewDashboard.CardAvailable.Value = WpfTheme.FormatBytes((long)info.AvailPhysBytes);
                    viewDashboard.ChipTotalRam.Value = WpfTheme.FormatBytes((long)info.TotalPhysBytes);

                    int usedPct = Math.Clamp(info.LoadPercentage, 0, 100);
                    int availPct = 100 - usedPct;
                    viewDashboard.ChipInUse.Value = LocalizationService.IsArabic
                        ? $"\u200E{WpfTheme.FormatBytes((long)info.UsedPhysBytes)} ({usedPct}%)\u200E"
                        : $"{WpfTheme.FormatBytes((long)info.UsedPhysBytes)} ({usedPct}%)";
                    viewDashboard.ChipAvailable.Value = LocalizationService.IsArabic
                        ? $"\u200E{WpfTheme.FormatBytes((long)info.AvailPhysBytes)} ({availPct}%)\u200E"
                        : $"{WpfTheme.FormatBytes((long)info.AvailPhysBytes)} ({availPct}%)";

                    // Sidebar mini bar
                    pbMiniRam.Value = info.LoadPercentage;
                    lblMiniRamPercent.Text = $"{info.LoadPercentage}%";
                    if (info.LoadPercentage >= 85)
                    {
                        pbMiniRam.Foreground = WpfTheme.DangerBrush;
                        lblMiniRamPercent.Foreground = WpfTheme.DangerBrush;
                    }
                    else if (info.LoadPercentage >= 70)
                    {
                        pbMiniRam.Foreground = new SolidColorBrush(WpfTheme.ColorWarning);
                        lblMiniRamPercent.Foreground = new SolidColorBrush(WpfTheme.ColorWarning);
                    }
                    else
                    {
                        pbMiniRam.Foreground = WpfTheme.PrimaryBrush;
                        lblMiniRamPercent.Foreground = WpfTheme.PrimaryBrush;
                    }
                }

                // Tray icon update (cached: only redraws when percentage changes)
                UpdateTrayIcon(info.LoadPercentage);

                // Auto-clean evaluation (with 15-minute cooldown to protect SSD/NVMe/HDD)
                var settings = SettingsService.Instance.Current;
                int cooldownSeconds = Math.Max(settings.AutoCleanIntervalSeconds, AppSettings.DefaultAutoCleanCooldownSeconds);
                if (!isAutoCleanRunning && !isManualBoostRunning && settings.AutoCleanEnabled && info.LoadPercentage >= settings.AutoCleanThresholdPercent)
                {
                    if ((DateTime.Now - lastAutoCleanTime).TotalSeconds >= cooldownSeconds)
                    {
                        lastAutoCleanTime = DateTime.Now;
                        _ = RunAutoCleanAsync(settings.AutoCleanThresholdPercent, settings.DisableRamCleanNotifications);
                    }
                }
            }
            catch { }
        }

        private async System.Threading.Tasks.Task RunAutoCleanAsync(int thresholdPercent, bool disableNotifications)
        {
            if (isAutoCleanRunning || isExplicitExit) return;
            isAutoCleanRunning = true;
            try
            {
                var (_, _, _, autoFreed) = await memService.BoostAndOptimizeAsync();
                if (isExplicitExit) return;

                viewDashboard.AppendLog(LocalizationService.IsArabic
                    ? $"[أتمتة خلفية] تجاوز الحمل {thresholdPercent}%: تم تحرير {WpfTheme.FormatBytes(autoFreed)} تلقائياً."
                    : $"[Auto-Clean] Load exceeded {thresholdPercent}%: Purged {WpfTheme.FormatBytes(autoFreed)} automatically.");

                if (!disableNotifications)
                {
                    trayIcon.ShowBalloonTip(2000, "PC Boost Auto-Clean",
                        LocalizationService.IsArabic ? $"تم تحرير {WpfTheme.FormatBytes(autoFreed)} من الذاكرة تلقائياً." : $"Automatically purged {WpfTheme.FormatBytes(autoFreed)}.",
                        ToolTipIcon.Info);
                }
            }
            catch { }
            finally
            {
                isAutoCleanRunning = false;
            }
        }

        private void InitializeTray()
        {
            trayMenu = new ContextMenuStrip();
            var itemOpen = new ToolStripMenuItem(LocalizationService.T("TrayOpen", "Open PC Boost"), null, (s, e) => RestoreFromTray());

            var itemBoost = new ToolStripMenuItem(UITheme.StripLeadingIcon(LocalizationService.T("TrayQuickBoost", "Quick Boost RAM")), null, (s, e) => OnBoostClicked());
            var itemStandby = new ToolStripMenuItem(UITheme.StripLeadingIcon(LocalizationService.T("TrayFlushStandby", "Flush Standby Cache")), null, async (s, e) =>
            {
                var (_, freed) = await memService.PurgeStandbyMemoryAsync();
                if (!isExplicitExit)
                {
                    viewDashboard.AppendLog(LocalizationService.IsArabic ? $"تم تفريغ الذاكرة المؤقتة: {WpfTheme.FormatBytes(freed)}" : $"Flushed standby list: {WpfTheme.FormatBytes(freed)}");
                }
            });

            var itemExit = new ToolStripMenuItem(LocalizationService.T("TrayExit", "Exit Completely"), null, (s, e) => ExitApplication());

            trayMenu.Items.Add(itemOpen);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(itemBoost);
            trayMenu.Items.Add(itemStandby);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(itemExit);

            trayIcon = new NotifyIcon
            {
                Text = "PC Boost",
                Visible = true,
                ContextMenuStrip = trayMenu,
                Icon = UITheme.AppIcon ?? SystemIcons.Application
            };
            trayIcon.DoubleClick += (s, e) => RestoreFromTray();
        }

        private void UpdateTrayIcon(int percent)
        {
            if (percent == lastTrayPercent && currentHIcon != IntPtr.Zero) return;
            try
            {
                using var bmp = new Bitmap(32, 32);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    System.Drawing.Color circleColor = percent >= 85 ? System.Drawing.Color.FromArgb(239, 68, 68) :
                                                       percent >= 70 ? System.Drawing.Color.FromArgb(245, 158, 11) :
                                                       System.Drawing.Color.FromArgb(59, 130, 246);

                    using var bBrush = new SolidBrush(System.Drawing.Color.FromArgb(20, 24, 30));
                    g.FillEllipse(bBrush, 1, 1, 30, 30);

                    using var pen = new System.Drawing.Pen(circleColor, 2.5f);
                    float sweep = (percent / 100f) * 360f;
                    g.DrawArc(pen, 2, 2, 27, 27, -90, sweep);

                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    using var font = new Font("Segoe UI Variable Text", 9f, System.Drawing.FontStyle.Regular);
                    using var fBrush = new SolidBrush(System.Drawing.Color.White);
                    string s = percent.ToString();
                    var sz = g.MeasureString(s, font);
                    g.DrawString(s, font, fBrush, (32 - sz.Width) / 2f, (32 - sz.Height) / 2f);
                }

                IntPtr newHIcon = bmp.GetHicon();
                var newIcon = System.Drawing.Icon.FromHandle(newHIcon);
                var oldIcon = currentTrayIcon;
                IntPtr oldHIcon = currentHIcon;

                trayIcon.Icon = newIcon;
                currentTrayIcon = newIcon;
                currentHIcon = newHIcon;
                lastTrayPercent = percent;
                trayIcon.Text = $"PC Boost: {percent}% RAM";

                if (oldIcon != null)
                {
                    try { oldIcon.Dispose(); } catch { }
                }
                if (oldHIcon != IntPtr.Zero)
                {
                    try { DestroyIcon(oldHIcon); } catch { }
                }
            }
            catch { }
        }

        private void RestoreFromTray()
        {
            if (!IsVisible)
            {
                Show();
            }
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            if (timer.Interval != ActiveMonitorInterval)
            {
                timer.Interval = ActiveMonitorInterval;
            }
            UpdateMemoryMonitoring();
            Activate();
        }

        private void OnSettingsChangedHandler()
        {
            if (isExplicitExit) return;
            try
            {
                if (Dispatcher.CheckAccess())
                {
                    viewDashboard.SyncSettingsControls();
                    viewSettings.SyncSettingsControls();
                }
                else
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (isExplicitExit) return;
                        viewDashboard.SyncSettingsControls();
                        viewSettings.SyncSettingsControls();
                    }));
                }
            }
            catch { }
        }

        private void OnNewStartupItemDetected(Models.StartupItem item)
        {
            viewStartup.SyncExternalItem(item);
            UpdateQuickAndSidebarStates();
            viewDashboard.AppendLog(LocalizationService.IsArabic
                ? $"[بدء التشغيل] تم اكتشاف عنصر جديد: {item.Name} ({item.LocationDisplayLocalized})"
                : $"[Startup Monitor] New startup item detected: {item.Name} ({item.LocationDisplayLocalized})");

            if (!SettingsService.Instance.Current.StartupAlertEnabled)
                return;

            if (pendingAlertQueue.Any(x => string.Equals(x.MonitorKey, item.MonitorKey, StringComparison.OrdinalIgnoreCase)))
                return;

            pendingAlertQueue.Enqueue(item);
            if (activeAlertWindow == null)
            {
                ShowNextStartupAlert();
            }
        }

        private void ShowNextStartupAlert()
        {
            if (isExplicitExit || pendingAlertQueue.Count == 0)
            {
                activeAlertWindow = null;
                return;
            }

            var nextItem = pendingAlertQueue.Dequeue();
            var alert = new StartupAlertWindow(nextItem);
            activeAlertWindow = alert;

            alert.DisableRequested += target =>
            {
                var result = startupService.SetItemEnabledDetailed(target, false);
                bool disabled = result.Success;
                if (!disabled && result.FailureReason == StartupOpFailureReason.RequiresAdministrator && !CheckIfAdmin())
                {
                    if (AppDialog.Confirm(
                        alert,
                        string.Format(LocalizationService.T("AlertElevateDisablePrompt", "Administrator permission is required to disable \"{0}\".\n\nElevate via Windows Administrator prompt (UAC) to disable it now?"), target.Name),
                        LocalizationService.T("AppTitle", "PC Boost"),
                        AppDialogTone.Question))
                    {
                        disabled = TryDisableItemElevated(target);
                    }
                    else
                    {
                        return;
                    }
                }

                if (disabled)
                {
                    target.IsEnabled = false;
                    startupMonitor.MarkKnownState(target);
                    if (alert.ShouldIgnoreOnClose)
                    {
                        startupMonitor.IgnoreItem(target);
                    }
                    viewStartup.SyncExternalItem(target);
                    UpdateQuickAndSidebarStates();
                    viewDashboard.AppendLog(LocalizationService.IsArabic
                        ? $"[بدء التشغيل] تم تعطيل: {target.Name}"
                        : $"[Startup Monitor] Disabled startup item: {target.Name}");
                    alert.Close();
                }
                else
                {
                    string failMsg = !string.IsNullOrWhiteSpace(result.Message)
                        ? (LocalizationService.IsArabic
                            ? $"تعذر تعطيل \"{target.Name}\".\n\nالسبب: {result.Message}"
                            : $"Could not disable \"{target.Name}\".\n\nReason: {result.Message}")
                        : string.Format(LocalizationService.T("AlertDisableFailed", "Could not disable \"{0}\". Please run PC Boost as Administrator for system startup entries."), target.Name);
                    AppDialog.Show(
                        alert,
                        failMsg,
                        LocalizationService.T("AppTitle", "PC Boost"),
                        AppDialogTone.Warning);
                }
            };

            alert.OpenSettingsRequested += () =>
            {
                RestoreFromTray();
                SwitchNav(5);
            };

            alert.Dismissed += target =>
            {
                if (alert.ShouldIgnoreOnClose && !alert.WasDisabled)
                {
                    startupMonitor.IgnoreItem(target);
                    viewStartup.SyncExternalItem(target);
                    UpdateQuickAndSidebarStates();
                }
                if (ReferenceEquals(activeAlertWindow, alert))
                {
                    activeAlertWindow = null;
                }
                ShowNextStartupAlert();
            };

            alert.Show();
        }

        private static bool TryDisableItemElevated(StartupItem target)
        {
            string escapedKey = target.MonitorKey.Replace("\"", "\\\"");
            return AdminPrivilegeService.TryRunElevatedSelfCommand($"--disable-startup \"{escapedKey}\"", out _, out _);
        }

        public void RequestExplicitExit()
        {
            ExitApplication();
        }

        private void ExitApplication()
        {
            isExplicitExit = true;
            try { SettingsService.Instance.OnSettingsChanged -= OnSettingsChangedHandler; } catch { }
            try { activeAlertWindow?.Close(); } catch { }
            try { viewToolbox.CloseAllOpenToolWindows(); } catch { }
            try { startupMonitor.Dispose(); } catch { }
            try
            {
                if (trayIcon != null)
                {
                    trayIcon.Visible = false;
                    trayIcon.Dispose();
                }
            }
            catch { }
            if (currentTrayIcon != null)
            {
                try { currentTrayIcon.Dispose(); } catch { }
                currentTrayIcon = null;
            }
            if (currentHIcon != IntPtr.Zero)
            {
                try { DestroyIcon(currentHIcon); } catch { }
                currentHIcon = IntPtr.Zero;
            }
            try { timer.Stop(); } catch { }
            Program.ReleaseAppMutex();
            try { Close(); } catch { }
            try { Application.Current?.Shutdown(); } catch { }
        }

        private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (isExplicitExit) return;

            if (SettingsService.Instance.Current.MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
                if (timer.Interval != TrayMonitorInterval)
                {
                    timer.Interval = TrayMonitorInterval;
                }
                TrimMemoryForBackground();
            }
            else
            {
                ExitApplication();
            }
        }

        private void TrimMemoryForBackground()
        {
            try
            {
                GC.Collect(2, GCCollectionMode.Forced, true);
                GC.WaitForPendingFinalizers();
                SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, new IntPtr(-1), new IntPtr(-1));
            }
            catch { }
        }

        private void RefreshAllThemes()
        {
            Background = WpfTheme.BgBrush;
            sidebarBorder.Background = WpfTheme.SidebarBrush;
            sidebarBorder.BorderBrush = WpfTheme.BorderBrush;
            lblAppTitle.Foreground = WpfTheme.TextPrimaryBrush;
            lblMiniRamTitle.Foreground = WpfTheme.TextSecondaryBrush;

            RefreshSidebarFooterButtons();

            for (int i = 0; i < 6; i++)
            {
                UpdateNavButtonStyle(navButtons[i], i == activeNavIndex);
            }

            WpfTheme.ApplyWindowResources(Resources);
            WpfTheme.ApplyDwmDarkMode(this, WpfTheme.IsDarkMode);

            viewDashboard.RefreshTheme();
            viewTurbo.RefreshTheme();
            viewCare.RefreshTheme();
            viewStartup.RefreshTheme();
            viewToolbox.RefreshTheme();
            viewSettings.RefreshTheme();
            UpdateQuickAndSidebarStates();
        }

        private void RefreshSidebarFooterButtons()
        {
            bool isAr = LocalizationService.IsArabic;
            string themeTxt = UITheme.StripLeadingIcon(WpfTheme.IsDarkMode ? LocalizationService.T("ThemeLight", "Light Theme") : LocalizationService.T("ThemeDark", "Dark Theme"));
            ApplySecondaryButtonStyle(btnThemeToggle);
            btnThemeToggle.Content = WpfIcons.CreateIconTextContent(WpfTheme.IsDarkMode ? "Sun" : "Moon", themeTxt, btnThemeToggle.Foreground, 16, 8);
            System.Windows.Automation.AutomationProperties.SetName(btnThemeToggle, themeTxt);

            string langTxt = isAr ? "English" : "العربية";
            ApplySecondaryButtonStyle(btnLangSwitch);
            btnLangSwitch.Content = WpfIcons.CreateIconTextContent("Globe", langTxt, btnLangSwitch.Foreground, 16, 8);
            System.Windows.Automation.AutomationProperties.SetName(btnLangSwitch, langTxt);

            bool isAdmin = CheckIfAdmin();
            System.Windows.Media.Brush adminFg = isAdmin ? WpfTheme.SuccessBrush : new SolidColorBrush(WpfTheme.ColorWarning);
            adminStack.Children.Clear();
            var adminIcon = WpfIcons.CreateIcon(isAdmin ? "Shield" : "Alert", adminFg, 15, 1.75);
            adminIcon.Margin = new Thickness(0, 0, 7, 0);
            lblAdmin.Text = UITheme.StripLeadingIcon(isAdmin ? LocalizationService.T("AdminActive", "Administrator Active") : LocalizationService.T("StandardUser", "Standard User (Limited)"));
            lblAdmin.Foreground = adminFg;
            adminStack.Children.Add(adminIcon);
            adminStack.Children.Add(lblAdmin);
        }

        private void ApplyAllLanguage()
        {
            bool isAr = LocalizationService.IsArabic;
            FlowDirection = isAr ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight;
            FontFamily = LocalizationService.ActiveWpfFontFamily;

            string[] navKeys = { "NavRam", "NavTurbo", "NavCare", "NavStartup", "NavToolbox", "NavSettings" };
            string[] defaultTitles = {
                "RAM & Speed",
                "Turbo Boost",
                "Care & Cleanup",
                "Startup Manager",
                "Toolbox",
                "Settings & Info"
            };

            for (int i = 0; i < 6; i++)
            {
                string navText = UITheme.StripLeadingIcon(LocalizationService.T(navKeys[i], defaultTitles[i]));
                navLabels[i].Text = navText;
                System.Windows.Automation.AutomationProperties.SetName(navButtons[i], navText);
                UpdateNavButtonStyle(navButtons[i], i == activeNavIndex);
            }

            lblAppTitle.Text = LocalizationService.T("AppTitle", "PC Boost");
            lblMiniRamTitle.Text = LocalizationService.T("RamUsageTitle", "RAM USAGE");
            RefreshSidebarFooterButtons();

            viewDashboard.ApplyLanguage();
            viewTurbo.ApplyLanguage();
            viewCare.ApplyLanguage();
            viewStartup.ApplyLanguage();
            viewToolbox.ApplyLanguage();
            viewSettings.ApplyLanguage();
            UpdateQuickAndSidebarStates();
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

        private static void ApplySecondaryButtonStyle(Button btn)
        {
            bool isDark = WpfTheme.IsDarkMode;
            btn.Background = new SolidColorBrush(isDark ? Color.FromRgb(33, 38, 45) : Color.FromRgb(241, 245, 249));
            btn.Foreground = new SolidColorBrush(isDark ? Color.FromRgb(201, 209, 217) : Color.FromRgb(30, 41, 59));
            btn.BorderBrush = new SolidColorBrush(isDark ? Color.FromRgb(48, 54, 61) : Color.FromRgb(226, 232, 240));
            btn.BorderThickness = new Thickness(1);
        }
    }
}
