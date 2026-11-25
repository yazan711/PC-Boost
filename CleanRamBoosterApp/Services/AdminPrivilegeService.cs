using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using CleanRamBoosterApp.WpfUI;

namespace CleanRamBoosterApp.Services
{
    /// <summary>
    /// Centralized Administrator privilege, token-privilege enablement, and UAC elevation helper
    /// shared across Startup Manager, Turbo Boost, Care & Cleanup, Toolbox, and Settings.
    /// </summary>
    public static class AdminPrivilegeService
    {
        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out long lpLuid);

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct TOKEN_PRIVILEGES
        {
            public int PrivilegeCount;
            public long Luid;
            public int Attributes;
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AdjustTokenPrivileges(
            IntPtr TokenHandle,
            [MarshalAs(UnmanagedType.Bool)] bool DisableAllPrivileges,
            ref TOKEN_PRIVILEGES NewState,
            int BufferLength,
            IntPtr PreviousState,
            IntPtr ReturnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const int SE_PRIVILEGE_ENABLED = 0x00000002;
        private const int ERROR_CANCELLED = 1223;

        private static readonly string[] AdminTokenPrivileges =
        {
            "SeDebugPrivilege",
            "SeShutdownPrivilege",
            "SeBackupPrivilege",
            "SeRestorePrivilege",
            "SeTakeOwnershipPrivilege",
            "SeIncreaseQuotaPrivilege",
            "SeProfileSingleProcessPrivilege"
        };

        private static int _privilegesInitialized;

        public static bool IsRunningAsAdmin()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Activates standard administrative privileges already granted in the current process token
        /// so SCM, Registry, Power/Shutdown, File, and Memory APIs execute with full Administrator capability.
        /// </summary>
        public static void EnsureProcessPrivileges()
        {
            if (System.Threading.Interlocked.Exchange(ref _privilegesInitialized, 1) == 1)
                return;

            try
            {
                if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr hToken))
                    return;

                try
                {
                    foreach (string priv in AdminTokenPrivileges)
                    {
                        if (!LookupPrivilegeValue(null, priv, out long luid)) continue;
                        var tp = new TOKEN_PRIVILEGES
                        {
                            PrivilegeCount = 1,
                            Luid = luid,
                            Attributes = SE_PRIVILEGE_ENABLED
                        };
                        AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                    }
                }
                finally
                {
                    CloseHandle(hToken);
                }
            }
            catch { }
        }

        /// <summary>
        /// Runs a system tool directly under the current Administrator token when elevated,
        /// or elevates via Windows UAC (runas) if the host process is running non-elevated.
        /// </summary>
        public static bool RunProcessAsAdmin(
            string fileName,
            string arguments,
            int timeoutMs,
            out string stdOut,
            out string stdErr,
            out int exitCode,
            out bool cancelledByUser)
        {
            stdOut = string.Empty;
            stdErr = string.Empty;
            exitCode = -1;
            cancelledByUser = false;

            try
            {
                if (IsRunningAsAdmin())
                {
                    using var p = Process.Start(new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    });

                    if (p == null) return false;

                    var outTask = p.StandardOutput.ReadToEndAsync();
                    var errTask = p.StandardError.ReadToEndAsync();

                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(true); } catch { }
                        stdErr = $"Timed out after {timeoutMs / 1000.0:0.#}s.";
                        return false;
                    }

                    stdOut = outTask.GetAwaiter().GetResult().Trim();
                    stdErr = errTask.GetAwaiter().GetResult().Trim();
                    exitCode = p.ExitCode;
                    return exitCode == 0;
                }
                else
                {
                    using var p = Process.Start(new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = ProcessWindowStyle.Hidden
                    });

                    if (p == null) return false;

                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(true); } catch { }
                        stdErr = $"Timed out after {timeoutMs / 1000.0:0.#}s.";
                        return false;
                    }

                    exitCode = p.ExitCode;
                    return exitCode == 0;
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ERROR_CANCELLED)
            {
                cancelledByUser = true;
                stdErr = "Administrator prompt (UAC) was cancelled.";
                return false;
            }
            catch (Exception ex)
            {
                stdErr = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Runs a short-lived elevated instance of PC Boost itself for a specific CLI command.
        /// </summary>
        public static bool TryRunElevatedSelfCommand(string arguments, out bool cancelledByUser, out int exitCode, int timeoutMs = 25000)
        {
            cancelledByUser = false;
            exitCode = -1;

            try
            {
                string exe = Environment.ProcessPath ?? string.Empty;
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                    return false;

                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = arguments,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory,
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                if (p == null) return false;
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(true); } catch { }
                    return false;
                }

                exitCode = p.ExitCode;
                return exitCode == 0;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ERROR_CANCELLED)
            {
                cancelledByUser = true;
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Restarts PC Boost elevated as Administrator via Windows UAC and cleanly exits the current instance.
        /// </summary>
        public static bool TryRestartAsAdmin(out bool cancelledByUser, out string? errorMessage)
        {
            cancelledByUser = false;
            errorMessage = null;

            string exe = Environment.ProcessPath ?? string.Empty;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                errorMessage = "Could not locate PC Boost executable path.";
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo(exe)
                {
                    Arguments = $"--replace-pid {Environment.ProcessId}",
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                var proc = Process.Start(psi);
                if (proc == null) return false;

                if (System.Windows.Application.Current?.MainWindow is WpfMainWindow mainWin)
                {
                    mainWin.RequestExplicitExit();
                }
                else
                {
                    Program.ReleaseAppMutex();
                    System.Windows.Application.Current?.Shutdown();
                }
                return true;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ERROR_CANCELLED)
            {
                cancelledByUser = true;
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Ensures PC Boost is running as Administrator before executing privileged features.
        /// If running non-elevated, prompts the user to restart PC Boost as Administrator via UAC.
        /// Returns true if already running as Administrator; false otherwise.
        /// </summary>
        public static bool EnsureAdminOrPrompt(object? owner, string featureNameEn, string featureNameAr)
        {
            if (IsRunningAsAdmin())
            {
                EnsureProcessPrivileges();
                return true;
            }

            bool isAr = LocalizationService.IsArabic;
            string prompt = isAr
                ? $"تتطلب ميزة «{featureNameAr}» صلاحيات المسؤول للعمل بالكامل دون قيود.\n\nهل تريد إعادة تشغيل PC Boost الآن بصلاحيات المسؤول؟"
                : $"\"{featureNameEn}\" requires Administrator privileges to operate with full system access.\n\nRestart PC Boost as Administrator now?";

            string title = LocalizationService.T("AppTitle", "PC Boost");
            bool confirmed = owner switch
            {
                System.Windows.DependencyObject dep => AppDialog.Confirm(dep, prompt, title, AppDialogTone.Question),
                System.Windows.Forms.IWin32Window win32 => AppDialog.Confirm(win32, prompt, title, AppDialogTone.Question),
                _ => AppDialog.Confirm(prompt, title, AppDialogTone.Question)
            };

            if (confirmed)
            {
                if (!TryRestartAsAdmin(out bool cancelled, out string? err) && !cancelled && !string.IsNullOrEmpty(err))
                {
                    switch (owner)
                    {
                        case System.Windows.DependencyObject dep:
                            AppDialog.Show(dep, err, title, AppDialogTone.Warning);
                            break;
                        case System.Windows.Forms.IWin32Window win32:
                            AppDialog.Show(win32, err, title, AppDialogTone.Warning);
                            break;
                        default:
                            AppDialog.Show(err, title, AppDialogTone.Warning);
                            break;
                    }
                }
            }

            return false;
        }
    }
}
