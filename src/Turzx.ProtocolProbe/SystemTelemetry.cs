using System;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Turzx.ProtocolProbe
{
    internal sealed class TelemetrySnapshot
    {
        public DateTime Timestamp { get; set; }
        public double CpuPercent { get; set; }
        public ulong MemoryUsedBytes { get; set; }
        public ulong MemoryTotalBytes { get; set; }
        public double MemoryPercent { get; set; }
        public double NetworkDownloadMbps { get; set; }
        public double NetworkUploadMbps { get; set; }
        public ulong DiskUsedBytes { get; set; }
        public ulong DiskTotalBytes { get; set; }
        public double DiskPercent { get; set; }
        public TimeSpan Uptime { get; set; }
        public string CpuName { get; set; }
        public string GpuName { get; set; }
        public double? CpuTemperatureC { get; set; }
        public double? CpuPowerWatts { get; set; }
        public double? CpuFanRpm { get; set; }
        public double? GpuTemperatureC { get; set; }
        public double? GpuPowerWatts { get; set; }
        public double? GpuFanPercent { get; set; }
        public double? GpuFanRpm { get; set; }
        public double? GpuLoadPercent { get; set; }
        public double? GpuMemoryUsedMb { get; set; }
        public double? GpuMemoryTotalMb { get; set; }
        public string SensorSource { get; set; }
    }

    internal sealed class SystemTelemetry
    {
        private readonly object _sync = new object();
        private ulong _lastIdle;
        private ulong _lastKernel;
        private ulong _lastUser;
        private ulong _lastReceived;
        private ulong _lastSent;
        private DateTime _lastNetworkSample;
        private bool _hasCpuSample;
        private bool _hasNetworkSample;
        private readonly string _cpuName;
        private readonly string _gpuName;

        public SystemTelemetry()
        {
            _cpuName = ReadCpuName();
            _gpuName = ReadGpuName();
        }

        public TelemetrySnapshot Capture()
        {
            lock (_sync)
            {
                DateTime now = DateTime.Now;
                double cpu = CaptureCpu();

                MemoryStatus memory = new MemoryStatus();
                memory.Length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
                if (!NativeMethods.GlobalMemoryStatusEx(ref memory))
                {
                    throw new InvalidOperationException("GlobalMemoryStatusEx failed.");
                }

                ulong received;
                ulong sent;
                CaptureNetworkTotals(out received, out sent);
                double downloadMbps = 0;
                double uploadMbps = 0;
                if (_hasNetworkSample)
                {
                    double seconds = Math.Max(0.001, (now - _lastNetworkSample).TotalSeconds);
                    downloadMbps = SafeDelta(received, _lastReceived) * 8.0 / seconds / 1000000.0;
                    uploadMbps = SafeDelta(sent, _lastSent) * 8.0 / seconds / 1000000.0;
                }
                _lastReceived = received;
                _lastSent = sent;
                _lastNetworkSample = now;
                _hasNetworkSample = true;

                ulong diskTotal;
                ulong diskFree;
                CaptureFixedDiskTotals(out diskTotal, out diskFree);
                ulong memoryUsed = memory.TotalPhysical - memory.AvailablePhysical;
                ulong diskUsed = diskTotal >= diskFree ? diskTotal - diskFree : 0;

                return new TelemetrySnapshot
                {
                    Timestamp = now,
                    CpuPercent = ClampPercent(cpu),
                    MemoryUsedBytes = memoryUsed,
                    MemoryTotalBytes = memory.TotalPhysical,
                    MemoryPercent = memory.TotalPhysical == 0 ? 0 :
                        ClampPercent(memoryUsed * 100.0 / memory.TotalPhysical),
                    NetworkDownloadMbps = Math.Max(0, downloadMbps),
                    NetworkUploadMbps = Math.Max(0, uploadMbps),
                    DiskUsedBytes = diskUsed,
                    DiskTotalBytes = diskTotal,
                    DiskPercent = diskTotal == 0 ? 0 : ClampPercent(diskUsed * 100.0 / diskTotal),
                    Uptime = TimeSpan.FromMilliseconds(NativeMethods.GetTickCount64()),
                    CpuName = _cpuName,
                    GpuName = _gpuName
                };
            }
        }

        private double CaptureCpu()
        {
            FileTime idle;
            FileTime kernel;
            FileTime user;
            if (!NativeMethods.GetSystemTimes(out idle, out kernel, out user))
            {
                throw new InvalidOperationException("GetSystemTimes failed.");
            }

            ulong currentIdle = ToUInt64(idle);
            ulong currentKernel = ToUInt64(kernel);
            ulong currentUser = ToUInt64(user);
            double percent = 0;
            if (_hasCpuSample)
            {
                ulong idleDelta = SafeDelta(currentIdle, _lastIdle);
                ulong kernelDelta = SafeDelta(currentKernel, _lastKernel);
                ulong userDelta = SafeDelta(currentUser, _lastUser);
                ulong total = kernelDelta + userDelta;
                if (total > 0 && total >= idleDelta)
                {
                    percent = (total - idleDelta) * 100.0 / total;
                }
            }

            _lastIdle = currentIdle;
            _lastKernel = currentKernel;
            _lastUser = currentUser;
            _hasCpuSample = true;
            return percent;
        }

        private static void CaptureNetworkTotals(out ulong received, out ulong sent)
        {
            received = 0;
            sent = 0;
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up ||
                    adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                try
                {
                    IPv4InterfaceStatistics statistics = adapter.GetIPv4Statistics();
                    received += (ulong)Math.Max(0, statistics.BytesReceived);
                    sent += (ulong)Math.Max(0, statistics.BytesSent);
                }
                catch (NetworkInformationException)
                {
                }
            }
        }

        private static void CaptureFixedDiskTotals(out ulong total, out ulong free)
        {
            total = 0;
            free = 0;
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                    {
                        total += (ulong)drive.TotalSize;
                        free += (ulong)drive.AvailableFreeSpace;
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private static string ReadCpuName()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    object value = key == null ? null : key.GetValue("ProcessorNameString");
                    return value == null ? "CPU" : value.ToString().Trim();
                }
            }
            catch
            {
                return "CPU";
            }
        }

        private static string ReadGpuName()
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT Name, PNPDeviceID, AdapterCompatibility FROM Win32_VideoController"))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    string bestName = null;
                    int bestScore = int.MinValue;
                    foreach (ManagementObject result in results)
                    {
                        object value = result["Name"];
                        if (value != null && value.ToString().Length > 0)
                        {
                            string name = value.ToString();
                            string lower = name.ToLowerInvariant();
                            string pnp = result["PNPDeviceID"] == null ? string.Empty :
                                result["PNPDeviceID"].ToString();
                            string vendor = result["AdapterCompatibility"] == null ? string.Empty :
                                result["AdapterCompatibility"].ToString().ToLowerInvariant();
                            int score = 0;
                            if (pnp.StartsWith("PCI", StringComparison.OrdinalIgnoreCase))
                            {
                                score += 100;
                            }
                            if (lower.Contains("nvidia") || lower.Contains("radeon") ||
                                lower.Contains("geforce") || vendor.Contains("advanced micro") ||
                                vendor.Contains("intel"))
                            {
                                score += 50;
                            }
                            if (lower.Contains("virtual") || lower.Contains("remote") ||
                                lower.Contains("gameviewer") || lower.Contains("basic display"))
                            {
                                score -= 1000;
                            }
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestName = name;
                            }
                        }
                    }
                    if (!string.IsNullOrEmpty(bestName))
                    {
                        return bestName;
                    }
                }
            }
            catch
            {
            }
            return "GPU";
        }

        private static ulong ToUInt64(FileTime value)
        {
            return ((ulong)value.High << 32) | value.Low;
        }

        private static ulong SafeDelta(ulong current, ulong previous)
        {
            return current >= previous ? current - previous : 0;
        }

        private static double ClampPercent(double value)
        {
            return Math.Max(0, Math.Min(100, value));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileTime
        {
            public uint Low;
            public uint High;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MemoryStatus
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhysical;
            public ulong AvailablePhysical;
            public ulong TotalPageFile;
            public ulong AvailablePageFile;
            public ulong TotalVirtual;
            public ulong AvailableVirtual;
            public ulong AvailableExtendedVirtual;
        }

        private static class NativeMethods
        {
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetSystemTimes(out FileTime idleTime,
                out FileTime kernelTime, out FileTime userTime);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);

            [DllImport("kernel32.dll")]
            internal static extern ulong GetTickCount64();
        }
    }
}
