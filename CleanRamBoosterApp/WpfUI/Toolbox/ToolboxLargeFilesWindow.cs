using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using CleanRamBoosterApp.Services;

namespace CleanRamBoosterApp.WpfUI.Toolbox
{
    public class ToolboxLargeFilesWindow : ToolboxWindowBase
    {
        public sealed class LargeFileRow
        {
            public LargeFileInfo Model { get; init; } = null!;
            public string FileName => Model.FileName;
            public long SizeBytes => Model.SizeBytes;
            public string SizeDisplay { get; init; } = string.Empty;
            public string ModifiedDisplay { get; init; } = string.Empty;
            public string FilePath => Model.FilePath;
        }

        private readonly DiskToolsService diskTools = new();
        private readonly TextBlock lblTitle;
        private readonly TextBlock lblSub;
        private readonly Border cardTop;
        private readonly Border cardGrid;
        private readonly ComboBox cbDrive;
        private readonly TextBox txtFolder;
        private readonly ComboBox cbMinSize;
        private readonly Button btnBrowse;
        private readonly Button btnScan;
        private readonly Button btnStop;
        private readonly Button btnOpen;
        private readonly Button btnDelete;
        private readonly Button btnClose;
        private readonly DataGrid gridFiles;
        private readonly ProgressBar pbScan;
        private readonly TextBlock lblStatus;

        private List<LargeFileRow> rows = new();
        private CancellationTokenSource? cts;
        private bool isBusy;

        public ToolboxLargeFilesWindow()
            : base("Large File Finder - Reclaim Disk Space - PC Boost", "مكتشف الملفات الضخمة - PC Boost", 860, 580, resizable: true)
        {
            MinWidth = 760;
            MinHeight = 500;
            bool isAr = LocalizationService.IsArabic;

            var root = new Grid { Margin = new Thickness(20, 18, 20, 16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Controls
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Grid
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer

            var header = BuildHeader("Box", WpfTheme.ColorSuccess,
                isAr ? "مكتشف الملفات الضخمة ومستهلكات التخزين" : "Large File Finder",
                isAr ? "تحديد وحذف الملفات الضخمة المستهلكة لمساحة القرص بسرعة فائقة وبلا حد." : "Locate and reclaim massive storage hogs across drives with unlimited results and smart Windows file safety.",
                out lblTitle, out lblSub);
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            cardTop = CreateCard(new Thickness(14, 12, 14, 12));
            cardTop.Margin = new Thickness(0, 0, 0, 12);
            var topStack = new StackPanel();

            // Row 1: Drive + Path + Browse
            var pathRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            pathRow.Children.Add(new TextBlock
            {
                Text = isAr ? "المسار / القرص:" : "Drive / Path:",
                FontSize = 12.5,
                Foreground = WpfTheme.TextPrimaryBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            cbDrive = new ComboBox { Width = 72, Height = 32, FontSize = 12.5, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), FlowDirection = FlowDirection.LeftToRight };
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady)) cbDrive.Items.Add(d.Name);
            txtFolder = CreatePathTextBox(@"C:\");
            txtFolder.Margin = new Thickness(0, 0, 8, 0);
            if (cbDrive.Items.Count > 0)
            {
                cbDrive.SelectedIndex = 0;
                txtFolder.Text = cbDrive.SelectedItem?.ToString() ?? @"C:\";
            }
            cbDrive.SelectionChanged += (_, _) =>
            {
                if (cbDrive.SelectedItem != null) txtFolder.Text = cbDrive.SelectedItem.ToString() ?? @"C:\";
            };
            Grid.SetColumn(cbDrive, 1);
            pathRow.Children.Add(cbDrive);

            Grid.SetColumn(txtFolder, 2);
            pathRow.Children.Add(txtFolder);

            btnBrowse = CreateSecondaryButton(null, isAr ? "استعراض..." : "Browse...", 32);
            btnBrowse.MinWidth = 96;
            btnBrowse.Click += (_, _) =>
            {
                string? chosen = PickFolder(txtFolder.Text.Trim());
                if (!string.IsNullOrEmpty(chosen)) txtFolder.Text = chosen;
            };
            Grid.SetColumn(btnBrowse, 3);
            pathRow.Children.Add(btnBrowse);
            topStack.Children.Add(pathRow);

            // Row 2: Min Size + Actions
            var actGrid = new Grid();
            actGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftBar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            leftBar.Children.Add(new TextBlock
            {
                Text = isAr ? "أدنى حجم:" : "Min Size:",
                FontSize = 12.5,
                Foreground = WpfTheme.TextPrimaryBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            cbMinSize = new ComboBox { Width = isAr ? 148 : 118, Height = 32, FontSize = 12.5, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            string[] sizes = isAr
                ? new[] { "أكبر من 50 MB", "أكبر من 100 MB", "أكبر من 500 MB", "أكبر من 1 GB", "أكبر من 5 GB" }
                : new[] { "> 50 MB", "> 100 MB", "> 500 MB", "> 1 GB", "> 5 GB" };
            foreach (var s in sizes) cbMinSize.Items.Add(s);
            cbMinSize.SelectedIndex = 1;
            leftBar.Children.Add(cbMinSize);

            btnScan = CreatePrimaryButton("SearchFiles", isAr ? "بدء الفحص" : "Find Files", 34);
            btnScan.Margin = new Thickness(0, 0, 8, 0);
            btnScan.Click += async (_, _) => await ScanLargeFilesAsync();

            btnStop = CreateSecondaryButton("Stop", isAr ? "إيقاف" : "Stop", 34);
            btnStop.Margin = new Thickness(0, 0, 8, 0);
            btnStop.IsEnabled = false;
            btnStop.Click += (_, _) => cts?.Cancel();

            btnOpen = CreateSecondaryButton("Folder", isAr ? "فتح المجلد" : "Open Folder", 34);
            btnOpen.Click += (_, _) => OpenSelectedFile();

            leftBar.Children.Add(btnScan);
            leftBar.Children.Add(btnStop);
            leftBar.Children.Add(btnOpen);
            Grid.SetColumn(leftBar, 0);
            actGrid.Children.Add(leftBar);

            btnDelete = CreateDangerButton("Trash", isAr ? "حذف الملف" : "Delete File", 34);
            btnDelete.Click += async (_, _) => await DeleteSelectedFilesAsync();
            Grid.SetColumn(btnDelete, 2);
            actGrid.Children.Add(btnDelete);

            topStack.Children.Add(actGrid);
            cardTop.Child = topStack;
            Grid.SetRow(cardTop, 1);
            root.Children.Add(cardTop);

            // Virtualized DataGrid Card
            cardGrid = CreateCard(new Thickness(0));
            cardGrid.ClipToBounds = true;

            gridFiles = CreateVirtualDataGrid();
            gridFiles.Columns.Add(new DataGridTextColumn
            {
                Header = isAr ? "اسم الملف" : "File Name",
                Binding = new Binding(nameof(LargeFileRow.FileName)),
                Width = new DataGridLength(200),
                ElementStyle = CreateEllipsisTextStyle(forceLtr: true)
            });
            gridFiles.Columns.Add(new DataGridTextColumn
            {
                Header = isAr ? "حجم الملف" : "File Size",
                Binding = new Binding(nameof(LargeFileRow.SizeDisplay)),
                SortMemberPath = nameof(LargeFileRow.SizeBytes),
                Width = new DataGridLength(96),
                ElementStyle = CreateEllipsisTextStyle(forceLtr: true)
            });
            gridFiles.Columns.Add(new DataGridTextColumn
            {
                Header = isAr ? "آخر تعديل" : "Last Modified",
                Binding = new Binding(nameof(LargeFileRow.ModifiedDisplay)),
                Width = new DataGridLength(128),
                ElementStyle = CreateEllipsisTextStyle(forceLtr: true)
            });
            gridFiles.Columns.Add(new DataGridTextColumn
            {
                Header = isAr ? "المسار الكامل" : "Full File Path",
                Binding = new Binding(nameof(LargeFileRow.FilePath)),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                ElementStyle = CreateEllipsisTextStyle(forceLtr: true)
            });

            var ctx = new ContextMenu();
            var miOpen = new MenuItem { Header = isAr ? "فتح المجلد في المستكشف (Enter)" : "Open Location in Explorer (Enter)" };
            miOpen.Click += (_, _) => OpenSelectedFile();
            var miCopy = new MenuItem { Header = isAr ? "نسخ المسار الكامل (Ctrl+C)" : "Copy Full Path (Ctrl+C)" };
            miCopy.Click += (_, _) => CopySelectedPaths();
            var miSelAll = new MenuItem { Header = isAr ? "تحديد الكل (Ctrl+A)" : "Select All Rows (Ctrl+A)" };
            miSelAll.Click += (_, _) => gridFiles.SelectAll();
            var miDel = new MenuItem { Header = isAr ? "حذف الملفات المحددة (Delete)" : "Delete Selected File(s) (Delete)", Foreground = WpfTheme.DangerBrush };
            miDel.Click += async (_, _) => await DeleteSelectedFilesAsync();
            ctx.Items.Add(miOpen);
            ctx.Items.Add(miCopy);
            ctx.Items.Add(miSelAll);
            ctx.Items.Add(new Separator());
            ctx.Items.Add(miDel);
            gridFiles.ContextMenu = ctx;

            gridFiles.MouseDoubleClick += (_, _) => OpenSelectedFile();
            gridFiles.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { OpenSelectedFile(); e.Handled = true; }
                else if (e.Key == Key.Delete) { _ = DeleteSelectedFilesAsync(); e.Handled = true; }
                else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { CopySelectedPaths(); e.Handled = true; }
            };

            cardGrid.Child = gridFiles;
            Grid.SetRow(cardGrid, 2);
            root.Children.Add(cardGrid);

            // Footer
            var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var statusStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            pbScan = new ProgressBar
            {
                Height = 4,
                IsIndeterminate = true,
                Visibility = Visibility.Collapsed,
                BorderThickness = new Thickness(0),
                Foreground = WpfTheme.PrimaryBrush,
                Background = WpfTheme.BorderBrush,
                Margin = new Thickness(0, 0, 0, 6)
            };
            lblStatus = new TextBlock
            {
                Text = isAr ? "حدد الحد الأدنى للحجم واضغط على 'بدء الفحص'." : "Select minimum size threshold and click 'Find Files'.",
                FontSize = 12,
                Foreground = WpfTheme.TextSecondaryBrush,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            statusStack.Children.Add(pbScan);
            statusStack.Children.Add(lblStatus);
            Grid.SetColumn(statusStack, 0);
            footer.Children.Add(statusStack);

            btnClose = CreateSecondaryButton(null, isAr ? "إغلاق" : "Close", 34);
            btnClose.MinWidth = 96;
            btnClose.Click += (_, _) => { cts?.Cancel(); Close(); };
            Grid.SetColumn(btnClose, 1);
            footer.Children.Add(btnClose);

            Grid.SetRow(footer, 3);
            root.Children.Add(footer);

            Content = root;
            Closing += (_, _) => cts?.Cancel();
        }

        public bool IsVirtualized => VirtualizingPanel.GetIsVirtualizing(gridFiles) && gridFiles.EnableRowVirtualization;

        private void SetBusy(bool busy)
        {
            isBusy = busy;
            btnScan.IsEnabled = !busy;
            btnBrowse.IsEnabled = !busy;
            btnDelete.IsEnabled = !busy;
            btnStop.IsEnabled = busy;
            pbScan.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }

        public async Task ScanLargeFilesAsync()
        {
            if (isBusy) return;
            bool isAr = LocalizationService.IsArabic;
            string folder = txtFolder.Text.Trim();
            if (!Directory.Exists(folder))
            {
                AppDialog.Show(this,
                    isAr ? "المجلد المحدد غير موجود." : "Selected directory does not exist.",
                    isAr ? "مسار غير صالح" : "Invalid Path",
                    AppDialogTone.Warning);
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
            rows = new List<LargeFileRow>();
            gridFiles.ItemsSource = null;
            cts = new CancellationTokenSource();

            var progress = new Progress<DiskScanProgress>(p =>
            {
                lblStatus.Text = isAr
                    ? $"تم فحص {p.FilesScanned:N0} ملف... تم العثور على {p.MatchesFound:N0} ملف ضخم"
                    : $"Scanned {p.FilesScanned:N0} files... Found {p.MatchesFound:N0} large files | {p.CurrentFolder}";
            });

            try
            {
                lblStatus.Text = isAr ? "جاري البحث بأمان عن الملفات الضخمة..." : "Scanning directory trees safely for large files...";
                var files = await diskTools.FindLargeFilesAsync(folder, minBytes, limit: int.MaxValue, progress, cts.Token);

                rows = files.Select(f => new LargeFileRow
                {
                    Model = f,
                    SizeDisplay = WpfTheme.FormatBytes(f.SizeBytes),
                    ModifiedDisplay = f.LastModified.ToString("yyyy-MM-dd HH:mm")
                }).ToList();
                gridFiles.ItemsSource = rows;

                long totalSize = files.Sum(f => f.SizeBytes);
                lblStatus.Text = isAr
                    ? $"اكتمل الفحص: {rows.Count:N0} ملف ضخم بإجمالي ~{WpfTheme.FormatBytes(totalSize)}."
                    : $"Scan complete: Located {rows.Count:N0} large files totaling ~{WpfTheme.FormatBytes(totalSize)}.";
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = isAr ? "تم إيقاف البحث بناءً على طلب المستخدم." : "Search was cancelled by user.";
            }
            catch (Exception ex)
            {
                DiagnosticLogger.LogError(nameof(ToolboxLargeFilesWindow), "Large files scan failed", ex);
                lblStatus.Text = isAr ? $"خطأ أثناء البحث: {ex.Message}" : $"Search error: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
                cts?.Dispose();
                cts = null;
            }
        }

        private void OpenSelectedFile()
        {
            var first = gridFiles.SelectedItems.OfType<LargeFileRow>().FirstOrDefault();
            if (first != null && File.Exists(first.FilePath))
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{first.FilePath}\"") { UseShellExecute = true }); } catch { }
            }
        }

        private async Task DeleteSelectedFilesAsync()
        {
            if (isBusy) return;
            bool isAr = LocalizationService.IsArabic;
            var selected = gridFiles.SelectedItems.OfType<LargeFileRow>().Select(r => r.Model).ToList();
            if (selected.Count == 0) return;

            var regular = new List<LargeFileInfo>();
            LargeFileInfo? hiberItem = null;
            LargeFileInfo? pageItem = null;
            foreach (var f in selected)
            {
                string low = f.FileName.Trim().ToLowerInvariant();
                if (low == "hiberfil.sys") hiberItem = f;
                else if (low == "pagefile.sys" || low == "swapfile.sys") pageItem = f;
                else regular.Add(f);
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

                if (AppDialog.Confirm(this, prompt, isAr ? "تأكيد الحذف" : "Confirm Deletion", AppDialogTone.Warning, isDestructive: true))
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
                                }
                                delSet.Add(f.FilePath);
                            }
                            catch (Exception ex) { fails.Add($"{f.FileName}: {ex.Message}"); }
                        }
                        return (del, bytes, delSet, fails);
                    });
                    SetBusy(false);

                    deleted += res.del;
                    freed += res.bytes;
                    foreach (var p in res.delSet) removedPaths.Add(p);
                    failures.AddRange(res.fails);
                }
            }

            if (hiberItem != null)
            {
                string hSize = WpfTheme.FormatBytes(hiberItem.SizeBytes);
                string hPrompt = isAr
                    ? $"ملف الإسبات ({hiberItem.FileName} • {hSize}) مقفل من نواة ويندوز.\n\nهل تريد إيقاف وضع الإسبات (powercfg /hibernate off) بصلاحيات المسؤول لحذفه وتحرير {hSize}؟"
                    : $"\"hiberfil.sys\" ({hSize}) is locked by Windows Hibernation while enabled.\n\nDisable Hibernation (powercfg /hibernate off) as Administrator to remove it and reclaim {hSize} immediately?";

                if (AppDialog.Confirm(this, hPrompt, isAr ? "إزالة ملف الإسبات" : "Remove Hibernation File", AppDialogTone.Question, isDestructive: true))
                {
                    bool cmdOk = AdminPrivilegeService.RunProcessAsAdmin("powercfg.exe", "/hibernate off", 10000, out string stdOut, out string stdErr, out _, out bool uacCancelled);
                    if (cmdOk && !File.Exists(hiberItem.FilePath))
                    {
                        deleted++;
                        freed += hiberItem.SizeBytes;
                        removedPaths.Add(hiberItem.FilePath);
                    }
                    else if (uacCancelled)
                    {
                        failures.Add($"{hiberItem.FileName}: {(isAr ? "تم إلغاء طلب صلاحيات المسؤول" : "UAC cancelled")}");
                    }
                    else
                    {
                        string errOut = $"{stdErr} {stdOut}".Trim();
                        failures.Add($"{hiberItem.FileName}: {(string.IsNullOrEmpty(errOut) ? "powercfg failed" : errOut)}");
                    }
                }
            }

            if (pageItem != null)
            {
                string pSize = WpfTheme.FormatBytes(pageItem.SizeBytes);
                string pMsg = isAr
                    ? $"ملف الذاكرة الظاهرية ({pageItem.FileName} • {pSize}) مقفل من نواة نظام ويندوز.\n\nهل تريد فتح إعدادات الذاكرة الظاهرية لتقليله أو إيقافه؟"
                    : $"\"{pageItem.FileName}\" ({pSize}) is actively locked by the Windows kernel.\n\nOpen Windows Performance / Virtual Memory settings to resize or disable it?";
                if (AppDialog.Confirm(this, pMsg, isAr ? "ملف محمي من النظام" : "Kernel-Locked Paging File", AppDialogTone.Info))
                {
                    try { Process.Start(new ProcessStartInfo("SystemPropertiesPerformance.exe") { UseShellExecute = true }); } catch { }
                }
            }

            if (removedPaths.Count > 0)
            {
                rows = rows.Where(r => !removedPaths.Contains(r.FilePath)).ToList();
                gridFiles.ItemsSource = rows;
            }

            string freedStr = WpfTheme.FormatBytes(freed);
            if (deleted > 0 || failures.Count > 0)
            {
                lblStatus.Text = failures.Count > 0
                    ? (isAr ? $"تم حذف {deleted:N0} ملفات (~{freedStr}) • تعذر حذف {failures.Count:N0}." : $"Deleted {deleted:N0} file(s) (~{freedStr}) • Failed: {failures.Count:N0}")
                    : (isAr ? $"تم حذف {deleted:N0} ملفات وتوفير ~{freedStr}." : $"Deleted {deleted:N0} file(s), reclaiming ~{freedStr}.");

                if (failures.Count > 0)
                {
                    AppDialog.Show(this,
                        isAr ? $"تم حذف {deleted:N0} ملفات (~{freedStr}).\n\nتعذر حذف:\n• {string.Join("\n• ", failures.Take(5))}" : $"Deleted {deleted:N0} file(s) (~{freedStr} freed).\n\nCould not delete:\n• {string.Join("\n• ", failures.Take(5))}",
                        isAr ? "نتيجة الحذف" : "Delete Result",
                        AppDialogTone.Warning);
                }
                else if (deleted > 0)
                {
                    AppDialog.Show(this,
                        isAr ? $"تم حذف {deleted:N0} ملفات بنجاح (تم توفير ~{freedStr})." : $"Deleted {deleted:N0} file(s) successfully (~{freedStr} freed).",
                        isAr ? "تم بنجاح" : "Success",
                        AppDialogTone.Success);
                }
            }
        }

        private void CopySelectedPaths()
        {
            var sel = gridFiles.SelectedItems.OfType<LargeFileRow>().ToList();
            if (sel.Count == 0) return;
            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, sel.Select(s => $"{s.FilePath} ({s.SizeDisplay})")));
                lblStatus.Text = LocalizationService.IsArabic ? $"تم نسخ {sel.Count:N0} مسار إلى الحافظة." : $"Copied {sel.Count:N0} path(s) to clipboard.";
            }
            catch { }
        }

        public override void RefreshTheme()
        {
            base.RefreshTheme();
            lblTitle.Foreground = WpfTheme.TextPrimaryBrush;
            lblSub.Foreground = WpfTheme.TextSecondaryBrush;
            lblStatus.Foreground = WpfTheme.TextSecondaryBrush;
            cardTop.Background = WpfTheme.CardBrush;
            cardTop.BorderBrush = WpfTheme.BorderBrush;
            cardGrid.Background = WpfTheme.CardBrush;
            cardGrid.BorderBrush = WpfTheme.BorderBrush;
            txtFolder.Background = WpfTheme.CardSubBrush;
            txtFolder.Foreground = WpfTheme.TextPrimaryBrush;
            txtFolder.BorderBrush = WpfTheme.BorderBrush;
            StyleDataGridForTheme(gridFiles);
            ApplySecondaryButtonStyle(btnBrowse);
            ApplySecondaryButtonStyle(btnStop);
            btnStop.Content = WpfIcons.CreateIconTextContent("Stop", LocalizationService.IsArabic ? "إيقاف" : "Stop", btnStop.Foreground, 14, 6);
            ApplySecondaryButtonStyle(btnOpen);
            btnOpen.Content = WpfIcons.CreateIconTextContent("Folder", LocalizationService.IsArabic ? "فتح المجلد" : "Open Folder", btnOpen.Foreground, 14, 6);
            ApplySecondaryButtonStyle(btnClose);
        }
    }
}
