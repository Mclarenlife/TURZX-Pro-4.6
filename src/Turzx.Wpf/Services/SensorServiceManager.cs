using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using Turzx.SensorBridge;

namespace Turzx.Desktop.Services
{
    internal sealed class SensorServiceState
    {
        public bool Installed { get; set; }
        public bool Running { get; set; }
        public bool Reachable { get; set; }
        public string Detail { get; set; }
    }

    internal static class SensorServiceManager
    {
        public static string ExecutablePath
        {
            get
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "sensor-service", "Turzx.SensorService.exe");
            }
        }

        public static SensorServiceState GetState()
        {
            SensorServiceState state = new SensorServiceState();
            try
            {
                using (ServiceController controller = new ServiceController(
                    SensorBridgeProtocol.ServiceName))
                {
                    controller.Refresh();
                    state.Installed = true;
                    state.Running = controller.Status == ServiceControllerStatus.Running;
                    state.Detail = TranslateStatus(controller.Status);
                }
            }
            catch (InvalidOperationException)
            {
                state.Detail = "尚未安装";
                return state;
            }

            if (state.Running)
            {
                SensorBridgeResponse response;
                string error;
                state.Reachable = new SensorBridgeClient().TryPing(out response, out error);
                state.Detail = state.Reachable ? "运行正常，普通权限界面可以读取" :
                    "服务已运行，但传感器尚不可用：" + error;
            }
            return state;
        }

        public static int RunElevatedCommand(string command)
        {
            if (!File.Exists(ExecutablePath))
                throw new FileNotFoundException("没有找到传感器服务程序。", ExecutablePath);
            ProcessStartInfo start = new ProcessStartInfo
            {
                FileName = ExecutablePath,
                Arguments = command,
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                UseShellExecute = true,
                Verb = "runas"
            };
            using (Process process = Process.Start(start))
            {
                process.WaitForExit();
                return process.ExitCode;
            }
        }

        private static string TranslateStatus(ServiceControllerStatus status)
        {
            switch (status)
            {
                case ServiceControllerStatus.Running: return "正在运行";
                case ServiceControllerStatus.StartPending: return "正在启动";
                case ServiceControllerStatus.StopPending: return "正在停止";
                case ServiceControllerStatus.Paused: return "已暂停";
                case ServiceControllerStatus.PausePending: return "正在暂停";
                case ServiceControllerStatus.ContinuePending: return "正在恢复";
                default: return "已停止";
            }
        }
    }
}
