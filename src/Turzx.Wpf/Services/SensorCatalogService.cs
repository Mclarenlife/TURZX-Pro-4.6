using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Security.Principal;
using LibreHardwareMonitor.Hardware;
using Turzx.Desktop.Models;
using Turzx.ProtocolProbe;
using Turzx.SensorBridge;

namespace Turzx.Desktop.Services
{
    internal sealed class SensorCatalogService : IDisposable
    {
        private readonly Computer _computer;
        private readonly SystemTelemetry _telemetry;
        private readonly SensorBridgeClient _bridgeClient;
        private double? _memoryClockMHz;
        private PerformanceCounter _cpuFrequencyCounter;
        private PerformanceCounter _cpuPerformanceCounter;

        public string SourceName { get; private set; }
        public string LastBridgeError { get; private set; }
        public bool IsBridgeConnected { get; private set; }
        public string CpuHardwareName { get; private set; }
        public string GpuHardwareName { get; private set; }

        public SensorCatalogService()
        {
            _telemetry = new SystemTelemetry();
            _bridgeClient = new SensorBridgeClient();
            SourceName = "Windows 基础数据";
            if (!IsProcessElevated()) return;

            try
            {
                _computer = new Computer
                {
                    IsBatteryEnabled = true,
                    IsControllerEnabled = true,
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMemoryEnabled = true,
                    IsMotherboardEnabled = true,
                    IsNetworkEnabled = true,
                    IsPowerMonitorEnabled = true,
                    IsPsuEnabled = true,
                    IsStorageEnabled = true
                };
                _computer.Open();
            }
            catch
            {
                if (_computer != null) _computer.Close();
                _computer = null;
            }
        }

        public IList<SensorReading> Capture()
        {
            SensorBridgeResponse bridgeResponse;
            string bridgeError;
            if (_bridgeClient.TryCapture(out bridgeResponse, out bridgeError))
            {
                IsBridgeConnected = true;
                LastBridgeError = null;
                SourceName = bridgeResponse.Source;
                IList<SensorReading> result = ConvertBridgeReadings(bridgeResponse.Readings);
                AddDesktopReadings(result);
                return result;
            }

            IsBridgeConnected = false;
            LastBridgeError = bridgeError;
            if (_computer != null)
            {
                SourceName = "管理员权限兼容模式";
                return CaptureDirect();
            }

            SourceName = "Windows 基础数据";
            return CaptureSystemOnly();
        }

        private IList<SensorReading> CaptureDirect()
        {
            List<SensorReading> result = new List<SensorReading>();
            _computer.Accept(new UpdateVisitor());
            foreach (IHardware hardware in _computer.Hardware)
            {
                ReadHardware(hardware, result);
            }

            AddSystemReadings(result);
            AddDesktopReadings(result);
            return result;
        }

        private IList<SensorReading> CaptureSystemOnly()
        {
            List<SensorReading> result = new List<SensorReading>();
            AddSystemReadings(result);
            AddDesktopReadings(result);
            return result;
        }

        private void AddSystemReadings(IList<SensorReading> result)
        {
            TelemetrySnapshot system = _telemetry.Capture();
            CpuHardwareName = system.CpuName;
            GpuHardwareName = system.GpuName;
            AddSystem(result, "os/cpu-total", "系统", system.CpuName,
                "CPU 总使用率", "Load", "%", system.CpuPercent);
            AddSystem(result, "os/memory-used", "系统", "物理内存",
                "内存使用率", "Load", "%", system.MemoryPercent);
            AddSystem(result, "os/disk-used", "系统", "固定磁盘",
                "磁盘使用率", "Load", "%", system.DiskPercent);
            AddSystem(result, "os/network-down", "网络", "所有活动网卡",
                "下载速度", "Throughput", "Mbps", system.NetworkDownloadMbps);
            AddSystem(result, "os/network-up", "网络", "所有活动网卡",
                "上传速度", "Throughput", "Mbps", system.NetworkUploadMbps);
            AddSystem(result, "os/uptime", "系统", "Windows",
                "运行时间", "Time", "h", system.Uptime.TotalHours);
        }

        private void AddDesktopReadings(IList<SensorReading> result)
        {
            TelemetrySnapshot system = _telemetry.Capture();
            double cDrivePercent = 0;
            try
            {
                DriveInfo drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                if (drive.IsReady && drive.TotalSize > 0)
                    cDrivePercent = (drive.TotalSize - drive.AvailableFreeSpace) * 100.0 / drive.TotalSize;
            }
            catch { }

            double appMemoryPercent = 0;
            try
            {
                if (system.MemoryTotalBytes > 0)
                    appMemoryPercent = Process.GetCurrentProcess().WorkingSet64 * 100.0 /
                        system.MemoryTotalBytes;
            }
            catch { }

            if (!_memoryClockMHz.HasValue) _memoryClockMHz = ReadMemoryClockMHz();
            double cpuClockMHz = ReadCpuClockMHz();
            if (cpuClockMHz > 0)
                AddSystem(result, "os/cpu-clock", "CPU", system.CpuName,
                    "CPU 实时平均频率", "Clock", "MHz", cpuClockMHz);
            AddSystem(result, "os/memory-clock", "内存", "物理内存",
                "内存频率", "Clock", "MHz", _memoryClockMHz ?? 0);
            AddSystem(result, "os/disk-c-used", "系统", "C 盘",
                "C 盘使用率", "Load", "%", cDrivePercent);
            AddSystem(result, "os/app-memory", "系统", "TURZX 控制软件",
                "软件后台内存占用率", "Load", "%", appMemoryPercent);
        }

        private static double ReadMemoryClockMHz()
        {
            try
            {
                double maximum = 0;
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT ConfiguredClockSpeed, Speed FROM Win32_PhysicalMemory"))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementObject item in results)
                    {
                        object raw = item["ConfiguredClockSpeed"] ?? item["Speed"];
                        double value;
                        if (raw != null && double.TryParse(raw.ToString(), out value))
                            maximum = Math.Max(maximum, value);
                    }
                }
                return maximum;
            }
            catch { return 0; }
        }

        private double ReadCpuClockMHz()
        {
            try
            {
                if (_cpuFrequencyCounter == null)
                    _cpuFrequencyCounter = new PerformanceCounter(
                        "Processor Information", "Processor Frequency", "_Total", true);
                if (_cpuPerformanceCounter == null)
                    _cpuPerformanceCounter = new PerformanceCounter(
                        "Processor Information", "% Processor Performance", "_Total", true);

                double baseFrequency = _cpuFrequencyCounter.NextValue();
                double performancePercent = _cpuPerformanceCounter.NextValue();
                if (baseFrequency > 0 && performancePercent > 0)
                    return baseFrequency * performancePercent / 100.0;
                if (baseFrequency > 0) return baseFrequency;
            }
            catch
            {
                DisposeCpuCounters();
            }

            return ReadCpuClockFromWmi();
        }

        private static double ReadCpuClockFromWmi()
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT CurrentClockSpeed FROM Win32_Processor"))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementObject item in results)
                    {
                        double value;
                        if (item["CurrentClockSpeed"] != null &&
                            double.TryParse(item["CurrentClockSpeed"].ToString(), out value))
                            return value;
                    }
                }
            }
            catch { }
            return 0;
        }

        private void DisposeCpuCounters()
        {
            if (_cpuFrequencyCounter != null)
            {
                _cpuFrequencyCounter.Dispose();
                _cpuFrequencyCounter = null;
            }
            if (_cpuPerformanceCounter != null)
            {
                _cpuPerformanceCounter.Dispose();
                _cpuPerformanceCounter = null;
            }
        }

        private static IList<SensorReading> ConvertBridgeReadings(
            IList<SensorBridgeReading> readings)
        {
            List<SensorReading> result = new List<SensorReading>();
            if (readings == null) return result;
            foreach (SensorBridgeReading reading in readings)
            {
                result.Add(new SensorReading
                {
                    Id = reading.Id,
                    Category = reading.Category,
                    Hardware = reading.Hardware,
                    Name = reading.Name,
                    Type = reading.Type,
                    Unit = reading.Unit,
                    Value = reading.Value
                });
            }
            return result;
        }

        private static void ReadHardware(IHardware hardware, IList<SensorReading> target)
        {
            string category = TranslateHardware(hardware.HardwareType.ToString());
            foreach (ISensor sensor in hardware.Sensors)
            {
                SensorReading reading = new SensorReading
                {
                    Id = sensor.Identifier.ToString(),
                    Category = category,
                    Hardware = hardware.Name,
                    Name = sensor.Name,
                    Type = sensor.SensorType.ToString(),
                    Unit = UnitFor(sensor.SensorType.ToString()),
                    Value = sensor.Value.HasValue ? (double?)sensor.Value.Value : null
                };
                if (IsCommonSensor(reading)) target.Add(reading);
            }
            foreach (IHardware child in hardware.SubHardware)
            {
                ReadHardware(child, target);
            }
        }

        private static bool IsCommonSensor(SensorReading sensor)
        {
            string category = sensor.Category ?? string.Empty;
            string type = sensor.Type ?? string.Empty;
            string name = sensor.Name ?? string.Empty;

            if (category == "CPU")
            {
                if (type == "Temperature")
                {
                    if (name.IndexOf("Distance", StringComparison.OrdinalIgnoreCase) >= 0)
                        return false;
                    return name.Equals("Core Max", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("Core Average", StringComparison.OrdinalIgnoreCase) ||
                        name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Tctl", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Tdie", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("CCD", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                if (type == "Power")
                    return name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Cores", StringComparison.OrdinalIgnoreCase) >= 0;
                if (type == "Clock")
                    return name.IndexOf("Average", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Bus", StringComparison.OrdinalIgnoreCase) >= 0;
                return false;
            }

            if (category == "GPU")
            {
                if (type == "Temperature" || type == "Fan" || type == "Power") return true;
                if (type == "Load")
                    return name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Memory", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Video", StringComparison.OrdinalIgnoreCase) >= 0;
                if (type == "Clock")
                    return name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Memory", StringComparison.OrdinalIgnoreCase) >= 0;
                return type == "Data" || type == "SmallData";
            }

            if (category == "内存")
                return type == "Load" || type == "Data" || type == "SmallData";

            if (category == "存储")
                return type == "Temperature" || type == "Load" || type == "Level" ||
                    type == "Data" || type == "SmallData";

            if (category == "主板")
                return type == "Temperature" || type == "Fan";

            if (category == "网络") return false;
            if (category == "电池") return type == "Level" || type == "Power";

            return type == "Temperature" || type == "Fan" || type == "Power" ||
                type == "Load";
        }

        private static void AddSystem(IList<SensorReading> target, string id,
            string category, string hardware, string name, string type, string unit,
            double value)
        {
            target.Add(new SensorReading
            {
                Id = id,
                Category = category,
                Hardware = hardware,
                Name = name,
                Type = type,
                Unit = unit,
                Value = value
            });
        }

        private static string TranslateHardware(string value)
        {
            if (value.StartsWith("Cpu", StringComparison.OrdinalIgnoreCase)) return "CPU";
            if (value.StartsWith("Gpu", StringComparison.OrdinalIgnoreCase)) return "GPU";
            if (value.IndexOf("Memory", StringComparison.OrdinalIgnoreCase) >= 0) return "内存";
            if (value.IndexOf("Storage", StringComparison.OrdinalIgnoreCase) >= 0) return "存储";
            if (value.IndexOf("Network", StringComparison.OrdinalIgnoreCase) >= 0) return "网络";
            if (value.IndexOf("Motherboard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("SuperIO", StringComparison.OrdinalIgnoreCase) >= 0) return "主板";
            if (value.IndexOf("Battery", StringComparison.OrdinalIgnoreCase) >= 0) return "电池";
            return value;
        }

        private static string UnitFor(string sensorType)
        {
            switch (sensorType)
            {
                case "Temperature": return "°C";
                case "Load":
                case "Control":
                case "Level": return "%";
                case "Clock": return "MHz";
                case "Power": return "W";
                case "Fan": return "RPM";
                case "Voltage": return "V";
                case "Current": return "A";
                case "Energy": return "Wh";
                case "Data": return "GB";
                case "SmallData": return "MB";
                case "Throughput": return "B/s";
                case "TimeSpan": return "s";
                default: return string.Empty;
            }
        }

        public void Dispose()
        {
            DisposeCpuCounters();
            if (_computer != null) _computer.Close();
        }

        private static bool IsProcessElevated()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                return new WindowsPrincipal(identity).IsInRole(
                    WindowsBuiltInRole.Administrator);
            }
        }

        private sealed class UpdateVisitor : IVisitor
        {
            public void VisitComputer(IComputer computer) { computer.Traverse(this); }
            public void VisitHardware(IHardware hardware)
            {
                hardware.Update();
                foreach (IHardware child in hardware.SubHardware) child.Accept(this);
            }
            public void VisitSensor(ISensor sensor) { }
            public void VisitParameter(IParameter parameter) { }
        }
    }
}
