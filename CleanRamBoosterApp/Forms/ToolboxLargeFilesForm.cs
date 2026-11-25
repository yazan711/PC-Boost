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
    public class ToolboxLargeFilesForm : Form
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

        private readonly DiskToolsService diskTools = new();
        private readonly ComboBox cbDrive;
        private readonly TextBox txtFolder;
        private readonly ComboBox cbMinSize;
        private readonly ListView lvFiles;
        private readonly Label lblStatus;
        private readonly ProgressBar pbScan;
        private readonly Button btnScan;
        private readonly Button btnStop;
        private readonly Button btnDelete;
        private readonly List<LargeFileInfo> largeFiles = new();
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

        public ToolboxLargeFilesForm()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;

            bool isAr = LocalizationService.IsArabic;
            Text = isAr ? "مكتشف الملفات الضخمة - استعادة مساحة القرص - PC Boost" : "Large File Finder - Reclaim Disk Space";
            ClientSize = new Size(784, 542);
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
                Text = isAr ? "مكتشف الملفات الضخمة ومستهلكات التخزين" : "Large File Finder",
                Font = UITheme.FontHeader,
                ForeColor = UITheme.TextPrimary
            };

            var pnlTop = UITheme.CreateCardPanel(18, 56, 748, 92);

            var lblDrive = new Label { Text = isAr ? "المسار / القرص:" : "Drive / Path:", Location = new Point(12, 14), AutoSize = true, Font = UITheme.FontBody, ForeColor = UITheme.TextPrimary };

            cbDrive = new ComboBox
            {
                Location = new Point(95, 11),
                Width = 70,
                RightToLeft = RightToLeft.No
            };
            UITheme.StyleComboBox(cbDrive);
            txtFolder = new TextBox
            {
                Location = new Point(170, 10),
                Width = 435,
                Text = @"C:\",
                Font = UITheme.FontBody,
                RightToLeft = RightToLeft.No,
                BackColor = UITheme.InputBg,
                ForeColor = UITheme.InputFg,
                BorderStyle = BorderStyle.FixedSingle
            };

            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                cbDrive.Items.Add(d.Name);
            }
            if (cbDrive.Items.Count > 0)
            {
                cbDrive.SelectedIndex = 0;
                txtFolder.Text = cbDrive.SelectedItem?.ToString() ?? @"C:\";
            }
            cbDrive.SelectedIndexChanged += (s, e) =>
            {
                if (cbDrive.SelectedItem != null && txtFolder != null)
                    txtFolder.Text = cbDrive.SelectedItem.ToString() ?? @"C:\";
            };

            var btnBrowse = new Button { Text = isAr ? "استعراض..." : "Browse...", Size = new Size(102, 30) };
            UITheme.StyleSecondaryButton(btnBrowse);
            btnBrowse.Click += (s, e) =>
            {
                using var fbd = new FolderBrowserDialog();
                if (fbd.ShowDialog(this) == DialogResult.OK)
                    txtFolder.Text = fbd.SelectedPath;
            };

            var lblSize = new Label { Text = isAr ? "أدنى حجم:" : "Min Size:", AutoSize = true, Font = UITheme.FontBody, ForeColor = UITheme.TextPrimary };
            cbMinSize = new ComboBox
            {
                Width = isAr ? 164 : 118
            };
            UITheme.StyleComboBox(cbMinSize);
            cbMinSize.Items.AddRange(isAr
                ? new object[] { "أكبر من 50 MB", "أكبر من 100 MB", "أكبر من 500 MB", "أكبر من 1 GB", "أكبر من 5 GB" }
                : new object[] { "> 50 MB", "> 100 MB", "> 500 MB", "> 1 GB", "> 5 GB" });
            cbMinSize.SelectedIndex = 1;

            btnScan = new Button
            {
                Text = isAr ? "بدء الفحص" : "Find Files",
                Size = new Size(118, 32),
                UseMnemonic = false
            };
            UITheme.StyleButton(btnScan, UITheme.Primary, Color.White, UITheme.PrimaryHover);
            UITheme.SetButtonIcon(btnScan, "SearchFiles", Color.White, 15);
            btnScan.Click += async (s, e) => await ScanLargeFilesAsync();

            btnStop = new Button
            {
                Text = isAr ? "إيقاف" : "Stop",
                Size = new Size(isAr ? 84 : 86, 32),
                Enabled = false,
                UseMnemonic = false
            };
            UITheme.StyleSecondaryButton(btnStop);
            UITheme.SetButtonIcon(btnStop, "Stop", UITheme.TextPrimary, 14);
            btnStop.Click += (s, e) => cts?.Cancel();

            var btnOpen = new Button
            {
                Text = isAr ? "فتح المجلد" : "Open Folder",
                Size = new Size(isAr ? 122 : 124, 32),
                UseMnemonic = false
            };
            UITheme.StyleSecondaryButton(btnOpen);
            UITheme.SetButtonIcon(btnOpen, "Folder", UITheme.TextPrimary, 15);
            btnOpen.Click += (s, e) => OpenSelectedFile();

            btnDelete = new Button
            {
                Text = isAr ? "حذف الملف" : "Delete File",
                Size = new Size(120, 32),
                UseMnemonic = false
            };
            UITheme.StyleButton(btnDelete, UITheme.Danger, Color.White, UITheme.DangerHover);
            UITheme.SetButtonIcon(btnDelete, "Trash", Color.White, 15);
            btnDelete.Click += async (s, e) => await DeleteSelectedFileAsync();

            if (isAr)
            {
                int lblDriveW = TextRenderer.MeasureText(lblDrive.Text, lblDrive.Font).Width;
                int lblSizeW = TextRenderer.MeasureText(lblSize.Text, lblSize.Font).Width;

                lblDrive.Location = new Point(748 - 14 - lblDriveW, 13);
                cbDrive.Location = new Point(lblDrive.Left - 8 - cbDrive.Width, 10);
                btnBrowse.Location = new Point(14, 8);
                txtFolder.Location = new Point(btnBrowse.Right + 8, 10);
                txtFolder.Width = cbDrive.Left - 8 - txtFolder.Left;

                lblSize.Location = new Point(748 - 14 - lblSizeW, 54);
                cbMinSize.Location = new Point(lblSize.Left - 8 - cbMinSize.Width, 50);
                btnScan.Location = new Point(cbMinSize.Left - 10 - btnScan.Width, 48);
                btnStop.Location = new Point(btnScan.Left - 8 - btnStop.Width, 48);
                btnOpen.Location = new Point(btnStop.Left - 8 - btnOpen.Width, 48);
                btnDelete.Location = new Point(14, 48);
            }
            else
            {
                int lblDriveW = TextRenderer.MeasureText(lblDrive.Text, lblDrive.Font).Width;
                int lblSizeW = TextRenderer.MeasureText(lblSize.Text, lblSize.Font).Width;

                lblDrive.Location = new Point(14, 13);
                cbDrive.Location = new Point(14 + lblDriveW + 8, 10);
                btnBrowse.Location = new Point(748 - 14 - btnBrowse.Width, 8);
                txtFolder.Location = new Point(cbDrive.Right + 8, 10);
                txtFolder.Width = btnBrowse.Left - 8 - txtFolder.Left;

                lblSize.Location = new Point(14, 54);
                cbMinSize.Location = new Point(14 + lblSizeW + 6, 50);
                btnScan.Location = new Point(cbMinSize.Right + 10, 48);
                btnStop.Location = new Point(btnScan.Right + 8, 48);
                btnOpen.Location = new Point(btnStop.Right + 8, 48);
                btnDelete.Location = new Point(748 - 14 - btnDelete.Width, 48);
            }

            pnlTop.Controls.Add(lblDrive);
            pnlTop.Controls.Add(cbDrive);
            pnlTop.Controls.Add(txtFolder);
            pnlTop.Controls.Add(btnBrowse);
            pnlTop.Controls.Add(lblSize);
            pnlTop.Controls.Add(cbMinSize);
            pnlTop.Controls.Add(btnScan);
            pnlTop.Controls.Add(btnStop);
            pnlTop.Controls.Add(btnOpen);
            pnlTop.Controls.Add(btnDelete);

            var pnlList = UITheme.CreateCardPanel(18, 156, 748, 324);

            lvFiles = new ListView
            {
                Location = new Point(10, 10),
                Size = new Size(728, 302),
                View = View.Details,
                VirtualMode = true,
                FullRowSelect = true,
                GridLines = false,
                BorderStyle = BorderStyle.None,
                Font = UITheme.FontBody,
                BackColor = UITheme.CardBg,
                ForeColor = UITheme.TextPrimary,
                RightToLeft = isAr ? RightToLeft.Yes : RightToLeft.No,
                MultiSelect = true
            };
            UITheme.StyleListView(lvFiles);
            lvFiles.Columns.Add(isAr ? "اسم الملف" : "File Name", 190);
            lvFiles.Columns.Add(isAr ? "حجم الملف" : "File Size", 95);
            lvFiles.Columns.Add(isAr ? "آخر تعديل" : "Last Modified", 115);
            lvFiles.Columns.Add(isAr ? "المسار الكامل" : "Full File Path", 310);

            lvFiles.RetrieveVirtualItem += (s, e) =>
            {
                if (e.ItemIndex < 0 || e.ItemIndex >= largeFiles.Count)
                {
                    e.Item = new ListViewItem(string.Empty);
                    return;
                }
                var f = largeFiles[e.ItemIndex];
                var lvi = new ListViewItem(f.FileName);
                lvi.SubItems.Add(UITheme.FormatBytes(f.SizeBytes));
                lvi.SubItems.Add(f.LastModified.ToString("yyyy-MM-dd HH:mm"));
                lvi.SubItems.Add(f.FilePath);
                e.Item = lvi;
            };

            lvFiles.DoubleClick += (s, e) => OpenSelectedFile();

            // Context Menu & Multi-Choose Shortcuts
            var ctxLarge = new ContextMenuStrip();
            var miOpen = new ToolStripMenuItem(isAr ? "فتح المجلد في المستكشف (Enter)" : "Open Location in Explorer (Enter)", null, (s, e) => OpenSelectedFile());
            var miCopy = new ToolStripMenuItem(isAr ? "نسخ المسار الكامل (Ctrl+C)" : "Copy Full Path (Ctrl+C)", null, (s, e) => CopySelectedFilePath());
            var miSelectAll = new ToolStripMenuItem(isAr ? "تحديد الكل (Ctrl+A)" : "Select All Rows (Ctrl+A)", null, (s, e) => SelectAllVirtual());
            var miSep = new ToolStripSeparator();
            var miDelete = new ToolStripMenuItem(isAr ? "حذف الملفات المحددة (Delete)" : "Delete Selected File(s) (Delete)", null, async (s, e) => await DeleteSelectedFileAsync()) { ForeColor = UITheme.Danger };
            ctxLarge.Items.AddRange(new ToolStripItem[] { miOpen, miCopy, miSelectAll, miSep, miDelete });
            lvFiles.ContextMenuStrip = ctxLarge;

            lvFiles.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.A)
                {
                    SelectAllVirtual();
                    e.Handled = true;
                }
                else if (e.Control && e.KeyCode == Keys.C)
                {
                    CopySelectedFilePath();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Delete)
                {
                    _ = DeleteSelectedFileAsync();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Enter)
                {
                    OpenSelectedFile();
                    e.Handled = true;
                }
            };

            pnlList.Controls.Add(lvFiles);

            pbScan = new ProgressBar
            {
                Location = new Point(18, 488),
                Size = new Size(580, 10),
                Style = ProgressBarStyle.Marquee,
                Visible = false
            };

            lblStatus = new Label
            {
                Text = isAr ? "حدد الحد الأدنى للحجم واضغط على 'بدء الفحص'." : "Select minimum size threshold and click 'Find Files'.",
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
                btnClose.Location = new Point(662, 496);
            }

            Controls.Add(lblHeader);
            Controls.Add(pnlTop);
            Controls.Add(pnlList);
            Controls.Add(pbScan);
            Controls.Add(lblStatus);
            Controls.Add(btnClose);

            UITheme.StyleToolHeader(this, lblHeader, "Box", UITheme.Success, isAr);
            ResumeLayout(false);
            PerformLayout();
        }

        private bool colsScaled;
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!colsScaled && lvFiles != null)
            {
                colsScaled = true;
                float scale = UITheme.GetDpiScale(lvFiles);
                foreach (ColumnHeader col in lvFiles.Columns)
                    col.Width = (int)Math.Round(col.Width * scale);
            }
        }

        private void SetBusy(bool busy)
        {
            isBusy = busy;
            btnScan.Enabled = !busy;
            btnDelete.Enabled = !busy;
            btnStop.Enabled = busy;
            pbScan.Visible = busy;
        }

        public async Task ScanLargeFilesAsync()
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

            long minBytes = cbMinSize.SelectedIndex switch
            {
                0 => 50L * 1024 * 1024,
                1 => 100L * 1024 * 1024,
                2 => 500L * 1024 * 1024,
                3 => 1024L * 1024 * 1024,
                4 => 5L * 1024 * 1024 * 1024,
                _ => 100L * 1024 * 1024
            };

            SetBusy(true);
            largeFiles.Clear();
            lvFiles.SelectedIndices.Clear();
            lvFiles.VirtualListSize = 0;

            cts = new CancellationTokenSource();
            var progress = new Progress<DiskScanProgress>(p =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                lblStatus.Text = isAr
                    ? $"تم فحص {p.FilesScanned:N0} ملف... تم العثور على {p.MatchesFound:N0} ملف ضخم"
                    : $"Scanned {p.FilesScanned:N0} files... Found {p.MatchesFound:N0} large files | {p.CurrentFolder}";
            });

            try
            {
                lblStatus.Text = isAr ? "جاري البحث بأمان عن الملفات الضخمة..." : "Scanning directory trees safely for large files...";
                var files = await diskTools.FindLargeFilesAsync(folder, minBytes, limit: int.MaxValue, progress, cts.Token);
                if (IsDisposed) return;

                largeFiles.AddRange(files);
                lvFiles.VirtualListSize = largeFiles.Count;
                lvFiles.Invalidate();

                long totalSize = largeFiles.Sum(f => f.SizeBytes);
                string totalFormatted = UITheme.FormatBytes(totalSize);
                lblStatus.Text = isAr
                    ? $"اكتمل الفحص: {largeFiles.Count:N0} ملف ضخم بإجمالي ~{totalFormatted}."
                    : $"Scan complete: Located {largeFiles.Count:N0} large files totaling ~{totalFormatted}.";
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed)
                    lblStatus.Text = isAr ? "تم إيقاف البحث بناءً على طلب المستخدم." : "Search was cancelled by user.";
            }
            catch (Exception ex)
            {
                DiagnosticLogger.LogError(nameof(ToolboxLargeFilesForm), "Large file scan failed", ex);
                if (!IsDisposed)
                    lblStatus.Text = isAr ? $"خطأ أثناء البحث: {ex.Message}" : $"Search error: {ex.Message}";
            }
            finally
            {
                if (!IsDisposed)
                    SetBusy(false);
                cts?.Dispose();
                cts = null;
            }
        }

        private List<LargeFileInfo> GetSelectedFiles()
        {
            var list = new List<LargeFileInfo>();
            foreach (int idx in lvFiles.SelectedIndices)
            {
                if (idx >= 0 && idx < largeFiles.Count)
                    list.Add(largeFiles[idx]);
            }
            return list;
        }

        private void OpenSelectedFile()
        {
            var sel = GetSelectedFiles();
            if (sel.Count == 0) return;
            var f = sel[0];
            if (File.Exists(f.FilePath))
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{f.FilePath}\"") { UseShellExecute = true }); } catch { }
            }
        }

        private async Task DeleteSelectedFileAsync()
        {
            if (isBusy) return;
            bool isAr = LocalizationService.IsArabic;
            var selected = GetSelectedFiles();
            if (selected.Count == 0) return;

            var regular = new List<LargeFileInfo>();
            LargeFileInfo? hiberItem = null;
            LargeFileInfo? pageItem = null;

            foreach (var f in selected)
            {
                string nameLower = f.FileName.Trim().ToLowerInvariant();
                if (nameLower == "hiberfil.sys")
                    hiberItem = f;
                else if (nameLower == "pagefile.sys" || nameLower == "swapfile.sys")
                    pageItem = f;
                else
                    regular.Add(f);
            }

            int deleted = 0;
            long freed = 0;
            var removedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var failures = new List<string>();

            if (regular.Count > 0)
            {
                string prompt = regular.Count == 1
                    ? (isAr ? $"هل أنت متأكد من الحذف النهائي للملف:\n\u200E{regular[0].FilePath}\u200E؟" : $"Are you sure you want to permanently delete:\n{regular[0].FilePath}?")
                    : (isAr ? $"هل أنت متأكد من الحذف النهائي لـ {regular.Count:N0} ملف محدد؟" : $"Are you sure you want to permanently delete {regular.Count:N0} selected files?");

                if (CleanRamBoosterApp.WpfUI.AppDialog.Confirm(this, prompt, isAr ? "تأكيد الحذف" : "Confirm Deletion",
                    CleanRamBoosterApp.WpfUI.AppDialogTone.Warning, isDestructive: true))
                {
                    SetBusy(true);
                    var res = await Task.Run(() =>
                    {
                        int del = 0;
                        long bytes = 0;
                        var delSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        var fails = new List<string>();
                        foreach (var f in regular)
                        {
                            try
                            {
                                if (File.Exists(f.FilePath))
                                {
                                    var attr = File.GetAttributes(f.FilePath);
                                    if ((attr & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != 0)
                                        File.SetAttributes(f.FilePath, FileAttributes.Normal);
                                    File.Delete(f.FilePath);
                                    del++;
                                    bytes += f.SizeBytes;
                                    delSet.Add(f.FilePath);
                                }
                                else
                                {
                                    delSet.Add(f.FilePath);
                                }
                            }
                            catch (Exception ex)
                            {
                                fails.Add($"{f.FileName}: {ex.Message}");
                            }
                        }
                        return (del, bytes, delSet, fails);
                    });
                    if (IsDisposed) return;
                    SetBusy(false);

                    deleted += res.del;
                    freed += res.bytes;
                    foreach (var p in res.delSet) removedPaths.Add(p);
                    failures.AddRange(res.fails);
                }
            }

            if (hiberItem != null)
            {
                string hSize = UITheme.FormatBytes(hiberItem.SizeBytes);
                string hPrompt = isAr
                    ? $"ملف الإسبات ({hiberItem.FileName} • {hSize}) مقفل من نواة ويندوز أثناء تفعيل ميزة الإسبات.\n\nهل تريد إيقاف وضع الإسبات (powercfg /hibernate off) بصلاحيات المسؤول لحذف {hiberItem.FileName} فوراً وتحرير {hSize}؟\n(سيبقى وضع السكون Sleep يعمل بشكل طبيعي)."
                    : $"\"hiberfil.sys\" ({hSize}) is locked by Windows Hibernation while enabled.\n\nDisable Windows Hibernation (powercfg /hibernate off) as Administrator to permanently delete C:\\hiberfil.sys and reclaim {hSize} immediately?\n(Standard Sleep mode still works normally.)";

                if (CleanRamBoosterApp.WpfUI.AppDialog.Confirm(this, hPrompt,
                    isAr ? "إزالة ملف الإسبات (hiberfil.sys)" : "Remove Hibernation File (hiberfil.sys)",
                    CleanRamBoosterApp.WpfUI.AppDialogTone.Question, isDestructive: true))
                {
                    try
                    {
                        bool cmdOk = AdminPrivilegeService.RunProcessAsAdmin(
                            "powercfg.exe",
                            "/hibernate off",
                            10000,
                            out string stdOut,
                            out string stdErr,
                            out _,
                            out bool uacCancelled);

                        if (cmdOk && !File.Exists(hiberItem.FilePath))
                        {
                            deleted++;
                            freed += hiberItem.SizeBytes;
                            removedPaths.Add(hiberItem.FilePath);
                        }
                        else if (uacCancelled)
                        {
                            failures.Add($"{hiberItem.FileName}: {(isAr ? "تم إلغاء طلب صلاحيات المسؤول (UAC)" : "Administrator prompt (UAC) cancelled")}");
                        }
                        else
                        {
                            string errOut = $"{stdErr} {stdOut}".Trim();
                            failures.Add($"{hiberItem.FileName}: {(string.IsNullOrEmpty(errOut) ? "Requires Administrator / powercfg failed" : errOut)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{hiberItem.FileName}: {ex.Message}");
                    }
                }
            }

            if (pageItem != null)
            {
                string pSize = UITheme.FormatBytes(pageItem.SizeBytes);
                string pMsg = isAr
                    ? $"ملف الذاكرة الظاهرية ({pageItem.FileName} • {pSize}) مقفل من نواة نظام ويندوز أثناء التشغيل ولا يمكن حذفه مباشرة حتى بصلاحيات المسؤول.\n\nهل تريد فتح إعدادات الذاكرة الظاهرية في ويندوز لتقليله أو إيقافه (يتم التطبيق بعد إعادة التشغيل)؟"
                    : $"\"{pageItem.FileName}\" ({pSize}) is actively locked by the Windows Virtual Memory kernel while Windows is running and cannot be directly deleted even as Administrator.\n\nOpen Windows Performance / Virtual Memory settings to resize or disable the paging file (takes effect on reboot)?";

                if (CleanRamBoosterApp.WpfUI.AppDialog.Confirm(this, pMsg,
                    isAr ? "ملف محمي من النظام" : "Kernel-Locked Paging File",
                    CleanRamBoosterApp.WpfUI.AppDialogTone.Info))
                {
                    try { Process.Start(new ProcessStartInfo("SystemPropertiesPerformance.exe") { UseShellExecute = true }); } catch { }
                }
            }

            if (removedPaths.Count > 0)
            {
                largeFiles.RemoveAll(f => removedPaths.Contains(f.FilePath));
                lvFiles.SelectedIndices.Clear();
                lvFiles.VirtualListSize = largeFiles.Count;
                lvFiles.Invalidate();
            }

            string freedStr = UITheme.FormatBytes(freed);
            if (deleted > 0 || failures.Count > 0)
            {
                lblStatus.Text = failures.Count > 0
                    ? (isAr ? $"تم حذف {deleted:N0} ملفات (~{freedStr}) • تعذر حذف {failures.Count:N0}." : $"Deleted {deleted:N0} file(s) (~{freedStr}) • Failed: {failures.Count:N0}")
                    : (isAr ? $"تم حذف {deleted:N0} ملفات وتوفير ~{freedStr}." : $"Deleted {deleted:N0} file(s), reclaiming ~{freedStr}.");

                if (failures.Count > 0)
                {
                    string errList = string.Join("\n• ", failures.Take(5));
                    CleanRamBoosterApp.WpfUI.AppDialog.Show(this,
                        isAr ? $"تم حذف {deleted:N0} ملفات (~{freedStr}).\n\nتعذر حذف:\n• {errList}" : $"Deleted {deleted:N0} file(s) (~{freedStr} freed).\n\nCould not delete:\n• {errList}",
                        isAr ? "نتيجة الحذف" : "Delete Result",
                        CleanRamBoosterApp.WpfUI.AppDialogTone.Warning);
                }
                else if (deleted > 0)
                {
                    CleanRamBoosterApp.WpfUI.AppDialog.Show(this,
                        isAr ? $"تم حذف {deleted:N0} ملفات بنجاح (تم توفير ~{freedStr})." : $"Deleted {deleted:N0} file(s) successfully (~{freedStr} freed).",
                        isAr ? "تم بنجاح" : "Success",
                        CleanRamBoosterApp.WpfUI.AppDialogTone.Success);
                }
            }
        }

        private void CopySelectedFilePath()
        {
            var sel = GetSelectedFiles();
            if (sel.Count == 0) return;
            var sb = new StringBuilder();
            foreach (var f in sel)
            {
                sb.AppendLine($"{f.FilePath} ({UITheme.FormatBytes(f.SizeBytes)})");
            }
            try
            {
                Clipboard.SetText(sb.ToString().TrimEnd());
                lblStatus.Text = LocalizationService.IsArabic
                    ? $"تم نسخ {sel.Count:N0} مسار إلى الحافظة."
                    : $"Copied {sel.Count:N0} path(s) to clipboard.";
            }
            catch { }
        }

        private void SelectAllVirtual()
        {
            if (largeFiles.Count == 0 || !lvFiles.IsHandleCreated) return;
            var lvi = new LVITEM { stateMask = LVIS_SELECTED, state = LVIS_SELECTED };
            SendMessage(lvFiles.Handle, LVM_SETITEMSTATE, (IntPtr)(-1), ref lvi);
        }
    }
}
