using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace CleanRamBoosterApp.Services
{
    public static class DiagnosticLogger
    {
        private static readonly object _sync = new object();
        private const long MaxLogBytes = 2L * 1024 * 1024; // 2 MB rotating cap

        public static string LogDirectory
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "CleanRamBooster");
                try { Directory.CreateDirectory(dir); } catch { }
                return dir;
            }
        }

        public static string LogFilePath => Path.Combine(LogDirectory, "app.log");
        public static string CrashLogPath => Path.Combine(LogDirectory, "crash.log");

        public static void LogInfo(string source, string message) => WriteEntry("INFO", source, message, null);
        public static void LogWarning(string source, string message, Exception? ex = null) => WriteEntry("WARN", source, message, ex);
        public static void LogError(string source, string message, Exception? ex = null) => WriteEntry("ERROR", source, message, ex);

        public static void LogCrash(string source, Exception? ex, string? fallbackDetails = null)
        {
            string details = ex != null ? ex.ToString() : (fallbackDetails ?? "Unknown fatal error");
            WriteEntry("CRASH", source, details, null);
            lock (_sync)
            {
                try
                {
                    File.AppendAllText(CrashLogPath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{source}] {details}{Environment.NewLine}{new string('-', 60)}{Environment.NewLine}",
                        Encoding.UTF8);
                }
                catch { }
            }
        }

        private static void WriteEntry(string level, string source, string message, Exception? ex)
        {
            lock (_sync)
            {
                try
                {
                    string path = LogFilePath;
                    RotateIfNeeded(path);

                    var sb = new StringBuilder();
                    sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("] ");
                    sb.Append('[').Append(level).Append("] ");
                    sb.Append('[').Append(source).Append("] ");
                    sb.Append(message);
                    if (ex != null)
                    {
                        sb.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
                    }
                    sb.AppendLine();

                    File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
                }
                catch
                {
                    // Never allow logging failure to break app execution.
                }
            }
        }

        private static void RotateIfNeeded(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                var fi = new FileInfo(path);
                if (fi.Length < MaxLogBytes) return;

                string backup = path + ".bak";
                if (File.Exists(backup))
                    File.Delete(backup);
                File.Move(path, backup);
            }
            catch { }
        }

        public static string ReadRecentLogTail(int maxLines = 150)
        {
            lock (_sync)
            {
                try
                {
                    if (!File.Exists(LogFilePath))
                        return "(No log entries recorded yet.)";
                    string[] lines = File.ReadAllLines(LogFilePath, Encoding.UTF8);
                    int start = Math.Max(0, lines.Length - maxLines);
                    return string.Join(Environment.NewLine, lines, start, lines.Length - start);
                }
                catch (Exception ex)
                {
                    return $"(Failed to read log: {ex.Message})";
                }
            }
        }

        public static bool OpenLogFolder()
        {
            try
            {
                string dir = LogDirectory;
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
                return true;
            }
            catch (Exception ex)
            {
                LogError(nameof(DiagnosticLogger), "Failed to open log folder", ex);
                return false;
            }
        }

        public static string ExportSupportDiagnosticsText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("============================================================");
            sb.AppendLine("PC Boost Support Diagnostics Bundle");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"App Version: {typeof(DiagnosticLogger).Assembly.GetName().Version}");
            sb.AppendLine($"OS: {Environment.OSVersion}");
            sb.AppendLine($"64-Bit OS / Process: {Environment.Is64BitOperatingSystem} / {Environment.Is64BitProcess}");
            sb.AppendLine($"Elevated (Administrator): {AdminPrivilegeService.IsRunningAsAdmin()}");
            sb.AppendLine($"Machine: {Environment.MachineName}");
            sb.AppendLine("============================================================");
            sb.AppendLine();

            try
            {
                var hw = new HardwareInfoService().GetDetailedReport();
                var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
                sb.AppendLine("--- Hardware Summary ---");
                sb.AppendLine($"CPU: {hw.CpuName} ({hw.CpuPhysicalCores} Cores / {hw.CpuLogicalCores} Threads)");
                sb.AppendLine($"Dedicated GPU: {(hw.DedicatedGpu != null ? $"{hw.DedicatedGpu.AdapterName} ({HardwareInfoService.FormatVramCompact(hw.DedicatedGpu.DedicatedUsedBytes)} / {HardwareInfoService.FormatVramCompact(hw.DedicatedGpu.DedicatedTotalBytes)})" : "None")}");
                sb.AppendLine($"Integrated GPU: {(hw.IntegratedGpu != null ? $"{hw.IntegratedGpu.AdapterName} ({HardwareInfoService.FormatVramCompact(hw.IntegratedGpu.DedicatedUsedBytes)} / {HardwareInfoService.FormatVramCompact(hw.IntegratedGpu.DedicatedTotalBytes)})" : "None")}");
                sb.AppendLine($"RAM: {HardwareInfoService.FormatInstalledRamGB(hw.TotalMemoryGB)}");
                sb.AppendLine($"OS: {hw.OsName}");
                sb.AppendLine($"Uptime: {up.Days}d {up.Hours}h {up.Minutes}m");
                sb.AppendLine();
            }
            catch (Exception ex)
            {
                sb.AppendLine($"(Hardware collection error: {ex.Message})");
                sb.AppendLine();
            }

            sb.AppendLine("--- Settings (settings.json) ---");
            try
            {
                string settingsFile = Path.Combine(LogDirectory, "settings.json");
                sb.AppendLine(File.Exists(settingsFile) ? File.ReadAllText(settingsFile) : "(settings.json not found)");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"(Settings read error: {ex.Message})");
            }
            sb.AppendLine();

            sb.AppendLine("--- Recent Application Log (app.log) ---");
            sb.AppendLine(ReadRecentLogTail(200));
            sb.AppendLine();

            if (File.Exists(CrashLogPath))
            {
                sb.AppendLine("--- Recent Crash Log (crash.log) ---");
                try
                {
                    string[] crashLines = File.ReadAllLines(CrashLogPath, Encoding.UTF8);
                    int start = Math.Max(0, crashLines.Length - 100);
                    sb.AppendLine(string.Join(Environment.NewLine, crashLines, start, crashLines.Length - start));
                }
                catch { }
            }

            return sb.ToString();
        }
    }
}
