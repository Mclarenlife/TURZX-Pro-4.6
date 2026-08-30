using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace Turzx.SensorBridge
{
    public static class SensorBridgeProtocol
    {
        public const string PipeName = "Turzx.SensorBridge.v1";
        public const string ServiceName = "TurzxSensorService";
        public const string ServiceDisplayName = "TURZX 传感器服务";
        public const int ProtocolVersion = 1;

        public static string Serialize(SensorBridgeResponse response)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 1024 * 1024;
            return serializer.Serialize(response);
        }

        public static SensorBridgeResponse Deserialize(string json)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 1024 * 1024;
            return serializer.Deserialize<SensorBridgeResponse>(json);
        }
    }

    [Serializable]
    public sealed class SensorBridgeResponse
    {
        public SensorBridgeResponse()
        {
            Readings = new List<SensorBridgeReading>();
        }

        public int ProtocolVersion { get; set; }
        public bool Success { get; set; }
        public string Error { get; set; }
        public string Source { get; set; }
        public DateTime TimestampUtc { get; set; }
        public List<SensorBridgeReading> Readings { get; set; }
    }

    [Serializable]
    public sealed class SensorBridgeReading
    {
        public string Id { get; set; }
        public string Category { get; set; }
        public string Hardware { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string Unit { get; set; }
        public double? Value { get; set; }
    }
}
