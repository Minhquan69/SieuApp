using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Nvidia.Nvml;
using System.Management;
using System.Threading.Tasks;

namespace V3SClient.libs
{
    public class Counter : IDisposable
    {
        private bool _hasGPU = false;
        private bool _hasNvidiaGPU = false;

        public static bool HasNvidiaGPU { get; private set; } = false;
        public static bool HasAnyGPU { get; private set; } = false;
        private IntPtr _gpuHandle = IntPtr.Zero;
        private double totalRAM = 1024; //MB
        private double totalDiskSpace = 32; //GB
        
        // Performance Counters with null-safety
        public PerformanceCounter PerformanceCPU { get; private set; }
        public PerformanceCounter TimeCPU { get; private set; }
        public PerformanceCounter OS_CPU { get; private set; }
        public PerformanceCounter UserCPU { get; private set; }
        public PerformanceCounter PerformanceRAM { get; private set; }
        public PerformanceCounter FreeRAM { get; private set; }
        public PerformanceCounter FreeSpaceDiskTotal { get; private set; }
        public List<PerformanceCounter> SentBytesPerSecond { get; private set; } = new List<PerformanceCounter>();
        public List<PerformanceCounter> ReceivedBytesPerSecond { get; private set; } = new List<PerformanceCounter>();

        public Dictionary<string, PerformanceCounter> FreeSpaceDisks { get; private set; } = new Dictionary<string, PerformanceCounter>();
        public Dictionary<string, double> DiskSpaceTotal { get; private set; } = new Dictionary<string, double>();

        public Counter()
        {
            // Run heavy hardware detection in background to avoid blocking UI thread on startup
            Task.Run(() =>
            {
                try
                {
                    InitializePerformanceCounters();
                    DetectGPU();
                    totalRAM = GetTotalRAMInMB();
                    totalDiskSpace = GetTotalDiskSpace();
                    
                    // Update global static statuses
                    HasNvidiaGPU = _hasNvidiaGPU;
                    HasAnyGPU = _hasGPU;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"DEBUG Counter Global Background Initialization Error: {ex.Message}");
                }
            });
        }

        private PerformanceCounter SafeInitCounter(string category, string counter, string instance = null)
        {
            try
            {
                if (PerformanceCounterCategory.Exists(category))
                {
                    var pc = instance != null 
                        ? new PerformanceCounter(category, counter, instance) 
                        : new PerformanceCounter(category, counter);
                    
                    // Trigger a test read to ensure it's actually accessible
                    pc.NextValue();
                    return pc;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR¸ Failed to init counter [{category}\\{counter}]: {ex.Message}");
            }
            return null;
        }

        private void InitializePerformanceCounters()
        {
            // CPU: Try modern "Processor Information" first, then fallback to "Processor"
            PerformanceCPU = SafeInitCounter("Processor Information", "% Processor Utility", "_Total") 
                             ?? SafeInitCounter("Processor", "% Processor Time", "_Total");
            
            TimeCPU = SafeInitCounter("Processor", "% Processor Time", "_Total");
            OS_CPU = SafeInitCounter("Processor", "% Privileged time", "_Total");
            UserCPU = SafeInitCounter("Processor", "% User Time", "_Total");

            // RAM
            PerformanceRAM = SafeInitCounter("Memory", "% Committed Bytes In Use");
            FreeRAM = SafeInitCounter("Memory", "Available MBytes");

            // Disk
            FreeSpaceDiskTotal = SafeInitCounter("LogicalDisk", "% Free Space", "_Total");

            InitializeDiskCounters();
            InitializeNetworkCounters();
        }

        private void InitializeNetworkCounters()
        {
            try
            {
                if (PerformanceCounterCategory.Exists("Network Interface"))
                {
                    var category = new PerformanceCounterCategory("Network Interface");
                    string[] networkAdapters = category.GetInstanceNames();
                    if (networkAdapters != null)
                    {
                        SentBytesPerSecond.Clear();
                        ReceivedBytesPerSecond.Clear();
                        foreach (var adapter in networkAdapters)
                        {
                            var s = SafeInitCounter("Network Interface", "Bytes Sent/sec", adapter);
                            if (s != null) SentBytesPerSecond.Add(s);
                            
                            var r = SafeInitCounter("Network Interface", "Bytes Received/sec", adapter);
                            if (r != null) ReceivedBytesPerSecond.Add(r);
                        }
                        Debug.WriteLine($"DEBUG Network initialization: Monitoring {networkAdapters.Length} interfaces.");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR Network Counter Init failed: {ex.Message}");
            }
        }

        private void DetectGPU()
        {
            // 1. Try Nvidia NVML
            try
            {
                NvGpu.NvmlInitV2();
                var deviceCount = NvGpu.NvmlDeviceGetCountV2();
                if (deviceCount > 0)
                {
                    _gpuHandle = NvGpu.NvmlDeviceGetHandleByIndex(0);
                    _hasGPU = true;
                    _hasNvidiaGPU = true;
                    Debug.WriteLine($"DEBUG¸ Nvidia GPU detected: {deviceCount} devices found.");
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR¸ Nvidia GPU check skipped/failed: {ex.Message}");
            }

            // 2. Check general Windows GPU engine (AMD/Intel/Integrated)
            _hasGPU = DetectOnboardGPU();
        }

        private bool DetectOnboardGPU()
        {
            try
            {
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("select * from Win32_VideoController");
                foreach (ManagementObject obj in searcher.Get())
                {
                    string name = obj["Name"]?.ToString() ?? "Unknown GPU";
                    Debug.WriteLine($"DEBUG Detected Video Controller: {name}");
                    if (name.ToLower().Contains("amd") || name.ToLower().Contains("radeon") || 
                        name.ToLower().Contains("intel") || name.ToLower().Contains("graphics"))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"âŒ GPU hardware survey failed: {ex.Message}");
            }
            return false;
        }

        bool IsNetworkAdapterActive(string adapterName)
        {
            try
            {
                var sentCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", adapterName);
                var receivedCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", adapterName);
                float sent = sentCounter.NextValue();
                float received = receivedCounter.NextValue();
                return (sent > 0 || received > 0);
            }
            catch
            {
                return false; 
            }
        }
        private void InitializeDiskCounters()
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                try
                {
                    string driveLetter = drive.Name.TrimEnd('\\').Replace(":",""); 
                    FreeSpaceDisks[driveLetter] = new PerformanceCounter("LogicalDisk", "% Free Space", $"{driveLetter}:");
                    DiskSpaceTotal[driveLetter]= GetTotalDiskSizeInGB(driveLetter);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erorr PerformanceCounter for {drive.Name}: {ex.Message}");
                }
            }
        }

        private float GetValueSafely(PerformanceCounter pc)
        {
            try
            {
                if (pc != null)
                {
                    return pc.NextValue();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"âš ï¸ Error reading counter: {ex.Message}");
            }
            return 0;
        }

        public double GetFreeSpace(string driveLetter)
        {
            try
            {
                if (FreeSpaceDisks.TryGetValue(driveLetter, out var counter))
                {
                    float val = GetValueSafely(counter);
                    return 1 - (val / 100.0);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetFreeSpace error: {ex.Message}");
            }
            return 0;
        }
        public double GetFreeSpaceGaugePercent(string driveLetter)
        {
            try
            {
                if (FreeSpaceDisks.TryGetValue(driveLetter, out var counter))
                {
                    return 100 - GetValueSafely(counter);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetFreeSpaceGaugePercent error: {ex.Message}");
            }
            return 0;
        }

        public double GetFreeSpaceLabel(string driveLetter)
        {
            try
            {
                if (FreeSpaceDisks.TryGetValue(driveLetter, out var counter) &&
                    DiskSpaceTotal.TryGetValue(driveLetter, out var total))
                {
                    return GetValueSafely(counter) * total / 100.0;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetFreeSpaceLabel error: {ex.Message}");
            }
            return 0;
        }

        public double GetUsedSpaceLabel(string driveLetter)
        {
            try
            {
                if (DiskSpaceTotal.TryGetValue(driveLetter, out var total))
                {
                    double free = GetFreeSpaceLabel(driveLetter);
                    return total - free;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetUsedSpaceLabel error: {ex.Message}");
            }
            return 0;
        }
        private double GetTotalDiskSizeInGB(string driveLetter = "C")
        {
            try
            {
                var drive = new System.IO.DriveInfo(driveLetter);
                return drive.TotalSize / (1024.0 * 1024 * 1024);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetTotalDiskSizeInGB error: {ex.Message}");
                return 1;
            }
        }

        private double GetTotalRAMInMB()
        {
            try
            {
                return new Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory / (1024.0 * 1024);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetTotalRAMInMB error: {ex.Message}");
                return 1024;
            }
        }
        public double GetTotalDiskSpace()
        {
            try
            {
                return DriveInfo.GetDrives()
                                .Where(d => d.IsReady)
                                .Sum(d => d.TotalSize / (1024.0 * 1024 * 1024));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetTotalDiskSpace error: {ex.Message}");
                return 1;
            }
        }

        //RAM
        public double GetFreeRAMInPercent()
        {
            try
            {
                if (FreeRAM == null || totalRAM <= 0) return 0;
                float freeMB = GetValueSafely(FreeRAM);
                return Math.Max(0, Math.Min(100, 100 - (freeMB * 100.0 / totalRAM)));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetFreeRAMInPercent error: {ex.Message}");
                return 0;
            }
        }
        public double GetFreeRAMInGBytes()
        {
            return GetValueSafely(FreeRAM) / 1024.0;
        }
        public double GetUsedRAMInGBytes()
        {
            try
            {
                double totalGB = totalRAM / 1024.0;
                double freeGB = GetFreeRAMInGBytes();
                return Math.Max(0, totalGB - freeGB);
            }
            catch
            {
                return 0;
            }
        }

        // Total Disk
        public double GetFreeSpaceTotal()
        {
            try
            {
                if (FreeSpaceDiskTotal == null) return 0;
                return 1 - (GetValueSafely(FreeSpaceDiskTotal) / 100.0);
            }
            catch
            {
                return 0;
            }
        }

        public double GetFreeSpaceLabel()
        {
            try
            {
                return GetValueSafely(FreeSpaceDiskTotal) * totalDiskSpace / 100.0;
            }
            catch
            {
                return 0;
            }
        }

        public double GetUsedSpaceLabel()
        {
            try
            {
                double free = GetFreeSpaceLabel();
                return Math.Max(0, totalDiskSpace - free);
            }
            catch
            {
                return 0;
            }
        }

        public double GetFreeSpaceTotalGauge()
        {
            return 100 - GetValueSafely(FreeSpaceDiskTotal);
        }

        // Network (Mbps)
        public double GetNetworkSentBytes()
        {
            double total = 0;
            if (SentBytesPerSecond != null)
            {
                foreach (var pc in SentBytesPerSecond)
                {
                    total += GetValueSafely(pc);
                }
            }
            // Convert bytes/sec to Mbps: * 8 bits / 1,000,000
            return total * 8.0 / 1000000.0;
        }

        public double GetNetworkReceivedBytes()
        {
            double total = 0;
            if (ReceivedBytesPerSecond != null)
            {
                foreach (var pc in ReceivedBytesPerSecond)
                {
                    total += GetValueSafely(pc);
                }
            }
            return total * 8.0 / 1000000.0;
        }

        public nvmlUtilization_t GetGpuData()
        {
            var gpuData = new nvmlUtilization_t();
            try
            {
                if (_hasNvidiaGPU && _gpuHandle != IntPtr.Zero)
                {
                    return NvGpu.NvmlDeviceGetUtilizationRates(_gpuHandle);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"â„¹ï¸ NVML data read failed (falling back): {ex.Message}");
            }

            // Fallback to WMI for AMD/Intel/Integrated
            if (_hasGPU)
            {
                return GetOnboardGPUUtilization();
            }
            
            return gpuData;
        }
        private nvmlUtilization_t GetOnboardGPUUtilization()
        {
            var gpuData = new nvmlUtilization_t();
            try
            {
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("select * from Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
                foreach (ManagementObject obj in searcher.Get())
                {
                    gpuData.gpu = Convert.ToUInt32(obj["UtilizationPercentage"] ?? 0);

                    if (obj.Properties["DedicatedUsage"] != null && obj["DedicatedUsage"] != null)
                    {
                        gpuData.memory = Convert.ToUInt32(obj["DedicatedUsage"]);
                    }
                    else if (obj.Properties["SharedUsage"] != null && obj["SharedUsage"] != null)
                    {
                        gpuData.memory = Convert.ToUInt32(obj["SharedUsage"]);
                    }
                    
                    return gpuData;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($" Failed to get onboard GPU data via WMI: {ex.Message}");
            }
            return gpuData;
        }
        public void Dispose()
        {
            try
            {
                PerformanceCPU?.Dispose();
                TimeCPU?.Dispose();
                OS_CPU?.Dispose();
                UserCPU?.Dispose();
                PerformanceRAM?.Dispose();
                FreeRAM?.Dispose();
                FreeSpaceDiskTotal?.Dispose();

                if (SentBytesPerSecond != null)
                {
                    foreach (var pc in SentBytesPerSecond) pc?.Dispose();
                    SentBytesPerSecond.Clear();
                }

                if (ReceivedBytesPerSecond != null)
                {
                    foreach (var pc in ReceivedBytesPerSecond) pc?.Dispose();
                    ReceivedBytesPerSecond.Clear();
                }

                if (FreeSpaceDisks != null)
                {
                    foreach (var pc in FreeSpaceDisks.Values) pc?.Dispose();
                    FreeSpaceDisks.Clear();
                }
                
                // Cleanup NVML if needed (if your library supports it)
                _gpuHandle = IntPtr.Zero;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error disposing Counter: {ex.Message}");
            }
        }
    }
}

