using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace CleanRamBoosterApp.Services
{
    public partial class HardwareInfoService
    {
        // Native DXGI (dxgi.dll) 64-bit GPU Dedicated/Shared VRAM & LUID detection (no 4GB 32-bit WMI overflow)
        [DllImport("dxgi.dll", ExactSpelling = true)]
        private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr ppFactory);

        private static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
        private const uint DXGI_ADAPTER_FLAG_SOFTWARE = 2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private unsafe struct DXGI_ADAPTER_DESC1
        {
            public fixed char Description[128];
            public uint VendorId;
            public uint DeviceId;
            public uint SubSysId;
            public uint Revision;
            public nuint DedicatedVideoMemory;
            public nuint DedicatedSystemMemory;
            public nuint SharedSystemMemory;
            public uint AdapterLuidLow;
            public int AdapterLuidHigh;
            public uint Flags;
        }

        public sealed class GpuAdapterSnapshot
        {
            public string AdapterName { get; init; } = string.Empty;
            public uint VendorId { get; init; }
            public bool IsIntegrated { get; init; }
            public string LuidKey { get; init; } = string.Empty;
            public ulong DedicatedTotalBytes { get; init; }
            public ulong DedicatedUsedBytes { get; set; }
            public ulong SharedTotalBytes { get; init; }
            public ulong SharedUsedBytes { get; set; }
        }

        public readonly struct GpuVramSnapshot
        {
            public string AdapterName { get; init; }
            public ulong DedicatedTotalBytes { get; init; }
            public ulong DedicatedUsedBytes { get; init; }
            public ulong SharedTotalBytes { get; init; }
            public ulong SharedUsedBytes { get; init; }
        }

        public static bool ClassifyIsIntegratedGpu(string name, uint vendorId, ulong dedicatedBytes)
        {
            string n = (name ?? string.Empty).Trim();

            // NVIDIA GPUs are discrete (Dedicated GPU)
            if (vendorId == 0x10DE || n.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || n.Contains("GeForce", StringComparison.OrdinalIgnoreCase) || n.Contains("Quadro", StringComparison.OrdinalIgnoreCase))
                return false;

            // Intel: discrete Arc A/B-series (e.g. A380/A770/B580) vs integrated UHD/HD/Iris/Arc Graphics
            if (vendorId == 0x8086 || n.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            {
                bool isDiscreteArc = n.Contains("Arc", StringComparison.OrdinalIgnoreCase) &&
                    (System.Text.RegularExpressions.Regex.IsMatch(n, @"\b[AB]\d{3}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
                     dedicatedBytes >= 3UL * 1024 * 1024 * 1024);
                return !isDiscreteArc;
            }

            // AMD: discrete Radeon RX / Pro / FirePro / Radeon VII vs integrated Radeon (TM) Graphics / Vega / 680M / 780M / 890M
            if (vendorId == 0x1002 || n.Contains("AMD", StringComparison.OrdinalIgnoreCase) || n.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
            {
                bool isExplicitDiscrete =
                    n.Contains("RX ", StringComparison.OrdinalIgnoreCase) ||
                    n.Contains("Radeon Pro", StringComparison.OrdinalIgnoreCase) ||
                    n.Contains("FirePro", StringComparison.OrdinalIgnoreCase) ||
                    n.Contains("Radeon VII", StringComparison.OrdinalIgnoreCase) ||
                    n.Contains("R9 ", StringComparison.OrdinalIgnoreCase) ||
                    n.Contains("R7 ", StringComparison.OrdinalIgnoreCase);

                bool isExplicitIntegrated =
                    n.Equals("AMD Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("AMD Radeon (TM) Graphics", StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("AMD Radeon Graphics", StringComparison.OrdinalIgnoreCase) ||
                    n.Contains("Vega ", StringComparison.OrdinalIgnoreCase) ||
                    System.Text.RegularExpressions.Regex.IsMatch(n, @"\b[678]\d0M\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (isExplicitDiscrete) return false;
                if (isExplicitIntegrated) return true;
                return dedicatedBytes <= 2UL * 1024 * 1024 * 1024;
            }

            // Fallback heuristic for any other GPU vendor
            return dedicatedBytes <= 1UL * 1024 * 1024 * 1024;
        }

        public static List<GpuAdapterSnapshot> GetAllLiveGpuAdapters()
        {
            var adapters = new List<GpuAdapterSnapshot>();
            var seenLuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Enumerate all hardware GPUs via DXGI (64-bit Dedicated & Shared VRAM + exact LUID)
            try
            {
                unsafe
                {
                    Guid iid = IID_IDXGIFactory1;
                    if (CreateDXGIFactory1(ref iid, out IntPtr pFactory) == 0 && pFactory != IntPtr.Zero)
                    {
                        try
                        {
                            void** facVtbl = *(void***)pFactory;
                            var enumAdapters1 = (delegate* unmanaged[Stdcall]<IntPtr, uint, out IntPtr, int>)facVtbl[12];

                            for (uint idx = 0; idx < 16; idx++)
                            {
                                if (enumAdapters1(pFactory, idx, out IntPtr pAdapter) != 0 || pAdapter == IntPtr.Zero)
                                    break;

                                try
                                {
                                    void** adpVtbl = *(void***)pAdapter;
                                    var getDesc1 = (delegate* unmanaged[Stdcall]<IntPtr, DXGI_ADAPTER_DESC1*, int>)adpVtbl[10];
                                    DXGI_ADAPTER_DESC1 desc = default;
                                    if (getDesc1(pAdapter, &desc) == 0)
                                    {
                                        if ((desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0)
                                            continue;

                                        string name = new string(desc.Description).TrimEnd('\0').Trim();
                                        if (string.IsNullOrWhiteSpace(name) ||
                                            name.Contains("Microsoft Basic Render", StringComparison.OrdinalIgnoreCase))
                                            continue;

                                        string luidKey = $"luid_0x{(uint)desc.AdapterLuidHigh:X8}_0x{desc.AdapterLuidLow:X8}";
                                        if (!seenLuids.Add(luidKey)) continue;

                                        ulong ded = (ulong)desc.DedicatedVideoMemory;
                                        ulong shd = (ulong)desc.SharedSystemMemory;
                                        bool isIgpu = ClassifyIsIntegratedGpu(name, desc.VendorId, ded);

                                        adapters.Add(new GpuAdapterSnapshot
                                        {
                                            AdapterName = name,
                                            VendorId = desc.VendorId,
                                            IsIntegrated = isIgpu,
                                            LuidKey = luidKey,
                                            DedicatedTotalBytes = ded,
                                            SharedTotalBytes = shd
                                        });
                                    }
                                }
                                finally
                                {
                                    Marshal.Release(pAdapter);
                                }
                            }
                        }
                        finally
                        {
                            Marshal.Release(pFactory);
                        }
                    }
                }
            }
            catch { }

            // 2. Attach real system-wide live VRAM usage per adapter LUID from GPUAdapterMemory counter
            try
            {
                var gpuMemPerf = NativeWmi.Query("SELECT Name, DedicatedUsage, SharedUsage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory");
                foreach (var adp in adapters)
                {
                    if (string.IsNullOrEmpty(adp.LuidKey)) continue;
                    foreach (var row in gpuMemPerf)
                    {
                        string inst = GetVal(row, "Name", "");
                        if (inst.StartsWith(adp.LuidKey, StringComparison.OrdinalIgnoreCase))
                        {
                            ulong.TryParse(GetVal(row, "DedicatedUsage", "0"), out ulong d);
                            ulong.TryParse(GetVal(row, "SharedUsage", "0"), out ulong s);
                            adp.DedicatedUsedBytes = d;
                            adp.SharedUsedBytes = s;
                            break;
                        }
                    }
                }

                // Fallback match if LUID prefix did not match directly on single-adapter systems
                if (adapters.Count == 1 && adapters[0].DedicatedUsedBytes == 0 && adapters[0].SharedUsedBytes == 0)
                {
                    ulong maxD = 0, maxS = 0;
                    foreach (var row in gpuMemPerf)
                    {
                        ulong.TryParse(GetVal(row, "DedicatedUsage", "0"), out ulong d);
                        ulong.TryParse(GetVal(row, "SharedUsage", "0"), out ulong s);
                        if (d > maxD || (maxD == 0 && s > maxS))
                        {
                            maxD = d;
                            maxS = s;
                        }
                    }
                    adapters[0].DedicatedUsedBytes = maxD;
                    adapters[0].SharedUsedBytes = maxS;
                }
            }
            catch { }

            // Order Dedicated GPUs first (largest VRAM first), then Integrated GPUs
            return adapters
                .OrderBy(a => a.IsIntegrated ? 1 : 0)
                .ThenByDescending(a => a.DedicatedTotalBytes)
                .ToList();
        }

        public static GpuVramSnapshot GetLiveGpuVramInfo()
        {
            var all = GetAllLiveGpuAdapters();
            var primary = all.FirstOrDefault();
            if (primary == null) return default;

            return new GpuVramSnapshot
            {
                AdapterName = primary.AdapterName,
                DedicatedTotalBytes = primary.DedicatedTotalBytes,
                DedicatedUsedBytes = primary.DedicatedUsedBytes,
                SharedTotalBytes = primary.SharedTotalBytes,
                SharedUsedBytes = primary.SharedUsedBytes
            };
        }

        public static string FormatVramCompact(ulong bytes)
        {
            if (bytes >= 1024UL * 1024 * 1024)
            {
                double gb = bytes / (1024.0 * 1024.0 * 1024.0);
                return Math.Abs(gb - Math.Round(gb)) < 0.05 ? $"{Math.Round(gb):0} GB" : $"{gb:0.0} GB";
            }
            double mb = bytes / (1024.0 * 1024.0);
            return $"{Math.Round(mb):0} MB";
        }

    }
}
