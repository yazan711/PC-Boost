using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using CleanRamBoosterApp.Services;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;

namespace CleanRamBoosterApp.WpfUI
{
    public enum AppDialogTone
    {
        Info,
        Question,
        Warning,
        Error,
        Success
    }

    /// <summary>
    /// Unified custom-themed modal confirmation and notification window matching
    /// PC Boost's card aesthetic across dark/light themes and English/Arabic RTL layouts.
    /// Supports both WPF Window and WinForms IWin32Window owners seamlessly.
    /// </summary>
    public sealed class AppDialogWindow : Window
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        private readonly IntPtr nativeOwnerHwnd;

        public AppDialogWindow(
            string message,
            string? title = null,
            AppDialogTone tone = AppDialogTone.Info,
            bool isConfirmation = false,
            bool isDestructive = false,
            string? primaryButtonText = null,
            string? secondaryButtonText = null,
            Window? wpfOwner = null,
            IntPtr nativeOwner = default)
        {
            bool isAr = LocalizationService.IsArabic;
            bool isDark = WpfTheme.IsDarkMode;
            nativeOwnerHwnd = nativeOwner;

            string appTitle = LocalizationService.T("AppTitle", "PC Boost");
            string cleanTitle = string.IsNullOrWhiteSpace(title) ? appTitle : title.Trim();
            bool hasDistinctHeadline = !string.Equals(cleanTitle, appTitle, StringComparison.OrdinalIgnoreCase) &&
                                       !string.Equals(cleanTitle, "PC Boost", StringComparison.OrdinalIgnoreCase);

            Title = cleanTitle;
            Width = 466;
            MinWidth = 420;
            MaxWidth = 540;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            FontFamily = LocalizationService.ActiveWpfFontFamily;
            FlowDirection = isAr ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

            if (wpfOwner != null && wpfOwner.IsVisible)
            {
                Owner = wpfOwner;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else if (nativeOwnerHwnd != IntPtr.Zero && IsWindowVisible(nativeOwnerHwnd))
            {
                new WindowInteropHelper(this).Owner = nativeOwnerHwnd;
                WindowStartupLocation = WindowStartupLocation.Manual;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            WpfTheme.ApplyWindowResources(Resources);

            var cardBorder = new Border
            {
                Margin = new Thickness(14),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Background = WpfTheme.CardBrush,
                BorderBrush = isDark
                    ? new SolidColorBrush(Color.FromRgb(56, 68, 84))
                    : new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    Opacity = isDark ? 0.58 : 0.22,
                    BlurRadius = 20,
                    ShadowDepth = 4
                }
            };

            var rootStack = new StackPanel();

            // 1. Top Draggable Brand Header Bar
            var headerGrid = new Grid
            {
                Margin = new Thickness(16, 12, 14, 10),
                Background = Brushes.Transparent
            };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    try { DragMove(); } catch { }
                }
            };

            var miniLogo = WpfTheme.CreateMiniBrandLogo(20);
            if (miniLogo != null)
            {
                miniLogo.Margin = new Thickness(0, 0, 9, 0);
                Grid.SetColumn(miniLogo, 0);
                headerGrid.Children.Add(miniLogo);
            }

            var lblBrand = new TextBlock
            {
                Text = appTitle,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = WpfTheme.TextSecondaryBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lblBrand, 1);
            headerGrid.Children.Add(lblBrand);

            var btnClose = new Button
            {
                Width = 26,
                Height = 26,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = LocalizationService.T("BtnClose", "Close"),
                Template = WpfTheme.CreateFlatButtonTemplate(enableHoverBg: true),
                Content = WpfIcons.CreateIcon("CloseX", WpfTheme.TextSecondaryBrush, 14, 1.85)
            };
            btnClose.Click += (s, e) => CloseWithResult(false);
            Grid.SetColumn(btnClose, 2);
            headerGrid.Children.Add(btnClose);
            rootStack.Children.Add(headerGrid);

            // 2. Body Area (Circular Vector Badge + Headline/Message)
            var bodyGrid = new Grid { Margin = new Thickness(18, 2, 18, 18) };
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Color amberColor = isDark ? Color.FromRgb(251, 146, 60) : Color.FromRgb(217, 119, 6);
            (string iconKey, Color accentColor) = ResolveVisualTone(tone, isDestructive, amberColor);
            var accentBrush = new SolidColorBrush(accentColor);

            var badge = new Border
            {
                Width = 42,
                Height = 42,
                CornerRadius = new CornerRadius(21),
                Background = new SolidColorBrush(Color.FromArgb(28, accentColor.R, accentColor.G, accentColor.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(145, accentColor.R, accentColor.G, accentColor.B)),
                BorderThickness = new Thickness(1.5),
                VerticalAlignment = hasDistinctHeadline ? VerticalAlignment.Top : VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 1, 0, 0),
                Child = WpfIcons.CreateIcon(iconKey, accentBrush, 22, 1.9)
            };
            Grid.SetColumn(badge, 0);
            bodyGrid.Children.Add(badge);

            var msgScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 250,
                VerticalAlignment = VerticalAlignment.Center
            };
            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            if (hasDistinctHeadline)
            {
                textStack.Children.Add(new TextBlock
                {
                    Text = cleanTitle,
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = WpfTheme.TextPrimaryBrush,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 5)
                });
            }

            textStack.Children.Add(new TextBlock
            {
                Text = message ?? string.Empty,
                FontSize = hasDistinctHeadline ? 13 : 13.5,
                FontWeight = FontWeights.Normal,
                Foreground = hasDistinctHeadline ? WpfTheme.TextSecondaryBrush : WpfTheme.TextPrimaryBrush,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20
            });

            msgScroll.Content = textStack;
            Grid.SetColumn(msgScroll, 1);
            bodyGrid.Children.Add(msgScroll);
            rootStack.Children.Add(bodyGrid);

            // 3. Subtle Tinted Footer Strip with Action Buttons
            var footerBorder = new Border
            {
                CornerRadius = new CornerRadius(0, 0, 9, 9),
                BorderThickness = new Thickness(0, 1, 0, 0),
                BorderBrush = WpfTheme.BorderBrush,
                Background = isDark
                    ? new SolidColorBrush(Color.FromRgb(13, 17, 23))
                    : new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                Padding = new Thickness(18, 12, 18, 12)
            };

            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            if (isConfirmation)
            {
                string noLabel = !string.IsNullOrWhiteSpace(secondaryButtonText)
                    ? secondaryButtonText
                    : (isAr ? "لا" : "No");
                var btnSecondary = CreateSecondaryButton(noLabel, isDark);
                btnSecondary.IsCancel = true;
                btnSecondary.Margin = new Thickness(0, 0, 10, 0);
                btnSecondary.Click += (s, e) => CloseWithResult(false);
                btnRow.Children.Add(btnSecondary);

                string yesLabel = !string.IsNullOrWhiteSpace(primaryButtonText)
                    ? primaryButtonText
                    : (isAr ? "نعم" : "Yes");
                var btnPrimary = CreatePrimaryButton(yesLabel, isDestructive);
                btnPrimary.IsDefault = true;
                btnPrimary.Click += (s, e) => CloseWithResult(true);
                btnRow.Children.Add(btnPrimary);
            }
            else
            {
                string okLabel = !string.IsNullOrWhiteSpace(primaryButtonText)
                    ? primaryButtonText
                    : (isAr ? "موافق" : "OK");
                var btnOk = CreatePrimaryButton(okLabel, isDestructive);
                btnOk.IsDefault = true;
                btnOk.IsCancel = true;
                btnOk.Click += (s, e) => CloseWithResult(true);
                btnRow.Children.Add(btnOk);
            }

            footerBorder.Child = btnRow;
            rootStack.Children.Add(footerBorder);

            cardBorder.Child = rootStack;
            Content = cardBorder;

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    CloseWithResult(false);
                    e.Handled = true;
                }
            };

            Loaded += (s, e) => CenterOverNativeOwnerIfNeeded();
        }

        private void CloseWithResult(bool accepted)
        {
            try { DialogResult = accepted; } catch { }
            Close();
        }

        private void CenterOverNativeOwnerIfNeeded()
        {
            if (Owner != null || nativeOwnerHwnd == IntPtr.Zero || !IsWindowVisible(nativeOwnerHwnd))
                return;

            try
            {
                if (!GetWindowRect(nativeOwnerHwnd, out var rc)) return;
                var source = PresentationSource.FromVisual(this);
                var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

                var topLeft = transform.Transform(new Point(rc.Left, rc.Top));
                var bottomRight = transform.Transform(new Point(rc.Right, rc.Bottom));

                double ownerW = Math.Max(0, bottomRight.X - topLeft.X);
                double ownerH = Math.Max(0, bottomRight.Y - topLeft.Y);
                double myW = ActualWidth > 0 ? ActualWidth : Width;
                double myH = ActualHeight > 0 ? ActualHeight : 200;

                double targetX = topLeft.X + (ownerW - myW) / 2.0;
                double targetY = topLeft.Y + (ownerH - myH) / 2.0;

                var wa = SystemParameters.WorkArea;
                Left = Math.Max(wa.Left, Math.Min(targetX, wa.Right - myW));
                Top = Math.Max(wa.Top, Math.Min(targetY, wa.Bottom - myH));
            }
            catch { }
        }

        private static (string IconKey, Color AccentColor) ResolveVisualTone(AppDialogTone tone, bool isDestructive, Color amberColor)
        {
            if (isDestructive)
            {
                return (tone == AppDialogTone.Warning ? "Alert" : "Trash", WpfTheme.ColorDanger);
            }

            return tone switch
            {
                AppDialogTone.Error => ("Alert", WpfTheme.ColorDanger),
                AppDialogTone.Warning => ("Alert", amberColor),
                AppDialogTone.Success => ("CheckCircle", WpfTheme.ColorSuccess),
                AppDialogTone.Question => ("HelpCircle", WpfTheme.ColorPrimary),
                _ => ("InfoCircle", WpfTheme.ColorPrimary)
            };
        }

        private static Button CreatePrimaryButton(string text, bool isDestructive)
        {
            var template = isDestructive
                ? WpfTheme.CreateRoundedCtaTemplate(
                    Color.FromRgb(239, 68, 68),
                    Color.FromRgb(220, 38, 38),
                    Color.FromRgb(248, 113, 113),
                    Color.FromRgb(239, 68, 68),
                    cornerRadius: 6)
                : WpfTheme.CreateRoundedCtaTemplate(
                    Color.FromRgb(59, 130, 246),
                    Color.FromRgb(37, 99, 235),
                    Color.FromRgb(96, 165, 250),
                    Color.FromRgb(59, 130, 246),
                    cornerRadius: 6);

            return new Button
            {
                Content = text,
                Height = 35,
                MinWidth = 96,
                Padding = new Thickness(18, 0, 18, 0),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Template = template
            };
        }

        private static Button CreateSecondaryButton(string text, bool isDark)
        {
            var bg = new SolidColorBrush(isDark ? Color.FromRgb(33, 38, 45) : Color.FromRgb(255, 255, 255));
            var border = new SolidColorBrush(isDark ? Color.FromRgb(48, 54, 61) : Color.FromRgb(203, 213, 225));
            var fg = new SolidColorBrush(isDark ? Color.FromRgb(201, 209, 217) : Color.FromRgb(30, 41, 59));

            return new Button
            {
                Content = text,
                Height = 35,
                MinWidth = 90,
                Padding = new Thickness(16, 0, 16, 0),
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                Foreground = fg,
                Background = bg,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Cursor = Cursors.Hand,
                Template = WpfTheme.CreateFlatButtonTemplate(enableHoverBg: true, cornerRadius: 6)
            };
        }
    }

    public static class AppDialog
    {
        public static void Show(
            DependencyObject? owner,
            string message,
            string? title = null,
            AppDialogTone tone = AppDialogTone.Info)
        {
            ShowCore(message, title, tone, isConfirmation: false, isDestructive: false,
                primaryText: null, secondaryText: null, wpfOwner: ResolveWpfOwner(owner), nativeOwner: IntPtr.Zero);
        }

        public static void Show(
            System.Windows.Forms.IWin32Window? owner,
            string message,
            string? title = null,
            AppDialogTone tone = AppDialogTone.Info)
        {
            ShowCore(message, title, tone, isConfirmation: false, isDestructive: false,
                primaryText: null, secondaryText: null, wpfOwner: null, nativeOwner: owner?.Handle ?? IntPtr.Zero);
        }

        public static void Show(
            string message,
            string? title = null,
            AppDialogTone tone = AppDialogTone.Info)
        {
            ShowCore(message, title, tone, isConfirmation: false, isDestructive: false,
                primaryText: null, secondaryText: null, wpfOwner: ResolveFallbackMainWindow(), nativeOwner: IntPtr.Zero);
        }

        public static bool Confirm(
            DependencyObject? owner,
            string message,
            string? title = null,
            AppDialogTone tone = AppDialogTone.Question,
            bool isDestructive = false,
            string? yesText = null,
            string? noText = null)
        {
            return ShowCore(message, title, tone, isConfirmation: true, isDestructive: isDestructive,
                primaryText: yesText, secondaryText: noText, wpfOwner: ResolveWpfOwner(owner), nativeOwner: IntPtr.Zero);
        }

        public static bool Confirm(
            System.Windows.Forms.IWin32Window? owner,
            string message,
            string? title = null,
            AppDialogTone tone = AppDialogTone.Question,
            bool isDestructive = false,
            string? yesText = null,
            string? noText = null)
        {
            return ShowCore(message, title, tone, isConfirmation: true, isDestructive: isDestructive,
                primaryText: yesText, secondaryText: noText, wpfOwner: null, nativeOwner: owner?.Handle ?? IntPtr.Zero);
        }

        public static bool Confirm(
            string message,
            string? title = null,
            AppDialogTone tone = AppDialogTone.Question,
            bool isDestructive = false)
        {
            return ShowCore(message, title, tone, isConfirmation: true, isDestructive: isDestructive,
                primaryText: null, secondaryText: null, wpfOwner: ResolveFallbackMainWindow(), nativeOwner: IntPtr.Zero);
        }

        private static Window? ResolveWpfOwner(DependencyObject? owner)
        {
            if (owner is Window w && w.IsVisible) return w;
            if (owner != null)
            {
                var found = Window.GetWindow(owner);
                if (found != null && found.IsVisible) return found;
            }
            return ResolveFallbackMainWindow();
        }

        private static Window? ResolveFallbackMainWindow()
        {
            var mw = Application.Current?.MainWindow;
            return (mw != null && mw.IsVisible) ? mw : null;
        }

        private static bool ShowCore(
            string message,
            string? title,
            AppDialogTone tone,
            bool isConfirmation,
            bool isDestructive,
            string? primaryText,
            string? secondaryText,
            Window? wpfOwner,
            IntPtr nativeOwner)
        {
            try
            {
                var dlg = new AppDialogWindow(
                    message, title, tone, isConfirmation, isDestructive,
                    primaryText, secondaryText, wpfOwner, nativeOwner);
                bool? res = dlg.ShowDialog();
                return res == true;
            }
            catch
            {
                var icon = tone switch
                {
                    AppDialogTone.Error => System.Windows.Forms.MessageBoxIcon.Error,
                    AppDialogTone.Warning => System.Windows.Forms.MessageBoxIcon.Warning,
                    AppDialogTone.Question => System.Windows.Forms.MessageBoxIcon.Question,
                    _ => System.Windows.Forms.MessageBoxIcon.Information
                };
                var buttons = isConfirmation
                    ? System.Windows.Forms.MessageBoxButtons.YesNo
                    : System.Windows.Forms.MessageBoxButtons.OK;
                var fallbackRes = System.Windows.Forms.MessageBox.Show(message, title ?? "PC Boost", buttons, icon);
                return fallbackRes == System.Windows.Forms.DialogResult.Yes || fallbackRes == System.Windows.Forms.DialogResult.OK;
            }
        }
    }
}
