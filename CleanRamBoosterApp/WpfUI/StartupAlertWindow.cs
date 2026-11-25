using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using CleanRamBoosterApp.Models;
using CleanRamBoosterApp.Services;
using CleanRamBoosterApp.UI;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using Image = System.Windows.Controls.Image;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;

namespace CleanRamBoosterApp.WpfUI
{
    /// <summary>
    /// Non-intrusive desktop toast dialog triggered when a new desktop startup entry
    /// or Microsoft Store background-enabled app is detected.
    /// </summary>
    public sealed class StartupAlertWindow : Window
    {
        private readonly StartupItem item;
        private readonly Border cardBorder;
        private readonly Border detailsBox;
        private readonly Button btnDetails;
        private readonly TextBlock txtDetailsLink;
        private readonly CheckBox chkIgnore;
        private readonly Button btnDisableNow;

        private bool isDetailsExpanded;
        private bool userDragged;
        private bool disableHandled;

        public bool ShouldIgnoreOnClose => chkIgnore.IsChecked == true;
        public bool WasDisabled => disableHandled;

        public event Action<StartupItem>? DisableRequested;
        public event Action<StartupItem>? Dismissed;
        public event Action? OpenSettingsRequested;

        public StartupAlertWindow(StartupItem detectedItem)
        {
            item = detectedItem;
            bool isAr = LocalizationService.IsArabic;
            bool isDark = WpfTheme.IsDarkMode;
            bool isStore = item.LocationType == StartupType.MicrosoftStoreApp;

            Title = LocalizationService.T("AppTitle", "PC Boost");
            Width = 520;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            FontFamily = LocalizationService.ActiveWpfFontFamily;
            FlowDirection = isAr ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

            WpfTheme.ApplyWindowResources(Resources);

            cardBorder = new Border
            {
                Margin = new Thickness(12),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Background = WpfTheme.CardBrush,
                BorderBrush = isDark
                    ? new SolidColorBrush(Color.FromRgb(56, 68, 84))
                    : new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    Opacity = isDark ? 0.55 : 0.22,
                    BlurRadius = 18,
                    ShadowDepth = 4
                }
            };

            var rootStack = new StackPanel
            {
                Margin = new Thickness(16, 12, 16, 15)
            };

            // ---------------------------------------------------------
            // 1. Top Title Bar (PC Boost + Settings Gear + Close X)
            // ---------------------------------------------------------
            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 12), Background = Brushes.Transparent };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    userDragged = true;
                    try { DragMove(); } catch { }
                }
            };

            var smallLogo = WpfTheme.CreateMiniBrandLogo(20);
            if (smallLogo != null)
            {
                smallLogo.Margin = new Thickness(0, 0, 8, 0);
                Grid.SetColumn(smallLogo, 0);
                headerGrid.Children.Add(smallLogo);
            }

            var lblBrand = new TextBlock
            {
                Text = LocalizationService.T("AppTitle", "PC Boost"),
                FontSize = 13.5,
                FontWeight = FontWeights.Medium,
                Foreground = WpfTheme.TextSecondaryBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lblBrand, 1);
            headerGrid.Children.Add(lblBrand);

            var btnSettings = CreateChromeIconButton(
                "Settings",
                LocalizationService.T("AlertOpenSettingsTip", "Startup Alert Settings"));
            btnSettings.Margin = new Thickness(0, 0, 4, 0);
            btnSettings.Click += (s, e) => OpenSettingsRequested?.Invoke();
            Grid.SetColumn(btnSettings, 2);
            headerGrid.Children.Add(btnSettings);

            var btnClose = CreateChromeIconButton(
                "CloseX",
                LocalizationService.T("BtnClose", "Close"));
            btnClose.Click += (s, e) => Close();
            Grid.SetColumn(btnClose, 3);
            headerGrid.Children.Add(btnClose);

            rootStack.Children.Add(headerGrid);

            // ---------------------------------------------------------
            // 2. Main Body (Amber Info Circle + Title / Subtitle / App Row / Details)
            // ---------------------------------------------------------
            var bodyGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var amberColor = isDark ? Color.FromRgb(251, 146, 60) : Color.FromRgb(217, 119, 6);
            var amberBrush = new SolidColorBrush(amberColor);

            var infoBadge = new Border
            {
                Width = 46,
                Height = 46,
                CornerRadius = new CornerRadius(23),
                Background = new SolidColorBrush(Color.FromArgb(24, amberColor.R, amberColor.G, amberColor.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(180, amberColor.R, amberColor.G, amberColor.B)),
                BorderThickness = new Thickness(1.6),
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 2, 0, 0),
                Child = WpfIcons.CreateIcon("InfoCircle", amberBrush, 26, 1.9)
            };
            Grid.SetColumn(infoBadge, 0);
            bodyGrid.Children.Add(infoBadge);

            var infoStack = new StackPanel();

            var lblTitle = new TextBlock
            {
                Text = LocalizationService.T("AlertNewStartupTitle", "A new startup item detected"),
                FontSize = 19,
                FontWeight = FontWeights.SemiBold,
                Foreground = amberBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 5)
            };
            infoStack.Children.Add(lblTitle);

            var lblSubtitle = new TextBlock
            {
                Text = isStore
                    ? LocalizationService.T("AlertNewStoreStartupSub", "This Microsoft Store app can run in the background at startup. Disable to set it to Never.")
                    : LocalizationService.T("AlertNewStartupSub", "It can be disabled to reduce your PC's boot time."),
                FontSize = 14.5,
                Foreground = WpfTheme.TextPrimaryBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 9)
            };
            infoStack.Children.Add(lblSubtitle);

            // Real Classification & Boot Impact Line
            var metaPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 11)
            };
            var lblSourceSummary = new TextBlock
            {
                Text = $"{item.LocationDisplayLocalized}  •  {item.BootImpactDisplay}",
                FontSize = 13,
                Foreground = WpfTheme.TextSecondaryBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 2, 8, 2)
            };
            metaPanel.Children.Add(lblSourceSummary);

            Color recColor = item.Recommendation switch
            {
                StartupRecommendation.SafeToDisable => WpfTheme.ColorSuccess,
                StartupRecommendation.Necessary => WpfTheme.ColorPrimary,
                _ => amberColor
            };
            var recTag = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8, 2.5, 8, 2.5),
                Margin = new Thickness(0, 1, 0, 1),
                Background = new SolidColorBrush(Color.FromArgb(30, recColor.R, recColor.G, recColor.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, recColor.R, recColor.G, recColor.B)),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = item.RecommendationDisplayLocalized,
                    FontSize = 12.5,
                    FontWeight = FontWeights.Medium,
                    Foreground = new SolidColorBrush(recColor)
                }
            };
            metaPanel.Children.Add(recTag);
            infoStack.Children.Add(metaPanel);

            // Item Icon + App Name + Clickable "Details" Link
            var appRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            appRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            appRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            appRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var appIconVisual = CreateAppIconVisual(item);
            appIconVisual.Margin = new Thickness(0, 0, 10, 0);
            Grid.SetColumn(appIconVisual, 0);
            appRow.Children.Add(appIconVisual);

            var lblAppName = new TextBlock
            {
                Text = isAr && !UITheme.ContainsArabic(item.Name) ? $"\u200E{item.Name}\u200E" : item.Name,
                FontSize = 15.5,
                FontWeight = FontWeights.Medium,
                Foreground = WpfTheme.TextPrimaryBrush,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                TextAlignment = TextAlignment.Left,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = item.Name
            };
            Grid.SetColumn(lblAppName, 1);
            appRow.Children.Add(lblAppName);

            var linkColor = isDark ? Color.FromRgb(96, 165, 250) : Color.FromRgb(37, 99, 235);
            txtDetailsLink = new TextBlock
            {
                Text = LocalizationService.T("AlertDetailsShow", "Details"),
                FontSize = 14.5,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(linkColor),
                TextDecorations = TextDecorations.Underline,
                VerticalAlignment = VerticalAlignment.Center
            };
            btnDetails = new Button
            {
                Content = txtDetailsLink,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 2, 4, 2),
                Margin = new Thickness(8, 0, 0, 0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Template = WpfTheme.CreateFlatButtonTemplate()
            };
            btnDetails.Click += (s, e) => SetDetailsExpanded(!isDetailsExpanded);
            Grid.SetColumn(btnDetails, 2);
            appRow.Children.Add(btnDetails);

            infoStack.Children.Add(appRow);

            // Expandable Details Card (collapsed by default)
            detailsBox = BuildDetailsCard(isDark, isAr, linkColor);
            detailsBox.Visibility = Visibility.Collapsed;
            infoStack.Children.Add(detailsBox);

            Grid.SetColumn(infoStack, 1);
            bodyGrid.Children.Add(infoStack);
            rootStack.Children.Add(bodyGrid);

            // ---------------------------------------------------------
            // 3. Footer Row ("Ignore this startup item" + "Disable Now")
            // ---------------------------------------------------------
            var footerGrid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            chkIgnore = new CheckBox
            {
                Content = LocalizationService.T("AlertIgnoreItem", "Ignore this startup item"),
                FontSize = 14,
                Foreground = WpfTheme.TextSecondaryBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            Grid.SetColumn(chkIgnore, 0);
            footerGrid.Children.Add(chkIgnore);

            btnDisableNow = new Button
            {
                Content = LocalizationService.T("AlertDisableNow", "Disable Now"),
                Height = 40,
                MinWidth = 144,
                Padding = new Thickness(20, 0, 20, 0),
                FontSize = 14.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Template = WpfTheme.CreateRoundedCtaTemplate(
                    Color.FromRgb(59, 130, 246),
                    Color.FromRgb(37, 99, 235),
                    Color.FromRgb(96, 165, 250),
                    Color.FromRgb(59, 130, 246))
            };
            btnDisableNow.Click += (s, e) =>
            {
                disableHandled = true;
                DisableRequested?.Invoke(item);
            };
            Grid.SetColumn(btnDisableNow, 1);
            footerGrid.Children.Add(btnDisableNow);

            rootStack.Children.Add(footerGrid);

            cardBorder.Child = rootStack;
            Content = cardBorder;

            SourceInitialized += (s, e) => AnchorBottomRight();
            Loaded += (s, e) => AnchorBottomRight();
            SizeChanged += (s, e) =>
            {
                if (!userDragged && IsLoaded)
                {
                    AnchorBottomRight();
                }
            };
            Closed += (s, e) => Dismissed?.Invoke(item);
        }

        public void SetDetailsExpanded(bool expanded)
        {
            isDetailsExpanded = expanded;
            detailsBox.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            txtDetailsLink.Text = expanded
                ? LocalizationService.T("AlertDetailsHide", "Hide Details")
                : LocalizationService.T("AlertDetailsShow", "Details");
        }

        private Border BuildDetailsCard(bool isDark, bool isAr, Color linkColor)
        {
            var box = new Border
            {
                Margin = new Thickness(0, 6, 0, 0),
                Padding = new Thickness(11, 9, 11, 9),
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(1),
                Background = isDark
                    ? new SolidColorBrush(Color.FromRgb(13, 17, 23))
                    : new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                BorderBrush = isDark
                    ? new SolidColorBrush(Color.FromRgb(48, 54, 61))
                    : new SolidColorBrush(Color.FromRgb(226, 232, 240))
            };

            var stack = new StackPanel();
            bool isStore = item.LocationType == StartupType.MicrosoftStoreApp;

            // 1. Primary Executable / File Path Row (clickable to open Explorer when a file/folder path exists)
            string primaryPath = item.FilePath;
            bool hasFilePath = !string.IsNullOrWhiteSpace(primaryPath);
            string pathLabel = isStore
                ? LocalizationService.T("AlertDetailStorePath", "App File / Path:")
                : LocalizationService.T("AlertDetailAutoRun", "Auto Run Program:");

            if (hasFilePath)
            {
                stack.Children.Add(CreatePathRow(pathLabel, primaryPath, linkColor, isAr));
            }
            else if (!string.IsNullOrWhiteSpace(item.Command))
            {
                string nonPathLabel = item.LocationType == StartupType.ScheduledTask
                    ? LocalizationService.T("AlertDetailTaskName", "Task Name:")
                    : pathLabel;
                stack.Children.Add(CreateInfoRow(nonPathLabel, item.Command, isAr, forceLtrValue: true));
            }

            // 2. Publisher Row
            string pub = !string.IsNullOrWhiteSpace(item.Publisher)
                ? item.Publisher
                : (isAr ? "ناشر غير معروف" : "Unknown Publisher");
            stack.Children.Add(CreateInfoRow(
                LocalizationService.T("AlertDetailPublisher", "Publisher:"),
                pub,
                isAr,
                forceLtrValue: !UITheme.ContainsArabic(pub)));

            // 3. Startup Source / Key or Package ID
            if (isStore)
            {
                if (!string.IsNullOrWhiteSpace(item.InstallFolder) &&
                    !string.Equals(item.InstallFolder, primaryPath, StringComparison.OrdinalIgnoreCase))
                {
                    stack.Children.Add(CreatePathRow(
                        LocalizationService.T("AlertDetailInstallFolder", "Install Folder:"),
                        item.InstallFolder,
                        linkColor,
                        isAr));
                }

                if (!string.IsNullOrWhiteSpace(item.PackageFamilyName))
                {
                    stack.Children.Add(CreateInfoRow(
                        LocalizationService.T("AlertDetailPackage", "Package ID:"),
                        item.PackageFamilyName,
                        isAr,
                        forceLtrValue: true));
                }

                string localizedPerm = item.BackgroundPermissionMode switch
                {
                    "Never" => isAr ? "أبداً" : "Never",
                    "Always" => isAr ? "دائماً" : "Always",
                    _ => isAr ? "محسّن للطاقة" : "Power Optimized"
                };

                stack.Children.Add(CreateInfoRow(
                    LocalizationService.T("AlertDetailSource", "Startup Source:"),
                    $"{item.LocationDisplayLocalized} ({localizedPerm})",
                    isAr,
                    forceLtrValue: false));
            }
            else
            {
                string sourceText = !string.IsNullOrEmpty(item.OriginalRegistryKey)
                    ? (isAr ? $"{item.LocationDisplayLocalized} • \u200E{item.OriginalRegistryKey}\u200E" : $"{item.LocationDisplayLocalized} • {item.OriginalRegistryKey}")
                    : item.LocationDisplayLocalized;
                stack.Children.Add(CreateInfoRow(
                    LocalizationService.T("AlertDetailSource", "Startup Source:"),
                    sourceText,
                    isAr,
                    forceLtrValue: !UITheme.ContainsArabic(sourceText)));

                if (!string.IsNullOrWhiteSpace(item.Command) &&
                    hasFilePath &&
                    !string.Equals(item.Command.Trim('\"'), primaryPath.Trim('\"'), StringComparison.OrdinalIgnoreCase))
                {
                    stack.Children.Add(CreateInfoRow(
                        LocalizationService.T("AlertDetailCommand", "Launch Command:"),
                        item.Command,
                        isAr,
                        forceLtrValue: true));
                }
            }

            box.Child = stack;
            return box;
        }

        private Grid CreatePathRow(string label, string pathValue, Color linkColor, bool isAr)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var lbl = new TextBlock
            {
                Text = label,
                FontSize = 13,
                Foreground = WpfTheme.TextSecondaryBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 7, 0)
            };
            Grid.SetColumn(lbl, 0);
            row.Children.Add(lbl);

            var linkText = new TextBlock
            {
                Text = pathValue,
                FontSize = 13,
                Foreground = new SolidColorBrush(linkColor),
                TextDecorations = TextDecorations.Underline,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FlowDirection = FlowDirection.LeftToRight,
                TextAlignment = isAr ? TextAlignment.Right : TextAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };

            var linkBtn = new Button
            {
                Content = linkText,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = pathValue,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Template = WpfTheme.CreateFlatButtonTemplate()
            };
            linkBtn.Click += (s, e) => OpenPathInExplorer(pathValue);
            Grid.SetColumn(linkBtn, 1);
            row.Children.Add(linkBtn);

            var copyBtn = new Button
            {
                Width = 24,
                Height = 22,
                Margin = new Thickness(6, 0, 0, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = LocalizationService.T("AlertCopyPathTip", "Copy path"),
                Template = WpfTheme.CreateFlatButtonTemplate(enableHoverBg: true),
                Content = WpfIcons.CreateIcon("Copy", WpfTheme.TextSecondaryBrush, 14.5, 1.75)
            };
            copyBtn.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(pathValue);
                    copyBtn.Content = new TextBlock
                    {
                        Text = "✓",
                        FontSize = 13,
                        FontWeight = FontWeights.Bold,
                        Foreground = WpfTheme.SuccessBrush,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                }
                catch { }
            };
            Grid.SetColumn(copyBtn, 2);
            row.Children.Add(copyBtn);

            return row;
        }

        private StackPanel CreateInfoRow(string label, string value, bool isAr, bool forceLtrValue)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lbl = new TextBlock
            {
                Text = label,
                FontSize = 13,
                Foreground = WpfTheme.TextSecondaryBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 7, 0)
            };
            Grid.SetColumn(lbl, 0);
            row.Children.Add(lbl);

            var val = new TextBlock
            {
                Text = forceLtrValue && isAr ? $"\u200E{value}\u200E" : value,
                FontSize = 13,
                Foreground = WpfTheme.TextPrimaryBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = value,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                TextAlignment = TextAlignment.Left
            };
            Grid.SetColumn(val, 1);
            row.Children.Add(val);

            var wrapper = new StackPanel();
            wrapper.Children.Add(row);
            return wrapper;
        }

        private static void OpenPathInExplorer(string path)
        {
            try
            {
                string clean = path.Trim('\"');
                if (File.Exists(clean))
                {
                    Process.Start("explorer.exe", $"/select,\"{clean}\"");
                }
                else if (Directory.Exists(clean))
                {
                    Process.Start("explorer.exe", $"\"{clean}\"");
                }
                else
                {
                    string? parent = Path.GetDirectoryName(clean);
                    if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                    {
                        Process.Start("explorer.exe", $"\"{parent}\"");
                    }
                }
            }
            catch { }
        }

        private void AnchorBottomRight()
        {
            try
            {
                var workArea = SystemParameters.WorkArea;
                double w = ActualWidth > 0 ? ActualWidth : Width;
                double h = ActualHeight > 0 ? ActualHeight : 230;
                Left = Math.Max(workArea.Left, workArea.Right - w - 12);
                Top = Math.Max(workArea.Top, workArea.Bottom - h - 12);
            }
            catch { }
        }

        private static Button CreateChromeIconButton(string iconKey, string tooltip)
        {
            return new Button
            {
                Width = 26,
                Height = 26,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Template = WpfTheme.CreateFlatButtonTemplate(enableHoverBg: true),
                Content = WpfIcons.CreateIcon(iconKey, WpfTheme.TextSecondaryBrush, 15, 1.85)
            };
        }

        private static FrameworkElement CreateAppIconVisual(StartupItem item)
        {
            try
            {
                // 1. Store App PNG asset from AppxManifest
                if (!string.IsNullOrEmpty(item.IconAssetPath) && File.Exists(item.IconAssetPath))
                {
                    using var fs = new FileStream(item.IconAssetPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.DecodePixelWidth = 64;
                    bmp.StreamSource = fs;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();

                    var img = new Image { Source = bmp, Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                    return img;
                }

                // 2. Associated icon from EXE / LNK
                string path = item.FilePath ?? string.Empty;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                    if (icon != null)
                    {
                        var src = Imaging.CreateBitmapSourceFromHIcon(
                            icon.Handle,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromWidthAndHeight(36, 36));
                        src.Freeze();
                        var img = new Image { Source = src, Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
                        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                        return img;
                    }
                }
            }
            catch { }

            string fallbackKey = item.LocationType == StartupType.MicrosoftStoreApp ? "Store" : "Startup";
            return WpfIcons.CreateIcon(fallbackKey, WpfTheme.PrimaryBrush, 22, 1.8);
        }
    }
}
