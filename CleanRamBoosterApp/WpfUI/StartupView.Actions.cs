using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using CleanRamBoosterApp.Models;
using CleanRamBoosterApp.Services;
using CleanRamBoosterApp.UI;

namespace CleanRamBoosterApp.WpfUI
{
    public partial class StartupView
    {
        private void UpdateSelectionActionsState()
        {
            var checkedItems = GetCheckedItems();
            var selectedRows = gridStartup.SelectedItems.OfType<StartupItem>().ToList();
            var targets = GetActionTargetItems();
            bool hasOnlyStoreApps = targets.Count > 0 && targets.All(x => x.LocationType == StartupType.MicrosoftStoreApp);
            btnDelete.IsEnabled = !hasOnlyStoreApps;
            btnDelete.Opacity = hasOnlyStoreApps ? 0.45 : 1.0;
            int multiCount = selectedRows.Count > 1 ? targets.Count : checkedItems.Count;
            UpdateToggleAndDeleteButtonLabels(multiCount);
        }

        private void SetActionBusy(bool busy)
        {
            isActionBusy = busy;
            btnToggle.IsEnabled = !busy;
            btnOptimizeSafe.IsEnabled = !busy;
            btnRefresh.IsEnabled = !busy;
            btnDisableAllStore.IsEnabled = !busy;
            if (!busy)
            {
                UpdateSelectionActionsState();
                UpdateUndoButtonState();
            }
            else
            {
                btnDelete.IsEnabled = false;
                btnUndoLast.IsEnabled = false;
            }
        }

        private void UpdateUndoButtonState()
        {
            int count = StartupHistoryService.Instance.Count;
            bool enabled = !isActionBusy && count > 0;
            btnUndoLast.IsEnabled = enabled;
            bool isDark = WpfTheme.IsDarkMode;
            Brush fg = enabled
                ? new SolidColorBrush(isDark ? Color.FromRgb(201, 209, 217) : Color.FromRgb(30, 41, 59))
                : new SolidColorBrush(isDark ? Color.FromRgb(148, 163, 184) : Color.FromRgb(100, 116, 139));
            btnUndoLast.Foreground = fg;
            string label = LocalizationService.T("StartupBtnUndo", "Undo");
            string text = count > 0 ? $"{label} ({count})" : label;
            btnUndoLast.Content = WpfIcons.CreateIconTextContent("Undo", text, fg, 15, 6);
            var latest = StartupHistoryService.Instance.PeekLatest();
            btnUndoLast.ToolTip = latest != null
                ? (LocalizationService.IsArabic
                    ? $"التراجع عن آخر إجراء ({latest.Changes.Count} عنصر) — {latest.Timestamp:HH:mm:ss}"
                    : $"Undo last change ({latest.Changes.Count} item(s)) — {latest.Timestamp:HH:mm:ss}")
                : (LocalizationService.IsArabic ? "لا توجد تغييرات سابقة للتراجع عنها" : "No recent startup changes to undo");
        }

        private async Task UndoLastStartupActionAsync()
        {
            if (isActionBusy) return;
            var latest = StartupHistoryService.Instance.PeekLatest();
            if (latest == null || latest.Changes.Count == 0)
            {
                UpdateUndoButtonState();
                return;
            }

            bool isAr = LocalizationService.IsArabic;
            string namesPreview = FormatNamesPreview(latest.Changes.Select(c => c.DisplayName), 4);
            string confirm = isAr
                ? $"هل تريد التراجع عن آخر إجراء ({latest.Changes.Count} عنصر)؟\n{namesPreview}"
                : $"Undo the last startup change ({latest.Changes.Count} item(s))?\n{namesPreview}";
            if (!AppDialog.Confirm(this, confirm, LocalizationService.T("AppTitle", "PC Boost"), AppDialogTone.Question))
                return;

            var entry = StartupHistoryService.Instance.PopLatest();
            if (entry == null) return;

            SetActionBusy(true);
            ShowActionFeedback(
                isAr ? $"جاري التراجع عن {entry.Changes.Count} تغيير..." : $"Undoing {entry.Changes.Count} startup change(s)...",
                AppDialogTone.Info);

            try
            {
                var plan = new List<(StartupItem Item, bool TargetEnable)>();
                foreach (var ch in entry.Changes)
                {
                    var match = allItems.FirstOrDefault(x => string.Equals(x.MonitorKey, ch.MonitorKey, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                        plan.Add((match, ch.PreviousEnabled));
                }

                if (plan.Count == 0)
                {
                    ShowActionFeedback(
                        isAr ? "تعذر العثور على العناصر في القائمة الحالية للتراجع عنها." : "Could not find the previous item(s) in the current startup list.",
                        AppDialogTone.Warning);
                    return;
                }

                var results = await Task.Run(() =>
                    plan.Select(p => (p.Item, p.TargetEnable, Result: startupService.SetItemEnabledDetailed(p.Item, p.TargetEnable))).ToList());

                var needAdmin = results
                    .Where(r => !r.Result.Success && r.Result.FailureReason == StartupOpFailureReason.RequiresAdministrator)
                    .ToList();

                if (needAdmin.Count > 0 && !AdminPrivilegeService.IsRunningAsAdmin())
                {
                    string elevPrompt = isAr
                        ? $"يتطلب التراجع عن {needAdmin.Count} عنصر صلاحيات المسؤول.\n\nهل تريد السماح بصلاحيات المسؤول (UAC) الآن؟"
                        : $"Administrator privileges are required to undo {needAdmin.Count} item(s).\n\nElevate via Windows UAC now?";
                    if (AppDialog.Confirm(this, elevPrompt, LocalizationService.T("AppTitle", "PC Boost"), AppDialogTone.Question))
                    {
                        var elevatedMap = await RunElevatedToggleBatchAsync(needAdmin);
                        for (int i = 0; i < results.Count; i++)
                        {
                            if (elevatedMap.TryGetValue(results[i].Item, out var updated))
                                results[i] = (results[i].Item, results[i].TargetEnable, updated);
                        }
                    }
                }

                int restored = 0;
                var failed = new List<(StartupItem Item, StartupOperationResult Result)>();
                foreach (var r in results)
                {
                    if (r.Result.Success)
                    {
                        r.Item.IsEnabled = r.TargetEnable;
                        ItemStateModified?.Invoke(r.Item);
                        restored++;
                    }
                    else
                    {
                        failed.Add((r.Item, r.Result));
                    }
                }

                if (restored == 0 && failed.Count > 0)
                {
                    // Put entry back on the stack if nothing could be reverted (e.g., user cancelled UAC)
                    StartupHistoryService.Instance.PushBack(entry);
                }

                UpdateTabCounts();
                ApplyFilter();
                gridStartup.Items.Refresh();

                if (failed.Count == 0)
                {
                    string msg = isAr
                        ? $"تم التراجع بنجاح عن {restored} عنصر بدء تشغيل: {FormatNamesPreview(plan.Select(p => p.Item.Name))}"
                        : $"Successfully reverted {restored} startup item(s): {FormatNamesPreview(plan.Select(p => p.Item.Name))}";
                    ShowActionFeedback(msg, AppDialogTone.Success);
                    StatusMessage?.Invoke($"[Startup Manager] {msg}");
                }
                else
                {
                    string msg = isAr
                        ? $"تمت استعادة {restored} عنصر • تعذر استعادة {failed.Count}."
                        : $"Reverted {restored} item(s) • {failed.Count} could not be reverted.";
                    ShowActionFeedback(msg, AppDialogTone.Warning, showElevateButton: !AdminPrivilegeService.IsRunningAsAdmin());
                    StatusMessage?.Invoke($"[Startup Manager] {msg}");
                }
            }
            finally
            {
                SetActionBusy(false);
            }
        }

        private void ShowActionFeedback(string message, AppDialogTone tone, bool showElevateButton = false)
        {
            lastFeedbackTone = tone;
            lblFeedback.Text = message;
            btnFeedbackElevate.Content = LocalizationService.IsArabic
                ? "إعادة التشغيل كمسؤول"
                : "Restart as Admin";
            btnFeedbackElevate.Visibility = (showElevateButton && !AdminPrivilegeService.IsRunningAsAdmin())
                ? Visibility.Visible
                : Visibility.Collapsed;
            ApplyFeedbackBannerTheme();
            cardFeedbackBanner.Visibility = Visibility.Visible;
        }

        private void ApplyFeedbackBannerTheme()
        {
            bool isDark = WpfTheme.IsDarkMode;
            Color accent = lastFeedbackTone switch
            {
                AppDialogTone.Success => Color.FromRgb(16, 185, 129),
                AppDialogTone.Warning => Color.FromRgb(245, 158, 11),
                AppDialogTone.Error => Color.FromRgb(239, 68, 68),
                _ => Color.FromRgb(59, 130, 246)
            };
            string iconKey = lastFeedbackTone switch
            {
                AppDialogTone.Success => "CheckCircle",
                AppDialogTone.Warning => "Alert",
                AppDialogTone.Error => "Alert",
                _ => "InfoCircle"
            };

            cardFeedbackBanner.Background = new SolidColorBrush(Color.FromArgb((byte)(isDark ? 34 : 24), accent.R, accent.G, accent.B));
            cardFeedbackBanner.BorderBrush = new SolidColorBrush(Color.FromArgb((byte)(isDark ? 120 : 105), accent.R, accent.G, accent.B));
            feedbackIconHost.Content = WpfIcons.CreateIcon(iconKey, new SolidColorBrush(accent), 16, 1.9);
            lblFeedback.Foreground = WpfTheme.TextPrimaryBrush;
            btnFeedbackDismiss.Foreground = WpfTheme.TextSecondaryBrush;
        }

        private static string FormatNamesPreview(IEnumerable<string> names, int maxNames = 3)
        {
            var list = names.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            if (list.Count == 0) return string.Empty;
            if (list.Count <= maxNames) return string.Join(", ", list);
            return string.Join(", ", list.Take(maxNames)) + $" (+{list.Count - maxNames})";
        }

        private async Task ToggleSelectedItemAsync()
        {
            if (isActionBusy) return;
            var selected = GetActionTargetItems();
            if (selected.Count == 0) return;

            bool isAr = LocalizationService.IsArabic;
            SetActionBusy(true);
            ShowActionFeedback(
                isAr ? $"جاري تطبيق التغيير على {selected.Count} عنصر..." : $"Updating {selected.Count} startup item(s)...",
                AppDialogTone.Info);

            try
            {
                var plan = selected.Select(item => (Item: item, TargetEnable: !item.IsEnabled)).ToList();

                var results = await Task.Run(() =>
                {
                    var outcomes = new List<(StartupItem Item, bool TargetEnable, StartupOperationResult Result)>(plan.Count);
                    foreach (var entry in plan)
                    {
                        var res = startupService.SetItemEnabledDetailed(entry.Item, entry.TargetEnable);
                        outcomes.Add((entry.Item, entry.TargetEnable, res));
                    }
                    return outcomes;
                });

                // If any items specifically need Administrator elevation while running non-elevated, offer 1-click UAC elevation
                var needAdmin = results
                    .Where(r => !r.Result.Success && r.Result.FailureReason == StartupOpFailureReason.RequiresAdministrator)
                    .ToList();

                if (needAdmin.Count > 0 && !AdminPrivilegeService.IsRunningAsAdmin())
                {
                    string preview = FormatNamesPreview(needAdmin.Select(x => x.Item.Name), 4);
                    string prompt = isAr
                        ? $"يتطلب تعديل {needAdmin.Count} عنصر صلاحيات المسؤول:\n{preview}\n\nهل تريد السماح بصلاحيات المسؤول (UAC) لتطبيق التغيير الآن؟"
                        : $"Administrator privileges are required to modify {needAdmin.Count} item(s):\n{preview}\n\nElevate via Windows Administrator prompt (UAC) to apply now?";

                    if (AppDialog.Confirm(this, prompt, LocalizationService.T("AppTitle", "PC Boost"), AppDialogTone.Question))
                    {
                        var elevatedMap = await RunElevatedToggleBatchAsync(needAdmin);
                        for (int i = 0; i < results.Count; i++)
                        {
                            if (elevatedMap.TryGetValue(results[i].Item, out var updated))
                            {
                                results[i] = (results[i].Item, results[i].TargetEnable, updated);
                            }
                        }
                    }
                }

                var succeeded = new List<(StartupItem Item, bool TargetEnable)>();
                var failed = new List<(StartupItem Item, StartupOperationResult Result)>();

                foreach (var r in results)
                {
                    if (r.Result.Success)
                    {
                        r.Item.IsEnabled = r.TargetEnable;
                        ItemStateModified?.Invoke(r.Item);
                        succeeded.Add((r.Item, r.TargetEnable));
                    }
                    else
                    {
                        failed.Add((r.Item, r.Result));
                    }
                }

                if (succeeded.Count > 0)
                {
                    StartupHistoryService.Instance.RecordChanges(
                        "Toggle",
                        succeeded.Select(s => (s.Item, PreviousEnabled: !s.TargetEnable, NewEnabled: s.TargetEnable)));
                    UpdateTabCounts();
                    ApplyFilter();
                    gridStartup.Items.Refresh();
                }

                ReportToggleOutcome(succeeded, failed, isAr);
            }
            finally
            {
                SetActionBusy(false);
            }
        }

        private static Task<Dictionary<StartupItem, StartupOperationResult>> RunElevatedToggleBatchAsync(
            List<(StartupItem Item, bool TargetEnable, StartupOperationResult Result)> needAdmin)
        {
            return Task.Run(() =>
            {
                var updated = new Dictionary<StartupItem, StartupOperationResult>();
                bool isAr = LocalizationService.IsArabic;

                foreach (var group in needAdmin.GroupBy(x => x.TargetEnable))
                {
                    string mode = group.Key ? "enable" : "disable";
                    var keysArg = string.Join(" ", group.Select(x => $"\"{x.Item.MonitorKey.Replace("\"", "\\\"")}\""));
                    bool ok = AdminPrivilegeService.TryRunElevatedSelfCommand($"--toggle-startup {mode} {keysArg}", out bool cancelled, out _);

                    foreach (var entry in group)
                    {
                        if (ok)
                        {
                            entry.Item.IsEnabled = group.Key;
                            updated[entry.Item] = StartupOperationResult.Ok();
                        }
                        else if (cancelled)
                        {
                            updated[entry.Item] = StartupOperationResult.Fail(
                                StartupOpFailureReason.CancelledByUser,
                                isAr ? "تم إلغاء نافذة صلاحيات المسؤول (UAC)." : "Administrator prompt (UAC) was cancelled.");
                        }
                        else
                        {
                            updated[entry.Item] = StartupOperationResult.Fail(
                                StartupOpFailureReason.ProtectedByWindows,
                                isAr
                                    ? "تعذر التعديل حتى بصلاحيات المسؤول (العنصر محمي من قِبل نظام ويندوز)."
                                    : "Could not modify even with Administrator privileges (protected by Windows).");
                        }
                    }
                }

                return updated;
            });
        }

        private void ReportToggleOutcome(
            List<(StartupItem Item, bool TargetEnable)> succeeded,
            List<(StartupItem Item, StartupOperationResult Result)> failed,
            bool isAr)
        {
            int enabledCount = succeeded.Count(x => x.TargetEnable);
            int disabledCount = succeeded.Count(x => !x.TargetEnable);

            if (failed.Count == 0 && succeeded.Count > 0)
            {
                string preview = FormatNamesPreview(succeeded.Select(x => x.Item.Name));
                string msg;
                if (disabledCount > 0 && enabledCount == 0)
                {
                    msg = isAr
                        ? $"تم تعطيل {disabledCount} عنصر بدء تشغيل: {preview}"
                        : $"Disabled {disabledCount} startup item(s): {preview}";
                }
                else if (enabledCount > 0 && disabledCount == 0)
                {
                    msg = isAr
                        ? $"تم تفعيل {enabledCount} عنصر بدء تشغيل: {preview}"
                        : $"Enabled {enabledCount} startup item(s): {preview}";
                }
                else
                {
                    msg = isAr
                        ? $"تم تحديث {succeeded.Count} عناصر ({disabledCount} معطل، {enabledCount} مفعل)."
                        : $"Updated {succeeded.Count} startup item(s) ({disabledCount} disabled, {enabledCount} enabled).";
                }

                ShowActionFeedback(msg, AppDialogTone.Success);
                StatusMessage?.Invoke($"[Startup Manager] {msg}");
                return;
            }

            if (failed.Count > 0)
            {
                bool anyNeedsAdmin = failed.Any(f => f.Result.FailureReason == StartupOpFailureReason.RequiresAdministrator)
                                     && !AdminPrivilegeService.IsRunningAsAdmin();
                bool allCancelled = failed.All(f => f.Result.FailureReason == StartupOpFailureReason.CancelledByUser);

                string bannerMsg;
                if (succeeded.Count > 0)
                {
                    bannerMsg = isAr
                        ? $"تم تحديث {succeeded.Count} عنصر، وتعذر تعديل {failed.Count} ({FormatNamesPreview(failed.Select(f => f.Item.Name), 2)})."
                        : $"Updated {succeeded.Count} item(s); {failed.Count} could not be modified ({FormatNamesPreview(failed.Select(f => f.Item.Name), 2)}).";
                }
                else if (allCancelled)
                {
                    bannerMsg = isAr
                        ? "تم إلغاء طلب صلاحيات المسؤول ولم يتم تعديل العناصر."
                        : "Administrator elevation prompt was cancelled; no items were changed.";
                }
                else
                {
                    bannerMsg = isAr
                        ? $"تعذر تعديل {failed.Count} عنصر: {failed[0].Item.Name} — {failed[0].Result.Message}"
                        : $"Could not modify {failed.Count} item(s): {failed[0].Item.Name} — {failed[0].Result.Message}";
                }

                ShowActionFeedback(bannerMsg, AppDialogTone.Warning, showElevateButton: anyNeedsAdmin);
                StatusMessage?.Invoke($"[Startup Manager] {bannerMsg}");

                if (!allCancelled)
                {
                    var detailLines = failed
                        .Take(6)
                        .Select(f => $"• {f.Item.Name}: {f.Result.Message}")
                        .ToList();
                    if (failed.Count > 6)
                    {
                        detailLines.Add(isAr ? $"...و {failed.Count - 6} عناصر أخرى." : $"...and {failed.Count - 6} more item(s).");
                    }

                    string header = succeeded.Count > 0
                        ? (isAr ? $"تم تحديث {succeeded.Count} عنصر، لكن تعذر تعديل {failed.Count}:\n\n" : $"Updated {succeeded.Count} item(s), but {failed.Count} could not be modified:\n\n")
                        : (isAr ? $"تعذر تعديل العناصر التالية ({failed.Count}):\n\n" : $"Could not modify the following item(s) ({failed.Count}):\n\n");

                    AppDialog.Show(this,
                        header + string.Join("\n", detailLines),
                        LocalizationService.T("AppTitle", "PC Boost"),
                        AppDialogTone.Warning);
                }
            }
        }

        private void DisableAllStoreAppsToNever()
        {
            var targets = allItems
                .Where(x => x.LocationType == StartupType.MicrosoftStoreApp && x.IsEnabled && x.Recommendation != StartupRecommendation.Necessary)
                .ToList();

            bool isAr = LocalizationService.IsArabic;
            if (targets.Count == 0)
            {
                AppDialog.Show(this,
                    isAr ? "جميع تطبيقات متجر مايكروسوفت الآمنة والاختيارية مضبوطة بالفعل على (أبداً / معطلة في الخلفية)." : "All safe & optional Microsoft Store apps are already set to 'Never' (background launch disabled).",
                    LocalizationService.T("AppTitle", "PC Boost"),
                    AppDialogTone.Info);
                return;
            }

            string prompt = isAr
                ? $"هل تريد تعيين إذن التشغيل في الخلفية إلى \"أبداً (Never)\" لعدد {targets.Count} من تطبيقات متجر مايكروسوفت؟\n\nسيمنع هذا تشغيلها في الخلفية أو التجهيز المسبق عند إقلاع ويندوز القادم (مثل خيار الإعدادات: التطبيقات > التطبيقات المثبتة > خيارات متقدمة > أبداً)، مع الحفاظ على تطبيقات الأمان الأساسية."
                : $"Set Background app permissions to 'Never' for {targets.Count} Microsoft Store app(s)?\n\nThis matches Settings > Apps > Installed apps > Advanced options > Background app permissions -> Never, preventing them from prelaunching or running in the background on next Windows boot (essential security apps stay protected).";

            if (!AppDialog.Confirm(this, prompt, LocalizationService.T("AppTitle", "PC Boost"), AppDialogTone.Question)) return;

            int changed = startupService.DisableAllMicrosoftStoreApps(targets);
            foreach (var t in targets.Where(x => !x.IsEnabled))
            {
                ItemStateModified?.Invoke(t);
            }
            UpdateTabCounts();
            ApplyFilter();
            gridStartup.Items.Refresh();

            AppDialog.Show(this,
                isAr ? $"تم بنجاح تعطيل التشغيل في الخلفية (أبداً) لعدد {changed} تطبيقات من متجر مايكروسوفت." : $"Successfully set 'Never' background permission (Disabled) for {changed} Microsoft Store app(s).",
                LocalizationService.T("AppTitle", "PC Boost"),
                AppDialogTone.Success);
        }

        private void OpenItemLocation()
        {
            var target = GetCheckedItems().FirstOrDefault() ?? gridStartup.SelectedItem as StartupItem;
            if (target is StartupItem item)
            {
                try
                {
                    if (!string.IsNullOrEmpty(item.FilePath) && File.Exists(item.FilePath))
                    {
                        Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
                    }
                    else if (!string.IsNullOrEmpty(item.FilePath) && Directory.Exists(item.FilePath))
                    {
                        Process.Start("explorer.exe", $"\"{item.FilePath}\"");
                    }
                    else if (!string.IsNullOrEmpty(item.Command))
                    {
                        string raw = item.Command.Trim('\"');
                        if (File.Exists(raw))
                        {
                            Process.Start("explorer.exe", $"/select,\"{raw}\"");
                        }
                    }
                }
                catch { }
            }
        }

        private async Task DeleteSelectedItemAsync()
        {
            if (isActionBusy) return;
            var targets = GetActionTargetItems();
            if (targets.Count == 0) return;

            bool isAr = LocalizationService.IsArabic;
            var deletable = targets.Where(x => x.LocationType != StartupType.MicrosoftStoreApp && x.LocationType != StartupType.WindowsService).ToList();
            if (deletable.Count == 0)
            {
                bool hasServices = targets.Any(x => x.LocationType == StartupType.WindowsService);
                string infoMsg = hasServices
                    ? (isAr ? "لا يمكن حذف خدمات ويندوز أو تطبيقات المتجر من إدارة بدء التشغيل لحماية النظام. استخدم زر «تبديل» لتعطيل تشغيلها عند الإقلاع." : "Windows Services and Microsoft Store apps cannot be permanently deleted here for system safety. Use 'Toggle' to disable their auto-start on boot.")
                    : (isAr ? "لا يمكن حذف تطبيقات متجر مايكروسوفت من إدارة بدء التشغيل. استخدم زر 'تبديل' أو 'تعطيل الكل' لمنع تشغيلها عند الإقلاع." : "Microsoft Store apps cannot be deleted from Startup Manager. Use 'Toggle' or 'Disable All to Never' to stop them from running on boot.");
                ShowActionFeedback(infoMsg, AppDialogTone.Info);
                AppDialog.Show(this, infoMsg, LocalizationService.T("AppTitle", "PC Boost"), AppDialogTone.Info);
                return;
            }

            string prompt = deletable.Count == 1
                ? (isAr ? $"هل أنت متأكد من رغبتك في حذف إدخال بدء التشغيل: {deletable[0].Name}؟" : $"Are you sure you want to permanently delete startup entry: {deletable[0].Name}?")
                : (isAr ? $"هل أنت متأكد من رغبتك في حذف عدد {deletable.Count} من عناصر بدء التشغيل المحددة نهائياً؟" : $"Are you sure you want to permanently delete {deletable.Count} checked startup entries?");

            if (!AppDialog.Confirm(this, prompt, LocalizationService.T("AppTitle", "PC Boost"), AppDialogTone.Warning, isDestructive: true)) return;

            SetActionBusy(true);
            ShowActionFeedback(
                isAr ? $"جاري حذف {deletable.Count} عنصر..." : $"Deleting {deletable.Count} startup entry(s)...",
                AppDialogTone.Info);

            try
            {
                var results = await Task.Run(() =>
                    deletable.Select(item => (Item: item, Result: startupService.DeleteItemDetailed(item))).ToList());

                var needAdmin = results
                    .Where(r => !r.Result.Success && r.Result.FailureReason == StartupOpFailureReason.RequiresAdministrator)
                    .ToList();

                if (needAdmin.Count > 0 && !AdminPrivilegeService.IsRunningAsAdmin())
                {
                    string elevPrompt = isAr
                        ? $"يتطلب حذف {needAdmin.Count} عنصر صلاحيات المسؤول.\n\nهل تريد السماح بصلاحيات المسؤول (UAC) للحذف الآن؟"
                        : $"Administrator privileges are required to delete {needAdmin.Count} startup entry(s).\n\nElevate via Windows UAC to delete now?";

                    if (AppDialog.Confirm(this, elevPrompt, LocalizationService.T("AppTitle", "PC Boost"), AppDialogTone.Question))
                    {
                        var keysArg = string.Join(" ", needAdmin.Select(x => $"\"{x.Item.MonitorKey.Replace("\"", "\\\"")}\""));
                        bool ok = await Task.Run(() => AdminPrivilegeService.TryRunElevatedSelfCommand($"--delete-startup {keysArg}", out _, out _));
                        if (ok)
                        {
                            for (int i = 0; i < results.Count; i++)
                            {
                                if (needAdmin.Any(n => ReferenceEquals(n.Item, results[i].Item)))
                                    results[i] = (results[i].Item, StartupOperationResult.Ok());
                            }
                        }
                    }
                }

                var deleted = new List<StartupItem>();
                var failed = new List<(StartupItem Item, StartupOperationResult Result)>();

                foreach (var r in results)
                {
                    if (r.Result.Success)
                    {
                        allItems.Remove(r.Item);
                        ItemRemoved?.Invoke(r.Item);
                        deleted.Add(r.Item);
                    }
                    else
                    {
                        failed.Add((r.Item, r.Result));
                    }
                }

                if (deleted.Count > 0)
                {
                    UpdateTabCounts();
                    ApplyFilter();
                }

                if (failed.Count == 0 && deleted.Count > 0)
                {
                    string msg = isAr
                        ? $"تم حذف {deleted.Count} عنصر بدء تشغيل: {FormatNamesPreview(deleted.Select(x => x.Name))}"
                        : $"Deleted {deleted.Count} startup entry(s): {FormatNamesPreview(deleted.Select(x => x.Name))}";
                    ShowActionFeedback(msg, AppDialogTone.Success);
                    StatusMessage?.Invoke($"[Startup Manager] {msg}");
                }
                else if (failed.Count > 0)
                {
                    bool anyNeedsAdmin = failed.Any(f => f.Result.FailureReason == StartupOpFailureReason.RequiresAdministrator)
                                         && !AdminPrivilegeService.IsRunningAsAdmin();
                    string bannerMsg = isAr
                        ? $"تم حذف {deleted.Count} عنصر وتعذر حذف {failed.Count} ({FormatNamesPreview(failed.Select(f => f.Item.Name), 2)})."
                        : $"Deleted {deleted.Count} entry(s); failed to delete {failed.Count} ({FormatNamesPreview(failed.Select(f => f.Item.Name), 2)}).";
                    ShowActionFeedback(bannerMsg, AppDialogTone.Warning, showElevateButton: anyNeedsAdmin);
                    StatusMessage?.Invoke($"[Startup Manager] {bannerMsg}");

                    var details = string.Join("\n", failed.Take(6).Select(f => $"• {f.Item.Name}: {f.Result.Message}"));
                    AppDialog.Show(this,
                        (isAr ? $"تعذر حذف {failed.Count} عنصر:\n\n" : $"Could not delete {failed.Count} startup entry(s):\n\n") + details,
                        LocalizationService.T("AppTitle", "PC Boost"),
                        AppDialogTone.Warning);
                }
            }
            finally
            {
                SetActionBusy(false);
            }
        }

        public void SyncExternalItem(StartupItem updatedItem)
        {
            if (allItems.Count == 0)
            {
                UpdateTabCounts();
                return;
            }

            var existing = allItems.FirstOrDefault(x =>
                string.Equals(x.MonitorKey, updatedItem.MonitorKey, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.IsEnabled = updatedItem.IsEnabled;
                existing.IsIgnored = updatedItem.IsIgnored;
                existing.IsUnknown = updatedItem.IsUnknown;
                existing.BackgroundPermissionMode = updatedItem.BackgroundPermissionMode;
                existing.Command = updatedItem.Command;
            }
            else
            {
                allItems.Add(updatedItem);
            }

            UpdateTabCounts();
            ApplyFilter();
            gridStartup.Items.Refresh();
        }

        private DataGridTemplateColumn CreateQuickCheckColumn()
        {
            bool isQuick = activeFilterTab == "Quick";
            lastCheckColumnQuickMode = isQuick;

            chkQuickSelectAll = new CheckBox
            {
                IsChecked = false,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            chkQuickSelectAll.Click += (s, e) =>
            {
                bool targetState = chkQuickSelectAll.IsChecked == true;
                bool qm = activeFilterTab == "Quick";
                if (gridStartup.ItemsSource is IEnumerable<StartupItem> visibleItems)
                {
                    foreach (var it in visibleItems)
                    {
                        if (qm)
                        {
                            if (!it.IsIgnored) it.IsQuickSelected = targetState;
                        }
                        else
                        {
                            it.IsSelected = targetState;
                        }
                    }
                }
                UpdateSummaryStrip();
                UpdateSelectionActionsState();
            };

            return new DataGridTemplateColumn
            {
                Header = chkQuickSelectAll,
                Width = new DataGridLength(36),
                MinWidth = 36,
                MaxWidth = 36,
                CanUserResize = false,
                CanUserSort = false,
                Visibility = Visibility.Visible,
                CellTemplate = CreateQuickCheckCellTemplate(isQuick),
                IsReadOnly = true
            };
        }

        private DataTemplate CreateQuickCheckCellTemplate(bool quickMode)
        {
            var cb = new FrameworkElementFactory(typeof(CheckBox));
            cb.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cb.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            cb.SetValue(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand);
            if (quickMode)
            {
                cb.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding("QuickCheckboxVisibility"));
            }
            cb.SetBinding(ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding(quickMode ? "IsQuickSelected" : "IsSelected")
            {
                Mode = System.Windows.Data.BindingMode.TwoWay,
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            });
            cb.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnRowCheckBoxClick));
            return new DataTemplate { VisualTree = cb };
        }

        private void OnRowCheckBoxClick(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is StartupItem clickedItem)
            {
                bool isQuick = activeFilterTab == "Quick";
                bool targetChecked = cb.IsChecked == true;
                bool shiftDown = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) == System.Windows.Input.ModifierKeys.Shift;

                if (shiftDown && lastClickedCheckItem != null && gridStartup.ItemsSource is IEnumerable<StartupItem> visible)
                {
                    var list = visible.ToList();
                    int startIdx = list.IndexOf(lastClickedCheckItem);
                    int endIdx = list.IndexOf(clickedItem);
                    if (startIdx >= 0 && endIdx >= 0)
                    {
                        int from = Math.Min(startIdx, endIdx);
                        int to = Math.Max(startIdx, endIdx);
                        for (int i = from; i <= to; i++)
                        {
                            var rowItem = list[i];
                            if (isQuick)
                            {
                                if (!rowItem.IsIgnored) rowItem.IsQuickSelected = targetChecked;
                            }
                            else
                            {
                                rowItem.IsSelected = targetChecked;
                            }
                        }
                    }
                }
                lastClickedCheckItem = clickedItem;
            }

            SyncQuickSelectAllCheckBox();
            UpdateSummaryStrip();
            UpdateSelectionActionsState();
        }

        private void SyncQuickSelectAllCheckBox()
        {
            if (chkQuickSelectAll == null) return;
            bool isQuick = activeFilterTab == "Quick";
            var visible = (gridStartup.ItemsSource as IEnumerable<StartupItem> ?? Array.Empty<StartupItem>()).ToList();
            var candidates = isQuick ? visible.Where(x => !x.IsIgnored).ToList() : visible;
            if (candidates.Count == 0)
            {
                chkQuickSelectAll.IsEnabled = false;
                chkQuickSelectAll.IsChecked = false;
                return;
            }

            chkQuickSelectAll.IsEnabled = true;
            int checkedCount = isQuick
                ? candidates.Count(x => x.IsQuickSelected)
                : candidates.Count(x => x.IsSelected);
            chkQuickSelectAll.IsChecked = checkedCount == 0
                ? false
                : (checkedCount == candidates.Count ? true : (bool?)null);
        }

        private DataGridTemplateColumn CreateNameColumn()
        {
            return new DataGridTemplateColumn
            {
                Header = LocalizationService.T("StartupColName", "Program / Service Name"),
                SortMemberPath = "Name",
                MinWidth = 144,
                Width = new DataGridLength(1.44, DataGridLengthUnitType.Star),
                CellTemplate = CreateNameCellTemplate(),
                IsReadOnly = true
            };
        }

        private DataTemplate CreateNameCellTemplate()
        {
            bool isAr = LocalizationService.IsArabic;
            bool isDark = WpfTheme.IsDarkMode;
            var dock = new FrameworkElementFactory(typeof(DockPanel));
            dock.SetValue(DockPanel.LastChildFillProperty, true);
            dock.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            var badge = new FrameworkElementFactory(typeof(Border));
            badge.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding("IsUnknownVisibility"));
            badge.SetValue(DockPanel.DockProperty, Dock.Right);
            badge.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            badge.SetValue(Border.PaddingProperty, new Thickness(5, 1, 5, 1));
            badge.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 0, 0));
            badge.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            badge.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(38, 245, 158, 11)));
            badge.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(130, 245, 158, 11)));
            badge.SetValue(Border.BorderThicknessProperty, new Thickness(1));

            var badgeTxt = new FrameworkElementFactory(typeof(TextBlock));
            badgeTxt.SetValue(TextBlock.TextProperty, isAr ? "جديد" : "NEW");
            badgeTxt.SetValue(TextBlock.FontSizeProperty, 10.0);
            badgeTxt.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            badgeTxt.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(245, 158, 11)));
            badge.AppendChild(badgeTxt);
            dock.AppendChild(badge);

            var ignBadge = new FrameworkElementFactory(typeof(Border));
            ignBadge.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding("IsIgnoredBadgeVisibility"));
            ignBadge.SetValue(DockPanel.DockProperty, Dock.Right);
            ignBadge.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            ignBadge.SetValue(Border.PaddingProperty, new Thickness(5, 1, 5, 1));
            ignBadge.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 0, 0));
            ignBadge.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            ignBadge.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(30, 148, 163, 184)));
            ignBadge.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(120, 148, 163, 184)));
            ignBadge.SetValue(Border.BorderThicknessProperty, new Thickness(1));

            var ignBadgeTxt = new FrameworkElementFactory(typeof(TextBlock));
            ignBadgeTxt.SetValue(TextBlock.TextProperty, LocalizationService.T("StartupTagIgnored", isAr ? "متجاهل" : "Ignored"));
            ignBadgeTxt.SetValue(TextBlock.FontSizeProperty, 10.0);
            ignBadgeTxt.SetValue(TextBlock.FontWeightProperty, FontWeights.Medium);
            ignBadgeTxt.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(isDark ? Color.FromRgb(148, 163, 184) : Color.FromRgb(71, 85, 105)));
            ignBadge.AppendChild(ignBadgeTxt);
            dock.AppendChild(ignBadge);

            var tb = new FrameworkElementFactory(typeof(TextBlock));
            tb.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Name"));
            tb.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            tb.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            tb.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            tb.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 0, 2, 0));
            if (isAr)
            {
                tb.SetBinding(FrameworkElement.FlowDirectionProperty,
                    new System.Windows.Data.Binding("Name") { Converter = new LtrFlowDirectionConverter() });
                tb.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Right);
            }
            dock.AppendChild(tb);

            return new DataTemplate { VisualTree = dock };
        }

        private DataGridTemplateColumn CreateOperationColumn()
        {
            return new DataGridTemplateColumn
            {
                Header = LocalizationService.T("StartupColAction", "Operation"),
                SortMemberPath = "IsIgnored",
                MinWidth = 76,
                Width = new DataGridLength(78),
                CellTemplate = CreateOperationCellTemplate(),
                IsReadOnly = true
            };
        }

        private DataTemplate CreateOperationCellTemplate()
        {
            bool isDark = WpfTheme.IsDarkMode;
            var normalFg = new SolidColorBrush(isDark ? Color.FromRgb(96, 165, 250) : Color.FromRgb(37, 99, 235));
            var unignoreFg = new SolidColorBrush(isDark ? Color.FromRgb(251, 146, 60) : Color.FromRgb(217, 119, 6));

            var tpl = new DataTemplate();

            var btn = new FrameworkElementFactory(typeof(Button));
            btn.SetValue(Control.BackgroundProperty, Brushes.Transparent);
            btn.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            btn.SetValue(Control.PaddingProperty, new Thickness(4, 2, 4, 2));
            btn.SetValue(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand);
            btn.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            btn.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            btn.AddHandler(Button.ClickEvent, new RoutedEventHandler(OnRowIgnoreClick));

            var flatTpl = new ControlTemplate(typeof(Button));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            flatTpl.VisualTree = cp;
            btn.SetValue(Control.TemplateProperty, flatTpl);

            var tb = new FrameworkElementFactory(typeof(TextBlock));
            tb.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("IgnoreActionText"));
            tb.SetValue(TextBlock.TextDecorationsProperty, TextDecorations.Underline);
            tb.SetValue(TextBlock.FontSizeProperty, 12.0);
            tb.SetValue(TextBlock.FontWeightProperty, FontWeights.Medium);

            var txtStyle = new Style(typeof(TextBlock));
            txtStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, normalFg));
            var trig = new DataTrigger
            {
                Binding = new System.Windows.Data.Binding("IsIgnored"),
                Value = true
            };
            trig.Setters.Add(new Setter(TextBlock.ForegroundProperty, unignoreFg));
            txtStyle.Triggers.Add(trig);
            tb.SetValue(FrameworkElement.StyleProperty, txtStyle);

            btn.AppendChild(tb);
            tpl.VisualTree = btn;
            return tpl;
        }

        private void OnRowIgnoreClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is StartupItem item)
            {
                if (item.IsIgnored)
                {
                    if (attachedMonitor != null)
                        attachedMonitor.UnignoreItem(item);
                    else
                        item.IsIgnored = false;
                }
                else
                {
                    if (attachedMonitor != null)
                        attachedMonitor.IgnoreItem(item);
                    else
                    {
                        item.IsIgnored = true;
                        item.IsUnknown = false;
                    }
                }

                UpdateTabCounts();
                if (activeFilterTab == "Unknown" || activeFilterTab == "Ignored" || activeFilterTab == "Quick")
                {
                    ApplyFilter();
                }
                else
                {
                    gridStartup.Items.Refresh();
                }
            }
        }

        private void MarkAllUnknownAsKnown()
        {
            foreach (var item in allItems)
            {
                item.IsUnknown = false;
            }
            attachedMonitor?.MarkAllUnknownAsKnown();
            UpdateTabCounts();
            if (activeFilterTab == "Unknown")
            {
                ApplyFilter();
            }
            else
            {
                gridStartup.Items.Refresh();
            }
        }

        private async Task OptimizeSafeItemsAsync()
        {
            if (isActionBusy) return;
            bool isQuick = activeFilterTab == "Quick";
            var targets = (isQuick
                    ? allItems.Where(x => IsActiveQuickOptimizable(x) && x.IsQuickSelected)
                    : allItems.Where(x => x.IsEnabled && !x.IsIgnored && x.Recommendation == StartupRecommendation.SafeToDisable))
                .ToList();

            bool isAr = LocalizationService.IsArabic;
            if (targets.Count == 0)
            {
                if (!isQuick && allItems.Any(x => x.IsUnknown && !x.IsIgnored))
                {
                    SelectFilterTab("Quick");
                }
                return;
            }

            string prompt = isAr
                ? $"هل تريد تعطيل عدد {targets.Count} من عناصر بدء التشغيل المحددة وغير المتجاهلة لتسريع الإقلاع؟"
                : $"Disable {targets.Count} selected non-ignored startup item(s) to speed up PC boot?";
            if (!AppDialog.Confirm(this, prompt, LocalizationService.T("AppTitle", "PC Boost"), AppDialogTone.Question)) return;

            SetActionBusy(true);
            ShowActionFeedback(
                isAr ? $"جاري تعطيل {targets.Count} عناصر لتحسين الإقلاع..." : $"Disabling {targets.Count} startup item(s)...",
                AppDialogTone.Info);

            try
            {
                var results = await Task.Run(() =>
                    targets.Select(t => (Item: t, TargetEnable: false, Result: startupService.SetItemEnabledDetailed(t, false))).ToList());

                var needAdmin = results
                    .Where(r => !r.Result.Success && r.Result.FailureReason == StartupOpFailureReason.RequiresAdministrator)
                    .ToList();

                if (needAdmin.Count > 0 && !AdminPrivilegeService.IsRunningAsAdmin())
                {
                    string elevPrompt = isAr
                        ? $"يتطلب تعطيل {needAdmin.Count} عنصر صلاحيات المسؤول.\n\nهل تريد السماح بصلاحيات المسؤول (UAC) لإكمال التحسين؟"
                        : $"Administrator privileges are required to disable {needAdmin.Count} startup item(s).\n\nElevate via Windows UAC to finish optimizing?";

                    if (AppDialog.Confirm(this, elevPrompt, LocalizationService.T("AppTitle", "PC Boost"), AppDialogTone.Question))
                    {
                        var elevatedMap = await RunElevatedToggleBatchAsync(needAdmin);
                        for (int i = 0; i < results.Count; i++)
                        {
                            if (elevatedMap.TryGetValue(results[i].Item, out var updated))
                            {
                                results[i] = (results[i].Item, false, updated);
                            }
                        }
                    }
                }

                int changed = 0;
                var succeededOptimize = new List<(StartupItem Item, bool PreviousEnabled, bool NewEnabled)>();
                var failed = new List<(StartupItem Item, StartupOperationResult Result)>();

                foreach (var r in results)
                {
                    if (r.Result.Success)
                    {
                        bool prev = r.Item.IsEnabled;
                        r.Item.IsEnabled = false;
                        r.Item.IsUnknown = false;
                        attachedMonitor?.MarkKnownState(r.Item);
                        succeededOptimize.Add((r.Item, prev, false));
                        changed++;
                    }
                    else
                    {
                        failed.Add((r.Item, r.Result));
                    }
                }

                if (succeededOptimize.Count > 0)
                {
                    StartupHistoryService.Instance.RecordChanges("Optimize Safe", succeededOptimize);
                }

                UpdateTabCounts();
                ApplyFilter();
                gridStartup.Items.Refresh();

                if (failed.Count == 0)
                {
                    string msg = isAr
                        ? $"تم تحسين بدء التشغيل بنجاح: تم تعطيل {changed} عنصر."
                        : $"Startup optimized: disabled {changed} item(s).";
                    ShowActionFeedback(msg, AppDialogTone.Success);
                    StatusMessage?.Invoke($"[Startup Manager] {msg}");
                    AppDialog.Show(this, msg, LocalizationService.T("AppTitle", "PC Boost"), AppDialogTone.Success);
                }
                else
                {
                    bool anyNeedsAdmin = failed.Any(f => f.Result.FailureReason == StartupOpFailureReason.RequiresAdministrator)
                                         && !AdminPrivilegeService.IsRunningAsAdmin();
                    string bannerMsg = isAr
                        ? $"تم تعطيل {changed} عنصر • تعذر تعطيل {failed.Count} ({FormatNamesPreview(failed.Select(f => f.Item.Name), 2)})."
                        : $"Disabled {changed} item(s) • {failed.Count} could not be disabled ({FormatNamesPreview(failed.Select(f => f.Item.Name), 2)}).";
                    ShowActionFeedback(bannerMsg, AppDialogTone.Warning, showElevateButton: anyNeedsAdmin);
                    StatusMessage?.Invoke($"[Startup Manager] {bannerMsg}");

                    var details = string.Join("\n", failed.Take(6).Select(f => $"• {f.Item.Name}: {f.Result.Message}"));
                    string dlgMsg = isAr
                        ? $"تم تعطيل {changed} عنصر.\n\nتعذر تعديل {failed.Count} عنصر:\n{details}"
                        : $"Disabled {changed} item(s).\n\nCould not modify {failed.Count} item(s):\n{details}";
                    AppDialog.Show(this, dlgMsg, LocalizationService.T("AppTitle", "PC Boost"), AppDialogTone.Warning);
                }
            }
            finally
            {
                SetActionBusy(false);
            }
        }

    }
}
