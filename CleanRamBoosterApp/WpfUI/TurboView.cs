using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CleanRamBoosterApp.Models;
using CleanRamBoosterApp.Services;
using CleanRamBoosterApp.UI;

namespace CleanRamBoosterApp.WpfUI
{
    public class TurboView : UserControl
    {
        private readonly TurboBoostService turboService;
        private List<TurboBoostItem> allItems = new();
        private TurboCategory? activeCategoryFilter = null;
        private bool isGameMode = true;
        private string? activeSortMemberPath;
        private System.ComponentModel.ListSortDirection activeSortDirection = System.ComponentModel.ListSortDirection.Descending;

        // UI Elements
        private readonly TextBlock lblHead;
        private readonly TextBlock lblSub;
        private readonly Border cardGaming;
        private readonly Border cardWork;
        private readonly ContentControl gamingIconHost;
        private readonly ContentControl workIconHost;
        private readonly TextBlock lblGamingTitle;
        private readonly TextBlock lblGamingSub;
        private readonly TextBlock lblWorkTitle;
        private readonly TextBlock lblWorkSub;

        private readonly Button btnFilterAll;
        private readonly Button btnFilterApps;
        private readonly Button btnFilterNonWin;
        private readonly Button btnFilterSys;

        private readonly Button btnSelectAll;
        private readonly Button btnDeselectAll;
        private readonly Button btnRestorePoint;
        private readonly Button btnAction;
        private readonly TextBlock lblSummary;

        private readonly Border cardGrid;
        private readonly DataGrid gridItems;

        private int scanGeneration = 0;
        private System.Threading.Tasks.Task? activeScanTask;
        private int toggleGeneration = 0;
        private bool hasCompletedInitialScan = false;
        private string? statusFeedbackMessage;
        private bool statusFeedbackSuccess = false;

        public bool IsTurboActive => turboService.IsBoostActive;
        public bool IsGameMode => isGameMode;
        public long LastSavedRamBytes => turboService.LastSavedRamBytes;
        public event Action? TurboStateChanged;
        public event Action<string>? TurboStatusMessage;

        public TurboView(TurboBoostService service)
        {
            turboService = service;

            var rootGrid = new Grid
            {
                Margin = new Thickness(24, 20, 24, 20)
            };
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Mode Cards
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Controls bar
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // DataGrid
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Bottom Action Bar

            // 1. Header
            var headRow = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var headBadge = WpfIcons.CreateHeaderBadge("Rocket", Color.FromRgb(59, 130, 246), 42, 22);
            headBadge.Margin = new Thickness(0, 0, 14, 0);
            Grid.SetColumn(headBadge, 0);
            headRow.Children.Add(headBadge);

            var headStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            lblHead = new TextBlock
            {
                Text = UITheme.StripLeadingIcon(LocalizationService.T("TurboHeader", "🚀 Turbo Boost Mode")),
                FontSize = 20,
                FontWeight = FontWeights.SemiBold
            };
            lblSub = new TextBlock
            {
                Text = LocalizationService.T("TurboSub", "Temporarily stop non-essential services and background apps to free CPU & RAM for heavy tasks."),
                FontSize = 13,
                Margin = new Thickness(0, 3, 0, 0)
            };
            headStack.Children.Add(lblHead);
            headStack.Children.Add(lblSub);
            Grid.SetColumn(headStack, 1);
            headRow.Children.Add(headStack);
            Grid.SetRow(headRow, 0);
            rootGrid.Children.Add(headRow);

            // 2. Mode Cards (Gaming vs Work)
            var modesGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            modesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            modesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            modesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Gaming Card
            cardGaming = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(2),
                Padding = new Thickness(16, 14, 16, 14),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            var gamingStack = new StackPanel();
            var gamingTitleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            gamingIconHost = new ContentControl { Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
            lblGamingTitle = new TextBlock
            {
                Text = UITheme.StripLeadingIcon(LocalizationService.T("TurboGamingTitle", "🎮 Gaming & Heavy Apps")),
                FontSize = 15,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            };
            gamingTitleRow.Children.Add(gamingIconHost);
            gamingTitleRow.Children.Add(lblGamingTitle);
            lblGamingSub = new TextBlock
            {
                Text = LocalizationService.T("TurboGamingSub", "Stops all non-essential Windows & 3rd-party services to prioritize GPU and low latency."),
                FontSize = 12.5,
                FontWeight = FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 0)
            };
            gamingStack.Children.Add(gamingTitleRow);
            gamingStack.Children.Add(lblGamingSub);
            cardGaming.Child = gamingStack;
            cardGaming.MouseLeftButtonDown += (s, e) => SetTurboMode(true);
            Grid.SetColumn(cardGaming, 0);
            modesGrid.Children.Add(cardGaming);

            // Work Card
            cardWork = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(2),
                Padding = new Thickness(16, 14, 16, 14),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            var workStack = new StackPanel();
            var workTitleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            workIconHost = new ContentControl { Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
            lblWorkTitle = new TextBlock
            {
                Text = UITheme.StripLeadingIcon(LocalizationService.T("TurboWorkTitle", "💼 Work & Productivity")),
                FontSize = 15,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            };
            workTitleRow.Children.Add(workIconHost);
            workTitleRow.Children.Add(lblWorkTitle);
            lblWorkSub = new TextBlock
            {
                Text = LocalizationService.T("TurboWorkSub", "Keeps print/network/office services running while disabling background telemetry bloat."),
                FontSize = 12.5,
                FontWeight = FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 0)
            };
            workStack.Children.Add(workTitleRow);
            workStack.Children.Add(lblWorkSub);
            cardWork.Child = workStack;
            cardWork.MouseLeftButtonDown += (s, e) => SetTurboMode(false);
            Grid.SetColumn(cardWork, 2);
            modesGrid.Children.Add(cardWork);

            Grid.SetRow(modesGrid, 1);
            rootGrid.Children.Add(modesGrid);

            // 3. Category Filter & Selection Bar
            var barGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var filterStack = new StackPanel { Orientation = Orientation.Horizontal };
            btnFilterAll = CreateFilterButton("All", null);
            btnFilterApps = CreateFilterButton("Background Apps", TurboCategory.BackgroundApps);
            btnFilterNonWin = CreateFilterButton("Non-Windows", TurboCategory.NonWindowsServices);
            btnFilterSys = CreateFilterButton("System Services", TurboCategory.SystemServices);

            filterStack.Children.Add(btnFilterAll);
            filterStack.Children.Add(btnFilterApps);
            filterStack.Children.Add(btnFilterNonWin);
            filterStack.Children.Add(btnFilterSys);
            Grid.SetColumn(filterStack, 0);
            barGrid.Children.Add(filterStack);

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
            btnSelectAll.Click += (s, e) =>
            {
                statusFeedbackMessage = null;
                foreach (var item in GetFilteredItems()) item.IsSelected = true;
                if (activeSortMemberPath == "IsSelected" || activeSortMemberPath == "StatusDisplay")
                    ApplyFilter();
                else
                    gridItems?.Items.Refresh();
                UpdateSummary();
            };

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
            btnDeselectAll.Click += (s, e) =>
            {
                statusFeedbackMessage = null;
                foreach (var item in GetFilteredItems()) item.IsSelected = false;
                if (activeSortMemberPath == "IsSelected" || activeSortMemberPath == "StatusDisplay")
                    ApplyFilter();
                else
                    gridItems?.Items.Refresh();
                UpdateSummary();
            };

            btnRestorePoint = new Button
            {
                Height = 32,
                Padding = new Thickness(12, 0, 12, 0),
                FontSize = 12.5,
                FontWeight = FontWeights.Normal
            };
            ApplySecondaryButtonStyle(btnRestorePoint);
            btnRestorePoint.Click += async (s, e) => await CreateRestorePointAsync();

            selStack.Children.Add(btnSelectAll);
            selStack.Children.Add(btnDeselectAll);
            selStack.Children.Add(btnRestorePoint);
            Grid.SetColumn(selStack, 2);
            barGrid.Children.Add(selStack);

            Grid.SetRow(barGrid, 2);
            rootGrid.Children.Add(barGrid);

            // 4. DataGrid
            cardGrid = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                ClipToBounds = true
            };
            gridItems = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserResizeRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                SelectionMode = DataGridSelectionMode.Single,
                BorderThickness = new Thickness(0),
                RowHeight = 35,
                FontSize = 12.5
            };

            // Column 1: CheckBox (uses app's custom styled CheckBox via DataGridTemplateColumn for 1-click toggle)
            var chkFactory = new FrameworkElementFactory(typeof(CheckBox));
            chkFactory.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding("IsSelected")
            {
                Mode = System.Windows.Data.BindingMode.TwoWay,
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            });
            chkFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            chkFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            chkFactory.SetValue(Control.PaddingProperty, new Thickness(0));

            var colCheck = new DataGridTemplateColumn
            {
                Header = "",
                CellTemplate = new DataTemplate { VisualTree = chkFactory },
                Width = new DataGridLength(44),
                CanUserResize = false,
                CanUserSort = true,
                SortMemberPath = "IsSelected"
            };
            gridItems.Columns.Add(colCheck);
            gridItems.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                if (e.OriginalSource is CheckBox)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        statusFeedbackMessage = null;
                        UpdateSummary();
                        if (activeSortMemberPath == "IsSelected" || activeSortMemberPath == "StatusDisplay")
                            ApplyFilter();
                        else
                            gridItems.Items.Refresh();
                    }));
                }
            }));

            // Column 2: Name
            var colName = new DataGridTextColumn
            {
                Header = LocalizationService.T("TurboColName", "Process / Service Name"),
                Binding = new System.Windows.Data.Binding("DisplayName"),
                Width = new DataGridLength(2, DataGridLengthUnitType.Star),
                IsReadOnly = true
            };
            gridItems.Columns.Add(colName);

            // Column 3: Type / Category
            var colType = new DataGridTextColumn
            {
                Header = LocalizationService.T("TurboColType", "Type"),
                Binding = new System.Windows.Data.Binding("CategoryDisplay"),
                SortMemberPath = "Category",
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                IsReadOnly = true
            };
            gridItems.Columns.Add(colType);

            // Column 4: RAM Usage
            var colRam = new DataGridTextColumn
            {
                Header = LocalizationService.T("TurboColRam", "RAM Usage"),
                Binding = new System.Windows.Data.Binding("MemoryUsageDisplay"),
                Width = new DataGridLength(120),
                IsReadOnly = true
            };
            gridItems.Columns.Add(colRam);

            // Column 5: Status
            var colStatus = new DataGridTextColumn
            {
                Header = LocalizationService.T("TurboColStatus", "Status"),
                Binding = new System.Windows.Data.Binding("StatusDisplayLocalized"),
                SortMemberPath = "StatusDisplay",
                Width = new DataGridLength(104),
                IsReadOnly = true
            };
            gridItems.Columns.Add(colStatus);
            gridItems.Sorting += GridItems_Sorting;

            cardGrid.Padding = new Thickness(0, 0, 0, 2);
            cardGrid.Child = gridItems;
            Grid.SetRow(cardGrid, 3);
            rootGrid.Children.Add(cardGrid);

            // 5. Bottom Action Bar
            var bottomGrid = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            lblSummary = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 13,
                FontWeight = FontWeights.Normal
            };
            Grid.SetColumn(lblSummary, 0);
            bottomGrid.Children.Add(lblSummary);

            btnAction = new Button
            {
                Height = 42,
                Padding = new Thickness(24, 0, 24, 0),
                FontSize = 13.5,
                FontWeight = FontWeights.Medium,
                Foreground = Brushes.White,
                Cursor = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(0)
            };
            btnAction.Click += (s, e) => _ = ToggleTurboBoostAsync();
            Grid.SetColumn(btnAction, 1);
            bottomGrid.Children.Add(btnAction);

            Grid.SetRow(bottomGrid, 4);
            rootGrid.Children.Add(bottomGrid);

            Content = rootGrid;

            UpdateModeCards();
            ApplyLanguage();
            RefreshTheme();
            activeScanTask = ScanCandidatesAsync();
        }

        private Button CreateFilterButton(string text, TurboCategory? category)
        {
            var btn = new Button
            {
                Content = text,
                Height = 32,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 8, 0),
                FontSize = 12.5,
                FontWeight = FontWeights.Normal,
                Tag = category
            };
            ApplySecondaryButtonStyle(btn);
            btn.Click += (s, e) =>
            {
                activeCategoryFilter = category;
                UpdateFilterButtons();
                ApplyFilter();
            };
            return btn;
        }

        private void UpdateFilterButtons()
        {
            SetFilterBtnState(btnFilterAll, activeCategoryFilter == null, null, LocalizationService.IsArabic ? "الكل" : "All");
            SetFilterBtnState(btnFilterApps, activeCategoryFilter == TurboCategory.BackgroundApps, "Gamepad", UITheme.StripLeadingIcon(LocalizationService.T("TurboMenuApps", "🎮 Background Apps")));
            SetFilterBtnState(btnFilterNonWin, activeCategoryFilter == TurboCategory.NonWindowsServices, "Box", UITheme.StripLeadingIcon(LocalizationService.T("TurboMenuNonWin", "🏢 Non-Windows")));
            SetFilterBtnState(btnFilterSys, activeCategoryFilter == TurboCategory.SystemServices, "Sliders", UITheme.StripLeadingIcon(LocalizationService.T("TurboMenuSys", "⚙️ System Services")));
        }

        private static void SetFilterBtnState(Button btn, bool isActive, string? iconKey, string label)
        {
            if (isActive)
            {
                btn.Background = WpfTheme.PrimaryBrush;
                btn.Foreground = Brushes.White;
                btn.BorderThickness = new Thickness(0);
            }
            else
            {
                ApplySecondaryButtonStyle(btn);
            }

            btn.Content = string.IsNullOrEmpty(iconKey)
                ? label
                : WpfIcons.CreateIconTextContent(iconKey, label, btn.Foreground, 15, 7);
        }

        private void UpdateModeCards()
        {
            var activeBorder = WpfTheme.PrimaryBrush;
            var inactiveBorder = WpfTheme.BorderBrush;

            cardGaming.BorderBrush = isGameMode ? activeBorder : inactiveBorder;
            cardGaming.Background = isGameMode ? (WpfTheme.IsDarkMode ? new SolidColorBrush(Color.FromArgb(40, 59, 130, 246)) : new SolidColorBrush(Color.FromArgb(25, 59, 130, 246))) : WpfTheme.CardBrush;

            cardWork.BorderBrush = !isGameMode ? activeBorder : inactiveBorder;
            cardWork.Background = !isGameMode ? (WpfTheme.IsDarkMode ? new SolidColorBrush(Color.FromArgb(40, 59, 130, 246)) : new SolidColorBrush(Color.FromArgb(25, 59, 130, 246))) : WpfTheme.CardBrush;

            gamingIconHost.Content = WpfIcons.CreateIcon("Gamepad", isGameMode ? WpfTheme.PrimaryBrush : WpfTheme.TextSecondaryBrush, 20, 1.8);
            workIconHost.Content = WpfIcons.CreateIcon("Briefcase", !isGameMode ? WpfTheme.PrimaryBrush : WpfTheme.TextSecondaryBrush, 20, 1.8);
        }

        private async System.Threading.Tasks.Task ScanCandidatesAsync()
        {
            int gen = ++scanGeneration;
            bool mode = isGameMode;
            try
            {
                var scanned = await System.Threading.Tasks.Task.Run(() => turboService.ScanBoostCandidates(mode));
                if (gen != scanGeneration) return;
                allItems = scanned;
                hasCompletedInitialScan = true;
                ApplyFilter();
                UpdateSummary();
            }
            catch { }
        }

        private IEnumerable<TurboBoostItem> GetFilteredItems()
        {
            if (activeCategoryFilter == null) return allItems;
            return allItems.Where(x => x.Category == activeCategoryFilter.Value);
        }

        private void GridItems_Sorting(object? sender, DataGridSortingEventArgs e)
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
                activeSortDirection = (memberPath == "MemoryUsageDisplay" || memberPath == "StatusDisplay" || memberPath == "IsSelected")
                    ? System.ComponentModel.ListSortDirection.Descending
                    : System.ComponentModel.ListSortDirection.Ascending;
            }

            ApplyFilter();
        }

        private List<TurboBoostItem> SortItems(IEnumerable<TurboBoostItem> items)
        {
            if (string.IsNullOrEmpty(activeSortMemberPath))
            {
                return items.ToList();
            }

            bool asc = activeSortDirection == System.ComponentModel.ListSortDirection.Ascending;
            IOrderedEnumerable<TurboBoostItem> ordered = activeSortMemberPath switch
            {
                "IsSelected" or "StatusDisplay" => asc
                    ? items.OrderBy(x => x.IsSelected ? 1 : 0)
                    : items.OrderByDescending(x => x.IsSelected ? 1 : 0),
                "MemoryUsageDisplay" => asc
                    ? items.OrderBy(x => x.MemoryUsageBytes)
                    : items.OrderByDescending(x => x.MemoryUsageBytes),
                "Category" => asc
                    ? items.OrderBy(x => x.Category.ToString(), StringComparer.CurrentCultureIgnoreCase)
                    : items.OrderByDescending(x => x.Category.ToString(), StringComparer.CurrentCultureIgnoreCase),
                _ => asc
                    ? items.OrderBy(x => x.DisplayName ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                    : items.OrderByDescending(x => x.DisplayName ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            };

            return ordered
                .ThenBy(x => x.DisplayName ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.ProcessId)
                .ToList();
        }

        private void SyncSortIndicators()
        {
            foreach (var col in gridItems.Columns)
            {
                col.SortDirection = !string.IsNullOrEmpty(activeSortMemberPath) && col.SortMemberPath == activeSortMemberPath
                    ? activeSortDirection
                    : null;
            }
        }

        private void ApplyFilter()
        {
            var selectedItem = gridItems.SelectedItem as TurboBoostItem;
            var list = SortItems(GetFilteredItems());
            gridItems.ItemsSource = list;
            SyncSortIndicators();
            if (selectedItem != null && list.Contains(selectedItem))
            {
                gridItems.SelectedItem = selectedItem;
            }
        }

        private void UpdateSummary()
        {
            int totalSelected = allItems.Count(x => x.IsSelected);
            long totalMem = allItems.Where(x => x.IsSelected).Sum(x => x.MemoryUsageBytes);
            string memFormatted = WpfTheme.FormatBytes(totalMem);

            string btnLabel;
            string defaultSummary;
            if (LocalizationService.IsArabic)
            {
                defaultSummary = $"المحدد للإيقاف: {totalSelected} عناصر | الذاكرة المتوقع تحريرها: {memFormatted}";
                btnLabel = UITheme.StripLeadingIcon(turboService.IsBoostActive ? LocalizationService.T("TurboBtnTurnOff", "⏹️ إيقاف الوضع الفائق واستعادة الخدمات") : LocalizationService.T("TurboBtnTurnOn", "🚀 تشغيل الوضع الفائق الآن"));
            }
            else
            {
                defaultSummary = $"Selected to boost: {totalSelected} items | Potential RAM savings: {memFormatted}";
                btnLabel = UITheme.StripLeadingIcon(turboService.IsBoostActive ? LocalizationService.T("TurboBtnTurnOff", "⏹️ RESTORE NORMAL STATE") : LocalizationService.T("TurboBtnTurnOn", "🚀 TURN ON TURBO BOOST"));
            }

            if (!string.IsNullOrEmpty(statusFeedbackMessage))
            {
                lblSummary.Text = statusFeedbackMessage;
                lblSummary.Foreground = (!string.IsNullOrEmpty(statusFeedbackMessage) && statusFeedbackSuccess)
                ? WpfTheme.SuccessBrush
                : WpfTheme.TextPrimaryBrush;
            }
            else
            {
                lblSummary.Text = defaultSummary;
                lblSummary.Foreground = WpfTheme.TextPrimaryBrush;
            }

            btnAction.Content = WpfIcons.CreateIconTextContent(turboService.IsBoostActive ? "Refresh" : "Rocket", btnLabel, Brushes.White, 17, 9);

            if (turboService.IsBoostActive)
            {
                btnAction.Background = new LinearGradientBrush(Color.FromRgb(239, 68, 68), Color.FromRgb(220, 38, 38), new Point(0, 0), new Point(1, 1));
            }
            else
            {
                btnAction.Background = new LinearGradientBrush(Color.FromRgb(59, 130, 246), Color.FromRgb(37, 99, 235), new Point(0, 0), new Point(1, 1));
            }
        }

        public async System.Threading.Tasks.Task ToggleTurboBoostAsync()
        {
            bool turningOn = !turboService.IsBoostActive;

            if (!AdminPrivilegeService.IsRunningAsAdmin())
            {
                bool needsAdmin = !turningOn || allItems.Any(x => x.IsSelected && x.IsService);
                if (needsAdmin)
                {
                    if (!AdminPrivilegeService.EnsureAdminOrPrompt(this, "Turbo Boost", "الوضع الفائق (Turbo Boost)"))
                        return;
                }
            }

            int myToggleGen = ++toggleGeneration;

            if (turningOn)
            {
                if (!hasCompletedInitialScan && activeScanTask != null && !activeScanTask.IsCompleted)
                {
                    statusFeedbackSuccess = false;
                    statusFeedbackMessage = LocalizationService.IsArabic
                        ? "جاري فحص العناصر قبل تفعيل الوضع الفائق..."
                        : "Scanning candidates for Turbo Boost...";
                    UpdateSummary();
                    await activeScanTask;
                    if (myToggleGen != toggleGeneration) return;
                }

                var selected = allItems.Where(x => x.IsSelected).ToList();
                int total = selected.Count;
                statusFeedbackSuccess = false;
                statusFeedbackMessage = total > 0
                    ? (LocalizationService.IsArabic ? $"جاري تفعيل الوضع الفائق (0/{total})..." : $"Activating Turbo Boost (0/{total})...")
                    : (LocalizationService.IsArabic ? "جاري تفعيل الوضع الفائق..." : "Activating Turbo Boost...");

                var progress = new Progress<(int done, int count)>(p =>
                {
                    if (myToggleGen != toggleGeneration) return;
                    statusFeedbackMessage = LocalizationService.IsArabic
                        ? $"جاري تفعيل الوضع الفائق ({p.done}/{p.count})..."
                        : $"Activating Turbo Boost ({p.done}/{p.count})...";
                    UpdateSummary();
                });

                var task = turboService.TurnOnTurboBoostAsync(selected, progress);
                UpdateSummary();
                TurboStateChanged?.Invoke();

                var (stoppedServices, closedApps, savedRamBytes, cancelled) = await task;
                if (cancelled || myToggleGen != toggleGeneration || !turboService.IsBoostActive) return;

                string savedRamText = WpfTheme.FormatBytes(savedRamBytes);
                int skipped = turboService.LastSkippedProtectedCount;
                string skipSuffix = skipped > 0
                    ? (LocalizationService.IsArabic ? $" • تم تخطي {skipped} عنصر محمي" : $" • {skipped} protected skipped")
                    : string.Empty;
                statusFeedbackSuccess = true;
                statusFeedbackMessage = LocalizationService.IsArabic
                    ? $"✓ تم تفعيل الوضع الفائق: تم إيقاف {stoppedServices} خدمة و {closedApps} تطبيق — تم توفير {savedRamText} من الذاكرة{skipSuffix}."
                    : $"✓ Turbo Boost Activated: Stopped {stoppedServices} services & {closedApps} background apps — Saved {savedRamText} RAM{skipSuffix}.";
                TurboStatusMessage?.Invoke(statusFeedbackMessage);
                TurboStateChanged?.Invoke();

                activeScanTask = ScanCandidatesAsync();
                await activeScanTask;
                if (myToggleGen == toggleGeneration) UpdateSummary();
                TurboStateChanged?.Invoke();
            }
            else
            {
                statusFeedbackSuccess = false;
                statusFeedbackMessage = LocalizationService.IsArabic
                    ? "جاري استعادة الوضع الطبيعي..."
                    : "Restoring normal state...";

                var progress = new Progress<(int done, int count)>(p =>
                {
                    if (myToggleGen != toggleGeneration) return;
                    statusFeedbackMessage = LocalizationService.IsArabic
                        ? $"جاري استعادة الوضع الطبيعي ({p.done}/{p.count})..."
                        : $"Restoring normal state ({p.done}/{p.count})...";
                    UpdateSummary();
                });

                var task = turboService.TurnOffTurboBoostAsync(progress);
                UpdateSummary();
                TurboStateChanged?.Invoke();

                var (restored, cancelled) = await task;
                if (cancelled || myToggleGen != toggleGeneration || turboService.IsBoostActive) return;

                int failedRestore = turboService.LastRestoreFailedCount;
                string failSuffix = failedRestore > 0
                    ? (LocalizationService.IsArabic ? $" • تعذر إعادة تشغيل {failedRestore}" : $" • {failedRestore} could not restart")
                    : string.Empty;
                statusFeedbackSuccess = true;
                statusFeedbackMessage = LocalizationService.IsArabic
                    ? $"✓ تمت استعادة الوضع الطبيعي: أُعيد تشغيل {restored} خدمة{failSuffix}."
                    : $"✓ Normal State Restored: Restarted {restored} services{failSuffix}.";
                TurboStatusMessage?.Invoke(statusFeedbackMessage);

                activeScanTask = ScanCandidatesAsync();
                await activeScanTask;
                if (myToggleGen == toggleGeneration) UpdateSummary();
                TurboStateChanged?.Invoke();
            }
        }

        public void SetTurboMode(bool gameMode)
        {
            isGameMode = gameMode;
            statusFeedbackMessage = null;
            UpdateModeCards();
            activeScanTask = ScanCandidatesAsync();
            TurboStateChanged?.Invoke();
        }

        public void TriggerToggleFromDashboard()
        {
            _ = ToggleTurboBoostAsync();
        }

        public (int Count, long RamBytes) GetStatusSummary()
        {
            int totalSelected = allItems.Count(x => x.IsSelected);
            long totalMem = allItems.Where(x => x.IsSelected).Sum(x => x.MemoryUsageBytes);
            return (totalSelected, totalMem);
        }

        private static void ApplySecondaryButtonStyle(Button btn)
        {
            bool isDark = WpfTheme.IsDarkMode;
            btn.Background = new SolidColorBrush(isDark ? Color.FromRgb(33, 38, 45) : Color.FromRgb(241, 245, 249));
            btn.Foreground = new SolidColorBrush(isDark ? Color.FromRgb(201, 209, 217) : Color.FromRgb(30, 41, 59));
            btn.BorderBrush = new SolidColorBrush(isDark ? Color.FromRgb(48, 54, 61) : Color.FromRgb(226, 232, 240));
            btn.BorderThickness = new Thickness(1);
        }

        private async System.Threading.Tasks.Task CreateRestorePointAsync()
        {
            if (!AdminPrivilegeService.EnsureAdminOrPrompt(this, "System Restore Point", "إنشاء نقطة استعادة للنظام"))
                return;

            bool isAr = LocalizationService.IsArabic;
            btnRestorePoint.IsEnabled = false;
            statusFeedbackSuccess = false;
            statusFeedbackMessage = isAr
                ? "جاري إنشاء نقطة استعادة للنظام..."
                : "Creating Windows System Restore Point...";
            UpdateSummary();

            try
            {
                var res = await System.Threading.Tasks.Task.Run(() =>
                    SystemRestoreService.CreateRestorePoint("PC Boost Turbo Boost Checkpoint"));
                statusFeedbackSuccess = res.Success;
                statusFeedbackMessage = res.Message;
                UpdateSummary();
                TurboStatusMessage?.Invoke($"[Turbo Boost] {res.Message}");
                AppDialog.Show(this, res.Message, LocalizationService.T("AppTitle", "PC Boost"),
                    res.Success ? AppDialogTone.Success : AppDialogTone.Warning);
            }
            finally
            {
                btnRestorePoint.IsEnabled = true;
            }
        }

        public void ApplyLanguage()
        {
            lblHead.Text = UITheme.StripLeadingIcon(LocalizationService.T("TurboHeader", "🚀 Turbo Boost Mode"));
            lblSub.Text = LocalizationService.T("TurboSub", "Temporarily stop non-essential services and background apps to free CPU & RAM for heavy tasks.");
            lblGamingTitle.Text = UITheme.StripLeadingIcon(LocalizationService.T("TurboGamingTitle", "🎮 Gaming & Heavy Apps"));
            lblGamingSub.Text = LocalizationService.T("TurboGamingSub", "Stops all non-essential Windows & 3rd-party services to prioritize GPU and low latency.");
            lblWorkTitle.Text = UITheme.StripLeadingIcon(LocalizationService.T("TurboWorkTitle", "💼 Work & Productivity"));
            lblWorkSub.Text = LocalizationService.T("TurboWorkSub", "Keeps print/network/office services running while disabling background telemetry bloat.");

            UpdateFilterButtons();

            btnSelectAll.Content = LocalizationService.T("BtnSelectAll", "Select All");
            btnDeselectAll.Content = LocalizationService.T("BtnDeselectAll", "Deselect All");
            btnRestorePoint.Content = WpfIcons.CreateIconTextContent("Shield", LocalizationService.T("BtnRestorePoint", "Restore Point"), btnRestorePoint.Foreground, 14, 6);

            if (gridItems.Columns.Count >= 5)
            {
                gridItems.Columns[1].Header = LocalizationService.T("TurboColName", "Process / Service Name");
                gridItems.Columns[2].Header = LocalizationService.T("TurboColType", "Type");
                gridItems.Columns[3].Header = LocalizationService.T("TurboColRam", "RAM Usage");
                gridItems.Columns[4].Header = LocalizationService.T("TurboColStatus", "Status");
                SyncSortIndicators();
            }

            UpdateSummary();
            gridItems.Items.Refresh();
        }

        public void RefreshTheme()
        {
            lblHead.Foreground = WpfTheme.TextPrimaryBrush;
            lblSub.Foreground = WpfTheme.TextSecondaryBrush;
            lblGamingTitle.Foreground = WpfTheme.TextPrimaryBrush;
            lblGamingSub.Foreground = WpfTheme.TextSecondaryBrush;
            lblWorkTitle.Foreground = WpfTheme.TextPrimaryBrush;
            lblWorkSub.Foreground = WpfTheme.TextSecondaryBrush;
            lblSummary.Foreground = (!string.IsNullOrEmpty(statusFeedbackMessage) && statusFeedbackSuccess)
                ? WpfTheme.SuccessBrush
                : WpfTheme.TextPrimaryBrush;

            cardGrid.Background = WpfTheme.CardBrush;
            cardGrid.BorderBrush = WpfTheme.BorderBrush;

            gridItems.Background = WpfTheme.CardBrush;
            gridItems.Foreground = WpfTheme.TextPrimaryBrush;
            gridItems.RowBackground = WpfTheme.CardBrush;
            gridItems.AlternatingRowBackground = WpfTheme.CardSubBrush;
            gridItems.BorderBrush = WpfTheme.BorderBrush;
            gridItems.HorizontalGridLinesBrush = WpfTheme.BorderBrush;
            gridItems.ColumnHeaderStyle = WpfTheme.CreateDataGridHeaderStyle();
            gridItems.CellStyle = WpfTheme.CreateDataGridCellStyle();
            gridItems.RowStyle = WpfTheme.CreateDataGridRowStyle();

            UpdateModeCards();
            UpdateFilterButtons();
            ApplySecondaryButtonStyle(btnSelectAll);
            ApplySecondaryButtonStyle(btnDeselectAll);
            ApplySecondaryButtonStyle(btnRestorePoint);
            btnRestorePoint.Content = WpfIcons.CreateIconTextContent("Shield", LocalizationService.T("BtnRestorePoint", "Restore Point"), btnRestorePoint.Foreground, 14, 6);
            SyncSortIndicators();
        }
    }
}
