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
    public partial class StartupView : UserControl
    {
        private readonly StartupService startupService;
        private StartupMonitorService? attachedMonitor;
        private List<StartupItem> allItems = new();
        private string activeFilterTab = "All";
        private string currentSearchText = string.Empty;
        private string? activeSortMemberPath;
        private System.ComponentModel.ListSortDirection activeSortDirection = System.ComponentModel.ListSortDirection.Descending;
        private bool? lastCheckColumnQuickMode;
        private StartupItem? lastClickedCheckItem;

        // UI Controls
        private readonly TextBlock lblHead;
        private readonly TextBlock lblSub;

        private readonly Button btnTabQuick;
        private readonly Button btnTabAll;
        private readonly Button btnTabEnabled;
        private readonly Button btnTabDisabled;
        private readonly Button btnTabApps;
        private readonly Button btnTabServices;
        private readonly Button btnTabTasks;
        private readonly Button btnTabStoreApps;
        private readonly Button btnTabSafe;
        private readonly Button btnTabUnknown;
        private readonly Button btnTabIgnored;

        private DataGridTemplateColumn colQuickCheck = null!;
        private CheckBox chkQuickSelectAll = null!;
        private DataGridTemplateColumn colName = null!;
        private DataGridTextColumn colPublisher = null!;
        private DataGridTextColumn colStatus = null!;
        private DataGridTextColumn colImpact = null!;
        private DataGridTextColumn colRec = null!;
        private DataGridTextColumn colLocation = null!;
        private DataGridTextColumn colCommand = null!;
        private DataGridTemplateColumn colOperation = null!;

        private readonly Border cardSummaryStrip;
        private readonly ContentControl summaryIconHost;
        private readonly TextBlock lblSummaryStrip;
        private readonly Button btnMarkKnown;
        private readonly Button btnOptimizeSafe;

        private readonly Border searchBorder;
        private readonly TextBox txtSearch;
        private readonly TextBlock lblSearchPlaceholder;
        private readonly Button btnRefresh;
        private readonly Button btnUndoLast;
        private readonly Button btnDisableAllStore;
        private readonly Button btnToggle;
        private readonly Button btnLocation;
        private readonly Button btnDelete;

        private readonly Border cardGrid;
        private readonly DataGrid gridStartup;
        private readonly Border cardFeedbackBanner;
        private readonly ContentControl feedbackIconHost;
        private readonly TextBlock lblFeedback;
        private readonly Button btnFeedbackElevate;
        private readonly Button btnFeedbackDismiss;
        private readonly TextBlock lblFooter;
        private bool isActionBusy;
        private AppDialogTone lastFeedbackTone = AppDialogTone.Info;

        public event Action<StartupItem>? ItemStateModified;
        public event Action<StartupItem>? ItemRemoved;
        public event Action? InventoryUpdated;
        public event Action<string>? StatusMessage;

        public StartupView(StartupService service)
        {
            startupService = service;

            var rootGrid = new Grid
            {
                Margin = new Thickness(24, 20, 24, 20)
            };
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Summary Info Banner
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Filter Pills & Actions
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // DataGrid
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer

            // 1. Header
            var headRow = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var headBadge = WpfIcons.CreateHeaderBadge("Startup", Color.FromRgb(59, 130, 246), 42, 22);
            headBadge.Margin = new Thickness(0, 0, 14, 0);
            Grid.SetColumn(headBadge, 0);
            headRow.Children.Add(headBadge);

            var headStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            lblHead = new TextBlock
            {
                Text = UITheme.StripLeadingIcon(LocalizationService.T("StartupHeader", "🔄 Startup & Next-Reboot Manager")),
                FontSize = 20,
                FontWeight = FontWeights.SemiBold
            };
            lblSub = new TextBlock
            {
                Text = LocalizationService.T("StartupSub", "Detect and manage programs, services, and tasks that automatically launch at Windows boot."),
                FontSize = 13,
                Margin = new Thickness(0, 3, 0, 0)
            };
            headStack.Children.Add(lblHead);
            headStack.Children.Add(lblSub);
            Grid.SetColumn(headStack, 1);
            headRow.Children.Add(headStack);
            Grid.SetRow(headRow, 0);
            rootGrid.Children.Add(headRow);

            // 1b. Unknown & Optimization Summary Banner
            cardSummaryStrip = new Border
            {
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var summaryGrid = new Grid();
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            summaryIconHost = new ContentControl { Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(summaryIconHost, 0);
            summaryGrid.Children.Add(summaryIconHost);

            lblSummaryStrip = new TextBlock
            {
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(lblSummaryStrip, 1);
            summaryGrid.Children.Add(lblSummaryStrip);

            btnMarkKnown = new Button
            {
                Height = 29,
                Padding = new Thickness(11, 0, 11, 0),
                Margin = new Thickness(8, 0, 6, 0),
                FontSize = 12,
                FontWeight = FontWeights.Normal,
                Visibility = Visibility.Collapsed
            };
            ApplySecondaryButtonStyle(btnMarkKnown);
            btnMarkKnown.Click += (s, e) => MarkAllUnknownAsKnown();
            Grid.SetColumn(btnMarkKnown, 2);
            summaryGrid.Children.Add(btnMarkKnown);

            btnOptimizeSafe = new Button
            {
                Height = 29,
                Padding = new Thickness(13, 0, 13, 0),
                Margin = new Thickness(4, 0, 0, 0),
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            btnOptimizeSafe.Background = new LinearGradientBrush(Color.FromRgb(59, 130, 246), Color.FromRgb(37, 99, 235), new Point(0, 0), new Point(1, 1));
            btnOptimizeSafe.Click += async (s, e) => await OptimizeSafeItemsAsync();
            Grid.SetColumn(btnOptimizeSafe, 3);
            summaryGrid.Children.Add(btnOptimizeSafe);

            cardSummaryStrip.Child = summaryGrid;
            Grid.SetRow(cardSummaryStrip, 1);
            rootGrid.Children.Add(cardSummaryStrip);

            // 2. Filter Pills & Actions Toolbar
            var toolGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            toolGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            toolGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Row 0: Filter Tabs (Pills)
            var tabsGrid = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            btnTabQuick = CreateTabButton("Quick");
            btnTabAll = CreateTabButton("All");
            btnTabEnabled = CreateTabButton("Enabled");
            btnTabDisabled = CreateTabButton("Disabled");
            btnTabApps = CreateTabButton("Apps");
            btnTabServices = CreateTabButton("Services");
            btnTabTasks = CreateTabButton("Tasks");
            btnTabStoreApps = CreateTabButton("StoreApps");
            btnTabSafe = CreateTabButton("Safe");
            btnTabUnknown = CreateTabButton("Unknown");
            btnTabIgnored = CreateTabButton("Ignored");

            tabsGrid.Children.Add(btnTabQuick);
            tabsGrid.Children.Add(btnTabAll);
            tabsGrid.Children.Add(btnTabEnabled);
            tabsGrid.Children.Add(btnTabDisabled);
            tabsGrid.Children.Add(btnTabApps);
            tabsGrid.Children.Add(btnTabServices);
            tabsGrid.Children.Add(btnTabTasks);
            tabsGrid.Children.Add(btnTabStoreApps);
            tabsGrid.Children.Add(btnTabSafe);
            tabsGrid.Children.Add(btnTabUnknown);
            tabsGrid.Children.Add(btnTabIgnored);
            Grid.SetRow(tabsGrid, 0);
            toolGrid.Children.Add(tabsGrid);

            // Row 1: Search & Action Buttons
            var actionsGrid = new Grid();
            actionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) }); // Search
            actionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Spacer
            actionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Actions

            searchBorder = new Border
            {
                Height = 34,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1)
            };
            var searchHost = new Grid();
            txtSearch = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(10, 0, 10, 0),
                FontSize = 12.5
            };
            lblSearchPlaceholder = new TextBlock
            {
                Text = LocalizationService.T("StartupSearchPrompt", "Search..."),
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(11, 0, 11, 0),
                IsHitTestVisible = false
            };
            txtSearch.TextChanged += (s, e) =>
            {
                currentSearchText = txtSearch.Text.Trim();
                lblSearchPlaceholder.Visibility = string.IsNullOrEmpty(txtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
                ApplyFilter();
            };
            searchHost.Children.Add(lblSearchPlaceholder);
            searchHost.Children.Add(txtSearch);
            searchBorder.Child = searchHost;
            Grid.SetColumn(searchBorder, 0);
            actionsGrid.Children.Add(searchBorder);

            var actStack = new StackPanel { Orientation = Orientation.Horizontal };

            btnRefresh = new Button
            {
                Height = 34,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 6, 0),
                FontSize = 12.5,
                FontWeight = FontWeights.Normal
            };
            ApplySecondaryButtonStyle(btnRefresh);
            btnRefresh.Click += async (s, e) => await LoadItemsAsync();

            btnUndoLast = new Button
            {
                Height = 34,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 6, 0),
                FontSize = 12.5,
                FontWeight = FontWeights.Normal,
                IsEnabled = StartupHistoryService.Instance.Count > 0
            };
            ApplySecondaryButtonStyle(btnUndoLast);
            btnUndoLast.Click += async (s, e) => await UndoLastStartupActionAsync();
            StartupHistoryService.Instance.HistoryChanged += () => Dispatcher.BeginInvoke(new Action(UpdateUndoButtonState));

            btnDisableAllStore = new Button
            {
                Height = 34,
                Padding = new Thickness(13, 0, 13, 0),
                Margin = new Thickness(0, 0, 6, 0),
                FontSize = 12.5,
                FontWeight = FontWeights.Medium,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Visibility = Visibility.Collapsed
            };
            btnDisableAllStore.Background = new LinearGradientBrush(Color.FromRgb(234, 88, 12), Color.FromRgb(194, 65, 12), new Point(0, 0), new Point(1, 1));
            btnDisableAllStore.Click += (s, e) => DisableAllStoreAppsToNever();

            btnToggle = new Button
            {
                Height = 34,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 6, 0),
                FontSize = 12.5,
                FontWeight = FontWeights.Medium,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            btnToggle.Background = new LinearGradientBrush(Color.FromRgb(59, 130, 246), Color.FromRgb(37, 99, 235), new Point(0, 0), new Point(1, 1));
            btnToggle.Click += async (s, e) => await ToggleSelectedItemAsync();

            btnLocation = new Button
            {
                Height = 34,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 6, 0),
                FontSize = 12.5,
                FontWeight = FontWeights.Normal
            };
            ApplySecondaryButtonStyle(btnLocation);
            btnLocation.Click += (s, e) => OpenItemLocation();

            var delTpl = new ControlTemplate(typeof(Button));
            var delBorder = new FrameworkElementFactory(typeof(Border));
            delBorder.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            delBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            delBorder.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var delPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            delPresenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            delPresenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            delBorder.AppendChild(delPresenter);
            delTpl.VisualTree = delBorder;

            btnDelete = new Button
            {
                Height = 34,
                Padding = new Thickness(12, 0, 12, 0),
                FontSize = 12.5,
                FontWeight = FontWeights.Medium,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Template = delTpl
            };
            btnDelete.Background = new LinearGradientBrush(Color.FromRgb(239, 68, 68), Color.FromRgb(220, 38, 38), new Point(0, 0), new Point(1, 1));
            btnDelete.Click += async (s, e) => await DeleteSelectedItemAsync();

            actStack.Children.Add(btnRefresh);
            actStack.Children.Add(btnUndoLast);
            actStack.Children.Add(btnDisableAllStore);
            actStack.Children.Add(btnToggle);
            actStack.Children.Add(btnLocation);
            actStack.Children.Add(btnDelete);
            Grid.SetColumn(actStack, 2);
            actionsGrid.Children.Add(actStack);

            Grid.SetRow(actionsGrid, 1);
            toolGrid.Children.Add(actionsGrid);

            Grid.SetRow(toolGrid, 2);
            rootGrid.Children.Add(toolGrid);

            // 3. DataGrid
            cardGrid = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                ClipToBounds = true
            };

            gridStartup = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserResizeRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                SelectionMode = DataGridSelectionMode.Extended,
                BorderThickness = new Thickness(0),
                RowHeight = 35,
                FontSize = 12.5
            };

            // Selection CheckBox column (supports 1-click multi-select across every tab without holding Ctrl)
            colQuickCheck = CreateQuickCheckColumn();
            gridStartup.Columns.Add(colQuickCheck);

            // Name (with NEW / Ignored badges)
            colName = CreateNameColumn();
            gridStartup.Columns.Add(colName);

            // Publisher
            colPublisher = new DataGridTextColumn
            {
                Header = LocalizationService.T("StartupColPublisher", "Publisher"),
                Binding = new System.Windows.Data.Binding("Publisher"),
                MinWidth = 68,
                Width = new DataGridLength(0.72, DataGridLengthUnitType.Star),
                ElementStyle = CreateCellTextStyle(bidiLtr: true),
                IsReadOnly = true
            };
            gridStartup.Columns.Add(colPublisher);

            // Status
            colStatus = new DataGridTextColumn
            {
                Header = LocalizationService.T("StartupColStatus", "Status"),
                Binding = new System.Windows.Data.Binding("StatusDisplayLocalized"),
                MinWidth = 58,
                Width = new DataGridLength(60),
                ElementStyle = CreateCellTextStyle(bidiLtr: false),
                IsReadOnly = true
            };
            gridStartup.Columns.Add(colStatus);

            // Boot Impact
            colImpact = new DataGridTextColumn
            {
                Header = LocalizationService.T("StartupColImpact", "Boot Impact"),
                Binding = new System.Windows.Data.Binding("BootImpactDisplay"),
                MinWidth = 84,
                Width = new DataGridLength(86),
                ElementStyle = CreateCellTextStyle(bidiLtr: false),
                IsReadOnly = true
            };
            gridStartup.Columns.Add(colImpact);

            // Recommendation
            colRec = new DataGridTextColumn
            {
                Header = LocalizationService.T("StartupColRec", "Recommendation"),
                Binding = new System.Windows.Data.Binding("RecommendationDisplayLocalized"),
                MinWidth = 111,
                Width = new DataGridLength(112),
                ElementStyle = CreateCellTextStyle(bidiLtr: false),
                IsReadOnly = true
            };
            gridStartup.Columns.Add(colRec);

            // Location Type
            colLocation = new DataGridTextColumn
            {
                Header = LocalizationService.T("StartupColLocation", "Startup Type"),
                Binding = new System.Windows.Data.Binding("LocationDisplayLocalized"),
                MinWidth = 84,
                Width = new DataGridLength(86),
                ElementStyle = CreateCellTextStyle(bidiLtr: false),
                IsReadOnly = true
            };
            gridStartup.Columns.Add(colLocation);

            // Command / Path
            var cmdStyle = new Style(typeof(TextBlock));
            cmdStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            cmdStyle.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
            cmdStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(2, 0, 2, 0)));
            cmdStyle.Setters.Add(new Setter(FrameworkElement.FlowDirectionProperty,
                new System.Windows.Data.Binding("Command") { Converter = new LtrFlowDirectionConverter() }));
            colCommand = new DataGridTextColumn
            {
                Header = LocalizationService.IsArabic ? "الأمر / المسار" : "Command / Path",
                Binding = new System.Windows.Data.Binding("Command"),
                MinWidth = 90,
                Width = new DataGridLength(1.08, DataGridLengthUnitType.Star),
                ElementStyle = cmdStyle,
                IsReadOnly = true
            };
            gridStartup.Columns.Add(colCommand);

            // Operation (Ignore / Unignore)
            colOperation = CreateOperationColumn();
            gridStartup.Columns.Add(colOperation);

            cardGrid.Child = gridStartup;
            gridStartup.SelectionChanged += (s, e) => UpdateSelectionActionsState();
            gridStartup.Sorting += GridStartup_Sorting;

            var savedSortCol = SettingsService.Instance.Current.StartupSortColumn;
            if (!string.IsNullOrEmpty(savedSortCol) && gridStartup.Columns.Any(c => c.SortMemberPath == savedSortCol))
            {
                activeSortMemberPath = savedSortCol;
                activeSortDirection = SettingsService.Instance.Current.StartupSortAscending
                    ? System.ComponentModel.ListSortDirection.Ascending
                    : System.ComponentModel.ListSortDirection.Descending;
            }
            Grid.SetRow(cardGrid, 3);
            rootGrid.Children.Add(cardGrid);

            // 4. Inline Action Feedback Banner + Footer
            var bottomStack = new StackPanel();

            cardFeedbackBanner = new Border
            {
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 8, 10, 8),
                Margin = new Thickness(0, 10, 0, 0),
                Visibility = Visibility.Collapsed
            };
            var fbGrid = new Grid();
            fbGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            fbGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fbGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            fbGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            feedbackIconHost = new ContentControl
            {
                Margin = new Thickness(0, 0, 9, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(feedbackIconHost, 0);
            fbGrid.Children.Add(feedbackIconHost);

            lblFeedback = new TextBlock
            {
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(lblFeedback, 1);
            fbGrid.Children.Add(lblFeedback);

            btnFeedbackElevate = new Button
            {
                Height = 27,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(10, 0, 6, 0),
                FontSize = 11.5,
                FontWeight = FontWeights.Medium,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Visibility = Visibility.Collapsed
            };
            btnFeedbackElevate.Background = new LinearGradientBrush(Color.FromRgb(245, 158, 11), Color.FromRgb(217, 119, 6), new Point(0, 0), new Point(1, 1));
            btnFeedbackElevate.Click += (s, e) =>
            {
                if (!AdminPrivilegeService.TryRestartAsAdmin(out bool cancelled, out string? err) && !cancelled && !string.IsNullOrEmpty(err))
                {
                    AppDialog.Show(this, err, LocalizationService.T("AppTitle", "PC Boost"), AppDialogTone.Warning);
                }
            };
            Grid.SetColumn(btnFeedbackElevate, 2);
            fbGrid.Children.Add(btnFeedbackElevate);

            btnFeedbackDismiss = new Button
            {
                Content = "✕",
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                Margin = new Thickness(4, 0, 0, 0),
                FontSize = 11.5,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            btnFeedbackDismiss.Click += (s, e) => cardFeedbackBanner.Visibility = Visibility.Collapsed;
            Grid.SetColumn(btnFeedbackDismiss, 3);
            fbGrid.Children.Add(btnFeedbackDismiss);

            cardFeedbackBanner.Child = fbGrid;
            bottomStack.Children.Add(cardFeedbackBanner);

            lblFooter = new TextBlock
            {
                FontSize = 12,
                FontWeight = FontWeights.Normal,
                Margin = new Thickness(0, 10, 0, 0)
            };
            bottomStack.Children.Add(lblFooter);

            Grid.SetRow(bottomStack, 4);
            rootGrid.Children.Add(bottomStack);

            Content = rootGrid;

            ApplyLanguage();
            RefreshTheme();

            Loaded += async (s, e) =>
            {
                if (allItems.Count == 0) await LoadItemsAsync();
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

        private static Style CreateCellTextStyle(bool bidiLtr)
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(2, 0, 2, 0)));
            if (bidiLtr && LocalizationService.IsArabic)
            {
                // Keep Latin/parenthesized app & publisher names readable while right-aligning inside RTL tables
                style.Setters.Add(new Setter(FrameworkElement.FlowDirectionProperty, FlowDirection.LeftToRight));
                style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
            }
            return style;
        }

        private sealed class LtrFlowDirectionConverter : System.Windows.Data.IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                if (!LocalizationService.IsArabic) return FlowDirection.LeftToRight;
                if (value is string s && UITheme.ContainsArabic(s)) return FlowDirection.RightToLeft;
                return FlowDirection.LeftToRight;
            }

            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
                => System.Windows.Data.Binding.DoNothing;
        }

        private void UpdateActionButtonContents()
        {
            btnRefresh.Content = WpfIcons.CreateIconTextContent("Refresh", UITheme.StripLeadingIcon(LocalizationService.T("StartupBtnRefresh", "🔄 Refresh")), btnRefresh.Foreground, 15, 7);
            btnDisableAllStore.Content = WpfIcons.CreateIconTextContent("Alert", UITheme.StripLeadingIcon(LocalizationService.T("StartupBtnDisableAllStore", "🚫 Disable All to Never")), Brushes.White, 15, 7);
            btnLocation.Content = WpfIcons.CreateIconTextContent("Folder", UITheme.StripLeadingIcon(LocalizationService.T("StartupBtnLocation", "📂 Location")), btnLocation.Foreground, 15, 7);
            UpdateUndoButtonState();
            UpdateToggleAndDeleteButtonLabels(GetCheckedItems().Count);
        }

        private void UpdateToggleAndDeleteButtonLabels(int checkedCount)
        {
            string toggleBase = UITheme.StripLeadingIcon(LocalizationService.T("StartupBtnToggle", "⚡ Toggle"));
            string deleteBase = UITheme.StripLeadingIcon(LocalizationService.T("StartupBtnDelete", "🗑️ Delete"));
            string suffix = checkedCount > 0 ? $" ({checkedCount})" : string.Empty;
            btnToggle.Content = WpfIcons.CreateIconTextContent("Lightning", toggleBase + suffix, Brushes.White, 15, 7);
            btnDelete.Content = WpfIcons.CreateIconTextContent("Trash", deleteBase + suffix, Brushes.White, 15, 7);
        }

        public void ApplyLanguage()
        {
            lblHead.Text = UITheme.StripLeadingIcon(LocalizationService.T("StartupHeader", "🔄 Startup & Next-Reboot Manager"));
            lblSub.Text = LocalizationService.T("StartupSub", "Detect and manage programs, services, scheduled tasks, and Microsoft Store background apps at Windows boot.");
            lblSearchPlaceholder.Text = LocalizationService.T("StartupSearchPrompt", "Search...");
            UpdateActionButtonContents();

            if (colName != null)
            {
                colName.Header = LocalizationService.T("StartupColName", "Program / Service Name");
                colName.CellTemplate = CreateNameCellTemplate();
            }
            if (colPublisher != null)
            {
                colPublisher.Header = LocalizationService.T("StartupColPublisher", "Publisher");
                colPublisher.ElementStyle = CreateCellTextStyle(bidiLtr: true);
            }
            if (colStatus != null) colStatus.Header = LocalizationService.T("StartupColStatus", "Status");
            if (colImpact != null) colImpact.Header = LocalizationService.T("StartupColImpact", "Boot Impact");
            if (colRec != null) colRec.Header = LocalizationService.T("StartupColRec", "Recommendation");
            if (colLocation != null) colLocation.Header = LocalizationService.T("StartupColLocation", "Startup Type");
            if (colCommand != null) colCommand.Header = LocalizationService.IsArabic ? "الأمر / المسار" : "Command / Path";
            if (colOperation != null)
            {
                colOperation.Header = LocalizationService.T("StartupColAction", "Operation");
                colOperation.CellTemplate = CreateOperationCellTemplate();
            }

            foreach (var item in allItems)
            {
                if (item.LocationType == StartupType.MicrosoftStoreApp)
                {
                    item.Command = StartupService.BuildStoreAppCommandSummary(item.BackgroundPermissionMode, item.PackageFamilyName);
                }
            }

            UpdateTabCounts();
            ApplyFilter();
            gridStartup.Items.Refresh();
        }

        public void RefreshTheme()
        {
            lblHead.Foreground = WpfTheme.TextPrimaryBrush;
            lblSub.Foreground = WpfTheme.TextSecondaryBrush;
            lblFooter.Foreground = WpfTheme.TextSecondaryBrush;

            searchBorder.Background = WpfTheme.CardSubBrush;
            searchBorder.BorderBrush = WpfTheme.BorderBrush;
            txtSearch.Foreground = WpfTheme.TextPrimaryBrush;
            txtSearch.CaretBrush = WpfTheme.TextPrimaryBrush;
            lblSearchPlaceholder.Foreground = WpfTheme.TextSecondaryBrush;
            lblSearchPlaceholder.Opacity = 0.75;

            cardGrid.Background = WpfTheme.CardBrush;
            cardGrid.BorderBrush = WpfTheme.BorderBrush;

            gridStartup.Background = WpfTheme.CardBrush;
            gridStartup.Foreground = WpfTheme.TextPrimaryBrush;
            gridStartup.RowBackground = WpfTheme.CardBrush;
            gridStartup.AlternatingRowBackground = WpfTheme.CardSubBrush;
            gridStartup.BorderBrush = WpfTheme.BorderBrush;
            gridStartup.HorizontalGridLinesBrush = WpfTheme.BorderBrush;
            gridStartup.ColumnHeaderStyle = WpfTheme.CreateDataGridHeaderStyle();
            gridStartup.CellStyle = WpfTheme.CreateDataGridCellStyle();
            gridStartup.RowStyle = CreateStartupRowStyle();

            ApplySecondaryButtonStyle(btnRefresh);
            ApplySecondaryButtonStyle(btnUndoLast);
            ApplySecondaryButtonStyle(btnLocation);
            ApplySecondaryButtonStyle(btnMarkKnown);
            cardSummaryStrip.Background = WpfTheme.CardBrush;
            cardSummaryStrip.BorderBrush = WpfTheme.BorderBrush;
            ApplyFeedbackBannerTheme();
            if (colName != null) colName.CellTemplate = CreateNameCellTemplate();
            if (colOperation != null) colOperation.CellTemplate = CreateOperationCellTemplate();
            UpdateSummaryStrip();
            UpdateActionButtonContents();
            UpdateTabButtons();
            SyncSortIndicators();
        }

        private static Style CreateStartupRowStyle()
        {
            var baseStyle = WpfTheme.CreateDataGridRowStyle();
            var style = new Style(typeof(DataGridRow), baseStyle);
            var ignTrigger = new DataTrigger
            {
                Binding = new System.Windows.Data.Binding("IsIgnored"),
                Value = true
            };
            ignTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.70));
            style.Triggers.Add(ignTrigger);
            return style;
        }
    }
}
