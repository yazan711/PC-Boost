using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CleanRamBoosterApp.Models;
using Microsoft.Win32;

namespace CleanRamBoosterApp.Services
{
    public partial class StartupService
    {
        public bool ToggleStartupItem(StartupItem item, bool enable)
            => ToggleStartupItemDetailed(item, enable).Success;

        public StartupOperationResult ToggleStartupItemDetailed(StartupItem item, bool enable)
        {
            bool isAr = LocalizationService.IsArabic;
            bool isAdmin = AdminPrivilegeService.IsRunningAsAdmin();

            try
            {
                if (item.LocationType is StartupType.RegistryUser or StartupType.RegistryMachine or StartupType.RegistryWow64)
                {
                    RegistryKey root = item.LocationType == StartupType.RegistryUser ? Registry.CurrentUser : Registry.LocalMachine;
                    string approvedPath = GetStartupApprovedSubKey(item.LocationType);
                    string valName = !string.IsNullOrEmpty(item.RegistryValueName) ? item.RegistryValueName : item.Name;

                    using var approvedKey = root.CreateSubKey(approvedPath, true);
                    if (approvedKey != null)
                    {
                        byte[] data = enable ? new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }
                                             : new byte[] { 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                        approvedKey.SetValue(valName, data, RegistryValueKind.Binary);
                        item.IsEnabled = enable;
                        return StartupOperationResult.Ok();
                    }
                    return ClassifyAccessFailure(isAdmin, isAr, "Could not open StartupApproved registry key.");
                }
                else if (item.LocationType is StartupType.StartupFolderUser or StartupType.StartupFolderCommon)
                {
                    RegistryKey root = item.LocationType == StartupType.StartupFolderUser ? Registry.CurrentUser : Registry.LocalMachine;
                    string approvedPath = GetStartupApprovedSubKey(item.LocationType);
                    string fileName = !string.IsNullOrEmpty(item.RegistryValueName) ? item.RegistryValueName : Path.GetFileName(item.FilePath);
                    if (string.IsNullOrEmpty(fileName))
                    {
                        return StartupOperationResult.Fail(StartupOpFailureReason.NotFound,
                            isAr ? "ملف اختصار بدء التشغيل غير موجود." : "Startup shortcut file was not found.");
                    }

                    using var approvedKey = root.CreateSubKey(approvedPath, true);
                    if (approvedKey != null)
                    {
                        byte[] data = enable ? new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }
                                             : new byte[] { 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                        approvedKey.SetValue(fileName, data, RegistryValueKind.Binary);
                        item.IsEnabled = enable;
                        return StartupOperationResult.Ok();
                    }
                    return ClassifyAccessFailure(isAdmin, isAr, "Could not open StartupFolder StartupApproved key.");
                }
                else if (item.LocationType == StartupType.WindowsService && !string.IsNullOrEmpty(item.ServiceName))
                {
                    // 1. Native SCM ChangeServiceConfigW (instant, no process spawn, exact Win32 error + per-user template support)
                    if (WinServiceManager.SetServiceStartType(item.ServiceName, enable, out int win32Err))
                    {
                        item.IsEnabled = enable;
                        return StartupOperationResult.Ok();
                    }

                    if (win32Err == WinServiceManager.ERROR_SERVICE_DOES_NOT_EXIST)
                    {
                        return StartupOperationResult.Fail(StartupOpFailureReason.NotFound,
                            isAr ? "الخدمة غير موجودة في النظام." : "Windows service no longer exists.", win32Err);
                    }

                    if (win32Err == WinServiceManager.ERROR_ACCESS_DENIED)
                    {
                        if (!isAdmin)
                        {
                            return StartupOperationResult.Fail(StartupOpFailureReason.RequiresAdministrator,
                                isAr ? "يتطلب صلاحيات المسؤول لتعديل خدمات ويندوز." : "Requires Administrator privileges to modify Windows services.", win32Err);
                        }

                        // Already running as Administrator: verify via sc.exe fallback in case of SCM quirk,
                        // otherwise classify accurately as a Windows-protected / TrustedInstaller / PPL service.
                        string startCmd = enable ? "auto" : "demand";
                        if (RunDirectTool("sc.exe", $"config \"{item.ServiceName}\" start= {startCmd}", 6000, out string scOut, out string scErr, out _))
                        {
                            item.IsEnabled = enable;
                            return StartupOperationResult.Ok();
                        }

                        return StartupOperationResult.Fail(StartupOpFailureReason.ProtectedByWindows,
                            isAr
                                ? "محمية من قِبل ويندوز (خدمة نظام/أمان/TrustedInstaller لا يمكن تعديلها حتى بصلاحيات المسؤول)."
                                : "Protected by Windows (System / Security / TrustedInstaller service cannot be modified even as Administrator).",
                            win32Err);
                    }

                    // Fallback to sc.exe for any other non-access Win32 error
                    string fallbackStart = enable ? "auto" : "demand";
                    if (RunDirectTool("sc.exe", $"config \"{item.ServiceName}\" start= {fallbackStart}", 6000, out string outText, out string errText, out int scExit))
                    {
                        item.IsEnabled = enable;
                        return StartupOperationResult.Ok();
                    }

                    string detail = !string.IsNullOrWhiteSpace(errText) ? errText : (!string.IsNullOrWhiteSpace(outText) ? outText : $"Win32 Error {win32Err}");
                    return StartupOperationResult.Fail(StartupOpFailureReason.Error, detail, win32Err != 0 ? win32Err : scExit);
                }
                else if (item.LocationType == StartupType.ScheduledTask && !string.IsNullOrEmpty(item.TaskName))
                {
                    string normalizedTask = "\\" + item.TaskName.TrimStart('\\');

                    // 1. Prefer native Task Scheduler 2.0 COM API (handles SYSTEM / S4U / updater tasks where schtasks /change fails)
                    if (TryToggleScheduledTaskCom(normalizedTask, enable, out int comHr, out string comErr))
                    {
                        item.IsEnabled = enable;
                        return StartupOperationResult.Ok();
                    }

                    // 2. Fallback to schtasks.exe
                    string action = enable ? "/enable" : "/disable";
                    if (RunDirectTool("schtasks.exe", $"/change /tn \"{normalizedTask}\" {action}", 8000, out string stdOut, out string stdErr, out int exitCode))
                    {
                        item.IsEnabled = enable;
                        return StartupOperationResult.Ok();
                    }

                    string combined = $"{stdErr} {stdOut} {comErr}".Trim();
                    bool isAccessDenied = comHr == unchecked((int)0x80070005) ||
                                          combined.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
                                          combined.Contains("access denied", StringComparison.OrdinalIgnoreCase);

                    if (isAccessDenied)
                    {
                        return ClassifyAccessFailure(isAdmin, isAr, combined);
                    }

                    if (comHr == unchecked((int)0x80070002) || combined.Contains("cannot find", StringComparison.OrdinalIgnoreCase))
                    {
                        return StartupOperationResult.Fail(StartupOpFailureReason.NotFound,
                            isAr ? "المهمة المجدولة لم تعد موجودة." : "Scheduled task no longer exists.");
                    }

                    return StartupOperationResult.Fail(StartupOpFailureReason.Error,
                        string.IsNullOrWhiteSpace(combined) ? $"Task update failed (exit code {exitCode})." : combined,
                        exitCode);
                }
                else if (item.LocationType == StartupType.MicrosoftStoreApp && !string.IsNullOrEmpty(item.PackageFamilyName))
                {
                    using var bgKey = Registry.CurrentUser.CreateSubKey($@"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications\{item.PackageFamilyName}", true);
                    if (bgKey != null)
                    {
                        int disabledVal = enable ? 0 : 1;
                        bgKey.SetValue("Disabled", disabledVal, RegistryValueKind.DWord);
                        bgKey.SetValue("DisabledByUser", disabledVal, RegistryValueKind.DWord);
                        bgKey.SetValue("IgnoreBatterySaver", 0, RegistryValueKind.DWord);
                        bgKey.SetValue("SleepDisabled", disabledVal, RegistryValueKind.DWord);
                        bgKey.SetValue("SleepIgnoreBatterySaver", 0, RegistryValueKind.DWord);

                        item.IsEnabled = enable;
                        item.BackgroundPermissionMode = enable ? "Power Optimized" : "Never";
                        item.Command = BuildStoreAppCommandSummary(item.BackgroundPermissionMode, item.PackageFamilyName);
                        return StartupOperationResult.Ok();
                    }
                    return StartupOperationResult.Fail(StartupOpFailureReason.Error,
                        isAr ? "تعذر فتح مفتاح أذونات تطبيقات المتجر في السجل." : "Could not open Store app background permissions registry key.");
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                return ClassifyAccessFailure(isAdmin, isAr, ex.Message);
            }
            catch (SecurityException ex)
            {
                return ClassifyAccessFailure(isAdmin, isAr, ex.Message);
            }
            catch (Exception ex)
            {
                return StartupOperationResult.Fail(StartupOpFailureReason.Error, ex.Message);
            }

            return StartupOperationResult.Fail(StartupOpFailureReason.NotSupported,
                isAr ? "نوع عنصر بدء التشغيل غير مدعوم." : "Unsupported startup entry type.");
        }

        public bool DeleteStartupItem(StartupItem item)
            => DeleteStartupItemDetailed(item).Success;

        public StartupOperationResult DeleteStartupItemDetailed(StartupItem item)
        {
            bool isAr = LocalizationService.IsArabic;
            bool isAdmin = AdminPrivilegeService.IsRunningAsAdmin();

            try
            {
                if (item.LocationType is StartupType.RegistryUser or StartupType.RegistryMachine or StartupType.RegistryWow64)
                {
                    RegistryKey root = item.LocationType == StartupType.RegistryUser ? Registry.CurrentUser : Registry.LocalMachine;
                    string valName = !string.IsNullOrEmpty(item.RegistryValueName) ? item.RegistryValueName : item.Name;
                    using var key = root.OpenSubKey(item.OriginalRegistryKey, true);
                    if (key != null)
                    {
                        key.DeleteValue(valName, false);
                        return StartupOperationResult.Ok();
                    }
                    return ClassifyAccessFailure(isAdmin, isAr, "Could not open startup registry key for deletion.");
                }
                else if (item.LocationType is StartupType.StartupFolderUser or StartupType.StartupFolderCommon)
                {
                    if (!File.Exists(item.FilePath))
                    {
                        return StartupOperationResult.Fail(StartupOpFailureReason.NotFound,
                            isAr ? "ملف بدء التشغيل غير موجود بالفعل." : "Startup file no longer exists.");
                    }
                    var attrs = File.GetAttributes(item.FilePath);
                    if ((attrs & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != 0)
                    {
                        File.SetAttributes(item.FilePath, FileAttributes.Normal);
                    }
                    File.Delete(item.FilePath);
                    return StartupOperationResult.Ok();
                }
                else if (item.LocationType == StartupType.ScheduledTask && !string.IsNullOrEmpty(item.TaskName))
                {
                    string normalizedTask = "\\" + item.TaskName.TrimStart('\\');
                    if (TryDeleteScheduledTaskCom(normalizedTask, out int comHr, out string comErr))
                    {
                        return StartupOperationResult.Ok();
                    }

                    if (RunDirectTool("schtasks.exe", $"/delete /tn \"{normalizedTask}\" /f", 8000, out string stdOut, out string stdErr, out int exitCode))
                    {
                        return StartupOperationResult.Ok();
                    }

                    string combined = $"{stdErr} {stdOut} {comErr}".Trim();
                    bool isAccessDenied = comHr == unchecked((int)0x80070005) ||
                                          combined.Contains("Access is denied", StringComparison.OrdinalIgnoreCase);
                    if (isAccessDenied) return ClassifyAccessFailure(isAdmin, isAr, combined);

                    return StartupOperationResult.Fail(StartupOpFailureReason.Error,
                        string.IsNullOrWhiteSpace(combined) ? $"Could not delete scheduled task (exit code {exitCode})." : combined,
                        exitCode);
                }
                else if (item.LocationType == StartupType.WindowsService)
                {
                    return StartupOperationResult.Fail(StartupOpFailureReason.NotSupported,
                        isAr
                            ? "لا يمكن حذف خدمات ويندوز من هنا لحماية النظام. استخدم زر «تبديل» لتعطيل تشغيلها التلقائي عند الإقلاع."
                            : "Windows Services cannot be permanently deleted here for system safety. Use 'Toggle' to disable auto-start on boot.");
                }
                else if (item.LocationType == StartupType.MicrosoftStoreApp)
                {
                    return StartupOperationResult.Fail(StartupOpFailureReason.NotSupported,
                        isAr
                            ? "لا يمكن حذف تطبيقات متجر مايكروسوفت من إدارة بدء التشغيل. استخدم زر «تبديل» لمنع تشغيلها في الخلفية."
                            : "Microsoft Store apps cannot be deleted from Startup Manager. Use 'Toggle' to disable background startup.");
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                return ClassifyAccessFailure(isAdmin, isAr, ex.Message);
            }
            catch (SecurityException ex)
            {
                return ClassifyAccessFailure(isAdmin, isAr, ex.Message);
            }
            catch (Exception ex)
            {
                return StartupOperationResult.Fail(StartupOpFailureReason.Error, ex.Message);
            }

            return StartupOperationResult.Fail(StartupOpFailureReason.NotSupported,
                isAr ? "نوع العنصر غير مدعوم للحذف." : "Entry type does not support deletion.");
        }

        private static StartupOperationResult ClassifyAccessFailure(bool isAdmin, bool isAr, string rawDetail)
        {
            if (!isAdmin)
            {
                return StartupOperationResult.Fail(
                    StartupOpFailureReason.RequiresAdministrator,
                    isAr
                        ? "يتطلب صلاحيات المسؤول لتعديل هذا العنصر."
                        : "Requires Administrator privileges to modify this entry.",
                    WinServiceManager.ERROR_ACCESS_DENIED);
            }

            return StartupOperationResult.Fail(
                StartupOpFailureReason.ProtectedByWindows,
                isAr
                    ? "محمي من قِبل نظام ويندوز (لا يمكن تعديله حتى بصلاحيات المسؤول)."
                    : "Protected by Windows (cannot be modified even as Administrator).",
                WinServiceManager.ERROR_ACCESS_DENIED);
        }

        private static bool RunDirectTool(string fileName, string arguments, int timeoutMs, out string stdOut, out string stdErr, out int exitCode)
        {
            stdOut = string.Empty;
            stdErr = string.Empty;
            exitCode = -1;
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                if (p == null) return false;
                var outTask = p.StandardOutput.ReadToEndAsync();
                var errTask = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(true); } catch { }
                    stdErr = "Timed out.";
                    return false;
                }
                stdOut = outTask.GetAwaiter().GetResult().Trim();
                stdErr = errTask.GetAwaiter().GetResult().Trim();
                exitCode = p.ExitCode;
                return exitCode == 0;
            }
            catch (Exception ex)
            {
                stdErr = ex.Message;
                return false;
            }
        }

        private static bool TryToggleScheduledTaskCom(string normalizedTaskPath, bool enable, out int hresult, out string error)
        {
            hresult = 0;
            error = string.Empty;
            try
            {
                Type? schedType = Type.GetTypeFromProgID("Schedule.Service");
                if (schedType == null) return false;
                dynamic? scheduler = Activator.CreateInstance(schedType);
                if (scheduler == null) return false;
                try
                {
                    scheduler.Connect();
                    dynamic rootFolder = scheduler.GetFolder("\\");
                    dynamic task = rootFolder.GetTask(normalizedTaskPath);
                    task.Enabled = enable;
                    return true;
                }
                finally
                {
                    try { Marshal.FinalReleaseComObject(scheduler); } catch { }
                }
            }
            catch (COMException cex)
            {
                hresult = cex.ErrorCode;
                error = cex.Message;
                return false;
            }
            catch (UnauthorizedAccessException uex)
            {
                hresult = unchecked((int)0x80070005);
                error = uex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool TryDeleteScheduledTaskCom(string normalizedTaskPath, out int hresult, out string error)
        {
            hresult = 0;
            error = string.Empty;
            try
            {
                Type? schedType = Type.GetTypeFromProgID("Schedule.Service");
                if (schedType == null) return false;
                dynamic? scheduler = Activator.CreateInstance(schedType);
                if (scheduler == null) return false;
                try
                {
                    scheduler.Connect();
                    string trimmed = normalizedTaskPath.TrimStart('\\');
                    int slash = trimmed.LastIndexOf('\\');
                    string folderPath = slash > 0 ? "\\" + trimmed.Substring(0, slash) : "\\";
                    string taskLeaf = slash > 0 ? trimmed.Substring(slash + 1) : trimmed;
                    dynamic folder = scheduler.GetFolder(folderPath);
                    folder.DeleteTask(taskLeaf, 0);
                    return true;
                }
                finally
                {
                    try { Marshal.FinalReleaseComObject(scheduler); } catch { }
                }
            }
            catch (COMException cex)
            {
                hresult = cex.ErrorCode;
                error = cex.Message;
                return false;
            }
            catch (UnauthorizedAccessException uex)
            {
                hresult = unchecked((int)0x80070005);
                error = uex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string ExtractFilePath(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return string.Empty;
            string clean = command.Trim();
            if (clean.StartsWith("\""))
            {
                int end = clean.IndexOf('\"', 1);
                if (end > 0) return clean.Substring(1, end - 1);
            }
            int spaceIdx = clean.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (spaceIdx > 0) return clean.Substring(0, spaceIdx + 4);
            return clean;
        }

        private static string GetFilePublisher(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var vi = FileVersionInfo.GetVersionInfo(path);
                    return !string.IsNullOrWhiteSpace(vi.CompanyName) ? vi.CompanyName : vi.ProductName ?? "Unknown Publisher";
                }
            }
            catch { }
            return "Unknown Publisher";
        }
    }
}
