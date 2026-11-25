using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using CleanRamBoosterApp.Services;

namespace CleanRamBoosterApp.WpfUI.Toolbox
{
    public class ToolboxWinFixWindow : ToolboxWindowBase
    {
        private sealed class WinFixRow : INotifyPropertyChanged
        {
            private bool isSelected;
            private string statusPrefix = string.Empty;
            private bool hasSucceeded;
            private bool hasFailed;

            public event PropertyChangedEventHandler? PropertyChanged;

            public WinFixItem Model { get; init; } = null!;
            public string RawTitle { get; init; } = string.Empty;
            public string CategoryDisplay { get; init; } = string.Empty;
            public string DescriptionDisplay { get; init; } = string.Empty;

            public string TitleDisplay => string.IsNullOrEmpty(statusPrefix) ? RawTitle : $"{statusPrefix} {RawTitle}";

            public bool IsSelected
            {
                get => isSelected;
                set
                {
                    if (isSelected != value)
                    {
                        isSelected = value;
                        Model.IsSelected = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                    }
                }
            }

            public bool HasSucceeded => hasSucceeded;
            public bool HasFailed => hasFailed;

            public void SetResult(bool ok)
            {
                hasSucceeded = ok;
                hasFailed = !ok;
                statusPrefix = ok ? "✓" : "✕";
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TitleDisplay)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSucceeded)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasFailed)));
            }
        }

        private readonly WinFixService winFix = new();
        private readonly List<WinFixRow> allRows;
        private readonly TextBlock lblTitle;
        private readonly TextBlock lblSub;
        private readonly Border cardTop;
        private readonly Border cardGrid;
        private readonly ComboBox cbCategory;
        private readonly Button btnSelectAll;
        private readonly Button btnUncheckAll;
        private readonly Button btnRollback;
        private readonly Button btnFixSelected;
        private readonly Button btnClose;
        private readonly DataGrid gridFixes;
        private readonly ProgressBar pbBusy;
        private readonly TextBlock lblStatus;
        private bool isBusy;

        public ToolboxWinFixWindow()
            : base("Win Fix - Windows Glitch Repair Center - PC Boost", "مركز إصلاح مشاكل ويندوز - PC Boost", 840, 585, resizable: true)
        {
            MinWidth = 760;
            MinHeight = 520;
            bool isAr = LocalizationService.IsArabic;

            allRows = winFix.GetAllFixes().Select(f => new WinFixRow
            {
                Model = f,
                IsSelected = f.IsSelected,
                RawTitle = isAr && !string.IsNullOrEmpty(f.TitleAr) ? f.TitleAr : f.Title,
                CategoryDisplay = LocalizeCategory(f.Category, isAr),
                DescriptionDisplay = isAr && !string.IsNullOrEmpty(f.DescriptionAr) ? f.DescriptionAr : f.Description
            }).ToList();

            var root = new Grid { Margin = new Thickness(20, 18, 20, 16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Toolbar
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Grid
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer

            var header = BuildHeader("Wrench", WpfTheme.ColorPrimary,
                isAr ? "مركز إصلاح أعطال ويندوز بنقرة واحدة" : "Windows 1-Click Repair Center",
                isAr ? "إصلاح أعطال سطح المكتب والشبكة والنظام مع نسخة احتياطية تلقائية للسجل ونقطة استعادة." : "Repair Explorer, network, security, and file association glitches with automatic registry backup & restore point.",
                out lblTitle, out lblSub);
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // Toolbar Card
            cardTop = CreateCard(new Thickness(12, 10, 12, 10));
            cardTop.Margin = new Thickness(0, 0, 0, 12);
            var barGrid = new Grid();
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftBar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            leftBar.Children.Add(new TextBlock
            {
                Text = isAr ? "التصنيف:" : "Category:",
                FontSize = 12.5,
                Foreground = WpfTheme.TextPrimaryBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            cbCategory = new ComboBox { Width = 178, Height = 32, FontSize = 12.5, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            string[] cats = isAr
                ? new[] { "جميع التصنيفات", "سطح المكتب والمستكشف", "الشبكة والإنترنت", "النظام والأمان", "اقتران الملفات" }
                : new[] { "All Categories", "Desktop & Explorer", "Network & Internet", "System & Security", "File Associations" };
            foreach (var c in cats) cbCategory.Items.Add(c);
            cbCategory.SelectedIndex = 0;
            cbCategory.SelectionChanged += (_, _) => FilterFixes();
            leftBar.Children.Add(cbCategory);

            btnSelectAll = CreateSecondaryButton(null, isAr ? "تحديد الكل" : "Select All", 32);
            btnSelectAll.Margin = new Thickness(0, 0, 6, 0);
            btnSelectAll.Click += (_, _) => SetVisibleChecks(true);

            btnUncheckAll = CreateSecondaryButton(null, isAr ? "إلغاء التحديد" : "Uncheck All", 32);
            btnUncheckAll.Click += (_, _) => SetVisibleChecks(false);

            leftBar.Children.Add(btnSelectAll);
            leftBar.Children.Add(btnUncheckAll);
            Grid.SetColumn(leftBar, 0);
            barGrid.Children.Add(leftBar);

            var rightBar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            btnRollback = CreateSecondaryButton("Undo", isAr ? "التراجع" : "Undo Fixes", 34);
            btnRollback.Margin = new Thickness(0, 0, 8, 0);
            btnRollback.Click += (_, _) => PromptRollback();

            btnFixSelected = CreatePrimaryButton("Wrench", isAr ? "إصلاح المحدد" : "Fix Selected", 34);
            btnFixSelected.Click += async (_, _) => await FixSelectedAsync();

            rightBar.Children.Add(btnRollback);
            rightBar.Children.Add(btnFixSelected);
            Grid.SetColumn(rightBar, 2);
            barGrid.Children.Add(rightBar);

            cardTop.Child = barGrid;
            Grid.SetRow(cardTop, 1);
            root.Children.Add(cardTop);

            // DataGrid Card
            cardGrid = CreateCard(new Thickness(0));
            cardGrid.ClipToBounds = true;

            gridFixes = CreateVirtualDataGrid();
            gridFixes.IsReadOnly = false;
            gridFixes.RowHeight = 35;

            var chkFactory = new FrameworkElementFactory(typeof(CheckBox));
            chkFactory.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(nameof(WinFixRow.IsSelected))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            chkFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            chkFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            chkFactory.SetValue(Control.PaddingProperty, new Thickness(0));

            gridFixes.Columns.Add(new DataGridTemplateColumn
            {
                Header = "",
                Width = new DataGridLength(44),
                CanUserResize = false,
                CellTemplate = new DataTemplate { VisualTree = chkFactory }
            });
            gridFixes.Columns.Add(new DataGridTextColumn
            {
                Header = isAr ? "مجال الإصلاح / المشكلة" : "Repair / Glitch Area",
                Binding = new Binding(nameof(WinFixRow.TitleDisplay)),
                Width = new DataGridLength(250),
                IsReadOnly = true,
                ElementStyle = CreateEllipsisTextStyle()
            });
            gridFixes.Columns.Add(new DataGridTextColumn
            {
                Header = isAr ? "التصنيف" : "Category",
                Binding = new Binding(nameof(WinFixRow.CategoryDisplay)),
                Width = new DataGridLength(155),
                IsReadOnly = true,
                ElementStyle = CreateEllipsisTextStyle()
            });
            gridFixes.Columns.Add(new DataGridTextColumn
            {
                Header = isAr ? "الوصف والحل" : "Description & Solution",
                Binding = new Binding(nameof(WinFixRow.DescriptionDisplay)),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                IsReadOnly = true,
                ElementStyle = CreateEllipsisTextStyle()
            });

            var ctx = new ContextMenu();
            var miFix = new MenuItem { Header = isAr ? "إصلاح المحدد (Enter)" : "Fix Selected (Enter)" };
            miFix.Click += async (_, _) => await FixSelectedAsync();
            var miChk = new MenuItem { Header = isAr ? "تحديد المظلل (Space)" : "Check Selected (Space)" };
            miChk.Click += (_, _) => SetSelectedRowsChecked(true);
            var miUnchk = new MenuItem { Header = isAr ? "إلغاء تحديد المظلل" : "Uncheck Selected" };
            miUnchk.Click += (_, _) => SetSelectedRowsChecked(false);
            var miCopy = new MenuItem { Header = isAr ? "نسخ التفاصيل (Ctrl+C)" : "Copy Fix Details (Ctrl+C)" };
            miCopy.Click += (_, _) => CopySelectedDetails();
            ctx.Items.Add(miFix);
            ctx.Items.Add(miChk);
            ctx.Items.Add(miUnchk);
            ctx.Items.Add(new Separator());
            ctx.Items.Add(miCopy);
            gridFixes.ContextMenu = ctx;

            gridFixes.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Space)
                {
                    ToggleSelectedRowsChecked();
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter)
                {
                    _ = FixSelectedAsync();
                    e.Handled = true;
                }
                else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
                {
                    CopySelectedDetails();
                    e.Handled = true;
                }
            };

            cardGrid.Child = gridFixes;
            Grid.SetRow(cardGrid, 2);
            root.Children.Add(cardGrid);

            // Footer
            var footerGrid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var statusStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            pbBusy = new ProgressBar
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
                Text = isAr ? $"يتوفر {allRows.Count} إصلاحاً لمشاكل وأعطال ويندوز." : $"Available {allRows.Count} Windows repairs ready to apply.",
                FontSize = 12,
                Foreground = WpfTheme.TextSecondaryBrush,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            statusStack.Children.Add(pbBusy);
            statusStack.Children.Add(lblStatus);
            Grid.SetColumn(statusStack, 0);
            footerGrid.Children.Add(statusStack);

            btnClose = CreateSecondaryButton(null, isAr ? "إغلاق" : "Close", 34);
            btnClose.MinWidth = 96;
            btnClose.Click += (_, _) => Close();
            Grid.SetColumn(btnClose, 1);
            footerGrid.Children.Add(btnClose);

            Grid.SetRow(footerGrid, 3);
            root.Children.Add(footerGrid);

            Content = root;
            FilterFixes();
        }

        private static string LocalizeCategory(string cat, bool isAr)
        {
            if (!isAr) return cat;
            return cat switch
            {
                "Desktop & Explorer" => "سطح المكتب والمستكشف",
                "Network & Internet" => "الشبكة والإنترنت",
                "System & Security" => "النظام والأمان",
                "File Associations" => "اقتران الملفات",
                _ => cat
            };
        }

        private void FilterFixes()
        {
            int idx = cbCategory.SelectedIndex;
            if (idx <= 0)
            {
                gridFixes.ItemsSource = allRows.ToList();
                return;
            }
            string[] internalCats = { "", "Desktop & Explorer", "Network & Internet", "System & Security", "File Associations" };
            string target = idx < internalCats.Length ? internalCats[idx] : "";
            gridFixes.ItemsSource = allRows.Where(r => r.Model.Category.Equals(target, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private void SetVisibleChecks(bool isChecked)
        {
            if (gridFixes.ItemsSource is IEnumerable<WinFixRow> rows)
            {
                foreach (var r in rows) r.IsSelected = isChecked;
            }
        }

        private void SetSelectedRowsChecked(bool isChecked)
        {
            foreach (var r in gridFixes.SelectedItems.OfType<WinFixRow>())
                r.IsSelected = isChecked;
        }

        private void ToggleSelectedRowsChecked()
        {
            var sel = gridFixes.SelectedItems.OfType<WinFixRow>().ToList();
            if (sel.Count == 0) return;
            bool target = !sel[0].IsSelected;
            foreach (var r in sel) r.IsSelected = target;
        }

        private async Task FixSelectedAsync()
        {
            if (isBusy) return;
            bool isAr = LocalizationService.IsArabic;

            var visible = (gridFixes.ItemsSource as IEnumerable<WinFixRow> ?? allRows).Where(r => r.IsSelected).ToList();
            if (visible.Count == 0)
            {
                AppDialog.Show(this,
                    isAr ? "يرجى تحديد الإصلاحات المراد تطبيقها." : "Please check the repairs you want to apply.",
                    isAr ? "تنبيه" : "Notice",
                    AppDialogTone.Info);
                return;
            }

            if (!AdminPrivilegeService.EnsureAdminOrPrompt(this, "Win Fix", "مركز إصلاح مشاكل ويندوز"))
                return;

            isBusy = true;
            btnFixSelected.IsEnabled = false;
            btnRollback.IsEnabled = false;
            pbBusy.Visibility = Visibility.Visible;

            int fixedCount = 0;
            var failures = new List<string>();
            try
            {
                lblStatus.Text = isAr
                    ? "جاري إنشاء نقطة استعادة للنظام ونسخة احتياطية للسجل قبل الإصلاح..."
                    : "Creating System Restore Point & Registry Snapshot before repairs...";

                var (rpOk, backupPath) = await Task.Run(() =>
                {
                    bool rp = winFix.CreateSystemRestorePoint();
                    string bp = winFix.CreateRegistryBackup();
                    return (rp, bp);
                });

                int idx = 0;
                foreach (var row in visible)
                {
                    idx++;
                    lblStatus.Text = isAr
                        ? $"جاري تطبيق ({idx}/{visible.Count}): {row.RawTitle}..."
                        : $"Applying ({idx}/{visible.Count}): {row.RawTitle}...";

                    var (ok, err) = await Task.Run(() => winFix.ExecuteFix(row.Model));
                    row.SetResult(ok);
                    if (ok) fixedCount++;
                    else failures.Add($"{row.RawTitle}: {err ?? "Failed"}");
                }

                string backupNotice = !string.IsNullOrEmpty(backupPath)
                    ? (isAr ? $"\nلقطة السجل الاحتياطية: \u200E{Path.GetFileName(backupPath)}\u200E" : $"\nRegistry Snapshot: {Path.GetFileName(backupPath)}")
                    : string.Empty;
                string rpStatus = isAr ? (rpOk ? "تم الإنشاء" : "محمي") : (rpOk ? "Created" : "Protected");

                if (failures.Count == 0)
                {
                    lblStatus.Text = isAr ? $"تم إكمال {fixedCount} إصلاحات وحفظ النسخة الاحتياطية بأمان." : $"Completed {fixedCount} repairs. Backup saved safely.";
                    AppDialog.Show(this,
                        isAr ? $"تم تطبيق {fixedCount} إصلاحات بنجاح!{backupNotice}\n\nنقطة استعادة النظام: {rpStatus}" : $"Completed {fixedCount} Windows repairs successfully!{backupNotice}\n\nSystem Restore Point: {rpStatus}",
                        isAr ? "اكتمل الإصلاح" : "Win Fix Finished",
                        AppDialogTone.Success);
                }
                else
                {
                    lblStatus.Text = isAr ? $"اكتمل {fixedCount} إصلاحات • تعذر تطبيق {failures.Count}." : $"Completed {fixedCount} repair(s) • Failed: {failures.Count}.";
                    string errList = string.Join("\n• ", failures.Take(5));
                    AppDialog.Show(this,
                        isAr ? $"تم تطبيق {fixedCount} إصلاحات، وتعذر إكمال {failures.Count}:{backupNotice}\n\n• {errList}" : $"Completed {fixedCount} repair(s), but {failures.Count} encountered an issue:{backupNotice}\n\n• {errList}",
                        isAr ? "نتيجة الإصلاح" : "Win Fix Result",
                        AppDialogTone.Warning);
                }
            }
            finally
            {
                pbBusy.Visibility = Visibility.Collapsed;
                btnFixSelected.IsEnabled = true;
                btnRollback.IsEnabled = true;
                isBusy = false;
            }
        }

        private void PromptRollback()
        {
            bool isAr = LocalizationService.IsArabic;
            var backups = winFix.GetAvailableBackups();
            if (backups.Count == 0)
            {
                AppDialog.Show(this,
                    isAr ? "لا توجد نسخ احتياطية سابقة للسجل لاستعادتها." : "No previous registry snapshots found to restore.",
                    isAr ? "ملاحظة الاستعادة" : "Restore Notice",
                    AppDialogTone.Info);
                return;
            }

            string latest = backups[0];
            string filename = Path.GetFileName(latest);
            DateTime created = File.GetCreationTime(latest);

            string confirmText = isAr
                ? $"هل تريد استعادة أحدث نسخة احتياطية للسجل؟\n\nالملف: \u200E{filename}\u200E\nالتاريخ: \u200E{created:G}\u200E"
                : $"Do you want to restore the most recent registry backup?\n\nFile: {filename}\nCreated: {created:G}";
            if (!AppDialog.Confirm(this, confirmText, isAr ? "تأكيد الاستعادة" : "Confirm Rollback", AppDialogTone.Question))
                return;

            if (!AdminPrivilegeService.EnsureAdminOrPrompt(this, "Win Fix Rollback", "استعادة نسخة السجل الاحتياطية"))
                return;

            bool ok = winFix.RestoreRegistryBackup(latest);
            if (ok)
            {
                AppDialog.Show(this,
                    isAr ? "تمت استعادة حالة السجل من النسخة الاحتياطية بنجاح!" : "Registry state successfully restored from snapshot!",
                    isAr ? "تمت الاستعادة" : "Restored",
                    AppDialogTone.Success);
                lblStatus.Text = isAr ? $"تمت استعادة النسخة: \u200E{filename}\u200E" : $"Restored backup: {filename}";
            }
            else
            {
                AppDialog.Show(this,
                    isAr ? "تعذرت استعادة نسخة السجل الاحتياطية." : "Failed to restore registry snapshot.",
                    isAr ? "خطأ" : "Error",
                    AppDialogTone.Error);
            }
        }

        private void CopySelectedDetails()
        {
            var sel = gridFixes.SelectedItems.OfType<WinFixRow>().ToList();
            if (sel.Count == 0) return;
            var sb = new StringBuilder();
            foreach (var r in sel) sb.AppendLine($"{r.TitleDisplay} | {r.CategoryDisplay} | {r.DescriptionDisplay}");
            try { Clipboard.SetText(sb.ToString().TrimEnd()); } catch { }
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
            StyleDataGridForTheme(gridFixes);
            ApplySecondaryButtonStyle(btnSelectAll);
            ApplySecondaryButtonStyle(btnUncheckAll);
            ApplySecondaryButtonStyle(btnRollback);
            btnRollback.Content = WpfIcons.CreateIconTextContent("Undo", LocalizationService.IsArabic ? "التراجع" : "Undo Fixes", btnRollback.Foreground, 14, 6);
            ApplySecondaryButtonStyle(btnClose);
        }
    }
}
