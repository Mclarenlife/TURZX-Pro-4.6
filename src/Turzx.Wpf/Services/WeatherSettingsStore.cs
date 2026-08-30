using System;
using System.IO;
using System.Xml.Serialization;
using Turzx.Desktop.Models;

namespace Turzx.Desktop.Services
{
    internal static class WeatherSettingsStore
    {
        public static string DefaultPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                    "TurzxControl", "weather.xml");
            }
        }

        public static WeatherSettings LoadOrDefault()
        {
            if (!File.Exists(DefaultPath)) return new WeatherSettings();
            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(WeatherSettings));
                using (FileStream stream = File.OpenRead(DefaultPath))
                    return (WeatherSettings)serializer.Deserialize(stream);
            }
            catch { return new WeatherSettings(); }
        }

        public static void Save(WeatherSettings settings)
        {
            string directory = Path.GetDirectoryName(DefaultPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            XmlSerializer serializer = new XmlSerializer(typeof(WeatherSettings));
            using (FileStream stream = File.Create(DefaultPath))
                serializer.Serialize(stream, settings);
        }
    }
}
