using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CleanRamBoosterApp.Services;
using Microsoft.Win32;

namespace CleanRamBoosterApp.WpfUI.Toolbox
{
    /// <summary>
    /// Shared pure-WPF base window for all Power User Toolbox utilities.
    /// Provides unified Dark/Light DWM titlebar theming, crisp HiDPI scaling,
    /// Arabic RTL FlowDirection, app icon binding, and consistent cards/buttons/DataGrids.
    /// </summary>
    public abstract class ToolboxWindowBase : Window
    {
        protected ToolboxWindowBase(string titleEn, string titleAr, double width, double height, bool resizable = false)
        {
            bool isAr = LocalizationService.IsArabic;
            Title = isAr ? titleAr : titleEn;
            Width = width;
            Height = height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = true;
            ResizeMode = resizable ? ResizeMode.CanResize : ResizeMode.CanMinimize;
            FlowDirection = isAr ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            FontFamily = new FontFamily(LocalizationService.ActiveFontFamilyName);
            Background = WpfTheme.BgBrush;
            Foreground = WpfTheme.TextPrimaryBrush;

            var iconSrc = WpfTheme.GetWindowIconSource();
            if (iconSrc != null) Icon = iconSrc;

            WpfTheme.ApplyWindowResources(Resources);

            SourceInitialized += (_, _) => WpfTheme.ApplyDwmDarkMode(this, WpfTheme.IsDarkMode);
            Loaded += (_, _) => WpfTheme.ApplyDwmDarkMode(this, WpfTheme.IsDarkMode);
        }

        public virtual void RefreshTheme()
        {
            WpfTheme.ApplyWindowResources(Resources);
            WpfTheme.ApplyDwmDarkMode(this, WpfTheme.IsDarkMode);
            Background = WpfTheme.BgBrush;
            Foreground = WpfTheme.TextPrimaryBrush;
        }

        protected static Grid BuildHeader(string iconKey, Color accent, string title, string subtitle, out TextBlock lblTitle, out TextBlock lblSub)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var badge = WpfIcons.CreateHeaderBadge(iconKey, accent, 40, 20);
            badge.Margin = new Thickness(0, 0, 12, 0);
            Grid.SetColumn(badge, 0);
            row.Children.Add(badge);

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            lblTitle = new TextBlock
            {
                Text = title,
                FontSize = 18.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = WpfTheme.TextPrimaryBrush
            };
            lblSub = new TextBlock
            {
                Text = subtitle,
                FontSize = 12.5,
                Foreground = WpfTheme.TextSecondaryBrush,
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            stack.Children.Add(lblTitle);
            stack.Children.Add(lblSub);
            Grid.SetColumn(stack, 1);
            row.Children.Add(stack);

            return row;
        }

        protected static Border CreateCard(Thickness? padding = null)
        {
            return new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Background = WpfTheme.CardBrush,
                BorderBrush = WpfTheme.BorderBrush,
                Padding = padding ?? new Thickness(14, 12, 14, 12)
            };
        }

        protected static Button CreatePrimaryButton(string iconKey, string text, double height = 34)
        {
            var btn = new Button
            {
                Height = height,
                Padding = new Thickness(14, 0, 14, 0),
                FontSize = 12.5,
                FontWeight = FontWeights.Medium,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Background = new LinearGradientBrush(Color.FromRgb(59, 130, 246), Color.FromRgb(37, 99, 235), new Point(0, 0), new Point(1, 1))
            };
            btn.Content = WpfIcons.CreateIconTextContent(iconKey, text, Brushes.White, 15, 7);
            return btn;
        }

        protected static Button CreateDangerButton(string iconKey, string text, double height = 34)
        {
            var btn = new Button
            {
                Height = height,
                Padding = new Thickness(14, 0, 14, 0),
                FontSize = 12.5,
                FontWeight = FontWeights.Medium,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Background = new LinearGradientBrush(Color.FromRgb(239, 68, 68), Color.FromRgb(220, 38, 38), new Point(0, 0), new Point(1, 1))
            };
            btn.Content = WpfIcons.CreateIconTextContent(iconKey, text, Brushes.White, 15, 7);
            return btn;
        }

        protected static Button CreateSecondaryButton(string? iconKey, string text, double height = 34)
        {
            var btn = new Button
            {
                Height = height,
                Padding = new Thickness(13, 0, 13, 0),
                FontSize = 12.5,
                FontWeight = FontWeights.Normal,
                Cursor = Cursors.Hand
            };
            ApplySecondaryButtonStyle(btn);
            btn.Content = string.IsNullOrEmpty(iconKey)
                ? text
                : WpfIcons.CreateIconTextContent(iconKey, text, btn.Foreground, 14, 6);
            return btn;
        }

        protected static void ApplySecondaryButtonStyle(Button btn)
        {
            bool isDark = WpfTheme.IsDarkMode;
            btn.Background = new SolidColorBrush(isDark ? Color.FromRgb(33, 38, 45) : Color.FromRgb(241, 245, 249));
            btn.Foreground = new SolidColorBrush(isDark ? Color.FromRgb(201, 209, 217) : Color.FromRgb(30, 41, 59));
            btn.BorderBrush = new SolidColorBrush(isDark ? Color.FromRgb(48, 54, 61) : Color.FromRgb(226, 232, 240));
            btn.BorderThickness = new Thickness(1);
        }

        protected static TextBox CreatePathTextBox(string initialText)
        {
            return new TextBox
            {
                Text = initialText,
                Height = 32,
                FontSize = 12.5,
                FlowDirection = FlowDirection.LeftToRight,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(9, 0, 9, 0),
                Background = WpfTheme.CardSubBrush,
                Foreground = WpfTheme.TextPrimaryBrush,
                CaretBrush = WpfTheme.TextPrimaryBrush,
                BorderBrush = WpfTheme.BorderBrush,
                BorderThickness = new Thickness(1)
            };
        }

        protected static DataGrid CreateVirtualDataGrid(DataGridSelectionMode selectionMode = DataGridSelectionMode.Extended)
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserResizeRows = false,
                CanUserReorderColumns = false,
                IsReadOnly = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                SelectionMode = selectionMode,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                BorderThickness = new Thickness(0),
                RowHeight = 32,
                FontSize = 12.5,
                Background = WpfTheme.CardBrush,
                Foreground = WpfTheme.TextPrimaryBrush,
                RowBackground = WpfTheme.CardBrush,
                AlternatingRowBackground = WpfTheme.CardSubBrush,
                HorizontalGridLinesBrush = WpfTheme.BorderBrush,
                EnableRowVirtualization = true,
                EnableColumnVirtualization = false,
                ColumnHeaderStyle = WpfTheme.CreateDataGridHeaderStyle(),
                CellStyle = WpfTheme.CreateDataGridCellStyle(),
                RowStyle = WpfTheme.CreateDataGridRowStyle()
            };
            VirtualizingPanel.SetIsVirtualizing(grid, true);
            VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
            ScrollViewer.SetCanContentScroll(grid, true);
            return grid;
        }

        protected static void StyleDataGridForTheme(DataGrid grid)
        {
            grid.Background = WpfTheme.CardBrush;
            grid.Foreground = WpfTheme.TextPrimaryBrush;
            grid.RowBackground = WpfTheme.CardBrush;
            grid.AlternatingRowBackground = WpfTheme.CardSubBrush;
            grid.HorizontalGridLinesBrush = WpfTheme.BorderBrush;
            grid.ColumnHeaderStyle = WpfTheme.CreateDataGridHeaderStyle();
            grid.CellStyle = WpfTheme.CreateDataGridCellStyle();
            grid.RowStyle = WpfTheme.CreateDataGridRowStyle();
        }

        protected static Style CreateEllipsisTextStyle(bool forceLtr = false)
        {
            var st = new Style(typeof(TextBlock));
            st.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            st.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
            st.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(3, 0, 3, 0)));
            if (forceLtr)
            {
                st.Setters.Add(new Setter(FrameworkElement.FlowDirectionProperty, FlowDirection.LeftToRight));
                if (LocalizationService.IsArabic)
                    st.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
            }
            return st;
        }

        protected string? PickFolder(string currentPath)
        {
            var dlg = new OpenFolderDialog
            {
                Title = LocalizationService.IsArabic ? "اختر مجلداً" : "Select Folder",
                Multiselect = false
            };
            if (!string.IsNullOrWhiteSpace(currentPath) && System.IO.Directory.Exists(currentPath))
            {
                dlg.InitialDirectory = currentPath;
            }
            return dlg.ShowDialog(this) == true ? dlg.FolderName : null;
        }
    }
}
