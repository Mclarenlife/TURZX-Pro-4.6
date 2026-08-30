using System;
using System.IO;
using System.Xml.Serialization;
using Turzx.Desktop.Models;

namespace Turzx.Desktop.Services
{
    internal static class SensorMappingStore
    {
        public static string DefaultPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                    "TurzxControl", "sensor-mappings.xml");
            }
        }

        public static SensorMappingSettings LoadOrDefault()
        {
            if (!File.Exists(DefaultPath)) return new SensorMappingSettings();
            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(SensorMappingSettings));
                using (FileStream stream = File.OpenRead(DefaultPath))
                    return (SensorMappingSettings)serializer.Deserialize(stream);
            }
            catch
            {
                return new SensorMappingSettings();
            }
        }

        public static void Save(SensorMappingSettings settings)
        {
            string directory = Path.GetDirectoryName(DefaultPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            XmlSerializer serializer = new XmlSerializer(typeof(SensorMappingSettings));
            using (FileStream stream = File.Create(DefaultPath))
                serializer.Serialize(stream, settings);
        }
    }
}
