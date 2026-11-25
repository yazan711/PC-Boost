using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CleanRamBoosterApp.Services;
using CleanRamBoosterApp.UI;

namespace CleanRamBoosterApp.Forms
{
    public class ToolboxEmptyFoldersForm : Form
    {
        private const int LVM_FIRST = 0x1000;
        private const int LVM_SETITEMSTATE = LVM_FIRST + 43;
        private const uint LVIF_STATE = 0x0008;
        private const uint LVIS_SELECTED = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        private struct LVITEM_STATE
        {
            public uint mask;
            public int iItem;
            public int iSubItem;
            public uint state;
            public uint stateMask;
            public IntPtr pszText;
            public int cchTextMax;
            public int iImage;
            public IntPtr lParam;
            public int iIndent;
            public int iGroupId;
            public uint cColumns;
            public IntPtr puColumns;
            public IntPtr piColFmt;
            public int iGroup;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref LVITEM_STATE lParam);

        private readonly DiskToolsService diskTools = new();
        private readonly TextBox txtFolder;
        private readonly Button btnBrowse;
        private readonly Button btnScan;
        private readonly Button btnStop;
        private readonly Button btnClean;
        private readonly ListView lvFolders;
        private readonly ProgressBar pbScan;
        private readonly Label lblStatus;
        private readonly List<string> foundEmptyFolders = new();
        private string lastScanRoot = string.Empty;
        private CancellationTokenSource? activeCts;
        private bool isBusy;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
                return cp;
            }
        }

        public ToolboxEmptyFoldersForm()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;

            bool isAr = LocalizationService.IsArabic;
            Text = isAr ? "فاحص المجلدات الفارغة - PC Boost" : "Empty Folder Scanner - Clean Abandoned Directories";
            ClientSize = new Size(744, 526);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            Font = UITheme.FontBody;
            BackColor = UITheme.BgContent;
            RightToLeft = isAr ? RightToLeft.Yes : RightToLeft.No;
            if (UITheme.AppIcon != null) Icon = UITheme.AppIcon;
            UITheme.ApplyDwmDarkMode(Handle, UITheme.IsDarkMode);

            var lblHeader = new Label
            {
                Text = isAr ? "فاحص ومزيل المجلدات الفارغة المهجورة" : "Empty Folder Scanner",
                Font = UITheme.FontHeader,
                ForeColor = UITheme.TextPrimary
            };

            var pnlTop = UITheme.CreateCardPanel(18, 56, 708, 86);

            var lblDir = new Label { Text = isAr ? "المجلد:" : "Directory:", AutoSize = true, Font = UITheme.FontBody, ForeColor = UITheme.TextPrimary };
            txtFolder = new TextBox
            {
                Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                BackColor = UITheme.InputBg,
                ForeColor = UITheme.InputFg,
                Font = UITheme.FontBody,
                RightToLeft = RightToLeft.No,
                BorderStyle = BorderStyle.FixedSingle
            };

            btnBrowse = new Button { Text = isAr ? "استعراض..." : "Browse...", Size = new Size(102, 30) };
            UITheme.StyleSecondaryButton(btnBrowse);
            btnBrowse.Click += (s, e) =>
            {
                using var fbd = new FolderBrowserDialog();
                if (fbd.ShowDialog(this) == DialogResult.OK)
                    txtFolder.Text = fbd.SelectedPath;
            };

            btnScan = new Button
            {
                Text = isAr ? "فحص المجلدات" : "Scan Folders",
                Size = new Size(154, 32),
                UseMnemonic = false
            };
            UITheme.StyleButton(btnScan, UITheme.Primary, Color.White, UITheme.PrimaryHover);
            UITheme.SetButtonIcon(btnScan, "SearchFiles", Color.White, 15);
            btnScan.Click += async (s, e) => await ScanEmptyFoldersAsync();

            btnStop = new Button
            {
                Text = isAr ? "إيقاف" : "Stop",
                Size = new Size(94, 32),
                Enabled = false,
                UseMnemonic = false
            };
            UITheme.StyleSecondaryButton(btnStop);
            UITheme.SetButtonIcon(btnStop, "Stop", UITheme.TextPrimary, 14);
            btnStop.Click += (s, e) => activeCts?.Cancel();

            btnClean = new Button
            {
                Text = isAr ? "حذف المجلدات الفارغة" : "Delete All Empty",
                Size = new Size(205, 32),
                UseMnemonic = false
            };
            UITheme.StyleButton(btnClean, UITheme.Danger, Color.White, UITheme.DangerHover);
            UITheme.SetButtonIcon(btnClean, "Trash", Color.White, 15);
            btnClean.Click += async (s, e) => await CleanFoldersAsync();

            if (isAr)
            {
                int lblDirW = TextRenderer.MeasureText(lblDir.Text, lblDir.Font).Width;
                lblDir.Location = new Point(708 - 14 - lblDirW, 13);
                btnBrowse.Location = new Point(14, 8);
                txtFolder.Location = new Point(btnBrowse.Right + 8, 10);
                txtFolder.Width = lblDir.Left - 8 - txtFolder.Left;

                btnScan.Location = new Point(708 - 14 - btnScan.Width, 45);
                btnStop.Location = new Point(btnScan.Left - 8 - btnStop.Width, 45);
                btnClean.Location = new Point(btnStop.Left - 10 - btnClean.Width, 45);
            }
            else
            {
                int lblDirW = TextRenderer.MeasureText(lblDir.Text, lblDir.Font).Width;
                lblDir.Location = new Point(14, 13);
                btnBrowse.Location = new Point(708 - 14 - btnBrowse.Width, 8);
                txtFolder.Location = new Point(14 + lblDirW + 8, 10);
                txtFolder.Width = btnBrowse.Left - 8 - txtFolder.Left;

                btnScan.Location = new Point(14, 45);
                btnStop.Location = new Point(btnScan.Right + 8, 45);
                btnClean.Location = new Point(btnStop.Right + 10, 45);
            }

            pnlTop.Controls.Add(lblDir);
            pnlTop.Controls.Add(txtFolder);
            pnlTop.Controls.Add(btnBrowse);
            pnlTop.Controls.Add(btnScan);
            pnlTop.Controls.Add(btnStop);
            pnlTop.Controls.Add(btnClean);

            var pnlList = UITheme.CreateCardPanel(18, 150, 708, 314);

            lvFolders = new ListView
            {
                Location = new Point(10, 10),
                Size = new Size(688, 294),
                View = View.Details,
                VirtualMode = true,
                VirtualListSize = 0,
                FullRowSelect = true,
                GridLines = false,
                BorderStyle = BorderStyle.None,
                Font = UITheme.FontBody,
                BackColor = UITheme.CardBg,
                ForeColor = UITheme.TextPrimary,
                RightToLeft = isAr ? RightToLeft.Yes : RightToLeft.No,
                MultiSelect = true
            };
            lvFolders.RetrieveVirtualItem += (s, e) =>
            {
                if (e.ItemIndex >= 0 && e.ItemIndex < foundEmptyFolders.Count)
                {
                    string path = foundEmptyFolders[e.ItemIndex];
                    var lvi = new ListViewItem(Path.GetFileName(path));
                    lvi.SubItems.Add(path);
                    e.Item = lvi;
                }
                else
                {
                    var empty = new ListViewItem(string.Empty);
                    empty.SubItems.Add(string.Empty);
                    e.Item = empty;
                }
            };
            UITheme.StyleListView(lvFolders);
            lvFolders.Columns.Add(isAr ? "اسم المجلد" : "Folder Name", 215);
            lvFolders.Columns.Add(isAr ? "المسار الكامل" : "Full Directory Path", 460);

            lvFolders.DoubleClick += (s, e) => OpenSelectedFolder();

            // Context Menu & Multi-Choose Shortcuts
            var ctxFolders = new ContextMenuStrip();
            var miOpen = new ToolStripMenuItem(isAr ? "فتح المجلد في المستكشف (Enter)" : "Open Folder in Explorer (Enter)", null, (s, e) => OpenSelectedFolder());
            var miCopy = new ToolStripMenuItem(isAr ? "نسخ المسار الكامل (Ctrl+C)" : "Copy Path (Ctrl+C)", null, (s, e) => CopySelectedFolderPath());
            var miSelectAll = new ToolStripMenuItem(isAr ? "تحديد الكل (Ctrl+A)" : "Select All Rows (Ctrl+A)", null, (s, e) => SelectAllVirtualListView(lvFolders));
            var miSep = new ToolStripSeparator();
            var miDelete = new ToolStripMenuItem(isAr ? "حذف المجلدات المحددة (Delete)" : "Delete Selected Folder(s) (Delete)", null, async (s, e) => await DeleteSelectedFoldersAsync()) { ForeColor = UITheme.Danger };
            ctxFolders.Items.AddRange(new ToolStripItem[] { miOpen, miCopy, miSelectAll, miSep, miDelete });
            lvFolders.ContextMenuStrip = ctxFolders;

            lvFolders.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.A)
                {
                    SelectAllVirtualListView(lvFolders);
                    e.Handled = true;
                }
                else if (e.Control && e.KeyCode == Keys.C)
                {
                    CopySelectedFolderPath();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Delete)
                {
                    _ = DeleteSelectedFoldersAsync();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Enter)
                {
                    OpenSelectedFolder();
                    e.Handled = true;
                }
            };

            pnlList.Controls.Add(lvFolders);

            pbScan = new ProgressBar
            {
                Location = new Point(18, 472),
                Size = new Size(584, 8),
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 28,
                Visible = false
            };

            lblStatus = new Label
            {
                Text = isAr ? "حدد المجلد المطلوب واضغط على 'فحص المجلدات' للبحث عن المجلدات الفارغة." : "Select directory and click 'Scan Folders' to find all empty folders.",
                Font = UITheme.FontSmall,
                ForeColor = UITheme.TextSecondary,
                Size = new Size(584, 22),
                TextAlign = isAr ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft
            };

            var btnClose = new Button
            {
                Text = isAr ? "إغلاق" : "Close",
                Size = new Size(104, 34)
            };
            UITheme.StyleSecondaryButton(btnClose);
            btnClose.Click += (s, e) =>
            {
                activeCts?.Cancel();
                Close();
            };

            if (isAr)
            {
                btnClose.Location = new Point(18, 480);
                pbScan.Location = new Point(18 + btnClose.Width + 12, 472);
                lblStatus.Location = new Point(18 + btnClose.Width + 12, 486);
            }
            else
            {
                pbScan.Location = new Point(18, 472);
                lblStatus.Location = new Point(20, 486);
                btnClose.Location = new Point(622, 480);
            }

            Controls.Add(lblHeader);
            Controls.Add(pnlTop);
            Controls.Add(pnlList);
            Controls.Add(pbScan);
            Controls.Add(lblStatus);
            Controls.Add(btnClose);

            UITheme.StyleToolHeader(this, lblHeader, "Folder", UITheme.Success, isAr);
            ResumeLayout(false);
            PerformLayout();

            FormClosing += (s, e) => activeCts?.Cancel();
        }

        private bool colsScaled;
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!colsScaled && lvFolders != null)
            {
                colsScaled = true;
                float scale = UITheme.GetDpiScale(lvFolders);
                foreach (ColumnHeader col in lvFolders.Columns)
                    col.Width = (int)Math.Round(col.Width * scale);
            }
        }

        private void SetBusyState(bool busy)
        {
            isBusy = busy;
            btnScan.Enabled = !busy;
            btnBrowse.Enabled = !busy;
            btnClean.Enabled = !busy;
            btnStop.Enabled = busy;
            pbScan.Visible = busy;
        }

        private void SyncVirtualListSize()
        {
            lvFolders.SelectedIndices.Clear();
            lvFolders.VirtualListSize = foundEmptyFolders.Count;
            lvFolders.Invalidate();
        }

        private async Task ScanEmptyFoldersAsync()
        {
            if (isBusy) return;
            bool isAr = LocalizationService.IsArabic;
            string folder = txtFolder.Text.Trim();
            if (!Directory.Exists(folder))
            {
                CleanRamBoosterApp.WpfUI.AppDialog.Show(this,
                    isAr ? "المجلد المحدد غير موجود." : "Selected directory does not exist.",
                    isAr ? "مسار غير صالح" : "Invalid Path",
                    CleanRamBoosterApp.WpfUI.AppDialogTone.Warning);
                return;
            }

            SetBusyState(true);
            foundEmptyFolders.Clear();
            SyncVirtualListSize();
            lastScanRoot = Path.GetFullPath(folder);

            lblStatus.Text = isAr ? "جاري فحص المجلدات الفرعية للبحث عن المجلدات الفارغة..." : "Scanning directory trees safely for all empty folders...";

            activeCts?.Dispose();
            activeCts = new CancellationTokenSource();
            var ct = activeCts.Token;

            var progress = new Progress<DiskScanProgress>(p =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                lblStatus.Text = isAr
                    ? $"تم فحص {p.FilesScanned:N0} مجلد • تم العثور على {p.MatchesFound:N0} مجلد فارغ"
                    : $"Scanned {p.FilesScanned:N0} folders • Found {p.MatchesFound:N0} empty | {p.CurrentFolder}";
            });

            try
            {
                var empties = await diskTools.FindEmptyFoldersAsync(lastScanRoot, progress, ct);
                if (IsDisposed) return;

                foundEmptyFolders.Clear();
                foundEmptyFolders.AddRange(empties);
                SyncVirtualListSize();

                lblStatus.Text = ct.IsCancellationRequested
                    ? (isAr ? $"تم إيقاف الفحص: تم عرض {empties.Count:N0} مجلد فارغ تم العثور عليها حتى الآن." : $"Scan stopped: showing {empties.Count:N0} empty folder(s) found so far.")
                    : (isAr ? $"تم العثور على {empties.Count:N0} مجلدات فارغة مهجورة (عرض الكل بلا حد)." : $"Found {empties.Count:N0} abandoned empty folder(s) (all shown, no limit).");
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed)
                {
                    lblStatus.Text = isAr ? "تم إيقاف الفحص بناءً على طلب المستخدم." : "Scan was stopped by user.";
                }
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                {
                    lblStatus.Text = isAr ? $"خطأ أثناء الفحص: {ex.Message}" : $"Scan error: {ex.Message}";
                }
            }
            finally
            {
                if (!IsDisposed)
                {
                    SetBusyState(false);
                }
                activeCts?.Dispose();
                activeCts = null;
            }
        }

        private async Task CleanFoldersAsync()
        {
            if (isBusy) return;
            bool isAr = LocalizationService.IsArabic;
            if (foundEmptyFolders.Count == 0)
            {
                CleanRamBoosterApp.WpfUI.AppDialog.Show(this,
                    isAr ? "لم يتم العثور على مجلدات فارغة لتنظيفها." : "No empty folders found to clean.",
                    isAr ? "تنبيه" : "Notice",
                    CleanRamBoosterApp.WpfUI.AppDialogTone.Info);
                return;
            }

            if (!CleanRamBoosterApp.WpfUI.AppDialog.Confirm(this,
                isAr ? $"هل أنت متأكد من رغبتك في حذف {foundEmptyFolders.Count:N0} مجلداً فارغاً؟" : $"Are you sure you want to delete all {foundEmptyFolders.Count:N0} empty folder(s)?",
                isAr ? "تأكيد التنظيف" : "Confirm Clean",
                CleanRamBoosterApp.WpfUI.AppDialogTone.Warning,
                isDestructive: true)) return;

            SetBusyState(true);
            lblStatus.Text = isAr ? "جاري حذف المجلدات الفارغة..." : "Removing empty folders...";

            var snapshot = foundEmptyFolders.OrderByDescending(p => p.Length).ToList();
            string scanRoot = lastScanRoot;

            activeCts?.Dispose();
            activeCts = new CancellationTokenSource();
            var ct = activeCts.Token;

            var progress = new Progress<(int done, int total, int deleted)>(p =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                lblStatus.Text = isAr
                    ? $"جاري الحذف ({p.done:N0}/{p.total:N0}) • تم حذف {p.deleted:N0}..."
                    : $"Deleting ({p.done:N0}/{p.total:N0}) • Removed {p.deleted:N0}...";
            });

            try
            {
                var result = await Task.Run(() =>
                {
                    var deletedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var gonePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    int failedCount = 0;
                    string? firstErr = null;
                    int processed = 0;
                    var sw = Stopwatch.StartNew();
                    long lastRep = 0;

                    foreach (var path in snapshot)
                    {
                        if (ct.IsCancellationRequested) break;
                        processed++;

                        if (deletedPaths.Contains(path) || gonePaths.Contains(path))
                        {
                            // Already deleted earlier as a parent or duplicate
                        }
                        else if (!Directory.Exists(path))
                        {
                            gonePaths.Add(path);
                        }
                        else if (diskTools.DeleteEmptyFolder(path, out string? err))
                        {
                            deletedPaths.Add(path);

                            string? parent = Path.GetDirectoryName(path);
                            while (!string.IsNullOrEmpty(parent) &&
                                   !string.IsNullOrEmpty(scanRoot) &&
                                   !parent.Equals(scanRoot, StringComparison.OrdinalIgnoreCase) &&
                                   parent.StartsWith(scanRoot.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                            {
                                if (deletedPaths.Contains(parent)) break;
                                if (diskTools.DeleteEmptyFolder(parent, out _))
                                {
                                    deletedPaths.Add(parent);
                                    parent = Path.GetDirectoryName(parent);
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }
                        else
                        {
                            failedCount++;
                            firstErr ??= $"{path}: {err}";
                        }

                        if (processed % 25 == 0 || sw.ElapsedMilliseconds - lastRep >= 100)
                        {
                            lastRep = sw.ElapsedMilliseconds;
                            ((IProgress<(int, int, int)>)progress).Report((processed, snapshot.Count, deletedPaths.Count));
                        }
                    }

                    return (deletedPaths, gonePaths, failedCount, firstErr, cancelled: ct.IsCancellationRequested);
                });

                if (IsDisposed) return;

                foundEmptyFolders.RemoveAll(p => result.deletedPaths.Contains(p) || result.gonePaths.Contains(p) || !Directory.Exists(p));
                SyncVirtualListSize();

                int totalDeleted = result.deletedPaths.Count;
                if (result.cancelled)
                {
                    lblStatus.Text = isAr
                        ? $"تم إيقاف الحذف: تم حذف {totalDeleted:N0} مجلد فارغ."
                        : $"Cleanup stopped: deleted {totalDeleted:N0} empty folder(s).";
                }
                else if (result.failedCount > 0)
                {
                    lblStatus.Text = isAr
                        ? $"تم حذف {totalDeleted:N0} مجلداً • تعذر حذف {result.failedCount:N0} (مقفل أو محمي من النظام)."
                        : $"Deleted {totalDeleted:N0} empty folder(s) • Skipped {result.failedCount:N0} locked/protected folder(s).";

                    string msg = isAr
                        ? $"تم حذف {totalDeleted:N0} مجلداً فارغاً.\nتعذر حذف {result.failedCount:N0} مجلدات لأنها قيد الاستخدام أو محمية من النظام:\n{result.firstErr}"
                        : $"Deleted {totalDeleted:N0} empty folder(s).\nCould not delete {result.failedCount:N0} locked or system-protected folder(s):\n{result.firstErr}";
                    CleanRamBoosterApp.WpfUI.AppDialog.Show(this, msg, isAr ? "نتيجة التنظيف" : "Cleanup Result",
                        totalDeleted > 0 ? CleanRamBoosterApp.WpfUI.AppDialogTone.Info : CleanRamBoosterApp.WpfUI.AppDialogTone.Warning);
                }
                else
                {
                    lblStatus.Text = isAr ? $"تم تنظيف {totalDeleted:N0} مجلداً فارغاً بنجاح." : $"Successfully pruned {totalDeleted:N0} empty folder(s).";
                    CleanRamBoosterApp.WpfUI.AppDialog.Show(this,
                        isAr ? $"تم حذف {totalDeleted:N0} مجلداً فارغاً بنجاح." : $"Deleted {totalDeleted:N0} empty folder(s).",
                        isAr ? "تم بنجاح" : "Success",
                        CleanRamBoosterApp.WpfUI.AppDialogTone.Success);
                }
            }
            finally
            {
                if (!IsDisposed) SetBusyState(false);
                activeCts?.Dispose();
                activeCts = null;
            }
        }

        private List<string> GetSelectedPaths()
        {
            var list = new List<string>(lvFolders.SelectedIndices.Count);
            foreach (int idx in lvFolders.SelectedIndices)
            {
                if (idx >= 0 && idx < foundEmptyFolders.Count)
                {
                    list.Add(foundEmptyFolders[idx]);
                }
            }
            return list;
        }

        private async Task DeleteSelectedFoldersAsync()
        {
            if (isBusy) return;
            bool isAr = LocalizationService.IsArabic;
            var selectedPaths = GetSelectedPaths();
            if (selectedPaths.Count == 0) return;

            if (!CleanRamBoosterApp.WpfUI.AppDialog.Confirm(this,
                isAr ? $"هل أنت متأكد من رغبتك في حذف {selectedPaths.Count:N0} مجلداً محدداً نهائياً؟" : $"Are you sure you want to permanently delete {selectedPaths.Count:N0} selected empty folder(s)?",
                isAr ? "تأكيد الحذف" : "Confirm Deletion",
                CleanRamBoosterApp.WpfUI.AppDialogTone.Warning,
                isDestructive: true)) return;

            SetBusyState(true);
            lblStatus.Text = isAr ? "جاري حذف المجلدات المحددة..." : "Deleting selected empty folders...";

            var ordered = selectedPaths.OrderByDescending(p => p.Length).ToList();
            activeCts?.Dispose();
            activeCts = new CancellationTokenSource();
            var ct = activeCts.Token;

            try
            {
                var res = await Task.Run(() =>
                {
                    var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    int failed = 0;
                    string? firstErr = null;
                    foreach (var path in ordered)
                    {
                        if (ct.IsCancellationRequested) break;
                        if (diskTools.DeleteEmptyFolder(path, out string? err))
                        {
                            removed.Add(path);
                        }
                        else
                        {
                            failed++;
                            firstErr ??= $"{path}: {err}";
                        }
                    }
                    return (removed, failed, firstErr);
                });

                if (IsDisposed) return;

                foundEmptyFolders.RemoveAll(p => res.removed.Contains(p) || !Directory.Exists(p));
                SyncVirtualListSize();

                int deleted = res.removed.Count;
                lblStatus.Text = res.failed > 0
                    ? (isAr ? $"تم حذف {deleted:N0} مجلداً • تعذر حذف {res.failed:N0} (مقفل/محمي)." : $"Deleted {deleted:N0} empty folder(s) • Failed {res.failed:N0}: {res.firstErr}")
                    : (isAr ? $"تم حذف {deleted:N0} مجلداً فارغاً." : $"Deleted {deleted:N0} empty folder(s).");

                if (res.failed > 0 && deleted == 0)
                {
                    CleanRamBoosterApp.WpfUI.AppDialog.Show(this,
                        res.firstErr ?? (isAr ? "تعذر حذف المجلدات المحددة." : "Could not delete the selected folders."),
                        isAr ? "تعذر الحذف" : "Delete Failed",
                        CleanRamBoosterApp.WpfUI.AppDialogTone.Warning);
                }
            }
            finally
            {
                if (!IsDisposed) SetBusyState(false);
                activeCts?.Dispose();
                activeCts = null;
            }
        }

        private void OpenSelectedFolder()
        {
            if (lvFolders.SelectedIndices.Count == 0) return;
            int idx = lvFolders.SelectedIndices[0];
            if (idx < 0 || idx >= foundEmptyFolders.Count) return;
            string path = foundEmptyFolders[idx];
            if (Directory.Exists(path))
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); } catch { }
            }
        }

        private void CopySelectedFolderPath()
        {
            var selectedPaths = GetSelectedPaths();
            if (selectedPaths.Count == 0) return;
            var sb = new StringBuilder();
            foreach (var p in selectedPaths)
            {
                sb.AppendLine(p);
            }
            try
            {
                Clipboard.SetText(sb.ToString().TrimEnd());
                lblStatus.Text = LocalizationService.IsArabic
                    ? $"تم نسخ {selectedPaths.Count:N0} مسار إلى الحافظة."
                    : $"Copied {selectedPaths.Count:N0} folder path(s) to clipboard.";
            }
            catch { }
        }

        private static void SelectAllVirtualListView(ListView lv)
        {
            if (!lv.IsHandleCreated || lv.VirtualListSize == 0) return;
            var item = new LVITEM_STATE
            {
                mask = LVIF_STATE,
                state = LVIS_SELECTED,
                stateMask = LVIS_SELECTED
            };
            SendMessage(lv.Handle, LVM_SETITEMSTATE, new IntPtr(-1), ref item);
        }
    }
}
