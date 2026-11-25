using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CleanRamBoosterApp.Services;

namespace CleanRamBoosterApp.WpfUI.Toolbox
{
    public class ToolboxAutoShutdownWindow : ToolboxWindowBase
    {
        [DllImport("powrprof.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

        private readonly TextBlock lblTitle;
        private readonly TextBlock lblSub;
        private readonly Border cardMain;
        private readonly ComboBox cbAction;
        private readonly RadioButton rbDuration;
        private readonly RadioButton rbSpecific;
        private readonly RadioButton rbDaily;

        private readonly StackPanel pnlDuration;
        private readonly ComboBox cbHours;
        private readonly ComboBox cbMinutes;

        private readonly StackPanel pnlSpecific;
        private readonly DatePicker dpDate;
        private readonly ComboBox cbSpecHour;
        private readonly ComboBox cbSpecMin;

        private readonly StackPanel pnlDaily;
        private readonly ComboBox cbDailyHour;
        private readonly ComboBox cbDailyMin;

        private readonly Button btnStart;
        private readonly Button btnCancel;
        private readonly Button btnClose;
        private readonly Border countdownCard;
        private readonly TextBlock lblCountdown;
        private readonly TextBlock lblStatus;

        private DispatcherTimer? shutdownTimer;
        private DateTime targetTime;

        public ToolboxAutoShutdownWindow()
            : base("Auto Shutdown & Power Scheduler - PC Boost", "إيقاف التشغيل المجدول وإدارة الطاقة - PC Boost", 600, 505, resizable: false)
        {
            bool isAr = LocalizationService.IsArabic;

            var root = new Grid { Margin = new Thickness(20, 18, 20, 16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Card
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer

            var header = BuildHeader("Clock", WpfTheme.ColorPurple,
                isAr ? "إدارة وتحديد مواعيد الطاقة والتشغيل" : "Scheduled Power Management",
                isAr ? "جدولة إيقاف التشغيل أو إعادة التشغيل أو السكون بمؤقت تنازلي أو وقت محدد." : "Schedule automatic shutdown, restart, sleep, hibernate, or workstation lock.",
                out lblTitle, out lblSub);
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            cardMain = CreateCard(new Thickness(18, 16, 18, 16));
            var stack = new StackPanel();

            // 1. Power Action Row
            var actGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            actGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(125) });
            actGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lblAction = new TextBlock
            {
                Text = isAr ? "إجراء الطاقة:" : "Power Action:",
                FontSize = 13,
                Foreground = WpfTheme.TextPrimaryBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lblAction, 0);
            actGrid.Children.Add(lblAction);

            cbAction = new ComboBox { Height = 32, FontSize = 12.5, VerticalContentAlignment = VerticalAlignment.Center };
            string[] actions = isAr
                ? new[] { "إيقاف تشغيل الكمبيوتر", "إعادة تشغيل الكمبيوتر", "وضع السكون (Sleep)", "وضع السبات (Hibernate)", "قفل الشاشة" }
                : new[] { "Shut Down PC", "Restart PC", "Sleep / Suspend", "Hibernate PC", "Lock Workstation" };
            foreach (var a in actions) cbAction.Items.Add(a);
            cbAction.SelectedIndex = 0;
            Grid.SetColumn(cbAction, 1);
            actGrid.Children.Add(cbAction);
            stack.Children.Add(actGrid);

            // 2. Trigger Mode Radios
            var modeGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(125) });
            modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lblMode = new TextBlock
            {
                Text = isAr ? "نوع المجدول:" : "Trigger Mode:",
                FontSize = 13,
                Foreground = WpfTheme.TextPrimaryBrush,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 0, 0)
            };
            Grid.SetColumn(lblMode, 0);
            modeGrid.Children.Add(lblMode);

            var radioWrap = new WrapPanel { Orientation = Orientation.Horizontal };
            rbDuration = CreateRadio(isAr ? "مؤقت تنازلي" : "Countdown Timer", true);
            rbSpecific = CreateRadio(isAr ? "وقت محدد" : "Specific Date & Time", false);
            rbDaily = CreateRadio(isAr ? "جدول يومي منتظم" : "Daily Schedule", false);
            rbDuration.Checked += (_, _) => UpdateModePanels();
            rbSpecific.Checked += (_, _) => UpdateModePanels();
            rbDaily.Checked += (_, _) => UpdateModePanels();

            radioWrap.Children.Add(rbDuration);
            radioWrap.Children.Add(rbSpecific);
            radioWrap.Children.Add(rbDaily);
            Grid.SetColumn(radioWrap, 1);
            modeGrid.Children.Add(radioWrap);
            stack.Children.Add(modeGrid);

            // 3. Mode-specific parameter inputs
            // 3a. Countdown Duration
            pnlDuration = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) };
            pnlDuration.Children.Add(CreateFieldLabel(isAr ? "التنفيذ بعد:" : "Execute after:", 125));
            cbHours = CreateNumberCombo(0, 72, 1, 70);
            pnlDuration.Children.Add(cbHours);
            pnlDuration.Children.Add(CreateUnitLabel(isAr ? "ساعة" : "hours"));
            cbMinutes = CreateNumberCombo(0, 59, 0, 70);
            pnlDuration.Children.Add(cbMinutes);
            pnlDuration.Children.Add(CreateUnitLabel(isAr ? "دقيقة" : "minutes"));
            stack.Children.Add(pnlDuration);

            // 3b. Specific Date & Time
            pnlSpecific = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 16), Visibility = Visibility.Collapsed };
            pnlSpecific.Children.Add(CreateFieldLabel(isAr ? "التنفيذ في:" : "Execute on:", 125));
            dpDate = new DatePicker
            {
                SelectedDate = DateTime.Today,
                Width = 148,
                Height = 32,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            var nextHr = DateTime.Now.AddHours(1);
            cbSpecHour = CreateNumberCombo(0, 23, nextHr.Hour, 64, twoDigits: true);
            cbSpecMin = CreateNumberCombo(0, 59, nextHr.Minute, 64, twoDigits: true);
            pnlSpecific.Children.Add(dpDate);
            pnlSpecific.Children.Add(cbSpecHour);
            pnlSpecific.Children.Add(new TextBlock { Text = ":", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = WpfTheme.TextPrimaryBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 5, 0) });
            pnlSpecific.Children.Add(cbSpecMin);
            stack.Children.Add(pnlSpecific);

            // 3c. Daily Schedule
            pnlDaily = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 16), Visibility = Visibility.Collapsed };
            pnlDaily.Children.Add(CreateFieldLabel(isAr ? "كل يوم عند:" : "Every day at:", 125));
            cbDailyHour = CreateNumberCombo(0, 23, 23, 68, twoDigits: true);
            cbDailyMin = CreateNumberCombo(0, 59, 0, 68, twoDigits: true);
            pnlDaily.Children.Add(cbDailyHour);
            pnlDaily.Children.Add(new TextBlock { Text = ":", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = WpfTheme.TextPrimaryBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) });
            pnlDaily.Children.Add(cbDailyMin);
            stack.Children.Add(pnlDaily);

            // 4. Start / Cancel Buttons
            var btnBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 16)
            };
            btnStart = CreatePrimaryButton("Lightning", isAr ? "تفعيل المجدول" : "Start Scheduler", 36);
            btnStart.MinWidth = 158;
            btnStart.Margin = new Thickness(0, 0, 12, 0);
            btnStart.Click += (_, _) => StartSchedule();

            btnCancel = CreateSecondaryButton("Stop", isAr ? "إلغاء المؤقت" : "Cancel Timer", 36);
            btnCancel.MinWidth = 144;
            btnCancel.Click += (_, _) => CancelSchedule();

            btnBar.Children.Add(btnStart);
            btnBar.Children.Add(btnCancel);
            stack.Children.Add(btnBar);

            // 5. Countdown Readout Panel
            countdownCard = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Background = WpfTheme.CardSubBrush,
                BorderBrush = WpfTheme.BorderBrush,
                Padding = new Thickness(14, 14, 14, 14)
            };
            var cdStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            lblCountdown = new TextBlock
            {
                Text = "--:--:--",
                FontSize = 28,
                FontWeight = FontWeights.SemiBold,
                Foreground = WpfTheme.PrimaryBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                FlowDirection = FlowDirection.LeftToRight
            };
            lblStatus = new TextBlock
            {
                Text = isAr ? "الحالة: لا يوجد جدولة طاقة نشطة حالياً." : "Status: No scheduled power event active.",
                FontSize = 12.5,
                Foreground = WpfTheme.TextSecondaryBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            };
            cdStack.Children.Add(lblCountdown);
            cdStack.Children.Add(lblStatus);
            countdownCard.Child = cdStack;
            stack.Children.Add(countdownCard);

            cardMain.Child = stack;
            Grid.SetRow(cardMain, 1);
            root.Children.Add(cardMain);

            // Footer
            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            btnClose = CreateSecondaryButton(null, isAr ? "إغلاق" : "Close", 34);
            btnClose.MinWidth = 96;
            btnClose.Click += (_, _) => Close();
            footer.Children.Add(btnClose);
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;

            Closed += (_, _) =>
            {
                try { shutdownTimer?.Stop(); shutdownTimer = null; } catch { }
            };
        }

        private static RadioButton CreateRadio(string text, bool isChecked)
        {
            return new RadioButton
            {
                Content = text,
                IsChecked = isChecked,
                FontSize = 12.5,
                Foreground = WpfTheme.TextPrimaryBrush,
                Margin = new Thickness(0, 0, 16, 6),
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        private static TextBlock CreateFieldLabel(string text, double width)
        {
            return new TextBlock
            {
                Text = text,
                Width = width,
                FontSize = 13,
                Foreground = WpfTheme.TextPrimaryBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static TextBlock CreateUnitLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 12.5,
                Foreground = WpfTheme.TextSecondaryBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 16, 0)
            };
        }

        private static ComboBox CreateNumberCombo(int min, int max, int defaultVal, double width, bool twoDigits = false)
        {
            var cb = new ComboBox
            {
                Width = width,
                Height = 32,
                FontSize = 12.5,
                VerticalContentAlignment = VerticalAlignment.Center,
                FlowDirection = FlowDirection.LeftToRight
            };
            for (int i = min; i <= max; i++)
                cb.Items.Add(twoDigits ? i.ToString("00") : i.ToString());
            cb.SelectedIndex = Math.Clamp(defaultVal - min, 0, max - min);
            return cb;
        }

        private void UpdateModePanels()
        {
            pnlDuration.Visibility = rbDuration.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            pnlSpecific.Visibility = rbSpecific.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            pnlDaily.Visibility = rbDaily.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void StartSchedule()
        {
            bool isAr = LocalizationService.IsArabic;
            if (rbDuration.IsChecked == true)
            {
                int hrs = cbHours.SelectedIndex;
                int mins = cbMinutes.SelectedIndex;
                int totalMins = hrs * 60 + mins;
                if (totalMins <= 0)
                {
                    AppDialog.Show(this,
                        isAr ? "يرجى تحديد مدة زمنية أكبر من 0 دقيقة." : "Please specify a duration greater than 0 minutes.",
                        isAr ? "مدة غير صالحة" : "Invalid Duration",
                        AppDialogTone.Warning);
                    return;
                }
                targetTime = DateTime.Now.AddMinutes(totalMins);
            }
            else if (rbSpecific.IsChecked == true)
            {
                DateTime date = dpDate.SelectedDate ?? DateTime.Today;
                int hr = Math.Max(0, cbSpecHour.SelectedIndex);
                int min = Math.Max(0, cbSpecMin.SelectedIndex);
                targetTime = date.Date.AddHours(hr).AddMinutes(min);

                if (targetTime <= DateTime.Now)
                {
                    AppDialog.Show(this,
                        isAr ? "يرجى اختيار تاريخ ووقت مستقبلي." : "Please select a future date and time.",
                        isAr ? "وقت غير صالح" : "Invalid Date/Time",
                        AppDialogTone.Warning);
                    return;
                }
            }
            else
            {
                int hr = Math.Max(0, cbDailyHour.SelectedIndex);
                int min = Math.Max(0, cbDailyMin.SelectedIndex);
                targetTime = DateTime.Today.AddHours(hr).AddMinutes(min);
                if (targetTime <= DateTime.Now)
                    targetTime = targetTime.AddDays(1);
            }

            shutdownTimer?.Stop();
            shutdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            shutdownTimer.Tick += (_, _) =>
            {
                var remaining = targetTime - DateTime.Now;
                if (remaining.TotalSeconds <= 0)
                {
                    shutdownTimer.Stop();
                    lblCountdown.Text = "00:00:00";
                    lblStatus.Text = isAr ? "جاري تنفيذ إجراء الطاقة المجدول الآن..." : "Executing scheduled power action now...";
                    ExecuteAction();
                }
                else
                {
                    UpdateCountdownText(remaining, isAr);
                }
            };
            shutdownTimer.Start();
            UpdateCountdownText(targetTime - DateTime.Now, isAr);
        }

        private void UpdateCountdownText(TimeSpan remaining, bool isAr)
        {
            if (remaining.TotalDays >= 1)
                lblCountdown.Text = $"{(int)remaining.TotalDays}d {remaining:hh\\:mm\\:ss}";
            else
                lblCountdown.Text = $"{remaining:hh\\:mm\\:ss}";

            lblStatus.Text = isAr
                ? $"تمت جدولة {cbAction.SelectedItem} في \u200E{targetTime:yyyy-MM-dd HH:mm:ss}\u200E"
                : $"{cbAction.SelectedItem} scheduled for {targetTime:yyyy-MM-dd HH:mm:ss}";
            lblStatus.Foreground = new SolidColorBrush(WpfTheme.ColorWarning);
        }

        private void CancelSchedule()
        {
            shutdownTimer?.Stop();
            shutdownTimer = null;
            lblCountdown.Text = "--:--:--";
            lblStatus.Text = LocalizationService.IsArabic
                ? "الحالة: تم إلغاء جدولة الطاقة."
                : "Status: Scheduled power event cancelled.";
            lblStatus.Foreground = WpfTheme.SuccessBrush;
        }

        private void ExecuteAction()
        {
            AdminPrivilegeService.EnsureProcessPrivileges();
            bool isAr = LocalizationService.IsArabic;
            try
            {
                switch (cbAction.SelectedIndex)
                {
                    case 0:
                        if (!AdminPrivilegeService.RunProcessAsAdmin("shutdown.exe", "/s /t 0", 8000, out _, out string err0, out _, out _))
                            lblStatus.Text = isAr ? $"تعذر إيقاف التشغيل: {err0}" : $"Shutdown failed: {err0}";
                        break;
                    case 1:
                        if (!AdminPrivilegeService.RunProcessAsAdmin("shutdown.exe", "/r /t 0", 8000, out _, out string err1, out _, out _))
                            lblStatus.Text = isAr ? $"تعذرت إعادة التشغيل: {err1}" : $"Restart failed: {err1}";
                        break;
                    case 2:
                        SetSuspendState(hibernate: false, forceCritical: true, disableWakeEvent: true);
                        break;
                    case 3:
                        SetSuspendState(hibernate: true, forceCritical: true, disableWakeEvent: true);
                        break;
                    case 4:
                        Process.Start(new ProcessStartInfo("rundll32.exe", "user32.dll,LockWorkStation")
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                        break;
                }
            }
            catch (Exception ex)
            {
                DiagnosticLogger.LogError(nameof(ToolboxAutoShutdownWindow), "Power action execution error", ex);
                lblStatus.Text = isAr ? $"خطأ في تنفيذ إجراء الطاقة: {ex.Message}" : $"Power action error: {ex.Message}";
                AppDialog.Show(this, lblStatus.Text, isAr ? "خطأ" : "Error", AppDialogTone.Error);
            }
        }

        public override void RefreshTheme()
        {
            base.RefreshTheme();
            lblTitle.Foreground = WpfTheme.TextPrimaryBrush;
            lblSub.Foreground = WpfTheme.TextSecondaryBrush;
            cardMain.Background = WpfTheme.CardBrush;
            cardMain.BorderBrush = WpfTheme.BorderBrush;
            countdownCard.Background = WpfTheme.CardSubBrush;
            countdownCard.BorderBrush = WpfTheme.BorderBrush;
            rbDuration.Foreground = WpfTheme.TextPrimaryBrush;
            rbSpecific.Foreground = WpfTheme.TextPrimaryBrush;
            rbDaily.Foreground = WpfTheme.TextPrimaryBrush;
            ApplySecondaryButtonStyle(btnCancel);
            btnCancel.Content = WpfIcons.CreateIconTextContent("Stop", LocalizationService.IsArabic ? "إلغاء المؤقت" : "Cancel Timer", btnCancel.Foreground, 14, 6);
            ApplySecondaryButtonStyle(btnClose);
        }
    }
}
