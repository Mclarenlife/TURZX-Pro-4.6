using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace Turzx.ControlPanel
{
    internal enum ImageFitMode
    {
        Contain,
        Cover,
        Stretch,
        Custom
    }

    internal static class ImageContentRenderer
    {
        public const int LogicalWidth = 960;
        public const int LogicalHeight = 320;

        public static Bitmap LoadOrientedBitmap(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("图片路径不能为空。", "path");
            }
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("找不到所选图片。", path);
            }

            using (Image source = Image.FromFile(path))
            {
                ApplyExifOrientation(source);
                Bitmap copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(copy))
                {
                    graphics.DrawImage(source, new Rectangle(0, 0, copy.Width, copy.Height));
                }
                return copy;
            }
        }

        public static byte[] CreateTransportPng(Image source, ImageFitMode fitMode,
            Color background)
        {
            return CreateTransportPng(source, fitMode, background, RectangleF.Empty);
        }

        public static byte[] CreateTransportPng(Image source, ImageFitMode fitMode,
            Color background, RectangleF normalizedCrop)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }
            if (source.Width <= 0 || source.Height <= 0)
            {
                throw new ArgumentException("图片尺寸无效。", "source");
            }

            using (Bitmap logical = new Bitmap(
                LogicalWidth, LogicalHeight, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(logical))
            {
                graphics.Clear(background);
                graphics.CompositingMode = CompositingMode.SourceOver;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.SmoothingMode = SmoothingMode.HighQuality;

                RectangleF destination = CalculateDestination(source.Size, fitMode);
                RectangleF sourceRectangle = new RectangleF(0, 0, source.Width, source.Height);
                if (fitMode == ImageFitMode.Custom && normalizedCrop.Width > 0 &&
                    normalizedCrop.Height > 0)
                {
                    destination = new RectangleF(0, 0, LogicalWidth, LogicalHeight);
                    sourceRectangle = new RectangleF(
                        normalizedCrop.X * source.Width,
                        normalizedCrop.Y * source.Height,
                        normalizedCrop.Width * source.Width,
                        normalizedCrop.Height * source.Height);
                }
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(source, Rectangle.Round(destination),
                        sourceRectangle.X, sourceRectangle.Y,
                        sourceRectangle.Width, sourceRectangle.Height,
                        GraphicsUnit.Pixel, attributes);
                }

                logical.RotateFlip(RotateFlipType.Rotate90FlipNone);
                using (MemoryStream stream = new MemoryStream())
                {
                    logical.Save(stream, ImageFormat.Png);
                    return stream.ToArray();
                }
            }
        }

        public static Bitmap CreateLogicalPreview(byte[] transportPng)
        {
            if (transportPng == null || transportPng.Length == 0)
            {
                throw new ArgumentException("预览数据为空。", "transportPng");
            }

            using (MemoryStream stream = new MemoryStream(transportPng))
            using (Image image = Image.FromStream(stream))
            {
                Bitmap preview = new Bitmap(image);
                preview.RotateFlip(RotateFlipType.Rotate270FlipNone);
                return preview;
            }
        }

        private static RectangleF CalculateDestination(Size sourceSize, ImageFitMode fitMode)
        {
            if (fitMode == ImageFitMode.Stretch || fitMode == ImageFitMode.Custom)
            {
                return new RectangleF(0, 0, LogicalWidth, LogicalHeight);
            }

            float scaleX = LogicalWidth / (float)sourceSize.Width;
            float scaleY = LogicalHeight / (float)sourceSize.Height;
            float scale = fitMode == ImageFitMode.Cover ?
                Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);
            float width = sourceSize.Width * scale;
            float height = sourceSize.Height * scale;
            return new RectangleF(
                (LogicalWidth - width) / 2.0f,
                (LogicalHeight - height) / 2.0f,
                width,
                height);
        }

        private static void ApplyExifOrientation(Image image)
        {
            const int orientationId = 0x0112;
            try
            {
                PropertyItem orientation = image.GetPropertyItem(orientationId);
                if (orientation == null || orientation.Value == null || orientation.Value.Length == 0)
                {
                    return;
                }

                switch (orientation.Value[0])
                {
                    case 2:
                        image.RotateFlip(RotateFlipType.RotateNoneFlipX);
                        break;
                    case 3:
                        image.RotateFlip(RotateFlipType.Rotate180FlipNone);
                        break;
                    case 4:
                        image.RotateFlip(RotateFlipType.Rotate180FlipX);
                        break;
                    case 5:
                        image.RotateFlip(RotateFlipType.Rotate90FlipX);
                        break;
                    case 6:
                        image.RotateFlip(RotateFlipType.Rotate90FlipNone);
                        break;
                    case 7:
                        image.RotateFlip(RotateFlipType.Rotate270FlipX);
                        break;
                    case 8:
                        image.RotateFlip(RotateFlipType.Rotate270FlipNone);
                        break;
                }
            }
            catch (ArgumentException)
            {
            }
        }
    }
}
