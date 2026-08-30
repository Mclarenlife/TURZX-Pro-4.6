using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Turzx.ControlPanel
{
    internal static class RoundedGeometry
    {
        public static GraphicsPath Create(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = Math.Max(2, radius * 2);
            Rectangle arc = new Rectangle(bounds.Left, bounds.Top, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal class RoundedPanel : Panel
    {
        private int _cornerRadius = 18;

        public RoundedPanel()
        {
            DoubleBuffered = true;
            BorderStyle = BorderStyle.None;
        }

        public int CornerRadius
        {
            get { return _cornerRadius; }
            set
            {
                _cornerRadius = Math.Max(2, value);
                UpdateRegion();
                Invalidate();
            }
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            UpdateRegion();
        }

        private void UpdateRegion()
        {
            if (Width <= 1 || Height <= 1)
            {
                return;
            }
            using (GraphicsPath path = RoundedGeometry.Create(
                new Rectangle(0, 0, Width, Height), _cornerRadius))
            {
                Region replacement = new Region(path);
                Region old = Region;
                Region = replacement;
                if (old != null)
                {
                    old.Dispose();
                }
            }
        }
    }

    internal sealed class AspectRatioHost : Panel
    {
        private float _targetAspectRatio = 3.0f;

        public float TargetAspectRatio
        {
            get { return _targetAspectRatio; }
            set
            {
                _targetAspectRatio = Math.Max(0.1f, value);
                PerformLayout();
            }
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (Controls.Count == 0 || ClientSize.Width <= 0 || ClientSize.Height <= 0)
            {
                return;
            }
            int width = ClientSize.Width;
            int height = (int)Math.Round(width / _targetAspectRatio);
            if (height > ClientSize.Height)
            {
                height = ClientSize.Height;
                width = (int)Math.Round(height * _targetAspectRatio);
            }
            Controls[0].Bounds = new Rectangle(
                (ClientSize.Width - width) / 2,
                (ClientSize.Height - height) / 2,
                Math.Max(1, width), Math.Max(1, height));
        }
    }

    internal sealed class SmoothImageBox : Control
    {
        private Image _image;
        private int _cornerRadius = 18;

        public SmoothImageBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Black;
        }

        public Image Image
        {
            get { return _image; }
            set
            {
                _image = value;
                Invalidate();
            }
        }

        public int CornerRadius
        {
            get { return _cornerRadius; }
            set
            {
                _cornerRadius = Math.Max(0, value);
                Invalidate();
            }
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            Color outside = Parent == null ? SystemColors.Control : Parent.BackColor;
            pevent.Graphics.Clear(outside);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            Rectangle bounds = new Rectangle(0, 0,
                Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using (GraphicsPath path = RoundedGeometry.Create(bounds,
                Math.Min(_cornerRadius, Math.Max(2, Height / 2))))
            {
                GraphicsState state = e.Graphics.Save();
                e.Graphics.SetClip(path);
                e.Graphics.Clear(BackColor);
                if (_image != null)
                {
                    e.Graphics.DrawImage(_image, ClientRectangle,
                        0, 0, _image.Width, _image.Height, GraphicsUnit.Pixel);
                }
                e.Graphics.Restore(state);
                using (Pen border = new Pen(Color.FromArgb(28, Color.White), 1.0f))
                {
                    e.Graphics.DrawPath(border, path);
                }
            }
        }
    }

    internal class RoundedButton : Control
    {
        private int _cornerRadius = 10;
        private Color _borderColor = Color.Transparent;
        private bool _hovered;
        private bool _pressed;

        public RoundedButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor,
                true);
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
        }

        public Color BorderColor
        {
            get { return _borderColor; }
            set
            {
                _borderColor = value;
                Invalidate();
            }
        }

        public int CornerRadius
        {
            get { return _cornerRadius; }
            set
            {
                _cornerRadius = Math.Max(2, value);
                UpdateRegion();
            }
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            if (Region != null)
            {
                Region old = Region;
                Region = null;
                old.Dispose();
            }
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Region = null;
        }

        protected override void OnBackColorChanged(EventArgs e)
        {
            base.OnBackColorChanged(e);
            if (Parent != null)
            {
                Parent.Invalidate(Bounds, true);
            }
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _pressed = true;
                Focus();
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _pressed = false;
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                OnClick(EventArgs.Empty);
                e.Handled = true;
            }
        }

        private void UpdateRegion()
        {
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            Color background = Parent == null ? SystemColors.Control : Parent.BackColor;
            pevent.Graphics.Clear(background);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            pevent.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            Rectangle bounds = new Rectangle(0, 0,
                Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            Color fillColor = BackColor;
            if (_pressed)
            {
                fillColor = ControlPaint.Dark(fillColor, 0.05f);
            }
            else if (_hovered)
            {
                fillColor = ControlPaint.Light(fillColor, 0.04f);
            }
            using (GraphicsPath path = RoundedGeometry.Create(bounds,
                Math.Min(_cornerRadius, Math.Max(2, Height / 2 - 1))))
            using (Brush fill = new SolidBrush(fillColor))
            {
                pevent.Graphics.FillPath(fill, path);
                if (_borderColor.A > 0 && _borderColor != fillColor)
                {
                    using (Pen border = new Pen(_borderColor, 1.0f))
                    {
                        pevent.Graphics.DrawPath(border, path);
                    }
                }
            }

            Color textColor = Enabled ? ForeColor : Color.FromArgb(142, 142, 147);
            TextRenderer.DrawText(pevent.Graphics, Text, Font, ClientRectangle, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

        }
    }
}
