using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Xml;
using Microsoft.Win32;

namespace CleanRamBoosterApp.Services
{
    /// <summary>
    /// Manages PC Boost auto-start on boot, including elevated Administrator startup via
    /// Task Scheduler (HighestAvailable / ONLOGON) so next reboot skips UAC prompts.
    /// </summary>
    public static class AutoStartService
    {
        public const string AdminTaskName = "PCBoost_AutoStart_Admin";
        public const string BootTrayArg = "--tray";
        private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValuePrimary = "CleanRamBoosterApp";
        private const string RunValueLegacy = "PCBoost";

        public static bool IsCurrentProcessAdmin()
        {
            try
            {
                using var id = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(id);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public static string GetCurrentUserAccount()
        {
            try
            {
                using var id = WindowsIdentity.GetCurrent();
                if (!string.IsNullOrEmpty(id.Name)) return id.Name;
            }
            catch { }
            return $"{Environment.UserDomainName}\\{Environment.UserName}";
        }

        public static bool IsAdminTaskRegistered()
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/query /tn \"{AdminTaskName}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                p?.WaitForExit(3000);
                return p != null && p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsRegistryRunPresent()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunSubKey, false);
                if (key == null) return false;
                return key.GetValue(RunValuePrimary) != null || key.GetValue(RunValueLegacy) != null;
            }
            catch
            {
                return false;
            }
        }

        public static bool ConfigureBootStartup(bool startWithWindows, bool runAsAdminOnBoot, out bool cancelledByUser)
        {
            cancelledByUser = false;
            string exePath = Environment.ProcessPath ?? string.Empty;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                return false;

            string userAccount = GetCurrentUserAccount();

            if (!startWithWindows)
            {
                RemoveRegistryRunEntries();
                if (IsAdminTaskRegistered())
                {
                    if (!RunAdminTaskCommand(enable: false, exePath, userAccount, out cancelledByUser))
                        return false;
                }
                return true;
            }

            // Since the app manifest specifies requireAdministrator, Windows blocks HKCU\Run entries
            // at logon. Always register the HighestAvailable scheduled task when Start with Windows is on.
            if (!RunAdminTaskCommand(enable: true, exePath, userAccount, out cancelledByUser))
                return false;

            RemoveRegistryRunEntries();
            return true;
        }

        public static bool ExecuteElevatedTaskAction(bool enable, string exePath, string userAccount)
        {
            if (enable)
                return CreateOrUpdateAdminTaskDirect(exePath, userAccount);
            return DeleteAdminTaskDirect();
        }

        private static bool RunAdminTaskCommand(bool enable, string exePath, string userAccount, out bool cancelledByUser)
        {
            cancelledByUser = false;

            if (IsCurrentProcessAdmin())
            {
                return enable
                    ? CreateOrUpdateAdminTaskDirect(exePath, userAccount)
                    : DeleteAdminTaskDirect();
            }

            try
            {
                string mode = enable ? "enable" : "disable";
                string args = $"--set-admin-startup {mode} \"{exePath}\" \"{userAccount}\"";
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = args,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
                    UseShellExecute = true,
                    Verb = "runas"
                });
                p?.WaitForExit(15000);
                return p != null && p.ExitCode == 0;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                cancelledByUser = true;
                return false;
            }
            catch
            {
                return false;
            }
        }

        public static bool EnsureBootTaskHasTrayIfRegistered()
        {
            if (!IsCurrentProcessAdmin()) return false;
            try
            {
                string? xmlText = QueryAdminTaskXml();
                if (!TryReadTaskExec(xmlText, out string existingCmd, out string existingArgs))
                    return false;

                if (existingArgs.Contains(BootTrayArg, StringComparison.OrdinalIgnoreCase))
                    return true;

                string targetExe = (!string.IsNullOrEmpty(existingCmd) && File.Exists(existingCmd))
                    ? existingCmd
                    : (Environment.ProcessPath ?? string.Empty);
                if (string.IsNullOrEmpty(targetExe) || !File.Exists(targetExe))
                    return false;

                return CreateOrUpdateAdminTaskDirect(targetExe, GetCurrentUserAccount());
            }
            catch
            {
                return false;
            }
        }

        public static bool HasAdminTaskTrayArgument()
        {
            string? xmlText = QueryAdminTaskXml();
            return TryReadTaskExec(xmlText, out _, out string args)
                && args.Contains(BootTrayArg, StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryReadTaskExec(string? xmlText, out string command, out string arguments)
        {
            command = "";
            arguments = "";
            if (string.IsNullOrWhiteSpace(xmlText)) return false;

            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(xmlText);
                var execNodes = doc.GetElementsByTagName("Exec");
                if (execNodes.Count == 0) return false;

                foreach (XmlNode child in execNodes[0]!.ChildNodes)
                {
                    if (string.Equals(child.LocalName, "Command", StringComparison.OrdinalIgnoreCase))
                        command = child.InnerText?.Trim().Trim('"') ?? "";
                    else if (string.Equals(child.LocalName, "Arguments", StringComparison.OrdinalIgnoreCase))
                        arguments = child.InnerText?.Trim() ?? "";
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string? QueryAdminTaskXml()
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/query /tn \"{AdminTaskName}\" /xml",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (p == null) return null;
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                return p.ExitCode == 0 ? output : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool CreateOrUpdateAdminTaskDirect(string exePath, string userAccount)
        {
            string tempXml = Path.Combine(Path.GetTempPath(), $"pcboost_task_{Guid.NewGuid():N}.xml");
            try
            {
                string workDir = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory;
                string escapedUser = SecurityElement.Escape(userAccount) ?? userAccount;
                string escapedExe = SecurityElement.Escape(exePath) ?? exePath;
                string escapedDir = SecurityElement.Escape(workDir) ?? workDir;

                string xml =
$@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.3"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Auto-start PC Boost with Administrator privileges on Windows logon.</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{escapedUser}</UserId>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{escapedUser}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>false</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>5</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{escapedExe}</Command>
      <Arguments>{BootTrayArg}</Arguments>
      <WorkingDirectory>{escapedDir}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";

                File.WriteAllText(tempXml, xml, Encoding.Unicode);

                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/create /tn \"{AdminTaskName}\" /xml \"{tempXml}\" /f",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                p?.WaitForExit(5000);
                return p != null && p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                try { if (File.Exists(tempXml)) File.Delete(tempXml); } catch { }
            }
        }

        private static bool DeleteAdminTaskDirect()
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/delete /tn \"{AdminTaskName}\" /f",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                p?.WaitForExit(5000);
                return p != null && p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private static void RemoveRegistryRunEntries()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunSubKey, true);
                if (key == null) return;
                key.DeleteValue(RunValuePrimary, false);
                key.DeleteValue(RunValueLegacy, false);
            }
            catch { }
        }
    }
}
