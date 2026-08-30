using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

namespace Turzx.ProtocolProbe
{
    internal static class DashboardRenderer
    {
        private const int Width = 960;
        private const int Height = 320;

        public static byte[] CreateTransportPng(TelemetrySnapshot data, Color accent)
        {
            if (data == null)
            {
                throw new ArgumentNullException("data");
            }

            using (Bitmap bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                DrawBackground(graphics, accent);
                DrawHeader(graphics, data, accent);

                DrawMetricCard(graphics, new Rectangle(22, 82, 174, 168),
                    "CPU", data.CpuPercent.ToString("0") + "%", data.CpuPercent,
                    accent, TemperaturePower(data.CpuTemperatureC, data.CpuPowerWatts));
                DrawMetricCard(graphics, new Rectangle(207, 82, 174, 168),
                    "GPU", PercentValue(data.GpuLoadPercent), data.GpuLoadPercent ?? 0,
                    Color.FromArgb(251, 191, 36),
                    TemperaturePower(data.GpuTemperatureC, data.GpuPowerWatts));
                DrawMetricCard(graphics, new Rectangle(392, 82, 174, 168),
                    "RAM", data.MemoryPercent.ToString("0") + "%", data.MemoryPercent,
                    Color.FromArgb(66, 172, 255),
                    FormatBytes(data.MemoryUsedBytes) + " / " + FormatBytes(data.MemoryTotalBytes));
                DrawMetricCard(graphics, new Rectangle(577, 82, 174, 168),
                    "DISK", data.DiskPercent.ToString("0") + "%", data.DiskPercent,
                    Color.FromArgb(139, 92, 246),
                    FormatBytes(data.DiskUsedBytes) + " used");
                DrawNetworkCard(graphics, new Rectangle(762, 82, 176, 168), data);

                DrawFooter(graphics, data);

                // The installed panel is reverse-landscape. Transport remains
                // a portrait 320x960 RGBA PNG.
                bitmap.RotateFlip(RotateFlipType.Rotate90FlipNone);
                using (MemoryStream stream = new MemoryStream())
                {
                    bitmap.Save(stream, ImageFormat.Png);
                    return stream.ToArray();
                }
            }
        }

        private static void DrawBackground(Graphics graphics, Color accent)
        {
            Rectangle bounds = new Rectangle(0, 0, Width, Height);
            using (LinearGradientBrush background = new LinearGradientBrush(
                bounds, Color.FromArgb(7, 14, 28), Color.FromArgb(15, 42, 58), 0.0f))
            {
                graphics.FillRectangle(background, bounds);
            }

            using (Pen grid = new Pen(Color.FromArgb(10, 255, 255, 255), 1.0f))
            {
                for (int x = 0; x < Width; x += 48)
                {
                    graphics.DrawLine(grid, x, 0, x, Height);
                }
                for (int y = 0; y < Height; y += 40)
                {
                    graphics.DrawLine(grid, 0, y, Width, y);
                }
            }

            using (SolidBrush glow = new SolidBrush(Color.FromArgb(18, accent)))
            {
                graphics.FillEllipse(glow, -110, -160, 480, 350);
            }
        }

        private static void DrawHeader(Graphics graphics, TelemetrySnapshot data, Color accent)
        {
            using (Font titleFont = new Font("Segoe UI", 21.0f, FontStyle.Bold, GraphicsUnit.Point))
            using (Font clockFont = new Font("Segoe UI", 20.0f, FontStyle.Bold, GraphicsUnit.Point))
            using (Font dateFont = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point))
            using (SolidBrush white = new SolidBrush(Color.White))
            using (SolidBrush muted = new SolidBrush(Color.FromArgb(165, 196, 210)))
            using (Pen line = new Pen(accent, 3.0f))
            using (StringFormat right = RightAligned())
            {
                graphics.DrawString("SYSTEM  //  LIVE", titleFont, white,
                    new RectangleF(22, 14, 360, 44));
                graphics.DrawString(data.Timestamp.ToString("HH:mm:ss"), clockFont, white,
                    new RectangleF(785, 12, 153, 42));
                graphics.DrawString(data.Timestamp.ToString("yyyy-MM-dd  dddd"), dateFont, muted,
                    new RectangleF(646, 52, 292, 22), right);
                graphics.DrawLine(line, 22, 68, 938, 68);
            }
        }

        private static void DrawMetricCard(Graphics graphics, Rectangle bounds, string label,
            string value, double percent, Color color, string detail)
        {
            DrawCardBackground(graphics, bounds, color);
            using (Font labelFont = new Font("Segoe UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point))
            using (Font valueFont = new Font("Segoe UI", 29.0f, FontStyle.Bold, GraphicsUnit.Point))
            using (Font detailFont = new Font("Segoe UI", 8.0f, FontStyle.Regular, GraphicsUnit.Point))
            using (SolidBrush white = new SolidBrush(Color.White))
            using (SolidBrush muted = new SolidBrush(Color.FromArgb(155, 187, 201)))
            using (SolidBrush colorBrush = new SolidBrush(color))
            using (SolidBrush track = new SolidBrush(Color.FromArgb(42, 255, 255, 255)))
            {
                graphics.DrawString(label, labelFont, muted,
                    new RectangleF(bounds.X + 18, bounds.Y + 15, bounds.Width - 36, 24));
                graphics.DrawString(value, valueFont, white,
                    new RectangleF(bounds.X + 15, bounds.Y + 48, bounds.Width - 30, 58));
                graphics.DrawString(detail, detailFont, muted,
                    new RectangleF(bounds.X + 18, bounds.Y + 112, bounds.Width - 36, 20));

                Rectangle trackBounds = new Rectangle(bounds.X + 18, bounds.Bottom - 22,
                    bounds.Width - 36, 7);
                graphics.FillRectangle(track, trackBounds);
                int fillWidth = (int)(trackBounds.Width * Math.Max(0, Math.Min(100, percent)) / 100.0);
                if (fillWidth > 0)
                {
                    graphics.FillRectangle(colorBrush,
                        trackBounds.X, trackBounds.Y, fillWidth, trackBounds.Height);
                }
            }
        }

        private static void DrawNetworkCard(Graphics graphics, Rectangle bounds, TelemetrySnapshot data)
        {
            Color color = Color.FromArgb(244, 114, 182);
            DrawCardBackground(graphics, bounds, color);
            using (Font labelFont = new Font("Segoe UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point))
            using (Font valueFont = new Font("Segoe UI", 15.0f, FontStyle.Bold, GraphicsUnit.Point))
            using (Font unitFont = new Font("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point))
            using (SolidBrush white = new SolidBrush(Color.White))
            using (SolidBrush muted = new SolidBrush(Color.FromArgb(155, 187, 201)))
            using (SolidBrush down = new SolidBrush(Color.FromArgb(46, 230, 166)))
            using (SolidBrush up = new SolidBrush(Color.FromArgb(244, 114, 182)))
            using (StringFormat right = RightAligned())
            {
                graphics.DrawString("NETWORK", labelFont, muted,
                    new RectangleF(bounds.X + 18, bounds.Y + 15, bounds.Width - 36, 24));
                graphics.DrawString("DOWN", unitFont, down,
                    new RectangleF(bounds.X + 15, bounds.Y + 51, 45, 18));
                graphics.DrawString(data.NetworkDownloadMbps.ToString("0.00"), valueFont, white,
                    new RectangleF(bounds.X + 52, bounds.Y + 44, 79, 32), right);
                graphics.DrawString("Mb/s", unitFont, muted,
                    new RectangleF(bounds.Right - 42, bounds.Y + 54, 36, 18));
                graphics.DrawString("UP", unitFont, up,
                    new RectangleF(bounds.X + 15, bounds.Y + 103, 45, 18));
                graphics.DrawString(data.NetworkUploadMbps.ToString("0.00"), valueFont, white,
                    new RectangleF(bounds.X + 52, bounds.Y + 96, 79, 32), right);
                graphics.DrawString("Mb/s", unitFont, muted,
                    new RectangleF(bounds.Right - 42, bounds.Y + 106, 36, 18));
            }
        }

        private static void DrawCardBackground(Graphics graphics, Rectangle bounds, Color color)
        {
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(38, 255, 255, 255)))
            using (Pen border = new Pen(Color.FromArgb(48, 255, 255, 255), 1.0f))
            using (SolidBrush edge = new SolidBrush(color))
            {
                graphics.FillRectangle(fill, bounds);
                graphics.DrawRectangle(border, bounds);
                graphics.FillRectangle(edge, bounds.X, bounds.Y, 5, bounds.Height);
            }
        }

        private static void DrawFooter(Graphics graphics, TelemetrySnapshot data)
        {
            using (Font font = new Font("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point))
            using (SolidBrush muted = new SolidBrush(Color.FromArgb(145, 179, 194)))
            using (StringFormat right = RightAligned())
            {
                string uptime = string.Format("UPTIME {0}d {1:00}:{2:00}:{3:00}",
                    (int)data.Uptime.TotalDays, data.Uptime.Hours,
                    data.Uptime.Minutes, data.Uptime.Seconds);
                string fans = "CPU FAN " + FormatFan(data.CpuFanRpm, null) +
                    "   GPU FAN " + FormatFan(data.GpuFanRpm, data.GpuFanPercent);
                graphics.DrawString(uptime + "   " + fans, font, muted,
                    new RectangleF(22, 278, 460, 22));
                string gpu = "GPU  " + TrimName(data.GpuName, 34);
                if (data.GpuMemoryUsedMb.HasValue && data.GpuMemoryTotalMb.HasValue)
                {
                    gpu += string.Format("   VRAM {0:0}/{1:0} MB",
                        data.GpuMemoryUsedMb.Value, data.GpuMemoryTotalMb.Value);
                }
                graphics.DrawString(gpu, font, muted,
                    new RectangleF(470, 278, 468, 22), right);
            }
        }

        private static string PercentValue(double? value)
        {
            return value.HasValue ? value.Value.ToString("0") + "%" : "--";
        }

        private static string TemperaturePower(double? temperature, double? power)
        {
            string temperatureText = temperature.HasValue ?
                temperature.Value.ToString("0") + " C" : "-- C";
            string powerText = power.HasValue ? power.Value.ToString("0") + " W" : "-- W";
            return temperatureText + "   " + powerText;
        }

        private static string FormatFan(double? rpm, double? percent)
        {
            if (rpm.HasValue)
            {
                return rpm.Value.ToString("0") + " RPM";
            }
            if (percent.HasValue)
            {
                return percent.Value.ToString("0") + "%";
            }
            return "--";
        }

        private static StringFormat RightAligned()
        {
            StringFormat format = new StringFormat();
            format.Alignment = StringAlignment.Far;
            format.LineAlignment = StringAlignment.Near;
            return format;
        }

        private static string FormatBytes(ulong bytes)
        {
            const double gib = 1024.0 * 1024.0 * 1024.0;
            if (bytes >= gib)
            {
                return (bytes / gib).ToString("0.0") + " GB";
            }
            return (bytes / (1024.0 * 1024.0)).ToString("0") + " MB";
        }

        private static string TrimName(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "UNKNOWN";
            }
            return value.Length <= maxLength ? value : value.Substring(0, maxLength - 1) + "…";
        }
    }
}
