using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using CleanRamBoosterApp.Services;
using Microsoft.Win32;

namespace CleanRamBoosterApp.WpfUI.Toolbox
{
    public class ToolboxSystemInfoWindow : ToolboxWindowBase
    {
        private sealed class SpecRow
        {
            public bool IsGroupHeader { get; init; }
            public string Name { get; init; } = string.Empty;
            public string Value { get; init; } = string.Empty;
        }

        private readonly HardwareInfoService hardwareService = new();
        private DetailedSystemReport? currentReport;
        private int currentCategoryIndex;

        private readonly Border headerBar;
        private readonly Border sidebarBorder;
        private readonly Border footerBar;
        private readonly Border cardSpecs;
        private readonly Border cardBottomMetrics;
        private readonly TextBlock lblTitle;
        private readonly TextBlock lblSub;
        private readonly TextBlock lblLoading;
        private readonly DataGrid gridSpecs;
        private readonly Button btnExport;
        private readonly Button btnCopy;
        private readonly Button btnClose;
        private readonly List<(Button Btn, Border AccentBar, string IconKey, string Title)> navButtons = new();

        // Live CPU Meter & Oscilloscope Controls
        private readonly Grid cpuMetricsGrid;
        private readonly StackPanel cpuLedStack;
        private readonly TextBlock lblCpuPct;
        private readonly TextBlock lblCpuUsageLegend;
        private readonly TextBlock lblCpuClockLegend;
        private readonly Canvas cpuChartCanvas;
        private readonly Grid memorySlotsHost;

        private readonly DispatcherTimer tmrLive;
        private int currentCpuPercent;
        private int currentCpuMhz = 2000;
        private int baseCpuMhz = 2000;
        private readonly Queue<int> cpuHistory = new();
        private const int MaxHistorySamples = 60;

        public ToolboxSystemInfoWindow(DetailedSystemReport? report = null)
            : base("System Information - PC Boost", "معلومات النظام - PC Boost", 950, 645, resizable: true)
        {
            MinWidth = 860;
            MinHeight = 560;
            bool isAr = LocalizationService.IsArabic;

            currentCpuMhz = HardwareInfoService.GetLiveCpuClockMhz(out baseCpuMhz, out _);
            for (int i = 0; i < MaxHistorySamples; i++) cpuHistory.Enqueue(0);

            var shell = new Grid();
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Body
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer

            // 1. Header Banner
            headerBar = new Border
            {
                Background = WpfTheme.SidebarBrush,
                BorderBrush = WpfTheme.BorderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(18, 12, 18, 12)
            };
            var hdrGrid = new Grid();
            hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var appIcon = WpfTheme.GetWindowIconSource();
            if (appIcon != null)
            {
                var img = new Image { Source = appIcon, Width = 42, Height = 42, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                Grid.SetColumn(img, 0);
                hdrGrid.Children.Add(img);
            }
            else
            {
                var badge = WpfIcons.CreateHeaderBadge("Monitor", WpfTheme.ColorPurple, 42, 22);
                badge.Margin = new Thickness(0, 0, 14, 0);
                Grid.SetColumn(badge, 0);
                hdrGrid.Children.Add(badge);
            }

            var hdrStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            lblTitle = new TextBlock
            {
                Text = isAr ? "معلومات النظام الشاملة" : "System Information",
                FontSize = 19,
                FontWeight = FontWeights.SemiBold,
                Foreground = WpfTheme.TextPrimaryBrush
            };
            lblSub = new TextBlock
            {
                Text = isAr ? "فحص وتجميع تفاصيل عتاد الجهاز وبيئة نظام التشغيل بدقة واحترافية." : "Collects and displays detailed information about your system hardware and OS environment.",
                FontSize = 12.5,
                Foreground = WpfTheme.TextSecondaryBrush,
                Margin = new Thickness(0, 2, 0, 0)
            };
            hdrStack.Children.Add(lblTitle);
            hdrStack.Children.Add(lblSub);
            Grid.SetColumn(hdrStack, 1);
            hdrGrid.Children.Add(hdrStack);
            headerBar.Child = hdrGrid;
            Grid.SetRow(headerBar, 0);
            shell.Children.Add(headerBar);

            // 2. Body (Sidebar + Content)
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(222) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 2a. Sidebar
            sidebarBorder = new Border
            {
                Background = WpfTheme.SidebarBrush,
                BorderBrush = WpfTheme.BorderBrush,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(8, 10, 8, 10)
            };
            var navStack = new StackPanel();
            string[] catTitles = isAr
                ? new[] { "نظرة عامة", "نظام التشغيل", "المعالج واللوحة الأم", "الذاكرة العشوائية", "التخزين والأقراص", "بطاقة الشاشة والعرض", "محولات الشبكة", "الأجهزة الأخرى" }
                : new[] { "Overall", "Operating System", "Processor & Mainboard", "Memory Device", "Drives", "Display", "Network", "Other Devices" };
            string[] catIcons = { "Globe", "OsWindows", "Cpu", "Memory", "Drive", "Monitor", "Network", "Keyboard" };

            for (int i = 0; i < catTitles.Length; i++)
            {
                int idx = i;
                var accentBar = new Border
                {
                    Width = 3.5,
                    Height = 22,
                    CornerRadius = new CornerRadius(2),
                    Background = WpfTheme.PrimaryBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                    Visibility = Visibility.Hidden
                };
                var btn = new Button
                {
                    Height = 38,
                    Margin = new Thickness(0, 0, 0, 4),
                    Padding = new Thickness(6, 0, 10, 0),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                btn.Click += (_, _) => SelectCategory(idx);
                navButtons.Add((btn, accentBar, catIcons[i], catTitles[i]));
                navStack.Children.Add(btn);
            }
            sidebarBorder.Child = navStack;
            Grid.SetColumn(sidebarBorder, 0);
            body.Children.Add(sidebarBorder);

            // 2b. Main Content Area (Specs Grid + Conditional Bottom Metrics Panel)
            var contentGrid = new Grid { Margin = new Thickness(14) };
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            cardSpecs = CreateCard(new Thickness(0));
            cardSpecs.ClipToBounds = true;

            var specsHost = new Grid();
            lblLoading = new TextBlock
            {
                Text = isAr ? "جاري فحص وتجميع مواصفات العتاد والنظام..." : "Collecting hardware and system configuration...",
                FontSize = 14,
                Foreground = WpfTheme.TextSecondaryBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            gridSpecs = CreateVirtualDataGrid(DataGridSelectionMode.Extended);
            gridSpecs.RowHeight = 30;
            gridSpecs.CanUserSortColumns = false;
            gridSpecs.RowStyle = CreateSpecRowStyle();

            var colName = new DataGridTextColumn
            {
                Header = isAr ? "اسم البند" : "Item Name",
                Binding = new Binding(nameof(SpecRow.Name)),
                Width = new DataGridLength(245),
                ElementStyle = CreateSpecNameCellStyle()
            };
            var colVal = new DataGridTextColumn
            {
                Header = isAr ? "القيمة والمواصفات" : "Value",
                Binding = new Binding(nameof(SpecRow.Value)),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                ElementStyle = CreateEllipsisTextStyle(forceLtr: true)
            };
            gridSpecs.Columns.Add(colName);
            gridSpecs.Columns.Add(colVal);
            gridSpecs.Visibility = Visibility.Collapsed;

            specsHost.Children.Add(gridSpecs);
            specsHost.Children.Add(lblLoading);
            cardSpecs.Child = specsHost;
            Grid.SetRow(cardSpecs, 0);
            contentGrid.Children.Add(cardSpecs);

            // Conditional Bottom Metrics Card (CPU Live Chart OR RAM Slots Bar)
            cardBottomMetrics = CreateCard(new Thickness(10));
            cardBottomMetrics.Margin = new Thickness(0, 12, 0, 0);
            cardBottomMetrics.Visibility = Visibility.Collapsed;

            var metricsHost = new Grid();

            // CPU Live Meter + Waveform Oscilloscope
            cpuMetricsGrid = new Grid { Height = 144, Visibility = Visibility.Collapsed };
            cpuMetricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(152) });
            cpuMetricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            cpuMetricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var ledBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(10, 16, 26)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(26, 40, 60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 6)
            };
            var ledGrid = new Grid();
            ledGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ledGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            ledGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var lblLedTitle = new TextBlock
            {
                Text = isAr ? "استخدام المعالج" : "CPU USAGE",
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            };
            Grid.SetRow(lblLedTitle, 0);
            ledGrid.Children.Add(lblLedTitle);

            cpuLedStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Width = 44
            };
            for (int s = 0; s < 14; s++)
            {
                cpuLedStack.Children.Add(new Border
                {
                    Height = 4.2,
                    Margin = new Thickness(0, 1.3, 0, 0),
                    CornerRadius = new CornerRadius(1),
                    Background = new SolidColorBrush(Color.FromRgb(20, 32, 26))
                });
            }
            Grid.SetRow(cpuLedStack, 1);
            ledGrid.Children.Add(cpuLedStack);

            lblCpuPct = new TextBlock
            {
                Text = "0%",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            };
            Grid.SetRow(lblCpuPct, 2);
            ledGrid.Children.Add(lblCpuPct);
            ledBorder.Child = ledGrid;
            Grid.SetColumn(ledBorder, 0);
            cpuMetricsGrid.Children.Add(ledBorder);

            // Right Oscilloscope Chart
            var chartBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(7, 13, 18)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(26, 40, 60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                ClipToBounds = true
            };
            var chartLayout = new Grid();
            chartLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            chartLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var chartHdr = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(13, 21, 34)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(26, 40, 60)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10, 5, 10, 5)
            };
            var chartHdrGrid = new Grid();
            chartHdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            chartHdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            chartHdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            chartHdrGrid.Children.Add(new TextBlock
            {
                Text = isAr ? "استخدام المعالج" : "CPU Usage",
                FontSize = 11.5,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                VerticalAlignment = VerticalAlignment.Center
            });

            var legendStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.LeftToRight };
            lblCpuUsageLegend = new TextBlock
            {
                Text = "Usage: 0%",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                Margin = new Thickness(0, 0, 10, 0)
            };
            lblCpuClockLegend = new TextBlock
            {
                Text = $"Clock: {currentCpuMhz / 1000.0:0.00} GHz (Base: {baseCpuMhz / 1000.0:0.00} GHz)",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248))
            };
            legendStack.Children.Add(lblCpuUsageLegend);
            legendStack.Children.Add(lblCpuClockLegend);
            Grid.SetColumn(legendStack, 2);
            chartHdrGrid.Children.Add(legendStack);
            chartHdr.Child = chartHdrGrid;
            Grid.SetRow(chartHdr, 0);
            chartLayout.Children.Add(chartHdr);

            cpuChartCanvas = new Canvas { ClipToBounds = true, FlowDirection = FlowDirection.LeftToRight };
            cpuChartCanvas.SizeChanged += (_, _) => RedrawCpuChart();
            Grid.SetRow(cpuChartCanvas, 1);
            chartLayout.Children.Add(cpuChartCanvas);

            chartBorder.Child = chartLayout;
            Grid.SetColumn(chartBorder, 2);
            cpuMetricsGrid.Children.Add(chartBorder);

            // Memory Slots Host
            memorySlotsHost = new Grid { Visibility = Visibility.Collapsed };

            metricsHost.Children.Add(cpuMetricsGrid);
            metricsHost.Children.Add(memorySlotsHost);
            cardBottomMetrics.Child = metricsHost;
            Grid.SetRow(cardBottomMetrics, 1);
            contentGrid.Children.Add(cardBottomMetrics);

            Grid.SetColumn(contentGrid, 1);
            body.Children.Add(contentGrid);
            Grid.SetRow(body, 1);
            shell.Children.Add(body);

            // 3. Footer Toolbar
            footerBar = new Border
            {
                Background = WpfTheme.SidebarBrush,
                BorderBrush = WpfTheme.BorderBrush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 10, 16, 10)
            };
            var ftrGrid = new Grid();
            ftrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ftrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ftrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftFtr = new StackPanel { Orientation = Orientation.Horizontal };
            btnExport = CreatePrimaryButton("ExportDoc", isAr ? "تصدير التقرير..." : "Export Report...", 34);
            btnExport.Margin = new Thickness(0, 0, 10, 0);
            btnExport.Click += (_, _) => PromptExport();

            btnCopy = CreateSecondaryButton("Copy", isAr ? "نسخ المواصفات" : "Copy Specs", 34);
            btnCopy.Click += (_, _) => CopyCurrentCategoryToClipboard();

            leftFtr.Children.Add(btnExport);
            leftFtr.Children.Add(btnCopy);
            Grid.SetColumn(leftFtr, 0);
            ftrGrid.Children.Add(leftFtr);

            btnClose = CreateSecondaryButton(null, isAr ? "إغلاق" : "Close", 34);
            btnClose.MinWidth = 96;
            btnClose.Click += (_, _) => Close();
            Grid.SetColumn(btnClose, 2);
            ftrGrid.Children.Add(btnClose);

            footerBar.Child = ftrGrid;
            Grid.SetRow(footerBar, 2);
            shell.Children.Add(footerBar);

            Content = shell;
            UpdateNavStyles();

            tmrLive = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            tmrLive.Tick += (_, _) => UpdateLiveMetrics();

            if (report != null)
            {
                LoadSpecs(report);
            }
            else
            {
                Loaded += async (_, _) => await LoadSpecsAsync();
            }

            Closed += (_, _) => tmrLive.Stop();
        }

        public void LoadSync()
        {
            if (currentReport != null) return;
            try
            {
                LoadSpecs(hardwareService.GetDetailedReport());
            }
            catch (Exception ex)
            {
                DiagnosticLogger.LogError(nameof(ToolboxSystemInfoWindow), "Failed to load system specs synchronously", ex);
            }
        }

        public void LoadSpecs(DetailedSystemReport report)
        {
            currentReport = report;
            lblLoading.Visibility = Visibility.Collapsed;
            gridSpecs.Visibility = Visibility.Visible;
            tmrLive.Start();
            SelectCategory(0);
        }

        private async Task LoadSpecsAsync()
        {
            if (currentReport != null) return;
            try
            {
                var rep = await Task.Run(() => hardwareService.GetDetailedReport());
                LoadSpecs(rep);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.LogError(nameof(ToolboxSystemInfoWindow), "Failed to load system specs", ex);
                lblLoading.Text = LocalizationService.IsArabic
                    ? $"تعذر قراءة تفاصيل العتاد: {ex.Message}"
                    : $"Error reading hardware configuration: {ex.Message}";
            }
        }

        public void SelectCategory(int index)
        {
            if (currentReport == null || index < 0 || index >= currentReport.Categories.Count) return;
            currentCategoryIndex = index;
            UpdateNavStyles();

            var cat = currentReport.Categories[index];
            var rows = new List<SpecRow>();
            foreach (var grp in cat.Groups)
            {
                rows.Add(new SpecRow { IsGroupHeader = true, Name = grp.GroupName, Value = string.Empty });
                foreach (var item in grp.Items)
                    rows.Add(new SpecRow { IsGroupHeader = false, Name = "   " + item.Name, Value = item.Value });
            }
            gridSpecs.ItemsSource = rows;

            if (cat.CategoryId == "CPU")
            {
                cardBottomMetrics.Visibility = Visibility.Visible;
                cpuMetricsGrid.Visibility = Visibility.Visible;
                memorySlotsHost.Visibility = Visibility.Collapsed;
                UpdateLiveMetrics();
            }
            else if (cat.CategoryId == "Memory")
            {
                cardBottomMetrics.Visibility = Visibility.Visible;
                cpuMetricsGrid.Visibility = Visibility.Collapsed;
                memorySlotsHost.Visibility = Visibility.Visible;
                BuildMemorySlotsBar();
            }
            else
            {
                cardBottomMetrics.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateNavStyles()
        {
            bool isDark = WpfTheme.IsDarkMode;
            for (int i = 0; i < navButtons.Count; i++)
            {
                var (btn, bar, iconKey, title) = navButtons[i];
                bool active = i == currentCategoryIndex;
                Brush fg = active ? (isDark ? new SolidColorBrush(WpfTheme.ColorCyan) : WpfTheme.PrimaryBrush) : WpfTheme.TextSecondaryBrush;
                btn.Background = active
                    ? new SolidColorBrush(isDark ? Color.FromRgb(30, 41, 59) : Color.FromRgb(219, 234, 254))
                    : Brushes.Transparent;
                bar.Background = fg;
                bar.Visibility = active ? Visibility.Visible : Visibility.Hidden;

                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                if (bar.Parent is Panel oldParent) oldParent.Children.Remove(bar);
                Grid.SetColumn(bar, 0);
                row.Children.Add(bar);

                var icon = WpfIcons.CreateIcon(iconKey, fg, 16, 1.75);
                icon.Margin = new Thickness(0, 0, 10, 0);
                Grid.SetColumn(icon, 1);
                row.Children.Add(icon);

                var tb = new TextBlock
                {
                    Text = title,
                    FontSize = 12.5,
                    FontWeight = active ? FontWeights.Medium : FontWeights.Normal,
                    Foreground = fg,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(tb, 2);
                row.Children.Add(tb);

                btn.Content = row;
            }
        }

        private Style CreateSpecRowStyle()
        {
            bool isDark = WpfTheme.IsDarkMode;
            var style = new Style(typeof(DataGridRow), WpfTheme.CreateDataGridRowStyle());
            var hdrTrigger = new DataTrigger { Binding = new Binding(nameof(SpecRow.IsGroupHeader)), Value = true };
            hdrTrigger.Setters.Add(new Setter(Control.BackgroundProperty,
                new SolidColorBrush(isDark ? Color.FromArgb(42, 56, 189, 248) : Color.FromRgb(224, 242, 254))));
            hdrTrigger.Setters.Add(new Setter(Control.ForegroundProperty,
                new SolidColorBrush(isDark ? Color.FromRgb(56, 189, 248) : Color.FromRgb(3, 105, 161))));
            hdrTrigger.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            style.Triggers.Add(hdrTrigger);
            return style;
        }

        private static Style CreateSpecNameCellStyle()
        {
            var st = CreateEllipsisTextStyle(forceLtr: false);
            var hdrTrigger = new DataTrigger { Binding = new Binding(nameof(SpecRow.IsGroupHeader)), Value = true };
            hdrTrigger.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
            st.Triggers.Add(hdrTrigger);
            return st;
        }

        private void UpdateLiveMetrics()
        {
            try
            {
                currentCpuPercent = HardwareInfoService.GetLiveCpuUsage();
                currentCpuMhz = HardwareInfoService.GetLiveCpuClockMhz(out int bMhz, out _);
                if (bMhz > 0) baseCpuMhz = bMhz;

                if (cpuHistory.Count >= MaxHistorySamples) cpuHistory.Dequeue();
                cpuHistory.Enqueue(currentCpuPercent);

                if (cpuMetricsGrid.Visibility == Visibility.Visible)
                {
                    RedrawCpuLedMeter();
                    RedrawCpuChart();
                }
            }
            catch { }
        }

        private void RedrawCpuLedMeter()
        {
            int activeSegments = (int)Math.Round((currentCpuPercent / 100.0) * 14);
            for (int i = 0; i < cpuLedStack.Children.Count; i++)
            {
                // Top child is index 0 (segment 13), bottom child is index 13 (segment 0)
                int segIndexFromBottom = 13 - i;
                bool active = segIndexFromBottom < activeSegments;
                Color col = active
                    ? (segIndexFromBottom > 11 ? Color.FromRgb(239, 68, 68) : (segIndexFromBottom > 8 ? Color.FromRgb(245, 158, 11) : Color.FromRgb(16, 185, 129)))
                    : Color.FromRgb(20, 32, 26);
                if (cpuLedStack.Children[i] is Border seg)
                    seg.Background = new SolidColorBrush(col);
            }

            Color pctCol = currentCpuPercent > 80
                ? Color.FromRgb(239, 68, 68)
                : (currentCpuPercent > 50 ? Color.FromRgb(245, 158, 11) : Color.FromRgb(34, 197, 94));
            lblCpuPct.Text = $"{currentCpuPercent}%";
            lblCpuPct.Foreground = new SolidColorBrush(pctCol);

            double curGhz = currentCpuMhz / 1000.0;
            double baseGhz = Math.Max(0.1, baseCpuMhz / 1000.0);
            lblCpuUsageLegend.Text = $"Usage: {currentCpuPercent}%";
            lblCpuClockLegend.Text = $"Clock: {curGhz:0.00} GHz (Base: {baseGhz:0.00} GHz)";
        }

        private void RedrawCpuChart()
        {
            cpuChartCanvas.Children.Clear();
            double w = cpuChartCanvas.ActualWidth;
            double h = cpuChartCanvas.ActualHeight;
            if (w < 40 || h < 30) return;

            double plotLeft = 34;
            double plotRight = w - 8;
            double plotTop = 8;
            double plotBottom = h - 8;
            double plotW = Math.Max(10, plotRight - plotLeft);
            double plotH = Math.Max(10, plotBottom - plotTop);

            var gridBrush = new SolidColorBrush(Color.FromRgb(18, 42, 46));
            for (double x = plotLeft; x <= plotRight; x += 28)
            {
                cpuChartCanvas.Children.Add(new Line { X1 = x, Y1 = plotTop, X2 = x, Y2 = plotBottom, Stroke = gridBrush, StrokeThickness = 1 });
            }

            foreach (int lvl in new[] { 100, 50, 0 })
            {
                double gy = plotBottom - (lvl / 100.0) * plotH;
                cpuChartCanvas.Children.Add(new Line { X1 = plotLeft, Y1 = gy, X2 = plotRight, Y2 = gy, Stroke = gridBrush, StrokeThickness = 1 });
                var lbl = new TextBlock
                {
                    Text = $"{lvl}%",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                    Width = 30,
                    TextAlignment = TextAlignment.Right
                };
                Canvas.SetLeft(lbl, 1);
                Canvas.SetTop(lbl, gy - 7);
                cpuChartCanvas.Children.Add(lbl);
            }

            var samples = cpuHistory.ToArray();
            if (samples.Length > 1)
            {
                double stepX = plotW / (samples.Length - 1);
                var linePts = new PointCollection();
                var polyPts = new PointCollection { new Point(plotLeft, plotBottom) };
                for (int i = 0; i < samples.Length; i++)
                {
                    double px = plotLeft + i * stepX;
                    double py = Math.Clamp(plotBottom - (Math.Clamp(samples[i], 0, 100) / 100.0) * plotH, plotTop, plotBottom);
                    var pt = new Point(px, py);
                    linePts.Add(pt);
                    polyPts.Add(pt);
                }
                polyPts.Add(new Point(plotRight, plotBottom));

                cpuChartCanvas.Children.Add(new Polygon
                {
                    Points = polyPts,
                    Fill = new LinearGradientBrush(Color.FromArgb(65, 34, 197, 94), Color.FromArgb(6, 34, 197, 94), new Point(0, 0), new Point(0, 1))
                });
                cpuChartCanvas.Children.Add(new Polyline
                {
                    Points = linePts,
                    Stroke = new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                    StrokeThickness = 1.9,
                    StrokeLineJoin = PenLineJoin.Round
                });
            }
        }

        private void BuildMemorySlotsBar()
        {
            memorySlotsHost.Children.Clear();
            if (currentReport == null) return;

            bool isAr = LocalizationService.IsArabic;
            bool isDark = WpfTheme.IsDarkMode;
            double totalInstalledGB = Math.Max(0.1, currentReport.RamSlots.Where(s => s.IsPopulated).Sum(s => s.CapacityGB));

            var outer = new StackPanel();

            // Header bar + architecture pill
            var hdrGrid = new Grid { Margin = new Thickness(2, 0, 2, 10) };
            hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            string hdrTitle = isAr
                ? $"شرائح الذاكرة الفعلية  ({currentReport.PopulatedSlots} / {Math.Max(currentReport.PopulatedSlots, currentReport.TotalPhysicalSlots)} مستخدمة)"
                : $"PHYSICAL MEMORY SLOTS  ({currentReport.PopulatedSlots} OF {Math.Max(currentReport.PopulatedSlots, currentReport.TotalPhysicalSlots)} POPULATED)";
            hdrGrid.Children.Add(new TextBlock
            {
                Text = hdrTitle,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = WpfTheme.TextSecondaryBrush,
                VerticalAlignment = VerticalAlignment.Center
            });

            string archText = isAr ? currentReport.MemoryArchitectureSummaryAr : currentReport.MemoryArchitectureSummary;
            if (!string.IsNullOrWhiteSpace(archText))
            {
                bool hasLp = currentReport.RamSlots.Any(x => x.IsLpddr);
                Color badgeAccent = currentReport.IsMixedMemory
                    ? Color.FromRgb(245, 158, 11)
                    : (hasLp ? Color.FromRgb(168, 85, 247) : WpfTheme.ColorPrimary);
                var pill = new Border
                {
                    CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(Color.FromArgb((byte)(isDark ? 38 : 24), badgeAccent.R, badgeAccent.G, badgeAccent.B)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(95, badgeAccent.R, badgeAccent.G, badgeAccent.B)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(10, 2, 10, 2)
                };
                pill.Child = new TextBlock
                {
                    Text = archText,
                    FontSize = 11,
                    FontWeight = FontWeights.Medium,
                    Foreground = new SolidColorBrush(badgeAccent)
                };
                Grid.SetColumn(pill, 1);
                hdrGrid.Children.Add(pill);
            }
            outer.Children.Add(hdrGrid);

            // Cards Grid
            var slotsGrid = new Grid();
            int count = Math.Max(1, currentReport.RamSlots.Count);
            for (int i = 0; i < count; i++)
                slotsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (int i = 0; i < currentReport.RamSlots.Count; i++)
            {
                var slot = currentReport.RamSlots[i];
                Color accent = !slot.IsPopulated
                    ? (isDark ? Color.FromRgb(100, 116, 139) : Color.FromRgb(148, 163, 184))
                    : (slot.IsLpddr ? Color.FromRgb(168, 85, 247)
                        : (slot.MemoryGeneration.StartsWith("DDR5", StringComparison.OrdinalIgnoreCase)
                            ? Color.FromRgb(14, 165, 233)
                            : WpfTheme.ColorPrimary));

                var card = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Background = WpfTheme.CardSubBrush,
                    BorderBrush = WpfTheme.BorderBrush,
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(i == 0 ? 0 : 6, 0, i == currentReport.RamSlots.Count - 1 ? 0 : 6, 0),
                    Padding = new Thickness(12, 10, 12, 10)
                };
                var cStack = new StackPanel();

                var topRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                topRow.Children.Add(new TextBlock
                {
                    Text = isAr ? $"المنفذ {slot.SlotIndex} • {slot.Locator}" : $"Slot {slot.SlotIndex} • {slot.Locator}",
                    FontSize = 12,
                    FontWeight = FontWeights.Medium,
                    Foreground = WpfTheme.TextPrimaryBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });

                var genPill = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(slot.IsPopulated ? accent : (isDark ? Color.FromRgb(51, 65, 85) : Color.FromRgb(203, 213, 225))),
                    Padding = new Thickness(8, 2, 8, 2)
                };
                genPill.Child = new TextBlock
                {
                    Text = slot.IsPopulated ? slot.MemoryGeneration.ToUpperInvariant() : (isAr ? "فارغ" : "EMPTY"),
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = slot.IsPopulated ? Brushes.White : WpfTheme.TextSecondaryBrush
                };
                Grid.SetColumn(genPill, 1);
                topRow.Children.Add(genPill);
                cStack.Children.Add(topRow);

                cStack.Children.Add(new TextBlock
                {
                    Text = slot.IsPopulated ? $"{slot.CapacityGB:N1} GB • {slot.SpeedDisplay}" : (isAr ? "منفذ شاغر — قابل للترقية" : "Empty Socket — Ready for Upgrade"),
                    FontSize = 13.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = slot.IsPopulated ? WpfTheme.TextPrimaryBrush : WpfTheme.TextSecondaryBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 0, 4)
                });

                string formLoc = slot.IsSoldered ? (isAr ? "مدمجة على اللوحة" : "Onboard (Soldered)") : slot.FormFactorName;
                string subLine = slot.IsPopulated ? $"{formLoc} • {(slot.ConfiguredVoltageMv > 0 ? slot.VoltageDisplay : slot.DataWidthBits + "-bit")}" : formLoc;
                cStack.Children.Add(new TextBlock
                {
                    Text = subLine,
                    FontSize = 11.5,
                    Foreground = WpfTheme.TextSecondaryBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });

                string vendorLine = slot.IsPopulated
                    ? string.Join(" • ", new[] { slot.Manufacturer, slot.PartNumber, slot.BankLabel }.Where(x => !string.IsNullOrWhiteSpace(x)))
                    : (string.IsNullOrWhiteSpace(slot.BankLabel) ? "-" : slot.BankLabel);
                cStack.Children.Add(new TextBlock
                {
                    Text = vendorLine,
                    FontSize = 11,
                    Foreground = WpfTheme.TextSecondaryBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 2, 0, 8)
                });

                // Capacity proportion bar
                var barTrack = new Grid { Height = 5 };
                barTrack.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(2.5),
                    Background = new SolidColorBrush(isDark ? Color.FromRgb(30, 41, 59) : Color.FromRgb(226, 232, 240))
                });
                if (slot.IsPopulated)
                {
                    double ratio = Math.Clamp(slot.CapacityGB / totalInstalledGB, 0.12, 1.0);
                    var fillGrid = new Grid();
                    fillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ratio, GridUnitType.Star) });
                    fillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 1.0 - ratio), GridUnitType.Star) });
                    fillGrid.Children.Add(new Border { CornerRadius = new CornerRadius(2.5), Background = new SolidColorBrush(accent) });
                    barTrack.Children.Add(fillGrid);
                }
                cStack.Children.Add(barTrack);

                card.Child = cStack;
                Grid.SetColumn(card, i);
                slotsGrid.Children.Add(card);
            }

            outer.Children.Add(slotsGrid);
            memorySlotsHost.Children.Add(outer);
        }

        private void PromptExport()
        {
            bool isAr = LocalizationService.IsArabic;
            if (currentReport == null)
            {
                AppDialog.Show(this,
                    isAr ? "لا تزال تفاصيل العتاد قيد الفحص والتحميل، يرجى الانتظار لحظة." : "Hardware configuration is still being loaded. Please wait a moment.",
                    isAr ? "التقرير غير جاهز" : "Report Not Ready",
                    AppDialogTone.Info);
                return;
            }

            var sfd = new SaveFileDialog
            {
                Title = isAr ? "تصدير تقرير معلومات النظام" : "Export System Information Report",
                Filter = isAr ? "تقرير HTML (*.html)|*.html|تقرير نصي (*.txt)|*.txt" : "HTML Report (*.html)|*.html|Text Report (*.txt)|*.txt",
                FileName = $"System Information Report - {Environment.MachineName}.html"
            };
            if (sfd.ShowDialog(this) != true) return;

            try
            {
                if (sfd.FilterIndex == 1 || sfd.FileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                    HardwareInfoService.ExportToHtml(currentReport, sfd.FileName);
                else
                    HardwareInfoService.ExportToText(currentReport, sfd.FileName);

                if (AppDialog.Confirm(this,
                    isAr ? $"تم تصدير التقرير بنجاح إلى:\n{sfd.FileName}\n\nهل تريد فتحه الآن؟" : $"Report successfully exported to:\n{sfd.FileName}\n\nWould you like to open it now?",
                    isAr ? "تم التصدير بنجاح" : "Export Succeeded",
                    AppDialogTone.Success))
                {
                    Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                DiagnosticLogger.LogError(nameof(ToolboxSystemInfoWindow), "Export failed", ex);
                AppDialog.Show(this, ex.Message, isAr ? "خطأ في التصدير" : "Export Error", AppDialogTone.Error);
            }
        }

        private void CopyCurrentCategoryToClipboard()
        {
            if (currentReport == null || currentCategoryIndex >= currentReport.Categories.Count) return;
            var cat = currentReport.Categories[currentCategoryIndex];
            var sb = new StringBuilder();
            sb.AppendLine($"[ {cat.Title.ToUpperInvariant()} ]");
            sb.AppendLine(new string('-', 50));
            foreach (var grp in cat.Groups)
            {
                sb.AppendLine($"{grp.GroupName}:");
                foreach (var item in grp.Items)
                    sb.AppendLine($"  {item.Name,-28} : {item.Value}");
                sb.AppendLine();
            }

            try
            {
                Clipboard.SetText(sb.ToString());
                bool isAr = LocalizationService.IsArabic;
                AppDialog.Show(this,
                    isAr ? $"تم نسخ مواصفات '{cat.Title}' إلى الحافظة." : $"Specs for '{cat.Title}' copied to clipboard.",
                    isAr ? "تم النسخ" : "Copied",
                    AppDialogTone.Success);
            }
            catch { }
        }

        public override void RefreshTheme()
        {
            base.RefreshTheme();
            headerBar.Background = WpfTheme.SidebarBrush;
            headerBar.BorderBrush = WpfTheme.BorderBrush;
            sidebarBorder.Background = WpfTheme.SidebarBrush;
            sidebarBorder.BorderBrush = WpfTheme.BorderBrush;
            footerBar.Background = WpfTheme.SidebarBrush;
            footerBar.BorderBrush = WpfTheme.BorderBrush;
            lblTitle.Foreground = WpfTheme.TextPrimaryBrush;
            lblSub.Foreground = WpfTheme.TextSecondaryBrush;
            lblLoading.Foreground = WpfTheme.TextSecondaryBrush;
            cardSpecs.Background = WpfTheme.CardBrush;
            cardSpecs.BorderBrush = WpfTheme.BorderBrush;
            cardBottomMetrics.Background = WpfTheme.CardBrush;
            cardBottomMetrics.BorderBrush = WpfTheme.BorderBrush;
            StyleDataGridForTheme(gridSpecs);
            gridSpecs.RowStyle = CreateSpecRowStyle();
            ApplySecondaryButtonStyle(btnCopy);
            btnCopy.Content = WpfIcons.CreateIconTextContent("Copy", LocalizationService.IsArabic ? "نسخ المواصفات" : "Copy Specs", btnCopy.Foreground, 14, 6);
            ApplySecondaryButtonStyle(btnClose);
            UpdateNavStyles();
            if (memorySlotsHost.Visibility == Visibility.Visible) BuildMemorySlotsBar();
        }
    }
}
