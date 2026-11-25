using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CleanRamBoosterApp.Services;
using CleanRamBoosterApp.UI;

namespace CleanRamBoosterApp.WpfUI
{
    public class ToolboxView : UserControl
    {
        private readonly TextBlock lblHead;
        private readonly TextBlock lblSub;
        private readonly WrapPanel wrapPanel;
        private readonly ScrollViewer scrollViewer;
        private readonly Dictionary<string, Toolbox.ToolboxWindowBase> openToolWindows = new(StringComparer.OrdinalIgnoreCase);

        public ToolboxView()
        {
            var rootGrid = new Grid
            {
                Margin = new Thickness(24, 20, 24, 20)
            };
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Cards Grid

            // 1. Header
            var headRow = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var headBadge = WpfIcons.CreateHeaderBadge("Toolbox", WpfTheme.ColorPurple, 42, 22);
            headBadge.Margin = new Thickness(0, 0, 14, 0);
            Grid.SetColumn(headBadge, 0);
            headRow.Children.Add(headBadge);

            var headStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            lblHead = new TextBlock
            {
                Text = UITheme.StripLeadingIcon(LocalizationService.T("ToolboxHeader", "🧰 All-In-One Power User Toolbox")),
                FontSize = 20,
                FontWeight = FontWeights.SemiBold
            };
            lblSub = new TextBlock
            {
                Text = LocalizationService.T("ToolboxSub", "Handy suite of native repair tools, storage analyzers, and hidden Windows power controls."),
                FontSize = 13,
                Margin = new Thickness(0, 3, 0, 0)
            };
            headStack.Children.Add(lblHead);
            headStack.Children.Add(lblSub);
            Grid.SetColumn(headStack, 1);
            headRow.Children.Add(headStack);
            Grid.SetRow(headRow, 0);
            rootGrid.Children.Add(headRow);

            // 2. WrapPanel inside ScrollViewer
            scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            wrapPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal
            };
            scrollViewer.Content = wrapPanel;

            Grid.SetRow(scrollViewer, 1);
            rootGrid.Children.Add(scrollViewer);

            Content = rootGrid;

            BuildToolCards();
            ApplyLanguage();
            RefreshTheme();
        }

        private void OpenToolWindow(string toolKey, Func<Toolbox.ToolboxWindowBase> factory)
        {
            if (openToolWindows.TryGetValue(toolKey, out var existing) && existing.IsLoaded)
            {
                if (existing.WindowState == WindowState.Minimized)
                {
                    existing.WindowState = WindowState.Normal;
                }
                existing.Show();
                existing.Activate();
                return;
            }

            var win = factory();
            win.ShowInTaskbar = true;
            win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            openToolWindows[toolKey] = win;
            win.Closed += (_, _) => openToolWindows.Remove(toolKey);
            win.Show();
            win.Activate();
        }

        public void CloseAllOpenToolWindows()
        {
            var snapshot = openToolWindows.Values.ToList();
            openToolWindows.Clear();
            foreach (var win in snapshot)
            {
                try { win.Close(); } catch { }
            }
        }

        public int OpenToolWindowCount => openToolWindows.Count(kv => kv.Value.IsLoaded);

        private void BuildToolCards()
        {
            wrapPanel.Children.Clear();

            // 1. Win Fix
            AddToolCard(
                "Wrench",
                LocalizationService.T("ToolWinFixTitle", "🔧 Win Fix"),
                LocalizationService.T("TagRepair", "REPAIR"),
                WpfTheme.ColorPrimary,
                LocalizationService.T("ToolWinFixDesc", "1-Click repair for broken desktop icons, flush DNS & winsock, and restart Windows Explorer."),
                () => OpenToolWindow("WinFix", () => new Toolbox.ToolboxWinFixWindow()),
                requiresAdmin: true);

            // 2. System Information
            AddToolCard(
                "Monitor",
                LocalizationService.T("ToolSysInfoTitle", "🖥️ System Information"),
                LocalizationService.T("TagSystem", "SYSTEM"),
                WpfTheme.ColorPurple,
                LocalizationService.T("ToolSysInfoDesc", "Hardware specs: CPU model/cores/clock, GPU, RAM, Motherboard, BIOS, Disks, and Uptime."),
                () => OpenToolWindow("SystemInfo", () => new Toolbox.ToolboxSystemInfoWindow()),
                requiresAdmin: false);

            // 3. Auto Shutdown
            AddToolCard(
                "Clock",
                LocalizationService.T("ToolAutoShutdownTitle", "⏰ Auto Shutdown"),
                LocalizationService.T("TagSystem", "SYSTEM"),
                WpfTheme.ColorPurple,
                LocalizationService.T("ToolAutoShutdownDesc", "Schedule automatic PC shutdown, restart, sleep, or hibernate with countdown timer."),
                () => OpenToolWindow("AutoShutdown", () => new Toolbox.ToolboxAutoShutdownWindow()),
                requiresAdmin: true);

            // 4. Duplicate Finder
            AddToolCard(
                "SearchFiles",
                LocalizationService.T("ToolDuplicateTitle", "🔍 Duplicate File Finder"),
                LocalizationService.T("TagStorage", "STORAGE"),
                WpfTheme.ColorSuccess,
                LocalizationService.T("ToolDuplicateDesc", "Scan directory trees and eliminate identical duplicate files by size and MD5 hash."),
                () => OpenToolWindow("DuplicateFinder", () => new Toolbox.ToolboxDuplicateFinderWindow()),
                requiresAdmin: true);

            // 5. Large Files Finder
            AddToolCard(
                "Box",
                LocalizationService.T("ToolLargeFilesTitle", "📦 Large File Finder"),
                LocalizationService.T("TagStorage", "STORAGE"),
                WpfTheme.ColorSuccess,
                LocalizationService.T("ToolLargeFilesDesc", "Identify and locate storage space hogs (>100MB, >500MB, >1GB) in seconds."),
                () => OpenToolWindow("LargeFiles", () => new Toolbox.ToolboxLargeFilesWindow()),
                requiresAdmin: true);

            // 6. Empty Folders Scanner
            AddToolCard(
                "Folder",
                LocalizationService.T("ToolEmptyFoldersTitle", "📁 Empty Folder Scanner"),
                LocalizationService.T("TagStorage", "STORAGE"),
                WpfTheme.ColorSuccess,
                LocalizationService.T("ToolEmptyFoldersDesc", "Recursively search directory trees and clean abandoned empty folder structures."),
                () => OpenToolWindow("EmptyFolders", () => new Toolbox.ToolboxEmptyFoldersWindow()),
                requiresAdmin: true);

            // 7. Default Programs
            AddToolCard(
                "Globe",
                LocalizationService.T("ToolDefaultAppsTitle", "🌐 Default Programs"),
                LocalizationService.T("TagWindows", "WINDOWS"),
                WpfTheme.ColorCyan,
                LocalizationService.T("ToolDefaultAppsDesc", "Quick access to Windows default apps and protocol associations settings."),
                () => Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true }),
                requiresAdmin: false);

            // 8. System Control (All Tasks)
            AddToolCard(
                "Sliders",
                LocalizationService.T("ToolSystemControlTitle", "⚙️ System Control"),
                LocalizationService.T("TagWindows", "WINDOWS"),
                WpfTheme.ColorCyan,
                LocalizationService.T("ToolSystemControlDesc", "Direct master access to hidden Windows settings and the complete system control panel."),
                () => Process.Start(new ProcessStartInfo("explorer.exe", "shell:::{ED7BA470-8E54-465E-825C-99712043E01C}") { UseShellExecute = true }),
                requiresAdmin: false);
        }

        private void AddToolCard(string iconKey, string title, string tag, Color tagColor, string description, Action onOpen, bool requiresAdmin = false)
        {
            var card = new Border
            {
                Width = 390,
                MinHeight = 148,
                Margin = new Thickness(0, 0, 16, 16),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Background = WpfTheme.CardBrush,
                BorderBrush = WpfTheme.BorderBrush,
                Padding = new Thickness(16, 14, 16, 14)
            };

            var outer = new Grid();
            outer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Upscaled 44px icon tile
            outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Body

            var iconTile = WpfIcons.CreateHeaderBadge(iconKey, tagColor, 44, 22);
            iconTile.VerticalAlignment = VerticalAlignment.Top;
            iconTile.Margin = new Thickness(0, 2, 14, 0);
            Grid.SetColumn(iconTile, 0);
            outer.Children.Add(iconTile);

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Title + Tag
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Desc
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Open button

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleText = new TextBlock
            {
                Text = UITheme.StripLeadingIcon(title),
                FontSize = 15,
                FontWeight = FontWeights.Medium,
                Foreground = WpfTheme.TextPrimaryBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(titleText, 0);
            headerGrid.Children.Add(titleText);

            var tagBadge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(35, tagColor.R, tagColor.G, tagColor.B)),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var tagText = new TextBlock
            {
                Text = tag,
                FontSize = 10.5,
                FontWeight = FontWeights.Normal,
                Foreground = new SolidColorBrush(tagColor)
            };
            tagBadge.Child = tagText;
            Grid.SetColumn(tagBadge, 1);
            headerGrid.Children.Add(tagBadge);

            Grid.SetRow(headerGrid, 0);
            grid.Children.Add(headerGrid);

            var descText = new TextBlock
            {
                Text = description,
                FontSize = 12.5,
                FontWeight = FontWeights.Normal,
                Foreground = WpfTheme.TextSecondaryBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 7, 0, 10),
                LineHeight = 18
            };
            Grid.SetRow(descText, 1);
            grid.Children.Add(descText);

            var btnOpen = new Button
            {
                Content = LocalizationService.T("BtnOpen", "Open"),
                Height = 30,
                MinWidth = 84,
                Padding = new Thickness(14, 0, 14, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                FontSize = 12.5,
                FontWeight = FontWeights.Normal,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            ApplySecondaryButtonStyle(btnOpen);
            string cleanToolName = UITheme.StripLeadingIcon(title);
            btnOpen.Click += (s, e) =>
            {
                if (requiresAdmin && !AdminPrivilegeService.IsRunningAsAdmin())
                {
                    if (!AdminPrivilegeService.EnsureAdminOrPrompt(this, cleanToolName, cleanToolName))
                        return;
                }
                try
                {
                    onOpen();
                }
                catch (Exception ex)
                {
                    AppDialog.Show(this,
                        LocalizationService.IsArabic
                            ? $"تعذر فتح «{cleanToolName}»: {ex.Message}"
                            : $"Could not open \"{cleanToolName}\": {ex.Message}",
                        LocalizationService.T("AppTitle", "PC Boost"),
                        AppDialogTone.Warning);
                }
            };
            Grid.SetRow(btnOpen, 2);
            grid.Children.Add(btnOpen);

            Grid.SetColumn(grid, 1);
            outer.Children.Add(grid);

            card.Child = outer;
            wrapPanel.Children.Add(card);
        }

        private static void ApplySecondaryButtonStyle(Button btn)
        {
            bool isDark = WpfTheme.IsDarkMode;
            btn.Background = new SolidColorBrush(isDark ? Color.FromRgb(33, 38, 45) : Color.FromRgb(241, 245, 249));
            btn.Foreground = new SolidColorBrush(isDark ? Color.FromRgb(201, 209, 217) : Color.FromRgb(30, 41, 59));
            btn.BorderBrush = new SolidColorBrush(isDark ? Color.FromRgb(48, 54, 61) : Color.FromRgb(226, 232, 240));
            btn.BorderThickness = new Thickness(1);
        }

        public void ApplyLanguage()
        {
            lblHead.Text = UITheme.StripLeadingIcon(LocalizationService.T("ToolboxHeader", "🧰 All-In-One Power User Toolbox"));
            lblSub.Text = LocalizationService.T("ToolboxSub", "Handy suite of native repair tools, storage analyzers, and hidden Windows power controls.");
            BuildToolCards();
        }

        public void RefreshTheme()
        {
            lblHead.Foreground = WpfTheme.TextPrimaryBrush;
            lblSub.Foreground = WpfTheme.TextSecondaryBrush;
            BuildToolCards();
            foreach (var win in openToolWindows.Values.ToList())
            {
                try { if (win.IsLoaded) win.RefreshTheme(); } catch { }
            }
        }
    }
}
