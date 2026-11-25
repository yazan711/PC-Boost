using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using CleanRamBoosterApp.Services;
using CleanRamBoosterApp.UI;

namespace CleanRamBoosterApp.Forms
{
    public class ToolboxAutoShutdownForm : Form
    {
        private readonly ComboBox cbAction;
        private readonly RadioButton rbDuration;
        private readonly RadioButton rbSpecificDateTime;
        private readonly RadioButton rbDaily;

        private readonly NumericUpDown numHours;
        private readonly NumericUpDown numMinutes;

        private readonly DateTimePicker dtpDate;
        private readonly DateTimePicker dtpTime;

        private readonly DateTimePicker dtpDailyTime;

        private readonly Panel pnlDuration;
        private readonly Panel pnlSpecific;
        private readonly Panel pnlDaily;

        private readonly Label lblStatus;
        private readonly Label lblCountdown;
        private System.Windows.Forms.Timer? shutdownTimer;
        private DateTime targetTime;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
                return cp;
            }
        }

        public ToolboxAutoShutdownForm()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;

            bool isAr = LocalizationService.IsArabic;
            Text = isAr ? "إيقاف التشغيل المجدول وإدارة الطاقة - PC Boost" : "Auto Shutdown & Power Scheduler";
            ClientSize = new Size(576, 464);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            FormClosing += (s, e) =>
            {
                try { shutdownTimer?.Stop(); shutdownTimer?.Dispose(); shutdownTimer = null; } catch { }
            };
            Font = UITheme.FontBody;
            BackColor = UITheme.BgContent;
            RightToLeft = isAr ? RightToLeft.Yes : RightToLeft.No;
            if (UITheme.AppIcon != null) Icon = UITheme.AppIcon;
            UITheme.ApplyDwmDarkMode(Handle, UITheme.IsDarkMode);

            var lblHeader = new Label
            {
                Text = isAr ? "إدارة وتحديد مواعيد الطاقة والتشغيل" : "Scheduled Power Management",
                Font = UITheme.FontHeader,
                ForeColor = UITheme.TextPrimary
            };

            var card = UITheme.CreateCardPanel(18, 56, 540, 342);

            // Action Row
            var lblAction = new Label { Text = isAr ? "إجراء الطاقة:" : "Power Action:", AutoSize = true, Font = UITheme.FontBody, ForeColor = UITheme.TextPrimary };
            cbAction = new ComboBox();
            UITheme.StyleComboBox(cbAction);
            cbAction.Items.AddRange(isAr
                ? new object[] { "إيقاف تشغيل الكمبيوتر", "إعادة تشغيل الكمبيوتر", "وضع السكون (Sleep)", "وضع السبات (Hibernate)", "قفل الشاشة" }
                : new object[] { "Shut Down PC", "Restart PC", "Sleep / Suspend", "Hibernate PC", "Lock Workstation" });
            cbAction.SelectedIndex = 0;

            if (isAr)
            {
                int lblActW = TextRenderer.MeasureText(lblAction.Text, lblAction.Font).Width;
                lblAction.Location = new Point(540 - 16 - lblActW, 16);
                cbAction.Location = new Point(16, 13);
                cbAction.Width = lblAction.Left - 10 - 16;
            }
            else
            {
                lblAction.Location = new Point(16, 16);
                cbAction.Location = new Point(134, 13);
                cbAction.Width = 390;
            }

            // Trigger Mode Radios (explicit measured widths so owner-drawn circle + ClearType label never clip)
            var lblMode = new Label { Text = isAr ? "نوع المجدول:" : "Trigger Mode:", AutoSize = true, Font = UITheme.FontBody, ForeColor = UITheme.TextPrimary, UseMnemonic = false };

            string txtDur = isAr ? "مؤقت تنازلي" : "Countdown Timer";
            string txtSpec = isAr ? "وقت محدد" : "Specific Date & Time";
            string txtDaily = isAr ? "جدول يومي منتظم" : "Daily Schedule";
            int wDur = TextRenderer.MeasureText(txtDur, UITheme.FontBody).Width + 46;
            int wSpec = TextRenderer.MeasureText(txtSpec, UITheme.FontBody).Width + 46;
            int wDaily = TextRenderer.MeasureText(txtDaily, UITheme.FontBody).Width + 46;

            rbDuration = new RadioButton { Text = txtDur, AutoSize = false, Size = new Size(wDur, 26), Checked = true, Font = UITheme.FontBody, ForeColor = UITheme.TextPrimary, UseMnemonic = false };
            rbSpecificDateTime = new RadioButton { Text = txtSpec, AutoSize = false, Size = new Size(wSpec, 26), Font = UITheme.FontBody, ForeColor = UITheme.TextPrimary, UseMnemonic = false };
            rbDaily = new RadioButton { Text = txtDaily, AutoSize = false, Size = new Size(wDaily, 26), Font = UITheme.FontBody, ForeColor = UITheme.TextPrimary, UseMnemonic = false };
            UITheme.StyleRadioButton(rbDuration);
            UITheme.StyleRadioButton(rbSpecificDateTime);
            UITheme.StyleRadioButton(rbDaily);

            if (isAr)
            {
                int lblModeW = TextRenderer.MeasureText(lblMode.Text, lblMode.Font).Width;
                lblMode.Location = new Point(540 - 16 - lblModeW, 56);
                rbDuration.Location = new Point(lblMode.Left - 12 - wDur, 52);
                rbSpecificDateTime.Location = new Point(rbDuration.Left - 14 - wSpec, 52);
                rbDaily.Location = new Point(lblMode.Left - 12 - wDaily, 82);
            }
            else
            {
                lblMode.Location = new Point(16, 56);
                rbDuration.Location = new Point(134, 52);
                rbSpecificDateTime.Location = new Point(rbDuration.Right + 16, 52);
                rbDaily.Location = new Point(134, 82);
            }

            rbDuration.CheckedChanged += (s, e) => UpdateModePanels();
            rbSpecificDateTime.CheckedChanged += (s, e) => UpdateModePanels();
            rbDaily.CheckedChanged += (s, e) => UpdateModePanels();

            // Panel 1: Duration
            pnlDuration = new Panel { Location = new Point(16, 118), Size = new Size(508, 44) };
            var lblDur = new Label { Text = isAr ? "التنفيذ بعد:" : "Shutdown after:", AutoSize = true, Font = UITheme.FontBody, ForeColor = UITheme.TextPrimary };
            numHours = new NumericUpDown { Size = new Size(64, 26), Minimum = 0, Maximum = 72, Value = 1, Font = UITheme.FontBody };
            UITheme.StyleNumericUpDown(numHours);
            var lblH = new Label { Text = isAr ? "ساعة" : "hours", AutoSize = true, Font = UITheme.FontBody, ForeColor = UITheme.TextPrimary };
            numMinutes = new NumericUpDown { Size = new Size(64, 26), Minimum = 0, Maximum = 59, Value = 0, Font = UITheme.FontBody };
            UITheme.StyleNumericUpDown(numMinutes);
            var lblM = new Label { Text = isAr ? "دقيقة" : "minutes", AutoSize = true, Font = UITheme.FontBody, ForeColor = UITheme.TextPrimary };

            if (isAr)
            {
                int durW = TextRenderer.MeasureText(lblDur.Text, lblDur.Font).Width + 4;
                int hW = TextRenderer.MeasureText(lblH.Text, lblH.Font).Width + 2;
                int mW = TextRenderer.MeasureText(lblM.Text, lblM.Font).Width + 2;

                lblDur.Location = new Point(504 - durW, 9);
                numHours.Location = new Point(lblDur.Left - 10 - numHours.Width, 6);
                lblH.Location = new Point(numHours.Left - 8 - hW, 9);
                numMinutes.Location = new Point(lblH.Left - 12 - numMinutes.Width, 6);
                lblM.Location = new Point(numMinutes.Left - 8 - mW, 9);
            }
            else
            {
                int durW = TextRenderer.MeasureText(lblDur.Text, lblDur.Font).Width + 4;
                int hW = TextRenderer.MeasureText(lblH.Text, lblH.Font).Width + 2;

                lblDur.Location = new Point(0, 9);
                numHours.Location = new Point(Math.Max(118, durW + 8), 6);
                lblH.Location = new Point(numHours.Right + 6, 9);
                numMinutes.Location = new Point(lblH.Left + hW + 14, 6);
                lblM.Location = new Point(numMinutes.Right + 6, 9);
            }

            pnlDuration.Controls.Add(lblDur);
            pnlDuration.Controls.Add(numHours);
            pnlDuration.Controls.Add(lblH);
            pnlDuration.Controls.Add(numMinutes);
            pnlDuration.Controls.Add(lblM);

            // Panel 2: Specific Date & Time
            pnlSpecific = new Panel { Location = new Point(16, 118), Size = new Size(508, 44), Visible = false };
            var lblOn = new Label { Text = isAr ? "التنفيذ في:" : "Execute on:", AutoSize = true, Font = UITheme.FontBody, ForeColor = UITheme.TextPrimary };
            dtpDate = new DateTimePicker
            {
                Width = 148,
                Format = DateTimePickerFormat.Short,
                Value = DateTime.Today
            };
            UITheme.StyleDateTimePicker(dtpDate);
            dtpTime = new DateTimePicker
            {
                Width = 128,
                Format = DateTimePickerFormat.Time,
                ShowUpDown = true,
                Value = DateTime.Now.AddHours(1)
            };
            UITheme.StyleDateTimePicker(dtpTime);

            if (isAr)
            {
                int onW = TextRenderer.MeasureText(lblOn.Text, lblOn.Font).Width + 4;
                lblOn.Location = new Point(504 - onW, 9);
                dtpDate.Location = new Point(lblOn.Left - 10 - dtpDate.Width, 6);
                dtpTime.Location = new Point(dtpDate.Left - 10 - dtpTime.Width, 6);
            }
            else
            {
                lblOn.Location = new Point(0, 9);
                dtpDate.Location = new Point(118, 6);
                dtpTime.Location = new Point(280, 6);
            }

            pnlSpecific.Controls.Add(lblOn);
            pnlSpecific.Controls.Add(dtpDate);
            pnlSpecific.Controls.Add(dtpTime);

            // Panel 3: Daily
            pnlDaily = new Panel { Location = new Point(16, 118), Size = new Size(508, 44), Visible = false };
            var lblEvery = new Label { Text = isAr ? "كل يوم عند:" : "Every day at:", AutoSize = true, Font = UITheme.FontBody, ForeColor = UITheme.TextPrimary };
            dtpDailyTime = new DateTimePicker
            {
                Width = 128,
                Format = DateTimePickerFormat.Time,
                ShowUpDown = true,
                Value = DateTime.Today.AddHours(23)
            };
            UITheme.StyleDateTimePicker(dtpDailyTime);

            if (isAr)
            {
                int evW = TextRenderer.MeasureText(lblEvery.Text, lblEvery.Font).Width + 4;
                lblEvery.Location = new Point(504 - evW, 9);
                dtpDailyTime.Location = new Point(lblEvery.Left - 10 - dtpDailyTime.Width, 6);
            }
            else
            {
                lblEvery.Location = new Point(0, 9);
                dtpDailyTime.Location = new Point(118, 6);
            }

            pnlDaily.Controls.Add(lblEvery);
            pnlDaily.Controls.Add(dtpDailyTime);

            // Buttons (centered symmetrically inside 540px card)
            var btnStart = new Button
            {
                Text = isAr ? "تفعيل المجدول" : "Start Scheduler",
                Size = new Size(158, 36),
                UseMnemonic = false
            };
            UITheme.StyleButton(btnStart, UITheme.Primary, Color.White, UITheme.PrimaryHover);
            UITheme.SetButtonIcon(btnStart, "Bolt", Color.White, 15);
            btnStart.Click += (s, e) => StartSchedule();

            var btnCancel = new Button
            {
                Text = isAr ? "إلغاء المؤقت" : "Cancel Timer",
                Size = new Size(146, 36),
                UseMnemonic = false
            };
            UITheme.StyleSecondaryButton(btnCancel);
            UITheme.SetButtonIcon(btnCancel, "Stop", UITheme.TextPrimary, 15);
            btnCancel.Click += (s, e) => CancelSchedule();

            int btnPairLeft = (540 - (btnStart.Width + 14 + btnCancel.Width)) / 2;
            if (isAr)
            {
                btnCancel.Location = new Point(btnPairLeft, 174);
                btnStart.Location = new Point(btnCancel.Right + 14, 174);
            }
            else
            {
                btnStart.Location = new Point(btnPairLeft, 174);
                btnCancel.Location = new Point(btnStart.Right + 14, 174);
            }

            // Status Display Card
            lblCountdown = new Label
            {
                Text = "--:--:--",
                Font = new Font(LocalizationService.ActiveFontFamilyName, 22F, FontStyle.Regular),
                ForeColor = UITheme.Primary,
                Location = new Point(16, 226),
                Size = new Size(504, 46),
                TextAlign = ContentAlignment.MiddleCenter
            };

            lblStatus = new Label
            {
                Text = isAr ? "الحالة: لا يوجد جدولة طاقة نشطة حالياً." : "Status: No scheduled power event active.",
                Font = UITheme.FontBody,
                ForeColor = UITheme.TextSecondary,
                Location = new Point(16, 282),
                Size = new Size(504, 40),
                TextAlign = ContentAlignment.MiddleCenter
            };

            card.Controls.Add(lblAction);
            card.Controls.Add(cbAction);
            card.Controls.Add(lblMode);
            card.Controls.Add(rbDuration);
            card.Controls.Add(rbSpecificDateTime);
            card.Controls.Add(rbDaily);
            card.Controls.Add(pnlDuration);
            card.Controls.Add(pnlSpecific);
            card.Controls.Add(pnlDaily);
            card.Controls.Add(btnStart);
            card.Controls.Add(btnCancel);
            card.Controls.Add(lblCountdown);
            card.Controls.Add(lblStatus);

            var btnClose = new Button
            {
                Text = isAr ? "إغلاق" : "Close",
                Location = isAr ? new Point(18, 412) : new Point(454, 412),
                Size = new Size(104, 34)
            };
            UITheme.StyleSecondaryButton(btnClose);
            btnClose.Click += (s, e) => Close();

            Controls.Add(lblHeader);
            Controls.Add(card);
            Controls.Add(btnClose);

            UITheme.StyleToolHeader(this, lblHeader, "Clock", UITheme.Purple, isAr);
            ResumeLayout(false);
            PerformLayout();
        }

        private void UpdateModePanels()
        {
            pnlDuration.Visible = rbDuration.Checked;
            pnlSpecific.Visible = rbSpecificDateTime.Checked;
            pnlDaily.Visible = rbDaily.Checked;
        }

        private void StartSchedule()
        {
            bool isAr = LocalizationService.IsArabic;
            if (rbDuration.Checked)
            {
                int totalMins = (int)(numHours.Value * 60 + numMinutes.Value);
                if (totalMins <= 0)
                {
                    CleanRamBoosterApp.WpfUI.AppDialog.Show(this,
                        isAr ? "يرجى تحديد مدة زمنية أكبر من 0 دقيقة." : "Please specify a duration greater than 0 minutes.",
                        isAr ? "مدة غير صالحة" : "Invalid Duration",
                        CleanRamBoosterApp.WpfUI.AppDialogTone.Warning);
                    return;
                }
                targetTime = DateTime.Now.AddMinutes(totalMins);
            }
            else if (rbSpecificDateTime.Checked)
            {
                DateTime selectedDate = dtpDate.Value.Date;
                TimeSpan selectedTime = dtpTime.Value.TimeOfDay;
                targetTime = selectedDate.Add(selectedTime);

                if (targetTime <= DateTime.Now)
                {
                    CleanRamBoosterApp.WpfUI.AppDialog.Show(this,
                        isAr ? "يرجى اختيار تاريخ ووقت مستقبلي." : "Please select a future date and time.",
                        isAr ? "وقت غير صالح" : "Invalid Date/Time",
                        CleanRamBoosterApp.WpfUI.AppDialogTone.Warning);
                    return;
                }
            }
            else // Daily
            {
                TimeSpan dailyTime = dtpDailyTime.Value.TimeOfDay;
                targetTime = DateTime.Today.Add(dailyTime);
                if (targetTime <= DateTime.Now)
                {
                    targetTime = targetTime.AddDays(1);
                }
            }
            shutdownTimer?.Stop();
            shutdownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            shutdownTimer.Tick += (s, e) =>
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
                    lblCountdown.Text = $"{remaining:dd\\.hh\\:mm\\:ss}".TrimStart('0', '.');
                    if (lblCountdown.Text.Length <= 5) lblCountdown.Text = $"{remaining:mm\\:ss}";
                    lblStatus.Text = isAr
                        ? $"تمت جدولة {cbAction.SelectedItem} في \u200E{targetTime:yyyy-MM-dd HH:mm:ss}\u200E"
                        : $"{cbAction.SelectedItem} scheduled on {targetTime:yyyy-MM-dd HH:mm:ss}";
                    lblStatus.ForeColor = UITheme.WarningHover;
                }
            };
            shutdownTimer.Start();

            var initialRemaining = targetTime - DateTime.Now;
            lblCountdown.Text = $"{initialRemaining:hh\\:mm\\:ss}";
            lblStatus.Text = isAr
                ? $"تمت جدولة {cbAction.SelectedItem} في \u200E{targetTime:yyyy-MM-dd HH:mm:ss}\u200E"
                : $"{cbAction.SelectedItem} scheduled for {targetTime:yyyy-MM-dd HH:mm:ss}";
            lblStatus.ForeColor = UITheme.WarningHover;
        }

        private void CancelSchedule()
        {
            if (shutdownTimer != null)
            {
                shutdownTimer.Stop();
                shutdownTimer = null;
            }
            lblCountdown.Text = "--:--:--";
            lblStatus.Text = LocalizationService.IsArabic
                ? "الحالة: تم إلغاء جدولة الطاقة."
                : "Status: Scheduled power event cancelled.";
            lblStatus.ForeColor = UITheme.Success;
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
                        Application.SetSuspendState(PowerState.Suspend, true, true);
                        break;
                    case 3:
                        Application.SetSuspendState(PowerState.Hibernate, true, true);
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
                lblStatus.Text = isAr ? $"خطأ في تنفيذ إجراء الطاقة: {ex.Message}" : $"Power action error: {ex.Message}";
                CleanRamBoosterApp.WpfUI.AppDialog.Show(this, lblStatus.Text, isAr ? "خطأ" : "Error", CleanRamBoosterApp.WpfUI.AppDialogTone.Error);
            }
        }
    }
}
