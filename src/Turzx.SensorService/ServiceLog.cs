using System;
using System.IO;

namespace Turzx.SensorBridge.Service
{
    internal static class ServiceLog
    {
        private static readonly object Sync = new object();

        public static string DirectoryPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData), "TurzxControl");
            }
        }

        public static string RuntimeLogPath
        {
            get { return Path.Combine(DirectoryPath, "sensor-service.log"); }
        }

        public static string InstallLogPath
        {
            get { return Path.Combine(DirectoryPath, "sensor-service-install.log"); }
        }

        public static void Write(string message, Exception exception)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(DirectoryPath);
                    File.AppendAllText(RuntimeLogPath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message +
                        (exception == null ? string.Empty : Environment.NewLine + exception) +
                        Environment.NewLine);
                }
            }
            catch
            {
            }
        }

        public static void WriteInstall(string message)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                File.AppendAllText(InstallLogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message +
                    Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
