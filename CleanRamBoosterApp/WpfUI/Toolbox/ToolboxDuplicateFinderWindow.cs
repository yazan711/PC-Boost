using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    public class ToolboxDuplicateFinderWindow : ToolboxWindowBase
    {
        public sealed class DuplicateRow : INotifyPropertyChanged
        {
            private bool isChecked;
            public event PropertyChangedEventHandler? PropertyChanged;

            public string FilePath { get; init; } = string.Empty;
            public string FileName { get; init; } = string.Empty;
            public long FileSize { get; init; }
            public string SizeText { get; init; } = string.Empty;
            public string FullHash { get; init; } = string.Empty;
            public string ShortHash { get; init; } = string.Empty;
            public bool IsOriginal { get; init; }

            public bool IsChecked
            {
                get => isChecked;
                set
                {
                    if (isChecked != value)
                    {
                        isChecked = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
                    }
                }
            }
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
        private readonly Button btnDeleteSelected;
        private readonly Button btnSmartSelect;
        private readonly Button btnClose;
        private readonly DataGrid gridDups;
        private readonly ProgressBar pbScan;
        private readonly TextBlock lblStatus;

        private List<DuplicateRow> rows = new();
        private CancellationTokenSource? cts;
        private bool isBusy;

        public ToolboxDuplicateFinderWindow()
            : base("Duplicate File Finder - PC Boost", "مكتشف الملفات المكررة - PC Boost", 840, 580, resizable: true)
        {
            MinWidth = 740;
            MinHeight = 500;
            bool isAr = LocalizationService.IsArabic;

            var root = new Grid { Margin = new Thickness(20, 18, 20, 16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Controls
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Grid
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer

            var header = BuildHeader("SearchFiles", WpfTheme.ColorSuccess,
                isAr ? "مكتشف ومزيل الملفات المكررة" : "Duplicate File Finder",
                isAr ? "فحص سريع بالحجم وبصمة التحقق (MD5) مع تحديد ذكي لإبقاء نسخة أصلية واحدة." : "Find and reclaim identical duplicate files by exact size & head/tail MD5 hash with zero UI freeze.",
                out lblTitle, out lblSub);
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            cardTop = CreateCard(new Thickness(14, 12, 14, 12));
            cardTop.Margin = new Thickness(0, 0, 0, 12);
            var topStack = new StackPanel();

            var dirRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            dirRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dirRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dirRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            dirRow.Children.Add(new TextBlock
            {
                Text = isAr ? "المجلد:" : "Directory:",
                FontSize = 12.5,
                Foreground = WpfTheme.TextPrimaryBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            });

            txtFolder = CreatePathTextBox(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
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

            var actGrid = new Grid();
            actGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftActs = new StackPanel { Orientation = Orientation.Horizontal };
            btnScan = CreatePrimaryButton("SearchFiles", isAr ? "بدء الفحص" : "Start Scan", 34);
            btnScan.Margin = new Thickness(0, 0, 8, 0);
            btnScan.Click += async (_, _) => await ScanFolderAsync();

            btnStop = CreateSecondaryButton("Stop", isAr ? "إيقاف" : "Stop", 34);
            btnStop.Margin = new Thickness(0, 0, 8, 0);
            btnStop.IsEnabled = false;
            btnStop.Click += (_, _) => cts?.Cancel();

            btnDeleteSelected = CreateDangerButton("Trash", isAr ? "حذف المكررات المحددة" : "Delete Checked Duplicates", 34);
            btnDeleteSelected.Click += async (_, _) => await DeleteCheckedDuplicatesAsync();

            leftActs.Children.Add(btnScan);
            leftActs.Children.Add(btnStop);
            leftActs.Children.Add(btnDeleteSelected);
            Grid.SetColumn(leftActs, 0);
            actGrid.Children.Add(leftActs);

            btnSmartSelect = CreateSecondaryButton("Lightning", isAr ? "تحديد ذكي" : "Auto-Select", 34);
            btnSmartSelect.Click += (_, _) => SmartSelectKeepOne();
            Grid.SetColumn(btnSmartSelect, 2);
            actGrid.Children.Add(btnSmartSelect);

            topStack.Children.Add(actGrid);
            cardTop.Child = topStack;
            Grid.SetRow(cardTop, 1);
            root.Children.Add(cardTop);

            // Virtualized DataGrid Card
            cardGrid = CreateCard(new Thickness(0));
            cardGrid.ClipToBounds = true;

            gridDups = CreateVirtualDataGrid();
            gridDups.IsReadOnly = false;
            gridDups.RowHeight = 33;

            var chkFactory = new FrameworkElementFactory(typeof(CheckBox));
            chkFactory.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(nameof(DuplicateRow.IsChecked))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            chkFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            chkFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            chkFactory.SetValue(Control.PaddingProperty, new Thickness(0));

            gridDups.Columns.Add(new DataGridTemplateColumn
            {
                Header = "",
                Width = new DataGridLength(42),
                CanUserResize = false,
                CellTemplate = new DataTemplate { VisualTree = chkFactory }
            });

            var nameStyle = CreateEllipsisTextStyle(forceLtr: true);
            var origTrigger = new DataTrigger { Binding = new Binding(nameof(DuplicateRow.IsOriginal)), Value = true };
            origTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, WpfTheme.PrimaryBrush));
            origTrigger.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
            nameStyle.Triggers.Add(origTrigger);

            gridDups.Columns.Add(new DataGridTextColumn
            {
                Header = isAr ? "اسم الملف" : "File Name",
                Binding = new Binding(nameof(DuplicateRow.FileName)),
                Width = new DataGridLength(195),
                IsReadOnly = true,
                ElementStyle = nameStyle
            });
            gridDups.Columns.Add(new DataGridTextColumn
            {
                Header = isAr ? "الحجم" : "Size",
                Binding = new Binding(nameof(DuplicateRow.SizeText)),
                SortMemberPath = nameof(DuplicateRow.FileSize),
                Width = new DataGridLength(88),
                IsReadOnly = true,
                ElementStyle = CreateEllipsisTextStyle(forceLtr: true)
            });
            gridDups.Columns.Add(new DataGridTextColumn
            {
                Header = isAr ? "بصمة MD5" : "MD5 Hash",
                Binding = new Binding(nameof(DuplicateRow.ShortHash)),
                Width = new DataGridLength(116),
                IsReadOnly = true,
                ElementStyle = CreateEllipsisTextStyle(forceLtr: true)
            });
            gridDups.Columns.Add(new DataGridTextColumn
            {
                Header = isAr ? "المسار الكامل" : "Full File Path",
                Binding = new Binding(nameof(DuplicateRow.FilePath)),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                IsReadOnly = true,
                ElementStyle = CreateEllipsisTextStyle(forceLtr: true)
            });

            gridDups.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler((_, e) =>
            {
                if (e.OriginalSource is CheckBox)
                    Dispatcher.BeginInvoke(new Action(UpdateCheckedStatusSummary));
            }));

            var ctx = new ContextMenu();
            var miChk = new MenuItem { Header = isAr ? "تحديد المظلل (Space)" : "Check Selected (Space)" };
            miChk.Click += (_, _) => SetSelectedChecks(true);
            var miUnchk = new MenuItem { Header = isAr ? "إلغاء تحديد المظلل" : "Uncheck Selected" };
            miUnchk.Click += (_, _) => SetSelectedChecks(false);
            var miKeep = new MenuItem { Header = isAr ? "تحديد ذكي (إبقاء نسخة أصلية)" : "Smart Select (Keep 1 Original per Group)" };
            miKeep.Click += (_, _) => SmartSelectKeepOne();
            var miOpen = new MenuItem { Header = isAr ? "فتح المسار في المستكشف (Enter)" : "Open Location in Explorer (Enter)" };
            miOpen.Click += (_, _) => OpenSelectedLocation();
            var miCopy = new MenuItem { Header = isAr ? "نسخ المسار الكامل (Ctrl+C)" : "Copy Full Path (Ctrl+C)" };
            miCopy.Click += (_, _) => CopySelectedPaths();
            var miDel = new MenuItem { Header = isAr ? "حذف الملفات المحددة (Delete)" : "Delete Checked Duplicates (Delete)", Foreground = WpfTheme.DangerBrush };
            miDel.Click += async (_, _) => await DeleteCheckedDuplicatesAsync();
            ctx.Items.Add(miChk);
            ctx.Items.Add(miUnchk);
            ctx.Items.Add(miKeep);
            ctx.Items.Add(new Separator());
            ctx.Items.Add(miOpen);
            ctx.Items.Add(miCopy);
            ctx.Items.Add(new Separator());
            ctx.Items.Add(miDel);
            gridDups.ContextMenu = ctx;

            gridDups.MouseDoubleClick += (_, _) => OpenSelectedLocation();
            gridDups.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Space) { ToggleSelectedChecks(); e.Handled = true; }
                else if (e.Key == Key.Delete) { _ = DeleteCheckedDuplicatesAsync(); e.Handled = true; }
                else if (e.Key == Key.Enter) { OpenSelectedLocation(); e.Handled = true; }
                else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { CopySelectedPaths(); e.Handled = true; }
            };

            cardGrid.Child = gridDups;
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
                Text = isAr ? "حدد المجلد المطلوب واضغط على 'بدء الفحص' للبحث عن الملفات المتطابقة." : "Select directory and click 'Start Scan' to search for identical duplicate files.",
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

        public bool IsVirtualized => VirtualizingPanel.GetIsVirtualizing(gridDups) && gridDups.EnableRowVirtualization;

        private void SetBusy(bool busy)
        {
            isBusy = busy;
            btnScan.IsEnabled = !busy;
            btnBrowse.IsEnabled = !busy;
            btnDeleteSelected.IsEnabled = !busy;
            btnSmartSelect.IsEnabled = !busy;
            btnStop.IsEnabled = busy;
            pbScan.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }

        public async Task ScanFolderAsync()
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

            SetBusy(true);
            rows = new List<DuplicateRow>();
            gridDups.ItemsSource = null;
            cts = new CancellationTokenSource();

            var progress = new Progress<DiskScanProgress>(p =>
            {
                lblStatus.Text = isAr
                    ? $"تم فحص {p.FilesScanned:N0} ملف... تم العثور على {p.MatchesFound:N0} مكرر"
                    : $"Scanned {p.FilesScanned:N0} files... Found {p.MatchesFound:N0} duplicates | {p.CurrentFolder}";
            });

            try
            {
                lblStatus.Text = isAr ? "جاري فحص المجلدات بأمان..." : "Traversing directory trees safely...";
                var groups = await diskTools.FindDuplicatesAsync(folder, progress, cts.Token, maxFiles: int.MaxValue);

                int totalDuplicates = 0;
                long wasteBytes = 0;
                var built = new List<DuplicateRow>();
                foreach (var g in groups)
                {
                    string sizeText = WpfTheme.FormatBytes(g.FileSize);
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
                        if (!isFirst) { totalDuplicates++; wasteBytes += g.FileSize; }
                        isFirst = false;
                    }
                }

                rows = built;
                gridDups.ItemsSource = rows;

                string wasteStr = WpfTheme.FormatBytes(wasteBytes);
                lblStatus.Text = isAr
                    ? $"اكتمل الفحص: {totalDuplicates:N0} ملف مكرر ضمن {groups.Count:N0} مجموعات (~{wasteStr} قابلة للاستعادة)."
                    : $"Scan finished: Located {totalDuplicates:N0} duplicate copies across {groups.Count:N0} groups (~{wasteStr} reclaimable).";
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = isAr ? "تم إيقاف الفحص بناءً على طلب المستخدم." : "Scan was cancelled by user.";
            }
            catch (Exception ex)
            {
                DiagnosticLogger.LogError(nameof(ToolboxDuplicateFinderWindow), "Duplicate scan failed", ex);
                lblStatus.Text = isAr ? $"خطأ أثناء الفحص: {ex.Message}" : $"Scan error: {ex.Message}";
            }
            finally
            {
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
                AppDialog.Show(this,
                    isAr ? "يرجى تحديد الملفات المكررة المراد حذفها." : "Please check the duplicate files you want to delete.",
                    isAr ? "تنبيه" : "Notice",
                    AppDialogTone.Info);
                return;
            }

            if (!AppDialog.Confirm(this,
                isAr ? $"هل أنت متأكد من الحذف النهائي لـ {targets.Count:N0} ملف محدد؟" : $"Are you sure you want to permanently delete {targets.Count:N0} checked files?",
                isAr ? "تأكيد الحذف" : "Confirm Deletion",
                AppDialogTone.Warning,
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
                        }
                        delPaths.Add(row.FilePath);
                    }
                    catch (Exception ex)
                    {
                        fails.Add($"{row.FileName}: {ex.Message}");
                    }
                }
                return (delPaths, delCount, freedBytes, fails);
            });

            rows = rows.Where(r => !deletedSet.Contains(r.FilePath)).ToList();
            gridDups.ItemsSource = rows;
            SetBusy(false);

            string freedStr = WpfTheme.FormatBytes(freed);
            if (failures.Count == 0)
            {
                lblStatus.Text = isAr ? $"تم حذف {deleted:N0} ملف مكرر بنجاح. تم توفير ~{freedStr}." : $"Successfully deleted {deleted:N0} duplicate files. Reclaimed ~{freedStr}.";
                AppDialog.Show(this,
                    isAr ? $"تم حذف {deleted:N0} نسخة مكررة، وتم توفير {freedStr}." : $"Deleted {deleted:N0} duplicates, reclaiming {freedStr}.",
                    isAr ? "تم بنجاح" : "Success",
                    AppDialogTone.Success);
            }
            else
            {
                lblStatus.Text = isAr ? $"تم حذف {deleted:N0} ملفات (~{freedStr}) • تعذر حذف {failures.Count:N0}." : $"Deleted {deleted:N0} duplicate(s) (~{freedStr}) • Failed: {failures.Count:N0}.";
                string errList = string.Join("\n• ", failures.Take(5));
                AppDialog.Show(this,
                    isAr ? $"تم حذف {deleted:N0} نسخة مكررة (~{freedStr}).\n\nتعذر حذف {failures.Count:N0} ملفات:\n• {errList}" : $"Deleted {deleted:N0} duplicate(s) (~{freedStr} freed).\n\nCould not delete {failures.Count:N0} file(s):\n• {errList}",
                    isAr ? "نتيجة الحذف" : "Delete Result",
                    AppDialogTone.Warning);
            }
        }

        private void UpdateCheckedStatusSummary()
        {
            int checkedCount = 0;
            long checkedBytes = 0;
            foreach (var r in rows)
            {
                if (r.IsChecked) { checkedCount++; checkedBytes += r.FileSize; }
            }
            lblStatus.Text = LocalizationService.IsArabic
                ? $"المحدد للحذف: {checkedCount:N0} من {rows.Count:N0} (~{WpfTheme.FormatBytes(checkedBytes)})"
                : $"Checked for deletion: {checkedCount:N0} of {rows.Count:N0} (~{WpfTheme.FormatBytes(checkedBytes)})";
        }

        private void SetSelectedChecks(bool check)
        {
            foreach (var r in gridDups.SelectedItems.OfType<DuplicateRow>()) r.IsChecked = check;
            UpdateCheckedStatusSummary();
        }

        private void ToggleSelectedChecks()
        {
            var sel = gridDups.SelectedItems.OfType<DuplicateRow>().ToList();
            if (sel.Count == 0) return;
            bool target = !sel[0].IsChecked;
            foreach (var r in sel) r.IsChecked = target;
            UpdateCheckedStatusSummary();
        }

        private void SmartSelectKeepOne()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
            {
                if (!string.IsNullOrEmpty(r.FullHash))
                    r.IsChecked = !seen.Add(r.FullHash);
            }
            lblStatus.Text = LocalizationService.IsArabic
                ? "اكتمل التحديد الذكي: تم إبقاء نسخة أصلية واحدة غير محددة لكل مجموعة مكررة."
                : "Smart selection complete: 1 original kept unchecked per duplicate group.";
        }

        private void OpenSelectedLocation()
        {
            var first = gridDups.SelectedItems.OfType<DuplicateRow>().FirstOrDefault();
            if (first != null && File.Exists(first.FilePath))
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{first.FilePath}\"") { UseShellExecute = true }); } catch { }
            }
        }

        private void CopySelectedPaths()
        {
            var sel = gridDups.SelectedItems.OfType<DuplicateRow>().ToList();
            if (sel.Count == 0) return;
            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, sel.Select(s => s.FilePath)));
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
            StyleDataGridForTheme(gridDups);
            ApplySecondaryButtonStyle(btnBrowse);
            ApplySecondaryButtonStyle(btnStop);
            btnStop.Content = WpfIcons.CreateIconTextContent("Stop", LocalizationService.IsArabic ? "إيقاف" : "Stop", btnStop.Foreground, 14, 6);
            ApplySecondaryButtonStyle(btnSmartSelect);
            btnSmartSelect.Content = WpfIcons.CreateIconTextContent("Lightning", LocalizationService.IsArabic ? "تحديد ذكي" : "Auto-Select", btnSmartSelect.Foreground, 14, 6);
            ApplySecondaryButtonStyle(btnClose);
        }
    }
}
