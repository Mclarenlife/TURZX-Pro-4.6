using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace Turzx.Desktop.Services
{
    internal static class StartupManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "TURZX Screen Control";

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                    {
                        string value = key == null ? null : Convert.ToString(key.GetValue(ValueName));
                        return string.Equals(value, BuildCommand(),
                            StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch { return false; }
            }
        }

        public static void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (key == null) throw new InvalidOperationException("无法打开当前用户的开机启动设置。");
                if (enabled)
                    key.SetValue(ValueName, BuildCommand(), RegistryValueKind.String);
                else
                    key.DeleteValue(ValueName, false);
            }
        }

        private static string BuildCommand()
        {
            string path = Process.GetCurrentProcess().MainModule.FileName;
            return "\"" + path + "\"";
        }
    }
}
