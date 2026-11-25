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
    public class ToolboxEmptyFoldersWindow : ToolboxWindowBase
    {
        public sealed class EmptyFolderRow
        {
            public string FolderName { get; init; } = string.Empty;
            public string FullPath { get; init; } = string.Empty;
        }

        private readonly DiskToolsService diskTools = new();
        private readonly TextBlock lblTitle;
        private readonly TextBlock lblSub;
        private readonly Border cardTop;
        private readonly Border cardGrid;
        private readonly TextBox txtFolder;
        private readonly Button btnBrowse;
        private readonly Button btnScan;
        private readonly Button btnStop;
        private readonly Button btnClean;
        private readonly Button btnClose;
        private readonly DataGrid gridFolders;
        private readonly ProgressBar pbScan;
        private readonly TextBlock lblStatus;

        private List<EmptyFolderRow> rows = new();
        private string lastScanRoot = string.Empty;
        private CancellationTokenSource? activeCts;
        private bool isBusy;

        public ToolboxEmptyFoldersWindow()
            : base("Empty Folder Scanner - PC Boost", "فاحص المجلدات الفارغة - PC Boost", 800, 565, resizable: true)
        {
            MinWidth = 700;
            MinHeight = 480;
            bool isAr = LocalizationService.IsArabic;

            var root = new Grid { Margin = new Thickness(20, 18, 20, 16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Controls
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Grid
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer

            var header = BuildHeader("Folder", WpfTheme.ColorSuccess,
                isAr ? "فاحص ومزيل المجلدات الفارغة المهجورة" : "Empty Folder Scanner",
                isAr ? "بحث متعمق وسريع بدون تجميد لعرض وحذف كافة المجلدات الفارغة بلا حد." : "Fast, non-blocking deep scan showing every abandoned empty folder with no item limit.",
                out lblTitle, out lblSub);
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // Top Controls Card
            cardTop = CreateCard(new Thickness(14, 12, 14, 12));
            cardTop.Margin = new Thickness(0, 0, 0, 12);
            var topStack = new StackPanel();

            var dirRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            dirRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dirRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dirRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var lblDir = new TextBlock
            {
                Text = isAr ? "المجلد:" : "Directory:",
                FontSize = 12.5,
                Foreground = WpfTheme.TextPrimaryBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(lblDir, 0);
            dirRow.Children.Add(lblDir);

            txtFolder = CreatePathTextBox(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            txtFolder.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(txtFolder, 1);
            dirRow.Children.Add(txtFolder);

            btnBrowse = CreateSecondaryButton(null, isAr ? "استعراض..." : "Browse...", 32);
            btnBrowse.MinWidth = 96;
            btnBrowse.Click += (_, _) =>
            {
                string? chosen = PickFolder(txtFolder.Text.Trim());
                if (!string.IsNullOrEmpty(chosen)) txtFolder.Text = chosen;
            };
            Grid.SetColumn(btnBrowse, 2);
            dirRow.Children.Add(btnBrowse);
            topStack.Children.Add(dirRow);

            var actionBar = new StackPanel { Orientation = Orientation.Horizontal };
            btnScan = CreatePrimaryButton("SearchFiles", isAr ? "فحص المجلدات" : "Scan Folders", 34);
            btnScan.Margin = new Thickness(0, 0, 8, 0);
            btnScan.Click += async (_, _) => await ScanEmptyFoldersAsync();

            btnStop = CreateSecondaryButton("Stop", isAr ? "إيقاف" : "Stop", 34);
            btnStop.Margin = new Thickness(0, 0, 8, 0);
            btnStop.IsEnabled = false;
            btnStop.Click += (_, _) => activeCts?.Cancel();

            btnClean = CreateDangerButton("Trash", isAr ? "حذف المجلدات الفارغة" : "Delete All Empty", 34);
            btnClean.Click += async (_, _) => await CleanAllFoldersAsync();

            actionBar.Children.Add(btnScan);
            actionBar.Children.Add(btnStop);
            actionBar.Children.Add(btnClean);
            topStack.Children.Add(actionBar);

            cardTop.Child = topStack;
            Grid.SetRow(cardTop, 1);
            root.Children.Add(cardTop);

            // Virtualized DataGrid Card
            cardGrid = CreateCard(new Thickness(0));
            cardGrid.ClipToBounds = true;

            gridFolders = CreateVirtualDataGrid();
            gridFolders.Columns.Add(new DataGridTextColumn
            {
                Header = isAr ? "اسم المجلد" : "Folder Name",
                Binding = new Binding(nameof(EmptyFolderRow.FolderName)),
                Width = new DataGridLength(220),
                ElementStyle = CreateEllipsisTextStyle(forceLtr: true)
            });
            gridFolders.Columns.Add(new DataGridTextColumn
            {
                Header = isAr ? "المسار الكامل" : "Full Directory Path",
                Binding = new Binding(nameof(EmptyFolderRow.FullPath)),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                ElementStyle = CreateEllipsisTextStyle(forceLtr: true)
            });

            var ctx = new ContextMenu();
            var miOpen = new MenuItem { Header = isAr ? "فتح المجلد في المستكشف (Enter)" : "Open Folder in Explorer (Enter)" };
            miOpen.Click += (_, _) => OpenSelectedFolder();
            var miCopy = new MenuItem { Header = isAr ? "نسخ المسار الكامل (Ctrl+C)" : "Copy Path (Ctrl+C)" };
            miCopy.Click += (_, _) => CopySelectedPaths();
            var miSelAll = new MenuItem { Header = isAr ? "تحديد الكل (Ctrl+A)" : "Select All Rows (Ctrl+A)" };
            miSelAll.Click += (_, _) => gridFolders.SelectAll();
            var miDel = new MenuItem { Header = isAr ? "حذف المجلدات المحددة (Delete)" : "Delete Selected Folder(s) (Delete)", Foreground = WpfTheme.DangerBrush };
            miDel.Click += async (_, _) => await DeleteSelectedFoldersAsync();
            ctx.Items.Add(miOpen);
            ctx.Items.Add(miCopy);
            ctx.Items.Add(miSelAll);
            ctx.Items.Add(new Separator());
            ctx.Items.Add(miDel);
            gridFolders.ContextMenu = ctx;

            gridFolders.MouseDoubleClick += (_, _) => OpenSelectedFolder();
            gridFolders.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { OpenSelectedFolder(); e.Handled = true; }
                else if (e.Key == Key.Delete) { _ = DeleteSelectedFoldersAsync(); e.Handled = true; }
                else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { CopySelectedPaths(); e.Handled = true; }
            };

            cardGrid.Child = gridFolders;
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
                Text = isAr ? "حدد المجلد المطلوب واضغط على 'فحص المجلدات' للبحث عن المجلدات الفارغة." : "Select directory and click 'Scan Folders' to find all empty folders.",
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
            btnClose.Click += (_, _) => { activeCts?.Cancel(); Close(); };
            Grid.SetColumn(btnClose, 1);
            footer.Children.Add(btnClose);

            Grid.SetRow(footer, 3);
            root.Children.Add(footer);

            Content = root;
            Closing += (_, _) => activeCts?.Cancel();
        }

        public bool IsVirtualized => VirtualizingPanel.GetIsVirtualizing(gridFolders) && gridFolders.EnableRowVirtualization;
        public int DisplayedCount => rows.Count;

        public void SetTargetDirectory(string path) => txtFolder.Text = path;

        private void SetBusyState(bool busy)
        {
            isBusy = busy;
            btnScan.IsEnabled = !busy;
            btnBrowse.IsEnabled = !busy;
            btnClean.IsEnabled = !busy;
            btnStop.IsEnabled = busy;
            pbScan.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }

        public async Task ScanEmptyFoldersAsync()
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

            SetBusyState(true);
            rows = new List<EmptyFolderRow>();
            gridFolders.ItemsSource = null;
            lastScanRoot = Path.GetFullPath(folder);

            lblStatus.Text = isAr ? "جاري فحص المجلدات الفرعية للبحث عن المجلدات الفارغة..." : "Scanning directory trees safely for all empty folders...";

            activeCts?.Dispose();
            activeCts = new CancellationTokenSource();
            var ct = activeCts.Token;

            var progress = new Progress<DiskScanProgress>(p =>
            {
                lblStatus.Text = isAr
                    ? $"تم فحص {p.FilesScanned:N0} مجلد • تم العثور على {p.MatchesFound:N0} مجلد فارغ"
                    : $"Scanned {p.FilesScanned:N0} folders • Found {p.MatchesFound:N0} empty | {p.CurrentFolder}";
            });

            try
            {
                var empties = await diskTools.FindEmptyFoldersAsync(lastScanRoot, progress, ct);
                rows = empties.Select(p => new EmptyFolderRow
                {
                    FolderName = Path.GetFileName(p),
                    FullPath = p
                }).ToList();
                gridFolders.ItemsSource = rows;

                lblStatus.Text = ct.IsCancellationRequested
                    ? (isAr ? $"تم إيقاف الفحص: تم عرض {rows.Count:N0} مجلد فارغ تم العثور عليها حتى الآن." : $"Scan stopped: showing {rows.Count:N0} empty folder(s) found so far.")
                    : (isAr ? $"تم العثور على {rows.Count:N0} مجلدات فارغة مهجورة (عرض الكل بلا حد)." : $"Found {rows.Count:N0} abandoned empty folder(s) (all shown, no limit).");
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = isAr ? "تم إيقاف الفحص بناءً على طلب المستخدم." : "Scan was stopped by user.";
            }
            catch (Exception ex)
            {
                DiagnosticLogger.LogError(nameof(ToolboxEmptyFoldersWindow), "Empty folder scan failed", ex);
                lblStatus.Text = isAr ? $"خطأ أثناء الفحص: {ex.Message}" : $"Scan error: {ex.Message}";
            }
            finally
            {
                SetBusyState(false);
                activeCts?.Dispose();
                activeCts = null;
            }
        }

        private async Task CleanAllFoldersAsync()
        {
            if (isBusy) return;
            bool isAr = LocalizationService.IsArabic;
            if (rows.Count == 0)
            {
                AppDialog.Show(this,
                    isAr ? "لم يتم العثور على مجلدات فارغة لتنظيفها." : "No empty folders found to clean.",
                    isAr ? "تنبيه" : "Notice",
                    AppDialogTone.Info);
                return;
            }

            if (!AppDialog.Confirm(this,
                isAr ? $"هل أنت متأكد من رغبتك في حذف {rows.Count:N0} مجلداً فارغاً؟" : $"Are you sure you want to delete all {rows.Count:N0} empty folder(s)?",
                isAr ? "تأكيد التنظيف" : "Confirm Clean",
                AppDialogTone.Warning,
                isDestructive: true)) return;

            SetBusyState(true);
            lblStatus.Text = isAr ? "جاري حذف المجلدات الفارغة..." : "Removing empty folders...";

            var snapshot = rows.Select(r => r.FullPath).OrderByDescending(p => p.Length).ToList();
            string scanRoot = lastScanRoot;
            activeCts?.Dispose();
            activeCts = new CancellationTokenSource();
            var ct = activeCts.Token;

            var progress = new Progress<(int done, int total, int deleted)>(p =>
            {
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

                        if (deletedPaths.Contains(path) || gonePaths.Contains(path)) { }
                        else if (!Directory.Exists(path)) gonePaths.Add(path);
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
                                else break;
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

                rows = rows.Where(r => !result.deletedPaths.Contains(r.FullPath) && !result.gonePaths.Contains(r.FullPath) && Directory.Exists(r.FullPath)).ToList();
                gridFolders.ItemsSource = rows;

                int totalDeleted = result.deletedPaths.Count;
                if (result.cancelled)
                {
                    lblStatus.Text = isAr ? $"تم إيقاف الحذف: تم حذف {totalDeleted:N0} مجلد فارغ." : $"Cleanup stopped: deleted {totalDeleted:N0} empty folder(s).";
                }
                else if (result.failedCount > 0)
                {
                    lblStatus.Text = isAr
                        ? $"تم حذف {totalDeleted:N0} مجلداً • تعذر حذف {result.failedCount:N0} (مقفل أو محمي)."
                        : $"Deleted {totalDeleted:N0} empty folder(s) • Skipped {result.failedCount:N0} locked/protected folder(s).";
                    AppDialog.Show(this,
                        isAr ? $"تم حذف {totalDeleted:N0} مجلداً فارغاً.\nتعذر حذف {result.failedCount:N0} مجلدات:\n{result.firstErr}" : $"Deleted {totalDeleted:N0} empty folder(s).\nCould not delete {result.failedCount:N0} folder(s):\n{result.firstErr}",
                        isAr ? "نتيجة التنظيف" : "Cleanup Result",
                        totalDeleted > 0 ? AppDialogTone.Info : AppDialogTone.Warning);
                }
                else
                {
                    lblStatus.Text = isAr ? $"تم تنظيف {totalDeleted:N0} مجلداً فارغاً بنجاح." : $"Successfully pruned {totalDeleted:N0} empty folder(s).";
                    AppDialog.Show(this,
                        isAr ? $"تم حذف {totalDeleted:N0} مجلداً فارغاً بنجاح." : $"Deleted {totalDeleted:N0} empty folder(s).",
                        isAr ? "تم بنجاح" : "Success",
                        AppDialogTone.Success);
                }
            }
            finally
            {
                SetBusyState(false);
                activeCts?.Dispose();
                activeCts = null;
            }
        }

        private async Task DeleteSelectedFoldersAsync()
        {
            if (isBusy) return;
            bool isAr = LocalizationService.IsArabic;
            var sel = gridFolders.SelectedItems.OfType<EmptyFolderRow>().Select(r => r.FullPath).ToList();
            if (sel.Count == 0) return;

            if (!AppDialog.Confirm(this,
                isAr ? $"هل أنت متأكد من رغبتك في حذف {sel.Count:N0} مجلداً محدداً نهائياً؟" : $"Are you sure you want to permanently delete {sel.Count:N0} selected empty folder(s)?",
                isAr ? "تأكيد الحذف" : "Confirm Deletion",
                AppDialogTone.Warning,
                isDestructive: true)) return;

            SetBusyState(true);
            var ordered = sel.OrderByDescending(p => p.Length).ToList();
            try
            {
                var res = await Task.Run(() =>
                {
                    var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    int failed = 0;
                    string? firstErr = null;
                    foreach (var p in ordered)
                    {
                        if (diskTools.DeleteEmptyFolder(p, out string? err)) removed.Add(p);
                        else { failed++; firstErr ??= $"{p}: {err}"; }
                    }
                    return (removed, failed, firstErr);
                });

                rows = rows.Where(r => !res.removed.Contains(r.FullPath) && Directory.Exists(r.FullPath)).ToList();
                gridFolders.ItemsSource = rows;
                lblStatus.Text = res.failed > 0
                    ? (isAr ? $"تم حذف {res.removed.Count:N0} مجلداً • تعذر حذف {res.failed:N0}." : $"Deleted {res.removed.Count:N0} empty folder(s) • Failed {res.failed:N0}: {res.firstErr}")
                    : (isAr ? $"تم حذف {res.removed.Count:N0} مجلداً فارغاً." : $"Deleted {res.removed.Count:N0} empty folder(s).");
            }
            finally
            {
                SetBusyState(false);
            }
        }

        private void OpenSelectedFolder()
        {
            var first = gridFolders.SelectedItems.OfType<EmptyFolderRow>().FirstOrDefault();
            if (first != null && Directory.Exists(first.FullPath))
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{first.FullPath}\"") { UseShellExecute = true }); } catch { }
            }
        }

        private void CopySelectedPaths()
        {
            var sel = gridFolders.SelectedItems.OfType<EmptyFolderRow>().ToList();
            if (sel.Count == 0) return;
            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, sel.Select(s => s.FullPath)));
                lblStatus.Text = LocalizationService.IsArabic
                    ? $"تم نسخ {sel.Count:N0} مسار إلى الحافظة."
                    : $"Copied {sel.Count:N0} folder path(s) to clipboard.";
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
            StyleDataGridForTheme(gridFolders);
            ApplySecondaryButtonStyle(btnBrowse);
            ApplySecondaryButtonStyle(btnStop);
            btnStop.Content = WpfIcons.CreateIconTextContent("Stop", LocalizationService.IsArabic ? "إيقاف" : "Stop", btnStop.Foreground, 14, 6);
            ApplySecondaryButtonStyle(btnClose);
        }
    }
}
