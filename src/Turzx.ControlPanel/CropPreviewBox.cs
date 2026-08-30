using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Turzx.ControlPanel
{
    internal sealed class CropPreviewBox : Control
    {
        private const float TargetAspectRatio = 3.0f;
        private Bitmap _image;
        private RectangleF _normalizedCrop;
        private bool _cropEnabled;
        private bool _dragging;
        private Point _dragOrigin;
        private RectangleF _cropAtDragStart;

        public CropPreviewBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Black;
            Cursor = Cursors.Default;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            Color outside = Parent == null ? SystemColors.Control : Parent.BackColor;
            pevent.Graphics.Clear(outside);
        }

        public event EventHandler CropChanged;

        public bool CropEnabled
        {
            get { return _cropEnabled; }
            set
            {
                _cropEnabled = value;
                Cursor = value ? Cursors.SizeAll : Cursors.Default;
                Invalidate();
            }
        }

        public RectangleF NormalizedCrop
        {
            get { return _normalizedCrop; }
        }

        public void SetImage(Image image, bool resetCrop)
        {
            Bitmap replacement = CreatePreviewBitmap(image);
            Bitmap old = _image;
            _image = replacement;
            if (old != null)
            {
                old.Dispose();
            }
            if (resetCrop || _normalizedCrop.Width <= 0 || _normalizedCrop.Height <= 0)
            {
                ResetCrop();
            }
            Invalidate();
        }

        public void SetCrop(RectangleF normalizedCrop)
        {
            if (_image == null || normalizedCrop.Width <= 0 || normalizedCrop.Height <= 0)
            {
                ResetCrop();
                return;
            }
            _normalizedCrop = Clamp(normalizedCrop);
            Invalidate();
        }

        public void ResetCrop()
        {
            if (_image == null || _image.Width <= 0 || _image.Height <= 0)
            {
                _normalizedCrop = RectangleF.Empty;
                return;
            }

            float sourceAspect = _image.Width / (float)_image.Height;
            if (sourceAspect >= TargetAspectRatio)
            {
                float width = TargetAspectRatio / sourceAspect;
                _normalizedCrop = new RectangleF((1.0f - width) / 2.0f, 0, width, 1.0f);
            }
            else
            {
                float height = sourceAspect / TargetAspectRatio;
                _normalizedCrop = new RectangleF(0, (1.0f - height) / 2.0f, 1.0f, height);
            }
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _image != null)
            {
                _image.Dispose();
                _image = null;
            }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.InterpolationMode = _dragging ?
                InterpolationMode.HighQualityBilinear :
                InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            Rectangle bounds = new Rectangle(0, 0,
                Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using (GraphicsPath path = RoundedGeometry.Create(bounds, 12))
            {
                GraphicsState state = e.Graphics.Save();
                e.Graphics.SetClip(path);
                e.Graphics.Clear(BackColor);
                RectangleF destination = new RectangleF(0, 0, Width, Height);
                if (_image != null && _cropEnabled && _normalizedCrop.Width > 0 &&
                    _normalizedCrop.Height > 0)
                {
                    RectangleF source = new RectangleF(
                        _normalizedCrop.X * _image.Width,
                        _normalizedCrop.Y * _image.Height,
                        _normalizedCrop.Width * _image.Width,
                        _normalizedCrop.Height * _image.Height);
                    e.Graphics.DrawImage(_image, destination, source, GraphicsUnit.Pixel);
                }
                else if (_image != null)
                {
                    e.Graphics.DrawImage(_image, destination,
                        new RectangleF(0, 0, _image.Width, _image.Height),
                        GraphicsUnit.Pixel);
                }
                e.Graphics.Restore(state);
                using (Pen border = new Pen(Color.FromArgb(42, Color.White), 1.0f))
                {
                    e.Graphics.DrawPath(border, path);
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!_cropEnabled || _image == null || e.Button != MouseButtons.Left)
            {
                return;
            }
            Focus();
            _dragging = true;
            _dragOrigin = e.Location;
            _cropAtDragStart = _normalizedCrop;
            Capture = true;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (_cropEnabled)
            {
                Focus();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging || _image == null)
            {
                return;
            }
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
            {
                return;
            }
            RectangleF moved = _cropAtDragStart;
            moved.X -= (e.X - _dragOrigin.X) / (float)ClientSize.Width * moved.Width;
            moved.Y -= (e.Y - _dragOrigin.Y) / (float)ClientSize.Height * moved.Height;
            UpdateCrop(Clamp(moved));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Capture = false;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!_cropEnabled || _image == null || _normalizedCrop.Width <= 0)
            {
                return;
            }

            float sourceAspect = _image.Width / (float)_image.Height;
            float normalizedRatio = TargetAspectRatio / sourceAspect;
            float factor = e.Delta > 0 ? 0.90f : 1.10f;
            float relativeX = Math.Max(0, Math.Min(1, e.X / (float)Math.Max(1, Width)));
            float relativeY = Math.Max(0, Math.Min(1, e.Y / (float)Math.Max(1, Height)));
            float anchorX = _normalizedCrop.X + relativeX * _normalizedCrop.Width;
            float anchorY = _normalizedCrop.Y + relativeY * _normalizedCrop.Height;
            float height = _normalizedCrop.Height * factor;
            float maximumHeight = Math.Min(1.0f, 1.0f / normalizedRatio);
            height = Math.Max(0.08f, Math.Min(maximumHeight, height));
            float width = height * normalizedRatio;
            UpdateCrop(Clamp(new RectangleF(
                anchorX - relativeX * width,
                anchorY - relativeY * height,
                width, height)));
        }

        private void UpdateCrop(RectangleF value)
        {
            _normalizedCrop = value;
            Invalidate();
            EventHandler handler = CropChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private static Bitmap CreatePreviewBitmap(Image image)
        {
            if (image == null)
            {
                return null;
            }

            const int maximumWidth = 1920;
            const int maximumHeight = 1080;
            float scale = Math.Min(1.0f, Math.Min(
                maximumWidth / (float)image.Width,
                maximumHeight / (float)image.Height));
            if (scale >= 0.999f)
            {
                return new Bitmap(image);
            }

            int width = Math.Max(1, (int)Math.Round(image.Width * scale));
            int height = Math.Max(1, (int)Math.Round(image.Height * scale));
            Bitmap preview = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(preview))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(image, new Rectangle(0, 0, width, height),
                    0, 0, image.Width, image.Height, GraphicsUnit.Pixel);
            }
            return preview;
        }

        private RectangleF GetImageBounds()
        {
            if (_image == null || ClientSize.Width <= 0 || ClientSize.Height <= 0)
            {
                return RectangleF.Empty;
            }
            float scale = Math.Min(ClientSize.Width / (float)_image.Width,
                ClientSize.Height / (float)_image.Height);
            float width = _image.Width * scale;
            float height = _image.Height * scale;
            return new RectangleF((ClientSize.Width - width) / 2.0f,
                (ClientSize.Height - height) / 2.0f, width, height);
        }

        private static RectangleF ToControlRectangle(RectangleF normalized,
            RectangleF imageBounds)
        {
            return new RectangleF(
                imageBounds.Left + normalized.Left * imageBounds.Width,
                imageBounds.Top + normalized.Top * imageBounds.Height,
                normalized.Width * imageBounds.Width,
                normalized.Height * imageBounds.Height);
        }

        private static RectangleF Clamp(RectangleF value)
        {
            float width = Math.Max(0.001f, Math.Min(1.0f, value.Width));
            float height = Math.Max(0.001f, Math.Min(1.0f, value.Height));
            float x = Math.Max(0, Math.Min(1.0f - width, value.X));
            float y = Math.Max(0, Math.Min(1.0f - height, value.Y));
            return new RectangleF(x, y, width, height);
        }
    }
}
