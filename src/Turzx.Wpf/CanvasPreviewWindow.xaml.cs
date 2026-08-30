using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Turzx.Desktop.Models;

namespace Turzx.Desktop
{
    public partial class CanvasPreviewWindow : Window
    {
        private readonly VisualBrush _previewBrush;
        private readonly IList<EditorComponent> _components;
        private readonly Action<EditorComponent> _selectComponent;
        private readonly Action<EditorComponent> _componentMoved;
        private readonly Dictionary<string, Border> _overlays =
            new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
        private Border _dragOverlay;
        private EditorComponent _dragComponent;
        private Point _dragOffset;

        public CanvasPreviewWindow(Visual source, IList<EditorComponent> components,
            Action<EditorComponent> selectComponent,
            Action<EditorComponent> componentMoved)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (components == null) throw new ArgumentNullException("components");
            InitializeComponent();
            _components = components;
            _selectComponent = selectComponent;
            _componentMoved = componentMoved;
            _previewBrush = new VisualBrush(source)
            {
                Stretch = Stretch.Fill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };
            PreviewSurface.Fill = _previewBrush;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ApplySelectedSize();
            RefreshOverlays(null);
        }

        public void RefreshOverlays(EditorComponent selectedComponent)
        {
            if (InteractionCanvas == null) return;
            HashSet<string> activeIds = new HashSet<string>(
                _components.Select(delegate(EditorComponent component) { return component.Id; }),
                StringComparer.OrdinalIgnoreCase);
            foreach (string id in _overlays.Keys.Where(delegate(string value)
            {
                return !activeIds.Contains(value);
            }).ToList())
            {
                InteractionCanvas.Children.Remove(_overlays[id]);
                _overlays.Remove(id);
            }

            foreach (EditorComponent component in _components)
            {
                Border overlay;
                if (!_overlays.TryGetValue(component.Id, out overlay))
                {
                    overlay = CreateOverlay(component);
                    _overlays[component.Id] = overlay;
                    InteractionCanvas.Children.Add(overlay);
                }
                overlay.Width = component.Width;
                overlay.Height = component.Height;
                overlay.Visibility = component.Visible ? Visibility.Visible : Visibility.Hidden;
                Canvas.SetLeft(overlay, component.X);
                Canvas.SetTop(overlay, component.Y);
                Panel.SetZIndex(overlay, _components.IndexOf(component));
                bool selected = component == selectedComponent;
                overlay.BorderThickness = new Thickness(selected ? 1.5 : 1);
                overlay.BorderBrush = selected ?
                    new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)) :
                    new SolidColorBrush(Color.FromArgb(35, 255, 255, 255));
            }
        }

        private Border CreateOverlay(EditorComponent component)
        {
            Border overlay = new Border
            {
                Tag = component,
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                Cursor = Cursors.SizeAll,
                CornerRadius = new CornerRadius(Math.Max(0, component.CornerRadius))
            };
            overlay.MouseLeftButtonDown += Overlay_MouseLeftButtonDown;
            overlay.MouseMove += Overlay_MouseMove;
            overlay.MouseLeftButtonUp += Overlay_MouseLeftButtonUp;
            AutomationProperties.SetName(overlay, "放大画布组件：" + component.Name);
            AutomationProperties.SetAutomationId(overlay, "PreviewComponent_" + component.Id);
            return overlay;
        }

        private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Border overlay = sender as Border;
            EditorComponent component = overlay == null ? null : overlay.Tag as EditorComponent;
            if (overlay == null || component == null) return;
            _dragOverlay = overlay;
            _dragComponent = component;
            Point point = e.GetPosition(InteractionCanvas);
            _dragOffset = new Point(point.X - component.X, point.Y - component.Y);
            overlay.CaptureMouse();
            if (_selectComponent != null) _selectComponent(component);
            RefreshOverlays(component);
            e.Handled = true;
        }

        private void Overlay_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragOverlay == null || _dragComponent == null ||
                e.LeftButton != MouseButtonState.Pressed) return;
            Point point = e.GetPosition(InteractionCanvas);
            _dragComponent.X = Math.Max(0, Math.Min(960 - _dragComponent.Width,
                point.X - _dragOffset.X));
            _dragComponent.Y = Math.Max(0, Math.Min(320 - _dragComponent.Height,
                point.Y - _dragOffset.Y));
            Canvas.SetLeft(_dragOverlay, _dragComponent.X);
            Canvas.SetTop(_dragOverlay, _dragComponent.Y);
            if (_componentMoved != null) _componentMoved(_dragComponent);
            e.Handled = true;
        }

        private void Overlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragOverlay != null && _dragOverlay.IsMouseCaptured)
                _dragOverlay.ReleaseMouseCapture();
            _dragOverlay = null;
            _dragComponent = null;
            e.Handled = true;
        }

        private void PreviewSizeCombo_SelectionChanged(object sender,
            SelectionChangedEventArgs e)
        {
            if (PreviewFrame == null) return;
            ApplySelectedSize();
        }

        private void ApplySelectedSize()
        {
            ComboBoxItem selected = PreviewSizeCombo.SelectedItem as ComboBoxItem;
            double scale;
            if (selected == null || !double.TryParse(Convert.ToString(selected.Tag),
                NumberStyles.Float, CultureInfo.InvariantCulture, out scale)) scale = 1.5;
            PreviewFrame.Width = 960 * scale;
            PreviewFrame.Height = 320 * scale;

            Rect workArea = SystemParameters.WorkArea;
            Width = Math.Min(workArea.Width - 36, PreviewFrame.Width + 110);
            Height = Math.Min(workArea.Height - 36, PreviewFrame.Height + 180);
            if (IsLoaded && Owner != null)
            {
                Left = Math.Max(workArea.Left, Math.Min(workArea.Right - Width,
                    Owner.Left + (Owner.ActualWidth - Width) / 2));
                Top = Math.Max(workArea.Top, Math.Min(workArea.Bottom - Height,
                    Owner.Top + (Owner.ActualHeight - Height) / 2));
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            PreviewSurface.Fill = null;
            InteractionCanvas.Children.Clear();
            _overlays.Clear();
        }
    }
}
