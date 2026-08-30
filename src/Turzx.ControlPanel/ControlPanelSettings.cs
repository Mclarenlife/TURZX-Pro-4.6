using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace Turzx.ControlPanel
{
    internal sealed class ControlPanelSettings
    {
        private const string AutostartName = "TurzxControlPanel";

        public ThemeMode AppearanceMode { get; set; }
        public int RefreshMilliseconds { get; set; }
        public int BrightnessPercent { get; set; }
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public ImageFitMode ImageFitMode { get; set; }
        public bool ImageBackgroundWhite { get; set; }
        public string LastImageDirectory { get; set; }
        public ImageFitMode VideoFitMode { get; set; }
        public bool VideoBackgroundWhite { get; set; }
        public int VideoFramesPerSecond { get; set; }
        public VideoOrientationMode VideoOrientationMode { get; set; }
        public bool VideoLoop { get; set; }
        public string LastVideoDirectory { get; set; }

        public ControlPanelSettings()
        {
            AppearanceMode = ThemeMode.System;
            RefreshMilliseconds = 1000;
            BrightnessPercent = 40;
            WindowWidth = 1180;
            WindowHeight = 720;
            ImageFitMode = Turzx.ControlPanel.ImageFitMode.Cover;
            ImageBackgroundWhite = false;
            LastImageDirectory = string.Empty;
            VideoFitMode = Turzx.ControlPanel.ImageFitMode.Cover;
            VideoBackgroundWhite = false;
            VideoFramesPerSecond = 5;
            VideoOrientationMode = Turzx.ControlPanel.VideoOrientationMode.Auto;
            VideoLoop = true;
            LastVideoDirectory = string.Empty;
        }

        public ControlPanelSettings Clone()
        {
            return new ControlPanelSettings
            {
                AppearanceMode = AppearanceMode,
                RefreshMilliseconds = RefreshMilliseconds,
                BrightnessPercent = BrightnessPercent,
                WindowWidth = WindowWidth,
                WindowHeight = WindowHeight,
                ImageFitMode = ImageFitMode,
                ImageBackgroundWhite = ImageBackgroundWhite,
                LastImageDirectory = LastImageDirectory,
                VideoFitMode = VideoFitMode,
                VideoBackgroundWhite = VideoBackgroundWhite,
                VideoFramesPerSecond = VideoFramesPerSecond,
                VideoOrientationMode = VideoOrientationMode,
                VideoLoop = VideoLoop,
                LastVideoDirectory = LastVideoDirectory
            };
        }

        public static ControlPanelSettings Load()
        {
            ControlPanelSettings settings = new ControlPanelSettings();
            string path = GetSettingsPath();
            if (!File.Exists(path))
            {
                return settings;
            }

            try
            {
                Dictionary<string, string> values = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (string line in File.ReadAllLines(path))
                {
                    int separator = line.IndexOf('=');
                    if (separator > 0)
                    {
                        values[line.Substring(0, separator).Trim()] =
                            line.Substring(separator + 1).Trim();
                    }
                }

                string text;
                if (values.TryGetValue("ThemeMode", out text))
                {
                    ThemeMode mode;
                    if (Enum.TryParse<ThemeMode>(text, true, out mode))
                    {
                        settings.AppearanceMode = mode;
                    }
                }

                int parsed;
                if (values.TryGetValue("RefreshMilliseconds", out text) &&
                    int.TryParse(text, out parsed))
                {
                    settings.RefreshMilliseconds = Math.Max(500, Math.Min(5000, parsed));
                }
                if (values.TryGetValue("BrightnessPercent", out text) &&
                    int.TryParse(text, out parsed))
                {
                    settings.BrightnessPercent = Math.Max(0, Math.Min(100, parsed));
                }
                if (values.TryGetValue("WindowWidth", out text) && int.TryParse(text, out parsed))
                {
                    settings.WindowWidth = Math.Max(1040, Math.Min(2400, parsed));
                }
                if (values.TryGetValue("WindowHeight", out text) && int.TryParse(text, out parsed))
                {
                    settings.WindowHeight = Math.Max(660, Math.Min(1600, parsed));
                }
                if (values.TryGetValue("ImageFitMode", out text))
                {
                    ImageFitMode fitMode;
                    if (Enum.TryParse<ImageFitMode>(text, true, out fitMode))
                    {
                        settings.ImageFitMode = fitMode;
                    }
                }
                if (values.TryGetValue("ImageBackgroundWhite", out text))
                {
                    bool white;
                    if (bool.TryParse(text, out white))
                    {
                        settings.ImageBackgroundWhite = white;
                    }
                }
                if (values.TryGetValue("LastImageDirectory", out text))
                {
                    settings.LastImageDirectory = text;
                }
                if (values.TryGetValue("VideoFitMode", out text))
                {
                    ImageFitMode fitMode;
                    if (Enum.TryParse<ImageFitMode>(text, true, out fitMode))
                    {
                        settings.VideoFitMode = fitMode;
                    }
                }
                if (values.TryGetValue("VideoBackgroundWhite", out text))
                {
                    bool white;
                    if (bool.TryParse(text, out white))
                    {
                        settings.VideoBackgroundWhite = white;
                    }
                }
                if (values.TryGetValue("VideoFramesPerSecond", out text) &&
                    int.TryParse(text, out parsed))
                {
                    settings.VideoFramesPerSecond = parsed == 2 || parsed == 5 ||
                        parsed == 10 || parsed == 15 || parsed == 20 || parsed == 24 ||
                        parsed == 30 ? parsed : 5;
                }
                if (values.TryGetValue("VideoOrientationMode", out text))
                {
                    VideoOrientationMode orientation;
                    if (Enum.TryParse<VideoOrientationMode>(text, true, out orientation))
                    {
                        settings.VideoOrientationMode = orientation;
                    }
                }
                if (values.TryGetValue("VideoLoop", out text))
                {
                    bool loop;
                    if (bool.TryParse(text, out loop))
                    {
                        settings.VideoLoop = loop;
                    }
                }
                if (values.TryGetValue("LastVideoDirectory", out text))
                {
                    settings.LastVideoDirectory = text;
                }
            }
            catch
            {
            }
            return settings;
        }

        public void CopyFrom(ControlPanelSettings source)
        {
            AppearanceMode = source.AppearanceMode;
            RefreshMilliseconds = source.RefreshMilliseconds;
            BrightnessPercent = source.BrightnessPercent;
            WindowWidth = source.WindowWidth;
            WindowHeight = source.WindowHeight;
            ImageFitMode = source.ImageFitMode;
            ImageBackgroundWhite = source.ImageBackgroundWhite;
            LastImageDirectory = source.LastImageDirectory;
            VideoFitMode = source.VideoFitMode;
            VideoBackgroundWhite = source.VideoBackgroundWhite;
            VideoFramesPerSecond = source.VideoFramesPerSecond;
            VideoOrientationMode = source.VideoOrientationMode;
            VideoLoop = source.VideoLoop;
            LastVideoDirectory = source.LastVideoDirectory;
        }

        public void Save()
        {
            string path = GetSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, new[]
            {
                "ThemeMode=" + AppearanceMode,
                "RefreshMilliseconds=" + RefreshMilliseconds,
                "BrightnessPercent=" + BrightnessPercent,
                "WindowWidth=" + WindowWidth,
                "WindowHeight=" + WindowHeight,
                "ImageFitMode=" + ImageFitMode,
                "ImageBackgroundWhite=" + ImageBackgroundWhite,
                "LastImageDirectory=" + (LastImageDirectory ?? string.Empty),
                "VideoFitMode=" + VideoFitMode,
                "VideoBackgroundWhite=" + VideoBackgroundWhite,
                "VideoFramesPerSecond=" + VideoFramesPerSecond,
                "VideoOrientationMode=" + VideoOrientationMode,
                "VideoLoop=" + VideoLoop,
                "LastVideoDirectory=" + (LastVideoDirectory ?? string.Empty)
            });
        }

        public static bool IsAutostartEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", false))
            {
                return key != null && key.GetValue(AutostartName) != null;
            }
        }

        public static void SetAutostart(bool enabled, string executablePath)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("无法打开当前用户的开机启动设置。");
                }

                if (enabled)
                {
                    key.SetValue(AutostartName, "\"" + executablePath + "\"");
                }
                else
                {
                    key.DeleteValue(AutostartName, false);
                }
            }
        }

        public static string GetSettingsPath()
        {
            return Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData), "TurzxControl", "settings.ini");
        }
    }
}
