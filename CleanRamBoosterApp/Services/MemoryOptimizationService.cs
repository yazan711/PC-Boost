using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace CleanRamBoosterApp.Services
{
    public class DetailedMemoryInfo
    {
        public int LoadPercentage { get; set; }
        public ulong TotalPhysBytes { get; set; }
        public ulong OsVisiblePhysBytes { get; set; }
        public ulong AvailPhysBytes { get; set; }
        public ulong UsedPhysBytes { get; set; }
        public ulong StandbyCacheBytes { get; set; }
        public ulong FreePhysBytes { get; set; }
    }

    public class MemoryOptimizationService
    {
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

        [StructLayout(LayoutKind.Sequential)]
        private struct PERFORMANCE_INFORMATION
        {
            public uint cb;
            public UIntPtr CommitTotal;
            public UIntPtr CommitLimit;
            public UIntPtr CommitPeak;
            public UIntPtr PhysicalTotal;
            public UIntPtr PhysicalAvailable;
            public UIntPtr SystemCache;
            public UIntPtr KernelTotal;
            public UIntPtr KernelPaged;
            public UIntPtr KernelNonpaged;
            public UIntPtr PageSize;
            public uint HandleCount;
            public uint ProcessCount;
            public uint ThreadCount;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetPhysicallyInstalledSystemMemory(out ulong TotalMemoryInKilobytes);

        private static ulong _cachedInstalledPhysBytes = 0;

        public static ulong GetInstalledPhysicalMemoryBytes()
        {
            if (_cachedInstalledPhysBytes > 0) return _cachedInstalledPhysBytes;
            try
            {
                if (GetPhysicallyInstalledSystemMemory(out ulong kb) && kb > 0)
                {
                    _cachedInstalledPhysBytes = kb * 1024UL;
                    return _cachedInstalledPhysBytes;
                }
            }
            catch { }
            return 0;
        }

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetPerformanceInfo(out PERFORMANCE_INFORMATION pPerformanceInformation, uint cb);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("ntdll.dll")]
        private static extern int NtSetSystemInformation(int SystemInformationClass, IntPtr SystemInformation, int SystemInformationLength);

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
        private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, [MarshalAs(UnmanagedType.Bool)] bool DisableAllPrivileges,
            ref TOKEN_PRIVILEGES NewState, int BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        private const uint PROCESS_SET_QUOTA = 0x0100;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const int SE_PRIVILEGE_ENABLED = 0x00000002;
        private const int ERROR_ACCESS_DENIED = 5;

        private const int SystemMemoryListInformation = 80;
        private const int MemoryPurgeStandbyList = 4;
        private const int MemoryEmptyWorkingSets = 2;

        public MemoryOptimizationService()
        {
            EnablePrivilege("SeDebugPrivilege");
            EnablePrivilege("SeProfileSingleProcessPrivilege");
            EnablePrivilege("SeIncreaseQuotaPrivilege");
        }

        public DetailedMemoryInfo GetMemoryInfo()
        {
            var info = new DetailedMemoryInfo();
            MEMORYSTATUSEX memStatus = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(memStatus))
            {
                ulong installed = GetInstalledPhysicalMemoryBytes();
                ulong totalPhys = installed > memStatus.ullTotalPhys ? installed : memStatus.ullTotalPhys;

                info.OsVisiblePhysBytes = memStatus.ullTotalPhys;
                info.TotalPhysBytes = totalPhys;
                info.AvailPhysBytes = memStatus.ullAvailPhys;
                info.UsedPhysBytes = totalPhys > memStatus.ullAvailPhys ? totalPhys - memStatus.ullAvailPhys : 0;
                info.LoadPercentage = totalPhys > 0
                    ? (int)Math.Clamp(Math.Round(info.UsedPhysBytes * 100.0 / totalPhys), 0, 100)
                    : (int)memStatus.dwMemoryLoad;

                PERFORMANCE_INFORMATION perf = new PERFORMANCE_INFORMATION();
                perf.cb = (uint)Marshal.SizeOf(typeof(PERFORMANCE_INFORMATION));
                if (GetPerformanceInfo(out perf, perf.cb))
                {
                    ulong pageSize = (ulong)perf.PageSize.ToUInt64();
                    ulong cacheBytes = (ulong)perf.SystemCache.ToUInt64() * pageSize;
                    info.StandbyCacheBytes = cacheBytes;

                    if (info.AvailPhysBytes > cacheBytes)
                        info.FreePhysBytes = info.AvailPhysBytes - cacheBytes;
                    else
                        info.FreePhysBytes = info.AvailPhysBytes / 2;
                }
                else
                {
                    info.StandbyCacheBytes = info.AvailPhysBytes / 2;
                    info.FreePhysBytes = info.AvailPhysBytes / 2;
                }
            }
            return info;
        }

        private readonly SemaphoreSlim boostGate = new(1, 1);

        public async Task<(int scanned, int cleaned, int accessDenied, long freedBytes)> BoostAndOptimizeAsync()
        {
            return await Task.Run(async () =>
            {
                await boostGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    var (scanned, cleaned, accessDenied, freed1) = TrimWorkingSets();
                    PurgeStandbyMemory(out long freed2);
                    return (scanned, cleaned, accessDenied, freed1 + freed2);
                }
                finally
                {
                    boostGate.Release();
                }
            }).ConfigureAwait(false);
        }

        public async Task<(bool success, long freedBytes)> PurgeStandbyMemoryAsync()
        {
            return await Task.Run(async () =>
            {
                await boostGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    bool ok = PurgeStandbyMemory(out long freed);
                    return (ok, freed);
                }
                finally
                {
                    boostGate.Release();
                }
            }).ConfigureAwait(false);
        }

        public (int scanned, int cleaned, int accessDenied, long freedBytes) TrimWorkingSets()
        {
            int scanned = 0;
            int cleaned = 0;
            int accessDenied = 0;
            int selfPid = Environment.ProcessId;

            MEMORYSTATUSEX before = new MEMORYSTATUSEX();
            GlobalMemoryStatusEx(before);

            foreach (var proc in Process.GetProcesses())
            {
                using (proc)
                {
                    scanned++;
                    if (IsProtected(proc, selfPid)) continue;

                    IntPtr hProc = OpenProcess(PROCESS_SET_QUOTA | PROCESS_QUERY_INFORMATION, false, proc.Id);
                    if (hProc == IntPtr.Zero)
                    {
                        if (Marshal.GetLastWin32Error() == ERROR_ACCESS_DENIED) accessDenied++;
                        continue;
                    }

                    try
                    {
                        if (SetProcessWorkingSetSize(hProc, new IntPtr(-1), new IntPtr(-1))) cleaned++;
                    }
                    finally
                    {
                        CloseHandle(hProc);
                    }
                }
            }

            try { SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, new IntPtr(-1), new IntPtr(-1)); } catch { }

            MEMORYSTATUSEX after = new MEMORYSTATUSEX();
            GlobalMemoryStatusEx(after);

            long freedBytes = after.ullAvailPhys > before.ullAvailPhys ? (long)(after.ullAvailPhys - before.ullAvailPhys) : 0;
            return (scanned, cleaned, accessDenied, freedBytes);
        }

        public bool PurgeStandbyMemory(out long freedBytes)
        {
            freedBytes = 0;
            MEMORYSTATUSEX before = new MEMORYSTATUSEX();
            GlobalMemoryStatusEx(before);

            try
            {
                int command = MemoryPurgeStandbyList;
                GCHandle handle = GCHandle.Alloc(command, GCHandleType.Pinned);
                try
                {
                    int res = NtSetSystemInformation(SystemMemoryListInformation, handle.AddrOfPinnedObject(), sizeof(int));
                    MEMORYSTATUSEX after = new MEMORYSTATUSEX();
                    GlobalMemoryStatusEx(after);
                    if (after.ullAvailPhys > before.ullAvailPhys)
                        freedBytes = (long)(after.ullAvailPhys - before.ullAvailPhys);
                    return res >= 0;
                }
                finally
                {
                    handle.Free();
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool IsProtected(Process p, int selfPid)
        {
            try
            {
                if (p.Id == 0 || p.Id == 4 || p.Id == selfPid) return true;
                string name = p.ProcessName.ToLowerInvariant();
                return name is "system" or "smss" or "csrss" or "wininit" or "services" or "lsass" or "winlogon"
                    or "registry" or "memcompression" or "msmpeng" or "mpdefendercoreservice" or "nissrv"
                    or "securityhealthservice" or "lsaiso" or "ngciso" or "audiodg";
            }
            catch { return true; }
        }

        public static bool EnablePrivilege(string privilegeName)
        {
            try
            {
                if (OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr hToken))
                {
                    if (LookupPrivilegeValue(null, privilegeName, out long luid))
                    {
                        TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES
                        {
                            PrivilegeCount = 1,
                            Luid = luid,
                            Attributes = SE_PRIVILEGE_ENABLED
                        };
                        bool res = AdjustTokenPrivileges(hToken, false, ref tp, Marshal.SizeOf(tp), IntPtr.Zero, IntPtr.Zero);
                        CloseHandle(hToken);
                        return res;
                    }
                    CloseHandle(hToken);
                }
            }
            catch { }
            return false;
        }
    }
}
