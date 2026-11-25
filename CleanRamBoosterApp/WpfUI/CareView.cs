using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CleanRamBoosterApp.Models;
using CleanRamBoosterApp.Services;
using CleanRamBoosterApp.UI;

namespace CleanRamBoosterApp.WpfUI
{
    public class CareView : UserControl
    {
        private readonly CareService careService;
        private List<CareAppGroup> scannedGroups = new();
        private bool isBusy;
        private DateTime? lastScanTime;
        private double lastScanSeconds;

        // UI Controls
        private readonly TextBlock lblHead;
        private readonly TextBlock lblSub;
        private readonly Button btnScan;
        private readonly Button btnClean;
        private readonly Button btnSelectAll;
        private readonly Button btnDeselectAll;
        private readonly Button btnExportReport;
        private readonly CheckBox chkRestorePoint;
        private readonly ProgressBar progressBar;
        private readonly TextBlock lblStatus;
        private readonly StackPanel itemsPanel;
        private readonly ScrollViewer scrollViewer;

        public event Action<string>? StatusMessage;

        public CareView(CareService service)
        {
            careService = service;

            var rootGrid = new Grid
            {
                Margin = new Thickness(24, 20, 24, 20)
            };
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Top Controls Bar
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Progress
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Cards List

            // 1. Header
            var headRow = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var headBadge = WpfIcons.CreateHeaderBadge("Shield", Color.FromRgb(16, 185, 129), 42, 22);
            headBadge.Margin = new Thickness(0, 0, 14, 0);
            Grid.SetColumn(headBadge, 0);
            headRow.Children.Add(headBadge);

            var headStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            lblHead = new TextBlock
            {
                Text = UITheme.StripLeadingIcon(LocalizationService.T("CareHeader", "🛡️ Complete PC Care & Cleanup")),
                FontSize = 20,
                FontWeight = FontWeights.SemiBold
            };
            lblSub = new TextBlock
            {
                Text = LocalizationService.T("CareSub", "Deep clean junk files, temporary caches, registry remnants, and boost system responsiveness."),
                FontSize = 13,
                Margin = new Thickness(0, 3, 0, 0)
            };
            headStack.Children.Add(lblHead);
            headStack.Children.Add(lblSub);
            Grid.SetColumn(headStack, 1);
            headRow.Children.Add(headStack);
            Grid.SetRow(headRow, 0);
            rootGrid.Children.Add(headRow);

            // 2. Top Controls Bar
            var topBar = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var scanStack = new StackPanel { Orientation = Orientation.Horizontal };
            btnScan = new Button
            {
                Height = 38,
                Padding = new Thickness(18, 0, 18, 0),
                FontWeight = FontWeights.Medium,
                FontSize = 13,
                Foreground = Brushes.White,
                Cursor = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 10, 0)
            };
            btnScan.Background = new LinearGradientBrush(Color.FromRgb(59, 130, 246), Color.FromRgb(37, 99, 235), new Point(0, 0), new Point(1, 1));
            btnScan.Click += async (s, e) => await StartScanAsync(manualTrigger: true);

            btnClean = new Button
            {
                Height = 38,
                Padding = new Thickness(18, 0, 18, 0),
                FontWeight = FontWeights.Medium,
                FontSize = 13,
                Foreground = Brushes.White,
                Cursor = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(0),
                IsEnabled = false
            };
            btnClean.Background = new LinearGradientBrush(Color.FromRgb(16, 185, 129), Color.FromRgb(5, 150, 105), new Point(0, 0), new Point(1, 1));
            btnClean.Click += async (s, e) => await StartCleanAsync();

            scanStack.Children.Add(btnScan);
            scanStack.Children.Add(btnClean);
            Grid.SetColumn(scanStack, 0);
            topBar.Children.Add(scanStack);

            var selStack = new StackPanel { Orientation = Orientation.Horizontal };
            btnSelectAll = new Button
            {
                Content = LocalizationService.T("BtnSelectAll", "Select All"),
                Height = 32,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 8, 0),
                FontSize = 12.5,
                FontWeight = FontWeights.Normal
            };
            ApplySecondaryButtonStyle(btnSelectAll);
            btnSelectAll.Click += (s, e) => SetAllSelection(true);

            btnDeselectAll = new Button
            {
                Content = LocalizationService.T("BtnDeselectAll", "Deselect All"),
                Height = 32,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 8, 0),
                FontSize = 12.5,
                FontWeight = FontWeights.Normal
            };
            ApplySecondaryButtonStyle(btnDeselectAll);
            btnDeselectAll.Click += (s, e) => SetAllSelection(false);

            btnExportReport = new Button
            {
                Height = 32,
                Padding = new Thickness(12, 0, 12, 0),
                FontSize = 12.5,
                FontWeight = FontWeights.Normal
            };
            ApplySecondaryButtonStyle(btnExportReport);
            btnExportReport.Click += (s, e) => ExportCareReportHtml();

            selStack.Children.Add(btnSelectAll);
            selStack.Children.Add(btnDeselectAll);
            selStack.Children.Add(btnExportReport);
            Grid.SetColumn(selStack, 3);
            topBar.Children.Add(selStack);

            Grid.SetRow(topBar, 1);
            rootGrid.Children.Add(topBar);

            // 3. Progress, Restore Point toggle & Status
            var progGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            progGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            progGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            progGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            chkRestorePoint = new CheckBox
            {
                Content = LocalizationService.T("CareCreateRestorePoint", "Create Windows System Restore Point before cleaning"),
                IsChecked = SettingsService.Instance.Current.CreateRestorePointBeforeCare,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 6)
            };
            chkRestorePoint.Click += (s, e) =>
                SettingsService.Instance.Update(st => st.CreateRestorePointBeforeCare = chkRestorePoint.IsChecked == true);
            Grid.SetRow(chkRestorePoint, 0);
            progGrid.Children.Add(chkRestorePoint);

            progressBar = new ProgressBar
            {
                Height = 5,
                Minimum = 0,
                Maximum = 100,
                Visibility = Visibility.Collapsed,
                IsIndeterminate = false,
                BorderThickness = new Thickness(0),
                Foreground = WpfTheme.PrimaryBrush,
                Background = WpfTheme.BorderBrush
            };
            Grid.SetRow(progressBar, 1);
            progGrid.Children.Add(progressBar);

            lblStatus = new TextBlock
            {
                FontSize = 12.5,
                Margin = new Thickness(0, 5, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Text = LocalizationService.IsArabic ? "جاهز للفحص الشامل..." : "Ready to scan for junk and optimization opportunities..."
            };
            Grid.SetRow(lblStatus, 2);
            progGrid.Children.Add(lblStatus);

            Grid.SetRow(progGrid, 2);
            rootGrid.Children.Add(progGrid);

            // 4. Items List in ScrollViewer
            scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            itemsPanel = new StackPanel();
            scrollViewer.Content = itemsPanel;

            Grid.SetRow(scrollViewer, 3);
            rootGrid.Children.Add(scrollViewer);

            Content = rootGrid;

            ApplyLanguage();
            RefreshTheme();

            // Auto initial scan in background
            Loaded += async (s, e) =>
            {
                if (scannedGroups.Count == 0 && !isBusy)
                {
                    await StartScanAsync(manualTrigger: false);
                }
            };
        }

        private async Task StartScanAsync(bool manualTrigger)
        {
            if (isBusy) return;
            isBusy = true;
            bool isAr = LocalizationService.IsArabic;

            btnScan.IsEnabled = false;
            btnClean.IsEnabled = false;
            btnSelectAll.IsEnabled = false;
            btnDeselectAll.IsEnabled = false;
            progressBar.Visibility = Visibility.Visible;
            progressBar.IsIndeterminate = false;
            progressBar.Value = 4;
            lblStatus.Foreground = WpfTheme.PrimaryBrush;
            lblStatus.Text = isAr
                ? "جاري بدء الفحص الشامل لجميع المتصفحات وملفات النظام وسلة المحذوفات..."
                : "Starting deep scan across browsers, system caches, Recycle Bin, and registry...";

            var sw = Stopwatch.StartNew();
            var progress = new Progress<CareScanProgress>(p =>
            {
                if (p.TotalSteps > 0)
                {
                    progressBar.Value = Math.Clamp(p.StepIndex * 100.0 / p.TotalSteps, 4, 100);
                }
                string foundSize = WpfTheme.FormatBytes(p.BytesFoundSoFar);
                lblStatus.Text = isAr
                    ? $"جاري الفحص ({p.StepIndex}/{p.TotalSteps}): {p.CurrentArea} • تم العثور على {p.FilesFoundSoFar:N0} ملف ({foundSize})..."
                    : $"Deep scanning ({p.StepIndex}/{p.TotalSteps}): {p.CurrentArea} • Found {p.FilesFoundSoFar:N0} files ({foundSize})...";
            });

            try
            {
                var expandedBefore = new HashSet<string>(
                    scannedGroups.Where(g => g.IsExpanded).Select(g => g.AppName),
                    StringComparer.OrdinalIgnoreCase);

                scannedGroups = await careService.ScanAllHierarchicalAsync(progress);
                sw.Stop();
                lastScanTime = DateTime.Now;
                lastScanSeconds = sw.Elapsed.TotalSeconds;

                foreach (var g in scannedGroups)
                {
                    if (expandedBefore.Contains(g.AppName))
                        g.IsExpanded = true;
                }

                // Ensure the user visibly sees the scan finish cleanly when clicking Start Deep Scan manually
                if (manualTrigger && sw.ElapsedMilliseconds < 280)
                {
                    progressBar.Value = 100;
                    await Task.Delay((int)(280 - sw.ElapsedMilliseconds));
                }

                RenderGroups();
                UpdateCleanButtonState();

                if (manualTrigger)
                {
                    long totalBytes = scannedGroups.Sum(g => g.AllScannedBytes);
                    int totalFiles = scannedGroups.Sum(g => g.AllScannedFiles);
                    int pendingTweaks = scannedGroups.Sum(g => g.PendingTweaksCount);
                    string logMsg = isAr
                        ? $"[الفحص الشامل] اكتمل خلال {lastScanSeconds:0.0}ث: {totalFiles:N0} ملف ({WpfTheme.FormatBytes(totalBytes)})، و {pendingTweaks} تحسين نظام."
                        : $"[Care & Cleanup] Deep Scan finished in {lastScanSeconds:0.0}s: {totalFiles:N0} files ({WpfTheme.FormatBytes(totalBytes)}), {pendingTweaks} system tweak(s) ready.";
                    StatusMessage?.Invoke(logMsg);
                }
            }
            finally
            {
                progressBar.Visibility = Visibility.Collapsed;
                btnScan.IsEnabled = true;
                btnSelectAll.IsEnabled = true;
                btnDeselectAll.IsEnabled = true;
                btnClean.IsEnabled = HasSelectedWork();
                isBusy = false;
            }
        }

        public void EnsureLoadedSynchronously()
        {
            if (scannedGroups.Count == 0)
            {
                scannedGroups = careService.ScanAllHierarchical();
                lastScanTime = DateTime.Now;
                lastScanSeconds = 0.1;
                RenderGroups();
                UpdateCleanButtonState();
            }
        }

        private bool HasSelectedWork()
            => scannedGroups.Any(g => g.IsSelected && g.SubItems.Any(s => s.IsSelected && (s.SizeBytes > 0 || s.FileCount > 0 || (s.IsSystemTweak && !s.IsAlreadyApplied))));

        private async Task StartCleanAsync()
        {
            if (isBusy) return;
            var selectedGroups = scannedGroups.Where(g => g.IsSelected && g.SubItems.Any(s => s.IsSelected)).ToList();
            if (selectedGroups.Count == 0) return;

            bool needsAdmin = selectedGroups.Any(g => g.SubItems.Any(s => s.IsSelected && s.RequiresAdmin));
            if (needsAdmin && !AdminPrivilegeService.IsRunningAsAdmin())
            {
                if (!AdminPrivilegeService.EnsureAdminOrPrompt(this, "Care & Cleanup", "الفحص والتنظيف الشامل"))
                    return;
            }

            isBusy = true;
            bool isAr = LocalizationService.IsArabic;
            btnScan.IsEnabled = false;
            btnClean.IsEnabled = false;
            btnSelectAll.IsEnabled = false;
            btnDeselectAll.IsEnabled = false;
            progressBar.Visibility = Visibility.Visible;
            progressBar.IsIndeterminate = true;
            lblStatus.Foreground = WpfTheme.PrimaryBrush;
            lblStatus.Text = isAr ? "جاري تنظيف وإصلاح العناصر المحددة..." : "Cleaning and optimizing selected components...";

            CareCleanSummary summary;
            string restorePointNote = string.Empty;
            try
            {
                if (chkRestorePoint.IsChecked == true)
                {
                    lblStatus.Text = isAr
                        ? "جاري إنشاء نقطة استعادة للنظام قبل التنظيف..."
                        : "Creating Windows System Restore Point before cleaning...";
                    var rp = await Task.Run(() => SystemRestoreService.CreateRestorePoint("PC Boost Care & Cleanup Checkpoint"));
                    restorePointNote = rp.Success
                        ? (isAr ? "\nنقطة استعادة النظام: تم الإنشاء بنجاح" : "\nSystem Restore Point: Created")
                        : $"\n{(isAr ? "ملاحظة نقطة الاستعادة" : "Restore Point note")}: {rp.Message}";
                    lblStatus.Text = isAr ? "جاري تنظيف وإصلاح العناصر المحددة..." : "Cleaning and optimizing selected components...";
                }

                summary = await Task.Run(() => careService.CleanSelectedGroups(selectedGroups));
            }
            finally
            {
                progressBar.IsIndeterminate = false;
                progressBar.Visibility = Visibility.Collapsed;
                btnScan.IsEnabled = true;
                btnSelectAll.IsEnabled = true;
                btnDeselectAll.IsEnabled = true;
                isBusy = false;
            }

            string formattedFreed = WpfTheme.FormatBytes(summary.ActualFreedBytes);
            int skippedTotal = summary.LockedSkipped + summary.AccessDenied;
            string tweakNote = summary.TweaksApplied > 0
                ? (isAr ? $"\nتحسينات النظام المطبقة: {summary.TweaksApplied}" : $"\nSystem optimizations applied: {summary.TweaksApplied}")
                : string.Empty;
            string skipNote = skippedTotal > 0
                ? (isAr
                    ? $"\nملفات قيد الاستخدام / محمية تم تخطيها بأمان: {skippedTotal}"
                    : $"\nIn-use / system-protected files safely skipped: {skippedTotal}")
                : string.Empty;

            string msg = isAr
                ? $"اكتملت عملية التنظيف!\nتم تحرير: {formattedFreed} ({summary.FilesDeleted:N0} ملف) عبر {summary.CleanedCategories} فئات.{tweakNote}{skipNote}{restorePointNote}"
                : $"Cleanup Complete!\nFreed: {formattedFreed} ({summary.FilesDeleted:N0} files) across {summary.CleanedCategories} categories.{tweakNote}{skipNote}{restorePointNote}";

            string logLine = isAr
                ? $"[التنظيف الشامل] تم تحرير {formattedFreed} ({summary.FilesDeleted:N0} ملف، {summary.TweaksApplied} تحسين، تم تخطي {skippedTotal} مقفل)."
                : $"[Care & Cleanup] Freed {formattedFreed} ({summary.FilesDeleted:N0} files, {summary.TweaksApplied} tweaks, {skippedTotal} locked/skipped).";
            StatusMessage?.Invoke(logLine);

            AppDialogTone tone = (summary.FilesDeleted > 0 || summary.TweaksApplied > 0 || skippedTotal == 0)
                ? AppDialogTone.Success
                : AppDialogTone.Warning;
            AppDialog.Show(this, msg, LocalizationService.T("AppTitle", "PC Boost"), tone);

            await StartScanAsync(manualTrigger: false);
        }

        private void RenderGroups()
        {
            itemsPanel.Children.Clear();
            bool isAr = LocalizationService.IsArabic;

            foreach (var g in scannedGroups)
            {
                var card = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    Background = WpfTheme.CardBrush,
                    BorderBrush = WpfTheme.BorderBrush,
                    Margin = new Thickness(0, 0, 0, 10),
                    Padding = new Thickness(14, 12, 14, 12)
                };

                var cardStack = new StackPanel();

                var cardGrid = new Grid();
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // CheckBox
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Icon Badge
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Info
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Stats Badge
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Expand Toggle

                var chk = new CheckBox
                {
                    IsChecked = g.IsSelected,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0)
                };
                chk.Click += (s, e) =>
                {
                    bool target = chk.IsChecked == true;
                    g.IsSelected = target;
                    foreach (var sub in g.SubItems)
                    {
                        if (sub.IsSystemTweak && sub.IsAlreadyApplied)
                            sub.IsSelected = false;
                        else
                            sub.IsSelected = target;
                    }
                    RenderGroups();
                    UpdateCleanButtonState();
                };
                Grid.SetColumn(chk, 0);
                cardGrid.Children.Add(chk);

                var (iconKey, iconAccent) = ResolveCareGroupIcon(g);
                var groupBadge = WpfIcons.CreateHeaderBadge(iconKey, iconAccent, 36, 18);
                groupBadge.Margin = new Thickness(0, 0, 14, 0);
                Grid.SetColumn(groupBadge, 1);
                cardGrid.Children.Add(groupBadge);

                var infoStack = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                infoStack.MouseLeftButtonUp += (s, e) =>
                {
                    g.IsExpanded = !g.IsExpanded;
                    RenderGroups();
                };

                var titleText = new TextBlock
                {
                    Text = g.AppName,
                    FontWeight = FontWeights.Medium,
                    FontSize = 14.5,
                    Foreground = WpfTheme.TextPrimaryBrush
                };
                var descText = new TextBlock
                {
                    Text = g.Description,
                    FontSize = 12.5,
                    FontWeight = FontWeights.Normal,
                    Foreground = WpfTheme.TextSecondaryBrush,
                    Margin = new Thickness(0, 3, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                };
                infoStack.Children.Add(titleText);
                infoStack.Children.Add(descText);
                Grid.SetColumn(infoStack, 2);
                cardGrid.Children.Add(infoStack);

                // Accurate Stats Badge
                var (badgeLabel, badgeBg, badgeFg) = BuildGroupBadge(g, isAr);
                var badge = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Background = badgeBg,
                    Padding = new Thickness(11, 5, 11, 5),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 8, 0)
                };
                badge.Child = new TextBlock
                {
                    Text = badgeLabel,
                    FontWeight = FontWeights.Medium,
                    FontSize = 12,
                    Foreground = badgeFg
                };
                Grid.SetColumn(badge, 3);
                cardGrid.Children.Add(badge);

                var btnExpand = new Button
                {
                    Content = g.IsExpanded ? "▾" : (isAr ? "◂" : "▸"),
                    Width = 28,
                    Height = 28,
                    FontSize = 13,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = WpfTheme.TextSecondaryBrush,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = isAr ? "عرض تفاصيل العناصر المفحوصة" : "Show scanned sub-items"
                };
                btnExpand.Click += (s, e) =>
                {
                    g.IsExpanded = !g.IsExpanded;
                    RenderGroups();
                };
                Grid.SetColumn(btnExpand, 4);
                cardGrid.Children.Add(btnExpand);

                cardStack.Children.Add(cardGrid);

                // Expandable sub-item breakdown showing exact folders/files/tweaks scanned
                if (g.IsExpanded && g.SubItems.Count > 0)
                {
                    var subPanel = new Border
                    {
                        Margin = new Thickness(28, 10, 0, 0),
                        Padding = new Thickness(12, 8, 12, 8),
                        CornerRadius = new CornerRadius(6),
                        Background = WpfTheme.CardSubBrush,
                        BorderBrush = WpfTheme.BorderBrush,
                        BorderThickness = new Thickness(1)
                    };
                    var subList = new StackPanel();
                    for (int i = 0; i < g.SubItems.Count; i++)
                    {
                        var sub = g.SubItems[i];
                        var row = new Grid
                        {
                            Margin = new Thickness(0, i == 0 ? 0 : 6, 0, 0)
                        };
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                        var subChk = new CheckBox
                        {
                            IsChecked = sub.IsSelected,
                            IsEnabled = !(sub.IsSystemTweak && sub.IsAlreadyApplied),
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(0, 0, 10, 0)
                        };
                        subChk.Click += (s, e) =>
                        {
                            sub.IsSelected = subChk.IsChecked == true;
                            g.IsSelected = g.SubItems.Any(x => x.IsSelected);
                            RenderGroups();
                            UpdateCleanButtonState();
                        };
                        Grid.SetColumn(subChk, 0);
                        row.Children.Add(subChk);

                        var subInfo = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                        subInfo.Children.Add(new TextBlock
                        {
                            Text = sub.Name,
                            FontSize = 12.5,
                            Foreground = WpfTheme.TextPrimaryBrush,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        });
                        if (!string.IsNullOrWhiteSpace(sub.TargetPath))
                        {
                            subInfo.Children.Add(new TextBlock
                            {
                                Text = sub.TargetPath,
                                FontSize = 11,
                                Foreground = WpfTheme.TextSecondaryBrush,
                                FlowDirection = FlowDirection.LeftToRight,
                                HorizontalAlignment = isAr ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                                TextTrimming = TextTrimming.CharacterEllipsis
                            });
                        }
                        Grid.SetColumn(subInfo, 1);
                        row.Children.Add(subInfo);

                        string subMetric;
                        Brush subMetricBrush;
                        if (sub.IsSystemTweak)
                        {
                            subMetric = sub.IsAlreadyApplied
                                ? (isAr ? "✓ مُحسَّن" : "✓ Applied")
                                : (isAr ? "جاهز للتطبيق" : "Ready");
                            subMetricBrush = sub.IsAlreadyApplied ? WpfTheme.SuccessBrush : new SolidColorBrush(WpfTheme.ColorWarning);
                        }
                        else if (sub.SizeBytes > 0 || sub.FileCount > 0)
                        {
                            subMetric = isAr
                                ? $"{WpfTheme.FormatBytes(sub.SizeBytes)} ({sub.FileCount:N0} ملف)"
                                : $"{WpfTheme.FormatBytes(sub.SizeBytes)} ({sub.FileCount:N0} files)";
                            subMetricBrush = WpfTheme.PrimaryBrush;
                        }
                        else
                        {
                            subMetric = isAr ? "نظيف (0 B)" : "Clean (0 B)";
                            subMetricBrush = WpfTheme.TextSecondaryBrush;
                        }

                        var subVal = new TextBlock
                        {
                            Text = subMetric,
                            FontSize = 11.5,
                            FontWeight = FontWeights.Medium,
                            Foreground = subMetricBrush,
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(10, 0, 0, 0)
                        };
                        Grid.SetColumn(subVal, 2);
                        row.Children.Add(subVal);

                        subList.Children.Add(row);
                    }
                    subPanel.Child = subList;
                    cardStack.Children.Add(subPanel);
                }

                card.Child = cardStack;
                itemsPanel.Children.Add(card);
            }
        }

        private static (string Label, Brush Bg, Brush Fg) BuildGroupBadge(CareAppGroup g, bool isAr)
        {
            bool isDark = WpfTheme.IsDarkMode;
            if (g.Category == CareCategory.SystemOptimization)
            {
                int pending = g.PendingTweaksCount;
                int applied = g.AppliedTweaksCount;
                int total = g.SubItems.Count;
                if (pending > 0)
                {
                    string lbl = isAr ? $"{pending} تحسين جاهز" : $"{pending} tweak(s) ready";
                    return (
                        lbl,
                        new SolidColorBrush(Color.FromArgb((byte)(isDark ? 45 : 28), 245, 158, 11)),
                        new SolidColorBrush(isDark ? Color.FromRgb(251, 191, 36) : Color.FromRgb(180, 83, 9)));
                }
                string optLbl = isAr ? $"✓ مُحسَّن ({applied}/{total})" : $"✓ Optimized ({applied}/{total})";
                return (
                    optLbl,
                    new SolidColorBrush(Color.FromArgb((byte)(isDark ? 40 : 25), 16, 185, 129)),
                    WpfTheme.SuccessBrush);
            }

            long bytes = g.IsSelected ? g.TotalBytes : g.AllScannedBytes;
            int files = g.IsSelected ? g.TotalFiles : g.AllScannedFiles;
            if (bytes > 0 || files > 0)
            {
                string lbl = isAr
                    ? $"{WpfTheme.FormatBytes(bytes)} • {files:N0} ملف"
                    : $"{WpfTheme.FormatBytes(bytes)} • {files:N0} files";
                return (
                    lbl,
                    new SolidColorBrush(Color.FromArgb((byte)(isDark ? 50 : 30), 59, 130, 246)),
                    WpfTheme.PrimaryBrush);
            }

            return (
                isAr ? "نظيف (0 B)" : "Clean (0 B)",
                new SolidColorBrush(Color.FromArgb((byte)(isDark ? 36 : 22), 16, 185, 129)),
                WpfTheme.SuccessBrush);
        }

        private static (string key, Color accent) ResolveCareGroupIcon(CareAppGroup g)
        {
            return g.IconEmoji switch
            {
                "🌐" => ("Globe", Color.FromRgb(59, 130, 246)),
                "🦊" => ("Globe", Color.FromRgb(249, 115, 22)),
                "📁" => ("Folder", Color.FromRgb(59, 130, 246)),
                "🧹" => ("Trash", Color.FromRgb(245, 158, 11)),
                "⚙️" => ("Sliders", Color.FromRgb(139, 92, 246)),
                "⚡" => ("Lightning", Color.FromRgb(245, 158, 11)),
                "📦" => ("Box", Color.FromRgb(59, 130, 246)),
                "💥" => ("Alert", Color.FromRgb(239, 68, 68)),
                "🗑️" => ("Trash", Color.FromRgb(239, 68, 68)),
                "🚀" => ("Rocket", Color.FromRgb(16, 185, 129)),
                _ => ("Folder", Color.FromRgb(59, 130, 246))
            };
        }

        private void SetAllSelection(bool select)
        {
            foreach (var g in scannedGroups)
            {
                foreach (var sub in g.SubItems)
                {
                    if (sub.IsSystemTweak && sub.IsAlreadyApplied)
                        sub.IsSelected = false;
                    else
                        sub.IsSelected = select;
                }
                g.IsSelected = select && g.SubItems.Any(s => s.IsSelected);
            }
            RenderGroups();
            UpdateCleanButtonState();
        }

        private void UpdateCleanButtonState()
        {
            bool enabled = HasSelectedWork();
            btnClean.IsEnabled = enabled;
            bool isDark = WpfTheme.IsDarkMode;
            Brush fg = enabled
                ? Brushes.White
                : new SolidColorBrush(isDark ? Color.FromRgb(148, 163, 184) : Color.FromRgb(100, 116, 139));
            btnClean.Foreground = fg;
            btnClean.Content = WpfIcons.CreateIconTextContent("Sparkle", UITheme.StripLeadingIcon(LocalizationService.T("CareBtnClean", "✨ CLEAN & REPAIR SELECTED")), fg, 16, 8);
            UpdateStatusLabel();
        }

        private void UpdateStatusLabel()
        {
            bool isAr = LocalizationService.IsArabic;
            long selectedBytes = scannedGroups.Where(g => g.IsSelected).Sum(g => g.TotalBytes);
            int selectedFiles = scannedGroups.Where(g => g.IsSelected).Sum(g => g.TotalFiles);
            int selectedTweaks = scannedGroups.Where(g => g.IsSelected).Sum(g => g.SelectedPendingTweaksCount);
            int selectedCategories = scannedGroups.Count(g => g.IsSelected && g.SubItems.Any(s => s.IsSelected));

            lblStatus.Foreground = WpfTheme.TextSecondaryBrush;

            string scanPrefix = string.Empty;
            if (lastScanTime.HasValue)
            {
                string t = lastScanTime.Value.ToString("HH:mm:ss");
                scanPrefix = isAr
                    ? $"✓ آخر فحص {t} ({lastScanSeconds:0.0}ث) • "
                    : $"✓ Deep scan at {t} ({lastScanSeconds:0.0}s) • ";
            }

            string tweakSuffix = selectedTweaks > 0
                ? (isAr ? $" + {selectedTweaks} تحسين" : $" + {selectedTweaks} tweak(s)")
                : string.Empty;

            if (isAr)
            {
                lblStatus.Text = $"{scanPrefix}المحدد: {selectedCategories} فئات ({selectedFiles:N0} ملف • {WpfTheme.FormatBytes(selectedBytes)}{tweakSuffix}) — انقر على أي بطاقة لعرض التفاصيل.";
            }
            else
            {
                lblStatus.Text = $"{scanPrefix}Selected: {selectedCategories} categories ({selectedFiles:N0} files • {WpfTheme.FormatBytes(selectedBytes)}{tweakSuffix}) — click any card to inspect details.";
            }
        }

        private static void ApplySecondaryButtonStyle(Button btn)
        {
            bool isDark = WpfTheme.IsDarkMode;
            btn.Background = new SolidColorBrush(isDark ? Color.FromRgb(33, 38, 45) : Color.FromRgb(241, 245, 249));
            btn.Foreground = new SolidColorBrush(isDark ? Color.FromRgb(201, 209, 217) : Color.FromRgb(30, 41, 59));
            btn.BorderBrush = new SolidColorBrush(isDark ? Color.FromRgb(48, 54, 61) : Color.FromRgb(226, 232, 240));
            btn.BorderThickness = new Thickness(1);
        }

        private void ExportCareReportHtml()
        {
            bool isAr = LocalizationService.IsArabic;
            if (scannedGroups.Count == 0)
            {
                AppDialog.Show(this,
                    isAr ? "يرجى تشغيل الفحص الشامل أولاً قبل تصدير التقرير." : "Please run a Deep Scan first before exporting a report.",
                    LocalizationService.T("AppTitle", "PC Boost"),
                    AppDialogTone.Info);
                return;
            }

            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "HTML Report (*.html)|*.html",
                FileName = $"PCBoost_CareReport_{DateTime.Now:yyyyMMdd_HHmmss}.html"
            };
            if (sfd.ShowDialog() != true) return;

            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>PC Boost Care Report</title>");
                sb.AppendLine("<style>body{font-family:'Segoe UI',Arial,sans-serif;margin:24px;background:#f8fafc;color:#0f172a}h1{margin-bottom:4px}table{width:100%;border-collapse:collapse;background:#fff;border:1px solid #cbd5e1;margin-top:16px}th,td{padding:8px 12px;border-bottom:1px solid #e2e8f0;text-align:left;font-size:13px}th{background:#1e293b;color:#fff}tr.grp td{background:#e0f2fe;font-weight:600;color:#0369a1}</style></head><body>");
                sb.AppendLine($"<h1>PC Boost - Care &amp; Cleanup Report</h1><p>Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} &bull; Machine: {System.Net.WebUtility.HtmlEncode(Environment.MachineName)}</p>");
                sb.AppendLine("<table><tr><th>Category / Item</th><th>Target Path</th><th>Details</th></tr>");
                foreach (var g in scannedGroups)
                {
                    sb.AppendLine($"<tr class=\"grp\"><td colspan=\"3\">{System.Net.WebUtility.HtmlEncode(g.AppName)} — {System.Net.WebUtility.HtmlEncode(g.Description)}</td></tr>");
                    foreach (var sub in g.SubItems)
                    {
                        string metric = sub.IsSystemTweak
                            ? (sub.IsAlreadyApplied ? "Optimized" : "Tweak Ready")
                            : $"{WpfTheme.FormatBytes(sub.SizeBytes)} ({sub.FileCount:N0} files)";
                        sb.AppendLine($"<tr><td>{System.Net.WebUtility.HtmlEncode(sub.Name)}</td><td>{System.Net.WebUtility.HtmlEncode(sub.TargetPath ?? "")}</td><td>{System.Net.WebUtility.HtmlEncode(metric)}</td></tr>");
                    }
                }
                sb.AppendLine("</table></body></html>");
                System.IO.File.WriteAllText(sfd.FileName, sb.ToString(), System.Text.Encoding.UTF8);

                if (AppDialog.Confirm(this,
                    isAr ? "تم حفظ تقرير الفحص بنجاح! هل ترغب بفتحه الآن؟" : "Care report exported successfully! Would you like to open it now?",
                    LocalizationService.T("AppTitle", "PC Boost"),
                    AppDialogTone.Success))
                {
                    Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                DiagnosticLogger.LogError(nameof(CareView), "Failed to export Care HTML report", ex);
                AppDialog.Show(this, ex.Message, LocalizationService.T("AppTitle", "PC Boost"), AppDialogTone.Error);
            }
        }

        public void ApplyLanguage()
        {
            lblHead.Text = UITheme.StripLeadingIcon(LocalizationService.T("CareHeader", "🛡️ Complete PC Care & Cleanup"));
            lblSub.Text = LocalizationService.T("CareSub", "Deep clean junk files, temporary caches, registry remnants, and boost system responsiveness.");
            btnScan.Content = WpfIcons.CreateIconTextContent("SearchFiles", UITheme.StripLeadingIcon(LocalizationService.T("CareBtnScan", "🔍 START DEEP SCAN")), Brushes.White, 16, 8);
            btnSelectAll.Content = LocalizationService.T("BtnSelectAll", "Select All");
            btnDeselectAll.Content = LocalizationService.T("BtnDeselectAll", "Deselect All");
            btnExportReport.Content = WpfIcons.CreateIconTextContent("ExportDoc", LocalizationService.T("BtnExportShort", "Export"), btnExportReport.Foreground, 14, 6);
            chkRestorePoint.Content = LocalizationService.T("CareCreateRestorePoint", "Create Windows System Restore Point before cleaning");
            UpdateCleanButtonState();

            if (scannedGroups.Count > 0)
            {
                scannedGroups = careService.ScanAllHierarchical();
                RenderGroups();
                UpdateCleanButtonState();
            }
            else
            {
                UpdateStatusLabel();
            }
        }

        public void RefreshTheme()
        {
            lblHead.Foreground = WpfTheme.TextPrimaryBrush;
            lblSub.Foreground = WpfTheme.TextSecondaryBrush;
            lblStatus.Foreground = WpfTheme.TextSecondaryBrush;
            chkRestorePoint.Foreground = WpfTheme.TextSecondaryBrush;

            ApplySecondaryButtonStyle(btnSelectAll);
            ApplySecondaryButtonStyle(btnDeselectAll);
            ApplySecondaryButtonStyle(btnExportReport);
            btnExportReport.Content = WpfIcons.CreateIconTextContent("ExportDoc", LocalizationService.T("BtnExportShort", "Export"), btnExportReport.Foreground, 14, 6);
            UpdateCleanButtonState();

            if (scannedGroups.Count > 0) RenderGroups();
        }
    }
}
