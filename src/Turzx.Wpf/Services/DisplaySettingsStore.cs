using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Xml.Serialization;

namespace Turzx.Desktop.Services
{
    public sealed class DisplaySettings
    {
        public DisplaySettings()
        {
            Brightness = 100;
            ColorTemperature = 6500;
        }

        public int Brightness { get; set; }
        public int ColorTemperature { get; set; }
        public int Hue { get; set; }

        public void Normalize()
        {
            Brightness = Math.Max(0, Math.Min(100, Brightness));
            ColorTemperature = Math.Max(3000, Math.Min(9000, ColorTemperature));
            Hue = Math.Max(-180, Math.Min(180, Hue));
        }
    }

    internal static class DisplaySettingsStore
    {
        public static string DefaultPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                    "TurzxControl", "display.xml");
            }
        }

        public static DisplaySettings LoadOrDefault()
        {
            if (!File.Exists(DefaultPath)) return new DisplaySettings();
            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(DisplaySettings));
                using (FileStream stream = File.OpenRead(DefaultPath))
                {
                    DisplaySettings settings = (DisplaySettings)serializer.Deserialize(stream);
                    settings.Normalize();
                    return settings;
                }
            }
            catch
            {
                return new DisplaySettings();
            }
        }

        public static void Save(DisplaySettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            settings.Normalize();
            string directory = Path.GetDirectoryName(DefaultPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            XmlSerializer serializer = new XmlSerializer(typeof(DisplaySettings));
            using (FileStream stream = File.Create(DefaultPath))
                serializer.Serialize(stream, settings);
        }
    }

    internal static class DisplayColorProcessor
    {
        public static byte[] Apply(byte[] png, int colorTemperature, int hue)
        {
            colorTemperature = Math.Max(3000, Math.Min(9000, colorTemperature));
            hue = Math.Max(-180, Math.Min(180, hue));
            if (colorTemperature == 6500 && hue == 0) return png;

            using (MemoryStream input = new MemoryStream(png, false))
            using (Image source = Image.FromStream(input))
            using (Bitmap output = new Bitmap(source.Width, source.Height,
                PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(output))
            using (ImageAttributes attributes = new ImageAttributes())
            {
                float redScale;
                float greenScale;
                float blueScale;
                GetTemperatureScales(colorTemperature,
                    out redScale, out greenScale, out blueScale);

                float radians = (float)(hue * Math.PI / 180.0);
                float cosine = (float)Math.Cos(radians);
                float sine = (float)Math.Sin(radians);
                const float lumR = 0.213f;
                const float lumG = 0.715f;
                const float lumB = 0.072f;

                ColorMatrix matrix = new ColorMatrix(new[]
                {
                    new[] { redScale * (lumR + cosine * (1f - lumR) + sine * -lumR),
                        redScale * (lumG + cosine * -lumG + sine * -lumG),
                        redScale * (lumB + cosine * -lumB + sine * (1f - lumB)), 0f, 0f },
                    new[] { greenScale * (lumR + cosine * -lumR + sine * 0.143f),
                        greenScale * (lumG + cosine * (1f - lumG) + sine * 0.140f),
                        greenScale * (lumB + cosine * -lumB + sine * -0.283f), 0f, 0f },
                    new[] { blueScale * (lumR + cosine * -lumR + sine * -(1f - lumR)),
                        blueScale * (lumG + cosine * -lumG + sine * lumG),
                        blueScale * (lumB + cosine * (1f - lumB) + sine * lumB), 0f, 0f },
                    new[] { 0f, 0f, 0f, 1f, 0f },
                    new[] { 0f, 0f, 0f, 0f, 1f }
                });

                attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default,
                    ColorAdjustType.Bitmap);
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.DrawImage(source,
                    new Rectangle(0, 0, output.Width, output.Height),
                    0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);

                using (MemoryStream result = new MemoryStream())
                {
                    output.Save(result, ImageFormat.Png);
                    return result.ToArray();
                }
            }
        }

        private static void GetTemperatureScales(int kelvin,
            out float redScale, out float greenScale, out float blueScale)
        {
            double targetRed;
            double targetGreen;
            double targetBlue;
            double referenceRed;
            double referenceGreen;
            double referenceBlue;
            KelvinToRgb(kelvin, out targetRed, out targetGreen, out targetBlue);
            KelvinToRgb(6500, out referenceRed, out referenceGreen, out referenceBlue);
            redScale = ClampScale(targetRed / referenceRed);
            greenScale = ClampScale(targetGreen / referenceGreen);
            blueScale = ClampScale(targetBlue / referenceBlue);
        }

        private static float ClampScale(double value)
        {
            return (float)Math.Max(0.35, Math.Min(1.65, value));
        }

        private static void KelvinToRgb(double kelvin,
            out double red, out double green, out double blue)
        {
            double value = kelvin / 100.0;
            if (value <= 66.0)
            {
                red = 255.0;
                green = 99.4708025861 * Math.Log(value) - 161.1195681661;
                blue = value <= 19.0 ? 0.0 :
                    138.5177312231 * Math.Log(value - 10.0) - 305.0447927307;
            }
            else
            {
                red = 329.698727446 * Math.Pow(value - 60.0, -0.1332047592);
                green = 288.1221695283 * Math.Pow(value - 60.0, -0.0755148492);
                blue = 255.0;
            }
            red = Math.Max(0.0, Math.Min(255.0, red));
            green = Math.Max(0.0, Math.Min(255.0, green));
            blue = Math.Max(0.0, Math.Min(255.0, blue));
        }
    }
}
