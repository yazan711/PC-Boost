using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using CleanRamBoosterApp.Models;
using CleanRamBoosterApp.Services;
using CleanRamBoosterApp.UI;

namespace CleanRamBoosterApp.WpfUI
{
    public partial class StartupView
    {
        private Button CreateTabButton(string tabKey)
        {
            var btn = new Button
            {
                Height = 31,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(0, 0, 5, 5),
                FontSize = 12,
                FontWeight = FontWeights.Normal,
                Tag = tabKey
            };
            ApplySecondaryButtonStyle(btn);
            btn.Click += (s, e) =>
            {
                activeFilterTab = tabKey;
                UpdateTabButtons();
                UpdateSummaryStrip();
                ApplyFilter();
            };
            return btn;
        }

        private void UpdateTabButtons()
        {
            SetTabBtnState(btnTabQuick, activeFilterTab == "Quick");
            SetTabBtnState(btnTabAll, activeFilterTab == "All");
            SetTabBtnState(btnTabEnabled, activeFilterTab == "Enabled");
            SetTabBtnState(btnTabDisabled, activeFilterTab == "Disabled");
            SetTabBtnState(btnTabApps, activeFilterTab == "Apps");
            SetTabBtnState(btnTabServices, activeFilterTab == "Services");
            SetTabBtnState(btnTabTasks, activeFilterTab == "Tasks");
            SetTabBtnState(btnTabStoreApps, activeFilterTab == "StoreApps");
            SetTabBtnState(btnTabSafe, activeFilterTab == "Safe");
            SetTabBtnState(btnTabUnknown, activeFilterTab == "Unknown");
            SetTabBtnState(btnTabIgnored, activeFilterTab == "Ignored");

            var amber = WpfTheme.IsDarkMode ? Color.FromRgb(251, 146, 60) : Color.FromRgb(217, 119, 6);
            int quickCount = allItems.Count(IsActiveQuickOptimizable);
            if (quickCount > 0 && activeFilterTab != "Quick")
            {
                btnTabQuick.BorderBrush = new SolidColorBrush(amber);
                btnTabQuick.Foreground = new SolidColorBrush(amber);
            }

            int unknownCount = allItems.Count(x => x.IsUnknown && !x.IsIgnored);
            if (unknownCount > 0 && activeFilterTab != "Unknown")
            {
                btnTabUnknown.BorderBrush = new SolidColorBrush(amber);
                btnTabUnknown.Foreground = new SolidColorBrush(amber);
            }

            btnDisableAllStore.Visibility = activeFilterTab == "StoreApps" ? Visibility.Visible : Visibility.Collapsed;
            UpdateCheckColumnMode();
        }

        private void UpdateCheckColumnMode()
        {
            if (colQuickCheck == null) return;
            bool isQuick = activeFilterTab == "Quick";
            colQuickCheck.Visibility = Visibility.Visible;
            if (lastCheckColumnQuickMode != isQuick)
            {
                lastCheckColumnQuickMode = isQuick;
                colQuickCheck.CellTemplate = CreateQuickCheckCellTemplate(isQuick);
            }
        }

        private static bool IsActiveQuickOptimizable(StartupItem x) =>
            x.IsEnabled && !x.IsIgnored && (x.Recommendation == StartupRecommendation.SafeToDisable || x.IsUnknown);

        private static void SetTabBtnState(Button btn, bool isActive)
        {
            if (isActive)
            {
                btn.Background = WpfTheme.PrimaryBrush;
                btn.Foreground = Brushes.White;
                btn.BorderThickness = new Thickness(0);
            }
            else
            {
                btn.BorderThickness = new Thickness(1);
                ApplySecondaryButtonStyle(btn);
            }
        }

        public void AttachMonitor(StartupMonitorService monitor)
        {
            attachedMonitor = monitor;
            if (allItems.Count > 0)
            {
                attachedMonitor.ApplyMonitorFlags(allItems);
                UpdateTabCounts();
                ApplyFilter();
                gridStartup.Items.Refresh();
            }
        }

        public (int OptimizableCount, int UnknownCount) GetSummaryCounts()
        {
            int opt = allItems.Count(IsActiveQuickOptimizable);
            int unk = allItems.Count > 0
                ? allItems.Count(x => x.IsUnknown && !x.IsIgnored)
                : (attachedMonitor?.GetUnknownCount() ?? 0);
            return (opt, unk);
        }

        public async Task LoadItemsAsync()
        {
            btnRefresh.IsEnabled = false;
            allItems = await startupService.GetAllStartupItemsAsync();
            attachedMonitor?.ApplyMonitorFlags(allItems);
            btnRefresh.IsEnabled = true;

            UpdateTabCounts();
            ApplyFilter();
        }

        public void EnsureLoadedSynchronously()
        {
            if (allItems.Count == 0)
            {
                allItems = startupService.GetAllStartupItems();
                attachedMonitor?.ApplyMonitorFlags(allItems);
                UpdateTabCounts();
                ApplyFilter();
            }
        }

        public void SelectFilterTab(string tabKey)
        {
            activeFilterTab = tabKey;
            UpdateTabButtons();
            UpdateSummaryStrip();
            ApplyFilter();
        }

        private void UpdateTabCounts()
        {
            int countQuick = allItems.Count(IsActiveQuickOptimizable);
            int countAll = allItems.Count;
            int countEnabled = allItems.Count(x => x.IsEnabled);
            int countDisabled = countAll - countEnabled;
            int countApps = allItems.Count(x => x.LocationType == StartupType.RegistryUser || x.LocationType == StartupType.RegistryMachine || x.LocationType == StartupType.RegistryWow64 || x.LocationType == StartupType.StartupFolderUser || x.LocationType == StartupType.StartupFolderCommon);
            int countServices = allItems.Count(x => x.LocationType == StartupType.WindowsService);
            int countTasks = allItems.Count(x => x.LocationType == StartupType.ScheduledTask);
            int countStoreApps = allItems.Count(x => x.LocationType == StartupType.MicrosoftStoreApp);
            int countSafe = allItems.Count(x => x.Recommendation == StartupRecommendation.SafeToDisable);
            int countUnknown = allItems.Count(x => x.IsUnknown && !x.IsIgnored);
            int countIgnored = allItems.Count(x => x.IsIgnored);

            bool isAr = LocalizationService.IsArabic;
            btnTabQuick.Content = isAr
                ? $"{LocalizationService.T("StartupTabQuick", "التحسين السريع")} ({countQuick})"
                : $"{LocalizationService.T("StartupTabQuick", "Quick Optimization")} ({countQuick})";
            btnTabAll.Content = isAr ? $"الكل ({countAll})" : $"All ({countAll})";
            btnTabEnabled.Content = isAr ? $"المفعلة ({countEnabled})" : $"Enabled ({countEnabled})";
            btnTabDisabled.Content = isAr ? $"المعطلة ({countDisabled})" : $"Disabled ({countDisabled})";
            btnTabApps.Content = isAr ? $"التطبيقات ({countApps})" : $"Apps ({countApps})";
            btnTabServices.Content = isAr ? $"الخدمات ({countServices})" : $"Services ({countServices})";
            btnTabTasks.Content = isAr ? $"المهام ({countTasks})" : $"Tasks ({countTasks})";
            btnTabStoreApps.Content = isAr ? $"تطبيقات متجر مايكروسوفت ({countStoreApps})" : $"Microsoft Store Apps ({countStoreApps})";
            btnTabSafe.Content = isAr ? $"آمن ({countSafe})" : $"Safe ({countSafe})";
            btnTabUnknown.Content = isAr
                ? $"{LocalizationService.T("StartupTabUnknown", "غير معروف")} ({countUnknown})"
                : $"{LocalizationService.T("StartupTabUnknown", "Unknown")} ({countUnknown})";
            btnTabIgnored.Content = isAr
                ? $"{LocalizationService.T("StartupTabIgnored", "تم تجاهلها")} ({countIgnored})"
                : $"{LocalizationService.T("StartupTabIgnored", "Ignored")} ({countIgnored})";

            UpdateTabButtons();
            UpdateSummaryStrip();
            InventoryUpdated?.Invoke();
        }

        private void UpdateSummaryStrip()
        {
            bool isAr = LocalizationService.IsArabic;
            bool isDark = WpfTheme.IsDarkMode;
            int unknownCount = allItems.Count(x => x.IsUnknown && !x.IsIgnored);
            int optimizableCount = allItems.Count(IsActiveQuickOptimizable);
            int selectedQuickCount = allItems.Count(x => IsActiveQuickOptimizable(x) && x.IsQuickSelected);
            int ignoredCount = allItems.Count(x => x.IsIgnored);

            var amberColor = isDark ? Color.FromRgb(251, 146, 60) : Color.FromRgb(217, 119, 6);
            var amberBrush = new SolidColorBrush(amberColor);
            bool attention = unknownCount > 0 || optimizableCount > 0;

            summaryIconHost.Content = WpfIcons.CreateIcon(
                attention ? "InfoCircle" : "CheckCircle",
                attention ? amberBrush : WpfTheme.SuccessBrush,
                19,
                1.85);

            lblSummaryStrip.Inlines.Clear();
            if (unknownCount > 0)
            {
                lblSummaryStrip.Inlines.Add(CreateSummaryLink(
                    isAr ? $"{unknownCount} عناصر جديدة غير معروفة" : $"{unknownCount} unknown new item{(unknownCount == 1 ? "" : "s")}",
                    amberBrush,
                    FontWeights.SemiBold,
                    () => SelectFilterTab(activeFilterTab == "Quick" ? "Unknown" : "Quick")));
                string rest = isAr
                    ? $"  •  {optimizableCount} قابلة للتحسين  •  {ignoredCount} متجاهلة"
                    : $"  •  {optimizableCount} optimizable  •  {ignoredCount} ignored";
                lblSummaryStrip.Inlines.Add(new System.Windows.Documents.Run(rest)
                {
                    Foreground = WpfTheme.TextSecondaryBrush
                });
            }
            else if (optimizableCount > 0)
            {
                lblSummaryStrip.Inlines.Add(CreateSummaryLink(
                    isAr ? $"{optimizableCount} عناصر بدء تشغيل" : $"{optimizableCount} item{(optimizableCount == 1 ? "" : "s")}",
                    amberBrush,
                    FontWeights.SemiBold,
                    () => SelectFilterTab("Quick")));
                string rest = isAr
                    ? $" يمكن تعطيلها بأمان لإقلاع أسرع  •  0 غير معروفة  •  {ignoredCount} متجاهلة"
                    : $" can be safely disabled for faster boot  •  0 unknown  •  {ignoredCount} ignored";
                lblSummaryStrip.Inlines.Add(new System.Windows.Documents.Run(rest)
                {
                    Foreground = WpfTheme.TextSecondaryBrush
                });
            }
            else
            {
                string cleanMsg = isAr
                    ? $"بدء التشغيل محسّن بالكامل  •  0 غير معروفة  •  {ignoredCount} متجاهلة"
                    : $"Startup is well optimized  •  0 unknown  •  {ignoredCount} ignored";
                lblSummaryStrip.Inlines.Add(new System.Windows.Documents.Run(cleanMsg)
                {
                    Foreground = WpfTheme.SuccessBrush,
                    FontWeight = FontWeights.Medium
                });
            }

            btnMarkKnown.Visibility = unknownCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            btnMarkKnown.Content = LocalizationService.T("StartupBtnMarkKnown", isAr ? "تحديد كمعروف" : "Mark Known");

            bool canOptimize;
            if (activeFilterTab == "Quick")
            {
                canOptimize = selectedQuickCount > 0;
                string optLabel = LocalizationService.T("StartupBtnOptimizeQuick", isAr ? "تحسين" : "Optimize");
                btnOptimizeSafe.Content = $"{optLabel} ({selectedQuickCount})";
            }
            else
            {
                canOptimize = optimizableCount > 0;
                btnOptimizeSafe.Content = LocalizationService.T("StartupBtnOptimizeSafe", isAr ? "تعطيل الآمن" : "Optimize Safe");
            }

            btnOptimizeSafe.IsEnabled = canOptimize;
            btnOptimizeSafe.Opacity = 1.0;
            btnOptimizeSafe.Cursor = canOptimize ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;
            if (canOptimize)
            {
                btnOptimizeSafe.Background = new LinearGradientBrush(Color.FromRgb(59, 130, 246), Color.FromRgb(37, 99, 235), new Point(0, 0), new Point(1, 1));
                btnOptimizeSafe.Foreground = Brushes.White;
                btnOptimizeSafe.BorderThickness = new Thickness(0);
                btnOptimizeSafe.BorderBrush = Brushes.Transparent;
            }
            else
            {
                btnOptimizeSafe.Background = new SolidColorBrush(isDark ? Color.FromRgb(28, 35, 47) : Color.FromRgb(226, 232, 240));
                btnOptimizeSafe.Foreground = new SolidColorBrush(isDark ? Color.FromRgb(148, 163, 184) : Color.FromRgb(100, 116, 139));
                btnOptimizeSafe.BorderBrush = new SolidColorBrush(isDark ? Color.FromRgb(51, 65, 85) : Color.FromRgb(203, 213, 225));
                btnOptimizeSafe.BorderThickness = new Thickness(1);
            }
        }

        private static System.Windows.Documents.Inline CreateSummaryLink(string text, Brush fg, FontWeight weight, Action onClick)
        {
            var run = new System.Windows.Documents.Run(text)
            {
                Foreground = fg,
                FontWeight = weight
            };
            var link = new System.Windows.Documents.Hyperlink(run)
            {
                Foreground = fg,
                TextDecorations = null,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            link.MouseEnter += (s, e) => link.TextDecorations = TextDecorations.Underline;
            link.MouseLeave += (s, e) => link.TextDecorations = null;
            link.Click += (s, e) => onClick();
            return link;
        }

        private IEnumerable<StartupItem> GetFilteredList()
        {
            var query = allItems.AsEnumerable();

            switch (activeFilterTab)
            {
                case "Quick":
                    query = query.Where(x => IsActiveQuickOptimizable(x) || x.IsIgnored);
                    break;
                case "Enabled":
                    query = query.Where(x => x.IsEnabled);
                    break;
                case "Disabled":
                    query = query.Where(x => !x.IsEnabled);
                    break;
                case "Apps":
                    query = query.Where(x => x.LocationType == StartupType.RegistryUser || x.LocationType == StartupType.RegistryMachine || x.LocationType == StartupType.RegistryWow64 || x.LocationType == StartupType.StartupFolderUser || x.LocationType == StartupType.StartupFolderCommon);
                    break;
                case "Services":
                    query = query.Where(x => x.LocationType == StartupType.WindowsService);
                    break;
                case "Tasks":
                    query = query.Where(x => x.LocationType == StartupType.ScheduledTask);
                    break;
                case "StoreApps":
                    query = query.Where(x => x.LocationType == StartupType.MicrosoftStoreApp);
                    break;
                case "Safe":
                    query = query.Where(x => x.Recommendation == StartupRecommendation.SafeToDisable);
                    break;
                case "Unknown":
                    query = query.Where(x => x.IsUnknown && !x.IsIgnored);
                    break;
                case "Ignored":
                    query = query.Where(x => x.IsIgnored);
                    break;
            }

            if (!string.IsNullOrWhiteSpace(currentSearchText))
            {
                query = query.Where(x =>
                    (x.Name?.Contains(currentSearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (x.Publisher?.Contains(currentSearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (x.Command?.Contains(currentSearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (x.PackageFamilyName?.Contains(currentSearchText, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            return query;
        }

        private void GridStartup_Sorting(object? sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;
            string? memberPath = e.Column?.SortMemberPath;
            if (string.IsNullOrEmpty(memberPath)) return;

            if (activeSortMemberPath == memberPath)
            {
                activeSortDirection = activeSortDirection == System.ComponentModel.ListSortDirection.Ascending
                    ? System.ComponentModel.ListSortDirection.Descending
                    : System.ComponentModel.ListSortDirection.Ascending;
            }
            else
            {
                activeSortMemberPath = memberPath;
                // Default Status (Enabled -> Disabled) and Boot Impact (High -> Low) to Descending on first click
                activeSortDirection = (memberPath == "StatusDisplayLocalized" || memberPath == "BootImpactDisplay")
                    ? System.ComponentModel.ListSortDirection.Descending
                    : System.ComponentModel.ListSortDirection.Ascending;
            }

            SettingsService.Instance.Update(s =>
            {
                s.StartupSortColumn = activeSortMemberPath;
                s.StartupSortAscending = activeSortDirection == System.ComponentModel.ListSortDirection.Ascending;
            });

            ApplyFilter();
        }

        private static int GetBootImpactRank(StartupItem item)
        {
            string impact = item.BootImpact ?? string.Empty;
            if (impact.Contains("High", StringComparison.OrdinalIgnoreCase)) return 3;
            if (impact.Contains("Med", StringComparison.OrdinalIgnoreCase)) return 2;
            return 1;
        }

        private List<StartupItem> SortItems(IEnumerable<StartupItem> items)
        {
            if (activeFilterTab == "Quick" && activeSortMemberPath != "IsIgnored")
            {
                IOrderedEnumerable<StartupItem> quickOrdered = items.OrderBy(x => x.IsIgnored ? 1 : 0);
                if (!string.IsNullOrEmpty(activeSortMemberPath))
                {
                    bool qAsc = activeSortDirection == System.ComponentModel.ListSortDirection.Ascending;
                    quickOrdered = activeSortMemberPath switch
                    {
                        "StatusDisplayLocalized" => qAsc
                            ? quickOrdered.ThenBy(x => x.IsEnabled ? 1 : 0)
                            : quickOrdered.ThenByDescending(x => x.IsEnabled ? 1 : 0),
                        "BootImpactDisplay" => qAsc
                            ? quickOrdered.ThenBy(GetBootImpactRank)
                            : quickOrdered.ThenByDescending(GetBootImpactRank),
                        "Publisher" => qAsc
                            ? quickOrdered.ThenBy(x => x.Publisher ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                            : quickOrdered.ThenByDescending(x => x.Publisher ?? string.Empty, StringComparer.CurrentCultureIgnoreCase),
                        "RecommendationDisplayLocalized" => qAsc
                            ? quickOrdered.ThenBy(x => x.RecommendationDisplayLocalized ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                            : quickOrdered.ThenByDescending(x => x.RecommendationDisplayLocalized ?? string.Empty, StringComparer.CurrentCultureIgnoreCase),
                        "LocationDisplayLocalized" => qAsc
                            ? quickOrdered.ThenBy(x => x.LocationDisplayLocalized ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                            : quickOrdered.ThenByDescending(x => x.LocationDisplayLocalized ?? string.Empty, StringComparer.CurrentCultureIgnoreCase),
                        "Command" => qAsc
                            ? quickOrdered.ThenBy(x => x.Command ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                            : quickOrdered.ThenByDescending(x => x.Command ?? string.Empty, StringComparer.CurrentCultureIgnoreCase),
                        _ => qAsc
                            ? quickOrdered.ThenBy(x => x.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                            : quickOrdered.ThenByDescending(x => x.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                    };
                }
                else
                {
                    quickOrdered = quickOrdered
                        .ThenByDescending(x => x.IsUnknown ? 1 : 0)
                        .ThenByDescending(GetBootImpactRank);
                }

                return quickOrdered
                    .ThenBy(x => x.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(x => x.Command ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (string.IsNullOrEmpty(activeSortMemberPath))
            {
                return items.ToList();
            }

            bool asc = activeSortDirection == System.ComponentModel.ListSortDirection.Ascending;
            IOrderedEnumerable<StartupItem> ordered = activeSortMemberPath switch
            {
                "StatusDisplayLocalized" => asc
                    ? items.OrderBy(x => x.IsEnabled ? 1 : 0)
                    : items.OrderByDescending(x => x.IsEnabled ? 1 : 0),
                "BootImpactDisplay" => asc
                    ? items.OrderBy(GetBootImpactRank)
                    : items.OrderByDescending(GetBootImpactRank),
                "Publisher" => asc
                    ? items.OrderBy(x => x.Publisher ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                    : items.OrderByDescending(x => x.Publisher ?? string.Empty, StringComparer.CurrentCultureIgnoreCase),
                "RecommendationDisplayLocalized" => asc
                    ? items.OrderBy(x => x.RecommendationDisplayLocalized ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                    : items.OrderByDescending(x => x.RecommendationDisplayLocalized ?? string.Empty, StringComparer.CurrentCultureIgnoreCase),
                "LocationDisplayLocalized" => asc
                    ? items.OrderBy(x => x.LocationDisplayLocalized ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                    : items.OrderByDescending(x => x.LocationDisplayLocalized ?? string.Empty, StringComparer.CurrentCultureIgnoreCase),
                "Command" => asc
                    ? items.OrderBy(x => x.Command ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                    : items.OrderByDescending(x => x.Command ?? string.Empty, StringComparer.CurrentCultureIgnoreCase),
                "IsIgnored" => asc
                    ? items.OrderBy(x => x.IsIgnored ? 1 : 0)
                    : items.OrderByDescending(x => x.IsIgnored ? 1 : 0),
                _ => asc
                    ? items.OrderBy(x => x.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                    : items.OrderByDescending(x => x.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            };

            return ordered
                .ThenBy(x => x.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.Command ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void SyncSortIndicators()
        {
            foreach (var col in gridStartup.Columns)
            {
                col.SortDirection = !string.IsNullOrEmpty(activeSortMemberPath) && col.SortMemberPath == activeSortMemberPath
                    ? activeSortDirection
                    : null;
            }
        }

        private static ScrollViewer? FindScrollViewer(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is ScrollViewer sv) return sv;
                var nested = FindScrollViewer(child);
                if (nested != null) return nested;
            }
            return null;
        }

        private void ApplyFilter()
        {
            var previouslySelected = gridStartup.SelectedItems.OfType<StartupItem>().ToList();
            var scrollViewer = FindScrollViewer(gridStartup);
            double? previousOffset = scrollViewer?.VerticalOffset;

            var list = SortItems(GetFilteredList());
            gridStartup.ItemsSource = list;
            SyncSortIndicators();
            SyncQuickSelectAllCheckBox();

            if (previouslySelected.Count > 0)
            {
                foreach (var sel in previouslySelected)
                {
                    if (list.Contains(sel))
                    {
                        gridStartup.SelectedItems.Add(sel);
                    }
                }
            }

            if (scrollViewer != null && previousOffset.HasValue && previousOffset.Value > 0)
            {
                scrollViewer.ScrollToVerticalOffset(previousOffset.Value);
            }

            int countAll = allItems.Count;
            int countEnabled = allItems.Count(x => x.IsEnabled);
            int countSafe = allItems.Count(x => x.Recommendation == StartupRecommendation.SafeToDisable);

            if (LocalizationService.IsArabic)
            {
                lblFooter.Text = $"عرض {list.Count} عناصر | الإجمالي: {countAll} ({countEnabled} مفعلة، {countSafe} آمن للتعطيل). انقر على عنوان العمود للفرز.";
            }
            else
            {
                lblFooter.Text = $"Showing {list.Count} items | Total: {countAll} ({countEnabled} Enabled, {countSafe} Safe to Disable). Click column headers to sort.";
            }
            UpdateSelectionActionsState();
        }

        private List<StartupItem> GetCheckedItems()
        {
            if (gridStartup.ItemsSource is not IEnumerable<StartupItem> visible)
                return new List<StartupItem>();

            return activeFilterTab == "Quick"
                ? visible.Where(x => !x.IsIgnored && x.IsQuickSelected).ToList()
                : visible.Where(x => x.IsSelected).ToList();
        }

        private List<StartupItem> GetActionTargetItems()
        {
            var checkedItems = GetCheckedItems();
            var selectedRows = gridStartup.SelectedItems.OfType<StartupItem>().ToList();
            if (checkedItems.Count > 0 && selectedRows.Count <= 1)
                return checkedItems;
            if (selectedRows.Count > 1)
                return checkedItems.Count == 0
                    ? selectedRows
                    : checkedItems.Union(selectedRows).ToList();
            return gridStartup.SelectedItem is StartupItem single
                ? new List<StartupItem> { single }
                : new List<StartupItem>();
        }

    }
}
