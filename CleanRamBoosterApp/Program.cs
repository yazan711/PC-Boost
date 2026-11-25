using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using CleanRamBoosterApp.UI;

namespace CleanRamBoosterApp;

static class Program
{
    public const string MutexName = @"Global\CleanRamBooster_SingleInstance_Mutex_99F";
    public const string MessageName = "CleanRamBooster_RestoreWindow_Message_99F";

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ChangeWindowMessageFilter(uint message, uint dwFlag);
    private const uint MSGFLT_ADD = 1;

    private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);

    public static Mutex? AppMutex;
    public static uint RestoreWindowMsg = 0;

    [STAThread]
    static void Main(string[] args)
    {
        Services.AdminPrivilegeService.EnsureProcessPrivileges();

        if (args.Length >= 2 && args[0] == "--disable-startup")
        {
            try
            {
                var svc = new Services.StartupService();
                bool allOk = true;
                for (int i = 1; i < args.Length; i++)
                {
                    if (!svc.DisableItemByMonitorKey(args[i]))
                        allOk = false;
                }
                Environment.ExitCode = allOk ? 0 : 1;
            }
            catch
            {
                Environment.ExitCode = 1;
            }
            return;
        }

        if (args.Length >= 3 && args[0] == "--toggle-startup")
        {
            try
            {
                bool enable = string.Equals(args[1], "enable", StringComparison.OrdinalIgnoreCase);
                var svc = new Services.StartupService();
                bool allOk = true;
                for (int i = 2; i < args.Length; i++)
                {
                    if (!svc.ToggleItemByMonitorKey(args[i], enable))
                        allOk = false;
                }
                Environment.ExitCode = allOk ? 0 : 1;
            }
            catch
            {
                Environment.ExitCode = 1;
            }
            return;
        }

        if (args.Length >= 2 && args[0] == "--delete-startup")
        {
            try
            {
                var svc = new Services.StartupService();
                bool allOk = true;
                for (int i = 1; i < args.Length; i++)
                {
                    if (!svc.DeleteItemByMonitorKey(args[i]))
                        allOk = false;
                }
                Environment.ExitCode = allOk ? 0 : 1;
            }
            catch
            {
                Environment.ExitCode = 1;
            }
            return;
        }

        if (args.Length >= 4 && args[0] == "--set-admin-startup")
        {
            try
            {
                bool enable = string.Equals(args[1], "enable", StringComparison.OrdinalIgnoreCase);
                bool ok = Services.AutoStartService.ExecuteElevatedTaskAction(enable, args[2], args[3]);
                Environment.ExitCode = ok ? 0 : 1;
            }
            catch
            {
                Environment.ExitCode = 1;
            }
            return;
        }

        // When restarting/elevating, wait briefly for the previous process PID to exit completely
        // before attempting to claim the single-instance mutex.
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--replace-pid" && int.TryParse(args[i + 1], out int oldPid) && oldPid > 0 && oldPid != Environment.ProcessId)
            {
                try
                {
                    using var oldProc = Process.GetProcessById(oldPid);
                    if (!oldProc.HasExited)
                    {
                        oldProc.WaitForExit(6000);
                    }
                }
                catch { }
            }
        }

        bool startInTray = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--tray", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[i], "--minimized", StringComparison.OrdinalIgnoreCase))
            {
                startInTray = true;
            }
        }

        // Allow broadcast messages across integrity levels (standard <-> elevated)
        uint restoreMsg = RegisterWindowMessage(MessageName);
        RestoreWindowMsg = restoreMsg;
        try { ChangeWindowMessageFilter(restoreMsg, MSGFLT_ADD); } catch { }

        bool isFirstInstance = false;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                AppMutex = new Mutex(true, MutexName, out isFirstInstance);
                if (isFirstInstance) break;
            }
            catch (AbandonedMutexException)
            {
                isFirstInstance = true;
                break;
            }
            catch (UnauthorizedAccessException)
            {
                isFirstInstance = true;
                break;
            }
            catch
            {
                isFirstInstance = true;
                break;
            }

            if (attempt < 7)
            {
                Thread.Sleep(200);
            }
        }

        if (!isFirstInstance)
        {
            if (!startInTray)
            {
                PostMessage(HWND_BROADCAST, restoreMsg, IntPtr.Zero, IntPtr.Zero);
            }
            return;
        }

        try
        {
            ApplicationConfiguration.Initialize();
            InitializeAppIcon();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Services.DiagnosticLogger.LogInfo("Program", $"Starting PC Boost (Admin={Services.AdminPrivilegeService.IsRunningAsAdmin()}, StartInTray={startInTray})");

            Application.ThreadException += (s, e) =>
            {
                Services.DiagnosticLogger.LogCrash("WinForms.ThreadException", e.Exception);
                try
                {
                    CleanRamBoosterApp.WpfUI.AppDialog.Show($"PC Boost Error:\n{e.Exception.Message}", "Application Notice", CleanRamBoosterApp.WpfUI.AppDialogTone.Error);
                }
                catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Services.DiagnosticLogger.LogCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception, e.ExceptionObject?.ToString());
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Services.DiagnosticLogger.LogError("TaskScheduler.UnobservedTaskException", "Unobserved background task exception", e.Exception);
                e.SetObserved();
            };

            var wpfApp = new System.Windows.Application
            {
                ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
            };
            wpfApp.DispatcherUnhandledException += (s, e) =>
            {
                Services.DiagnosticLogger.LogCrash("WPF.DispatcherUnhandledException", e.Exception);
                e.Handled = true;
                try
                {
                    CleanRamBoosterApp.WpfUI.AppDialog.Show(
                        $"An unexpected error occurred and was logged to app.log:\n\n{e.Exception.Message}",
                        "Application Notice",
                        CleanRamBoosterApp.WpfUI.AppDialogTone.Error);
                }
                catch { }
            };
            var mainWindow = new CleanRamBoosterApp.WpfUI.WpfMainWindow(enableStartupMonitor: true, startInTray: startInTray);
            wpfApp.MainWindow = mainWindow;
            if (startInTray)
            {
                wpfApp.Run();
            }
            else
            {
                wpfApp.Run(mainWindow);
            }
        }
        finally
        {
            ReleaseAppMutex();
        }
    }

    public static void ReleaseAppMutex()
    {
        if (AppMutex != null)
        {
            try { AppMutex.ReleaseMutex(); } catch { }
            try { AppMutex.Dispose(); } catch { }
            AppMutex = null;
        }
    }

    public static void InitializeAppIcon()
    {
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] possibleIco = {
                Path.Combine(baseDir, "app.ico"),
                Path.Combine(baseDir, "Resources", "app.ico")
            };
            foreach (var p in possibleIco)
            {
                if (File.Exists(p))
                {
                    UITheme.AppIcon = new Icon(p);
                    break;
                }
            }
            if (UITheme.AppIcon == null)
            {
                UITheme.AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }

            string[] possiblePng = {
                Path.Combine(baseDir, "app_icon.png"),
                Path.Combine(baseDir, "Resources", "app_icon.png")
            };
            foreach (var p in possiblePng)
            {
                if (File.Exists(p))
                {
                    using var fs = new FileStream(p, FileMode.Open, FileAccess.Read);
                    UITheme.AppIconBitmap = Image.FromStream(fs);
                    break;
                }
            }
        }
        catch { }
    }
}
