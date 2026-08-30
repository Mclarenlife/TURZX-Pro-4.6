using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32.SafeHandles;

namespace Turzx.SensorBridge.Service
{
    internal sealed class SensorPipeServer : IDisposable
    {
        private readonly object _sync = new object();
        private Thread _thread;
        private volatile bool _stopping;
        private HardwareSensorSampler _sampler;
        private string _initializationError;

        public void Start()
        {
            if (_thread != null) return;
            _thread = new Thread(Run);
            _thread.Name = "TURZX Sensor Pipe";
            _thread.IsBackground = true;
            _thread.Start();
        }

        private void Run()
        {
            try
            {
                Type pawnIoType = typeof(Computer).Assembly.GetType(
                    "LibreHardwareMonitor.PawnIo.PawnIo", true);
                bool pawnIoInstalled = (bool)pawnIoType.GetProperty(
                    "IsInstalled").GetValue(null, null);
                if (!pawnIoInstalled)
                {
                    _initializationError = "PawnIO 底层驱动尚未安装。";
                }
                else
                {
                    _sampler = new HardwareSensorSampler();
                }
            }
            catch (Exception exception)
            {
                _initializationError = exception.Message;
                ServiceLog.Write("传感器初始化失败", exception);
            }

            while (!_stopping)
            {
                try
                {
                    using (NamedPipeServerStream pipe = CreatePipe())
                    {
                        pipe.WaitForConnection();
                        if (_stopping) break;
                        Serve(pipe);
                    }
                }
                catch (Exception exception)
                {
                    if (!_stopping)
                    {
                        ServiceLog.Write("命名管道请求失败", exception);
                        Thread.Sleep(250);
                    }
                }
            }
        }

        private static NamedPipeServerStream CreatePipe()
        {
            PipeSecurity security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance |
                    PipeAccessRights.Synchronize,
                AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance |
                    PipeAccessRights.Synchronize,
                AccessControlType.Allow));

            return new NamedPipeServerStream(SensorBridgeProtocol.PipeName,
                PipeDirection.InOut, 4, PipeTransmissionMode.Byte,
                PipeOptions.None, 65536, 65536, security);
        }

        private void Serve(NamedPipeServerStream pipe)
        {
            SensorBridgeResponse response;
            if (!IsLocalClient(pipe))
            {
                response = Failure("拒绝远程命名管道客户端。");
            }
            else
            {
                using (StreamReader reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true))
                using (StreamWriter writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true))
                {
                    string request = reader.ReadLine();
                    if (string.Equals(request, "PING", StringComparison.OrdinalIgnoreCase))
                    {
                        response = string.IsNullOrEmpty(_initializationError) ? Success() :
                            Failure(_initializationError);
                    }
                    else if (string.Equals(request, "CAPTURE", StringComparison.OrdinalIgnoreCase))
                    {
                        response = Capture();
                    }
                    else
                    {
                        response = Failure("不支持的请求。");
                    }

                    writer.WriteLine(SensorBridgeProtocol.Serialize(response));
                    writer.Flush();
                    return;
                }
            }

            using (StreamWriter deniedWriter = new StreamWriter(pipe,
                new UTF8Encoding(false), 4096, true))
            {
                deniedWriter.WriteLine(SensorBridgeProtocol.Serialize(response));
                deniedWriter.Flush();
            }
        }

        private SensorBridgeResponse Capture()
        {
            if (_sampler == null)
                return Failure(string.IsNullOrEmpty(_initializationError) ?
                    "传感器服务尚未完成初始化。" : _initializationError);
            try
            {
                SensorBridgeResponse response = Success();
                response.Readings = _sampler.Capture();
                return response;
            }
            catch (Exception exception)
            {
                ServiceLog.Write("传感器采集失败", exception);
                return Failure(exception.Message);
            }
        }

        private static SensorBridgeResponse Success()
        {
            return new SensorBridgeResponse
            {
                ProtocolVersion = SensorBridgeProtocol.ProtocolVersion,
                Success = true,
                Source = "TURZX 后台传感器服务",
                TimestampUtc = DateTime.UtcNow
            };
        }

        private static SensorBridgeResponse Failure(string message)
        {
            return new SensorBridgeResponse
            {
                ProtocolVersion = SensorBridgeProtocol.ProtocolVersion,
                Success = false,
                Source = "TURZX 后台传感器服务",
                Error = message,
                TimestampUtc = DateTime.UtcNow
            };
        }

        private static bool IsLocalClient(NamedPipeServerStream pipe)
        {
            uint processId;
            if (!NativeMethods.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out processId) ||
                processId == 0)
            {
                return false;
            }
            try
            {
                using (Process process = Process.GetProcessById((int)processId))
                {
                    return process.SessionId >= 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public void Stop()
        {
            _stopping = true;
            try
            {
                using (NamedPipeClientStream wake = new NamedPipeClientStream(".",
                    SensorBridgeProtocol.PipeName, PipeDirection.Out))
                {
                    wake.Connect(500);
                }
            }
            catch
            {
            }
            if (_thread != null && !_thread.Join(8000))
                ServiceLog.Write("传感器线程未在超时前退出。", null);
            lock (_sync)
            {
                if (_sampler != null)
                {
                    _sampler.Dispose();
                    _sampler = null;
                }
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private static class NativeMethods
        {
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetNamedPipeClientProcessId(
                SafePipeHandle pipe, out uint clientProcessId);
        }
    }
}
