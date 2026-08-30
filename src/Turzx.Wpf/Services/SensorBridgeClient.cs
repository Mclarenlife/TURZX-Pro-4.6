using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using Turzx.SensorBridge;

namespace Turzx.Desktop.Services
{
    internal sealed class SensorBridgeClient
    {
        public bool TryCapture(out SensorBridgeResponse response, out string error)
        {
            return TryRequest("CAPTURE", 5000, out response, out error);
        }

        public bool TryPing(out SensorBridgeResponse response, out string error)
        {
            return TryRequest("PING", 2500, out response, out error);
        }

        private static bool TryRequest(string command, int readTimeout,
            out SensorBridgeResponse response, out string error)
        {
            response = null;
            error = null;
            try
            {
                using (NamedPipeClientStream pipe = new NamedPipeClientStream(".",
                    SensorBridgeProtocol.PipeName, PipeDirection.InOut,
                    PipeOptions.None))
                {
                    pipe.Connect(350);
                    using (StreamReader reader = new StreamReader(pipe, Encoding.UTF8,
                        false, 4096, true))
                    using (StreamWriter writer = new StreamWriter(pipe,
                        new UTF8Encoding(false), 4096, true))
                    {
                        writer.WriteLine(command);
                        writer.Flush();
                        Task<string> readTask = reader.ReadLineAsync();
                        if (!readTask.Wait(readTimeout))
                        {
                            error = "后台服务响应超时。";
                            return false;
                        }
                        string json = readTask.Result;
                        if (string.IsNullOrWhiteSpace(json))
                        {
                            error = "后台服务没有返回数据。";
                            return false;
                        }
                        response = SensorBridgeProtocol.Deserialize(json);
                    }
                }

                if (response == null)
                {
                    error = "后台服务返回了无效数据。";
                    return false;
                }
                if (response.ProtocolVersion != SensorBridgeProtocol.ProtocolVersion)
                {
                    error = "后台服务协议版本不匹配。";
                    return false;
                }
                if (!response.Success)
                {
                    error = string.IsNullOrWhiteSpace(response.Error) ?
                        "后台服务报告传感器不可用。" : response.Error;
                    return false;
                }
                return true;
            }
            catch (TimeoutException)
            {
                error = "后台服务连接超时。";
                return false;
            }
            catch (IOException exception)
            {
                error = exception.Message;
                return false;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }
    }
}
