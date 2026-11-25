using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using CleanRamBoosterApp.Services;
using CleanRamBoosterApp.UI;

namespace CleanRamBoosterApp.Forms
{
    public class ToolboxSystemInfoForm : Form
    {
        private readonly HardwareInfoService hardwareService = new();
        private DetailedSystemReport? currentReport;
        private int currentCategoryIndex = 0;

        // UI Components
        private Panel pnlHeader = null!;
        private Panel pnlSidebar = null!;
        private Panel pnlContentContainer = null!;
        private DoubleBufferedCardPanel cardSpecs = null!;
        private ListView lvSpecs = null!;
        private Panel pnlBottomSpacer = null!;
        private Panel pnlBottomMetrics = null!;
        private Panel pnlFooter = null!;
        private readonly List<Button> navButtons = new();

        // Live CPU & Memory Graph Components
        private System.Windows.Forms.Timer tmrLive = null!;
        private int currentCpuPercent = 0;
        private int currentCpuMhz = 2000;
        private int baseCpuMhz = 2000;
        private readonly Queue<int> cpuHistory = new();
        private const int MaxHistorySamples = 60;
        private Panel pnlCpuMeter = null!;
        private Panel pnlCpuChart = null!;
        private Label lblStatusLoading = null!;
        private PictureBox pbIcon = null!;

        public ToolboxSystemInfoForm(DetailedSystemReport? report = null)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;

            bool isAr = LocalizationService.IsArabic;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = isAr ? "معلومات النظام - PC Boost" : "System Information - PC Boost";
            ClientSize = new Size(924, 622);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            Font = UITheme.FontBody;
            BackColor = UITheme.BgContent;
            RightToLeft = isAr ? RightToLeft.Yes : RightToLeft.No;
            if (UITheme.AppIcon != null) Icon = UITheme.AppIcon;
            UITheme.ApplyDwmDarkMode(Handle, UITheme.IsDarkMode);

            // Initialize CPU usage history & baseline clock
            currentCpuMhz = HardwareInfoService.GetLiveCpuClockMhz(out baseCpuMhz, out _);
            for (int i = 0; i < MaxHistorySamples; i++)
            {
                cpuHistory.Enqueue(0);
            }

            BuildLayout();
            ApplyTheme();

            ResumeLayout(false);
            PerformLayout();

            // Live Update Timer (1 second interval for CPU & RAM monitoring)
            tmrLive = new System.Windows.Forms.Timer { Interval = 1000 };
            tmrLive.Tick += (s, e) => UpdateLiveMetrics();

            DpiChanged += (s, e) =>
            {
                if (pbIcon != null)
                {
                    pbIcon.Image = UITheme.GetScaledAppIcon(48, this) ?? UITheme.AppIconBitmap;
                }
                AdjustSpecsColumns();
                if (currentReport != null && currentCategoryIndex >= 0 && currentCategoryIndex < currentReport.Categories.Count)
                {
                    string catId = currentReport.Categories[currentCategoryIndex].CategoryId;
                    if (catId == "Memory") RenderMemorySlotsBottomBar();
                    else if (catId == "CPU") LayoutCpuBottomControls();
                }
            };

            if (report != null)
            {
                currentReport = report;
                lblStatusLoading.Visible = false;
                lvSpecs.Visible = true;
                tmrLive.Start();
                SelectCategory(0);
            }
            else
            {
                // Load specs asynchronously to keep UI responsive
                LoadSpecsAsync();
            }

            FormClosing += (s, e) =>
            {
                tmrLive.Stop();
                tmrLive.Dispose();
            };
        }

        private void BuildLayout()
        {
            bool isAr = LocalizationService.IsArabic;

            // 1. Header Banner
            pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 72,
                BackColor = UITheme.SidebarBg,
                Padding = new Padding(16, 12, 16, 12)
            };
            pnlHeader.Paint += (s, e) =>
            {
                using var pen = new Pen(UITheme.BorderSubtle, 1);
                e.Graphics.DrawLine(pen, 0, pnlHeader.Height - 1, pnlHeader.Width, pnlHeader.Height - 1);
            };

            pbIcon = new PictureBox
            {
                Size = new Size(48, 48),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            pbIcon.Image = UITheme.GetScaledAppIcon(48, this) ?? UITheme.AppIconBitmap;
            if (pbIcon.Image == null)
            {
                pbIcon.Paint += (s, e) =>
                {
                    VectorIcons.DrawIcon(e.Graphics, "OsWindows", new RectangleF(6, 6, pbIcon.Width - 12, pbIcon.Height - 12), UITheme.AccentCyan, 1.85f);
                };
            }

            var lblTitle = new Label
            {
                Text = isAr ? "معلومات النظام الشاملة" : "System Information",
                Font = UITheme.FontHeader,
                ForeColor = UITheme.TextPrimary,
                AutoSize = true
            };

            var lblSub = new Label
            {
                Text = isAr ? "فحص وتجميع تفاصيل عتاد الجهاز وبيئة نظام التشغيل بدقة واحترافية." : "Collects and displays detailed information about your system hardware and OS environment.",
                Font = UITheme.FontSub,
                ForeColor = UITheme.TextSecondary,
                AutoSize = true
            };

            if (isAr)
            {
                pbIcon.Location = new Point(pnlHeader.Width - 16 - 48, 12);
                pbIcon.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                lblTitle.Location = new Point(pnlHeader.Width - 76 - lblTitle.PreferredSize.Width, 12);
                lblTitle.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                lblSub.Location = new Point(pnlHeader.Width - 78 - lblSub.PreferredSize.Width, 40);
                lblSub.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            }
            else
            {
                pbIcon.Location = new Point(16, 12);
                lblTitle.Location = new Point(76, 12);
                lblSub.Location = new Point(78, 40);
            }

            pnlHeader.Controls.Add(pbIcon);
            pnlHeader.Controls.Add(lblTitle);
            pnlHeader.Controls.Add(lblSub);

            // 2. Footer Toolbar
            pnlFooter = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                BackColor = UITheme.SidebarBottomBg,
                Padding = new Padding(16, 11, 16, 11)
            };
            pnlFooter.Paint += (s, e) =>
            {
                using var pen = new Pen(UITheme.BorderSubtle, 1);
                e.Graphics.DrawLine(pen, 0, 0, pnlFooter.Width, 0);
            };

            var btnExport = new Button
            {
                Text = isAr ? "تصدير التقرير..." : "Export Report...",
                Font = UITheme.FontButton,
                Size = new Size(168, 34),
                Cursor = Cursors.Hand
            };
            UITheme.StyleButton(btnExport, UITheme.Primary, Color.White, UITheme.PrimaryHover);
            UITheme.SetButtonIcon(btnExport, "Export", Color.White);
            btnExport.Click += (s, e) => PromptExport();

            var btnCopy = new Button
            {
                Text = isAr ? "نسخ المواصفات" : "Copy Specs",
                Font = UITheme.FontButton,
                Size = new Size(156, 34),
                Cursor = Cursors.Hand
            };
            UITheme.StyleSecondaryButton(btnCopy);
            UITheme.SetButtonIcon(btnCopy, "Copy");
            btnCopy.Click += (s, e) => CopyCurrentCategoryToClipboard();

            var btnClose = new Button
            {
                Text = isAr ? "إغلاق" : "Close",
                Font = UITheme.FontButton,
                Size = new Size(100, 34),
                Cursor = Cursors.Hand
            };
            UITheme.StyleSecondaryButton(btnClose);
            btnClose.Click += (s, e) => Close();

            if (isAr)
            {
                btnExport.Location = new Point(pnlFooter.Width - 16 - 168, 11);
                btnExport.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                btnCopy.Location = new Point(pnlFooter.Width - 16 - 168 - 10 - 156, 11);
                btnCopy.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                btnClose.Location = new Point(16, 11);
                btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            }
            else
            {
                btnExport.Location = new Point(16, 11);
                btnCopy.Location = new Point(194, 11);
                btnClose.Location = new Point(pnlFooter.Width - 116, 11);
                btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            }

            pnlFooter.Controls.Add(btnExport);
            pnlFooter.Controls.Add(btnCopy);
            pnlFooter.Controls.Add(btnClose);

            // 3. Navigation Sidebar
            pnlSidebar = new Panel
            {
                Dock = isAr ? DockStyle.Right : DockStyle.Left,
                Width = 224,
                BackColor = UITheme.SidebarBg,
                Padding = new Padding(8, 10, 8, 10)
            };
            pnlSidebar.Paint += (s, e) =>
            {
                using var pen = new Pen(UITheme.BorderSubtle, 1);
                int lineX = isAr ? 0 : pnlSidebar.Width - 1;
                e.Graphics.DrawLine(pen, lineX, 0, lineX, pnlSidebar.Height);
            };

            string[] catTitles = isAr ? new string[] {
                "نظرة عامة",
                "نظام التشغيل",
                "المعالج واللوحة الأم",
                "الذاكرة العشوائية",
                "التخزين والأقراص",
                "بطاقة الشاشة والعرض",
                "محولات الشبكة",
                "الأجهزة الأخرى"
            } : new string[] {
                "Overall",
                "Operating System",
                "Processor & Mainboard",
                "Memory Device",
                "Drives",
                "Display",
                "Network",
                "Other Devices"
            };
            string[] catIconKeys = new string[] {
                "Globe",
                "OsWindows",
                "Cpu",
                "Memory",
                "Drive",
                "Display",
                "Network",
                "Keyboard"
            };

            int y = 10;
            for (int i = 0; i < catTitles.Length; i++)
            {
                int index = i;
                string title = catTitles[i];
                string iconKey = catIconKeys[i];
                bool isHovered = false;
                var btn = new Button
                {
                    Text = string.Empty,
                    AccessibleName = title,
                    Font = UITheme.FontBody,
                    ForeColor = UITheme.SidebarNavFg,
                    BackColor = UITheme.SidebarBg,
                    FlatStyle = FlatStyle.Flat,
                    Location = new Point(6, y),
                    Size = new Size(212, 40),
                    Cursor = Cursors.Hand,
                    UseMnemonic = false
                };
                btn.FlatAppearance.BorderSize = 0;
                btn.Click += (s, e) => SelectCategory(index);
                btn.MouseEnter += (s, e) => { isHovered = true; btn.Invalidate(); };
                btn.MouseLeave += (s, e) => { isHovered = false; btn.Invalidate(); };
                btn.Paint += (s, e) =>
                {
                    var g = e.Graphics;
                    float sc = UITheme.GetDpiScale(btn);
                    bool isActive = (currentCategoryIndex == index);

                    Color rowBg = isActive
                        ? UITheme.SidebarNavActiveBg
                        : (isHovered
                            ? (UITheme.IsDarkMode ? Color.FromArgb(30, 41, 59) : Color.FromArgb(241, 245, 249))
                            : UITheme.SidebarBg);
                    using (var bgBrush = new SolidBrush(rowBg))
                    {
                        g.FillRectangle(bgBrush, btn.ClientRectangle);
                    }

                    if (isActive)
                    {
                        int barW = Math.Max(3, (int)Math.Round(4f * sc));
                        int barPadY = Math.Max(4, (int)Math.Round(5f * sc));
                        int barX = isAr ? btn.Width - barW : 0;
                        using var accentBrush = new SolidBrush(UITheme.IsDarkMode ? UITheme.AccentCyan : UITheme.Primary);
                        g.FillRectangle(accentBrush, barX, barPadY, barW, btn.Height - barPadY * 2);
                    }

                    int iconSize = Math.Max(14, (int)Math.Round(18f * sc));
                    int padOuter = Math.Max(10, (int)Math.Round(14f * sc));
                    int gap = Math.Max(8, (int)Math.Round(10f * sc));
                    int iconX = isAr ? (btn.Width - padOuter - iconSize) : padOuter;
                    int iconY = (btn.Height - iconSize) / 2;

                    Color fg = isActive ? UITheme.SidebarNavActiveFg : UITheme.SidebarNavFg;
                    VectorIcons.DrawIcon(g, iconKey, new RectangleF(iconX, iconY, iconSize, iconSize), fg, 1.75f);

                    int textPadEnd = Math.Max(6, (int)Math.Round(8f * sc));
                    Rectangle textRect = isAr
                        ? new Rectangle(textPadEnd, 0, Math.Max(20, iconX - gap - textPadEnd), btn.Height)
                        : new Rectangle(iconX + iconSize + gap, 0, Math.Max(20, btn.Width - (iconX + iconSize + gap) - textPadEnd), btn.Height);
                    UITheme.DrawBidiText(g, title, btn.Font, textRect, fg, isAr, false);
                };

                navButtons.Add(btn);
                pnlSidebar.Controls.Add(btn);
                y += 44;
            }

            // 4. Content Area Container
            pnlContentContainer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UITheme.BgContent,
                Padding = new Padding(12)
            };

            // Card Container for Details Table (8px rounded corners, matching Startup Manager table container)
            cardSpecs = new DoubleBufferedCardPanel
            {
                Dock = DockStyle.Fill,
                BackColor = UITheme.CardBg,
                BorderColor = UITheme.BorderSubtle,
                CornerRadius = 8,
                Padding = new Padding(8)
            };

            // Loading status label
            lblStatusLoading = new Label
            {
                Text = isAr ? "جاري فحص وتجميع مواصفات العتاد والنظام..." : "Collecting hardware and system configuration...",
                Font = UITheme.FontTitle,
                ForeColor = UITheme.TextSecondary,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Visible = true
            };
            cardSpecs.Controls.Add(lblStatusLoading);

            // ListView for Details Table
            lvSpecs = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                BorderStyle = BorderStyle.None,
                BackColor = UITheme.CardBg,
                ForeColor = UITheme.TextPrimary,
                Font = UITheme.FontBody,
                RightToLeft = isAr ? RightToLeft.Yes : RightToLeft.No,
                Visible = true
            };
            UITheme.StyleListView(lvSpecs);

            lvSpecs.Columns.Add(isAr ? "اسم البند" : "Item Name", 250);
            lvSpecs.Columns.Add(isAr ? "القيمة والمواصفات" : "Value", 450);

            cardSpecs.Controls.Add(lvSpecs);
            if (currentReport == null)
            {
                lblStatusLoading.BringToFront();
            }
            else
            {
                lblStatusLoading.Visible = false;
            }

            // 5. Bottom Metrics Panel (for CPU Graph & RAM slot cards)
            pnlBottomSpacer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 10,
                BackColor = Color.Transparent,
                Visible = false
            };

            pnlBottomMetrics = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 162,
                BackColor = UITheme.CardBg,
                Visible = false,
                Padding = new Padding(10)
            };
            UITheme.EnableDoubleBuffering(pnlBottomMetrics);
            pnlBottomMetrics.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = new Rectangle(0, 0, pnlBottomMetrics.Width - 1, pnlBottomMetrics.Height - 1);
                using var path = UITheme.GetRoundedPath(rect, 8);
                using var bgBrush = new SolidBrush(UITheme.CardBg);
                g.FillPath(bgBrush, path);
                using var pen = new Pen(UITheme.BorderSubtle, 1);
                g.DrawPath(pen, path);
            };
            pnlBottomMetrics.Resize += (s, e) =>
            {
                if (pnlCpuMeter != null && pnlCpuMeter.Visible)
                {
                    LayoutCpuBottomControls();
                }
            };

            BuildLiveMetricsControls();

            pnlContentContainer.Controls.Add(pnlBottomMetrics);
            pnlContentContainer.Controls.Add(pnlBottomSpacer);
            pnlContentContainer.Controls.Add(cardSpecs);
            pnlBottomMetrics.SendToBack();
            cardSpecs.BringToFront();

            Controls.Add(pnlContentContainer);
            Controls.Add(pnlSidebar);
            Controls.Add(pnlFooter);
            Controls.Add(pnlHeader);
        }

        private void BuildLiveMetricsControls()
        {
            // Left CPU Usage Meter Block
            pnlCpuMeter = new Panel
            {
                BackColor = Color.FromArgb(10, 16, 26)
            };
            UITheme.EnableDoubleBuffering(pnlCpuMeter);
            pnlCpuMeter.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float sc = UITheme.GetDpiScale(pnlCpuMeter);
                int Px(int v) => Math.Max(1, (int)Math.Round(v * sc));
                bool isAr = LocalizationService.IsArabic;

                int w = pnlCpuMeter.Width;
                int h = pnlCpuMeter.Height;
                using var borderPen = new Pen(Color.FromArgb(26, 40, 60), 1);
                g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);

                // Clear Card Header
                var hdrRect = new Rectangle(Px(6), Px(6), Math.Max(40, w - Px(12)), Px(18));
                UITheme.DrawBidiText(g, isAr ? "استخدام المعالج" : "CPU USAGE", UITheme.FontSmall, hdrRect, Color.FromArgb(148, 163, 184), isAr, center: true);

                // Segmented LED Usage Bar
                int barW = Px(42);
                int barX = (w - barW) / 2;
                int barY = Px(28);
                int barH = Math.Max(Px(44), h - Px(60));
                int segments = 14;
                float step = (float)barH / segments;
                int segH = Math.Max(2, (int)Math.Floor(step - Math.Max(1f, sc)));
                int activeSegments = (int)Math.Round((currentCpuPercent / 100.0) * segments);

                for (int i = 0; i < segments; i++)
                {
                    int segY = barY + barH - (int)Math.Round((i + 1) * step);
                    bool active = i < activeSegments;
                    Color segColor = active
                        ? (i > 11 ? Color.FromArgb(239, 68, 68) : (i > 8 ? Color.FromArgb(245, 158, 11) : Color.FromArgb(16, 185, 129)))
                        : Color.FromArgb(20, 32, 26);
                    using var brush = new SolidBrush(segColor);
                    g.FillRectangle(brush, barX, segY, barW, segH);
                }

                // Clear Percentage Readout at bottom
                Color pctColor = currentCpuPercent > 80
                    ? Color.FromArgb(239, 68, 68)
                    : (currentCpuPercent > 50 ? Color.FromArgb(245, 158, 11) : Color.FromArgb(34, 197, 94));
                var pctRect = new Rectangle(Px(6), h - Px(29), Math.Max(40, w - Px(12)), Px(23));
                TextRenderer.DrawText(g, $"{currentCpuPercent}%", UITheme.FontHeader, pctRect, pctColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            };

            // Right Oscilloscope CPU Usage Chart (with live Clock + Base readout in header)
            pnlCpuChart = new Panel
            {
                BackColor = Color.FromArgb(7, 13, 18)
            };
            UITheme.EnableDoubleBuffering(pnlCpuChart);
            pnlCpuChart.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float sc = UITheme.GetDpiScale(pnlCpuChart);
                int Px(int v) => Math.Max(1, (int)Math.Round(v * sc));
                bool isAr = LocalizationService.IsArabic;

                int w = pnlCpuChart.Width;
                int h = pnlCpuChart.Height;
                int headerH = Px(26);

                // Top Header & Legend Bar
                using (var hdrBg = new SolidBrush(Color.FromArgb(13, 21, 34)))
                {
                    g.FillRectangle(hdrBg, 0, 0, w, headerH);
                }
                using (var hdrLine = new Pen(Color.FromArgb(26, 40, 60), 1))
                {
                    g.DrawLine(hdrLine, 0, headerH, w, headerH);
                }

                double curGhz = currentCpuMhz / 1000.0;
                double baseGhz = Math.Max(0.1, baseCpuMhz / 1000.0);
                string leftTitle = isAr ? "استخدام المعالج" : "CPU Usage";
                int titleW = Math.Min(w / 2, TextRenderer.MeasureText(leftTitle, UITheme.FontSmall).Width + Px(14));

                if (isAr)
                {
                    var titleRect = new Rectangle(w - titleW, 0, titleW - Px(6), headerH);
                    var legRect = new Rectangle(Px(8), 0, Math.Max(Px(80), w - titleW - Px(12)), headerH);
                    string rightLegend = $"الاستخدام: {currentCpuPercent}%  •  التردد: \u200E{curGhz:0.00} GHz (الأساسي: {baseGhz:0.00} GHz)";
                    UITheme.DrawBidiText(g, leftTitle, UITheme.FontSmall, titleRect, Color.FromArgb(226, 232, 240), true, center: false);
                    TextRenderer.DrawText(g, rightLegend, UITheme.FontSmall, legRect, Color.FromArgb(56, 189, 248),
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.RightToLeft);
                }
                else
                {
                    var titleRect = new Rectangle(Px(8), 0, Math.Max(Px(60), titleW - Px(6)), headerH);
                    TextRenderer.DrawText(g, leftTitle, UITheme.FontSmall, titleRect, Color.FromArgb(226, 232, 240),
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                    string usageText = $"Usage: {currentCpuPercent}%";
                    string sepText = "  •  ";
                    string clockText = $"Clock: {curGhz:0.00} GHz (Base: {baseGhz:0.00} GHz)";
                    var exactFlags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
                    int clkW = TextRenderer.MeasureText(g, clockText, UITheme.FontSmall, new Size(int.MaxValue, headerH), exactFlags).Width;
                    int sepW = TextRenderer.MeasureText(g, sepText, UITheme.FontSmall, new Size(int.MaxValue, headerH), exactFlags).Width;
                    int usgW = TextRenderer.MeasureText(g, usageText, UITheme.FontSmall, new Size(int.MaxValue, headerH), exactFlags).Width;

                    int rightEdge = w - Px(8);
                    int minLegLeft = titleRect.Right + Px(8);
                    int clkX = Math.Max(minLegLeft, rightEdge - clkW);
                    var clkRect = new Rectangle(clkX, 0, Math.Max(10, rightEdge - clkX), headerH);
                    TextRenderer.DrawText(g, clockText, UITheme.FontSmall, clkRect, Color.FromArgb(56, 189, 248),
                        exactFlags | TextFormatFlags.Right | TextFormatFlags.EndEllipsis);

                    int usgRight = clkX - sepW;
                    if (usgRight - usgW >= minLegLeft)
                    {
                        var sepRect = new Rectangle(usgRight, 0, sepW, headerH);
                        TextRenderer.DrawText(g, sepText, UITheme.FontSmall, sepRect, Color.FromArgb(100, 116, 139),
                            exactFlags | TextFormatFlags.HorizontalCenter);

                        var usgRect = new Rectangle(usgRight - usgW, 0, usgW, headerH);
                        TextRenderer.DrawText(g, usageText, UITheme.FontSmall, usgRect, Color.FromArgb(34, 197, 94),
                            exactFlags | TextFormatFlags.Left);
                    }
                }

                int plotLeft = Px(34);
                int plotRight = w - Px(8);
                int plotTop = headerH + Px(6);
                int plotBottom = h - Px(6);
                int plotW = Math.Max(20, plotRight - plotLeft);
                int plotH = Math.Max(20, plotBottom - plotTop);

                // 1. Draw Grid Lines & 0%/50%/100% Y-Axis Scale Labels
                int gridStepX = Px(28);
                using var gridPen = new Pen(Color.FromArgb(18, 42, 46), 1);
                for (int x = plotLeft; x <= plotRight; x += gridStepX) g.DrawLine(gridPen, x, plotTop, x, plotBottom);

                int[] yLevels = { 100, 50, 0 };
                foreach (int lvl in yLevels)
                {
                    int gy = plotBottom - (int)Math.Round((lvl / 100.0) * plotH);
                    g.DrawLine(gridPen, plotLeft, gy, plotRight, gy);
                    var lblRect = new Rectangle(Px(2), gy - Px(8), Px(30), Px(16));
                    TextRenderer.DrawText(g, $"{lvl}%", UITheme.FontSmall, lblRect, Color.FromArgb(100, 116, 139),
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }

                // 2. Plot CPU Usage (%) Area Waveform (Neon Green)
                var samples = cpuHistory.ToArray();
                if (samples.Length > 1)
                {
                    float stepX = (float)plotW / (samples.Length - 1);
                    PointF[] points = new PointF[samples.Length];
                    for (int i = 0; i < samples.Length; i++)
                    {
                        float px = plotLeft + (i * stepX);
                        float py = plotBottom - ((Math.Clamp(samples[i], 0, 100) / 100f) * plotH);
                        points[i] = new PointF(px, Math.Clamp(py, plotTop, plotBottom));
                    }

                    using var fillPath = new GraphicsPath();
                    fillPath.AddLine(plotLeft, plotBottom, points[0].X, points[0].Y);
                    for (int i = 1; i < points.Length; i++) fillPath.AddLine(points[i - 1], points[i]);
                    fillPath.AddLine(points[^1].X, points[^1].Y, plotRight, plotBottom);
                    fillPath.CloseFigure();
                    using var areaBrush = new LinearGradientBrush(
                        new Rectangle(plotLeft, plotTop, plotW, plotH),
                        Color.FromArgb(55, 34, 197, 94),
                        Color.FromArgb(5, 34, 197, 94),
                        LinearGradientMode.Vertical);
                    g.FillPath(areaBrush, fillPath);

                    using var linePen = new Pen(Color.FromArgb(34, 197, 94), 2f);
                    g.DrawLines(linePen, points);
                }

                // Border
                using var borderPen = new Pen(Color.FromArgb(26, 40, 60), 1);
                g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);
            };

            pnlBottomMetrics.Controls.Add(pnlCpuMeter);
            pnlBottomMetrics.Controls.Add(pnlCpuChart);
            LayoutCpuBottomControls();
        }

        private void LayoutCpuBottomControls()
        {
            if (pnlBottomMetrics == null || pnlCpuMeter == null || pnlCpuChart == null) return;
            float sc = UITheme.GetDpiScale(this);
            int Px(int v) => Math.Max(1, (int)Math.Round(v * sc));

            int pad = Px(10);
            int gap = Px(10);
            int innerH = Math.Max(Px(80), pnlBottomMetrics.ClientSize.Height - pad * 2);
            int meterW = Px(160);
            bool isAr = LocalizationService.IsArabic;

            if (isAr)
            {
                pnlCpuMeter.Location = new Point(Math.Max(pad, pnlBottomMetrics.ClientSize.Width - pad - meterW), pad);
                pnlCpuMeter.Size = new Size(meterW, innerH);
                pnlCpuChart.Location = new Point(pad, pad);
                pnlCpuChart.Size = new Size(Math.Max(Px(120), pnlCpuMeter.Left - gap - pad), innerH);
            }
            else
            {
                pnlCpuMeter.Location = new Point(pad, pad);
                pnlCpuMeter.Size = new Size(meterW, innerH);
                pnlCpuChart.Location = new Point(pnlCpuMeter.Right + gap, pad);
                pnlCpuChart.Size = new Size(Math.Max(Px(120), pnlBottomMetrics.ClientSize.Width - pad - pnlCpuChart.Left), innerH);
            }
        }

        public void LoadSync()
        {
            if (currentReport != null) return;
            try
            {
                currentReport = hardwareService.GetDetailedReport();
                lblStatusLoading.Visible = false;
                lvSpecs.Visible = true;
                tmrLive.Start();
                SelectCategory(0);
            }
            catch { }
        }

        private async void LoadSpecsAsync()
        {
            try
            {
                if (currentReport != null) return;
                var rep = await Task.Run(() => hardwareService.GetDetailedReport());

                if (IsDisposed) return;
                currentReport = rep;

                this.BeginInvoke(new Action(() =>
                {
                    if (IsDisposed) return;
                    lblStatusLoading.Visible = false;
                    lvSpecs.Visible = true;
                    tmrLive.Start();
                    SelectCategory(0);
                }));
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                {
                    string errTxt = LocalizationService.IsArabic
                        ? $"تعذر قراءة تفاصيل العتاد: {ex.Message}"
                        : $"Error reading hardware configuration: {ex.Message}";
                    try { this.BeginInvoke(new Action(() => lblStatusLoading.Text = errTxt)); } catch { }
                }
            }
        }

        public void LoadSpecs(DetailedSystemReport report)
        {
            currentReport = report;
            lblStatusLoading.Visible = false;
            lvSpecs.Visible = true;
            tmrLive.Start();
            SelectCategory(0);
        }

        public void SelectCategory(int index)
        {
            if (currentReport == null || index < 0 || index >= currentReport.Categories.Count) return;

            currentCategoryIndex = index;

            // Highlight active button
            for (int i = 0; i < navButtons.Count; i++)
            {
                bool isActive = (i == index);
                navButtons[i].BackColor = isActive ? UITheme.SidebarNavActiveBg : UITheme.SidebarBg;
                navButtons[i].ForeColor = isActive ? UITheme.SidebarNavActiveFg : UITheme.SidebarNavFg;
                navButtons[i].Invalidate();
            }

            var cat = currentReport.Categories[index];
            PopulateTable(cat);

            // Conditional Bottom Metrics Panel
            if (cat.CategoryId == "CPU")
            {
                pnlBottomMetrics.Height = UITheme.ScalePx(162, this);
                pnlBottomSpacer.Visible = true;
                pnlBottomMetrics.Visible = true;
                pnlBottomMetrics.SendToBack();
                cardSpecs.BringToFront();
                SetCpuMetricsVisible(true);
                LayoutCpuBottomControls();
                UpdateLiveMetrics();
            }
            else if (cat.CategoryId == "Memory")
            {
                pnlBottomMetrics.Height = UITheme.ScalePx(182, this);
                pnlBottomSpacer.Visible = true;
                pnlBottomMetrics.Visible = true;
                pnlBottomMetrics.SendToBack();
                cardSpecs.BringToFront();
                SetCpuMetricsVisible(false);
                RenderMemorySlotsBottomBar();
            }
            else
            {
                pnlBottomSpacer.Visible = false;
                pnlBottomMetrics.Visible = false;
            }
        }

        private void SetCpuMetricsVisible(bool visible)
        {
            if (pnlCpuMeter != null) pnlCpuMeter.Visible = visible;
            if (pnlCpuChart != null) pnlCpuChart.Visible = visible;
            if (!visible) return;
            var toRemove = new List<Control>();
            foreach (Control c in pnlBottomMetrics.Controls)
            {
                if (c != pnlCpuMeter && c != pnlCpuChart) toRemove.Add(c);
            }
            foreach (var c in toRemove) pnlBottomMetrics.Controls.Remove(c);
        }

        private void PopulateTable(SystemReportCategory cat)
        {
            lvSpecs.BeginUpdate();
            lvSpecs.Items.Clear();

            foreach (var grp in cat.Groups)
            {
                // Group Header Banner
                var hdr = new ListViewItem(grp.GroupName) { Tag = "GROUP_HEADER" };
                hdr.SubItems.Add("");
                lvSpecs.Items.Add(hdr);

                // Child Items
                foreach (var item in grp.Items)
                {
                    var lvi = new ListViewItem("   " + item.Name);
                    lvi.SubItems.Add(item.Value);
                    lvSpecs.Items.Add(lvi);
                }
            }

            lvSpecs.EndUpdate();
            AdjustSpecsColumns();
        }

        private void AdjustSpecsColumns()
        {
            if (lvSpecs != null && lvSpecs.Columns.Count >= 2 && lvSpecs.ClientSize.Width > UITheme.ScalePx(300, lvSpecs))
            {
                int col0 = UITheme.ScalePx(240, lvSpecs);
                int avail = lvSpecs.ClientSize.Width - col0 - 2;
                lvSpecs.Columns[0].Width = col0;
                lvSpecs.Columns[1].Width = Math.Max(UITheme.ScalePx(200, lvSpecs), avail);
            }
        }

        private void RenderMemorySlotsBottomBar()
        {
            var toRemove = new List<Control>();
            foreach (Control c in pnlBottomMetrics.Controls)
            {
                if (c != pnlCpuMeter && c != pnlCpuChart) toRemove.Add(c);
            }
            foreach (var c in toRemove)
            {
                pnlBottomMetrics.Controls.Remove(c);
                c.Dispose();
            }

            if (currentReport == null) return;

            float scale = UITheme.GetDpiScale(this);
            int Px(int v) => Math.Max(1, (int)Math.Round(v * scale));

            bool isAr = LocalizationService.IsArabic;
            bool isDark = UITheme.IsDarkMode;
            int padOuter = Px(14);
            int innerW = Math.Max(Px(320), pnlBottomMetrics.Width - (padOuter * 2));
            int slotCount = Math.Max(1, currentReport.RamSlots.Count);
            int gap = Px(12);
            int cardW = Math.Max(Px(155), (innerW - ((slotCount - 1) * gap)) / slotCount);
            double totalInstalledGB = Math.Max(0.1, currentReport.RamSlots.Where(s => s.IsPopulated).Sum(s => s.CapacityGB));

            // Header bar (title + architecture/hybrid pill badge)
            var hdrPanel = new Panel
            {
                Location = new Point(padOuter, Px(10)),
                Size = new Size(innerW, Px(28)),
                BackColor = Color.Transparent
            };
            UITheme.EnableDoubleBuffering(hdrPanel);

            string hdrTitle = isAr
                ? $"شرائح الذاكرة الفعلية  ({currentReport.PopulatedSlots} / {Math.Max(currentReport.PopulatedSlots, currentReport.TotalPhysicalSlots)} مستخدمة)"
                : $"PHYSICAL MEMORY SLOTS  ({currentReport.PopulatedSlots} OF {Math.Max(currentReport.PopulatedSlots, currentReport.TotalPhysicalSlots)} POPULATED)";
            string archText = isAr ? currentReport.MemoryArchitectureSummaryAr : currentReport.MemoryArchitectureSummary;
            if (isAr && !string.IsNullOrEmpty(archText))
            {
                var archParts = archText.Split('•', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (archParts.Length > 1)
                {
                    archText = $"{archParts[0]}  •  \u200E{string.Join(" • ", archParts.Skip(1))}\u200E";
                }
            }

            hdrPanel.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                UITheme.DrawBidiText(g, hdrTitle, UITheme.FontSmall, new Rectangle(0, 0, hdrPanel.Width, hdrPanel.Height), UITheme.TextSecondary, isAr, false);

                if (!string.IsNullOrWhiteSpace(archText))
                {
                    var textSize = TextRenderer.MeasureText(archText, UITheme.FontSmall, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    int pillW = Math.Min(hdrPanel.Width / 2 + Px(90), textSize.Width + Px(24));
                    int pillH = Px(22);
                    int pillX = isAr ? 0 : hdrPanel.Width - pillW;
                    int pillY = (hdrPanel.Height - pillH) / 2;
                    var pillRect = new Rectangle(pillX, pillY, pillW, pillH);

                    bool hasLp = currentReport.RamSlots.Any(x => x.IsLpddr);
                    Color badgeAccent = currentReport.IsMixedMemory
                        ? Color.FromArgb(245, 158, 11)
                        : (hasLp ? Color.FromArgb(168, 85, 247) : UITheme.Primary);
                    Color pillBg = isDark
                        ? Color.FromArgb(35, badgeAccent.R, badgeAccent.G, badgeAccent.B)
                        : Color.FromArgb(22, badgeAccent.R, badgeAccent.G, badgeAccent.B);
                    Color pillFg = currentReport.IsMixedMemory
                        ? (isDark ? Color.FromArgb(251, 191, 36) : Color.FromArgb(180, 83, 9))
                        : (hasLp ? (isDark ? Color.FromArgb(216, 180, 254) : Color.FromArgb(107, 33, 168))
                                 : (isDark ? Color.FromArgb(96, 165, 250) : Color.FromArgb(29, 78, 216)));

                    using var pillPath = UITheme.GetRoundedPath(pillRect, Math.Max(6, pillH / 2));
                    using var pillBrush = new SolidBrush(pillBg);
                    g.FillPath(pillBrush, pillPath);
                    using var pillPen = new Pen(Color.FromArgb(90, badgeAccent.R, badgeAccent.G, badgeAccent.B), 1);
                    g.DrawPath(pillPen, pillPath);

                    UITheme.DrawBidiText(g, archText, UITheme.FontSmall, pillRect, pillFg, isAr, true);
                }
            };
            pnlBottomMetrics.Controls.Add(hdrPanel);

            int cardsY = Px(44);
            int cardH = Px(124);
            for (int idx = 0; idx < currentReport.RamSlots.Count; idx++)
            {
                var slot = currentReport.RamSlots[idx];
                int drawIdx = isAr ? (currentReport.RamSlots.Count - 1 - idx) : idx;
                int cardX = padOuter + drawIdx * (cardW + gap);

                var pnlSlot = new Panel
                {
                    Location = new Point(cardX, cardsY),
                    Size = new Size(cardW, cardH),
                    BackColor = UITheme.CardSubBg
                };
                UITheme.EnableDoubleBuffering(pnlSlot);

                Color accentColor = !slot.IsPopulated
                    ? (isDark ? Color.FromArgb(100, 116, 139) : Color.FromArgb(148, 163, 184))
                    : (slot.IsLpddr ? Color.FromArgb(168, 85, 247)
                        : (slot.MemoryGeneration.StartsWith("DDR5", StringComparison.OrdinalIgnoreCase)
                            ? Color.FromArgb(14, 165, 233)
                            : UITheme.Primary));

                pnlSlot.Paint += (s, e) =>
                {
                    var g = e.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    int w = pnlSlot.Width;
                    int h = pnlSlot.Height;
                    var outerRect = new Rectangle(0, 0, w - 1, h - 1);
                    using var cardPath = UITheme.GetRoundedPath(outerRect, Px(8));
                    using (var cardBrush = new SolidBrush(UITheme.CardSubBg))
                    {
                        g.FillPath(cardBrush, cardPath);
                    }
                    using (var borderPen = new Pen(UITheme.BorderSubtle, 1))
                    {
                        g.DrawPath(borderPen, cardPath);
                    }

                    // Leading 4px vertical accent bar
                    int barW = Px(4);
                    int barPadY = Px(10);
                    using (var accentBrush = new SolidBrush(accentColor))
                    {
                        int barX = isAr ? w - barW : 0;
                        g.FillRectangle(accentBrush, barX, barPadY, barW, h - (barPadY * 2));
                    }

                    int padL = isAr ? Px(12) : Px(14);
                    int padR = isAr ? Px(14) : Px(12);

                    // 1. DDR / LPDDR Type Badge Pill (Top Far Edge)
                    string genBadgeText = slot.IsPopulated
                        ? slot.MemoryGeneration.ToUpperInvariant()
                        : (isAr ? "فارغ" : "EMPTY");
                    var badgeSz = TextRenderer.MeasureText(genBadgeText, UITheme.FontSmall, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    int badgeW = Math.Max(Px(52), badgeSz.Width + Px(16));
                    int badgeH = Px(21);
                    int badgeY = Px(10);
                    int badgeX = isAr ? padL : (w - padR - badgeW);
                    var badgeRect = new Rectangle(badgeX, badgeY, badgeW, badgeH);
                    using (var badgePath = UITheme.GetRoundedPath(badgeRect, Math.Max(6, badgeH / 2)))
                    using (var badgeBrush = new SolidBrush(slot.IsPopulated ? accentColor : (isDark ? Color.FromArgb(51, 65, 85) : Color.FromArgb(203, 213, 225))))
                    {
                        g.FillPath(badgeBrush, badgePath);
                    }
                    Color badgeTextCol = slot.IsPopulated ? Color.White : (isDark ? Color.FromArgb(203, 213, 225) : Color.FromArgb(51, 65, 85));
                    UITheme.DrawBidiText(g, genBadgeText, UITheme.FontSmall, badgeRect, badgeTextCol, false, true);

                    // 2. Slot Locator Title (Top Near Edge)
                    string slotTitle = isAr
                        ? $"المنفذ {slot.SlotIndex}  •  \u200E{slot.Locator}\u200E"
                        : $"Slot {slot.SlotIndex}  •  {slot.Locator}";
                    int titleX = isAr ? (badgeRect.Right + Px(8)) : padL;
                    int titleW = Math.Max(Px(40), (isAr ? (w - padR - titleX) : (badgeRect.Left - Px(8) - titleX)));
                    UITheme.DrawBidiText(g, slotTitle, UITheme.FontBody, new Rectangle(titleX, Px(9), titleW, Px(23)), UITheme.TextPrimary, isAr, false);

                    // 3. Capacity + Speed Hero Line
                    string heroText = slot.IsPopulated
                        ? $"{slot.CapacityGB:N1} GB  •  {slot.SpeedDisplay}"
                        : (isAr ? "منفذ شاغر — قابل لتركيب شريحة إضافية" : "Empty Socket — Ready for Memory Upgrade");
                    var heroRect = new Rectangle(padL, Px(37), Math.Max(Px(40), w - padL - padR), Px(22));
                    Color heroCol = slot.IsPopulated ? UITheme.TextPrimary : UITheme.TextSecondary;
                    if (slot.IsPopulated)
                    {
                        TextFormatFlags ltrFlushFlags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | (isAr ? TextFormatFlags.Right : TextFormatFlags.Left);
                        TextRenderer.DrawText(g, heroText, UITheme.FontTitle, heroRect, heroCol, ltrFlushFlags);
                    }
                    else
                    {
                        UITheme.DrawBidiText(g, heroText, UITheme.FontBody, heroRect, heroCol, isAr, false);
                    }

                    // 4. Form Factor (Onboard vs SO-DIMM) + Voltage + Manufacturer / Part
                    string formLocalized = slot.IsSoldered
                        ? (isAr ? "مدمجة على اللوحة (Soldered)" : "Onboard (Soldered)")
                        : slot.FormFactorName;
                    string specSub = slot.IsPopulated
                        ? (isAr && slot.IsSoldered ? $"{formLocalized}  •  \u200E{(slot.ConfiguredVoltageMv > 0 ? slot.VoltageDisplay : slot.DataWidthBits + "-bit")}" : $"{formLocalized}  •  {(slot.ConfiguredVoltageMv > 0 ? slot.VoltageDisplay : slot.DataWidthBits + "-bit")}")
                        : formLocalized;
                    Color specSubCol = slot.IsPopulated ? (isDark ? Color.FromArgb(148, 163, 184) : Color.FromArgb(51, 65, 85)) : UITheme.TextSecondary;
                    var specSubRect = new Rectangle(padL, Px(62), Math.Max(Px(40), w - padL - padR), Px(19));
                    if (isAr && !slot.IsSoldered)
                    {
                        TextRenderer.DrawText(g, specSub, UITheme.FontSub, specSubRect, specSubCol,
                            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.Right);
                    }
                    else
                    {
                        UITheme.DrawBidiText(g, specSub, UITheme.FontSub, specSubRect, specSubCol, isAr, false);
                    }

                    string vendorPart = slot.IsPopulated
                        ? string.Join("  •  ", new[] { slot.Manufacturer, slot.PartNumber, slot.BankLabel }.Where(x => !string.IsNullOrWhiteSpace(x)))
                        : (string.IsNullOrWhiteSpace(slot.BankLabel) ? "-" : slot.BankLabel);
                    var vendorRect = new Rectangle(padL, Px(82), Math.Max(Px(40), w - padL - padR), Px(19));
                    TextRenderer.DrawText(g, vendorPart, UITheme.FontSub, vendorRect, UITheme.TextSecondary,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | (isAr ? TextFormatFlags.Right : TextFormatFlags.Left));

                    // 5. Sleek Bottom Capacity Share Progress Track
                    int trackX = padL;
                    int trackW = Math.Max(Px(40), w - padL - padR);
                    int trackH = Px(5);
                    int trackY = h - Px(14);
                    var trackRect = new Rectangle(trackX, trackY, trackW, trackH);
                    Color trackBg = isDark ? Color.FromArgb(30, 41, 59) : Color.FromArgb(226, 232, 240);
                    int trackR = Math.Max(2, trackH / 2);
                    using (var trackPath = UITheme.GetRoundedPath(trackRect, trackR))
                    using (var trackBrush = new SolidBrush(trackBg))
                    {
                        g.FillPath(trackBrush, trackPath);
                    }

                    if (slot.IsPopulated && totalInstalledGB > 0)
                    {
                        double ratio = Math.Clamp(slot.CapacityGB / totalInstalledGB, 0.12, 1.0);
                        int fillW = Math.Max(Px(8), (int)Math.Round(trackW * ratio));
                        int fillX = isAr ? (trackX + trackW - fillW) : trackX;
                        var fillRect = new Rectangle(fillX, trackY, fillW, trackH);
                        using var fillPath = UITheme.GetRoundedPath(fillRect, trackR);
                        using var fillBrush = new SolidBrush(accentColor);
                        g.FillPath(fillBrush, fillPath);
                    }
                };

                pnlBottomMetrics.Controls.Add(pnlSlot);
            }
        }

        private void UpdateLiveMetrics()
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;

            try
            {
                currentCpuPercent = HardwareInfoService.GetLiveCpuUsage();
                currentCpuMhz = HardwareInfoService.GetLiveCpuClockMhz(out int bMhz, out _);
                if (bMhz > 0) baseCpuMhz = bMhz;

                // Update rolling history queue
                if (cpuHistory.Count >= MaxHistorySamples) cpuHistory.Dequeue();
                cpuHistory.Enqueue(currentCpuPercent);

                if (pnlCpuMeter != null && pnlCpuMeter.Visible) pnlCpuMeter.Invalidate();
                if (pnlCpuChart != null && pnlCpuChart.Visible) pnlCpuChart.Invalidate();
            }
            catch { }
        }

        private void PromptExport()
        {
            bool isAr = LocalizationService.IsArabic;
            if (currentReport == null)
            {
                CleanRamBoosterApp.WpfUI.AppDialog.Show(this,
                    isAr ? "لا تزال تفاصيل العتاد قيد الفحص والتحميل، يرجى الانتظار لحظة." : "Hardware configuration is still being loaded. Please wait a moment.",
                    isAr ? "التقرير غير جاهز" : "Report Not Ready",
                    CleanRamBoosterApp.WpfUI.AppDialogTone.Info);
                return;
            }

            using var sfd = new SaveFileDialog
            {
                Title = isAr ? "تصدير تقرير معلومات النظام" : "Export System Information Report",
                Filter = isAr ? "تقرير HTML (*.html)|*.html|تقرير نصي (*.txt)|*.txt" : "HTML Report (*.html)|*.html|Text Report (*.txt)|*.txt",
                FileName = $"System Information Report - {Environment.MachineName}.html"
            };

            if (sfd.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    if (sfd.FilterIndex == 1 || sfd.FileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                    {
                        HardwareInfoService.ExportToHtml(currentReport, sfd.FileName);
                    }
                    else
                    {
                        HardwareInfoService.ExportToText(currentReport, sfd.FileName);
                    }

                    if (CleanRamBoosterApp.WpfUI.AppDialog.Confirm(this,
                        isAr ? $"تم تصدير التقرير بنجاح إلى:\n{sfd.FileName}\n\nهل تريد فتحه الآن؟" : $"Report successfully exported to:\n{sfd.FileName}\n\nWould you like to open it now?",
                        isAr ? "تم التصدير بنجاح" : "Export Succeeded",
                        CleanRamBoosterApp.WpfUI.AppDialogTone.Success))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = sfd.FileName,
                            UseShellExecute = true
                        });
                    }
                }
                catch (Exception ex)
                {
                    CleanRamBoosterApp.WpfUI.AppDialog.Show(this,
                        isAr ? $"فشل تصدير التقرير: {ex.Message}" : $"Failed to export report: {ex.Message}",
                        isAr ? "خطأ في التصدير" : "Export Error",
                        CleanRamBoosterApp.WpfUI.AppDialogTone.Error);
                }
            }
        }

        private void CopyCurrentCategoryToClipboard()
        {
            if (currentReport == null || currentCategoryIndex >= currentReport.Categories.Count) return;

            var cat = currentReport.Categories[currentCategoryIndex];
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[ {cat.Title.ToUpperInvariant()} ]");
            sb.AppendLine(new string('-', 50));
            foreach (var grp in cat.Groups)
            {
                sb.AppendLine($"{grp.GroupName}:");
                foreach (var item in grp.Items)
                {
                    sb.AppendLine($"  {item.Name,-28} : {item.Value}");
                }
                sb.AppendLine();
            }

            Clipboard.SetText(sb.ToString());
            bool isAr = LocalizationService.IsArabic;
            CleanRamBoosterApp.WpfUI.AppDialog.Show(this,
                isAr ? $"تم نسخ مواصفات '{cat.Title}' إلى الحافظة." : $"Specs for '{cat.Title}' copied to clipboard.",
                isAr ? "تم النسخ" : "Copied",
                CleanRamBoosterApp.WpfUI.AppDialogTone.Success);
        }

        private void ApplyTheme()
        {
            BackColor = UITheme.BgContent;
            pnlHeader.BackColor = UITheme.SidebarBg;
            pnlSidebar.BackColor = UITheme.SidebarBg;
            pnlFooter.BackColor = UITheme.SidebarBottomBg;
            pnlContentContainer.BackColor = UITheme.BgContent;
            if (cardSpecs != null)
            {
                cardSpecs.BackColor = UITheme.CardBg;
                cardSpecs.BorderColor = UITheme.BorderSubtle;
            }
            lvSpecs.BackColor = UITheme.CardBg;
            lvSpecs.ForeColor = UITheme.TextPrimary;
            pnlBottomMetrics.BackColor = UITheme.CardBg;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            MinimumSize = new Size(UITheme.ScalePx(880, this), UITheme.ScalePx(580, this));
            AdjustSpecsColumns();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            AdjustSpecsColumns();
            if (currentReport != null && currentCategoryIndex >= 0 && currentCategoryIndex < currentReport.Categories.Count &&
                currentReport.Categories[currentCategoryIndex].CategoryId == "Memory")
            {
                RenderMemorySlotsBottomBar();
            }
        }
    }
}
