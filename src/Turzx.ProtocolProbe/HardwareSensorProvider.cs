using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using LibreHardwareMonitor.Hardware;

namespace Turzx.ProtocolProbe
{
    internal sealed class HardwareSensorSnapshot
    {
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
        public string Source { get; set; }
    }

    internal sealed class HardwareSensorProvider : IDisposable
    {
        private Computer _computer;
        private bool _computerAvailable;
        private string _initializationError;

        public HardwareSensorProvider()
        {
            try
            {
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMemoryEnabled = true,
                    IsMotherboardEnabled = true,
                    IsControllerEnabled = true
                };
                _computer.Open();
                _computerAvailable = true;
            }
            catch (Exception exception)
            {
                _initializationError = exception.Message;
                if (_computer != null)
                {
                    try { _computer.Close(); }
                    catch { }
                    _computer = null;
                }
            }
        }

        public string InitializationError
        {
            get { return _initializationError; }
        }

        public HardwareSensorSnapshot Capture()
        {
            HardwareSensorSnapshot result = new HardwareSensorSnapshot();
            bool lhmFound = false;

            if (_computerAvailable && _computer != null)
            {
                try
                {
                    _computer.Accept(new UpdateVisitor());
                    foreach (IHardware hardware in _computer.Hardware)
                    {
                        lhmFound |= ReadHardware(hardware, result);
                    }
                }
                catch (Exception exception)
                {
                    _initializationError = exception.Message;
                }
            }

            HardwareSensorSnapshot nvidia = CaptureNvidiaSmi();
            bool nvidiaFound = nvidia != null;
            if (nvidiaFound)
            {
                result.GpuTemperatureC = IfMissing(result.GpuTemperatureC, nvidia.GpuTemperatureC);
                result.GpuPowerWatts = IfMissing(result.GpuPowerWatts, nvidia.GpuPowerWatts);
                result.GpuFanPercent = IfMissing(result.GpuFanPercent, nvidia.GpuFanPercent);
                result.GpuLoadPercent = IfMissing(result.GpuLoadPercent, nvidia.GpuLoadPercent);
                result.GpuMemoryUsedMb = IfMissing(result.GpuMemoryUsedMb, nvidia.GpuMemoryUsedMb);
                result.GpuMemoryTotalMb = IfMissing(result.GpuMemoryTotalMb, nvidia.GpuMemoryTotalMb);
            }

            if (lhmFound && nvidiaFound)
            {
                result.Source = "LibreHardwareMonitor + NVIDIA SMI";
            }
            else if (lhmFound)
            {
                result.Source = "LibreHardwareMonitor";
            }
            else if (nvidiaFound)
            {
                result.Source = "NVIDIA SMI";
            }
            else
            {
                result.Source = "Unavailable";
            }
            return result;
        }

        public void Enrich(TelemetrySnapshot target)
        {
            if (target == null)
            {
                throw new ArgumentNullException("target");
            }

            HardwareSensorSnapshot source = Capture();
            target.CpuTemperatureC = source.CpuTemperatureC;
            target.CpuPowerWatts = source.CpuPowerWatts;
            target.CpuFanRpm = source.CpuFanRpm;
            target.GpuTemperatureC = source.GpuTemperatureC;
            target.GpuPowerWatts = source.GpuPowerWatts;
            target.GpuFanPercent = source.GpuFanPercent;
            target.GpuFanRpm = source.GpuFanRpm;
            target.GpuLoadPercent = source.GpuLoadPercent;
            target.GpuMemoryUsedMb = source.GpuMemoryUsedMb;
            target.GpuMemoryTotalMb = source.GpuMemoryTotalMb;
            target.SensorSource = source.Source;
        }

        private static bool ReadHardware(IHardware hardware, HardwareSensorSnapshot result)
        {
            bool found = false;
            string kind = hardware.HardwareType.ToString();
            bool isCpu = string.Equals(kind, "Cpu", StringComparison.OrdinalIgnoreCase);
            bool isGpu = kind.StartsWith("Gpu", StringComparison.OrdinalIgnoreCase);
            bool isBoard = string.Equals(kind, "Motherboard", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kind, "SuperIO", StringComparison.OrdinalIgnoreCase);

            foreach (ISensor sensor in hardware.Sensors)
            {
                if (!sensor.Value.HasValue)
                {
                    continue;
                }

                double value = sensor.Value.Value;
                string type = sensor.SensorType.ToString();
                string name = sensor.Name == null ? string.Empty : sensor.Name;
                string lower = name.ToLowerInvariant();

                if (isCpu && type == "Temperature" &&
                    (lower.Contains("package") || lower.Contains("average") ||
                     lower.Contains("core max") || !result.CpuTemperatureC.HasValue))
                {
                    result.CpuTemperatureC = PreferTemperature(
                        result.CpuTemperatureC, value, lower.Contains("package"));
                    found = true;
                }
                else if (isCpu && type == "Power" &&
                    (lower.Contains("package") || lower.Contains("cpu") ||
                     !result.CpuPowerWatts.HasValue))
                {
                    result.CpuPowerWatts = PreferPositive(result.CpuPowerWatts, value);
                    found = true;
                }
                else if ((isCpu || isBoard) && type == "Fan" && lower.Contains("cpu"))
                {
                    result.CpuFanRpm = PreferPositive(result.CpuFanRpm, value);
                    found = true;
                }
                else if (isGpu && type == "Temperature" &&
                    (lower.Contains("core") || lower.Contains("gpu") ||
                     !result.GpuTemperatureC.HasValue))
                {
                    result.GpuTemperatureC = PreferTemperature(
                        result.GpuTemperatureC, value, lower.Contains("core"));
                    found = true;
                }
                else if (isGpu && type == "Power" &&
                    (lower.Contains("package") || lower.Contains("board") ||
                     lower.Contains("gpu") || !result.GpuPowerWatts.HasValue))
                {
                    result.GpuPowerWatts = PreferPositive(result.GpuPowerWatts, value);
                    found = true;
                }
                else if (isGpu && type == "Fan")
                {
                    result.GpuFanRpm = PreferPositive(result.GpuFanRpm, value);
                    found = true;
                }
                else if (isGpu && type == "Control" && lower.Contains("fan"))
                {
                    result.GpuFanPercent = PreferPositive(result.GpuFanPercent, value);
                    found = true;
                }
                else if (isGpu && type == "Load" &&
                    (lower.Contains("core") || lower == "gpu" || !result.GpuLoadPercent.HasValue))
                {
                    result.GpuLoadPercent = ClampPercent(value);
                    found = true;
                }
            }

            foreach (IHardware child in hardware.SubHardware)
            {
                found |= ReadHardware(child, result);
            }
            return found;
        }

        private static HardwareSensorSnapshot CaptureNvidiaSmi()
        {
            string executable = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe");
            if (!File.Exists(executable))
            {
                return null;
            }

            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = executable;
                info.Arguments = "--query-gpu=temperature.gpu,power.draw,utilization.gpu,fan.speed,memory.used,memory.total --format=csv,noheader,nounits";
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;

                using (Process process = Process.Start(info))
                {
                    string line = process.StandardOutput.ReadLine();
                    process.WaitForExit(3000);
                    if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(line))
                    {
                        return null;
                    }

                    string[] values = line.Split(',');
                    if (values.Length < 6)
                    {
                        return null;
                    }

                    return new HardwareSensorSnapshot
                    {
                        GpuTemperatureC = ParseNullable(values[0]),
                        GpuPowerWatts = ParseNullable(values[1]),
                        GpuLoadPercent = ParseNullable(values[2]),
                        GpuFanPercent = ParseNullable(values[3]),
                        GpuMemoryUsedMb = ParseNullable(values[4]),
                        GpuMemoryTotalMb = ParseNullable(values[5]),
                        Source = "NVIDIA SMI"
                    };
                }
            }
            catch
            {
                return null;
            }
        }

        private static double? ParseNullable(string value)
        {
            double parsed;
            if (double.TryParse(value.Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }
            return null;
        }

        private static double? IfMissing(double? target, double? fallback)
        {
            return !target.HasValue && fallback.HasValue ? fallback : target;
        }

        private static double? PreferTemperature(double? current, double candidate, bool preferred)
        {
            if (!current.HasValue || preferred)
            {
                return candidate;
            }
            return current;
        }

        private static double? PreferPositive(double? current, double candidate)
        {
            if (candidate <= 0)
            {
                return current;
            }
            if (!current.HasValue || (candidate > 0 && current.Value <= 0))
            {
                return candidate;
            }
            return current;
        }

        private static double ClampPercent(double value)
        {
            return Math.Max(0, Math.Min(100, value));
        }

        public void Dispose()
        {
            if (_computer != null)
            {
                try { _computer.Close(); }
                catch { }
                _computer = null;
            }
            _computerAvailable = false;
        }

        private sealed class UpdateVisitor : IVisitor
        {
            public void VisitComputer(IComputer computer)
            {
                computer.Traverse(this);
            }

            public void VisitHardware(IHardware hardware)
            {
                hardware.Update();
                foreach (IHardware child in hardware.SubHardware)
                {
                    child.Accept(this);
                }
            }

            public void VisitSensor(ISensor sensor) { }
            public void VisitParameter(IParameter parameter) { }
        }
    }
}
