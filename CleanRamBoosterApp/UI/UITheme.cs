using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CleanRamBoosterApp.Services;

namespace CleanRamBoosterApp.UI
{
    public static class UITheme
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("uxtheme.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string? pszSubIdList);

        [DllImport("uxtheme.dll", EntryPoint = "#135")]
        private static extern int SetPreferredAppMode(int appMode);

        [DllImport("uxtheme.dll", EntryPoint = "#136")]
        private static extern void FlushMenuThemes();

        public static void ApplyPreferredDarkAppMode(bool isDark)
        {
            try
            {
                // 2 = AllowDark, 0 = Default (Light) on Windows 10 1903+ / Windows 11
                SetPreferredAppMode(isDark ? 2 : 0);
                FlushMenuThemes();
            }
            catch { }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref RECT lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT lpPaint);

        [DllImport("user32.dll")]
        private static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT lpPaint);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hwnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        private const int LVM_GETHEADER = 0x101F;
        private const int HDM_GETITEMRECT = 0x1207;
        private const int WM_PAINT = 0x000F;
        private const int WM_ERASEBKGND = 0x0014;
        private const int WM_NCPAINT = 0x0085;
        private const int WM_PRINT = 0x0317;
        private const int WM_PRINTCLIENT = 0x0318;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PAINTSTRUCT
        {
            public IntPtr hdc;
            public bool fErase;
            public RECT rcPaint;
            public bool fRestore;
            public bool fIncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] rgbReserved;
        }

        private sealed class ListViewHeaderPainter : NativeWindow
        {
            private readonly ListView lv;

            [DllImport("user32.dll")]
            private static extern int GetScrollPos(IntPtr hWnd, int nBar);

            [DllImport("user32.dll")]
            private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

            [DllImport("gdi32.dll")]
            private static extern uint GetLayout(IntPtr hdc);

            [DllImport("gdi32.dll")]
            private static extern uint SetLayout(IntPtr hdc, uint dwLayout);

            public ListViewHeaderPainter(ListView listView)
            {
                lv = listView;
            }

            public void Attach()
            {
                if (Handle != IntPtr.Zero) ReleaseHandle();
                if (!lv.IsHandleCreated) return;
                IntPtr hHeader = SendMessage(lv.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
                if (hHeader != IntPtr.Zero)
                {
                    try { SetWindowTheme(hHeader, "", ""); } catch { }
                    AssignHandle(hHeader);
                }
            }

            public void EnsureAttachedAndInvalidate()
            {
                if (Handle == IntPtr.Zero && lv.IsHandleCreated)
                {
                    Attach();
                }
                if (Handle != IntPtr.Zero)
                {
                    InvalidateRect(Handle, IntPtr.Zero, false);
                }
            }

            private void PaintHeaderSurface(Graphics g, int w, int h)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                using var bgBrush = new SolidBrush(CardSubBg);
                g.FillRectangle(bgBrush, 0, 0, w, h);

                using var borderPen = new Pen(BorderSubtle, 1);
                bool isRtlText = lv.RightToLeft == RightToLeft.Yes;
                bool isRtlLayout = isRtlText && lv.RightToLeftLayout;

                var cols = new System.Collections.Generic.List<ColumnHeader>(lv.Columns.Count);
                foreach (ColumnHeader col in lv.Columns)
                {
                    cols.Add(col);
                }
                cols.Sort((a, b) => a.DisplayIndex.CompareTo(b.DisplayIndex));

                int scrollX = GetScrollPos(lv.Handle, 0);
                int x = isRtlLayout ? (w + scrollX) : (-scrollX);

                for (int i = 0; i < cols.Count; i++)
                {
                    int colW = cols[i].Width;
                    int left = isRtlLayout ? (x - colW) : x;
                    int right = left + colW;

                    if (colW > 0)
                    {
                        if (i < cols.Count - 1)
                        {
                            int sepX = isRtlLayout ? left : (right - 1);
                            g.DrawLine(borderPen, sepX, 5, sepX, Math.Max(6, h - 6));
                        }

                        var textRect = new Rectangle(left + 10, 0, Math.Max(0, colW - 20), h);
                        DrawBidiText(g, cols[i].Text ?? "", FontBody, textRect, TextSecondary, isRtlText, center: false, backColor: CardSubBg);
                    }

                    x = isRtlLayout ? left : right;
                }

                g.DrawLine(borderPen, 0, h - 1, w, h - 1);
            }

            private void PaintToHdc(IntPtr hdc)
            {
                if (hdc == IntPtr.Zero) return;
                GetClientRect(Handle, out RECT rc);
                int w = rc.Right - rc.Left;
                int h = rc.Bottom - rc.Top;
                if (w <= 0 || h <= 0) return;

                uint prevLayout = GetLayout(hdc);
                if ((prevLayout & 1) != 0) SetLayout(hdc, 0);
                try
                {
                    using var buffered = BufferedGraphicsManager.Current.Allocate(hdc, new Rectangle(0, 0, w, h));
                    PaintHeaderSurface(buffered.Graphics, w, h);
                    buffered.Render(hdc);
                }
                catch
                {
                    using var g = Graphics.FromHdc(hdc);
                    PaintHeaderSurface(g, w, h);
                }
                finally
                {
                    if ((prevLayout & 1) != 0) SetLayout(hdc, prevLayout);
                }
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_NCPAINT)
                {
                    m.Result = IntPtr.Zero;
                    return;
                }

                if (m.Msg == WM_ERASEBKGND)
                {
                    m.Result = (IntPtr)1;
                    return;
                }

                if (m.Msg == WM_PAINT)
                {
                    IntPtr hdc = BeginPaint(Handle, out PAINTSTRUCT ps);
                    try
                    {
                        PaintToHdc(hdc);
                    }
                    finally
                    {
                        EndPaint(Handle, ref ps);
                    }
                    return;
                }

                if (m.Msg == WM_PRINT || m.Msg == WM_PRINTCLIENT)
                {
                    PaintToHdc(m.WParam);
                    m.Result = IntPtr.Zero;
                    return;
                }

                base.WndProc(ref m);
            }
        }

        private sealed class ListViewSurfacePainter : NativeWindow
        {
            private readonly ListView lv;

            [DllImport("gdi32.dll")]
            private static extern uint GetLayout(IntPtr hdc);

            [DllImport("gdi32.dll")]
            private static extern uint SetLayout(IntPtr hdc, uint dwLayout);

            public ListViewSurfacePainter(ListView listView)
            {
                lv = listView;
            }

            public void Attach()
            {
                if (Handle != IntPtr.Zero) ReleaseHandle();
                if (lv.IsHandleCreated) AssignHandle(lv.Handle);
            }

            private void PaintEmptySurfaceToHdc(IntPtr hdc, bool fullClient)
            {
                if (hdc == IntPtr.Zero || !lv.IsHandleCreated) return;
                GetClientRect(Handle, out RECT rc);
                int w = rc.Right - rc.Left;
                int h = rc.Bottom - rc.Top;
                if (w <= 0 || h <= 0) return;

                int headerH = 26;
                IntPtr hHeader = SendMessage(Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
                if (hHeader != IntPtr.Zero && GetClientRect(hHeader, out RECT hRc))
                {
                    int measuredH = hRc.Bottom - hRc.Top;
                    if (measuredH > 0) headerH = measuredH;
                }

                int clearTop = fullClient ? 0 : headerH;
                if (!fullClient && lv.Items.Count > 0)
                {
                    int lastBottom = headerH;
                    for (int i = lv.Items.Count - 1; i >= 0; i--)
                    {
                        Rectangle b = lv.Items[i].Bounds;
                        if (b.Bottom > headerH && b.Top < h)
                        {
                            lastBottom = b.Bottom;
                            break;
                        }
                        if (b.Top >= h) continue;
                    }
                    clearTop = Math.Max(headerH, lastBottom);
                }

                uint prevLayout = GetLayout(hdc);
                if ((prevLayout & 1) != 0) SetLayout(hdc, 0);
                try
                {
                    using var g = Graphics.FromHdc(hdc);
                    using var bgBrush = new SolidBrush(CardBg);
                    if (clearTop < h)
                    {
                        g.FillRectangle(bgBrush, 0, clearTop, w, h - clearTop);
                    }
                }
                finally
                {
                    if ((prevLayout & 1) != 0) SetLayout(hdc, prevLayout);
                }
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_ERASEBKGND)
                {
                    if (m.WParam != IntPtr.Zero)
                    {
                        PaintEmptySurfaceToHdc(m.WParam, fullClient: true);
                    }
                    m.Result = (IntPtr)1;
                    return;
                }

                base.WndProc(ref m);

                if (m.Msg == WM_PAINT)
                {
                    IntPtr hdc = GetDC(Handle);
                    if (hdc != IntPtr.Zero)
                    {
                        try
                        {
                            PaintEmptySurfaceToHdc(hdc, fullClient: false);
                        }
                        finally
                        {
                            ReleaseDC(Handle, hdc);
                        }
                    }
                }
                else if ((m.Msg == WM_PRINTCLIENT || m.Msg == WM_PRINT) && m.WParam != IntPtr.Zero)
                {
                    PaintEmptySurfaceToHdc(m.WParam, fullClient: false);
                }
            }
        }

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public static void ApplyDwmDarkMode(IntPtr hWnd, bool isDark)
        {
            ApplyPreferredDarkAppMode(isDark);
            if (hWnd == IntPtr.Zero) return;
            try
            {
                int useDarkMode = isDark ? 1 : 0;
                if (DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDarkMode, sizeof(int));
                }
            }
            catch { }
        }

        public static void ApplyControlTheme(Control control, bool isDark)
        {
            if (control == null || !control.IsHandleCreated) return;
            ApplyPreferredDarkAppMode(isDark);
            try
            {
                if (control is TreeView || control is ListView || control is TextBoxBase)
                {
                    SetWindowTheme(control.Handle, isDark ? "DarkMode_Explorer" : "Explorer", null);
                }
                else if (control is ComboBox)
                {
                    SetWindowTheme(control.Handle, isDark ? "DarkMode_CFD" : "Explorer", null);
                }
            }
            catch { }
        }

        public static bool IsDarkMode
        {
            get => SettingsService.Instance.Current.IsDarkMode;
            set
            {
                SettingsService.Instance.Update(s => s.IsDarkMode = value);
                ThemeChanged?.Invoke();
            }
        }

        public static event Action? ThemeChanged;

        public static Icon? AppIcon { get; set; }
        public static Image? AppIconBitmap { get; set; }
        private static bool _iconLoaded = false;

        public static void EnsureAppIconLoaded()
        {
            if (_iconLoaded) return;
            _iconLoaded = true;
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] possibleIco = {
                    Path.Combine(baseDir, "app.ico"),
                    Path.Combine(baseDir, "Resources", "app.ico")
                };
                foreach (var p in possibleIco)
                {
                    if (File.Exists(p))
                    {
                        AppIcon = new Icon(p);
                        break;
                    }
                }
                if (AppIcon == null)
                {
                    AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                }

                string[] possiblePng = {
                    Path.Combine(baseDir, "app_icon.png"),
                    Path.Combine(baseDir, "Resources", "app_icon.png")
                };
                foreach (var p in possiblePng)
                {
                    if (File.Exists(p))
                    {
                        using var fs = new FileStream(p, FileMode.Open, FileAccess.Read);
                        AppIconBitmap = Image.FromStream(fs);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Services.DiagnosticLogger.LogWarning("UITheme", "Failed to load application icons", ex);
            }
        }

        // -------------------------------------------------------------
        // Dynamic Color Palette (Supports Dark & Light Modes)
        // -------------------------------------------------------------
        public static Color SidebarBg => IsDarkMode ? Color.FromArgb(15, 23, 42) : Color.FromArgb(241, 245, 249);
        public static Color SidebarNavActiveBg => IsDarkMode ? Color.FromArgb(30, 41, 59) : Color.FromArgb(219, 234, 254);
        public static Color SidebarNavActiveFg => IsDarkMode ? AccentCyan : Primary;
        public static Color SidebarNavFg => IsDarkMode ? TextMuted : Color.FromArgb(71, 85, 105);
        public static Color SidebarBottomBg => IsDarkMode ? Color.FromArgb(20, 28, 48) : Color.FromArgb(226, 232, 240);
        public static Color SidebarPillBg => IsDarkMode ? Color.FromArgb(15, 23, 42) : Color.White;

        public static Color BgDark => SidebarBg;          // Alias for sidebar
        public static Color BgDarkNavActive => SidebarNavActiveBg;    // Alias for active nav tab
        public static Color BgDarkNavHover => IsDarkMode ? Color.FromArgb(24, 33, 54) : Color.FromArgb(235, 240, 246);

        public static Color BgContent => IsDarkMode ? Color.FromArgb(13, 17, 23) : Color.FromArgb(248, 250, 252);
        public static Color CardBg => IsDarkMode ? Color.FromArgb(22, 27, 34) : Color.White;
        public static Color CardSubBg => IsDarkMode ? Color.FromArgb(13, 17, 23) : Color.FromArgb(241, 245, 249);
        public static Color BorderSubtle => IsDarkMode ? Color.FromArgb(48, 54, 61) : Color.FromArgb(226, 232, 240);
        public static Color BorderFocus => Color.FromArgb(59, 130, 246);   // #3B82F6 - Highlight Border
        public static Color BorderDark => Color.FromArgb(51, 65, 85);

        public static Color TextPrimary => IsDarkMode ? Color.FromArgb(240, 246, 252) : Color.FromArgb(15, 23, 42);
        public static Color TextSecondary => IsDarkMode ? Color.FromArgb(139, 148, 158) : Color.FromArgb(100, 116, 139);
        public static Color TextMuted => IsDarkMode ? Color.FromArgb(110, 118, 129) : Color.FromArgb(148, 163, 184);
        public static Color TextLight => Color.FromArgb(248, 250, 252);

        public static Color ButtonSecondaryBg => IsDarkMode ? Color.FromArgb(33, 38, 45) : Color.FromArgb(241, 245, 249);
        public static Color ButtonSecondaryFg => IsDarkMode ? Color.FromArgb(201, 209, 217) : Color.FromArgb(30, 41, 59);
        public static Color ButtonSecondaryHover => IsDarkMode ? Color.FromArgb(48, 54, 61) : Color.FromArgb(226, 232, 240);

        public static Color InputBg => IsDarkMode ? Color.FromArgb(13, 17, 23) : Color.White;
        public static Color InputFg => IsDarkMode ? Color.FromArgb(240, 246, 252) : Color.FromArgb(15, 23, 42);
        public static Color InputBorder => IsDarkMode ? Color.FromArgb(48, 54, 61) : Color.FromArgb(203, 213, 225);

        public static readonly Color Primary = Color.FromArgb(37, 99, 235);         // Blue 600
        public static readonly Color PrimaryHover = Color.FromArgb(29, 78, 216);    // Blue 700
        public static Color PrimaryLight => IsDarkMode ? Color.FromArgb(26, 45, 77) : Color.FromArgb(239, 246, 255);

        public static readonly Color Success = Color.FromArgb(16, 185, 129);        // Emerald 500
        public static readonly Color SuccessHover = Color.FromArgb(5, 150, 105);
        public static Color SuccessLight => IsDarkMode ? Color.FromArgb(19, 42, 33) : Color.FromArgb(236, 253, 245);

        public static readonly Color Warning = Color.FromArgb(245, 158, 11);        // Amber 500
        public static readonly Color WarningHover = Color.FromArgb(217, 119, 6);
        public static Color WarningLight => IsDarkMode ? Color.FromArgb(51, 38, 18) : Color.FromArgb(254, 243, 199);

        public static readonly Color Danger = Color.FromArgb(239, 68, 68);          // Red 500
        public static readonly Color DangerHover = Color.FromArgb(220, 38, 38);
        public static Color DangerLight => IsDarkMode ? Color.FromArgb(48, 20, 20) : Color.FromArgb(254, 242, 242);

        public static readonly Color AccentCyan = Color.FromArgb(56, 189, 248);     // Sky 400
        public static readonly Color Purple = Color.FromArgb(139, 92, 246);         // Violet 500
        public static Color PurpleLight => IsDarkMode ? Color.FromArgb(45, 30, 70) : Color.FromArgb(245, 243, 255);

        // -------------------------------------------------------------
        // Typography (Upscaled clean Regular weights; avoid heavy over-bolding)
        // -------------------------------------------------------------
        public static Font FontHeader => LocalizationService.GetFont(15.5F, FontStyle.Regular);
        public static Font FontTitle => LocalizationService.GetFont(12F, FontStyle.Regular);
        public static Font FontSub => LocalizationService.GetFont(9.5F, FontStyle.Regular);
        public static Font FontBody => LocalizationService.GetFont(10.25F, FontStyle.Regular);
        public static Font FontBodyBold => LocalizationService.GetFont(10.5F, FontStyle.Regular);
        public static Font FontButton => LocalizationService.GetFont(10F, FontStyle.Regular);
        public static Font FontSmall => LocalizationService.GetFont(9.25F, FontStyle.Regular);
        public static Font FontSmallBold => LocalizationService.GetFont(9.25F, FontStyle.Regular);
        public static Font FontMetricVal => LocalizationService.GetFont(15F, FontStyle.Regular);
        public static Font FontMono => new Font("Consolas", 9.5F, FontStyle.Regular);

        public static float GetDpiScale(Control? c) => (c != null && c.DeviceDpi > 0) ? (c.DeviceDpi / 96f) : 1f;
        public static int ScalePx(int px, Control? c) => Math.Max(1, (int)Math.Round(px * GetDpiScale(c)));

        public static Bitmap? GetScaledAppIcon(int logicalSize, Control? host)
        {
            if (AppIconBitmap == null) return null;
            int dpi = (host != null && host.DeviceDpi > 0) ? host.DeviceDpi : 96;
            int px = Math.Max(16, (int)Math.Round(logicalSize * (dpi / 96f)));
            var bmp = new Bitmap(px, px, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            bmp.SetResolution(dpi, dpi);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.DrawImage(AppIconBitmap, new Rectangle(0, 0, px, px));
            return bmp;
        }

        public static void SetButtonIcon(Button btn, string iconKey, Color? customColor = null, int logicalSize = 15)
        {
            void UpdateIcon()
            {
                int dpi = btn.DeviceDpi > 0 ? btn.DeviceDpi : 96;
                int px = Math.Max(12, (int)Math.Round(logicalSize * (dpi / 96f)));
                Color fg = customColor ?? (btn.Enabled ? btn.ForeColor : TextMuted);
                var old = btn.Image;
                btn.Image = VectorIcons.RenderBitmap(iconKey, px, fg, dpi, 1.8f);
                old?.Dispose();
            }

            btn.TextImageRelation = TextImageRelation.ImageBeforeText;
            btn.ImageAlign = ContentAlignment.MiddleCenter;
            btn.TextAlign = ContentAlignment.MiddleCenter;
            btn.Padding = new Padding(4, 0, 4, 0);
            UpdateIcon();
            btn.HandleCreated += (s, e) => UpdateIcon();
            btn.DpiChangedAfterParent += (s, e) => UpdateIcon();
            btn.EnabledChanged += (s, e) => UpdateIcon();
        }

        // -------------------------------------------------------------
        // Helper Methods
        // -------------------------------------------------------------
        public static void EnableDoubleBuffering(Control control)
        {
            try
            {
                var prop = typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
                prop?.SetValue(control, true, null);
            }
            catch { }
        }

        public static void StyleButton(Button btn, Color bg, Color fg, Color? hoverBg = null)
        {
            Color hBg = hoverBg ?? ControlPaint.Light(bg);
            ApplyFlatButtonChrome(
                btn,
                borderSize: 0,
                getBg: () => bg,
                getFg: () => fg,
                getHoverBg: () => hBg,
                getBorder: () => bg,
                getDisabledBg: () => IsDarkMode ? Color.FromArgb(38, 46, 58) : Color.FromArgb(203, 213, 225),
                getDisabledFg: () => IsDarkMode ? Color.FromArgb(148, 163, 184) : Color.FromArgb(100, 116, 139));
        }

        public static void StyleSecondaryButton(Button btn)
        {
            btn.Tag = "Secondary";
            ApplyFlatButtonChrome(
                btn,
                borderSize: 1,
                getBg: () => ButtonSecondaryBg,
                getFg: () => ButtonSecondaryFg,
                getHoverBg: () => ButtonSecondaryHover,
                getBorder: () => BorderSubtle,
                getDisabledBg: () => IsDarkMode ? Color.FromArgb(26, 32, 40) : Color.FromArgb(241, 245, 249),
                getDisabledFg: () => IsDarkMode ? Color.FromArgb(139, 148, 158) : Color.FromArgb(148, 163, 184));
        }

        private static void ApplyFlatButtonChrome(
            Button btn,
            int borderSize,
            Func<Color> getBg,
            Func<Color> getFg,
            Func<Color> getHoverBg,
            Func<Color> getBorder,
            Func<Color> getDisabledBg,
            Func<Color> getDisabledFg)
        {
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = borderSize;
            btn.FlatAppearance.BorderColor = getBorder();
            btn.UseMnemonic = false;
            btn.Cursor = btn.Enabled ? Cursors.Hand : Cursors.Default;
            btn.Font = FontButton;

            void SyncState()
            {
                btn.BackColor = btn.Enabled ? getBg() : getDisabledBg();
                btn.ForeColor = btn.Enabled ? getFg() : getDisabledFg();
                btn.FlatAppearance.BorderColor = getBorder();
                btn.Cursor = btn.Enabled ? Cursors.Hand : Cursors.Default;
                btn.Invalidate();
            }

            SyncState();
            btn.MouseEnter += (s, e) => { if (btn.Enabled) btn.BackColor = getHoverBg(); };
            btn.MouseLeave += (s, e) => { if (btn.Enabled) btn.BackColor = getBg(); };
            btn.EnabledChanged += (s, e) => SyncState();
            btn.Paint += (s, e) =>
            {
                if (btn.Enabled) return;
                Color dBg = getDisabledBg();
                Color dFg = getDisabledFg();
                using var bgBrush = new SolidBrush(dBg);
                e.Graphics.FillRectangle(bgBrush, btn.ClientRectangle);
                if (borderSize > 0)
                {
                    using var borderPen = new Pen(getBorder(), borderSize);
                    e.Graphics.DrawRectangle(borderPen, 0, 0, btn.Width - 1, btn.Height - 1);
                }
                TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
                if (btn.RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft;

                if (btn.Image != null)
                {
                    int dpi = btn.DeviceDpi > 0 ? btn.DeviceDpi : 96;
                    int imgW = (int)Math.Round(15 * (dpi / 96f));
                    int gap = (int)Math.Round(6 * (dpi / 96f));
                    Size textSz = TextRenderer.MeasureText(e.Graphics, btn.Text ?? "", btn.Font, btn.ClientRectangle.Size, flags);
                    int totalW = imgW + gap + textSz.Width;
                    int startX = Math.Max(4, (btn.Width - totalW) / 2);
                    int imgY = (btn.Height - imgW) / 2;
                    bool rtl = btn.RightToLeft == RightToLeft.Yes;
                    int imgX = rtl ? (startX + textSz.Width + gap) : startX;
                    int txtX = rtl ? startX : (startX + imgW + gap);
                    e.Graphics.DrawImage(btn.Image, new Rectangle(imgX, imgY, imgW, imgW));
                    TextRenderer.DrawText(e.Graphics, btn.Text ?? "", btn.Font, new Rectangle(txtX, 0, textSz.Width + 4, btn.Height), dFg, flags & ~TextFormatFlags.HorizontalCenter);
                }
                else
                {
                    TextRenderer.DrawText(e.Graphics, btn.Text ?? "", btn.Font, btn.ClientRectangle, dFg, flags);
                }
            };
        }

        public static string StripLeadingIcon(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            string s = text.TrimStart();
            // Remove leading emoji / non-letter symbol tokens separated by space
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsLetterOrDigit(c) || c == '%' || c == '(' || c == '[' || c == '"' || c == '\'') break;
                i++;
            }
            return i > 0 ? s.Substring(i).TrimStart() : s;
        }

        public static void StyleToolHeader(Form form, Label lblHeader, string iconKey, Color accent, bool isAr)
        {
            lblHeader.Text = StripLeadingIcon(lblHeader.Text);
            lblHeader.Font = FontHeader;
            lblHeader.AutoSize = false;
            lblHeader.Height = 36;
            lblHeader.TextAlign = isAr ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft;

            int badgeSize = 36;
            int marginX = 18;
            int topY = 12;

            var badge = new Panel
            {
                Size = new Size(badgeSize, badgeSize),
                BackColor = Color.Transparent,
                Location = isAr ? new Point(form.ClientSize.Width - marginX - badgeSize, topY) : new Point(marginX, topY)
            };
            EnableDoubleBuffering(badge);
            badge.Paint += (s, e) =>
            {
                VectorIcons.PaintHeaderBadge(e.Graphics, iconKey, new Rectangle(0, 0, badge.Width - 1, badge.Height - 1), accent, IsDarkMode);
            };
            form.Controls.Add(badge);

            int textGap = 12;
            if (isAr)
            {
                lblHeader.Location = new Point(marginX, topY);
                lblHeader.Width = Math.Max(160, badge.Left - textGap - marginX);
                lblHeader.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                badge.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            }
            else
            {
                lblHeader.Location = new Point(badge.Right + textGap, topY);
                lblHeader.Width = Math.Max(160, form.ClientSize.Width - lblHeader.Left - marginX);
                lblHeader.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                badge.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            }
        }

        public static bool ContainsArabic(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char ch in text)
            {
                if ((ch >= '\u0600' && ch <= '\u06FF') || (ch >= '\u0750' && ch <= '\u077F') || (ch >= '\uFB50' && ch <= '\uFDFF') || (ch >= '\uFE70' && ch <= '\uFEFF'))
                {
                    return true;
                }
            }
            return false;
        }

        public static void DrawBidiText(Graphics g, string text, Font font, Rectangle rect, Color color, bool isRtl, bool center = false, Color? backColor = null)
        {
            if (rect.Width <= 1 || rect.Height <= 1) return;
            bool hasArabic = ContainsArabic(text);
            var flags = TextFormatFlags.NoPrefix
                      | TextFormatFlags.VerticalCenter
                      | TextFormatFlags.EndEllipsis
                      | TextFormatFlags.SingleLine
                      | TextFormatFlags.PreserveGraphicsClipping
                      | TextFormatFlags.PreserveGraphicsTranslateTransform;

            if (center)
            {
                flags |= TextFormatFlags.HorizontalCenter;
                if (isRtl && hasArabic) flags |= TextFormatFlags.RightToLeft;
            }
            else if (isRtl && !hasArabic)
            {
                flags |= TextFormatFlags.Right;
            }
            else if (isRtl && hasArabic)
            {
                flags |= TextFormatFlags.RightToLeft | TextFormatFlags.Right;
            }
            else
            {
                flags |= TextFormatFlags.Left;
            }

            if (backColor.HasValue)
            {
                TextRenderer.DrawText(g, text ?? "", font, rect, color, backColor.Value, flags);
            }
            else
            {
                TextRenderer.DrawText(g, text ?? "", font, rect, color, flags);
            }
        }

        private static StringFormat CreateBidiStringFormat(string? text, bool isRtl)
        {
            bool hasArabic = ContainsArabic(text);

            if (isRtl && !hasArabic)
            {
                return new StringFormat
                {
                    Alignment = StringAlignment.Far,
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.NoWrap
                };
            }

            return new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap | (isRtl ? StringFormatFlags.DirectionRightToLeft : 0)
            };
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ListView, ListViewHeaderPainter> _headerPainters = new();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ListView, ListViewSurfacePainter> _surfacePainters = new();

        public static void StyleListView(ListView lv)
        {
            lv.BackColor = CardBg;
            lv.ForeColor = TextPrimary;
            lv.BorderStyle = BorderStyle.None;
            lv.FullRowSelect = true;
            lv.GridLines = false; // NEVER harsh white gridlines! Matches Startup Manager Table Design
            lv.View = View.Details;
            EnableDoubleBuffering(lv);

            // 1. Enforce crisp row height scaled cleanly for current monitor DPI
            void SyncRowHeight()
            {
                int rowH = ScalePx(33, lv);
                if (lv.SmallImageList == null || lv.SmallImageList.ImageSize.Height != rowH)
                {
                    lv.SmallImageList = new ImageList { ImageSize = new Size(1, rowH) };
                }
            }
            SyncRowHeight();
            lv.DpiChangedAfterParent += (s, e) => SyncRowHeight();

            // 2. Owner Drawing
            lv.OwnerDraw = true;

            if (_headerPainters.TryGetValue(lv, out var existingPainter))
            {
                ApplyControlTheme(lv, IsDarkMode);
                existingPainter.EnsureAttachedAndInvalidate();
                if (_surfacePainters.TryGetValue(lv, out var existingSurface) && existingSurface.Handle == IntPtr.Zero && lv.IsHandleCreated)
                {
                    existingSurface.Attach();
                }
                lv.Invalidate(true);
                return;
            }

            // Attach native SysHeader32 + ListView surface painters so empty rows and Light headers stay seamless
            var headerPainter = new ListViewHeaderPainter(lv);
            var surfacePainter = new ListViewSurfacePainter(lv);
            _headerPainters.Add(lv, headerPainter);
            _surfacePainters.Add(lv, surfacePainter);
            lv.HandleCreated += (s, e) =>
            {
                ApplyControlTheme(lv, IsDarkMode);
                surfacePainter.Attach();
                headerPainter.Attach();
            };
            lv.HandleDestroyed += (s, e) =>
            {
                if (headerPainter.Handle != IntPtr.Zero) headerPainter.ReleaseHandle();
                if (surfacePainter.Handle != IntPtr.Zero) surfacePainter.ReleaseHandle();
            };
            lv.Layout += (s, e) => headerPainter.EnsureAttachedAndInvalidate();
            if (lv.IsHandleCreated)
            {
                ApplyControlTheme(lv, IsDarkMode);
                surfacePainter.Attach();
                headerPainter.Attach();
            }

            // Fallback Column Header drawing if WM_PRINT triggers DrawColumnHeader directly
            lv.DrawColumnHeader += (s, e) =>
            {
                e.DrawDefault = false;
                if (e.Bounds.Width <= 0 || e.Bounds.Height <= 0) return;

                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                using var hdrBg = new SolidBrush(CardSubBg);
                g.FillRectangle(hdrBg, e.Bounds);

                using var borderPen = new Pen(BorderSubtle, 1);
                g.DrawLine(borderPen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                if (e.ColumnIndex < lv.Columns.Count - 1)
                {
                    int sepX = lv.RightToLeft == RightToLeft.Yes ? e.Bounds.Left : (e.Bounds.Right - 1);
                    g.DrawLine(borderPen, sepX, e.Bounds.Top + 5, sepX, Math.Max(e.Bounds.Top + 6, e.Bounds.Bottom - 6));
                }

                var textRect = new Rectangle(e.Bounds.X + 10, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 20), e.Bounds.Height);
                DrawBidiText(g, e.Header?.Text ?? "", FontBody, textRect, TextSecondary, lv.RightToLeft == RightToLeft.Yes, center: false, backColor: CardSubBg);
            };

            // Draw Item
            lv.DrawItem += (s, e) => e.DrawDefault = false;

            // Draw SubItem matching Startup Manager rows (alternating, soft dividers, modern checkboxes)
            lv.DrawSubItem += (s, e) =>
            {
                e.DrawDefault = false;
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                // Use SelectedIndices in VirtualMode and e.Item.Selected in normal mode; e.ItemState in DrawSubItem is unreliable on .NET WinForms ListView
                bool isSelected = lv.VirtualMode
                    ? (e.ItemIndex >= 0 && lv.SelectedIndices.Contains(e.ItemIndex))
                    : (e.Item != null && e.Item.Selected);
                bool isGroupHdr = e.Item?.Tag?.ToString() == "GROUP_HEADER";

                if (isGroupHdr)
                {
                    // Full-row section banner spanning seamlessly across columns without wrapping/clipping
                    Color groupBg = IsDarkMode ? Color.FromArgb(24, 34, 52) : Color.FromArgb(224, 242, 254);
                    Color groupFg = IsDarkMode ? Color.FromArgb(56, 189, 248) : Color.FromArgb(3, 105, 161);
                    using var grpBrush = new SolidBrush(groupBg);
                    g.FillRectangle(grpBrush, e.Bounds);

                    bool isRtl = lv.RightToLeft == RightToLeft.Yes;
                    int fullLeft = e.Item?.Bounds.Left ?? e.Bounds.Left;
                    int fullWidth = e.Item?.Bounds.Width ?? e.Bounds.Width;

                    // Crisp 3px accent bar on leading edge of the section row
                    if ((isRtl && e.Bounds.Right >= fullLeft + fullWidth - 4) || (!isRtl && e.Bounds.Left <= fullLeft + 4))
                    {
                        using var accentBrush = new SolidBrush(groupFg);
                        int barX = isRtl ? e.Bounds.Right - 4 : fullLeft;
                        g.FillRectangle(accentBrush, barX, e.Bounds.Y + 4, 3, Math.Max(4, e.Bounds.Height - 8));
                    }

                    // Draw full-row non-wrapping banner text clipped to current subitem bounds so it spans Col 0 + Col 1 smoothly
                    var oldClip = g.Clip;
                    try
                    {
                        g.SetClip(e.Bounds);
                        string bannerText = e.Item?.Text ?? e.SubItem?.Text ?? "";
                        var grpTextRect = new Rectangle(fullLeft + 12, e.Bounds.Y, Math.Max(40, fullWidth - 24), e.Bounds.Height);
                        DrawBidiText(g, bannerText, FontTitle, grpTextRect, groupFg, isRtl);
                    }
                    finally
                    {
                        g.Clip = oldClip;
                    }

                    using var borderPen = new Pen(IsDarkMode ? Color.FromArgb(40, 56, 82) : Color.FromArgb(186, 230, 253), 1);
                    g.DrawLine(borderPen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                    return;
                }

                // Row background: Selected vs Alternating rows (even: CardBg, odd: CardSubBg)
                Color rowBg;
                if (isSelected)
                {
                    rowBg = IsDarkMode ? Color.FromArgb(31, 41, 55) : Color.FromArgb(219, 234, 254);
                }
                else
                {
                    rowBg = (e.ItemIndex % 2 == 0) ? CardBg : CardSubBg;
                }

                using var rowBrush = new SolidBrush(rowBg);
                g.FillRectangle(rowBrush, e.Bounds);

                // Subtle 1px horizontal bottom divider
                using var penDivider = new Pen(BorderSubtle, 1);
                g.DrawLine(penDivider, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

                int textOffsetLeft = 10;
                int textOffsetRight = 10;

                // Modern Rounded Checkbox rendering if enabled and column 0
                if (lv.CheckBoxes && e.ColumnIndex == 0)
                {
                    float scale = GetDpiScale(lv);
                    int boxSize = Math.Max(14, (int)Math.Round(16 * scale));
                    int boxY = e.Bounds.Y + (e.Bounds.Height - boxSize) / 2;
                    int boxX;

                    if (lv.RightToLeft == RightToLeft.Yes)
                    {
                        boxX = e.Bounds.Right - ScalePx(10, lv) - boxSize;
                        textOffsetRight += boxSize + ScalePx(10, lv);
                    }
                    else
                    {
                        boxX = e.Bounds.X + ScalePx(10, lv);
                        textOffsetLeft += boxSize + ScalePx(10, lv);
                    }

                    var boxRect = new Rectangle(boxX, boxY, boxSize, boxSize);
                    int rad = Math.Max(3, boxSize / 4);
                    using var boxPath = GetRoundedPath(boxRect, rad);

                    if (e.Item?.Checked == true)
                    {
                        using var chkBg = new SolidBrush(Primary);
                        g.FillPath(chkBg, boxPath);
                        using var chkPen = new Pen(Primary, 1f);
                        g.DrawPath(chkPen, boxPath);

                        using var checkPen = new Pen(Color.White, Math.Max(1.6f, 1.85f * scale))
                        {
                            StartCap = LineCap.Round,
                            EndCap = LineCap.Round,
                            LineJoin = LineJoin.Round
                        };
                        g.DrawLines(checkPen, new[]
                        {
                            new PointF(boxX + boxSize * 0.24f, boxY + boxSize * 0.52f),
                            new PointF(boxX + boxSize * 0.43f, boxY + boxSize * 0.72f),
                            new PointF(boxX + boxSize * 0.78f, boxY + boxSize * 0.30f)
                        });
                    }
                    else
                    {
                        using var chkBg = new SolidBrush(InputBg);
                        g.FillPath(chkBg, boxPath);
                        using var chkBorderPen = new Pen(IsDarkMode ? Color.FromArgb(88, 106, 134) : BorderSubtle, 1.2f);
                        g.DrawPath(chkBorderPen, boxPath);
                    }
                }

                // Text color & Font (always crisp Regular weight, no heavy bolding)
                Color textCol;
                if (isSelected)
                {
                    textCol = IsDarkMode ? Color.White : Primary;
                }
                else if (e.SubItem != null && e.SubItem.ForeColor != Color.Empty && e.SubItem.ForeColor != Color.Black && e.SubItem.ForeColor != SystemColors.WindowText)
                {
                    textCol = e.SubItem.ForeColor;
                }
                else
                {
                    textCol = TextPrimary;
                }

                string itemText = e.SubItem?.Text ?? "";
                Font rowFont = e.SubItem?.Font ?? FontBody;
                var textRect = new Rectangle(e.Bounds.X + textOffsetLeft, e.Bounds.Y, Math.Max(0, e.Bounds.Width - textOffsetLeft - textOffsetRight), e.Bounds.Height);
                DrawBidiText(g, itemText, rowFont, textRect, textCol, lv.RightToLeft == RightToLeft.Yes);
            };

            // Checkbox click hit-testing
            lv.MouseDown += (s, e) =>
            {
                if (!lv.CheckBoxes) return;
                var hit = lv.HitTest(e.Location);
                if (hit.Item != null)
                {
                    bool inCheckZone;
                    if (lv.RightToLeft == RightToLeft.Yes)
                    {
                        inCheckZone = e.X >= hit.Item.Bounds.Right - 34 && e.X <= hit.Item.Bounds.Right;
                    }
                    else
                    {
                        inCheckZone = e.X >= hit.Item.Bounds.Left && e.X <= hit.Item.Bounds.Left + 34;
                    }

                    if (inCheckZone)
                    {
                        hit.Item.Checked = !hit.Item.Checked;
                        lv.Invalidate(hit.Item.Bounds);
                    }
                }
            };
        }

        private sealed class ComboBoxChromePainter : NativeWindow
        {
            private readonly ComboBox cb;
            private bool isHovered;

            public ComboBoxChromePainter(ComboBox comboBox)
            {
                cb = comboBox;
                cb.MouseEnter += (s, e) => { isHovered = true; cb.Invalidate(); };
                cb.MouseLeave += (s, e) => { isHovered = false; cb.Invalidate(); };
                cb.DropDown += (s, e) => cb.Invalidate();
                cb.DropDownClosed += (s, e) => cb.Invalidate();
            }

            public void Attach()
            {
                if (Handle != IntPtr.Zero) ReleaseHandle();
                if (cb.IsHandleCreated) AssignHandle(cb.Handle);
            }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (m.Msg == WM_PAINT)
                {
                    DrawCustomChrome();
                }
            }

            private void DrawCustomChrome()
            {
                if (!cb.IsHandleCreated || cb.Width <= 4 || cb.Height <= 4) return;
                using var g = Graphics.FromHwnd(cb.Handle);
                g.SmoothingMode = SmoothingMode.AntiAlias;

                bool isRtl = cb.RightToLeft == RightToLeft.Yes;
                int btnW = SystemInformation.VerticalScrollBarWidth + 4;
                Rectangle btnRect = isRtl
                    ? new Rectangle(1, 1, btnW, cb.Height - 2)
                    : new Rectangle(cb.Width - btnW - 1, 1, btnW, cb.Height - 2);

                using (var bgBrush = new SolidBrush(InputBg))
                {
                    g.FillRectangle(bgBrush, btnRect);
                }

                // Crisp vector chevron aligned with WPF ComboBox
                int cx = btnRect.X + btnRect.Width / 2;
                int cy = btnRect.Y + btnRect.Height / 2;
                Color chevronCol = cb.DroppedDown ? Primary : (isHovered ? InputFg : TextSecondary);
                using (var pen = new Pen(chevronCol, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    g.DrawLines(pen, new PointF[]
                    {
                        new PointF(cx - 4f, cy - 2f),
                        new PointF(cx, cy + 2f),
                        new PointF(cx + 4f, cy - 2f)
                    });
                }

                Color borderCol = cb.DroppedDown || cb.Focused ? Primary : (isHovered ? Primary : BorderSubtle);
                using var borderPen = new Pen(borderCol, 1f);
                g.DrawRectangle(borderPen, 0, 0, cb.Width - 1, cb.Height - 1);
            }
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ComboBox, ComboBoxChromePainter> _comboPainters = new();

        public static void StyleComboBox(ComboBox cb)
        {
            cb.BackColor = InputBg;
            cb.ForeColor = InputFg;
            cb.Font = FontBody;
            cb.DropDownStyle = ComboBoxStyle.DropDownList;
            cb.DrawMode = DrawMode.OwnerDrawFixed;
            cb.ItemHeight = ScalePx(26, cb);
            cb.FlatStyle = FlatStyle.Flat;
            cb.DpiChangedAfterParent += (s, e) => cb.ItemHeight = ScalePx(26, cb);

            if (_comboPainters.TryGetValue(cb, out var existingComboPainter))
            {
                ApplyControlTheme(cb, IsDarkMode);
                if (existingComboPainter.Handle == IntPtr.Zero && cb.IsHandleCreated) existingComboPainter.Attach();
                cb.Invalidate();
                return;
            }

            var painter = new ComboBoxChromePainter(cb);
            _comboPainters.Add(cb, painter);
            cb.HandleCreated += (s, e) =>
            {
                ApplyControlTheme(cb, IsDarkMode);
                painter.Attach();
            };
            cb.HandleDestroyed += (s, e) =>
            {
                if (painter.Handle != IntPtr.Zero) painter.ReleaseHandle();
            };
            if (cb.IsHandleCreated)
            {
                ApplyControlTheme(cb, IsDarkMode);
                painter.Attach();
            }

            cb.DrawItem += (s, e) =>
            {
                if (e.Index < 0) return;
                bool isEditBox = (e.State & DrawItemState.ComboBoxEdit) == DrawItemState.ComboBoxEdit;
                bool selected = !isEditBox && ((e.State & DrawItemState.Selected) == DrawItemState.Selected);
                Color rowBg = selected ? Primary : InputBg;
                Color rowFg = selected ? Color.White : InputFg;

                using var bgBrush = new SolidBrush(rowBg);
                e.Graphics.FillRectangle(bgBrush, e.Bounds);

                string itemText = cb.Items[e.Index]?.ToString() ?? "";
                var textRect = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, Math.Max(10, e.Bounds.Width - 16), e.Bounds.Height);
                DrawBidiText(e.Graphics, itemText, FontBody, textRect, rowFg, cb.RightToLeft == RightToLeft.Yes);
            };
        }

        public static void StyleRadioButton(RadioButton rb)
        {
            rb.Font = FontBody;
            rb.ForeColor = TextPrimary;
            rb.UseMnemonic = false;
            rb.Cursor = Cursors.Hand;
            rb.FlatStyle = FlatStyle.Flat;

            rb.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                Color bg = rb.Parent?.BackColor ?? CardBg;
                if (bg == Color.Transparent) bg = CardBg;
                g.Clear(bg);

                float scale = GetDpiScale(rb);
                int circleSize = Math.Max(14, (int)Math.Round(16 * scale));
                bool isRtl = rb.RightToLeft == RightToLeft.Yes;
                int cy = Math.Max(0, (rb.Height - circleSize) / 2);
                int cx = isRtl ? Math.Max(2, rb.Width - circleSize - 2) : 2;
                var circleRect = new RectangleF(cx, cy, circleSize, circleSize);

                if (rb.Checked)
                {
                    using var fillBrush = new SolidBrush(Primary);
                    g.FillEllipse(fillBrush, circleRect);
                    float inner = circleSize * 0.42f;
                    var dotRect = new RectangleF(
                        cx + (circleSize - inner) / 2f,
                        cy + (circleSize - inner) / 2f,
                        inner,
                        inner);
                    using var dotBrush = new SolidBrush(Color.White);
                    g.FillEllipse(dotBrush, dotRect);
                }
                else
                {
                    using var fillBrush = new SolidBrush(InputBg);
                    g.FillEllipse(fillBrush, circleRect);
                    using var ringPen = new Pen(IsDarkMode ? Color.FromArgb(100, 116, 139) : Color.FromArgb(148, 163, 184), 1.4f * scale);
                    g.DrawEllipse(ringPen, circleRect);
                }

                int gap = (int)Math.Round(7 * scale);
                Rectangle textRect = isRtl
                    ? new Rectangle(0, 0, Math.Max(10, cx - gap), rb.Height)
                    : new Rectangle(cx + circleSize + gap, 0, Math.Max(10, rb.Width - (cx + circleSize + gap)), rb.Height);
                DrawBidiText(g, rb.Text ?? "", rb.Font, textRect, rb.Enabled ? TextPrimary : TextMuted, isRtl);
            };
        }

        private sealed class NumericSpinnerPainter : NativeWindow
        {
            private readonly Control spinner;
            [DllImport("user32.dll")]
            private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
            [StructLayout(LayoutKind.Sequential)]
            private struct RECT { public int Left, Top, Right, Bottom; }

            public NumericSpinnerPainter(Control ctrl) => spinner = ctrl;
            public void Attach() { if (Handle != IntPtr.Zero) ReleaseHandle(); if (spinner.IsHandleCreated) AssignHandle(spinner.Handle); }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x0014) { m.Result = (IntPtr)1; return; }
                base.WndProc(ref m);
                if (m.Msg == 0x000F)
                {
                    if (!GetClientRect(Handle, out RECT rc)) return;
                    int w = rc.Right - rc.Left;
                    int h = rc.Bottom - rc.Top;
                    if (w <= 4 || h <= 4) return;
                    using var g = Graphics.FromHwnd(Handle);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    using var bgBrush = new SolidBrush(CardSubBg);
                    g.FillRectangle(bgBrush, 0, 0, w, h);
                    using var divPen = new Pen(BorderSubtle, 1);
                    g.DrawLine(divPen, 0, 0, 0, h);
                    int mid = h / 2;
                    g.DrawLine(divPen, 0, mid, w, mid);
                    using var arrowPen = new Pen(TextSecondary, Math.Max(1.4f, w * 0.09f))
                    {
                        StartCap = LineCap.Round,
                        EndCap = LineCap.Round,
                        LineJoin = LineJoin.Round
                    };
                    float cx = w / 2f;
                    float arm = Math.Max(2.5f, w * 0.22f);
                    float upY = mid * 0.52f;
                    float dnY = mid + (h - mid) * 0.48f;
                    g.DrawLines(arrowPen, new[] { new PointF(cx - arm, upY + arm * 0.55f), new PointF(cx, upY - arm * 0.45f), new PointF(cx + arm, upY + arm * 0.55f) });
                    g.DrawLines(arrowPen, new[] { new PointF(cx - arm, dnY - arm * 0.55f), new PointF(cx, dnY + arm * 0.45f), new PointF(cx + arm, dnY - arm * 0.55f) });
                }
            }
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, NumericSpinnerPainter> _nudPainters = new();

        public static void StyleNumericUpDown(NumericUpDown nud)
        {
            nud.BackColor = InputBg;
            nud.ForeColor = InputFg;
            nud.BorderStyle = BorderStyle.FixedSingle;
            nud.TextAlign = HorizontalAlignment.Center;

            void ApplyNudTheme()
            {
                ApplyControlTheme(nud, IsDarkMode);
                foreach (Control child in nud.Controls)
                {
                    child.BackColor = InputBg;
                    child.ForeColor = InputFg;
                    ApplyControlTheme(child, IsDarkMode);
                    if (child is not TextBox && !_nudPainters.TryGetValue(child, out _))
                    {
                        var spPainter = new NumericSpinnerPainter(child);
                        _nudPainters.Add(child, spPainter);
                        if (child.IsHandleCreated) spPainter.Attach();
                        child.HandleCreated += (_, _) => spPainter.Attach();
                        child.HandleDestroyed += (_, _) => { if (spPainter.Handle != IntPtr.Zero) spPainter.ReleaseHandle(); };
                    }
                }
            }

            if (nud.IsHandleCreated) ApplyNudTheme();
            nud.HandleCreated += (s, e) => ApplyNudTheme();
        }

        public static void StyleDateTimePicker(DateTimePicker dtp)
        {
            dtp.BackColor = InputBg;
            dtp.ForeColor = InputFg;
            dtp.CalendarForeColor = InputFg;
            dtp.CalendarMonthBackground = InputBg;
            dtp.CalendarTitleBackColor = CardSubBg;
            dtp.CalendarTitleForeColor = TextPrimary;
            dtp.CalendarTrailingForeColor = TextMuted;

            if (dtp.IsHandleCreated) ApplyControlTheme(dtp, IsDarkMode);
            dtp.HandleCreated += (s, e) => ApplyControlTheme(dtp, IsDarkMode);
        }

        public static void StyleLinkLabel(LinkLabel lnk)
        {
            lnk.LinkColor = IsDarkMode ? AccentCyan : Primary;
            lnk.ActiveLinkColor = IsDarkMode ? Color.White : PrimaryHover;
            lnk.VisitedLinkColor = IsDarkMode ? AccentCyan : Primary;
            lnk.LinkBehavior = LinkBehavior.HoverUnderline;
        }

        public static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius) => GetRoundedPath(bounds, radius);

        public static GraphicsPath GetRoundedPath(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int diameter = radius * 2;
            if (bounds.Width < diameter || bounds.Height < diameter)
            {
                path.AddRectangle(bounds);
                return path;
            }
            Rectangle arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static (Color Bg, Color Fg, Color Border) GetBadgeTagColors(string tag, bool isDark)
        {
            return tag.ToUpperInvariant() switch
            {
                "REPAIR" or "إصلاح" => isDark
                    ? (Color.FromArgb(45, 26, 14), Color.FromArgb(251, 191, 36), Color.FromArgb(120, 53, 15))   // Warm Amber / Gold
                    : (Color.FromArgb(254, 243, 199), Color.FromArgb(180, 83, 9), Color.FromArgb(253, 230, 138)), // Pastel Amber / Rich Amber Text
                "SYSTEM" or "نظام" => isDark
                    ? (Color.FromArgb(19, 35, 58), Color.FromArgb(56, 189, 248), Color.FromArgb(30, 58, 95))     // Deep Slate / Vibrant Sky
                    : (Color.FromArgb(224, 242, 254), Color.FromArgb(3, 105, 161), Color.FromArgb(186, 230, 253)), // Sky 100 / Deep Ocean Text
                "STORAGE" or "تخزين" => isDark
                    ? (Color.FromArgb(16, 42, 28), Color.FromArgb(52, 211, 153), Color.FromArgb(20, 83, 45))    // Deep Forest / Luminous Mint
                    : (Color.FromArgb(220, 252, 231), Color.FromArgb(21, 128, 61), Color.FromArgb(187, 247, 208)), // Mint 100 / Deep Forest Text
                "WINDOWS" or "WINDOW" or "ويندوز" => isDark
                    ? (Color.FromArgb(30, 27, 75), Color.FromArgb(165, 180, 252), Color.FromArgb(49, 46, 129))  // Deep Indigo / Radiant Indigo
                    : (Color.FromArgb(238, 242, 255), Color.FromArgb(67, 56, 202), Color.FromArgb(199, 210, 254)), // Indigo 50 / Royal Indigo Text
                _ => isDark
                    ? (Color.FromArgb(26, 45, 77), Color.FromArgb(96, 165, 250), Color.FromArgb(37, 99, 235))
                    : (Color.FromArgb(239, 246, 255), Color.FromArgb(29, 78, 216), Color.FromArgb(191, 219, 254))
            };
        }

        public static DoubleBufferedCardPanel CreateCardPanel(int x, int y, int width, int height)
        {
            return new DoubleBufferedCardPanel
            {
                Location = new Point(x, y),
                Size = new Size(width, height),
                BackColor = CardBg,
                Tag = "Card"
            };
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "\u200E0 MB";
            double mb = bytes / (1024.0 * 1024.0);
            if (mb <= 1023.0) return $"\u200E{mb:N1} MB";
            double gb = mb / 1024.0;
            return $"\u200E{gb:N2} GB";
        }

        public static string FormatRam(long bytes)
        {
            if (bytes <= 0) return "\u200E0 MB";
            double mb = bytes / (1024.0 * 1024.0);
            if (mb <= 1023.0) return $"\u200E{mb:N1} MB";
            double gb = mb / 1024.0;
            return $"\u200E{gb:N2} GB";
        }
    }
}
