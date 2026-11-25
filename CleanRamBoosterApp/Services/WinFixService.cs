using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CleanRamBoosterApp.Services
{
    public class WinFixItem
    {
        public string Category { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string TitleAr { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string DescriptionAr { get; set; } = string.Empty;
        public Action FixAction { get; set; } = null!;
        public Func<(bool Success, string? Error)>? DetailedFixAction { get; set; }
        public bool RequiresAdmin { get; set; } = true;
        public bool IsSelected { get; set; } = true;
    }

    public class WinFixService
    {
        // -------------------------------------------------------------
        // SYSTEM RESTORE & REGISTRY SNAPSHOT ENGINE (Safety 10/10)
        // -------------------------------------------------------------
        public static string BackupDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CleanRamBooster", "Backups");

        public bool CreateSystemRestorePoint(string description = "PC Boost WinFix Pre-Repair Restore Point")
        {
            var res = SystemRestoreService.CreateRestorePoint(description);
            return res.Success;
        }

        public string CreateRegistryBackup()
        {
            try
            {
                Directory.CreateDirectory(BackupDirectory);
                string filename = $"WinFix_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.reg";
                string filePath = Path.Combine(BackupDirectory, filename);

                string[] keys = new[]
                {
                    @"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies",
                    @"HKLM\Software\Microsoft\Windows\CurrentVersion\Policies",
                    @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"
                };

                using (var writer = new StreamWriter(filePath, false, System.Text.Encoding.Unicode))
                {
                    writer.WriteLine("Windows Registry Editor Version 5.00");
                    writer.WriteLine($"; CleanRam Suite Pre-Repair Auto-Snapshot ({DateTime.Now})");
                    writer.WriteLine();
                }

                foreach (var k in keys)
                {
                    string tmpFile = filePath + ".tmp";
                    RunCmd($"reg export \"{k}\" \"{tmpFile}\" /y >nul 2>&1");
                    if (File.Exists(tmpFile))
                    {
                        string content = File.ReadAllText(tmpFile);
                        File.AppendAllText(filePath, content + Environment.NewLine);
                        File.Delete(tmpFile);
                    }
                }

                return filePath;
            }
            catch
            {
                return string.Empty;
            }
        }

        public bool RestoreRegistryBackup(string backupFilePath)
        {
            if (!File.Exists(backupFilePath)) return false;
            return RunCmdWithResult($"reg import \"{backupFilePath}\"", out _, 15000);
        }

        public List<string> GetAvailableBackups()
        {
            var list = new List<string>();
            try
            {
                if (Directory.Exists(BackupDirectory))
                {
                    list.AddRange(Directory.GetFiles(BackupDirectory, "*.reg"));
                    list.Sort((a, b) => File.GetCreationTime(b).CompareTo(File.GetCreationTime(a)));
                }
            }
            catch { }
            return list;
        }
        // -------------------------------------------------------------
        // 1. DESKTOP & SHELL REPAIRS
        // -------------------------------------------------------------
        public void RebuildIconCache()
        {
            try
            {
                // Terminate explorer
                foreach (var p in Process.GetProcessesByName("explorer"))
                {
                    try { p.Kill(); p.WaitForExit(1500); } catch { }
                }

                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string iconCache = Path.Combine(localApp, "IconCache.db");
                if (File.Exists(iconCache))
                {
                    try { File.Delete(iconCache); } catch { }
                }

                // Delete Explorer thumbcache and iconcache files
                string expCacheDir = Path.Combine(localApp, @"Microsoft\Windows\Explorer");
                if (Directory.Exists(expCacheDir))
                {
                    foreach (var f in Directory.GetFiles(expCacheDir, "*cache*.db"))
                    {
                        try { File.Delete(f); } catch { }
                    }
                }
            }
            catch { }
            finally
            {
                RestartExplorer();
            }
        }

        public void RestartExplorer()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("explorer"))
                {
                    try { p.Kill(); p.WaitForExit(1000); } catch { }
                }
            }
            catch { }

            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            }
            catch { }
        }

        public void RestoreDesktopIcons()
        {
            try
            {
                // CLSIDs: This PC, Recycle Bin, User Files, Control Panel, Network
                string[] clsids = {
                    "{20D04FE0-3AEA-1069-A2D8-08002B30309D}", // This PC
                    "{645FF040-5081-101B-9F08-00AA002F954E}", // Recycle Bin
                    "{59031a47-3f72-44a7-89c5-5595fe6b30ee}", // User Files
                    "{5399E693-AE2C-4cd0-9377-B5BFB207D4DE}", // Control Panel
                    "{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}"  // Network
                };

                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel", true);
                if (key != null)
                {
                    foreach (var id in clsids)
                    {
                        key.SetValue(id, 0, RegistryValueKind.DWord); // 0 = Show icon
                    }
                }
            }
            catch { }
        }

        public void FixTaskbarAndStartMenu()
        {
            try
            {
                // Clean ShellExperienceHost & StartMenuExperienceHost
                foreach (var p in Process.GetProcessesByName("StartMenuExperienceHost"))
                {
                    try { p.Kill(); } catch { }
                }
                foreach (var p in Process.GetProcessesByName("ShellExperienceHost"))
                {
                    try { p.Kill(); } catch { }
                }
                RestartExplorer();
            }
            catch { }
        }

        public void ResetWindowsSearchIndex()
        {
            try
            {
                RunCmd("net stop WSearch && net start WSearch");
            }
            catch { }
        }

        // -------------------------------------------------------------
        // 2. NETWORK & CONNECTIVITY REPAIRS
        // -------------------------------------------------------------
        public void FlushDnsAndWinsock()
        {
            try
            {
                RunCmd("ipconfig /flushdns");
                RunCmd("netsh winsock reset");
                RunCmd("netsh int ip reset");
            }
            catch { }
        }

        public void ResetProxySettings()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true);
                if (key != null)
                {
                    key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                    key.DeleteValue("ProxyServer", false);
                    key.DeleteValue("ProxyOverride", false);
                }
            }
            catch { }
        }

        public void RenewIpConfiguration()
        {
            try
            {
                RunCmd("ipconfig /release && ipconfig /renew");
            }
            catch { }
        }

        public void ResetWindowsFirewall()
        {
            try
            {
                RunCmd("netsh advfirewall reset");
            }
            catch { }
        }

        // -------------------------------------------------------------
        // 3. SYSTEM SECURITY POLICIES & SERVICES (Malware Damage Fixes)
        // -------------------------------------------------------------
        public void FixTaskManagerDisabled()
        {
            try
            {
                using var keyCU = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\System", true);
                keyCU?.DeleteValue("DisableTaskMgr", false);

                using var keyLM = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\System", true);
                keyLM?.DeleteValue("DisableTaskMgr", false);
            }
            catch { }
        }

        public void FixRegistryEditorDisabled()
        {
            try
            {
                using var keyCU = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\System", true);
                keyCU?.DeleteValue("DisableRegistryTools", false);

                using var keyLM = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\System", true);
                keyLM?.DeleteValue("DisableRegistryTools", false);
            }
            catch { }
        }

        public void FixCommandPromptDisabled()
        {
            try
            {
                using var keyCU = Registry.CurrentUser.OpenSubKey(@"Software\Policies\Microsoft\Windows\System", true);
                keyCU?.DeleteValue("DisableCMD", false);

                using var keyLM = Registry.LocalMachine.OpenSubKey(@"Software\Policies\Microsoft\Windows\System", true);
                keyLM?.DeleteValue("DisableCMD", false);
            }
            catch { }
        }

        public void ResetPrintSpooler()
        {
            try
            {
                RunCmd("net stop Spooler");
                string spoolPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"spool\PRINTERS");
                if (Directory.Exists(spoolPath))
                {
                    foreach (var file in Directory.GetFiles(spoolPath))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
                RunCmd("net start Spooler");
            }
            catch { }
        }

        public void ResetWindowsUpdate()
        {
            try
            {
                RunCmd("net stop wuauserv");
                RunCmd("net stop bits");

                string downloadDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"SoftwareDistribution\Download");
                if (Directory.Exists(downloadDir))
                {
                    foreach (var f in Directory.GetFiles(downloadDir))
                    {
                        try { File.Delete(f); } catch { }
                    }
                }

                RunCmd("net start bits");
                RunCmd("net start wuauserv");
            }
            catch { }
        }

        public void FixWindowsInstaller()
        {
            try
            {
                RunCmd("msiexec /unregister && msiexec /regserver");
                RunCmd("net start msiserver");
            }
            catch { }
        }

        // -------------------------------------------------------------
        // 4. FILE ASSOCIATIONS REPAIRS
        // -------------------------------------------------------------
        public void FixExeAssociation()
        {
            try
            {
                using var key = Registry.ClassesRoot.CreateSubKey(@"exefile\shell\open\command", true);
                key?.SetValue("", "\"%1\" %*");
            }
            catch { }
        }

        public void FixLnkAssociation()
        {
            try
            {
                using var key = Registry.ClassesRoot.CreateSubKey(@"lnkfile\shell\open\command", true);
                key?.SetValue("", "\"%1\" %*");
            }
            catch { }
        }

        public void FixBatAssociation()
        {
            try
            {
                using var key = Registry.ClassesRoot.CreateSubKey(@"batfile\shell\open\command", true);
                key?.SetValue("", "\"%1\" %*");
            }
            catch { }
        }

        public void FixRegAssociation()
        {
            try
            {
                using var key = Registry.ClassesRoot.CreateSubKey(@"regfile\shell\open\command", true);
                key?.SetValue("", "regedit.exe \"%1\"");
            }
            catch { }
        }

        public void FixTxtAssociation()
        {
            try
            {
                using var key = Registry.ClassesRoot.CreateSubKey(@"txtfile\shell\open\command", true);
                key?.SetValue("", "notepad.exe \"%1\"");
            }
            catch { }
        }

        // -------------------------------------------------------------
        // ALL ITEMS LIST PROVIDER
        // -------------------------------------------------------------
        public List<WinFixItem> GetAllFixes()
        {
            return new List<WinFixItem>
            {
                // Desktop & Explorer
                new WinFixItem
                {
                    Category = "Desktop & Explorer",
                    Title = "Rebuild Icon & Thumbnail Cache",
                    TitleAr = "إعادة بناء كاش الأيقونات والمصغرات",
                    Description = "Clears corrupted icon cache and restarts Windows Explorer.",
                    DescriptionAr = "مسح ذاكرة كاش الأيقونات التالفة وإعادة تشغيل مستكشف ويندوز.",
                    FixAction = RebuildIconCache
                },
                new WinFixItem
                {
                    Category = "Desktop & Explorer",
                    Title = "Restore Missing Desktop Icons",
                    TitleAr = "استعادة أيقونات سطح المكتب المفقودة",
                    Description = "Restores Computer (This PC), Recycle Bin, Control Panel, Network desktop icons.",
                    DescriptionAr = "استعادة أيقونات هذا الكمبيوتر، سلة المحذوفات، لوحة التحكم، والشبكة.",
                    FixAction = RestoreDesktopIcons
                },
                new WinFixItem
                {
                    Category = "Desktop & Explorer",
                    Title = "Restart Windows Explorer Process",
                    TitleAr = "إعادة تشغيل مستكشف ويندوز (Explorer)",
                    Description = "Cleanly restarts the Windows desktop shell to resolve frozen taskbar elements.",
                    DescriptionAr = "إعادة تشغيل واجهة سطح المكتب لحل تجميد وتعليق عناصر شريط المهام.",
                    FixAction = RestartExplorer
                },
                new WinFixItem
                {
                    Category = "Desktop & Explorer",
                    Title = "Fix Frozen Taskbar & Start Menu",
                    TitleAr = "إصلاح تجميد شريط المهام وقائمة ابدأ",
                    Description = "Restarts StartMenuExperienceHost and re-registers the taskbar shell.",
                    DescriptionAr = "إعادة تشغيل خدمة واجهة قائمة ابدأ وإعادة تسجيل شريط المهام تلقائياً.",
                    FixAction = FixTaskbarAndStartMenu
                },
                new WinFixItem
                {
                    Category = "Desktop & Explorer",
                    Title = "Reset Windows Search Indexer",
                    TitleAr = "إعادة تعيين مفهرس بحث ويندوز",
                    Description = "Restarts the WSearch indexing service and resolves stuck search queries.",
                    DescriptionAr = "إعادة تشغيل خدمة الفهرسة WSearch وحل مشكلة تعليق وتوقف البحث.",
                    FixAction = ResetWindowsSearchIndex,
                    DetailedFixAction = () => RunCmdStep("net stop WSearch && net start WSearch", 20000)
                },

                // Network & Internet
                new WinFixItem
                {
                    Category = "Network & Internet",
                    Title = "Flush DNS & Reset Winsock Catalog",
                    TitleAr = "تفريغ DNS وإعادة ضبط مكدس Winsock",
                    Description = "Resolves DNS resolution drops, browser socket stalls, and TCP/IP stack errors.",
                    DescriptionAr = "حل انقطاع الاتصال وتوقف مقابس المتصفح وأخطاء بروتوكول TCP/IP.",
                    FixAction = FlushDnsAndWinsock,
                    DetailedFixAction = () => RunCmdStep("ipconfig /flushdns && netsh winsock reset && netsh int ip reset", 20000)
                },
                new WinFixItem
                {
                    Category = "Network & Internet",
                    Title = "Reset Windows Proxy Hijacks",
                    TitleAr = "إزالة إعدادات البروكسي الإجباري الضارة",
                    Description = "Disables rogue proxy server redirects in internet settings.",
                    DescriptionAr = "تعطيل خوادم البروكسي الضارة في إعدادات الاتصال تلقائياً.",
                    RequiresAdmin = false,
                    FixAction = ResetProxySettings
                },
                new WinFixItem
                {
                    Category = "Network & Internet",
                    Title = "Release & Renew DHCP IP Lease",
                    TitleAr = "تحرير وتجديد عنوان IP المحلي (DHCP)",
                    Description = "Releases local network IP allocation and queries router for a clean lease.",
                    DescriptionAr = "إلغاء عنوان IP القديم وطلب عنوان شبكة نظيف وجديد من الراوتر.",
                    FixAction = RenewIpConfiguration,
                    DetailedFixAction = () => RunCmdStep("ipconfig /release && ipconfig /renew", 25000)
                },
                new WinFixItem
                {
                    Category = "Network & Internet",
                    Title = "Restore Default Windows Firewall",
                    TitleAr = "استعادة الإعدادات الافتراضية لجدار الحماية",
                    Description = "Resets Windows Defender Firewall rules back to factory safe defaults.",
                    DescriptionAr = "إعادة ضبط قواعد جدار حماية Windows Defender إلى الوضع الافتراضي الآمن.",
                    FixAction = ResetWindowsFirewall,
                    DetailedFixAction = () => RunCmdStep("netsh advfirewall reset", 15000)
                },

                // System & Security Policies
                new WinFixItem
                {
                    Category = "System & Security",
                    Title = "Enable Task Manager (Policy Unlock)",
                    TitleAr = "تمكين مدير المهام (Task Manager)",
                    Description = "Removes malware policy restrictions that disable Task Manager.",
                    DescriptionAr = "إزالة قيود وسياسات البرمجيات الضارة التي تعطل فتح مدير المهام.",
                    FixAction = FixTaskManagerDisabled,
                    DetailedFixAction = () => DeletePolicyValuePair(@"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableTaskMgr")
                },
                new WinFixItem
                {
                    Category = "System & Security",
                    Title = "Enable Registry Editor (Regedit Unlock)",
                    TitleAr = "تمكين محرر السجل (Regedit)",
                    Description = "Removes administrator/malware policies blocking Registry Editor.",
                    DescriptionAr = "إزالة القيود والسياسات التي تمنع فتح محرر السجل في النظام.",
                    FixAction = FixRegistryEditorDisabled,
                    DetailedFixAction = () => DeletePolicyValuePair(@"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableRegistryTools")
                },
                new WinFixItem
                {
                    Category = "System & Security",
                    Title = "Enable Command Prompt (CMD Unlock)",
                    TitleAr = "تمكين موجه الأوامر (CMD)",
                    Description = "Removes administrator/malware restrictions disabling cmd.exe.",
                    DescriptionAr = "إزالة القيود التي تعطل تشغيل cmd.exe في ويندوز.",
                    FixAction = FixCommandPromptDisabled,
                    DetailedFixAction = () => DeletePolicyValuePair(@"Software\Policies\Microsoft\Windows\System", "DisableCMD")
                },
                new WinFixItem
                {
                    Category = "System & Security",
                    Title = "Reset Print Spooler Queue",
                    TitleAr = "إعادة ضبط طابور الطباعة (Print Spooler)",
                    Description = "Stops spooler service, purges stuck print jobs, and restarts print engine.",
                    DescriptionAr = "إيقاف خدمة الطباعة، مسح مهام الطباعة العالقة، وإعادة تشغيل المحرك.",
                    FixAction = ResetPrintSpooler,
                    DetailedFixAction = () =>
                    {
                        ResetPrintSpooler();
                        return RunCmdStep("sc query Spooler", 8000);
                    }
                },
                new WinFixItem
                {
                    Category = "System & Security",
                    Title = "Reset Windows Update Components",
                    TitleAr = "إعادة ضبط مكونات تحديثات ويندوز",
                    Description = "Clears corrupted SoftwareDistribution download caches and restarts BITS.",
                    DescriptionAr = "مسح كاش التنزيل التالف في SoftwareDistribution وإعادة تشغيل BITS.",
                    FixAction = ResetWindowsUpdate,
                    DetailedFixAction = () =>
                    {
                        ResetWindowsUpdate();
                        return RunCmdStep("sc query wuauserv", 8000);
                    }
                },
                new WinFixItem
                {
                    Category = "System & Security",
                    Title = "Repair Windows Installer Service (MSI)",
                    TitleAr = "إصلاح خدمة مثبت ويندوز (MSI)",
                    Description = "Re-registers msiexec.exe and starts the Windows Installer service.",
                    DescriptionAr = "إعادة تسجيل msiexec.exe وبدء خدمة تثبيت البرامج المتوقفة.",
                    FixAction = FixWindowsInstaller,
                    DetailedFixAction = () => RunCmdStep("msiexec /unregister && msiexec /regserver && net start msiserver", 20000)
                },

                // File Associations
                new WinFixItem
                {
                    Category = "File Associations",
                    Title = "Fix Executable (.EXE) Association",
                    TitleAr = "إصلاح اقتران الملفات التنفيذية (.EXE)",
                    Description = "Restores .exe execution handler to fix 'Cannot open file' errors.",
                    DescriptionAr = "استعادة معالج تشغيل ملفات .exe وحل خطأ تعذر فتح الملف.",
                    FixAction = FixExeAssociation,
                    DetailedFixAction = () => SetClassesRootCommand(@"exefile\shell\open\command", "\"%1\" %*")
                },
                new WinFixItem
                {
                    Category = "File Associations",
                    Title = "Fix Desktop Shortcut (.LNK) Association",
                    TitleAr = "إصلاح اقتران اختصارات سطح المكتب (.LNK)",
                    Description = "Restores shortcut execution handler so desktop icons launch apps.",
                    DescriptionAr = "استعادة معالج تشغيل الاختصارات لتتمكن الأيقونات من فتح البرامج.",
                    FixAction = FixLnkAssociation,
                    DetailedFixAction = () => SetClassesRootCommand(@"lnkfile\shell\open\command", "\"%1\" %*")
                },
                new WinFixItem
                {
                    Category = "File Associations",
                    Title = "Fix Script (.BAT / .CMD) Association",
                    TitleAr = "إصلاح اقتران الملفات الدفعية (.BAT / .CMD)",
                    Description = "Restores batch script execution handler.",
                    DescriptionAr = "استعادة معالج تشغيل وتنفيذ السكربتات والأوامر الدفعية.",
                    FixAction = FixBatAssociation,
                    DetailedFixAction = () => SetClassesRootCommand(@"batfile\shell\open\command", "\"%1\" %*")
                },
                new WinFixItem
                {
                    Category = "File Associations",
                    Title = "Fix Registry Script (.REG) Association",
                    TitleAr = "إصلاح اقتران ملفات التسجيل (.REG)",
                    Description = "Restores .reg file import handler.",
                    DescriptionAr = "استعادة معالج استيراد ملفات التسجيل في السجل تلقائياً.",
                    FixAction = FixRegAssociation,
                    DetailedFixAction = () => SetClassesRootCommand(@"regfile\shell\open\command", "regedit.exe \"%1\"")
                },
                new WinFixItem
                {
                    Category = "File Associations",
                    Title = "Fix Text Document (.TXT) Association",
                    TitleAr = "إصلاح اقتران المستندات النصية (.TXT)",
                    Description = "Restores Notepad association for standard text files.",
                    DescriptionAr = "استعادة اقتران برنامج المفكرة القياسي مع الملفات النصية.",
                    FixAction = FixTxtAssociation,
                    DetailedFixAction = () => SetClassesRootCommand(@"txtfile\shell\open\command", "notepad.exe \"%1\"")
                },
            };
        }

        public (bool Success, string? Error) ExecuteFix(WinFixItem fix)
        {
            if (fix == null) return (false, "Invalid repair item.");
            if (fix.RequiresAdmin && !AdminPrivilegeService.IsRunningAsAdmin())
            {
                return (false, LocalizationService.IsArabic
                    ? "يتطلب صلاحيات المسؤول."
                    : "Requires Administrator privileges.");
            }

            try
            {
                if (fix.DetailedFixAction != null)
                {
                    return fix.DetailedFixAction();
                }

                fix.FixAction?.Invoke();
                return (true, null);
            }
            catch (UnauthorizedAccessException)
            {
                return (false, LocalizationService.IsArabic
                    ? "تم رفض الوصول (صلاحيات المسؤول أو حماية النظام)."
                    : "Access denied (Administrator privileges or system protection).");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private static (bool Success, string? Error) DeletePolicyValuePair(string subKey, string valueName)
        {
            try
            {
                using var keyCU = Registry.CurrentUser.OpenSubKey(subKey, true);
                keyCU?.DeleteValue(valueName, false);
                using var keyLM = Registry.LocalMachine.OpenSubKey(subKey, true);
                keyLM?.DeleteValue(valueName, false);
                return (true, null);
            }
            catch (UnauthorizedAccessException)
            {
                return (false, LocalizationService.IsArabic ? "يتطلب صلاحيات المسؤول لتعديل سياسات النظام." : "Administrator privileges required to update system policies.");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private static (bool Success, string? Error) SetClassesRootCommand(string subKey, string command)
        {
            try
            {
                using var key = Registry.ClassesRoot.CreateSubKey(subKey, true);
                if (key == null)
                    return (false, LocalizationService.IsArabic ? "تعذر فتح مفتاح اقتران الملفات في السجل." : "Could not open file association registry key.");
                key.SetValue("", command);
                return (true, null);
            }
            catch (UnauthorizedAccessException)
            {
                return (false, LocalizationService.IsArabic ? "يتطلب صلاحيات المسؤول لتعديل اقتران الملفات." : "Administrator privileges required to repair file associations.");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private static (bool Success, string? Error) RunCmdStep(string cmd, int timeoutMs = 15000)
        {
            bool ok = RunCmdWithResult(cmd, out string err, timeoutMs);
            return (ok, ok ? null : err);
        }

        private static bool RunCmd(string cmd, int timeoutMs = 15000)
            => RunCmdWithResult(cmd, out _, timeoutMs);

        private static bool RunCmdWithResult(string cmd, out string errorMessage, int timeoutMs = 15000)
        {
            errorMessage = string.Empty;
            bool ok = AdminPrivilegeService.RunProcessAsAdmin(
                "cmd.exe",
                $"/c {cmd}",
                timeoutMs,
                out string stdOut,
                out string stdErr,
                out int exitCode,
                out bool cancelled);

            if (!ok)
            {
                if (cancelled)
                {
                    errorMessage = LocalizationService.IsArabic
                        ? "تم إلغاء طلب صلاحيات المسؤول (UAC)."
                        : "Administrator prompt (UAC) was cancelled.";
                }
                else
                {
                    errorMessage = !string.IsNullOrWhiteSpace(stdErr)
                        ? stdErr
                        : (!string.IsNullOrWhiteSpace(stdOut) ? stdOut : $"Exit code {exitCode}");
                }
            }
            return ok;
        }
    }
}
