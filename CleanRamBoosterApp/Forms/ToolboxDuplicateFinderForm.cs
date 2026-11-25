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
    public class ToolboxDuplicateFinderForm : Form
    {
        private const int LVM_FIRST = 0x1000;
        private const int LVM_SETITEMSTATE = LVM_FIRST + 43;
        private const uint LVIS_SELECTED = 0x0002;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct LVITEM
        {
            public uint stateMask;
            public uint state;
            public uint mask;
            public int iItem;
            public int iSubItem;
            public IntPtr pszText;
            public int cchTextMax;
            public int iImage;
            public IntPtr lParam;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref LVITEM lParam);

        private sealed class DuplicateRow
        {
            public string FilePath { get; init; } = string.Empty;
            public string FileName { get; init; } = string.Empty;
            public long FileSize { get; init; }
            public string SizeText { get; init; } = string.Empty;
            public string FullHash { get; init; } = string.Empty;
            public string ShortHash { get; init; } = string.Empty;
            public bool IsOriginal { get; init; }
            public bool IsChecked { get; set; }
        }

        private readonly DiskToolsService diskTools = new();
        private readonly TextBox txtFolder;
        private readonly ListView lvDuplicates;
        private readonly Label lblStatus;
        private readonly ProgressBar pbScan;
        private readonly Button btnScan;
        private readonly Button btnStop;
        private readonly Button btnDeleteSelected;
        private readonly Button btnSmartSelect;
        private readonly List<DuplicateRow> rows = new();
        private CancellationTokenSource? cts;
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

        public ToolboxDuplicateFinderForm()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;

            bool isAr = LocalizationService.IsArabic;
            Text = isAr ? "مكتشف الملفات المكررة - PC Boost" : "Duplicate File Finder - Zero-Bloat Space Reclaimer";
            ClientSize = new Size(744, 542);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            FormClosing += (s, e) => cts?.Cancel();
            Font = UITheme.FontBody;
            BackColor = UITheme.BgContent;
            RightToLeft = isAr ? RightToLeft.Yes : RightToLeft.No;
            if (UITheme.AppIcon != null) Icon = UITheme.AppIcon;
            UITheme.ApplyDwmDarkMode(Handle, UITheme.IsDarkMode);

            var lblHeader = new Label
            {
                Text = isAr ? "مكتشف ومزيل الملفات المكررة" : "Duplicate File Finder",
                Font = UITheme.FontHeader,
                ForeColor = UITheme.TextPrimary
            };

            var pnlTop = UITheme.CreateCardPanel(18, 56, 708, 92);

            var lblDir = new Label { Text = isAr ? "المجلد:" : "Directory:", AutoSize = true, Font = UITheme.FontBody, ForeColor = UITheme.TextPrimary };
            txtFolder = new TextBox
            {
                Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                BackColor = UITheme.InputBg,
                ForeColor = UITheme.InputFg,
                Font = UITheme.FontBody,
                RightToLeft = RightToLeft.No,
                BorderStyle = BorderStyle.FixedSingle
            };

            var btnBrowse = new Button { Text = isAr ? "استعراض..." : "Browse...", Size = new Size(102, 30) };
            UITheme.StyleSecondaryButton(btnBrowse);
            btnBrowse.Click += (s, e) =>
            {
                using var fbd = new FolderBrowserDialog();
                if (fbd.ShowDialog(this) == DialogResult.OK)
                    txtFolder.Text = fbd.SelectedPath;
            };

            btnScan = new Button
            {
                Text = isAr ? "بدء الفحص" : "Start Scan",
                Size = new Size(132, 32),
                UseMnemonic = false
            };
            UITheme.StyleButton(btnScan, UITheme.Primary, Color.White, UITheme.PrimaryHover);
            UITheme.SetButtonIcon(btnScan, "SearchFiles", Color.White, 15);
            btnScan.Click += async (s, e) => await ScanFolderAsync();

            btnStop = new Button
            {
                Text = isAr ? "إيقاف" : "Stop",
                Size = new Size(94, 32),
                Enabled = false
            };
            UITheme.StyleSecondaryButton(btnStop);
            UITheme.SetButtonIcon(btnStop, "Stop", UITheme.TextPrimary, 14);
            btnStop.Click += (s, e) => cts?.Cancel();

            btnDeleteSelected = new Button
            {
                Text = isAr ? "حذف المكررات" : "Delete Checked Duplicates",
                Size = new Size(isAr ? 172 : 216, 32),
                UseMnemonic = false
            };
            UITheme.StyleButton(btnDeleteSelected, UITheme.Danger, Color.White, UITheme.DangerHover);
            UITheme.SetButtonIcon(btnDeleteSelected, "Trash", Color.White, 15);
            btnDeleteSelected.Click += async (s, e) => await DeleteCheckedDuplicatesAsync();

            btnSmartSelect = new Button
            {
                Text = isAr ? "تحديد ذكي" : "Auto-Select",
                Size = new Size(134, 32),
                UseMnemonic = false
            };
            UITheme.StyleSecondaryButton(btnSmartSelect);
            UITheme.SetButtonIcon(btnSmartSelect, "Bolt", UITheme.TextPrimary, 15);
            btnSmartSelect.Click += (s, e) => SmartSelectKeepOne();

            if (isAr)
            {
                int lblDirW = TextRenderer.MeasureText(lblDir.Text, lblDir.Font).Width;
                lblDir.Location = new Point(708 - 14 - lblDirW, 13);
                btnBrowse.Location = new Point(14, 8);
                txtFolder.Location = new Point(btnBrowse.Right + 10, 10);
                txtFolder.Width = lblDir.Left - 10 - txtFolder.Left;

                btnScan.Location = new Point(708 - 14 - btnScan.Width, 48);
                btnStop.Location = new Point(btnScan.Left - 8 - btnStop.Width, 48);
                btnDeleteSelected.Location = new Point(btnStop.Left - 8 - btnDeleteSelected.Width, 48);
                btnSmartSelect.Location = new Point(14, 48);
            }
            else
            {
                int lblDirW = TextRenderer.MeasureText(lblDir.Text, lblDir.Font).Width;
                lblDir.Location = new Point(14, 13);
                btnBrowse.Location = new Point(708 - 14 - btnBrowse.Width, 8);
                txtFolder.Location = new Point(14 + lblDirW + 10, 10);
                txtFolder.Width = btnBrowse.Left - 10 - txtFolder.Left;

                btnScan.Location = new Point(14, 48);
                btnStop.Location = new Point(btnScan.Right + 8, 48);
                btnDeleteSelected.Location = new Point(btnStop.Right + 8, 48);
                btnSmartSelect.Location = new Point(708 - 14 - btnSmartSelect.Width, 48);
            }

            pnlTop.Controls.Add(lblDir);
            pnlTop.Controls.Add(txtFolder);
            pnlTop.Controls.Add(btnBrowse);
            pnlTop.Controls.Add(btnScan);
            pnlTop.Controls.Add(btnStop);
            pnlTop.Controls.Add(btnDeleteSelected);
            pnlTop.Controls.Add(btnSmartSelect);

            var pnlList = UITheme.CreateCardPanel(18, 156, 708, 324);

            lvDuplicates = new ListView
            {
                Location = new Point(10, 10),
                Size = new Size(688, 302),
                View = View.Details,
                VirtualMode = true,
                FullRowSelect = true,
                GridLines = false,
                BorderStyle = BorderStyle.None,
                Font = UITheme.FontBody,
                RightToLeft = isAr ? RightToLeft.Yes : RightToLeft.No,
                MultiSelect = true
            };
            UITheme.StyleListView(lvDuplicates);
            lvDuplicates.Columns.Add(isAr ? "اسم الملف" : "File Name", 202);
            lvDuplicates.Columns.Add(isAr ? "الحجم" : "Size", 85);
            lvDuplicates.Columns.Add(isAr ? "بصمة MD5" : "MD5 Hash", 115);
            lvDuplicates.Columns.Add(isAr ? "المسار الكامل" : "Full File Path", 278);

            lvDuplicates.RetrieveVirtualItem += (s, e) =>
            {
                if (e.ItemIndex < 0 || e.ItemIndex >= rows.Count)
                {
                    e.Item = new ListViewItem(string.Empty);
                    return;
                }
                var r = rows[e.ItemIndex];
                string prefix = r.IsChecked ? "☑ " : "☐ ";
                var lvi = new ListViewItem(prefix + r.FileName)
                {
                    ForeColor = r.IsOriginal ? UITheme.Primary : UITheme.TextPrimary
                };
                lvi.SubItems.Add(r.SizeText);
                lvi.SubItems.Add(r.ShortHash);
                lvi.SubItems.Add(r.FilePath);
                e.Item = lvi;
            };

            lvDuplicates.MouseClick += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                var hit = lvDuplicates.HitTest(e.Location);
                if (hit.Item == null) return;
                int idx = hit.Item.Index;
                if (idx < 0 || idx >= rows.Count) return;

                // Toggle checkbox when clicking near the leading checkbox glyph
                float scale = UITheme.GetDpiScale(lvDuplicates);
                int hitThreshold = (int)Math.Round(30 * scale);
                bool clickedCheckbox = isAr
                    ? e.X >= lvDuplicates.ClientSize.Width - hitThreshold
                    : e.X <= hitThreshold;

                if (clickedCheckbox)
                {
                    rows[idx].IsChecked = !rows[idx].IsChecked;
                    lvDuplicates.RedrawItems(idx, idx, false);
                    UpdateCheckedStatusSummary();
                }
            };

            // Context Menu & Multi-Selection Shortcuts
            var ctxDup = new ContextMenuStrip();
            var miDupCheck = new ToolStripMenuItem(isAr ? "تحديد المظلل (Space)" : "Check Selected (Space)", null, (s, e) => SetDuplicateChecks(true));
            var miDupUncheck = new ToolStripMenuItem(isAr ? "إلغاء تحديد المظلل" : "Uncheck Selected", null, (s, e) => SetDuplicateChecks(false));
            var miDupKeepOne = new ToolStripMenuItem(isAr ? "تحديد ذكي (إبقاء نسخة أصلية)" : "Smart Select (Keep 1 Original per Group)", null, (s, e) => SmartSelectKeepOne());
            var miDupSep1 = new ToolStripSeparator();
            var miDupOpen = new ToolStripMenuItem(isAr ? "فتح المسار في المستكشف (Enter)" : "Open Location in Explorer (Enter)", null, (s, e) => OpenSelectedDuplicateLocation());
            var miDupCopy = new ToolStripMenuItem(isAr ? "نسخ المسار الكامل (Ctrl+C)" : "Copy Full Path (Ctrl+C)", null, (s, e) => CopySelectedDuplicatePath());
            var miDupSelectAll = new ToolStripMenuItem(isAr ? "تحديد الكل (Ctrl+A)" : "Select All Rows (Ctrl+A)", null, (s, e) => SelectAllVirtual());
            var miDupSep2 = new ToolStripSeparator();
            var miDupDelete = new ToolStripMenuItem(isAr ? "حذف الملفات المحددة (Delete)" : "Delete Checked Duplicates (Delete)", null, async (s, e) => await DeleteCheckedDuplicatesAsync()) { ForeColor = UITheme.Danger };
            ctxDup.Items.AddRange(new ToolStripItem[] { miDupCheck, miDupUncheck, miDupKeepOne, miDupSep1, miDupOpen, miDupCopy, miDupSelectAll, miDupSep2, miDupDelete });
            lvDuplicates.ContextMenuStrip = ctxDup;

            lvDuplicates.DoubleClick += (s, e) => OpenSelectedDuplicateLocation();
            lvDuplicates.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.A)
                {
                    SelectAllVirtual();
                    e.Handled = true;
                }
                else if (e.Control && e.KeyCode == Keys.C)
                {
                    CopySelectedDuplicatePath();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Space)
                {
                    ToggleDuplicateChecks();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Delete)
                {
                    _ = DeleteCheckedDuplicatesAsync();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Enter)
                {
                    OpenSelectedDuplicateLocation();
                    e.Handled = true;
                }
            };

            pnlList.Controls.Add(lvDuplicates);

            pbScan = new ProgressBar
            {
                Location = new Point(18, 488),
                Size = new Size(580, 10),
                Style = ProgressBarStyle.Marquee,
                Visible = false
            };

            lblStatus = new Label
            {
                Text = isAr ? "حدد المجلد المطلوب واضغط على 'بدء الفحص' للبحث عن الملفات المتطابقة." : "Select directory and click 'Start Scan' to search for identical duplicate files.",
                Font = UITheme.FontSmall,
                ForeColor = UITheme.TextSecondary,
                Size = new Size(580, 22),
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
                cts?.Cancel();
                Close();
            };

            if (isAr)
            {
                btnClose.Location = new Point(18, 496);
                lblStatus.Location = new Point(18 + btnClose.Width + 12, 504);
            }
            else
            {
                lblStatus.Location = new Point(20, 504);
                btnClose.Location = new Point(622, 496);
            }

            Controls.Add(lblHeader);
            Controls.Add(pnlTop);
            Controls.Add(pnlList);
            Controls.Add(pbScan);
            Controls.Add(lblStatus);
            Controls.Add(btnClose);

            UITheme.StyleToolHeader(this, lblHeader, "SearchFiles", UITheme.Success, isAr);
            ResumeLayout(false);
            PerformLayout();
        }

        private bool colsScaled;
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!colsScaled && lvDuplicates != null)
            {
                colsScaled = true;
                float scale = UITheme.GetDpiScale(lvDuplicates);
                foreach (ColumnHeader col in lvDuplicates.Columns)
                    col.Width = (int)Math.Round(col.Width * scale);
            }
        }

        private void SetBusy(bool busy)
        {
            isBusy = busy;
            btnScan.Enabled = !busy;
            btnDeleteSelected.Enabled = !busy;
            btnSmartSelect.Enabled = !busy;
            btnStop.Enabled = busy;
            pbScan.Visible = busy;
        }

        public async Task ScanFolderAsync()
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

            SetBusy(true);
            rows.Clear();
            lvDuplicates.SelectedIndices.Clear();
            lvDuplicates.VirtualListSize = 0;

            cts = new CancellationTokenSource();
            var progress = new Progress<DiskScanProgress>(p =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                lblStatus.Text = isAr
                    ? $"تم فحص {p.FilesScanned:N0} ملف... تم العثور على {p.MatchesFound:N0} مكرر"
                    : $"Scanned {p.FilesScanned:N0} files... Found {p.MatchesFound:N0} duplicates | {p.CurrentFolder}";
            });

            try
            {
                lblStatus.Text = isAr ? "جاري فحص المجلدات بأمان..." : "Traversing directory trees safely...";
                var groups = await diskTools.FindDuplicatesAsync(folder, progress, cts.Token, maxFiles: int.MaxValue);
                if (IsDisposed) return;

                int totalDuplicates = 0;
                long wasteBytes = 0;
                var built = new List<DuplicateRow>();

                foreach (var g in groups)
                {
                    string sizeText = UITheme.FormatBytes(g.FileSize);
                    string shortHash = g.Hash.Length >= 12 ? g.Hash.Substring(0, 12) + "..." : g.Hash;
                    bool isFirst = true;
                    foreach (var path in g.FilePaths)
                    {
                        built.Add(new DuplicateRow
                        {
                            FilePath = path,
                            FileName = Path.GetFileName(path),
                            FileSize = g.FileSize,
                            SizeText = sizeText,
                            FullHash = g.Hash,
                            ShortHash = shortHash,
                            IsOriginal = isFirst,
                            IsChecked = !isFirst
                        });
                        if (!isFirst)
                        {
                            totalDuplicates++;
                            wasteBytes += g.FileSize;
                        }
                        isFirst = false;
                    }
                }

                rows.AddRange(built);
                lvDuplicates.VirtualListSize = rows.Count;
                lvDuplicates.Invalidate();

                string wasteStr = UITheme.FormatBytes(wasteBytes);
                lblStatus.Text = isAr
                    ? $"اكتمل الفحص: {totalDuplicates:N0} ملف مكرر ضمن {groups.Count:N0} مجموعات (~{wasteStr} قابلة للاستعادة)."
                    : $"Scan finished: Located {totalDuplicates:N0} duplicate copies across {groups.Count:N0} groups (~{wasteStr} reclaimable).";
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed)
                    lblStatus.Text = isAr ? "تم إيقاف الفحص بناءً على طلب المستخدم." : "Scan was cancelled by user.";
            }
            catch (Exception ex)
            {
                DiagnosticLogger.LogError(nameof(ToolboxDuplicateFinderForm), "Duplicate scan failed", ex);
                if (!IsDisposed)
                    lblStatus.Text = isAr ? $"خطأ أثناء الفحص: {ex.Message}" : $"Scan error: {ex.Message}";
            }
            finally
            {
                if (!IsDisposed)
                    SetBusy(false);
                cts?.Dispose();
                cts = null;
            }
        }

        private async Task DeleteCheckedDuplicatesAsync()
        {
            if (isBusy) return;
            bool isAr = LocalizationService.IsArabic;
            var targets = rows.Where(r => r.IsChecked).ToList();
            if (targets.Count == 0)
            {
                CleanRamBoosterApp.WpfUI.AppDialog.Show(this,
                    isAr ? "يرجى تحديد الملفات المكررة المراد حذفها (انقر مربع الاختيار أو اضغط Space)." : "Please check the duplicate files you want to delete (click checkbox or press Space).",
                    isAr ? "تنبيه" : "Notice",
                    CleanRamBoosterApp.WpfUI.AppDialogTone.Info);
                return;
            }

            if (!CleanRamBoosterApp.WpfUI.AppDialog.Confirm(this,
                isAr ? $"هل أنت متأكد من الحذف النهائي لـ {targets.Count:N0} ملف محدد؟" : $"Are you sure you want to permanently delete {targets.Count:N0} checked files?",
                isAr ? "تأكيد الحذف" : "Confirm Deletion",
                CleanRamBoosterApp.WpfUI.AppDialogTone.Warning,
                isDestructive: true)) return;

            SetBusy(true);
            lblStatus.Text = isAr ? $"جاري حذف {targets.Count:N0} ملف مكرر..." : $"Deleting {targets.Count:N0} duplicate file(s)...";

            var (deletedSet, deleted, freed, failures) = await Task.Run(() =>
            {
                var delPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int delCount = 0;
                long freedBytes = 0;
                var fails = new List<string>();

                foreach (var row in targets)
                {
                    try
                    {
                        if (File.Exists(row.FilePath))
                        {
                            var attrs = File.GetAttributes(row.FilePath);
                            if ((attrs & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != 0)
                                File.SetAttributes(row.FilePath, FileAttributes.Normal);
                            File.Delete(row.FilePath);
                            delCount++;
                            freedBytes += row.FileSize;
                            delPaths.Add(row.FilePath);
                        }
                        else
                        {
                            delPaths.Add(row.FilePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        fails.Add($"{row.FileName}: {ex.Message}");
                    }
                }
                return (delPaths, delCount, freedBytes, fails);
            });

            if (IsDisposed) return;

            rows.RemoveAll(r => deletedSet.Contains(r.FilePath));
            lvDuplicates.SelectedIndices.Clear();
            lvDuplicates.VirtualListSize = rows.Count;
            lvDuplicates.Invalidate();
            SetBusy(false);

            string freedStr = UITheme.FormatBytes(freed);
            if (failures.Count == 0)
            {
                lblStatus.Text = isAr
                    ? $"تم حذف {deleted:N0} ملف مكرر بنجاح. تم توفير ~\u200E{freedStr}\u200E."
                    : $"Successfully deleted {deleted:N0} duplicate files. Reclaimed ~{freedStr}.";
                CleanRamBoosterApp.WpfUI.AppDialog.Show(this,
                    isAr ? $"تم حذف {deleted:N0} نسخة مكررة، وتم توفير \u200E{freedStr}\u200E." : $"Deleted {deleted:N0} duplicates, reclaiming {freedStr}.",
                    isAr ? "تم بنجاح" : "Success",
                    CleanRamBoosterApp.WpfUI.AppDialogTone.Success);
            }
            else
            {
                lblStatus.Text = isAr
                    ? $"تم حذف {deleted:N0} ملفات (~{freedStr}) • تعذر حذف {failures.Count:N0}."
                    : $"Deleted {deleted:N0} duplicate(s) (~{freedStr}) • Failed: {failures.Count:N0}.";
                string errList = string.Join("\n• ", failures.Take(5));
                CleanRamBoosterApp.WpfUI.AppDialog.Show(this,
                    isAr
                        ? $"تم حذف {deleted:N0} نسخة مكررة (~{freedStr}).\n\nتعذر حذف {failures.Count:N0} ملفات:\n• {errList}"
                        : $"Deleted {deleted:N0} duplicate(s) (~{freedStr} freed).\n\nCould not delete {failures.Count:N0} file(s):\n• {errList}",
                    isAr ? "نتيجة الحذف" : "Delete Result",
                    CleanRamBoosterApp.WpfUI.AppDialogTone.Warning);
            }
        }

        private void UpdateCheckedStatusSummary()
        {
            int checkedCount = 0;
            long checkedBytes = 0;
            foreach (var r in rows)
            {
                if (r.IsChecked)
                {
                    checkedCount++;
                    checkedBytes += r.FileSize;
                }
            }
            lblStatus.Text = LocalizationService.IsArabic
                ? $"المحدد للحذف: {checkedCount:N0} من {rows.Count:N0} (~{UITheme.FormatBytes(checkedBytes)})"
                : $"Checked for deletion: {checkedCount:N0} of {rows.Count:N0} (~{UITheme.FormatBytes(checkedBytes)})";
        }

        private void SetDuplicateChecks(bool check)
        {
            foreach (int idx in lvDuplicates.SelectedIndices)
            {
                if (idx >= 0 && idx < rows.Count)
                    rows[idx].IsChecked = check;
            }
            lvDuplicates.Invalidate();
            UpdateCheckedStatusSummary();
        }

        private void ToggleDuplicateChecks()
        {
            if (lvDuplicates.SelectedIndices.Count == 0) return;
            int first = lvDuplicates.SelectedIndices[0];
            if (first < 0 || first >= rows.Count) return;
            bool target = !rows[first].IsChecked;
            foreach (int idx in lvDuplicates.SelectedIndices)
            {
                if (idx >= 0 && idx < rows.Count)
                    rows[idx].IsChecked = target;
            }
            lvDuplicates.Invalidate();
            UpdateCheckedStatusSummary();
        }

        private void SmartSelectKeepOne()
        {
            var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
            {
                if (!string.IsNullOrEmpty(r.FullHash))
                {
                    r.IsChecked = !seenHashes.Add(r.FullHash);
                }
            }
            lvDuplicates.Invalidate();
            lblStatus.Text = LocalizationService.IsArabic
                ? "اكتمل التحديد الذكي: تم إبقاء نسخة أصلية واحدة غير محددة لكل مجموعة مكررة."
                : "Smart selection complete: 1 original kept unchecked per duplicate group.";
        }

        private void OpenSelectedDuplicateLocation()
        {
            if (lvDuplicates.SelectedIndices.Count == 0) return;
            int idx = lvDuplicates.SelectedIndices[0];
            if (idx < 0 || idx >= rows.Count) return;
            string path = rows[idx].FilePath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    bool isAr = LocalizationService.IsArabic;
                    CleanRamBoosterApp.WpfUI.AppDialog.Show(this,
                        isAr ? $"تعذر فتح الموقع: {ex.Message}" : $"Could not open location: {ex.Message}",
                        isAr ? "خطأ" : "Error",
                        CleanRamBoosterApp.WpfUI.AppDialogTone.Error);
                }
            }
        }

        private void CopySelectedDuplicatePath()
        {
            if (lvDuplicates.SelectedIndices.Count == 0) return;
            var sb = new StringBuilder();
            foreach (int idx in lvDuplicates.SelectedIndices)
            {
                if (idx >= 0 && idx < rows.Count)
                    sb.AppendLine(rows[idx].FilePath);
            }
            try
            {
                Clipboard.SetText(sb.ToString().TrimEnd());
                lblStatus.Text = LocalizationService.IsArabic
                    ? $"تم نسخ {lvDuplicates.SelectedIndices.Count:N0} مسار إلى الحافظة."
                    : $"Copied {lvDuplicates.SelectedIndices.Count:N0} path(s) to clipboard.";
            }
            catch { }
        }

        private void SelectAllVirtual()
        {
            if (rows.Count == 0 || !lvDuplicates.IsHandleCreated) return;
            var lvi = new LVITEM { stateMask = LVIS_SELECTED, state = LVIS_SELECTED };
            SendMessage(lvDuplicates.Handle, LVM_SETITEMSTATE, (IntPtr)(-1), ref lvi);
        }
    }
}
