using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace CleanRamBoosterApp.Services
{
    public class ServiceInfo
    {
        public string ServiceName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public int StartType { get; set; } // 2=Auto, 3=Manual, 4=Disabled
        public bool IsRunning { get; set; }
    }

    public static class WinServiceManager
    {
        private const uint SC_MANAGER_CONNECT = 0x0001;
        private const uint SERVICE_QUERY_CONFIG = 0x0001;
        private const uint SERVICE_CHANGE_CONFIG = 0x0002;
        private const uint SERVICE_QUERY_STATUS = 0x0004;
        private const uint SERVICE_START = 0x0010;
        private const uint SERVICE_STOP = 0x0020;

        private const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
        private const uint SERVICE_AUTO_START = 0x00000002;
        private const uint SERVICE_DEMAND_START = 0x00000003;

        private const int SC_STATUS_PROCESS_INFO = 0;
        private const uint SERVICE_WIN32_OWN_PROCESS = 0x00000010;
        private const uint SERVICE_CONTROL_STOP = 0x00000001;
        private const uint SERVICE_STOPPED = 0x00000001;
        private const uint SERVICE_START_PENDING = 0x00000002;
        private const uint SERVICE_STOP_PENDING = 0x00000003;
        private const uint SERVICE_RUNNING = 0x00000004;
        private const uint SERVICE_PAUSED = 0x00000007;
        private const uint SERVICE_ACCEPT_STOP = 0x00000001;
        public const int ERROR_ACCESS_DENIED = 5;
        public const int ERROR_INVALID_PARAMETER = 87;
        private const int ERROR_SERVICE_ALREADY_RUNNING = 1056;
        public const int ERROR_SERVICE_DOES_NOT_EXIST = 1060;
        public const int ERROR_SERVICE_MARKED_FOR_DELETE = 1072;

        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_STATUS
        {
            public uint dwServiceType;
            public uint dwCurrentState;
            public uint dwControlsAccepted;
            public uint dwWin32ExitCode;
            public uint dwServiceSpecificExitCode;
            public uint dwCheckPoint;
            public uint dwWaitHint;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_STATUS_PROCESS
        {
            public uint dwServiceType;
            public uint dwCurrentState;
            public uint dwControlsAccepted;
            public uint dwWin32ExitCode;
            public uint dwServiceSpecificExitCode;
            public uint dwCheckPoint;
            public uint dwWaitHint;
            public uint dwProcessId;
            public uint dwServiceFlags;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenSCManagerW(string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenServiceW(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryServiceStatus(IntPtr hService, out SERVICE_STATUS lpServiceStatus);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryServiceStatusEx(IntPtr hService, int InfoLevel, out SERVICE_STATUS_PROCESS lpBuffer, uint cbBufSize, out uint pcbBytesNeeded);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ControlService(IntPtr hService, uint dwControl, out SERVICE_STATUS lpServiceStatus);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool StartServiceW(IntPtr hService, uint dwNumServiceArgs, IntPtr lpServiceArgVectors);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ChangeServiceConfigW(
            IntPtr hService,
            uint dwServiceType,
            uint dwStartType,
            uint dwErrorControl,
            string? lpBinaryPathName,
            string? lpLoadOrderGroup,
            IntPtr lpdwTagId,
            string? lpDependencies,
            string? lpServiceStartName,
            string? lpPassword,
            string? lpDisplayName);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseServiceHandle(IntPtr hSCObject);

        public static List<ServiceInfo> GetServices()
        {
            var list = new List<ServiceInfo>();
            try
            {
                using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
                if (root == null) return list;

                foreach (var name in root.GetSubKeyNames())
                {
                    try
                    {
                        using var sKey = root.OpenSubKey(name);
                        if (sKey == null) continue;

                        int type = Convert.ToInt32(sKey.GetValue("Type", 0));
                        // 0x10 = Win32OwnProcess, 0x20 = Win32ShareProcess
                        if ((type & 0x30) == 0) continue;

                        string rawDisplay = sKey.GetValue("DisplayName")?.ToString() ?? name;
                        string display = MuiHelper.Resolve(rawDisplay, name);

                        string rawDesc = sKey.GetValue("Description")?.ToString() ?? string.Empty;
                        string desc = MuiHelper.Resolve(rawDesc, "");

                        string imagePath = sKey.GetValue("ImagePath")?.ToString() ?? string.Empty;
                        int start = Convert.ToInt32(sKey.GetValue("Start", 3));

                        list.Add(new ServiceInfo
                        {
                            ServiceName = name,
                            DisplayName = display,
                            Description = desc,
                            ImagePath = imagePath,
                            StartType = start,
                            IsRunning = false // Updated on demand
                        });
                    }
                    catch { }
                }
            }
            catch { }
            return list;
        }

        public static HashSet<string> GetRunningStoppableServiceNames(IEnumerable<string> serviceNames)
            => new(GetRunningStoppableServices(serviceNames).Keys, StringComparer.OrdinalIgnoreCase);

        public static Dictionary<string, (int ProcessId, bool IsOwnProcess)> GetRunningStoppableServices(IEnumerable<string> serviceNames)
        {
            var running = new Dictionary<string, (int ProcessId, bool IsOwnProcess)>(StringComparer.OrdinalIgnoreCase);
            IntPtr hScm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
            if (hScm == IntPtr.Zero) return running;

            bool checkStopRights = AdminPrivilegeService.IsRunningAsAdmin();
            uint desiredAccess = checkStopRights ? (SERVICE_QUERY_STATUS | SERVICE_STOP) : SERVICE_QUERY_STATUS;
            uint bufSize = (uint)Marshal.SizeOf<SERVICE_STATUS_PROCESS>();
            try
            {
                foreach (var serviceName in serviceNames)
                {
                    if (string.IsNullOrWhiteSpace(serviceName)) continue;
                    IntPtr hSvc = OpenServiceW(hScm, serviceName, desiredAccess);
                    if (hSvc == IntPtr.Zero) continue;
                    try
                    {
                        if (QueryServiceStatusEx(hSvc, SC_STATUS_PROCESS_INFO, out var st, bufSize, out _) &&
                            st.dwCurrentState == SERVICE_RUNNING &&
                            (st.dwControlsAccepted & SERVICE_ACCEPT_STOP) != 0)
                        {
                            bool isOwn = (st.dwServiceType & SERVICE_WIN32_OWN_PROCESS) != 0;
                            running[serviceName] = ((int)st.dwProcessId, isOwn);
                        }
                    }
                    finally
                    {
                        CloseServiceHandle(hSvc);
                    }
                }
            }
            catch { }
            finally
            {
                CloseServiceHandle(hScm);
            }
            return running;
        }

        public static bool TryGetServiceProcessInfo(string serviceName, out int processId, out bool isOwnProcess)
        {
            processId = 0;
            isOwnProcess = false;
            if (string.IsNullOrWhiteSpace(serviceName)) return false;

            IntPtr hScm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
            if (hScm == IntPtr.Zero) return false;
            try
            {
                IntPtr hSvc = OpenServiceW(hScm, serviceName, SERVICE_QUERY_STATUS);
                if (hSvc == IntPtr.Zero) return false;
                try
                {
                    uint bufSize = (uint)Marshal.SizeOf<SERVICE_STATUS_PROCESS>();
                    if (QueryServiceStatusEx(hSvc, SC_STATUS_PROCESS_INFO, out var st, bufSize, out _) &&
                        st.dwCurrentState == SERVICE_RUNNING)
                    {
                        processId = (int)st.dwProcessId;
                        isOwnProcess = (st.dwServiceType & SERVICE_WIN32_OWN_PROCESS) != 0;
                        return true;
                    }
                    return false;
                }
                finally
                {
                    CloseServiceHandle(hSvc);
                }
            }
            catch
            {
                return false;
            }
            finally
            {
                CloseServiceHandle(hScm);
            }
        }

        public static bool IsServiceRunning(string serviceName)
        {
            if (string.IsNullOrWhiteSpace(serviceName)) return false;
            IntPtr hScm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
            if (hScm == IntPtr.Zero) return false;
            try
            {
                IntPtr hSvc = OpenServiceW(hScm, serviceName, SERVICE_QUERY_STATUS);
                if (hSvc == IntPtr.Zero) return false;
                try
                {
                    return QueryServiceStatus(hSvc, out var st) && st.dwCurrentState == SERVICE_RUNNING;
                }
                finally
                {
                    CloseServiceHandle(hSvc);
                }
            }
            catch
            {
                return false;
            }
            finally
            {
                CloseServiceHandle(hScm);
            }
        }

        public static bool SetServiceStartType(string serviceName, bool autoStart, out int win32Error)
        {
            win32Error = 0;
            if (string.IsNullOrWhiteSpace(serviceName))
            {
                win32Error = ERROR_INVALID_PARAMETER;
                return false;
            }

            if (TryChangeServiceStartTypeDirect(serviceName, autoStart, out win32Error))
                return true;

            // Windows 10/11 per-user service instances (e.g. CDPUserSvc_3a1f2) inherit startup type from
            // their base template service (CDPUserSvc). When changing the instance fails with Access Denied
            // or Invalid Parameter under Admin, update the template service so next boot applies cleanly.
            if (AdminPrivilegeService.IsRunningAsAdmin() &&
                (win32Error == ERROR_ACCESS_DENIED || win32Error == ERROR_INVALID_PARAMETER) &&
                TryGetPerUserBaseTemplate(serviceName, out string baseTemplate))
            {
                if (TryChangeServiceStartTypeDirect(baseTemplate, autoStart, out int baseErr))
                {
                    win32Error = 0;
                    return true;
                }
                if (baseErr != 0) win32Error = baseErr;
            }

            return false;
        }

        private static bool TryGetPerUserBaseTemplate(string serviceName, out string baseTemplate)
        {
            baseTemplate = string.Empty;
            int idx = serviceName.LastIndexOf('_');
            if (idx <= 0 || idx >= serviceName.Length - 1) return false;

            string suffix = serviceName.Substring(idx + 1);
            if (suffix.Length < 4 || suffix.Length > 16) return false;
            foreach (char c in suffix)
            {
                if (!Uri.IsHexDigit(c)) return false;
            }

            string candidate = serviceName.Substring(0, idx);
            try
            {
                using var baseKey = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{candidate}", false);
                if (baseKey == null) return false;
                int type = Convert.ToInt32(baseKey.GetValue("Type", 0));
                bool hasUserFlags = baseKey.GetValue("UserServiceFlags") != null;
                if (hasUserFlags || (type & 0x40) != 0)
                {
                    baseTemplate = candidate;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static bool TryChangeServiceStartTypeDirect(string serviceName, bool autoStart, out int win32Error)
        {
            win32Error = 0;
            IntPtr hScm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
            if (hScm == IntPtr.Zero)
            {
                win32Error = Marshal.GetLastWin32Error();
                return false;
            }

            try
            {
                IntPtr hSvc = OpenServiceW(hScm, serviceName, SERVICE_CHANGE_CONFIG | SERVICE_QUERY_CONFIG);
                if (hSvc == IntPtr.Zero)
                {
                    win32Error = Marshal.GetLastWin32Error();
                    return false;
                }
                try
                {
                    uint startType = autoStart ? SERVICE_AUTO_START : SERVICE_DEMAND_START;
                    if (!ChangeServiceConfigW(
                        hSvc,
                        SERVICE_NO_CHANGE,
                        startType,
                        SERVICE_NO_CHANGE,
                        null,
                        null,
                        IntPtr.Zero,
                        null,
                        null,
                        null,
                        null))
                    {
                        win32Error = Marshal.GetLastWin32Error();
                        return false;
                    }
                    return true;
                }
                finally
                {
                    CloseServiceHandle(hSvc);
                }
            }
            catch
            {
                win32Error = Marshal.GetLastWin32Error();
                return false;
            }
            finally
            {
                CloseServiceHandle(hScm);
            }
        }

        public static bool StopService(string serviceName, CancellationToken ct = default)
            => StopService(serviceName, ct, out _);

        public static bool StopService(string serviceName, CancellationToken ct, out int win32Error)
        {
            win32Error = 0;
            if (string.IsNullOrWhiteSpace(serviceName) || ct.IsCancellationRequested) return false;
            IntPtr hScm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
            if (hScm == IntPtr.Zero)
            {
                win32Error = Marshal.GetLastWin32Error();
                return false;
            }

            try
            {
                IntPtr hSvc = OpenServiceW(hScm, serviceName, SERVICE_QUERY_STATUS | SERVICE_STOP);
                if (hSvc == IntPtr.Zero)
                {
                    win32Error = Marshal.GetLastWin32Error();
                    return false;
                }
                try
                {
                    if (!QueryServiceStatus(hSvc, out var st))
                    {
                        win32Error = Marshal.GetLastWin32Error();
                        return false;
                    }
                    if (st.dwCurrentState != SERVICE_RUNNING && st.dwCurrentState != SERVICE_PAUSED)
                        return false;
                    if ((st.dwControlsAccepted & SERVICE_ACCEPT_STOP) == 0)
                    {
                        win32Error = ERROR_ACCESS_DENIED;
                        return false;
                    }

                    if (!ControlService(hSvc, SERVICE_CONTROL_STOP, out st))
                    {
                        win32Error = Marshal.GetLastWin32Error();
                        return false;
                    }

                    int elapsedMs = 0;
                    while (st.dwCurrentState == SERVICE_STOP_PENDING && elapsedMs < 1200 && !ct.IsCancellationRequested)
                    {
                        Thread.Sleep(60);
                        elapsedMs += 60;
                        if (!QueryServiceStatus(hSvc, out st)) break;
                    }
                    return true;
                }
                finally
                {
                    CloseServiceHandle(hSvc);
                }
            }
            catch
            {
                win32Error = Marshal.GetLastWin32Error();
                return false;
            }
            finally
            {
                CloseServiceHandle(hScm);
            }
        }

        public static bool StartService(string serviceName, CancellationToken ct = default)
            => StartService(serviceName, ct, out _);

        public static bool StartService(string serviceName, CancellationToken ct, out int win32Error)
        {
            win32Error = 0;
            if (string.IsNullOrWhiteSpace(serviceName) || ct.IsCancellationRequested) return false;
            IntPtr hScm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
            if (hScm == IntPtr.Zero)
            {
                win32Error = Marshal.GetLastWin32Error();
                return false;
            }

            try
            {
                IntPtr hSvc = OpenServiceW(hScm, serviceName, SERVICE_QUERY_STATUS | SERVICE_START);
                if (hSvc == IntPtr.Zero)
                {
                    win32Error = Marshal.GetLastWin32Error();
                    return false;
                }
                try
                {
                    if (QueryServiceStatus(hSvc, out var st) && st.dwCurrentState == SERVICE_RUNNING)
                        return true;

                    if (!StartServiceW(hSvc, 0, IntPtr.Zero))
                    {
                        int err = Marshal.GetLastWin32Error();
                        if (err != ERROR_SERVICE_ALREADY_RUNNING)
                        {
                            win32Error = err;
                            return false;
                        }
                    }

                    int elapsedMs = 0;
                    while (!ct.IsCancellationRequested && elapsedMs < 1200 && QueryServiceStatus(hSvc, out st))
                    {
                        if (st.dwCurrentState != SERVICE_START_PENDING) break;
                        Thread.Sleep(60);
                        elapsedMs += 60;
                    }
                    return true;
                }
                finally
                {
                    CloseServiceHandle(hSvc);
                }
            }
            catch
            {
                win32Error = Marshal.GetLastWin32Error();
                return false;
            }
            finally
            {
                CloseServiceHandle(hScm);
            }
        }
    }
}
