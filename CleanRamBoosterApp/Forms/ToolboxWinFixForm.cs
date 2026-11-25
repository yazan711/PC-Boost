using System;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using CleanRamBoosterApp.Services;
using CleanRamBoosterApp.UI;

namespace CleanRamBoosterApp.Forms
{
    public class ToolboxWinFixForm : Form
    {
        private readonly WinFixService winFix = new();
        private readonly List<WinFixItem> allFixes;
        private readonly ComboBox cbCategory;
        private readonly ListView lvFixes;
        private readonly Label lblStatus;
        private readonly Button btnFixSelected;
        private bool isPopulatingFixes;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
                return cp;
            }
        }

        public ToolboxWinFixForm()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;

            bool isAr = LocalizationService.IsArabic;
            Text = isAr ? "مركز إصلاح مشاكل ويندوز - PC Boost" : "Win Fix - All-In-One Windows Glitch Repair Center";
            ClientSize = new Size(784, 542);
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

            allFixes = winFix.GetAllFixes();

            var lblHeader = new Label
            {
                Text = isAr ? "مركز إصلاح أعطال ويندوز بنقرة واحدة" : "Windows 1-Click Repair Center",
                Font = UITheme.FontHeader,
                ForeColor = UITheme.TextPrimary
            };

            var pnlTop = UITheme.CreateCardPanel(18, 56, 748, 48);

            var lblCat = new Label { Text = isAr ? "التصنيف:" : "Category:", AutoSize = true, Font = UITheme.FontBody, ForeColor = UITheme.TextPrimary };
            cbCategory = new ComboBox
            {
                Width = isAr ? 148 : 155
            };
            UITheme.StyleComboBox(cbCategory);
            cbCategory.Items.AddRange(isAr 
                ? new object[] { "جميع التصنيفات", "سطح المكتب والمستكشف", "الشبكة والإنترنت", "النظام والأمان", "اقتران الملفات" }
                : new object[] { "All Categories", "Desktop & Explorer", "Network & Internet", "System & Security", "File Associations" });
            cbCategory.SelectedIndex = 0;
            cbCategory.SelectedIndexChanged += (s, e) => FilterFixes();

            var lnkCheckAll = new LinkLabel
            {
                Text = isAr ? "تحديد الكل" : "Select All",
                AutoSize = true,
                Font = UITheme.FontSmall
            };
            UITheme.StyleLinkLabel(lnkCheckAll);
            lnkCheckAll.LinkClicked += (s, e) => SetAllChecks(true);

            var lnkUncheckAll = new LinkLabel
            {
                Text = isAr ? "إلغاء التحديد" : "Uncheck All",
                AutoSize = true,
                Font = UITheme.FontSmall
            };
            UITheme.StyleLinkLabel(lnkUncheckAll);
            lnkUncheckAll.LinkClicked += (s, e) => SetAllChecks(false);

            var btnRollback = new Button
            {
                Text = isAr ? "التراجع" : "Undo Fixes",
                Size = new Size(isAr ? 108 : 124, 32),
                UseMnemonic = false
            };
            UITheme.StyleSecondaryButton(btnRollback);
            UITheme.SetButtonIcon(btnRollback, "Undo", UITheme.TextPrimary, 15);
            btnRollback.Click += (s, e) => PromptRollback();

            btnFixSelected = new Button
            {
                Text = isAr ? "إصلاح المحدد" : "Fix Selected",
                Size = new Size(isAr ? 136 : 150, 32),
                UseMnemonic = false
            };
            UITheme.StyleButton(btnFixSelected, UITheme.Primary, Color.White, UITheme.PrimaryHover);
            UITheme.SetButtonIcon(btnFixSelected, "Wrench", Color.White, 15);
            btnFixSelected.Click += async (s, e) => await FixSelectedAsync();

            if (isAr)
            {
                int lblCatW = TextRenderer.MeasureText(lblCat.Text, lblCat.Font).Width;
                int lnkCheckW = TextRenderer.MeasureText(lnkCheckAll.Text, lnkCheckAll.Font).Width;
                int lnkUncheckW = TextRenderer.MeasureText(lnkUncheckAll.Text, lnkUncheckAll.Font).Width;

                lblCat.Location = new Point(748 - 14 - lblCatW, 14);
                cbCategory.Location = new Point(lblCat.Left - 8 - cbCategory.Width, 11);
                lnkCheckAll.Location = new Point(cbCategory.Left - 14 - lnkCheckW, 14);
                lnkUncheckAll.Location = new Point(lnkCheckAll.Left - 12 - lnkUncheckW, 14);
                btnFixSelected.Location = new Point(14, 8);
                btnRollback.Location = new Point(btnFixSelected.Right + 8, 8);
            }
            else
            {
                lblCat.Location = new Point(14, 14);
                cbCategory.Location = new Point(84, 11);
                lnkCheckAll.Location = new Point(252, 14);
                lnkUncheckAll.Location = new Point(324, 14);
                btnRollback.Location = new Point(748 - 14 - 150 - 8 - 124, 8);
                btnFixSelected.Location = new Point(748 - 14 - 150, 8);
            }

            pnlTop.Controls.Add(lblCat);
            pnlTop.Controls.Add(cbCategory);
            pnlTop.Controls.Add(lnkCheckAll);
            pnlTop.Controls.Add(lnkUncheckAll);
            pnlTop.Controls.Add(btnRollback);
            pnlTop.Controls.Add(btnFixSelected);

            var pnlList = UITheme.CreateCardPanel(18, 104, 748, 385);

            lvFixes = new ListView
            {
                Location = new Point(10, 10),
                Size = new Size(728, 365),
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                GridLines = false,
                BorderStyle = BorderStyle.None,
                Font = UITheme.FontBody,
                RightToLeft = isAr ? RightToLeft.Yes : RightToLeft.No,
                MultiSelect = true
            };
            UITheme.StyleListView(lvFixes);
            lvFixes.Columns.Add(isAr ? "مجال الإصلاح / المشكلة" : "Repair / Glitch Area", 258);
            lvFixes.Columns.Add(isAr ? "التصنيف" : "Category", 156);
            lvFixes.Columns.Add(isAr ? "الوصف والحل" : "Description & Solution", 290);

            // Context Menu & Multi-Choose Shortcuts
            var ctxFixes = new ContextMenuStrip();
            var miFix = new ToolStripMenuItem(isAr ? "إصلاح المحدد (Enter)" : "Fix Selected (Enter)", null, async (s, e) => await FixSelectedAsync());
            var miCheckSel = new ToolStripMenuItem(isAr ? "تحديد المظلل (Space)" : "Check Selected (Space)", null, (s, e) => SetSelectedChecks(true));
            var miUncheckSel = new ToolStripMenuItem(isAr ? "إلغاء تحديد المظلل" : "Uncheck Selected", null, (s, e) => SetSelectedChecks(false));
            var miSep = new ToolStripSeparator();
            var miSelectAll = new ToolStripMenuItem(isAr ? "تحديد الكل (Ctrl+A)" : "Select All (Ctrl+A)", null, (s, e) => SelectAllListView(lvFixes));
            var miCopy = new ToolStripMenuItem(isAr ? "نسخ التفاصيل (Ctrl+C)" : "Copy Fix Details (Ctrl+C)", null, (s, e) => CopyFixDetails());
            ctxFixes.Items.AddRange(new ToolStripItem[] { miFix, miCheckSel, miUncheckSel, miSep, miSelectAll, miCopy });
            lvFixes.ContextMenuStrip = ctxFixes;

            lvFixes.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.A)
                {
                    SelectAllListView(lvFixes);
                    e.Handled = true;
                }
                else if (e.Control && e.KeyCode == Keys.C)
                {
                    CopyFixDetails();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Space)
                {
                    ToggleSelectedChecks(lvFixes);
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Enter)
                {
                    _ = FixSelectedAsync();
                    e.Handled = true;
                }
            };

            lvFixes.ItemChecked += (s, e) =>
            {
                if (!isPopulatingFixes && e.Item?.Tag is WinFixItem fix)
                {
                    fix.IsSelected = e.Item.Checked;
                }
            };

            pnlList.Controls.Add(lvFixes);

            lblStatus = new Label
            {
                Text = isAr ? $"يتوفر {allFixes.Count} إصلاحاً لمشاكل وأعطال ويندوز." : $"Available {allFixes.Count} Windows repairs matching Advanced SystemCare Win Fix.",
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
            btnClose.Click += (s, e) => Close();

            if (isAr)
            {
                btnClose.Location = new Point(18, 498);
                lblStatus.Location = new Point(18 + btnClose.Width + 12, 504);
            }
            else
            {
                lblStatus.Location = new Point(20, 504);
                btnClose.Location = new Point(662, 498);
            }

            Controls.Add(lblHeader);
            Controls.Add(pnlTop);
            Controls.Add(pnlList);
            Controls.Add(lblStatus);
            Controls.Add(btnClose);

            UITheme.StyleToolHeader(this, lblHeader, "Wrench", UITheme.Primary, isAr);
            ResumeLayout(false);
            PerformLayout();

            FilterFixes();
        }

        private bool colsScaled;
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!colsScaled && lvFixes != null)
            {
                colsScaled = true;
                float scale = UITheme.GetDpiScale(lvFixes);
                foreach (ColumnHeader col in lvFixes.Columns)
                    col.Width = (int)Math.Round(col.Width * scale);
            }
        }

        private void FilterFixes()
        {
            isPopulatingFixes = true;
            lvFixes.BeginUpdate();
            try
            {
                lvFixes.Items.Clear();
                bool isAr = LocalizationService.IsArabic;
                int selIdx = cbCategory.SelectedIndex;

                IEnumerable<WinFixItem> items;
                if (selIdx <= 0)
                {
                    items = allFixes;
                }
                else
                {
                    string[] internalCats = { "", "Desktop & Explorer", "Network & Internet", "System & Security", "File Associations" };
                    string targetCat = (selIdx < internalCats.Length) ? internalCats[selIdx] : "";
                    items = allFixes.Where(x => x.Category.Equals(targetCat, StringComparison.OrdinalIgnoreCase));
                }

                foreach (var fix in items)
                {
                    string title = isAr && !string.IsNullOrEmpty(fix.TitleAr) ? fix.TitleAr : fix.Title;
                    string desc = isAr && !string.IsNullOrEmpty(fix.DescriptionAr) ? fix.DescriptionAr : fix.Description;
                    var lvi = new ListViewItem(title) { Checked = fix.IsSelected, Tag = fix };
                    string catDisplay = fix.Category;
                    if (isAr)
                    {
                        if (catDisplay == "Desktop & Explorer") catDisplay = "سطح المكتب والمستكشف";
                        else if (catDisplay == "Network & Internet") catDisplay = "الشبكة والإنترنت";
                        else if (catDisplay == "System & Security") catDisplay = "النظام والأمان";
                        else if (catDisplay == "File Associations") catDisplay = "اقتران الملفات";
                    }
                    lvi.SubItems.Add(catDisplay);
                    lvi.SubItems.Add(desc);
                    lvFixes.Items.Add(lvi);
                }
            }
            finally
            {
                lvFixes.EndUpdate();
                isPopulatingFixes = false;
            }
        }

        private void SetAllChecks(bool isChecked)
        {
            foreach (ListViewItem lvi in lvFixes.Items)
            {
                lvi.Checked = isChecked;
                if (lvi.Tag is WinFixItem fix) fix.IsSelected = isChecked;
            }
        }

        private async System.Threading.Tasks.Task FixSelectedAsync()
        {
            bool isAr = LocalizationService.IsArabic;
            if (lvFixes.CheckedItems.Count == 0)
            {
                CleanRamBoosterApp.WpfUI.AppDialog.Show(this,
                    isAr ? "يرجى تحديد الإصلاحات المراد تطبيقها." : "Please check the repairs you want to apply.",
                    isAr ? "تنبيه" : "Notice",
                    CleanRamBoosterApp.WpfUI.AppDialogTone.Info);
                return;
            }

            if (!AdminPrivilegeService.EnsureAdminOrPrompt(this, "Win Fix", "مركز إصلاح مشاكل ويندوز"))
                return;

            var checkedEntries = lvFixes.CheckedItems.Cast<ListViewItem>()
                .Where(lvi => lvi.Tag is WinFixItem)
                .Select(lvi => (ItemView: lvi, Fix: (WinFixItem)lvi.Tag!))
                .ToList();
            if (checkedEntries.Count == 0) return;

            int fixedCount = 0;
            var failures = new List<string>();
            btnFixSelected.Enabled = false;
            Cursor = Cursors.WaitCursor;

            try
            {
                // 1. Automatic Safety Net: System Restore Point & Registry Snapshot
                lblStatus.Text = isAr
                    ? "جاري إنشاء نقطة استعادة للنظام ونسخة احتياطية للسجل قبل الإصلاح..."
                    : "Creating System Restore Point & Registry Snapshot before repairs...";

                var (restorePointOk, backupPath) = await System.Threading.Tasks.Task.Run(() =>
                {
                    bool rpOk = winFix.CreateSystemRestorePoint();
                    string bp = winFix.CreateRegistryBackup();
                    return (rpOk, bp);
                });

                int idx = 0;
                foreach (var entry in checkedEntries)
                {
                    idx++;
                    string title = isAr && !string.IsNullOrEmpty(entry.Fix.TitleAr) ? entry.Fix.TitleAr : entry.Fix.Title;
                    lblStatus.Text = isAr
                        ? $"جاري تطبيق ({idx}/{checkedEntries.Count}): {title}..."
                        : $"Applying ({idx}/{checkedEntries.Count}): {title}...";

                    var (ok, err) = await System.Threading.Tasks.Task.Run(() => winFix.ExecuteFix(entry.Fix));
                    if (ok)
                    {
                        fixedCount++;
                        entry.ItemView.SubItems[0].Text = "✓ " + title;
                        entry.ItemView.ForeColor = UITheme.SuccessHover;
                    }
                    else
                    {
                        entry.ItemView.SubItems[0].Text = "✕ " + title;
                        entry.ItemView.ForeColor = UITheme.Danger;
                        failures.Add($"{title}: {err ?? "Failed"}");
                    }
                }

                string backupNotice = !string.IsNullOrEmpty(backupPath)
                    ? (isAr ? $"\nلقطة السجل الاحتياطية: \u200E{Path.GetFileName(backupPath)}\u200E" : $"\nRegistry Snapshot: {Path.GetFileName(backupPath)}")
                    : "";
                string rpStatus = isAr ? (restorePointOk ? "تم الإنشاء" : "محمي") : (restorePointOk ? "Created" : "Protected");

                if (failures.Count == 0)
                {
                    lblStatus.Text = isAr ? $"تم إكمال {fixedCount} إصلاحات وحفظ النسخة الاحتياطية بأمان." : $"Completed {fixedCount} repairs. Backup saved safely.";
                    CleanRamBoosterApp.WpfUI.AppDialog.Show(this,
                        isAr ? $"تم تطبيق {fixedCount} إصلاحات بنجاح!{backupNotice}\n\nنقطة استعادة النظام: {rpStatus}" : $"Completed {fixedCount} Windows repairs successfully!{backupNotice}\n\nSystem Restore Point: {rpStatus}",
                        isAr ? "اكتمل الإصلاح" : "Win Fix Finished",
                        CleanRamBoosterApp.WpfUI.AppDialogTone.Success);
                }
                else
                {
                    lblStatus.Text = isAr
                        ? $"اكتمل {fixedCount} إصلاحات • تعذر تطبيق {failures.Count}."
                        : $"Completed {fixedCount} repair(s) • Failed: {failures.Count}.";
                    string errList = string.Join("\n• ", failures.Take(5));
                    CleanRamBoosterApp.WpfUI.AppDialog.Show(this,
                        isAr
                            ? $"تم تطبيق {fixedCount} إصلاحات، وتعذر إكمال {failures.Count}:{backupNotice}\n\n• {errList}"
                            : $"Completed {fixedCount} repair(s), but {failures.Count} encountered an issue:{backupNotice}\n\n• {errList}",
                        isAr ? "نتيجة الإصلاح" : "Win Fix Result",
                        CleanRamBoosterApp.WpfUI.AppDialogTone.Warning);
                }
            }
            finally
            {
                Cursor = Cursors.Default;
                btnFixSelected.Enabled = true;
            }
        }

        private void PromptRollback()
        {
            bool isAr = LocalizationService.IsArabic;
            var backups = winFix.GetAvailableBackups();
            if (backups.Count == 0)
            {
                CleanRamBoosterApp.WpfUI.AppDialog.Show(this,
                    isAr ? "لا توجد نسخ احتياطية سابقة للسجل لاستعادتها." : "No previous registry snapshots found to restore.",
                    isAr ? "ملاحظة الاستعادة" : "Restore Notice",
                    CleanRamBoosterApp.WpfUI.AppDialogTone.Info);
                return;
            }

            string latest = backups[0];
            string filename = Path.GetFileName(latest);
            DateTime created = File.GetCreationTime(latest);

            string confirmText = isAr
                ? $"هل تريد استعادة أحدث نسخة احتياطية للسجل؟\n\nالملف: \u200E{filename}\u200E\nالتاريخ: \u200E{created:G}\u200E\n\nسيؤدي هذا إلى استرجاع حالة السجل قبل الإصلاحات."
                : $"Do you want to restore the most recent registry backup?\n\nFile: {filename}\nCreated: {created:G}\n\nThis will restore the registry state prior to repairs.";
            if (CleanRamBoosterApp.WpfUI.AppDialog.Confirm(this, confirmText, isAr ? "تأكيد الاستعادة" : "Confirm Rollback", CleanRamBoosterApp.WpfUI.AppDialogTone.Question))
            {
                if (!AdminPrivilegeService.EnsureAdminOrPrompt(this, "Win Fix Rollback", "استعادة نسخة السجل الاحتياطية"))
                    return;
                Cursor = Cursors.WaitCursor;
                bool ok = winFix.RestoreRegistryBackup(latest);
                Cursor = Cursors.Default;

                if (ok)
                {
                    CleanRamBoosterApp.WpfUI.AppDialog.Show(this,
                        isAr ? "تمت استعادة حالة السجل من النسخة الاحتياطية بنجاح!" : "Registry state successfully restored from snapshot!",
                        isAr ? "تمت الاستعادة" : "Restored",
                        CleanRamBoosterApp.WpfUI.AppDialogTone.Success);
                    lblStatus.Text = isAr ? $"تمت استعادة النسخة: \u200E{filename}\u200E" : $"Restored backup: {filename}";
                }
                else
                {
                    CleanRamBoosterApp.WpfUI.AppDialog.Show(this,
                        isAr ? "تعذرت استعادة نسخة السجل الاحتياطية." : "Failed to restore registry snapshot.",
                        isAr ? "خطأ" : "Error",
                        CleanRamBoosterApp.WpfUI.AppDialogTone.Error);
                }
            }
        }

        private void SetSelectedChecks(bool check)
        {
            foreach (ListViewItem item in lvFixes.SelectedItems)
            {
                item.Checked = check;
            }
        }

        private static void ToggleSelectedChecks(ListView lv)
        {
            if (lv.SelectedItems.Count == 0) return;
            bool target = !lv.SelectedItems[0].Checked;
            foreach (ListViewItem item in lv.SelectedItems)
            {
                item.Checked = target;
            }
        }

        private static void SelectAllListView(ListView lv)
        {
            lv.BeginUpdate();
            try
            {
                foreach (ListViewItem item in lv.Items)
                {
                    item.Selected = true;
                }
            }
            finally
            {
                lv.EndUpdate();
            }
        }

        private void CopyFixDetails()
        {
            if (lvFixes.SelectedItems.Count == 0) return;
            var sb = new StringBuilder();
            foreach (ListViewItem item in lvFixes.SelectedItems)
            {
                sb.AppendLine($"{item.SubItems[0].Text} | {item.SubItems[1].Text} | {item.SubItems[2].Text}");
            }
            try { Clipboard.SetText(sb.ToString()); } catch { }
        }
    }
}
