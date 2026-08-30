using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;
using Turzx.Desktop.Models;

namespace Turzx.Desktop.Services
{
    internal static class AiImageSettingsStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TurzxControl.AiImage.v1");

        public static string DefaultPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                    "TurzxControl", "ai-image.xml");
            }
        }

        public static AiImageSettings LoadOrDefault()
        {
            if (!File.Exists(DefaultPath)) return new AiImageSettings();
            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(AiImageSettings));
                AiImageSettings settings;
                using (FileStream stream = File.OpenRead(DefaultPath))
                    settings = (AiImageSettings)serializer.Deserialize(stream);
                if (!string.IsNullOrWhiteSpace(settings.EncryptedApiKey))
                {
                    byte[] encrypted = Convert.FromBase64String(settings.EncryptedApiKey);
                    byte[] clear = ProtectedData.Unprotect(encrypted, Entropy,
                        DataProtectionScope.CurrentUser);
                    settings.ApiKey = Encoding.UTF8.GetString(clear);
                }
                return settings;
            }
            catch
            {
                return new AiImageSettings();
            }
        }

        public static void Save(AiImageSettings settings)
        {
            string apiKey = settings.ApiKey ?? string.Empty;
            settings.EncryptedApiKey = apiKey.Length == 0 ? string.Empty :
                Convert.ToBase64String(ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(apiKey), Entropy,
                    DataProtectionScope.CurrentUser));
            string directory = Path.GetDirectoryName(DefaultPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            XmlSerializer serializer = new XmlSerializer(typeof(AiImageSettings));
            using (FileStream stream = File.Create(DefaultPath))
                serializer.Serialize(stream, settings);
        }
    }
}
