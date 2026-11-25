using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using CleanRamBoosterApp.Services;
using CleanRamBoosterApp.UI;

namespace CleanRamBoosterApp.WpfUI
{
    public class DashboardView : UserControl
    {
        // Controls
        public CircularGaugeControl Gauge { get; }
        public MetricCardControl CardUsed { get; }
        public MetricCardControl CardAvailable { get; }

        public StatusPillControl ChipInUse { get; }
        public StatusPillControl ChipAvailable { get; }
        public StatusPillControl ChipTotalRam { get; }

        public SegmentedRamBarControl RamBar { get; }
        public Button BtnBoost { get; }

        public CheckBox ChkAutoClean { get; }
        public ComboBox CbThreshold { get; }
        public CheckBox ChkMinimizeToTray { get; }

        public TextBox TxtLog { get; }
        public Button BtnCopyLog { get; }
        public Button BtnClearLog { get; }

        private readonly TextBlock lblHead;
        private readonly TextBlock lblSub;
        private readonly Border cardTop;
        private readonly Border cardSettings;
        private readonly Border cardQuickTurbo;
        private readonly Border cardQuickStartup;
        private readonly Border cardLog;
        private readonly TextBlock lblLogTitle;
        private readonly TextBlock lblTrigger;

        // Quick state cards state & controls
        private readonly Grid turboIconBadgeHost;
        private readonly TextBlock lblTurboTitlePrefix;
        private readonly TextBlock lblTurboTitleState;
        private readonly TextBlock lblTurboDesc;
        private readonly Border turboSplitContainer;
        private readonly Button btnTurboMain;
        private readonly Border turboSplitDivider;
        private readonly Button btnTurboDrop;

        private readonly Grid startupIconBadgeHost;
        private readonly TextBlock lblStartupTitle;
        private readonly TextBlock lblStartupDesc;
        private readonly Button btnStartupOptimize;

        private bool lastTurboActive;
        private bool lastTurboGameMode = true;
        private long lastTurboSavedBytes;
        private bool isBoostBusy;
        private bool isSyncingSettings;
        private int lastStartupOptimizableCount;
        private int lastStartupUnknownCount;

        public event Action? BoostClicked;
        public event Action? TurboToggleClicked;
        public event Action<bool>? TurboModeSelected;
        public event Action? OpenTurboRequested;
        public event Action? StartupOptimizeClicked;

        public DashboardView()
        {
            var rootGrid = new Grid
            {
                Margin = new Thickness(24, 14, 24, 14)
            };
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Top Card
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Settings Card
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Quick Turbo & Startup Cards
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Log Card

            // 1. Header
            var headRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var headBadge = WpfIcons.CreateHeaderBadge("Lightning", Color.FromRgb(59, 130, 246), 42, 22);
            headBadge.Margin = new Thickness(0, 0, 14, 0);
            Grid.SetColumn(headBadge, 0);
            headRow.Children.Add(headBadge);

            var headStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            lblHead = new TextBlock
            {
                Text = UITheme.StripLeadingIcon(LocalizationService.T("DashHeader", "⚡ Memory & Performance Center")),
                FontSize = 20,
                FontWeight = FontWeights.SemiBold
            };
            lblSub = new TextBlock
            {
                Text = LocalizationService.T("DashSub", "Real-time physical working set monitoring with intelligent zero-bloat RAM purging."),
                FontSize = 13,
                Margin = new Thickness(0, 3, 0, 0)
            };
            headStack.Children.Add(lblHead);
            headStack.Children.Add(lblSub);
            Grid.SetColumn(headStack, 1);
            headRow.Children.Add(headStack);
            Grid.SetRow(headRow, 0);
            rootGrid.Children.Add(headRow);

            // 2. Top Card (Gauge + Metrics + Status Pills + Bar + Boost Button)
            cardTop = CreateCardBorder();
            cardTop.Margin = new Thickness(0, 0, 0, 10);
            var topGrid = new Grid { Margin = new Thickness(14) };
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) }); // Gauge
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Content

            var leftGaugeStack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Gauge = new CircularGaugeControl
            {
                HorizontalAlignment = HorizontalAlignment.Center
            };
            ChipTotalRam = new StatusPillControl
            {
                AccentColor = Color.FromRgb(14, 165, 233),
                Width = 176,
                Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            leftGaugeStack.Children.Add(Gauge);
            leftGaugeStack.Children.Add(ChipTotalRam);
            Grid.SetColumn(leftGaugeStack, 0);
            topGrid.Children.Add(leftGaugeStack);

            // Right side of top card
            var rightStack = new Grid();
            rightStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2 Metric Cards
            rightStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2 Status Pills
            rightStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Segmented Bar
            rightStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Boost Button

            // Metrics row (2 columns: In-Use & Available)
            var metricsGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            for (int i = 0; i < 2; i++) metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            CardUsed = new MetricCardControl { AccentColor = WpfTheme.ColorPrimary, Margin = new Thickness(0, 0, 6, 0) };
            CardAvailable = new MetricCardControl { AccentColor = WpfTheme.ColorSuccess, Margin = new Thickness(6, 0, 0, 0) };

            Grid.SetColumn(CardUsed, 0);
            Grid.SetColumn(CardAvailable, 1);
            metricsGrid.Children.Add(CardUsed);
            metricsGrid.Children.Add(CardAvailable);
            Grid.SetRow(metricsGrid, 0);
            rightStack.Children.Add(metricsGrid);

            // Status Pills Row (2 columns: In-Use & Available)
            var pillsGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            pillsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pillsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            ChipInUse = new StatusPillControl { AccentColor = WpfTheme.ColorPrimary, Margin = new Thickness(0, 0, 6, 0) };
            ChipAvailable = new StatusPillControl { AccentColor = WpfTheme.ColorSuccess, Margin = new Thickness(6, 0, 0, 0) };

            Grid.SetColumn(ChipInUse, 0);
            Grid.SetColumn(ChipAvailable, 1);
            pillsGrid.Children.Add(ChipInUse);
            pillsGrid.Children.Add(ChipAvailable);
            Grid.SetRow(pillsGrid, 1);
            rightStack.Children.Add(pillsGrid);

            // Segmented RAM Bar
            RamBar = new SegmentedRamBarControl { Margin = new Thickness(0, 0, 0, 12) };
            Grid.SetRow(RamBar, 2);
            rightStack.Children.Add(RamBar);

            // Boost Button
            BtnBoost = new Button
            {
                Height = 48,
                FontSize = 14,
                FontWeight = FontWeights.Medium,
                Foreground = Brushes.White,
                Cursor = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(0)
            };
            ApplyPrimaryButtonStyle(BtnBoost);
            BtnBoost.Click += (s, e) => BoostClicked?.Invoke();
            Grid.SetRow(BtnBoost, 3);
            rightStack.Children.Add(BtnBoost);

            Grid.SetColumn(rightStack, 1);
            topGrid.Children.Add(rightStack);
            cardTop.Child = topGrid;
            Grid.SetRow(cardTop, 1);
            rootGrid.Children.Add(cardTop);

            // 3. Auto-Clean Settings Card
            cardSettings = CreateCardBorder();
            cardSettings.Margin = new Thickness(0, 0, 0, 10);
            var settingsGrid = new Grid
            {
                Margin = new Thickness(16, 10, 16, 10),
                VerticalAlignment = VerticalAlignment.Center
            };
            settingsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            settingsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            settingsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            settingsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            settingsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            ChkAutoClean = new CheckBox
            {
                Content = LocalizationService.T("AutoCleanEnable", "Enable Background Auto-Clean"),
                FontSize = 13,
                FontWeight = FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                IsChecked = SettingsService.Instance.Current.AutoCleanEnabled,
                Margin = new Thickness(0, 0, 20, 0)
            };
            ChkAutoClean.Checked += OnAutoCleanToggled;
            ChkAutoClean.Unchecked += OnAutoCleanToggled;
            Grid.SetColumn(ChkAutoClean, 0);
            settingsGrid.Children.Add(ChkAutoClean);

            lblTrigger = new TextBlock
            {
                Text = LocalizationService.T("AutoCleanTrigger", "Trigger when RAM exceeds:"),
                FontSize = 13,
                FontWeight = FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(lblTrigger, 1);
            settingsGrid.Children.Add(lblTrigger);

            CbThreshold = new ComboBox
            {
                Width = 128,
                Height = 32,
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = SettingsService.Instance.Current.AutoCleanEnabled
            };
            CbThreshold.Items.Add("70% Load");
            CbThreshold.Items.Add("75% Load");
            CbThreshold.Items.Add("80% Load");
            CbThreshold.Items.Add("85% Load");
            CbThreshold.Items.Add("90% Load");
            int initialIdx = Math.Clamp((SettingsService.Instance.Current.AutoCleanThresholdPercent - 70) / 5, 0, 4);
            CbThreshold.SelectedIndex = initialIdx;
            CbThreshold.SelectionChanged += OnThresholdSelectionChanged;
            Grid.SetColumn(CbThreshold, 2);
            settingsGrid.Children.Add(CbThreshold);

            ChkMinimizeToTray = new CheckBox
            {
                Content = LocalizationService.T("KeepInTray", "Keep in Tray on Close"),
                FontSize = 13,
                FontWeight = FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                IsChecked = SettingsService.Instance.Current.MinimizeToTray
            };
            ChkMinimizeToTray.Checked += OnMinimizeToTrayToggled;
            ChkMinimizeToTray.Unchecked += OnMinimizeToTrayToggled;
            Grid.SetColumn(ChkMinimizeToTray, 4);
            settingsGrid.Children.Add(ChkMinimizeToTray);

            cardSettings.Child = settingsGrid;
            Grid.SetRow(cardSettings, 2);
            rootGrid.Children.Add(cardSettings);

            // 3b. Quick State Row (Turbo Boost + Startup Optimizer)
            var quickRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            quickRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            quickRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            quickRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left Card: Turbo Boost Status & Toggle
            cardQuickTurbo = CreateCardBorder();
            var turboInner = new Grid { Margin = new Thickness(16, 10, 16, 10) };
            turboInner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            turboInner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            turboInner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
            turboInner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            turboIconBadgeHost = new Grid { Width = 42, Height = 42, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
            Grid.SetRow(turboIconBadgeHost, 0);
            Grid.SetColumn(turboIconBadgeHost, 0);
            turboInner.Children.Add(turboIconBadgeHost);

            var turboTextStack = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            var turboTitlePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            lblTurboTitlePrefix = new TextBlock { FontSize = 14.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 5, 0) };
            lblTurboTitleState = new TextBlock { FontSize = 14.5, FontWeight = FontWeights.SemiBold };
            turboTitlePanel.Children.Add(lblTurboTitlePrefix);
            turboTitlePanel.Children.Add(lblTurboTitleState);
            turboTextStack.Children.Add(turboTitlePanel);

            lblTurboDesc = new TextBlock
            {
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18
            };
            turboTextStack.Children.Add(lblTurboDesc);
            Grid.SetRow(turboTextStack, 0);
            Grid.SetColumn(turboTextStack, 1);
            turboInner.Children.Add(turboTextStack);

            turboSplitContainer = new Border
            {
                CornerRadius = new CornerRadius(6),
                ClipToBounds = true,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 10, 0, 0),
                Height = 32
            };
            var turboSplitGrid = new Grid();
            turboSplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            turboSplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            turboSplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            btnTurboMain = new Button
            {
                Height = 32,
                MinWidth = 92,
                Padding = new Thickness(16, 0, 14, 0),
                FontSize = 12.5,
                FontWeight = FontWeights.Medium,
                Foreground = Brushes.White,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Template = CreateFlatButtonTemplate()
            };
            btnTurboMain.Click += (s, e) => TurboToggleClicked?.Invoke();
            Grid.SetColumn(btnTurboMain, 0);
            turboSplitGrid.Children.Add(btnTurboMain);

            turboSplitDivider = new Border
            {
                Width = 1,
                Margin = new Thickness(0, 6, 0, 6),
                Background = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255))
            };
            Grid.SetColumn(turboSplitDivider, 1);
            turboSplitGrid.Children.Add(turboSplitDivider);

            btnTurboDrop = new Button
            {
                Width = 28,
                Height = 32,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Template = CreateFlatButtonTemplate(),
                Content = WpfIcons.CreateIcon("ChevronDown", Brushes.White, 12, 2.0)
            };
            btnTurboDrop.Click += (s, e) => ShowTurboModeMenu();
            Grid.SetColumn(btnTurboDrop, 2);
            turboSplitGrid.Children.Add(btnTurboDrop);

            turboSplitContainer.Child = turboSplitGrid;
            Grid.SetRow(turboSplitContainer, 1);
            Grid.SetColumn(turboSplitContainer, 1);
            turboInner.Children.Add(turboSplitContainer);

            cardQuickTurbo.Child = turboInner;
            Grid.SetColumn(cardQuickTurbo, 0);
            quickRow.Children.Add(cardQuickTurbo);

            // Right Card: Startup Optimizer & Unknown Counter
            cardQuickStartup = CreateCardBorder();
            var startupInner = new Grid { Margin = new Thickness(16, 10, 16, 10) };
            startupInner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            startupInner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            startupInner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
            startupInner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            startupIconBadgeHost = new Grid { Width = 42, Height = 42, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
            Grid.SetRow(startupIconBadgeHost, 0);
            Grid.SetColumn(startupIconBadgeHost, 0);
            startupInner.Children.Add(startupIconBadgeHost);

            var startupTextStack = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            lblStartupTitle = new TextBlock
            {
                FontSize = 14.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            startupTextStack.Children.Add(lblStartupTitle);

            lblStartupDesc = new TextBlock
            {
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18
            };
            startupTextStack.Children.Add(lblStartupDesc);
            Grid.SetRow(startupTextStack, 0);
            Grid.SetColumn(startupTextStack, 1);
            startupInner.Children.Add(startupTextStack);

            btnStartupOptimize = new Button
            {
                Height = 32,
                MinWidth = 108,
                Padding = new Thickness(18, 0, 18, 0),
                FontSize = 12.5,
                FontWeight = FontWeights.Medium,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 10, 0, 0),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Template = CreateFlatButtonTemplate(cornerRadius: 6)
            };
            ApplyPrimaryButtonStyle(btnStartupOptimize);
            btnStartupOptimize.Click += (s, e) => StartupOptimizeClicked?.Invoke();
            Grid.SetRow(btnStartupOptimize, 1);
            Grid.SetColumn(btnStartupOptimize, 1);
            startupInner.Children.Add(btnStartupOptimize);

            cardQuickStartup.Child = startupInner;
            Grid.SetColumn(cardQuickStartup, 2);
            quickRow.Children.Add(cardQuickStartup);

            Grid.SetRow(quickRow, 3);
            rootGrid.Children.Add(quickRow);

            // 4. Live Activity Log Card
            cardLog = CreateCardBorder();
            cardLog.MinHeight = 100;
            var logGrid = new Grid { Margin = new Thickness(16, 10, 16, 10) };
            logGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            logGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var logHeaderGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            logHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            logHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            logHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            lblLogTitle = new TextBlock
            {
                Text = LocalizationService.T("LogTitle", "Live Activity & Optimization Log"),
                FontWeight = FontWeights.Medium,
                FontSize = 14.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lblLogTitle, 0);
            logHeaderGrid.Children.Add(lblLogTitle);

            BtnCopyLog = new Button
            {
                MinWidth = 94,
                Height = 31,
                Padding = new Thickness(12, 0, 12, 0),
                FontWeight = FontWeights.Normal,
                FontSize = 12.5,
                Margin = new Thickness(0, 0, 8, 0)
            };
            ApplySecondaryButtonStyle(BtnCopyLog);
            BtnCopyLog.Click += (s, e) =>
            {
                if (TxtLog != null && !string.IsNullOrEmpty(TxtLog.Text))
                {
                    Clipboard.SetText(TxtLog.Text);
                    AppDialog.Show(this,
                        LocalizationService.IsArabic ? "تم نسخ سجل النشاط للحافظة بنجاح." : "Activity log copied to clipboard.",
                        LocalizationService.IsArabic ? "تم النسخ" : "Copied",
                        AppDialogTone.Success);
                }
            };
            Grid.SetColumn(BtnCopyLog, 1);
            logHeaderGrid.Children.Add(BtnCopyLog);

            BtnClearLog = new Button
            {
                MinWidth = 94,
                Height = 31,
                Padding = new Thickness(12, 0, 12, 0),
                FontWeight = FontWeights.Normal,
                FontSize = 12.5
            };
            ApplySecondaryButtonStyle(BtnClearLog);
            BtnClearLog.Click += (s, e) => TxtLog?.Clear();
            Grid.SetColumn(BtnClearLog, 2);
            logHeaderGrid.Children.Add(BtnClearLog);

            Grid.SetRow(logHeaderGrid, 0);
            logGrid.Children.Add(logHeaderGrid);

            TxtLog = new TextBox
            {
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 12,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10)
            };
            Grid.SetRow(TxtLog, 1);
            logGrid.Children.Add(TxtLog);

            cardLog.Child = logGrid;
            Grid.SetRow(cardLog, 4);
            rootGrid.Children.Add(cardLog);

            Content = rootGrid;
            ApplyLanguage();
            RefreshTheme();
        }

        private static Border CreateCardBorder()
        {
            return new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1)
            };
        }

        private static void ApplyPrimaryButtonStyle(Button btn)
        {
            var brush = new LinearGradientBrush(
                Color.FromRgb(59, 130, 246),
                Color.FromRgb(37, 99, 235),
                new Point(0, 0),
                new Point(1, 1));
            btn.Background = brush;
        }

        private static void ApplySecondaryButtonStyle(Button btn)
        {
            bool isDark = WpfTheme.IsDarkMode;
            btn.Background = new SolidColorBrush(isDark ? Color.FromRgb(33, 38, 45) : Color.FromRgb(241, 245, 249));
            btn.Foreground = new SolidColorBrush(isDark ? Color.FromRgb(201, 209, 217) : Color.FromRgb(30, 41, 59));
            btn.BorderBrush = new SolidColorBrush(isDark ? Color.FromRgb(48, 54, 61) : Color.FromRgb(226, 232, 240));
            btn.BorderThickness = new Thickness(1);
        }

        public void AppendLog(string message)
        {
            string time = DateTime.Now.ToString("HH:mm:ss");
            TxtLog.AppendText($"[{time}] {message}\r\n");
            TxtLog.ScrollToEnd();
        }

        public void SetBoostBusy(bool busy)
        {
            isBoostBusy = busy;
            BtnBoost.IsEnabled = !busy;
            UpdateBoostButtonContent();
        }

        private void UpdateBoostButtonContent()
        {
            string label = isBoostBusy
                ? (LocalizationService.IsArabic ? "جاري تحسين الذاكرة..." : "Optimizing RAM...")
                : UITheme.StripLeadingIcon(LocalizationService.T("BtnBoostRam", "🚀 BOOST & OPTIMIZE RAM"));
            BtnBoost.Content = WpfIcons.CreateIconTextContent("Rocket", label, Brushes.White, 18, 9);
        }

        private void OnAutoCleanToggled(object sender, RoutedEventArgs e)
        {
            if (isSyncingSettings) return;
            bool enabled = ChkAutoClean.IsChecked == true;
            CbThreshold.IsEnabled = enabled;
            if (SettingsService.Instance.Current.AutoCleanEnabled != enabled)
            {
                SettingsService.Instance.Update(st => st.AutoCleanEnabled = enabled);
            }
        }

        private void OnThresholdSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isSyncingSettings || CbThreshold.SelectedIndex < 0) return;
            int threshold = Math.Clamp(70 + (CbThreshold.SelectedIndex * 5), 70, 90);
            if (SettingsService.Instance.Current.AutoCleanThresholdPercent != threshold)
            {
                SettingsService.Instance.Update(st => st.AutoCleanThresholdPercent = threshold);
            }
        }

        private void OnMinimizeToTrayToggled(object sender, RoutedEventArgs e)
        {
            if (isSyncingSettings) return;
            bool keepInTray = ChkMinimizeToTray.IsChecked == true;
            if (SettingsService.Instance.Current.MinimizeToTray != keepInTray)
            {
                SettingsService.Instance.Update(st => st.MinimizeToTray = keepInTray);
            }
        }

        public void SyncSettingsControls()
        {
            isSyncingSettings = true;
            try
            {
                var st = SettingsService.Instance.Current;
                ChkAutoClean.IsChecked = st.AutoCleanEnabled;
                CbThreshold.IsEnabled = st.AutoCleanEnabled;
                int idx = Math.Clamp((st.AutoCleanThresholdPercent - 70) / 5, 0, 4);
                if (CbThreshold.Items.Count > idx)
                {
                    CbThreshold.SelectedIndex = idx;
                }
                ChkMinimizeToTray.IsChecked = st.MinimizeToTray;
            }
            finally
            {
                isSyncingSettings = false;
            }
        }

        public void ApplyLanguage()
        {
            bool isAr = LocalizationService.IsArabic;
            lblHead.Text = UITheme.StripLeadingIcon(LocalizationService.T("DashHeader", "⚡ Memory & Performance Center"));
            lblSub.Text = LocalizationService.T("DashSub", "Real-time physical working set monitoring with intelligent zero-bloat RAM purging.");
            UpdateBoostButtonContent();
            ChkAutoClean.Content = LocalizationService.T("AutoCleanEnable", "Enable Background Auto-Clean");
            lblTrigger.Text = LocalizationService.T("AutoCleanTrigger", "Trigger when RAM exceeds:");
            string cooldownTip = LocalizationService.T("AutoCleanCooldownTooltip", "Runs automatic RAM cleanup when usage reaches this level (15-minute cooldown to protect SSD/NVMe/HDD).");
            ChkAutoClean.ToolTip = cooldownTip;
            lblTrigger.ToolTip = cooldownTip;
            CbThreshold.ToolTip = cooldownTip;
            ChkMinimizeToTray.Content = LocalizationService.T("KeepInTray", "Keep in Tray on Close");
            lblLogTitle.Text = LocalizationService.T("LogTitle", "Live Activity & Optimization Log");
            UpdateLogActionButtons();

            CardUsed.Title = LocalizationService.T("MetricInUse", "IN-USE RAM");
            CardAvailable.Title = LocalizationService.T("MetricAvailable", "AVAILABLE RAM");

            ChipInUse.Title = LocalizationService.T("StatusInUse", "In-Use");
            ChipAvailable.Title = LocalizationService.T("StatusAvailable", "Available");
            ChipTotalRam.Title = LocalizationService.T("StatusTotalRam", "Total RAM");

            isSyncingSettings = true;
            try
            {
                CbThreshold.Items.Clear();
                if (isAr)
                {
                    CbThreshold.Items.Add("\u200E70% ضغط");
                    CbThreshold.Items.Add("\u200E75% ضغط");
                    CbThreshold.Items.Add("\u200E80% ضغط");
                    CbThreshold.Items.Add("\u200E85% ضغط");
                    CbThreshold.Items.Add("\u200E90% ضغط");
                }
                else
                {
                    CbThreshold.Items.Add("70% Load");
                    CbThreshold.Items.Add("75% Load");
                    CbThreshold.Items.Add("80% Load");
                    CbThreshold.Items.Add("85% Load");
                    CbThreshold.Items.Add("90% Load");
                }
                int initialIdx = Math.Clamp((SettingsService.Instance.Current.AutoCleanThresholdPercent - 70) / 5, 0, 4);
                CbThreshold.SelectedIndex = initialIdx;
                CbThreshold.IsEnabled = SettingsService.Instance.Current.AutoCleanEnabled;
            }
            finally
            {
                isSyncingSettings = false;
            }
            RenderQuickCards();
        }

        private void UpdateLogActionButtons()
        {
            var fg = WpfTheme.IsDarkMode
                ? new SolidColorBrush(Color.FromRgb(201, 209, 217))
                : new SolidColorBrush(Color.FromRgb(30, 41, 59));
            BtnCopyLog.Content = WpfIcons.CreateIconTextContent("Copy", UITheme.StripLeadingIcon(LocalizationService.T("BtnCopy", "📋 Copy")), fg, 15, 7);
            BtnClearLog.Content = WpfIcons.CreateIconTextContent("Trash", UITheme.StripLeadingIcon(LocalizationService.T("BtnClear", "🗑️ Clear")), fg, 15, 7);
        }

        public void RefreshTheme()
        {
            bool isDark = WpfTheme.IsDarkMode;
            lblHead.Foreground = WpfTheme.TextPrimaryBrush;
            lblSub.Foreground = WpfTheme.TextSecondaryBrush;
            lblTrigger.Foreground = WpfTheme.TextSecondaryBrush;
            lblLogTitle.Foreground = WpfTheme.TextPrimaryBrush;

            ChkAutoClean.Foreground = WpfTheme.TextPrimaryBrush;
            ChkMinimizeToTray.Foreground = WpfTheme.TextPrimaryBrush;

            cardTop.Background = WpfTheme.CardBrush;
            cardTop.BorderBrush = WpfTheme.BorderBrush;
            cardSettings.Background = WpfTheme.CardBrush;
            cardSettings.BorderBrush = WpfTheme.BorderBrush;
            cardLog.Background = WpfTheme.CardBrush;
            cardLog.BorderBrush = WpfTheme.BorderBrush;

            TxtLog.Background = WpfTheme.CardSubBrush;
            TxtLog.Foreground = isDark ? WpfTheme.CyanBrush : new SolidColorBrush(Color.FromRgb(30, 41, 59));

            ApplySecondaryButtonStyle(BtnCopyLog);
            ApplySecondaryButtonStyle(BtnClearLog);
            UpdateLogActionButtons();

            cardQuickTurbo.Background = WpfTheme.CardBrush;
            cardQuickTurbo.BorderBrush = WpfTheme.BorderBrush;
            cardQuickStartup.Background = WpfTheme.CardBrush;
            cardQuickStartup.BorderBrush = WpfTheme.BorderBrush;

            CardUsed.UpdateTheme();
            CardAvailable.UpdateTheme();

            ChipInUse.RefreshTheme();
            ChipAvailable.RefreshTheme();
            ChipTotalRam.RefreshTheme();

            RenderQuickCards();
        }

        public void UpdateQuickStates(bool turboActive, bool turboGameMode, int startupOptimizableCount, int startupUnknownCount, long turboSavedBytes = 0)
        {
            lastTurboActive = turboActive;
            lastTurboGameMode = turboGameMode;
            lastStartupOptimizableCount = startupOptimizableCount;
            lastStartupUnknownCount = startupUnknownCount;
            lastTurboSavedBytes = turboSavedBytes;
            RenderQuickCards();
        }

        private void RenderQuickCards()
        {
            bool isDark = WpfTheme.IsDarkMode;
            bool isAr = LocalizationService.IsArabic;
            var settings = SettingsService.Instance.Current;
            var amberColor = isDark ? Color.FromRgb(251, 146, 60) : Color.FromRgb(217, 119, 6);
            var amberBrush = new SolidColorBrush(amberColor);
            var linkColor = isDark ? Color.FromRgb(96, 165, 250) : Color.FromRgb(37, 99, 235);
            var linkBrush = new SolidColorBrush(linkColor);

            // 1. Render Turbo Boost Card
            bool turboIgnored = settings.IgnoreTurboDashboardCard && !lastTurboActive;
            bool turboAttention = !lastTurboActive && !turboIgnored;
            BuildBadgedIcon(turboIconBadgeHost, "SpeedGauge", turboAttention, amberColor);

            lblTurboTitlePrefix.Text = LocalizationService.T("DashTurboTitle", "Turbo Boost:");
            lblTurboTitlePrefix.Foreground = WpfTheme.TextPrimaryBrush;
            lblTurboTitleState.Text = lastTurboActive
                ? LocalizationService.T("DashTurboStateOn", "ON")
                : (turboIgnored ? LocalizationService.T("DashTurboStateIgnored", "Ignored") : LocalizationService.T("DashTurboStateOff", "OFF"));
            lblTurboTitleState.Foreground = lastTurboActive
                ? WpfTheme.SuccessBrush
                : (turboIgnored ? WpfTheme.TextSecondaryBrush : amberBrush);

            lblTurboDesc.Inlines.Clear();
            if (lastTurboActive)
            {
                string modeName = lastTurboGameMode
                    ? UITheme.StripLeadingIcon(LocalizationService.T("DashTurboGaming", "Gaming & Heavy Apps"))
                    : UITheme.StripLeadingIcon(LocalizationService.T("DashTurboWork", "Work & Productivity"));
                if (lastTurboSavedBytes > 0)
                {
                    string savedStr = WpfTheme.FormatBytes(lastTurboSavedBytes);
                    string prefix = isAr ? $"{modeName} نشط • " : $"{modeName} active • ";
                    string savedPart = isAr ? $"تم توفير \u200E{savedStr}\u200E من الذاكرة." : $"saved {savedStr} RAM.";
                    lblTurboDesc.Inlines.Add(new Run(prefix) { Foreground = WpfTheme.TextSecondaryBrush });
                    lblTurboDesc.Inlines.Add(new Run(savedPart)
                    {
                        Foreground = WpfTheme.SuccessBrush,
                        FontWeight = FontWeights.SemiBold
                    });
                }
                else
                {
                    lblTurboDesc.Inlines.Add(new Run(string.Format(LocalizationService.T("DashTurboDescOn", "{0} active • freeing background apps & services."), modeName))
                    {
                        Foreground = WpfTheme.TextSecondaryBrush
                    });
                }
            }
            else if (turboIgnored)
            {
                lblTurboDesc.Inlines.Add(new Run(LocalizationService.T("DashTurboDescIgnored", "Turbo Boost prompt is currently ignored.") + " ")
                {
                    Foreground = WpfTheme.TextSecondaryBrush
                });
                lblTurboDesc.Inlines.Add(CreateInlineActionLink(LocalizationService.T("DashUnignore", "Unignore"), linkBrush, () =>
                {
                    SettingsService.Instance.Update(st => st.IgnoreTurboDashboardCard = false);
                    RenderQuickCards();
                }));
            }
            else
            {
                lblTurboDesc.Inlines.Add(new Run(LocalizationService.T("DashTurboDescOff", "Stop unnecessary programs to release RAM and boost your PC.") + " ")
                {
                    Foreground = WpfTheme.TextSecondaryBrush
                });
                lblTurboDesc.Inlines.Add(CreateInlineActionLink(LocalizationService.T("DashIgnore", "Ignore"), linkBrush, () =>
                {
                    SettingsService.Instance.Update(st => st.IgnoreTurboDashboardCard = true);
                    RenderQuickCards();
                }));
            }

            if (lastTurboActive)
            {
                btnTurboMain.Content = LocalizationService.T("DashTurboTurnOff", "Turn Off");
                turboSplitContainer.Background = new LinearGradientBrush(Color.FromRgb(239, 68, 68), Color.FromRgb(220, 38, 38), new Point(0, 0), new Point(1, 1));
                turboSplitDivider.Visibility = Visibility.Collapsed;
                btnTurboDrop.Visibility = Visibility.Collapsed;
            }
            else
            {
                btnTurboMain.Content = LocalizationService.T("DashTurboTurnOn", "Turn On");
                turboSplitContainer.Background = new LinearGradientBrush(Color.FromRgb(59, 130, 246), Color.FromRgb(37, 99, 235), new Point(0, 0), new Point(1, 1));
                turboSplitDivider.Visibility = Visibility.Visible;
                btnTurboDrop.Visibility = Visibility.Visible;
            }

            // 2. Render Startup Optimizer Card
            bool startupIgnored = settings.IgnoreStartupDashboardCard;
            bool startupAttention = !startupIgnored && (lastStartupUnknownCount > 0 || lastStartupOptimizableCount > 0);
            BuildBadgedIcon(startupIconBadgeHost, "Rocket", startupAttention, amberColor);

            lblStartupTitle.Text = LocalizationService.T("DashStartupTitle", "Startup Optimizer");
            lblStartupTitle.Foreground = WpfTheme.TextPrimaryBrush;
            btnStartupOptimize.Content = LocalizationService.T("DashStartupOptimize", "Optimize");
            ApplyPrimaryButtonStyle(btnStartupOptimize);

            lblStartupDesc.Inlines.Clear();
            if (startupIgnored)
            {
                string ignoredMsg = lastStartupUnknownCount > 0
                    ? (isAr
                        ? $"تم تجاهل تنبيه بدء التشغيل ({lastStartupUnknownCount} غير معروف بانتظار المراجعة)."
                        : $"Startup optimization prompt is ignored ({lastStartupUnknownCount} unknown pending).")
                    : LocalizationService.T("DashStartupIgnoredDesc", "Startup optimization prompt is ignored.");
                lblStartupDesc.Inlines.Add(new Run(ignoredMsg + " ")
                {
                    Foreground = WpfTheme.TextSecondaryBrush
                });
                lblStartupDesc.Inlines.Add(CreateInlineActionLink(LocalizationService.T("DashUnignore", "Unignore"), linkBrush, () =>
                {
                    SettingsService.Instance.Update(st => st.IgnoreStartupDashboardCard = false);
                    RenderQuickCards();
                }));
            }
            else if (lastStartupUnknownCount > 0)
            {
                string unknownHighlight = isAr
                    ? $"{lastStartupUnknownCount} تطبيق جديد غير معروف"
                    : $"{lastStartupUnknownCount} unknown new item{(lastStartupUnknownCount == 1 ? "" : "s")}";
                lblStartupDesc.Inlines.Add(new Run(unknownHighlight)
                {
                    Foreground = amberBrush,
                    FontWeight = FontWeights.SemiBold
                });

                string rest = isAr
                    ? (lastStartupOptimizableCount > 0
                        ? $" (و{lastStartupOptimizableCount} قابلة للتحسين) لإقلاع أسرع. "
                        : " بانتظار المراجعة عند الإقلاع. ")
                    : (lastStartupOptimizableCount > 0
                        ? $" detected ({lastStartupOptimizableCount} optimizable) to boot faster. "
                        : " detected on startup awaiting review. ");
                lblStartupDesc.Inlines.Add(new Run(rest) { Foreground = WpfTheme.TextSecondaryBrush });
                lblStartupDesc.Inlines.Add(CreateInlineActionLink(LocalizationService.T("DashIgnore", "Ignore"), linkBrush, () =>
                {
                    SettingsService.Instance.Update(st => st.IgnoreStartupDashboardCard = true);
                    RenderQuickCards();
                }));
            }
            else if (lastStartupOptimizableCount > 0)
            {
                string highlight = isAr
                    ? $"{lastStartupOptimizableCount} عناصر بدء تشغيل"
                    : $"{lastStartupOptimizableCount} startup item{(lastStartupOptimizableCount == 1 ? "" : "s")}";
                lblStartupDesc.Inlines.Add(new Run(highlight)
                {
                    Foreground = amberBrush,
                    FontWeight = FontWeights.SemiBold
                });
                string tail = isAr
                    ? " يمكن تحسينها لتسريع إقلاع جهازك. "
                    : " can be optimized to boot your PC faster. ";
                lblStartupDesc.Inlines.Add(new Run(tail) { Foreground = WpfTheme.TextSecondaryBrush });
                lblStartupDesc.Inlines.Add(CreateInlineActionLink(LocalizationService.T("DashIgnore", "Ignore"), linkBrush, () =>
                {
                    SettingsService.Instance.Update(st => st.IgnoreStartupDashboardCard = true);
                    RenderQuickCards();
                }));
            }
            else
            {
                lblStartupDesc.Inlines.Add(new Run(LocalizationService.T("DashStartupOptimalDesc", "Startup items are optimized for fast boot."))
                {
                    Foreground = WpfTheme.SuccessBrush
                });
            }
        }

        private static void BuildBadgedIcon(Grid host, string iconKey, bool showWarning, Color amberColor)
        {
            host.Children.Clear();

            var mainIcon = WpfIcons.CreateIcon(iconKey, WpfTheme.TextSecondaryBrush, 34, 1.5);
            mainIcon.HorizontalAlignment = HorizontalAlignment.Left;
            mainIcon.VerticalAlignment = VerticalAlignment.Top;
            mainIcon.Margin = new Thickness(0, 2, 0, 0);
            host.Children.Add(mainIcon);

            var badgeColor = showWarning ? amberColor : WpfTheme.ColorSuccess;
            var badgeBg = new Border
            {
                Width = 17,
                Height = 17,
                CornerRadius = new CornerRadius(8.5),
                Background = new SolidColorBrush(badgeColor),
                BorderBrush = WpfTheme.CardBrush,
                BorderThickness = new Thickness(1.5),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 2, 2),
                Child = new TextBlock
                {
                    Text = showWarning ? "i" : "✓",
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            host.Children.Add(badgeBg);
        }

        private static Hyperlink CreateInlineActionLink(string text, Brush foreground, Action onClick)
        {
            var link = new Hyperlink(new Run(text))
            {
                Foreground = foreground,
                TextDecorations = TextDecorations.Underline
            };
            link.Click += (s, e) => onClick();
            return link;
        }

        private void ShowTurboModeMenu()
        {
            var menu = new ContextMenu();
            var itemGame = new MenuItem
            {
                Header = UITheme.StripLeadingIcon(LocalizationService.T("DashTurboGaming", "Gaming & Heavy Apps")),
                IsCheckable = true,
                IsChecked = lastTurboGameMode
            };
            itemGame.Click += (s, e) => TurboModeSelected?.Invoke(true);

            var itemWork = new MenuItem
            {
                Header = UITheme.StripLeadingIcon(LocalizationService.T("DashTurboWork", "Work & Productivity")),
                IsCheckable = true,
                IsChecked = !lastTurboGameMode
            };
            itemWork.Click += (s, e) => TurboModeSelected?.Invoke(false);

            var itemOpen = new MenuItem
            {
                Header = LocalizationService.T("DashTurboOpenFull", "Open Turbo Boost View...")
            };
            itemOpen.Click += (s, e) => OpenTurboRequested?.Invoke();

            menu.Items.Add(itemGame);
            menu.Items.Add(itemWork);
            menu.Items.Add(new Separator());
            menu.Items.Add(itemOpen);

            menu.PlacementTarget = turboSplitContainer;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private static ControlTemplate CreateFlatButtonTemplate(double cornerRadius = 0)
        {
            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
            if (cornerRadius > 0)
            {
                borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(cornerRadius));
            }

            var cpFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            cpFactory.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cpFactory.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            cpFactory.SetValue(MarginProperty, new TemplateBindingExtension(PaddingProperty));
            borderFactory.AppendChild(cpFactory);

            template.VisualTree = borderFactory;
            return template;
        }
    }
}
