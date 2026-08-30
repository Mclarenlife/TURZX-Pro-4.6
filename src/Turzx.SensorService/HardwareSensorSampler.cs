using System;
using System.Collections.Generic;
using LibreHardwareMonitor.Hardware;
using Turzx.ProtocolProbe;

namespace Turzx.SensorBridge.Service
{
    internal sealed class HardwareSensorSampler : IDisposable
    {
        private readonly Computer _computer;
        private readonly SystemTelemetry _telemetry;

        public HardwareSensorSampler()
        {
            _telemetry = new SystemTelemetry();
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

        public List<SensorBridgeReading> Capture()
        {
            List<SensorBridgeReading> result = new List<SensorBridgeReading>();
            _computer.Accept(new UpdateVisitor());
            foreach (IHardware hardware in _computer.Hardware)
            {
                ReadHardware(hardware, result);
            }

            TelemetrySnapshot system = _telemetry.Capture();
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
            return result;
        }

        private static void ReadHardware(IHardware hardware,
            IList<SensorBridgeReading> target)
        {
            string category = TranslateHardware(hardware.HardwareType.ToString());
            foreach (ISensor sensor in hardware.Sensors)
            {
                SensorBridgeReading reading = new SensorBridgeReading
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

        private static bool IsCommonSensor(SensorBridgeReading sensor)
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
            if (category == "主板") return type == "Temperature" || type == "Fan";
            if (category == "网络") return false;
            if (category == "电池") return type == "Level" || type == "Power";
            return type == "Temperature" || type == "Fan" || type == "Power" ||
                type == "Load";
        }

        private static void AddSystem(IList<SensorBridgeReading> target, string id,
            string category, string hardware, string name, string type, string unit,
            double value)
        {
            target.Add(new SensorBridgeReading
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
            _computer.Close();
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
