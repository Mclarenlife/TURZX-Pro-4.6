using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.ServiceProcess;
using LibreHardwareMonitor.Hardware;

namespace Turzx.SensorBridge.Service
{
    internal static class Program
    {
        private const string PawnIoSetupSha256 =
            "A3A46226C5E2824F4CDD42BE0EECBABFC672C86F7889710F5AB1E6AD385B47A0";

        private static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0)
                {
                    string command = args[0].ToLowerInvariant();
                    if (command == "--install") return InstallService();
                    if (command == "--uninstall") return UninstallService();
                    if (command == "--diagnose")
                        return Diagnose(args.Length > 1 ? args[1] : null);
                }

                ServiceBase.Run(new TurzxSensorWindowsService());
                return 0;
            }
            catch (Exception exception)
            {
                ServiceLog.WriteInstall(exception.ToString());
                return 1;
            }
        }

        private static int InstallService()
        {
            EnsurePawnIoInstalled();
            string executable = Process.GetCurrentProcess().MainModule.FileName;
            string imagePath = "\\\"" + executable + "\\\" --service";
            bool exists = ServiceExists();
            int exitCode;
            string output;
            if (exists)
            {
                exitCode = RunSc("config " + SensorBridgeProtocol.ServiceName +
                    " binPath= \"" + imagePath + "\" start= auto DisplayName= \"" +
                    SensorBridgeProtocol.ServiceDisplayName + "\"", out output);
            }
            else
            {
                exitCode = RunSc("create " + SensorBridgeProtocol.ServiceName +
                    " binPath= \"" + imagePath + "\" start= auto DisplayName= \"" +
                    SensorBridgeProtocol.ServiceDisplayName + "\"", out output);
            }
            ServiceLog.WriteInstall(output);
            if (exitCode != 0) return exitCode;

            RunSc("description " + SensorBridgeProtocol.ServiceName +
                " \"以系统权限读取温度、功耗等硬件传感器，并通过本地命名管道提供给 TURZX。\"",
                out output);
            ServiceLog.WriteInstall(output);
            RunSc("failure " + SensorBridgeProtocol.ServiceName +
                " reset= 86400 actions= restart/3000/restart/10000/\"\"/0", out output);
            ServiceLog.WriteInstall(output);

            using (ServiceController controller = new ServiceController(
                SensorBridgeProtocol.ServiceName))
            {
                controller.Refresh();
                if (controller.Status != ServiceControllerStatus.Running &&
                    controller.Status != ServiceControllerStatus.StartPending)
                {
                    controller.Start();
                }
                controller.WaitForStatus(ServiceControllerStatus.Running,
                    TimeSpan.FromSeconds(30));
            }
            ServiceLog.WriteInstall("服务安装并启动成功：" + executable);
            return 0;
        }

        private static void EnsurePawnIoInstalled()
        {
            Type pawnIoType = typeof(Computer).Assembly.GetType(
                "LibreHardwareMonitor.PawnIo.PawnIo", true);
            bool installed = (bool)pawnIoType.GetProperty("IsInstalled").GetValue(null, null);
            if (installed)
            {
                ServiceLog.WriteInstall("PawnIO 已安装，无需重复安装。");
                return;
            }

            string lhmExecutable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "LibreHardwareMonitor.exe");
            if (!File.Exists(lhmExecutable))
                throw new FileNotFoundException("缺少用于提取官方 PawnIO 安装程序的 LibreHardwareMonitor.exe。",
                    lhmExecutable);

            string temporaryInstaller = Path.Combine(Path.GetTempPath(),
                "Turzx_PawnIO_setup_" + Guid.NewGuid().ToString("N") + ".exe");
            try
            {
                Assembly assembly = Assembly.LoadFrom(lhmExecutable);
                using (Stream resource = assembly.GetManifestResourceStream(
                    "LibreHardwareMonitor.Resources.PawnIO_setup.exe"))
                {
                    if (resource == null)
                        throw new InvalidOperationException("LibreHardwareMonitor 中没有找到 PawnIO 安装资源。");
                    using (FileStream file = new FileStream(temporaryInstaller,
                        FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        resource.CopyTo(file);
                    }
                }
                VerifyPawnIoInstaller(temporaryInstaller);

                ProcessStartInfo start = new ProcessStartInfo
                {
                    FileName = temporaryInstaller,
                    Arguments = "-install -silent",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (Process process = Process.Start(start))
                {
                    process.WaitForExit();
                    if (process.ExitCode != 0 && process.ExitCode != 3010)
                        throw new InvalidOperationException(
                            "PawnIO 安装失败，退出代码：" + process.ExitCode);
                    ServiceLog.WriteInstall("PawnIO 安装完成，退出代码：" + process.ExitCode);
                }
            }
            finally
            {
                try { if (File.Exists(temporaryInstaller)) File.Delete(temporaryInstaller); }
                catch { }
            }
        }

        private static void VerifyPawnIoInstaller(string path)
        {
            string actual;
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                actual = BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", "");
            }
            if (!string.Equals(actual, PawnIoSetupSha256,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "PawnIO 安装程序完整性校验失败，已拒绝执行。实际 SHA-256：" + actual);
            }
            ServiceLog.WriteInstall("PawnIO 安装程序 SHA-256 校验通过：" + actual);
        }

        private static int UninstallService()
        {
            if (!ServiceExists()) return 0;
            try
            {
                using (ServiceController controller = new ServiceController(
                    SensorBridgeProtocol.ServiceName))
                {
                    controller.Refresh();
                    if (controller.Status != ServiceControllerStatus.Stopped &&
                        controller.Status != ServiceControllerStatus.StopPending)
                    {
                        controller.Stop();
                    }
                    controller.WaitForStatus(ServiceControllerStatus.Stopped,
                        TimeSpan.FromSeconds(20));
                }
            }
            catch (Exception exception)
            {
                ServiceLog.WriteInstall("停止服务时出现错误：" + exception.Message);
            }

            string output;
            int exitCode = RunSc("delete " + SensorBridgeProtocol.ServiceName, out output);
            ServiceLog.WriteInstall(output);
            return exitCode;
        }

        private static bool ServiceExists()
        {
            try
            {
                using (ServiceController controller = new ServiceController(
                    SensorBridgeProtocol.ServiceName))
                {
                    ServiceControllerStatus ignored = controller.Status;
                    return true;
                }
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static int Diagnose(string outputPath)
        {
            SensorBridgeResponse response;
            try
            {
                using (HardwareSensorSampler sampler = new HardwareSensorSampler())
                {
                    response = new SensorBridgeResponse
                    {
                        ProtocolVersion = SensorBridgeProtocol.ProtocolVersion,
                        Success = true,
                        Source = "TURZX 传感器诊断",
                        TimestampUtc = DateTime.UtcNow,
                        Readings = sampler.Capture()
                    };
                }
            }
            catch (Exception exception)
            {
                response = new SensorBridgeResponse
                {
                    ProtocolVersion = SensorBridgeProtocol.ProtocolVersion,
                    Success = false,
                    Source = "TURZX 传感器诊断",
                    Error = exception.ToString(),
                    TimestampUtc = DateTime.UtcNow
                };
            }
            if (!string.IsNullOrWhiteSpace(outputPath))
                File.WriteAllText(outputPath, SensorBridgeProtocol.Serialize(response));
            return response.Success ? 0 : 2;
        }

        private static int RunSc(string arguments, out string output)
        {
            ProcessStartInfo start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "sc.exe"),
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (Process process = Process.Start(start))
            {
                string standardOutput = process.StandardOutput.ReadToEnd();
                string standardError = process.StandardError.ReadToEnd();
                process.WaitForExit();
                output = "sc.exe " + arguments + Environment.NewLine + standardOutput +
                    standardError + "ExitCode=" + process.ExitCode;
                return process.ExitCode;
            }
        }
    }

    internal sealed class TurzxSensorWindowsService : ServiceBase
    {
        private SensorPipeServer _server;

        public TurzxSensorWindowsService()
        {
            ServiceName = SensorBridgeProtocol.ServiceName;
            CanStop = true;
            CanShutdown = true;
            AutoLog = false;
        }

        protected override void OnStart(string[] args)
        {
            _server = new SensorPipeServer();
            _server.Start();
        }

        protected override void OnStop()
        {
            StopServer();
        }

        protected override void OnShutdown()
        {
            StopServer();
            base.OnShutdown();
        }

        private void StopServer()
        {
            if (_server == null) return;
            _server.Dispose();
            _server = null;
        }
    }
}
