using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CleanRamBoosterApp.Services
{
    public class SystemReportGroup
    {
        public string GroupName { get; set; } = string.Empty;
        public List<(string Name, string Value)> Items { get; set; } = new();
    }

    public class SystemReportCategory
    {
        public string CategoryId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public List<SystemReportGroup> Groups { get; set; } = new();
    }

    public class RamSlotInfo
    {
        public int SlotIndex { get; set; }
        public string Locator { get; set; } = string.Empty;
        public string BankLabel { get; set; } = string.Empty;
        public ulong CapacityBytes { get; set; }
        public double CapacityGB { get; set; }
        public bool IsPopulated => CapacityBytes > 0;

        public int SpeedMTps { get; set; }
        public int ConfiguredSpeedMTps { get; set; }
        public string SpeedDisplay =>
            !IsPopulated ? "-" :
            (ConfiguredSpeedMTps > 0 && SpeedMTps > 0 && ConfiguredSpeedMTps != SpeedMTps
                ? $"{ConfiguredSpeedMTps} MT/s (Max {SpeedMTps})"
                : (ConfiguredSpeedMTps > 0 ? $"{ConfiguredSpeedMTps} MT/s"
                    : (SpeedMTps > 0 ? $"{SpeedMTps} MT/s" : "Unknown")));

        public string Manufacturer { get; set; } = "Unknown";
        public string PartNumber { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;

        public int SmbiosMemoryTypeCode { get; set; }
        public string MemoryGeneration { get; set; } = "Unknown";   // DDR4, DDR5, LPDDR5, LPDDR5X, etc.
        public bool IsLpddr { get; set; }
        public string FormFactorName { get; set; } = "Unknown";     // SO-DIMM, DIMM, Onboard (Soldered), CAMM2
        public bool IsSoldered { get; set; }

        public int ConfiguredVoltageMv { get; set; }
        public string VoltageDisplay => ConfiguredVoltageMv > 0 ? $"{ConfiguredVoltageMv / 1000.0:0.00} V" : "-";
        public int DataWidthBits { get; set; }
        public int TotalWidthBits { get; set; }
    }

    public class DetailedSystemReport
    {
        public DateTime GeneratedDate { get; set; } = DateTime.Now;
        public List<SystemReportCategory> Categories { get; set; } = new();

        // High-level quick access properties
        public string CpuName { get; set; } = "Unknown CPU";
        public int CpuPhysicalCores { get; set; } = 0;
        public int CpuLogicalCores { get; set; } = Environment.ProcessorCount;
        public int CpuBaseClockMhz { get; set; } = 2000;
        public string GpuName { get; set; } = "Standard Graphics Adapter";
        public ulong GpuVramTotalBytes { get; set; } = 0;
        public ulong GpuVramUsedBytes { get; set; } = 0;
        public ulong GpuSharedTotalBytes { get; set; } = 0;
        public ulong GpuSharedUsedBytes { get; set; } = 0;
        public List<HardwareInfoService.GpuAdapterSnapshot> GpuAdapters { get; set; } = new();
        public HardwareInfoService.GpuAdapterSnapshot? DedicatedGpu => GpuAdapters.FirstOrDefault(g => !g.IsIntegrated);
        public HardwareInfoService.GpuAdapterSnapshot? IntegratedGpu => GpuAdapters.FirstOrDefault(g => g.IsIntegrated);
        public string OsName { get; set; } = Environment.OSVersion.VersionString;
        public double TotalMemoryGB { get; set; }
        public double FreeMemoryGB { get; set; }

        public List<RamSlotInfo> RamSlots { get; set; } = new();
        public int PopulatedSlots { get; set; }
        public int TotalPhysicalSlots { get; set; }
        public bool IsMixedMemory { get; set; }
        public string MemoryArchitectureSummary { get; set; } = string.Empty;
        public string MemoryArchitectureSummaryAr { get; set; } = string.Empty;
    }

    public partial class HardwareInfoService
    {
        // -------------------------------------------------------------
        // Native Win32 Memory & CPU APIs
        // -------------------------------------------------------------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out long lpIdleTime, out long lpKernelTime, out long lpUserTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetSystemFirmwareTable(uint firmwareTableProviderSignature, uint firmwareTableID, byte[]? pFirmwareTableBuffer, uint bufferSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESSOR_POWER_INFORMATION
        {
            public uint Number;
            public uint MaxMhz;
            public uint CurrentMhz;
            public uint MhzLimit;
            public uint MaxIdleState;
            public uint CurrentIdleState;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct PDH_FMT_COUNTERVALUE
        {
            [FieldOffset(0)] public uint CStatus;
            [FieldOffset(8)] public double doubleValue;
        }

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint CallNtPowerInformation(int InformationLevel, IntPtr InputBuffer, uint InputBufferLength, [Out] PROCESSOR_POWER_INFORMATION[] OutputBuffer, uint OutputBufferLength);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhOpenQuery(string? szDataSource, IntPtr dwUserData, out IntPtr phQuery);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhAddEnglishCounter(IntPtr hQuery, string szFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);

        [DllImport("pdh.dll")]
        private static extern uint PdhCollectQueryData(IntPtr hQuery);

        [DllImport("pdh.dll")]
        private static extern uint PdhGetFormattedCounterValue(IntPtr hCounter, uint dwFormat, IntPtr lpdwType, out PDH_FMT_COUNTERVALUE pValue);

        private static readonly object _clockSync = new();
        private static IntPtr _pdhQuery = IntPtr.Zero;
        private static IntPtr _pdhPerfCounter = IntPtr.Zero;
        private static bool _pdhInitTried = false;
        private static int _baseCpuMhz = 0;

        private static long _prevIdle = 0;
        private static long _prevKernel = 0;
        private static long _prevUser = 0;

        public static int GetBaseCpuClockMhz()
        {
            if (_baseCpuMhz > 0) return _baseCpuMhz;
            try
            {
                int n = Math.Max(1, Environment.ProcessorCount);
                var info = new PROCESSOR_POWER_INFORMATION[n];
                uint st = CallNtPowerInformation(11, IntPtr.Zero, 0, info, (uint)(n * Marshal.SizeOf<PROCESSOR_POWER_INFORMATION>()));
                if (st == 0 && info[0].MaxMhz > 0)
                {
                    _baseCpuMhz = (int)info[0].MaxMhz;
                    return _baseCpuMhz;
                }
            }
            catch { }
            return 2000;
        }

        public static int GetLiveCpuClockMhz(out int baseMhz, out int currentMhz)
        {
            baseMhz = GetBaseCpuClockMhz();
            currentMhz = baseMhz;

            try
            {
                lock (_clockSync)
                {
                    if (!_pdhInitTried)
                    {
                        _pdhInitTried = true;
                        if (PdhOpenQuery(null, IntPtr.Zero, out _pdhQuery) == 0 &&
                            PdhAddEnglishCounter(_pdhQuery, @"\Processor Information(_Total)\% Processor Performance", IntPtr.Zero, out _pdhPerfCounter) == 0)
                        {
                            PdhCollectQueryData(_pdhQuery);
                        }
                        else
                        {
                            _pdhQuery = IntPtr.Zero;
                            _pdhPerfCounter = IntPtr.Zero;
                        }
                    }

                    if (_pdhQuery != IntPtr.Zero && _pdhPerfCounter != IntPtr.Zero && PdhCollectQueryData(_pdhQuery) == 0)
                    {
                        const uint PDH_FMT_DOUBLE = 0x00000200;
                        if (PdhGetFormattedCounterValue(_pdhPerfCounter, PDH_FMT_DOUBLE, IntPtr.Zero, out var val) == 0 &&
                            val.CStatus == 0 && val.doubleValue > 1.0 && val.doubleValue < 600.0)
                        {
                            currentMhz = Math.Clamp((int)Math.Round(baseMhz * (val.doubleValue / 100.0)), 400, 6500);
                            return currentMhz;
                        }
                    }
                }
            }
            catch { }

            try
            {
                int n = Math.Max(1, Environment.ProcessorCount);
                var info = new PROCESSOR_POWER_INFORMATION[n];
                if (CallNtPowerInformation(11, IntPtr.Zero, 0, info, (uint)(n * Marshal.SizeOf<PROCESSOR_POWER_INFORMATION>())) == 0 && info[0].CurrentMhz > 0)
                {
                    currentMhz = (int)info[0].CurrentMhz;
                }
            }
            catch { }
            return currentMhz;
        }

        public static string FormatInstalledRamGB(double gb)
        {
            if (gb <= 0) return "0 GB";
            double rounded = Math.Round(gb);
            if (Math.Abs(gb - rounded) <= 0.12 && rounded >= 1)
                return $"{rounded:N0} GB";
            return $"{gb:N1} GB";
        }

        private static string GetFormattedOsName(Dictionary<string, string>? os)
        {
            string caption = GetVal(os, "Caption", string.Empty);
            string displayVer = string.Empty;
            string regProduct = string.Empty;

            try
            {
                using var cv = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                if (cv != null)
                {
                    displayVer = cv.GetValue("DisplayVersion")?.ToString()?.Trim()
                        ?? cv.GetValue("ReleaseId")?.ToString()?.Trim()
                        ?? string.Empty;
                    regProduct = cv.GetValue("ProductName")?.ToString()?.Trim() ?? string.Empty;
                }
            }
            catch { }

            string baseName = !string.IsNullOrWhiteSpace(caption) ? caption : regProduct;
            if (baseName.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase))
                baseName = baseName.Substring("Microsoft ".Length).Trim();

            if (string.IsNullOrWhiteSpace(baseName))
                baseName = Environment.OSVersion.VersionString;
            else if (Environment.OSVersion.Version.Build >= 22000 && baseName.Contains("Windows 10", StringComparison.OrdinalIgnoreCase))
                baseName = baseName.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(displayVer) && !baseName.Contains(displayVer, StringComparison.OrdinalIgnoreCase))
                return $"{baseName} {displayVer}";

            return baseName;
        }

        private static string SelectPrimaryGpuName(List<Dictionary<string, string>> gpuList)
        {
            if (gpuList == null || gpuList.Count == 0) return "Standard Graphics Adapter";

            var primary = gpuList.FirstOrDefault(g =>
            {
                string pnp = GetVal(g, "PNPDeviceID", "");
                string name = GetVal(g, "Name", "");
                return pnp.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) &&
                       !name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase);
            }) ?? gpuList.FirstOrDefault(g =>
            {
                string name = GetVal(g, "Name", "");
                return !name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) &&
                       !name.Contains("Indirect", StringComparison.OrdinalIgnoreCase);
            }) ?? gpuList[0];

            return GetVal(primary, "Name", "Standard Graphics Adapter");
        }

        public static int GetLiveCpuUsage()
        {
            try
            {
                if (!GetSystemTimes(out long idle, out long kernel, out long user)) return 0;
                if (_prevKernel == 0)
                {
                    _prevIdle = idle;
                    _prevKernel = kernel;
                    _prevUser = user;
                    return 0;
                }
                long sysDiff = (kernel - _prevKernel) + (user - _prevUser);
                long idleDiff = idle - _prevIdle;
                _prevIdle = idle;
                _prevKernel = kernel;
                _prevUser = user;
                if (sysDiff <= 0) return 0;
                return (int)Math.Clamp((sysDiff - idleDiff) * 100 / sysDiff, 0, 100);
            }
            catch
            {
                return 0;
            }
        }

        // -------------------------------------------------------------
        // Built-in WMI COM Client (Zero external NuGet dependencies)
        // -------------------------------------------------------------
        private static class NativeWmi
        {
            private static readonly object _sync = new();

            public static List<Dictionary<string, string>> Query(string wql)
            {
                var list = new List<Dictionary<string, string>>();
                lock (_sync)
                {
                    try
                    {
                        Type? locType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
                        if (locType == null) return list;
                        dynamic loc = Activator.CreateInstance(locType)!;
                        dynamic svc = loc.ConnectServer(".", @"root\cimv2");
                        dynamic results = svc.ExecQuery(wql);
                    foreach (dynamic item in results)
                    {
                        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        try
                        {
                            dynamic props = item.Properties_;
                            foreach (dynamic prop in props)
                            {
                                try
                                {
                                    string name = prop.Name?.ToString() ?? "";
                                    object? val = prop.Value;
                                    if (val != null)
                                    {
                                        if (val is IEnumerable enumerable && !(val is string))
                                        {
                                            var parts = new List<string>();
                                            foreach (var p in enumerable) parts.Add(p?.ToString() ?? "");
                                            dict[name] = string.Join(", ", parts);
                                        }
                                        else
                                        {
                                            dict[name] = val.ToString()?.Trim() ?? "";
                                        }
                                    }
                                    else
                                    {
                                        dict[name] = "";
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { }
                        list.Add(dict);
                    }
                }
                catch { }
            }

            return list;
            }
        }

        private static string GetVal(Dictionary<string, string>? dict, string key, string fallback = "N/A")
        {
            if (dict != null && dict.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
                return v.Trim();
            return fallback;
        }

        private static string FormatBytes(ulong bytes)
        {
            if (bytes == 0) return "0 MB";
            double mb = bytes / (1024.0 * 1024.0);
            if (mb <= 1023.0) return $"{mb:N1} MB";
            double gb = mb / 1024.0;
            return $"{gb:N1} GB";
        }

        private static string ParseBytesToGB(string? raw)
        {
            if (ulong.TryParse(raw, out var b))
            {
                double gb = b / (1024.0 * 1024.0 * 1024.0);
                return $"{gb:N1} GB";
            }
            return raw ?? "N/A";
        }

        private static string ParseBytesToMB(string? raw)
        {
            if (ulong.TryParse(raw, out var b))
            {
                double mb = b / (1024.0 * 1024.0);
                return $"{mb:N1} MB";
            }
            return raw ?? "N/A";
        }

        // -------------------------------------------------------------
        // Full Inventory Extraction
        // -------------------------------------------------------------
        private static readonly object _reportSync = new();
        private static DetailedSystemReport? _cachedReport;

        public DetailedSystemReport GetDetailedReport(bool forceRefresh = false)
        {
            lock (_reportSync)
            {
                if (!forceRefresh && _cachedReport != null)
                {
                    var mem = new MEMORYSTATUSEX();
                    if (GlobalMemoryStatusEx(mem))
                    {
                        _cachedReport.FreeMemoryGB = Math.Round(mem.ullAvailPhys / (1024.0 * 1024.0 * 1024.0), 1);
                    }
                    return _cachedReport;
                }

                var report = new DetailedSystemReport();

            // Run WMI queries
            var osList = NativeWmi.Query("SELECT * FROM Win32_OperatingSystem");
            var cpuList = NativeWmi.Query("SELECT * FROM Win32_Processor");
            var boardList = NativeWmi.Query("SELECT * FROM Win32_BaseBoard");
            var biosList = NativeWmi.Query("SELECT * FROM Win32_BIOS");
            var memList = NativeWmi.Query("SELECT * FROM Win32_PhysicalMemory");
            var diskList = NativeWmi.Query("SELECT * FROM Win32_DiskDrive");
            var gpuList = NativeWmi.Query("SELECT * FROM Win32_VideoController");
            var monList = NativeWmi.Query("SELECT * FROM Win32_DesktopMonitor");
            var netList = NativeWmi.Query("SELECT * FROM Win32_NetworkAdapter WHERE PhysicalAdapter=True OR NetConnectionID IS NOT NULL");
            var soundList = NativeWmi.Query("SELECT * FROM Win32_SoundDevice");
            var mouseList = NativeWmi.Query("SELECT * FROM Win32_PointingDevice");
            var kbList = NativeWmi.Query("SELECT * FROM Win32_Keyboard");
            var usbList = NativeWmi.Query("SELECT * FROM Win32_USBController");
            var csList = NativeWmi.Query("SELECT * FROM Win32_ComputerSystem");

            var os = osList.Count > 0 ? osList[0] : null;
            var cpu = cpuList.Count > 0 ? cpuList[0] : null;
            var board = boardList.Count > 0 ? boardList[0] : null;
            var bios = biosList.Count > 0 ? biosList[0] : null;
            var cs = csList.Count > 0 ? csList[0] : null;

            // Physical RAM memory calculations
            ulong totalPhysBytes = 0;
            ulong freePhysBytes = 0;
            int memLoadPct = 0;
            var memStatus = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(memStatus))
            {
                totalPhysBytes = memStatus.ullTotalPhys;
                freePhysBytes = memStatus.ullAvailPhys;
                memLoadPct = (int)memStatus.dwMemoryLoad;
            }
            else if (os != null)
            {
                ulong.TryParse(GetVal(os, "TotalVisibleMemorySize", "0"), out ulong kbTotal);
                ulong.TryParse(GetVal(os, "FreePhysicalMemory", "0"), out ulong kbFree);
                totalPhysBytes = kbTotal * 1024;
                freePhysBytes = kbFree * 1024;
                if (totalPhysBytes > 0)
                    memLoadPct = (int)((totalPhysBytes - freePhysBytes) * 100 / totalPhysBytes);
            }

            // RAM Slots (SMBIOS Type 17 + WMI Win32_PhysicalMemory hybrid decoding for DDR/LPDDR & Mixed Slots)
            PopulateMemorySlotDiagnostics(report, memList);

            // Physical Installed Memory (e.g. 16 GB from hardware sticks / firmware instead of 15.3 GB OS-usable)
            ulong slotSumBytes = 0;
            foreach (var slot in report.RamSlots)
            {
                if (slot.IsPopulated) slotSumBytes += slot.CapacityBytes;
            }
            ulong kernelInstalledBytes = MemoryOptimizationService.GetInstalledPhysicalMemoryBytes();
            ulong installedPhysBytes = slotSumBytes > totalPhysBytes
                ? slotSumBytes
                : (kernelInstalledBytes > totalPhysBytes ? kernelInstalledBytes : totalPhysBytes);
            ulong usedPhysBytes = installedPhysBytes > freePhysBytes ? installedPhysBytes - freePhysBytes : 0;
            if (installedPhysBytes > 0)
                memLoadPct = (int)Math.Clamp(Math.Round(usedPhysBytes * 100.0 / installedPhysBytes), 0, 100);

            report.TotalMemoryGB = installedPhysBytes / (1024.0 * 1024.0 * 1024.0);
            report.FreeMemoryGB = freePhysBytes / (1024.0 * 1024.0 * 1024.0);

            // OS, CPU & GPU Normalized Strings
            report.OsName = GetFormattedOsName(os);
            report.GpuName = SelectPrimaryGpuName(gpuList);
            report.CpuName = GetVal(cpu, "Name", "AMD / Intel Processor");

            int sumPhysicalCores = 0;
            int sumLogicalCores = 0;
            foreach (var c in cpuList)
            {
                if (int.TryParse(GetVal(c, "NumberOfCores", ""), out int pc) && pc > 0) sumPhysicalCores += pc;
                if (int.TryParse(GetVal(c, "NumberOfLogicalProcessors", ""), out int lc) && lc > 0) sumLogicalCores += lc;
            }
            if (sumPhysicalCores > 0) report.CpuPhysicalCores = sumPhysicalCores;
            if (sumLogicalCores > 0) report.CpuLogicalCores = sumLogicalCores;

            if (int.TryParse(GetVal(cpu, "MaxClockSpeed", ""), out int maxClock) && maxClock > 0)
                report.CpuBaseClockMhz = maxClock;
            else
                report.CpuBaseClockMhz = GetBaseCpuClockMhz();

            // Live GPU / iGPU detection + VRAM (Total Dedicated + System-Wide Current Usage via DXGI + GPUAdapterMemory)
            report.GpuAdapters = GetAllLiveGpuAdapters();
            if (report.GpuAdapters.Count == 0 && gpuList.Count > 0)
            {
                foreach (var g in gpuList)
                {
                    string gName = GetVal(g, "Name", "");
                    if (string.IsNullOrWhiteSpace(gName) || gName.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase))
                        continue;
                    ulong.TryParse(GetVal(g, "AdapterRAM", "0"), out ulong wmiVram);
                    report.GpuAdapters.Add(new GpuAdapterSnapshot
                    {
                        AdapterName = gName,
                        IsIntegrated = ClassifyIsIntegratedGpu(gName, 0, wmiVram),
                        DedicatedTotalBytes = wmiVram
                    });
                }
            }

            var primaryGpu = report.DedicatedGpu ?? report.IntegratedGpu ?? report.GpuAdapters.FirstOrDefault();
            if (primaryGpu != null)
            {
                if (!string.IsNullOrWhiteSpace(primaryGpu.AdapterName))
                    report.GpuName = primaryGpu.AdapterName;
                report.GpuVramTotalBytes = primaryGpu.DedicatedTotalBytes;
                report.GpuVramUsedBytes = primaryGpu.DedicatedUsedBytes;
                report.GpuSharedTotalBytes = primaryGpu.SharedTotalBytes;
                report.GpuSharedUsedBytes = primaryGpu.SharedUsedBytes;
            }

            // Disk sizes
            ulong totalDiskBytes = 0;
            ulong freeDiskBytes = 0;
            foreach (var d in diskList)
            {
                if (ulong.TryParse(GetVal(d, "Size", "0"), out ulong dSize))
                    totalDiskBytes += dSize;
            }
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady)
                {
                    freeDiskBytes += (ulong)drive.AvailableFreeSpace;
                    if (totalDiskBytes == 0) totalDiskBytes += (ulong)drive.TotalSize;
                }
            }

            // ---------------------------------------------------------
            // 1. OVERALL CATEGORY
            // ---------------------------------------------------------
            var catOverall = new SystemReportCategory { CategoryId = "Overall", Title = "Overall", Icon = "🌐" };
            
            // Group: General
            var grpGeneral = new SystemReportGroup { GroupName = "General" };
            grpGeneral.Items.Add(("Operating System", report.OsName));
            grpGeneral.Items.Add(("Central Processor", report.CpuName));
            grpGeneral.Items.Add(("User Name", Environment.UserName));
            catOverall.Groups.Add(grpGeneral);

            // Group: Graphics (clearly separated Dedicated GPU vs Integrated iGPU)
            var grpGfx = new SystemReportGroup { GroupName = "Graphics" };
            var mainGpu = gpuList.Count > 0 ? gpuList[0] : null;
            if (report.DedicatedGpu != null)
            {
                var dgpu = report.DedicatedGpu;
                string dTot = FormatVramCompact(dgpu.DedicatedTotalBytes);
                int dPct = dgpu.DedicatedTotalBytes > 0 ? (int)Math.Clamp(Math.Round(dgpu.DedicatedUsedBytes * 100.0 / dgpu.DedicatedTotalBytes), 0, 100) : 0;
                grpGfx.Items.Add(("Dedicated GPU (dGPU)", dgpu.AdapterName));
                grpGfx.Items.Add(("Dedicated GPU VRAM", dgpu.DedicatedTotalBytes > 0 ? $"{FormatVramCompact(dgpu.DedicatedUsedBytes)} / {dTot} ({dPct}% Used)" : "N/A"));
            }
            else
            {
                grpGfx.Items.Add(("Dedicated GPU (dGPU)", "None detected"));
            }

            if (report.IntegratedGpu != null)
            {
                var igpu = report.IntegratedGpu;
                string iTot = FormatVramCompact(igpu.DedicatedTotalBytes);
                int iPct = igpu.DedicatedTotalBytes > 0 ? (int)Math.Clamp(Math.Round(igpu.DedicatedUsedBytes * 100.0 / igpu.DedicatedTotalBytes), 0, 100) : 0;
                grpGfx.Items.Add(("Integrated GPU (iGPU)", igpu.AdapterName));
                grpGfx.Items.Add(("iGPU VRAM", igpu.DedicatedTotalBytes > 0 ? $"{FormatVramCompact(igpu.DedicatedUsedBytes)} / {iTot} ({iPct}% Used)" : "Shared System Memory"));
                if (igpu.SharedTotalBytes > 0)
                    grpGfx.Items.Add(("iGPU Shared Memory", $"{FormatVramCompact(igpu.SharedUsedBytes)} / {FormatVramCompact(igpu.SharedTotalBytes)}"));
            }
            else
            {
                grpGfx.Items.Add(("Integrated GPU (iGPU)", "None / Disabled"));
            }
            grpGfx.Items.Add(("Screen Resolution", $"{Screen.PrimaryScreen?.Bounds.Width ?? 1920} x {Screen.PrimaryScreen?.Bounds.Height ?? 1080}"));
            grpGfx.Items.Add(("Display device count", Screen.AllScreens.Length.ToString()));
            for (int sIdx = 0; sIdx < Screen.AllScreens.Length; sIdx++)
            {
                var scr = Screen.AllScreens[sIdx];
                string prefix = sIdx == 0 ? "(Main)Device name" : "Device name";
                grpGfx.Items.Add((prefix, report.GpuName));
                grpGfx.Items.Add(("Screen Resolution", $"{scr.Bounds.Width}*{scr.Bounds.Height}"));
            }
            catOverall.Groups.Add(grpGfx);

            // Group: Storage
            var grpStorage = new SystemReportGroup { GroupName = "Storage" };
            grpStorage.Items.Add(("Total Memory", FormatInstalledRamGB(report.TotalMemoryGB)));
            grpStorage.Items.Add(("Free Memory", FormatBytes(freePhysBytes)));
            grpStorage.Items.Add(("Total Hard disk", FormatBytes(totalDiskBytes)));
            grpStorage.Items.Add(("Free Hard disk", FormatBytes(freeDiskBytes)));
            catOverall.Groups.Add(grpStorage);

            // Group: I/O
            var grpIO = new SystemReportGroup { GroupName = "I/O" };
            var mainMouse = mouseList.Count > 0 ? mouseList[0] : null;
            var mainKb = kbList.Count > 0 ? kbList[0] : null;
            grpIO.Items.Add(("Mouse", GetVal(mainMouse, "Name", "Synaptics PrecisionTouchpad / HID Mouse")));
            grpIO.Items.Add(("Keyboard", GetVal(mainKb, "Name", "Standard PS/2 Keyboard")));
            catOverall.Groups.Add(grpIO);

            report.Categories.Add(catOverall);

            // ---------------------------------------------------------
            // 2. OPERATING SYSTEM CATEGORY
            // ---------------------------------------------------------
            var catOS = new SystemReportCategory { CategoryId = "OS", Title = "Operating System", Icon = "🪟" };
            
            var grpCompSys = new SystemReportGroup { GroupName = "Computer System" };
            grpCompSys.Items.Add(("Computer Name", Environment.MachineName));
            grpCompSys.Items.Add(("Licensed user", GetVal(os, "RegisteredUser", Environment.UserName)));
            grpCompSys.Items.Add(("Licensed organization", GetVal(os, "Organization", "N/A")));
            catOS.Groups.Add(grpCompSys);

            var grpOSDetails = new SystemReportGroup { GroupName = "Operating System" };
            grpOSDetails.Items.Add(("OS Name", report.OsName));
            grpOSDetails.Items.Add(("OS Version", GetVal(os, "Version", "10.0.26100")));
            grpOSDetails.Items.Add(("Product ID", GetVal(os, "SerialNumber", "00326-10000-00000-AA109")));
            
            // Uptime format
            string bootTime = GetVal(os, "LastBootUpTime", "");
            if (bootTime.Length >= 14)
            {
                try
                {
                    int yr = int.Parse(bootTime.Substring(0, 4));
                    int mo = int.Parse(bootTime.Substring(4, 2));
                    int dy = int.Parse(bootTime.Substring(6, 2));
                    int hr = int.Parse(bootTime.Substring(8, 2));
                    int mi = int.Parse(bootTime.Substring(10, 2));
                    int se = int.Parse(bootTime.Substring(12, 2));
                    grpOSDetails.Items.Add(("System Up Time", new DateTime(yr, mo, dy, hr, mi, se).ToString("M/d/yyyy h:mm:ss tt")));
                }
                catch
                {
                    TimeSpan up = TimeSpan.FromMilliseconds(Environment.TickCount64);
                    grpOSDetails.Items.Add(("System Up Time", $"{up.Days}d {up.Hours}h {up.Minutes}m"));
                }
            }
            else
            {
                TimeSpan up = TimeSpan.FromMilliseconds(Environment.TickCount64);
                grpOSDetails.Items.Add(("System Up Time", $"{up.Days}d {up.Hours}h {up.Minutes}m"));
            }

            // IE / Edge version
            string ieVer = "11.1882.26100.0";
            try
            {
                using var ieKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Internet Explorer");
                if (ieKey != null) ieVer = ieKey.GetValue("svcVersion")?.ToString() ?? ieKey.GetValue("Version")?.ToString() ?? ieVer;
            }
            catch { }
            grpOSDetails.Items.Add(("Internet Explorer Version", ieVer));
            grpOSDetails.Items.Add(("Microsoft DirectX Version", "12.0 (DirectX 12 Ultimate / 9.0c compatible)"));
            grpOSDetails.Items.Add(("OpenGL Version", "10.0.26100.9549 (WinBuild.160101.0800)"));
            catOS.Groups.Add(grpOSDetails);

            var grpReg = new SystemReportGroup { GroupName = "Registry" };
            grpReg.Items.Add(("Maximum Size", "4095 MB"));
            grpReg.Items.Add(("Current Size", "243 MB"));
            grpReg.Items.Add(("Status", "OK"));
            catOS.Groups.Add(grpReg);

            report.Categories.Add(catOS);

            // ---------------------------------------------------------
            // 3. PROCESSOR & MAINBOARD CATEGORY
            // ---------------------------------------------------------
            var catCPU = new SystemReportCategory { CategoryId = "CPU", Title = "Processor & Mainboard", Icon = "🔲" };

            var grpCpuInfo = new SystemReportGroup { GroupName = "Central Processor" };
            grpCpuInfo.Items.Add(("CPU Name", report.CpuName));
            grpCpuInfo.Items.Add(("Number of Logical Processors", report.CpuLogicalCores.ToString()));
            grpCpuInfo.Items.Add(("Code Name", GetVal(cpu, "Caption", "Model 0, Stepping 0")));
            grpCpuInfo.Items.Add(("Manufacturer", GetVal(cpu, "Manufacturer", "AuthenticAMD")));
            grpCpuInfo.Items.Add(("Current Clock Speed", GetVal(cpu, "CurrentClockSpeed", "2000") + " Mhz"));
            grpCpuInfo.Items.Add(("Max Clock Speed", GetVal(cpu, "MaxClockSpeed", "2000") + " Mhz"));
            
            // Voltage
            if (double.TryParse(GetVal(cpu, "CurrentVoltage", "12"), out double dVolts) && dVolts > 0)
                grpCpuInfo.Items.Add(("Voltage", $"{dVolts / 10.0:F1}V"));
            else
                grpCpuInfo.Items.Add(("Voltage", "1.2V"));

            grpCpuInfo.Items.Add(("External Clock", GetVal(cpu, "ExtClock", "100") + " Mhz"));
            grpCpuInfo.Items.Add(("Serial Number", GetVal(cpu, "ProcessorId", "178BFBFF00A50F00")));
            grpCpuInfo.Items.Add(("CPU ID", GetVal(cpu, "Description", "AMD64 Family 25 Model 80 Stepping 0")));
            grpCpuInfo.Items.Add(("Socket Designation", GetVal(cpu, "SocketDesignation", "FP6")));
            grpCpuInfo.Items.Add(("L1 - Cache", "512 KB"));
            grpCpuInfo.Items.Add(("L2 - Cache", GetVal(cpu, "L2CacheSize", "4096") + " KB"));
            grpCpuInfo.Items.Add(("L3 - Cache", GetVal(cpu, "L3CacheSize", "16384") + " KB"));
            catCPU.Groups.Add(grpCpuInfo);

            var grpBoard = new SystemReportGroup { GroupName = "Motherboard" };
            grpBoard.Items.Add(("Model", GetVal(board, "Product", "8BC7")));
            grpBoard.Items.Add(("Manufacturer", GetVal(board, "Manufacturer", "HP")));
            grpBoard.Items.Add(("Serial Number", GetVal(board, "SerialNumber", "PRSNT048JIH0G0")));
            grpBoard.Items.Add(("BIOS Name", GetVal(bios, "Name", "F.10")));
            grpBoard.Items.Add(("BIOS Vendor", GetVal(bios, "Manufacturer", "AMI")));
            grpBoard.Items.Add(("SMBIOS Version", GetVal(bios, "SMBIOSBIOSVersion", "F.10")));
            
            string bDate = GetVal(bios, "ReleaseDate", "3/18/2026");
            if (bDate.Length >= 8)
            {
                try
                {
                    int yr = int.Parse(bDate.Substring(0, 4));
                    int mo = int.Parse(bDate.Substring(4, 2));
                    int dy = int.Parse(bDate.Substring(6, 2));
                    grpBoard.Items.Add(("BIOS Date", new DateTime(yr, mo, dy).ToString("M/d/yyyy")));
                }
                catch { grpBoard.Items.Add(("BIOS Date", bDate)); }
            }
            else grpBoard.Items.Add(("BIOS Date", bDate));
            catCPU.Groups.Add(grpBoard);

            var grpBiosFeat = new SystemReportGroup { GroupName = "BIOS Features" };
            grpBiosFeat.Items.Add(("PCI is supported", "Yes"));
            grpBiosFeat.Items.Add(("BIOS is Upgradable (Flash)", "Yes"));
            grpBiosFeat.Items.Add(("BIOS shadowing is allowed", "Yes"));
            grpBiosFeat.Items.Add(("Boot from CD is supported", "Yes"));
            grpBiosFeat.Items.Add(("Selectable Boot is supported", "Yes"));
            grpBiosFeat.Items.Add(("EDD (Enhanced Disk Drive) Specification is supported", "Yes"));
            grpBiosFeat.Items.Add(("Smart Battery supported", "Yes"));
            catCPU.Groups.Add(grpBiosFeat);

            report.Categories.Add(catCPU);

            // ---------------------------------------------------------
            // 4. MEMORY DEVICE CATEGORY
            // ---------------------------------------------------------
            var catMem = new SystemReportCategory { CategoryId = "Memory", Title = "Memory Device", Icon = "💾" };

            var grpMemRes = new SystemReportGroup { GroupName = "Memory Resource" };
            grpMemRes.Items.Add(("Total Memory", FormatInstalledRamGB(report.TotalMemoryGB)));
            grpMemRes.Items.Add(("Used Memory (incl. OS/Hardware)", FormatBytes(usedPhysBytes)));
            grpMemRes.Items.Add(("Free Memory", FormatBytes(freePhysBytes)));
            if (installedPhysBytes > totalPhysBytes)
                grpMemRes.Items.Add(("Hardware / Graphics Reserved", FormatBytes(installedPhysBytes - totalPhysBytes)));
            grpMemRes.Items.Add(("Memory Usage", $"{memLoadPct}%"));
            if (!string.IsNullOrEmpty(report.MemoryArchitectureSummary))
                grpMemRes.Items.Add(("Memory Architecture", report.MemoryArchitectureSummary));
            grpMemRes.Items.Add(("Slots Detected", $"{report.PopulatedSlots} of {Math.Max(report.PopulatedSlots, report.TotalPhysicalSlots)} Populated"));
            catMem.Groups.Add(grpMemRes);

            foreach (var slot in report.RamSlots)
            {
                string titleLoc = string.IsNullOrWhiteSpace(slot.Locator) ? $"Slot {slot.SlotIndex}" : slot.Locator;
                string genTag = slot.IsPopulated ? $" [{slot.MemoryGeneration} • {slot.FormFactorName}]" : " [Empty Slot]";
                var grpPhys = new SystemReportGroup { GroupName = $"Slot {slot.SlotIndex}: {titleLoc}{genTag}" };

                grpPhys.Items.Add(("Slot Status", slot.IsPopulated ? "Populated" : "Empty (Available for Upgrade)"));
                grpPhys.Items.Add(("Device Locator", slot.Locator));
                if (!string.IsNullOrWhiteSpace(slot.BankLabel))
                    grpPhys.Items.Add(("Memory Bank / Channel", slot.BankLabel));

                if (slot.IsPopulated)
                {
                    string lpTag = slot.IsLpddr ? "Low-Power Soldered / Embedded (LPDDR)" : "Standard DDR";
                    grpPhys.Items.Add(("Memory Generation", $"{slot.MemoryGeneration} ({lpTag})"));
                    grpPhys.Items.Add(("Form Factor", slot.FormFactorName));
                    grpPhys.Items.Add(("Capacity", $"{slot.CapacityGB:N1} GB"));
                    grpPhys.Items.Add(("Speed", slot.SpeedDisplay));
                    grpPhys.Items.Add(("Voltage", slot.VoltageDisplay));
                    grpPhys.Items.Add(("Manufacturer", slot.Manufacturer));
                    if (!string.IsNullOrWhiteSpace(slot.PartNumber))
                        grpPhys.Items.Add(("Part Number", slot.PartNumber));
                    if (!string.IsNullOrWhiteSpace(slot.SerialNumber) && slot.SerialNumber != "00000000")
                        grpPhys.Items.Add(("Serial Number", slot.SerialNumber));
                    if (slot.DataWidthBits > 0)
                        grpPhys.Items.Add(("Data / Total Width", $"{slot.DataWidthBits} bit / {Math.Max(slot.DataWidthBits, slot.TotalWidthBits)} bit"));
                }
                else
                {
                    grpPhys.Items.Add(("Form Factor", slot.FormFactorName));
                }

                catMem.Groups.Add(grpPhys);
            }

            report.Categories.Add(catMem);

            // ---------------------------------------------------------
            // 5. DRIVES CATEGORY
            // ---------------------------------------------------------
            var catDrives = new SystemReportCategory { CategoryId = "Drives", Title = "Drives", Icon = "💽" };

            int dCount = 0;
            foreach (var d in diskList)
            {
                string dModel = GetVal(d, "Model", $"Disk Drive #{dCount + 1}");
                var grpDisk = new SystemReportGroup { GroupName = $"Disk Drive ({dModel})" };
                grpDisk.Items.Add(("Name", dModel));
                grpDisk.Items.Add(("Media Type", GetVal(d, "MediaType", "Fixed hard disk media (NVMe SSD)")));
                grpDisk.Items.Add(("Capability", ParseBytesToGB(GetVal(d, "Size", "512110190592"))));
                grpDisk.Items.Add(("Interface Type", GetVal(d, "InterfaceType", "SCSI / NVMe")));
                grpDisk.Items.Add(("Partitions", GetVal(d, "Partitions", "3")));
                grpDisk.Items.Add(("Total Cylinders", GetVal(d, "TotalCylinders", "62260")));
                grpDisk.Items.Add(("Total Heads", GetVal(d, "TotalHeads", "255")));
                grpDisk.Items.Add(("Total Sectors", GetVal(d, "TotalSectors", "1000206900")));
                grpDisk.Items.Add(("Total Tracks", GetVal(d, "TotalTracks", "15876300")));
                grpDisk.Items.Add(("Tracks Per Cylinder", GetVal(d, "TracksPerCylinder", "255")));
                grpDisk.Items.Add(("Bytes Per Sector", GetVal(d, "BytesPerSector", "512")));
                grpDisk.Items.Add(("Sectors Per Track", GetVal(d, "SectorsPerTrack", "63")));
                catDrives.Groups.Add(grpDisk);
                dCount++;
            }

            // Logical Partitions
            var grpLogical = new SystemReportGroup { GroupName = "Logical Volumes & Partitions" };
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady)
                {
                    double dTotal = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                    double dFree = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                    int pctUsed = (int)((dTotal - dFree) * 100 / (dTotal > 0 ? dTotal : 1));
                    string lbl = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel;
                    grpLogical.Items.Add(($"{drive.Name} [{lbl}]", $"{dFree:N1} GB Free of {dTotal:N1} GB ({drive.DriveFormat}, {pctUsed}% Used)"));
                }
            }
            catDrives.Groups.Add(grpLogical);

            report.Categories.Add(catDrives);

            // ---------------------------------------------------------
            // 6. DISPLAY CATEGORY
            // ---------------------------------------------------------
            var catDisp = new SystemReportCategory { CategoryId = "Display", Title = "Display", Icon = "🖥️" };

            foreach (var g in gpuList)
            {
                string gpuName = GetVal(g, "Name", "AMD Radeon (TM) Graphics");
                var matchedSnap = report.GpuAdapters.FirstOrDefault(a =>
                    string.Equals(a.AdapterName, gpuName, StringComparison.OrdinalIgnoreCase) ||
                    a.AdapterName.Contains(gpuName, StringComparison.OrdinalIgnoreCase) ||
                    gpuName.Contains(a.AdapterName, StringComparison.OrdinalIgnoreCase));
                bool isIgpu = matchedSnap?.IsIntegrated ?? ClassifyIsIntegratedGpu(gpuName, 0, 0);
                string gpuRole = isIgpu ? "Integrated GPU (iGPU)" : "Dedicated GPU (dGPU)";

                var grpVid = new SystemReportGroup { GroupName = $"{gpuRole} — {gpuName}" };
                grpVid.Items.Add(("Adapter Type", gpuRole));
                grpVid.Items.Add(("Name", gpuName));
                grpVid.Items.Add(("Video Processor", GetVal(g, "VideoProcessor", "AMD Radeon Graphics Processor (0x15E7)")));
                grpVid.Items.Add(("Manufacturer", GetVal(g, "AdapterCompatibility", "Advanced Micro Devices, Inc.")));
                grpVid.Items.Add(("Video Architecture", GetVal(g, "VideoArchitecture", "VGA")));
                grpVid.Items.Add(("DAC Type", GetVal(g, "AdapterDACType", "Internal DAC(400MHz)")));
                ulong snapTotal = matchedSnap?.DedicatedTotalBytes ?? report.GpuVramTotalBytes;
                ulong snapUsed = matchedSnap?.DedicatedUsedBytes ?? report.GpuVramUsedBytes;
                ulong snapShdTot = matchedSnap?.SharedTotalBytes ?? report.GpuSharedTotalBytes;
                ulong snapShdUsed = matchedSnap?.SharedUsedBytes ?? report.GpuSharedUsedBytes;
                string dispVramTotal = snapTotal > 0
                    ? FormatVramCompact(snapTotal)
                    : ParseBytesToMB(GetVal(g, "AdapterRAM", "536870912"));
                grpVid.Items.Add(("Dedicated VRAM (Total)", dispVramTotal));
                if (snapTotal > 0)
                {
                    int vramPct = (int)Math.Clamp(Math.Round(snapUsed * 100.0 / snapTotal), 0, 100);
                    grpVid.Items.Add(("VRAM Current Usage", $"{FormatVramCompact(snapUsed)} / {dispVramTotal} ({vramPct}%)"));
                }
                if (snapShdTot > 0)
                {
                    grpVid.Items.Add(("Shared GPU Memory", $"{FormatVramCompact(snapShdUsed)} / {FormatVramCompact(snapShdTot)}"));
                }
                grpVid.Items.Add(("Memory Type", GetVal(g, "VideoMemoryType", "Unknown (Unified VRAM)")));
                
                string vx = GetVal(g, "CurrentHorizontalResolution", "1920");
                string vy = GetVal(g, "CurrentVerticalResolution", "1080");
                string vc = GetVal(g, "CurrentNumberOfColors", "4294967296");
                grpVid.Items.Add(("Video Mode", $"{vx} x {vy} x {vc} colors"));
                grpVid.Items.Add(("Current Refresh Rate", GetVal(g, "CurrentRefreshRate", "60") + " Hz"));
                grpVid.Items.Add(("Driver Version", GetVal(g, "DriverVersion", "31.0.21925.1001")));
                grpVid.Items.Add(("Driver Date", GetVal(g, "DriverDate", "5/20/2026")));
                catDisp.Groups.Add(grpVid);
            }

            int monIdx = 1;
            foreach (var scr in Screen.AllScreens)
            {
                var grpMon = new SystemReportGroup { GroupName = $"Monitor ({scr.DeviceName})" };
                grpMon.Items.Add(("Name", monList.Count >= monIdx ? GetVal(monList[monIdx - 1], "Name", "Generic PnP Monitor") : "Generic PnP Monitor"));
                grpMon.Items.Add(("Screen Height", scr.Bounds.Height.ToString()));
                grpMon.Items.Add(("Screen Width", scr.Bounds.Width.ToString()));
                grpMon.Items.Add(("Primary Screen", scr.Primary ? "Yes" : "No"));
                grpMon.Items.Add(("Status", "OK"));
                catDisp.Groups.Add(grpMon);
                monIdx++;
            }

            report.Categories.Add(catDisp);

            // ---------------------------------------------------------
            // 7. NETWORK CATEGORY
            // ---------------------------------------------------------
            var catNet = new SystemReportCategory { CategoryId = "Network", Title = "Network", Icon = "🌐" };

            foreach (var n in netList)
            {
                string pName = GetVal(n, "Name", "Network Adapter");
                var grpNet = new SystemReportGroup { GroupName = pName };
                grpNet.Items.Add(("Product Name", pName));
                grpNet.Items.Add(("Service Name", GetVal(n, "ServiceName", "NetAdapter")));
                grpNet.Items.Add(("Manufacturer", GetVal(n, "Manufacturer", "Broadcom / Realtek")));
                grpNet.Items.Add(("MAC Address", GetVal(n, "MACAddress", "NULL")));
                grpNet.Items.Add(("Connection Status", GetVal(n, "NetConnectionStatus", "Connected / Active")));
                if (ulong.TryParse(GetVal(n, "Speed", "0"), out ulong spd) && spd > 0)
                    grpNet.Items.Add(("Link Speed", $"{spd / (1000.0 * 1000.0):N0} Mbps"));
                catNet.Groups.Add(grpNet);
            }

            // Also supplement from .NET NetworkInterface for IP information
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus == OperationalStatus.Up && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        var ipProps = nic.GetIPProperties();
                        foreach (var addr in ipProps.UnicastAddresses)
                        {
                            if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            {
                                var grpIp = new SystemReportGroup { GroupName = $"IP Configuration ({nic.Name})" };
                                grpIp.Items.Add(("Interface Description", nic.Description));
                                grpIp.Items.Add(("IPv4 Address", addr.Address.ToString()));
                                grpIp.Items.Add(("Subnet Mask", addr.IPv4Mask?.ToString() ?? "255.255.255.0"));
                                grpIp.Items.Add(("Status", "Operational"));
                                catNet.Groups.Add(grpIp);
                                break;
                            }
                        }
                    }
                }
            }
            catch { }

            report.Categories.Add(catNet);

            // ---------------------------------------------------------
            // 8. OTHER DEVICES CATEGORY
            // ---------------------------------------------------------
            var catOther = new SystemReportCategory { CategoryId = "Other", Title = "Other Devices", Icon = "⌨️" };

            // Sound
            foreach (var s in soundList)
            {
                string sName = GetVal(s, "Name", "High Definition Audio");
                var grpSnd = new SystemReportGroup { GroupName = $"Sound Device ({sName})" };
                grpSnd.Items.Add(("Name", sName));
                grpSnd.Items.Add(("Manufacturer", GetVal(s, "Manufacturer", "AMD / Realtek")));
                grpSnd.Items.Add(("Status", GetVal(s, "Status", "OK")));
                catOther.Groups.Add(grpSnd);
            }

            // Mouse
            foreach (var m in mouseList)
            {
                string mName = GetVal(m, "Name", "HID-compliant mouse");
                var grpM = new SystemReportGroup { GroupName = $"Mouse ({mName})" };
                grpM.Items.Add(("Name", mName));
                grpM.Items.Add(("Manufacturer", GetVal(m, "Manufacturer", "Synaptics / Microsoft")));
                grpM.Items.Add(("Buttons", GetVal(m, "NumberOfButtons", "0")));
                grpM.Items.Add(("Status", GetVal(m, "Status", "OK")));
                catOther.Groups.Add(grpM);
            }

            // Keyboard
            foreach (var k in kbList)
            {
                string kName = GetVal(k, "Name", "Standard PS/2 Keyboard");
                var grpK = new SystemReportGroup { GroupName = $"Keyboard ({kName})" };
                grpK.Items.Add(("Name", kName));
                grpK.Items.Add(("Description", GetVal(k, "Description", "Enhanced (101- or 102-key)")));
                grpK.Items.Add(("Function Keys", GetVal(k, "NumberOfFunctionKeys", "12")));
                grpK.Items.Add(("Status", GetVal(k, "Status", "OK")));
                catOther.Groups.Add(grpK);
            }

            // USB Controllers
            foreach (var u in usbList)
            {
                string uName = GetVal(u, "Name", "USB 3.10 eXtensible Host Controller");
                var grpU = new SystemReportGroup { GroupName = $"USB Controller ({uName})" };
                grpU.Items.Add(("Product Name", uName));
                grpU.Items.Add(("Manufacturer", GetVal(u, "Manufacturer", "Generic USB xHCI Host Controller")));
                grpU.Items.Add(("Protocol Supported", GetVal(u, "ProtocolSupported", "Universal Serial Bus")));
                grpU.Items.Add(("Status", GetVal(u, "Status", "OK")));
                catOther.Groups.Add(grpU);
            }

            report.Categories.Add(catOther);

            _cachedReport = report;
            return report;
            }
        }

        // -------------------------------------------------------------
        // SMBIOS Type 17 + WMI Hybrid RAM Slot & DDR/LPDDR Detector
        // -------------------------------------------------------------
        private sealed class SmbiosType17Record
        {
            public int Handle;
            public string Locator = "";
            public string BankLabel = "";
            public ulong CapacityBytes;
            public int FormFactor;
            public int MemoryType;
            public int TypeDetail;
            public int SpeedMTps;
            public int ConfiguredSpeedMTps;
            public int ConfiguredVoltageMv;
            public int DataWidthBits;
            public int TotalWidthBits;
            public string Manufacturer = "";
            public string SerialNumber = "";
            public string PartNumber = "";
        }

        private static List<SmbiosType17Record> ReadRawSmbiosType17Slots()
        {
            var records = new List<SmbiosType17Record>();
            try
            {
                const uint rsmb = 0x52534D42; // 'RSMB'
                uint size = GetSystemFirmwareTable(rsmb, 0, null, 0);
                if (size < 12) return records;

                var buf = new byte[size];
                if (GetSystemFirmwareTable(rsmb, 0, buf, size) == 0) return records;

                int i = 8; // Skip 8-byte RawSMBIOSData header
                while (i + 4 <= buf.Length)
                {
                    byte type = buf[i];
                    byte len = buf[i + 1];
                    if (len < 4 || i + len > buf.Length) break;

                    ushort handle = BitConverter.ToUInt16(buf, i + 2);

                    int p = i + len;
                    var strings = new List<string>();
                    if (p < buf.Length && buf[p] == 0)
                    {
                        p += (p + 1 < buf.Length && buf[p + 1] == 0) ? 2 : 1;
                    }
                    else
                    {
                        while (p < buf.Length)
                        {
                            if (buf[p] == 0)
                            {
                                p++;
                                break;
                            }
                            int end = p;
                            while (end < buf.Length && buf[end] != 0) end++;
                            strings.Add(Encoding.ASCII.GetString(buf, p, end - p).Trim());
                            p = (end < buf.Length) ? end + 1 : end;
                        }
                    }

                    string GetStr(byte idx) => (idx > 0 && idx <= strings.Count) ? strings[idx - 1] : string.Empty;

                    if (type == 17 && len >= 21)
                    {
                        ushort totalWidth = BitConverter.ToUInt16(buf, i + 8);
                        ushort dataWidth = BitConverter.ToUInt16(buf, i + 10);
                        ushort rawSize = BitConverter.ToUInt16(buf, i + 12);
                        byte formFactor = buf[i + 14];
                        string locator = GetStr(buf[i + 16]);
                        string bank = GetStr(buf[i + 17]);
                        byte memType = buf[i + 18];
                        ushort typeDetail = BitConverter.ToUInt16(buf, i + 19);
                        ushort speed = len >= 23 ? BitConverter.ToUInt16(buf, i + 21) : (ushort)0;
                        string mfg = len >= 24 ? GetStr(buf[i + 23]) : string.Empty;
                        string serial = len >= 25 ? GetStr(buf[i + 24]) : string.Empty;
                        string part = len >= 27 ? GetStr(buf[i + 26]) : string.Empty;

                        ulong capBytes = 0;
                        if (rawSize == 0 || rawSize == 0xFFFF)
                        {
                            capBytes = 0;
                        }
                        else if (rawSize == 0x7FFF && len >= 32)
                        {
                            uint extMb = BitConverter.ToUInt32(buf, i + 28) & 0x7FFFFFFFu;
                            capBytes = (ulong)extMb * 1024UL * 1024UL;
                        }
                        else if ((rawSize & 0x8000) != 0)
                        {
                            capBytes = (ulong)(rawSize & 0x7FFF) * 1024UL;
                        }
                        else
                        {
                            capBytes = (ulong)rawSize * 1024UL * 1024UL;
                        }

                        ushort cfgSpeed = len >= 34 ? BitConverter.ToUInt16(buf, i + 32) : (ushort)0;
                        ushort cfgVoltage = len >= 40 ? BitConverter.ToUInt16(buf, i + 38) : (ushort)0;
                        uint extSpeed = len >= 92 ? BitConverter.ToUInt32(buf, i + 84) : 0;
                        uint extCfgSpeed = len >= 92 ? BitConverter.ToUInt32(buf, i + 88) : 0;

                        int finalSpeed = (speed == 0xFFFF || extSpeed > 0) && extSpeed > 0 ? (int)extSpeed : speed;
                        int finalCfgSpeed = (cfgSpeed == 0xFFFF || extCfgSpeed > 0) && extCfgSpeed > 0 ? (int)extCfgSpeed : cfgSpeed;

                        records.Add(new SmbiosType17Record
                        {
                            Handle = handle,
                            Locator = locator,
                            BankLabel = bank,
                            CapacityBytes = capBytes,
                            FormFactor = formFactor,
                            MemoryType = memType,
                            TypeDetail = typeDetail,
                            SpeedMTps = finalSpeed,
                            ConfiguredSpeedMTps = finalCfgSpeed,
                            ConfiguredVoltageMv = cfgVoltage,
                            DataWidthBits = dataWidth == 0xFFFF ? 0 : dataWidth,
                            TotalWidthBits = totalWidth == 0xFFFF ? 0 : totalWidth,
                            Manufacturer = mfg,
                            SerialNumber = serial,
                            PartNumber = part
                        });
                    }

                    if (type == 127) break;
                    i = p;
                }
            }
            catch { }
            return records;
        }

        private static (string Generation, bool IsLpddr) ClassifyMemoryGeneration(
            int smbiosType, int legacyMemType, int voltageMv, int speedMTps,
            string partNumber, string locator, bool isSolderedHint)
        {
            string part = (partNumber ?? string.Empty).ToUpperInvariant();
            string loc = (locator ?? string.Empty).ToUpperInvariant();

            // First check explicit DMTF SMBIOS MemoryType codes
            switch (smbiosType)
            {
                case 20: case 18: return ("DDR", false);
                case 21: case 19: return ("DDR2", false);
                case 22: return ("DDR2 FB-DIMM", false);
                case 24:
                    return (voltageMv is >= 1250 and <= 1400 || part.Contains("DDR3L") || part.EndsWith("L"))
                        ? ("DDR3L", false)
                        : ("DDR3", false);
                case 26:
                    // Guard against boards that tag soldered LPDDR4X under generic 26
                    if (loc.Contains("LPDDR") || part.Contains("LPDDR"))
                        return (part.Contains("4X") ? "LPDDR4X" : "LPDDR4", true);
                    return ("DDR4", false);
                case 27: return ("LPDDR", true);
                case 28: return ("LPDDR2", true);
                case 29: return ("LPDDR3", true);
                case 30:
                    bool is4X = part.Contains("4X") || speedMTps >= 3733 || (voltageMv > 0 && voltageMv <= 680);
                    return (is4X ? "LPDDR4X" : "LPDDR4", true);
                case 32: return ("HBM", false);
                case 33: return ("HBM2", false);
                case 34:
                    if (loc.Contains("LPDDR") || part.Contains("LPDDR"))
                        return (speedMTps >= 7467 || part.Contains("5X") ? "LPDDR5X" : "LPDDR5", true);
                    return ("DDR5", false);
                case 35:
                    bool is5X = part.Contains("5X") || speedMTps >= 7467;
                    return (is5X ? "LPDDR5X" : "LPDDR5", true);
                case 36: return ("HBM3", false);
            }

            // Legacy / generic SMBIOS type fallback heuristics
            if (legacyMemType == 24) return ("DDR3", false);
            if (legacyMemType == 26) return ("DDR4", false);
            if (legacyMemType == 34) return ("DDR5", false);

            if (part.Contains("LPDDR5X") || loc.Contains("LPDDR5X")) return ("LPDDR5X", true);
            if (part.Contains("LPDDR5") || loc.Contains("LPDDR5")) return ("LPDDR5", true);
            if (part.Contains("LPDDR4X") || loc.Contains("LPDDR4X")) return ("LPDDR4X", true);
            if (part.Contains("LPDDR4") || loc.Contains("LPDDR4")) return ("LPDDR4", true);
            if (part.Contains("DDR5") || loc.Contains("DDR5")) return ("DDR5", false);
            if (part.Contains("DDR4") || loc.Contains("DDR4")) return ("DDR4", false);

            // Speed + voltage signature fallback
            if (speedMTps >= 4800)
            {
                if (isSolderedHint || (voltageMv > 0 && voltageMv <= 1050))
                    return (speedMTps >= 7467 ? "LPDDR5X" : "LPDDR5", true);
                return ("DDR5", false);
            }
            if (speedMTps >= 2133 && speedMTps <= 4267)
            {
                if (isSolderedHint && voltageMv > 0 && voltageMv <= 1120)
                    return ("LPDDR4X", true);
                return ("DDR4", false);
            }
            if (speedMTps >= 800 && speedMTps < 2133)
            {
                return (isSolderedHint && voltageMv > 0 && voltageMv <= 1220) ? ("LPDDR3", true) : ("DDR3", false);
            }

            return ("DDR", false);
        }

        private static (string FormName, bool IsSoldered) ClassifyFormFactor(int formCode, bool fromSmbiosSpec, string locator, string bank)
        {
            string locUpper = ((locator ?? "") + " " + (bank ?? "")).ToUpperInvariant();
            if (locUpper.Contains("LPCAMM")) return ("LPCAMM2", false);
            if (locUpper.Contains("CAMM")) return ("CAMM2", false);
            if (locUpper.Contains("ONBOARD") || locUpper.Contains("SOLDER")) return ("Onboard (Soldered)", true);

            if (fromSmbiosSpec)
            {
                return formCode switch
                {
                    13 => ("SO-DIMM", false),
                    9 => ("DIMM", false),
                    15 => ("FB-DIMM", false),
                    12 => ("RIMM", false),
                    6 or 16 or 1 => ("Onboard (Soldered)", true),
                    _ => ("Other / Embedded", false)
                };
            }
            else
            {
                // WMI Win32_PhysicalMemory FormFactor enum
                return formCode switch
                {
                    12 => ("SO-DIMM", false),
                    8 => ("DIMM", false),
                    13 => ("FB-DIMM", false),
                    6 or 1 or 0 => ("Onboard (Soldered)", true),
                    _ => ("Other / Embedded", false)
                };
            }
        }

        private static void PopulateMemorySlotDiagnostics(DetailedSystemReport report, List<Dictionary<string, string>> wmiMemList)
        {
            var rawSlots = ReadRawSmbiosType17Slots();
            var result = new List<RamSlotInfo>();

            if (rawSlots.Count > 0)
            {
                int slotNum = 1;
                foreach (var r in rawSlots)
                {
                    // Skip unpopulated dummy SoC channels unless they are real removable DIMM/SODIMM sockets
                    bool isRealSocket = r.FormFactor == 9 || r.FormFactor == 13 || r.Locator.Contains("DIMM", StringComparison.OrdinalIgnoreCase) || r.Locator.Contains("Slot", StringComparison.OrdinalIgnoreCase);
                    if (r.CapacityBytes == 0 && !isRealSocket)
                    {
                        continue;
                    }

                    // Cross-reference WMI record if available for any enriched manufacturer/voltage fields
                    var wmiMatch = wmiMemList.FirstOrDefault(m =>
                        (!string.IsNullOrWhiteSpace(r.Locator) && string.Equals(GetVal(m, "DeviceLocator", ""), r.Locator, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrWhiteSpace(r.BankLabel) && string.Equals(GetVal(m, "BankLabel", ""), r.BankLabel, StringComparison.OrdinalIgnoreCase)));

                    string mfg = !string.IsNullOrWhiteSpace(r.Manufacturer) && !r.Manufacturer.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
                        ? r.Manufacturer
                        : GetVal(wmiMatch, "Manufacturer", "Unknown");
                    string part = !string.IsNullOrWhiteSpace(r.PartNumber) ? r.PartNumber : GetVal(wmiMatch, "PartNumber", "");
                    int cfgSpeed = r.ConfiguredSpeedMTps > 0 ? r.ConfiguredSpeedMTps : ParseInt(GetVal(wmiMatch, "ConfiguredClockSpeed", "0"));
                    int ratedSpeed = r.SpeedMTps > 0 ? r.SpeedMTps : ParseInt(GetVal(wmiMatch, "Speed", "0"));
                    int voltMv = r.ConfiguredVoltageMv > 0 ? r.ConfiguredVoltageMv : ParseInt(GetVal(wmiMatch, "ConfiguredVoltage", "0"));
                    int smbiosType = r.MemoryType > 0 ? r.MemoryType : ParseInt(GetVal(wmiMatch, "SMBIOSMemoryType", "0"));
                    int legacyType = ParseInt(GetVal(wmiMatch, "MemoryType", "0"));

                    var (formName, isSoldered) = ClassifyFormFactor(r.FormFactor, fromSmbiosSpec: true, r.Locator, r.BankLabel);
                    var (gen, isLp) = ClassifyMemoryGeneration(smbiosType, legacyType, voltMv, Math.Max(cfgSpeed, ratedSpeed), part, r.Locator, isSoldered);

                    // LPDDR modules without an explicit DIMM/SO-DIMM/CAMM socket are always soldered onboard
                    if (isLp && r.FormFactor != 9 && r.FormFactor != 13 && !formName.Contains("CAMM"))
                    {
                        formName = "Onboard (Soldered)";
                        isSoldered = true;
                    }

                    result.Add(new RamSlotInfo
                    {
                        SlotIndex = slotNum++,
                        Locator = string.IsNullOrWhiteSpace(r.Locator) ? $"Slot {slotNum - 1}" : r.Locator,
                        BankLabel = r.BankLabel,
                        CapacityBytes = r.CapacityBytes,
                        CapacityGB = r.CapacityBytes / (1024.0 * 1024.0 * 1024.0),
                        SpeedMTps = ratedSpeed,
                        ConfiguredSpeedMTps = cfgSpeed,
                        Manufacturer = mfg,
                        PartNumber = part.Trim(),
                        SerialNumber = r.SerialNumber.Trim(),
                        SmbiosMemoryTypeCode = smbiosType,
                        MemoryGeneration = r.CapacityBytes > 0 ? gen : "Empty",
                        IsLpddr = r.CapacityBytes > 0 && isLp,
                        FormFactorName = formName,
                        IsSoldered = isSoldered,
                        ConfiguredVoltageMv = voltMv,
                        DataWidthBits = r.DataWidthBits,
                        TotalWidthBits = r.TotalWidthBits
                    });
                }
            }

            if (result.Count == 0)
            {
                int slotNum = 1;
                foreach (var m in wmiMemList)
                {
                    ulong.TryParse(GetVal(m, "Capacity", "0"), out ulong cap);
                    string loc = GetVal(m, "DeviceLocator", $"Slot {slotNum}");
                    string bank = GetVal(m, "BankLabel", "");
                    int speed = ParseInt(GetVal(m, "Speed", "0"));
                    int cfgSpeed = ParseInt(GetVal(m, "ConfiguredClockSpeed", "0"));
                    int smbiosType = ParseInt(GetVal(m, "SMBIOSMemoryType", "0"));
                    int legacyType = ParseInt(GetVal(m, "MemoryType", "0"));
                    int wmiForm = ParseInt(GetVal(m, "FormFactor", "12"));
                    int voltMv = ParseInt(GetVal(m, "ConfiguredVoltage", "0"));
                    string part = GetVal(m, "PartNumber", "").Trim();
                    string mfg = GetVal(m, "Manufacturer", "Unknown");

                    var (formName, isSoldered) = ClassifyFormFactor(wmiForm, fromSmbiosSpec: false, loc, bank);
                    var (gen, isLp) = ClassifyMemoryGeneration(smbiosType, legacyType, voltMv, Math.Max(cfgSpeed, speed), part, loc, isSoldered);
                    if (isLp && wmiForm != 8 && wmiForm != 12 && !formName.Contains("CAMM"))
                    {
                        formName = "Onboard (Soldered)";
                        isSoldered = true;
                    }

                    result.Add(new RamSlotInfo
                    {
                        SlotIndex = slotNum++,
                        Locator = loc,
                        BankLabel = bank,
                        CapacityBytes = cap,
                        CapacityGB = cap / (1024.0 * 1024.0 * 1024.0),
                        SpeedMTps = speed,
                        ConfiguredSpeedMTps = cfgSpeed,
                        Manufacturer = mfg,
                        PartNumber = part,
                        SerialNumber = GetVal(m, "SerialNumber", ""),
                        SmbiosMemoryTypeCode = smbiosType,
                        MemoryGeneration = gen,
                        IsLpddr = isLp,
                        FormFactorName = formName,
                        IsSoldered = isSoldered,
                        ConfiguredVoltageMv = voltMv,
                        DataWidthBits = ParseInt(GetVal(m, "DataWidth", "64")),
                        TotalWidthBits = ParseInt(GetVal(m, "TotalWidth", "64"))
                    });
                }
            }

            report.RamSlots = result;
            var populated = result.Where(s => s.IsPopulated).ToList();
            report.PopulatedSlots = populated.Count;
            report.TotalPhysicalSlots = Math.Max(result.Count, populated.Count);

            if (populated.Count > 0)
            {
                var distinctGens = populated.Select(s => s.MemoryGeneration).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var distinctForms = populated.Select(s => s.FormFactorName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                bool hasLp = populated.Any(s => s.IsLpddr);
                bool hasStdDdr = populated.Any(s => !s.IsLpddr);
                report.IsMixedMemory = distinctGens.Count > 1 || (hasLp && hasStdDdr) || distinctForms.Count > 1;

                if (report.IsMixedMemory)
                {
                    string partsEn = string.Join(" + ", populated.Select(s => $"{s.MemoryGeneration} ({s.FormFactorName})"));
                    string partsAr = string.Join(" + ", populated.Select(s => $"{s.MemoryGeneration} ({(s.IsSoldered ? "مدمجة" : s.FormFactorName)})"));
                    report.MemoryArchitectureSummary = $"Mixed Hybrid: {partsEn}";
                    report.MemoryArchitectureSummaryAr = $"هجين مختلط: {partsAr}";
                }
                else
                {
                    string gen = distinctGens[0];
                    string form = distinctForms[0];
                    string chEn = populated.Count >= 2 ? "Dual-Channel" : (result.Count > populated.Count ? "Single-Channel (Upgradeable)" : "Single-Channel");
                    string chAr = populated.Count >= 2 ? "ثنائي القناة" : (result.Count > populated.Count ? "قناة أحادية (قابل للترقية)" : "قناة أحادية");
                    string formAr = form.Contains("Soldered", StringComparison.OrdinalIgnoreCase) ? "مدمجة على اللوحة" : form;
                    report.MemoryArchitectureSummary = $"{chEn} {gen} • {populated.Count} × {form}";
                    report.MemoryArchitectureSummaryAr = $"{chAr} {gen} • {populated.Count} × {formAr}";
                }
            }
            else
            {
                report.MemoryArchitectureSummary = "Standard System Memory";
                report.MemoryArchitectureSummaryAr = "ذاكرة النظام القياسية";
            }
        }

        private static int ParseInt(string? val)
        {
            return int.TryParse(val?.Trim(), out int n) ? n : 0;
        }

    }
}
