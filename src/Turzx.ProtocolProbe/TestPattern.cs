using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

namespace Turzx.ProtocolProbe
{
    internal static class TestPattern
    {
        private const int LandscapeWidth = 960;
        private const int LandscapeHeight = 320;

        public static byte[] CreateLandscapePng(int frameIndex, int frameCount)
        {
            if (frameCount < 1)
            {
                throw new ArgumentOutOfRangeException("frameCount");
            }

            if (frameIndex < 0 || frameIndex >= frameCount)
            {
                throw new ArgumentOutOfRangeException("frameIndex");
            }

            using (Bitmap bitmap = new Bitmap(
                LandscapeWidth, LandscapeHeight, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                DrawLandscapeBackground(graphics);
                DrawLandscapeHeader(graphics);
                DrawStatusCard(graphics, 24, 92, 280, 142,
                    "USB LINK", "CONNECTED", Color.FromArgb(46, 230, 166));
                DrawStatusCard(graphics, 324, 92, 280, 142,
                    "LOGICAL PANEL", "960 x 320", Color.FromArgb(66, 172, 255));
                DrawStatusCard(graphics, 624, 92, 312, 142,
                    "MOTION FRAME", string.Format("{0:00} / {1:00}", frameIndex + 1, frameCount),
                    Color.FromArgb(139, 92, 246));
                DrawLandscapeProgress(graphics, frameIndex, frameCount);

                // TURZX USB transports every image as a 320x960 portrait PNG.
                // This panel is physically mounted in reverse-landscape, so rotate
                // the logical canvas 90 degrees. Relative to normal landscape this
                // turns the displayed result by 180 degrees.
                bitmap.RotateFlip(RotateFlipType.Rotate90FlipNone);
                using (MemoryStream stream = new MemoryStream())
                {
                    bitmap.Save(stream, ImageFormat.Png);
                    return stream.ToArray();
                }
            }
        }

        public static byte[] CreatePng(int width, int height)
        {
            // The panel decoder expects an 8-bit RGBA PNG (PNG color type 6).
            // A 24-bit RGB PNG is accepted at the USB command layer but can
            // corrupt the display output on firmware turzx_0001_0021.
            using (Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                Rectangle bounds = new Rectangle(0, 0, width, height);
                using (LinearGradientBrush background = new LinearGradientBrush(
                    bounds, Color.FromArgb(8, 15, 35), Color.FromArgb(20, 62, 86), 90.0f))
                {
                    graphics.FillRectangle(background, bounds);
                }

                DrawHeader(graphics, width);
                DrawStatusCard(graphics, 24, 190, width - 48, 180,
                    "USB LINK", "CONNECTED", Color.FromArgb(46, 230, 166));
                DrawStatusCard(graphics, 24, 398, width - 48, 180,
                    "PANEL", "320 x 960", Color.FromArgb(66, 172, 255));
                DrawColorBars(graphics, width, 620);

                using (Font footerFont = new Font("Segoe UI", 11.0f, FontStyle.Regular, GraphicsUnit.Point))
                using (SolidBrush footerBrush = new SolidBrush(Color.FromArgb(185, 210, 220)))
                {
                    string footer = "Independent WinUSB test\n" +
                        DateTime.Now.ToString("yyyy-MM-dd  HH:mm:ss");
                    graphics.DrawString(footer, footerFont, footerBrush,
                        new RectangleF(24, 850, width - 48, 70));
                }

                using (MemoryStream stream = new MemoryStream())
                {
                    bitmap.Save(stream, ImageFormat.Png);
                    return stream.ToArray();
                }
            }
        }

        private static void DrawLandscapeBackground(Graphics graphics)
        {
            Rectangle bounds = new Rectangle(0, 0, LandscapeWidth, LandscapeHeight);
            using (LinearGradientBrush background = new LinearGradientBrush(
                bounds, Color.FromArgb(8, 15, 35), Color.FromArgb(20, 62, 86), 0.0f))
            {
                graphics.FillRectangle(background, bounds);
            }

            using (Pen grid = new Pen(Color.FromArgb(14, 255, 255, 255), 1.0f))
            {
                for (int x = 0; x < LandscapeWidth; x += 40)
                {
                    graphics.DrawLine(grid, x, 0, x, LandscapeHeight);
                }
                for (int y = 0; y < LandscapeHeight; y += 40)
                {
                    graphics.DrawLine(grid, 0, y, LandscapeWidth, y);
                }
            }
        }

        private static void DrawLandscapeHeader(Graphics graphics)
        {
            using (Font titleFont = new Font("Segoe UI", 25.0f, FontStyle.Bold, GraphicsUnit.Point))
            using (Font subtitleFont = new Font("Segoe UI", 11.0f, FontStyle.Regular, GraphicsUnit.Point))
            using (SolidBrush white = new SolidBrush(Color.White))
            using (SolidBrush muted = new SolidBrush(Color.FromArgb(178, 205, 218)))
            {
                graphics.DrawString("TURZX", titleFont, white,
                    new RectangleF(24, 18, 180, 52));
                graphics.DrawString("LANDSCAPE PROTOCOL TEST", subtitleFont, muted,
                    new RectangleF(205, 34, 310, 28));
                graphics.DrawString(DateTime.Now.ToString("yyyy-MM-dd  HH:mm:ss"), subtitleFont, muted,
                    new RectangleF(700, 34, 236, 28));
            }

            using (Pen accent = new Pen(Color.FromArgb(46, 230, 166), 3.0f))
            {
                graphics.DrawLine(accent, 24, 76, 936, 76);
            }
        }

        private static void DrawLandscapeProgress(Graphics graphics, int frameIndex, int frameCount)
        {
            const int x = 24;
            const int y = 261;
            const int width = 912;
            const int height = 28;
            float progress = (frameIndex + 1.0f) / frameCount;
            int fillWidth = Math.Max(8, (int)(width * progress));

            using (SolidBrush track = new SolidBrush(Color.FromArgb(55, 255, 255, 255)))
            using (LinearGradientBrush fill = new LinearGradientBrush(
                new Rectangle(x, y, width, height),
                Color.FromArgb(46, 230, 166), Color.FromArgb(66, 172, 255), 0.0f))
            using (Pen marker = new Pen(Color.White, 3.0f))
            {
                graphics.FillRectangle(track, x, y, width, height);
                graphics.FillRectangle(fill, x, y, fillWidth, height);
                int markerX = x + fillWidth - 2;
                graphics.DrawLine(marker, markerX, y - 4, markerX, y + height + 4);
            }
        }

        private static void DrawHeader(Graphics graphics, int width)
        {
            using (Font titleFont = new Font("Segoe UI", 29.0f, FontStyle.Bold, GraphicsUnit.Point))
            using (Font subtitleFont = new Font("Segoe UI", 12.0f, FontStyle.Regular, GraphicsUnit.Point))
            using (SolidBrush white = new SolidBrush(Color.White))
            using (SolidBrush muted = new SolidBrush(Color.FromArgb(178, 205, 218)))
            {
                graphics.DrawString("TURZX", titleFont, white,
                    new RectangleF(22, 40, width - 44, 55));
                graphics.DrawString("PROTOCOL PROBE  /  4.6 INCH", subtitleFont, muted,
                    new RectangleF(25, 105, width - 50, 30));
            }

            using (Pen accent = new Pen(Color.FromArgb(46, 230, 166), 4.0f))
            {
                graphics.DrawLine(accent, 25, 150, width - 25, 150);
            }
        }

        private static void DrawStatusCard(Graphics graphics, int x, int y, int width,
            int height, string label, string value, Color accent)
        {
            Rectangle card = new Rectangle(x, y, width, height);
            using (SolidBrush cardBrush = new SolidBrush(Color.FromArgb(34, 255, 255, 255)))
            using (Pen border = new Pen(Color.FromArgb(45, 255, 255, 255), 1.0f))
            {
                graphics.FillRectangle(cardBrush, card);
                graphics.DrawRectangle(border, card);
            }

            using (SolidBrush accentBrush = new SolidBrush(accent))
            using (SolidBrush white = new SolidBrush(Color.White))
            using (SolidBrush muted = new SolidBrush(Color.FromArgb(170, 199, 212)))
            using (Font labelFont = new Font("Segoe UI", 11.0f, FontStyle.Bold, GraphicsUnit.Point))
            using (Font valueFont = new Font("Segoe UI", 24.0f, FontStyle.Bold, GraphicsUnit.Point))
            {
                graphics.FillRectangle(accentBrush, x, y, 7, height);
                graphics.DrawString(label, labelFont, muted,
                    new RectangleF(x + 25, y + 24, width - 45, 30));
                graphics.DrawString(value, valueFont, white,
                    new RectangleF(x + 22, y + 72, width - 42, 55));
            }
        }

        private static void DrawColorBars(Graphics graphics, int width, int y)
        {
            Color[] colors =
            {
                Color.FromArgb(46, 230, 166),
                Color.FromArgb(66, 172, 255),
                Color.FromArgb(139, 92, 246),
                Color.FromArgb(244, 114, 182),
                Color.FromArgb(251, 191, 36)
            };

            int gap = 8;
            int left = 24;
            int available = width - 48 - (colors.Length - 1) * gap;
            int barWidth = available / colors.Length;
            for (int index = 0; index < colors.Length; index++)
            {
                using (SolidBrush brush = new SolidBrush(colors[index]))
                {
                    graphics.FillRectangle(brush,
                        left + index * (barWidth + gap), y, barWidth, 105);
                }
            }
        }
    }
}
