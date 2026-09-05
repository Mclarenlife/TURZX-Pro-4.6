using System;
using System.IO;
using System.Xml.Serialization;

namespace Turzx.Desktop.Services
{
    public enum OutputSessionMode
    {
        None,
        Image,
        Video,
        Theme
    }

    [Serializable]
    public sealed class OutputSessionSettings
    {
        public OutputSessionSettings()
        {
            Mode = OutputSessionMode.None;
            ImageFitIndex = 1;
            ImageScale = 1;
            VideoFitIndex = 0;
            VideoFpsIndex = 3;
            VideoLoop = true;
            VideoScale = 1;
        }

        public OutputSessionMode Mode { get; set; }
        public string ImagePath { get; set; }
        public int ImageFitIndex { get; set; }
        public int ImageRotationIndex { get; set; }
        public double ImageScale { get; set; }
        public double ImageOffsetX { get; set; }
        public double ImageOffsetY { get; set; }
        public string VideoPath { get; set; }
        public int VideoFitIndex { get; set; }
        public int VideoRotationIndex { get; set; }
        public int VideoFpsIndex { get; set; }
        public bool VideoLoop { get; set; }
        public double VideoScale { get; set; }
        public double VideoOffsetX { get; set; }
        public double VideoOffsetY { get; set; }
    }

    internal static class OutputSessionStore
    {
        public static string DefaultPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                    "TurzxControl", "last-output.xml");
            }
        }

        public static OutputSessionSettings LoadOrDefault()
        {
            if (!File.Exists(DefaultPath)) return new OutputSessionSettings();
            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(OutputSessionSettings));
                using (FileStream stream = File.OpenRead(DefaultPath))
                    return (OutputSessionSettings)serializer.Deserialize(stream);
            }
            catch
            {
                return new OutputSessionSettings();
            }
        }

        public static void Save(OutputSessionSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            string directory = Path.GetDirectoryName(DefaultPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporaryPath = DefaultPath + ".tmp";
            XmlSerializer serializer = new XmlSerializer(typeof(OutputSessionSettings));
            using (FileStream stream = File.Create(temporaryPath))
                serializer.Serialize(stream, settings);
            File.Copy(temporaryPath, DefaultPath, true);
            File.Delete(temporaryPath);
        }
    }
}
