using System;
using System.Runtime.InteropServices;

namespace CleanRamBoosterApp.Services
{
    public sealed class RestorePointResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public int ErrorCode { get; init; }
    }

    /// <summary>
    /// Creates Windows System Restore Points cleanly via native SrClient.dll (SRSetRestorePointW)
    /// without invoking PowerShell or external scripts.
    /// </summary>
    public static class SystemRestoreService
    {
        private const int BEGIN_SYSTEM_CHANGE = 100;
        private const int END_SYSTEM_CHANGE = 101;
        private const int MODIFY_SETTINGS = 12;
        private const int MAX_DESC_W = 256;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RESTOREPOINTINFOW
        {
            public int dwEventType;
            public int dwRestorePtType;
            public long llSequenceNumber;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_DESC_W)]
            public string szDescription;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STATEMGRSTATUS
        {
            public int nStatus;
            public long llSequenceNumber;
        }

        [DllImport("srclient.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SRSetRestorePointW(ref RESTOREPOINTINFOW pRestorePtSpec, out STATEMGRSTATUS pSMgrStatus);

        public static RestorePointResult CreateRestorePoint(string description)
        {
            bool isAr = LocalizationService.IsArabic;

            if (!AdminPrivilegeService.IsRunningAsAdmin())
            {
                return new RestorePointResult
                {
                    Success = false,
                    ErrorCode = 5,
                    Message = isAr
                        ? "يتطلب إنشاء نقطة استعادة للنظام صلاحيات المسؤول."
                        : "Creating a System Restore Point requires Administrator privileges."
                };
            }

            try
            {
                string safeDesc = string.IsNullOrWhiteSpace(description)
                    ? "PC Boost Checkpoint"
                    : description.Trim();
                if (safeDesc.Length >= MAX_DESC_W)
                    safeDesc = safeDesc.Substring(0, MAX_DESC_W - 1);

                var info = new RESTOREPOINTINFOW
                {
                    dwEventType = BEGIN_SYSTEM_CHANGE,
                    dwRestorePtType = MODIFY_SETTINGS,
                    llSequenceNumber = 0,
                    szDescription = safeDesc
                };

                bool ok = SRSetRestorePointW(ref info, out var status);
                if (ok && status.nStatus == 0)
                {
                    // Close out the restore point sequence cleanly
                    var endInfo = new RESTOREPOINTINFOW
                    {
                        dwEventType = END_SYSTEM_CHANGE,
                        dwRestorePtType = MODIFY_SETTINGS,
                        llSequenceNumber = status.llSequenceNumber,
                        szDescription = string.Empty
                    };
                    SRSetRestorePointW(ref endInfo, out _);

                    DiagnosticLogger.LogInfo(nameof(SystemRestoreService), $"Created System Restore Point '{safeDesc}' (Seq #{status.llSequenceNumber})");
                    return new RestorePointResult
                    {
                        Success = true,
                        ErrorCode = 0,
                        Message = isAr
                            ? $"تم إنشاء نقطة استعادة للنظام بنجاح: {safeDesc}"
                            : $"System Restore Point created: {safeDesc}"
                    };
                }

                int err = status.nStatus != 0 ? status.nStatus : Marshal.GetLastWin32Error();
                string detail = err switch
                {
                    1058 => isAr
                        ? "حماية النظام (System Protection) معطلة في ويندوز حالياً."
                        : "Windows System Protection is currently disabled on this PC.",
                    13 => isAr
                        ? "تم إنشاء نقطة استعادة مؤخراً (يسمح ويندوز بنقطة واحدة كل 24 ساعة افتراضياً)."
                        : "A restore point was recently created (Windows enforces a 24-hour cooldown by default).",
                    5 => isAr
                        ? "تم رفض الوصول عند إنشاء نقطة الاستعادة."
                        : "Access denied while creating a System Restore Point.",
                    _ => isAr
                        ? $"تعذر إنشاء نقطة استعادة للنظام (رمز الخطأ {err})."
                        : $"Could not create System Restore Point (Windows code {err})."
                };

                DiagnosticLogger.LogWarning(nameof(SystemRestoreService), $"SRSetRestorePointW failed (code={err}) for '{safeDesc}'");
                return new RestorePointResult
                {
                    Success = false,
                    ErrorCode = err,
                    Message = detail
                };
            }
            catch (DllNotFoundException ex)
            {
                DiagnosticLogger.LogWarning(nameof(SystemRestoreService), "srclient.dll unavailable", ex);
                return new RestorePointResult
                {
                    Success = false,
                    ErrorCode = -1,
                    Message = isAr
                        ? "ميزة استعادة النظام غير متوفرة على هذا الإصدار من ويندوز."
                        : "Windows System Restore is not available on this OS edition."
                };
            }
            catch (Exception ex)
            {
                DiagnosticLogger.LogError(nameof(SystemRestoreService), "Unexpected error creating restore point", ex);
                return new RestorePointResult
                {
                    Success = false,
                    ErrorCode = -1,
                    Message = ex.Message
                };
            }
        }
    }
}
