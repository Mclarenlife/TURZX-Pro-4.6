using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Turzx.Desktop.Models;

namespace Turzx.Desktop
{
    public partial class ComponentCustomizerWindow : Window
    {
        private readonly EditorComponent _component;
        private string _progressColor;
        private string _progressTrackColor;

        public ComponentCustomizerWindow(EditorComponent component)
        {
            if (component == null) throw new ArgumentNullException("component");
            _component = component;
            InitializeComponent();
            LoadValues();
        }

        private void LoadValues()
        {
            ComponentNameText.Text = _component.Name;
            TextContentText.Text = _component.Text;
            TimeFormatCombo.Text = string.IsNullOrWhiteSpace(_component.Text) ? "HH:mm:ss" : _component.Text;
            WeatherIconCheck.IsChecked = _component.WeatherShowIcon;
            WeatherLocationCheck.IsChecked = _component.WeatherShowLocation;
            WeatherTemperatureCheck.IsChecked = _component.WeatherShowTemperature;
            WeatherConditionCheck.IsChecked = _component.WeatherShowCondition;
            WeatherSeparatorCheck.IsChecked = _component.WeatherShowSeparator;
            SensorLabelCheck.IsChecked = _component.ShowLabel;
            SelectComboByTag(SensorDecimalCombo, _component.ValueDecimalPlaces.ToString());
            ProgressLabelCheck.IsChecked = _component.ShowLabel;
            SelectComboByTag(ProgressDecimalCombo, _component.ValueDecimalPlaces.ToString());
            ProgressWidthText.Text = (_component.ProgressLength > 0 ? _component.ProgressLength :
                Math.Max(20, _component.Width - _component.PaddingLeft - _component.PaddingRight -
                    _component.BorderThickness * 2)).ToString("0");
            ProgressThicknessText.Text = _component.ProgressThickness.ToString("0.#");
            SelectComboByTag(ProgressStyleCombo, string.IsNullOrWhiteSpace(_component.ProgressStyle) ? "Rounded" : _component.ProgressStyle);
            _progressColor = string.IsNullOrWhiteSpace(_component.Accent) ? "#FF4CC2FF" : _component.Accent;
            _progressTrackColor = string.IsNullOrWhiteSpace(_component.ProgressTrackColor) ? "#33FFFFFF" : _component.ProgressTrackColor;
            UpdateColorSwatches();

            TextSection.Visibility = _component.Kind == EditorComponentKind.Text ? Visibility.Visible : Visibility.Collapsed;
            TimeSection.Visibility = _component.Kind == EditorComponentKind.Time ? Visibility.Visible : Visibility.Collapsed;
            WeatherSection.Visibility = _component.Kind == EditorComponentKind.Weather ? Visibility.Visible : Visibility.Collapsed;
            SensorSection.Visibility = _component.Kind == EditorComponentKind.Sensor ? Visibility.Visible : Visibility.Collapsed;
            ProgressSection.Visibility = _component.Kind == EditorComponentKind.Progress ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            _component.Name = string.IsNullOrWhiteSpace(ComponentNameText.Text) ? KindName(_component.Kind) : ComponentNameText.Text.Trim();
            if (_component.Kind == EditorComponentKind.Text)
                _component.Text = TextContentText.Text ?? string.Empty;
            else if (_component.Kind == EditorComponentKind.Time)
                _component.Text = string.IsNullOrWhiteSpace(TimeFormatCombo.Text) ? "HH:mm:ss" : TimeFormatCombo.Text.Trim();
            else if (_component.Kind == EditorComponentKind.Weather)
            {
                _component.WeatherShowIcon = WeatherIconCheck.IsChecked == true;
                _component.WeatherShowLocation = WeatherLocationCheck.IsChecked == true;
                _component.WeatherShowTemperature = WeatherTemperatureCheck.IsChecked == true;
                _component.WeatherShowCondition = WeatherConditionCheck.IsChecked == true;
                _component.WeatherShowSeparator = WeatherSeparatorCheck.IsChecked == true;
                if (!_component.WeatherShowIcon && !_component.WeatherShowLocation &&
                    !_component.WeatherShowTemperature && !_component.WeatherShowCondition)
                    _component.WeatherShowTemperature = true;
            }
            else if (_component.Kind == EditorComponentKind.Sensor)
            {
                _component.ShowLabel = SensorLabelCheck.IsChecked == true;
                _component.ValueDecimalPlaces = ParseIntegerTag(SensorDecimalCombo,
                    _component.ValueDecimalPlaces);
            }
            else if (_component.Kind == EditorComponentKind.Progress)
            {
                _component.ShowLabel = ProgressLabelCheck.IsChecked == true;
                _component.ValueDecimalPlaces = ParseIntegerTag(ProgressDecimalCombo,
                    _component.ValueDecimalPlaces);
                _component.ProgressLength = ParseNumber(ProgressWidthText.Text,
                    _component.ProgressLength > 0 ? _component.ProgressLength : 200, 20, 920);
                _component.ProgressThickness = ParseNumber(ProgressThicknessText.Text, _component.ProgressThickness, 2, 40);
                _component.ProgressStyle = GetSelectedTag(ProgressStyleCombo, "Rounded");
                _component.Accent = _progressColor;
                _component.ProgressTrackColor = _progressTrackColor;
            }
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void ProgressColor_Click(object sender, RoutedEventArgs e)
        {
            _progressColor = PickColor(_progressColor, false);
            UpdateColorSwatches();
        }

        private void ProgressTrackColor_Click(object sender, RoutedEventArgs e)
        {
            _progressTrackColor = PickColor(_progressTrackColor, true);
            UpdateColorSwatches();
        }

        private string PickColor(string current, bool translucent)
        {
            System.Windows.Media.Color color = ParseColor(current, Colors.White);
            using (System.Windows.Forms.ColorDialog dialog = new System.Windows.Forms.ColorDialog())
            {
                dialog.AnyColor = true;
                dialog.AllowFullOpen = true;
                dialog.FullOpen = true;
                dialog.Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B);
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return current;
                return string.Format(translucent ? "#55{0:X2}{1:X2}{2:X2}" : "#FF{0:X2}{1:X2}{2:X2}",
                    dialog.Color.R, dialog.Color.G, dialog.Color.B);
            }
        }

        private void UpdateColorSwatches()
        {
            ProgressColorSwatch.Background = new SolidColorBrush(ParseColor(_progressColor, Colors.DeepSkyBlue));
            ProgressTrackColorSwatch.Background = new SolidColorBrush(ParseColor(_progressTrackColor, Colors.Gray));
        }

        private static System.Windows.Media.Color ParseColor(string value, System.Windows.Media.Color fallback)
        {
            try { return (System.Windows.Media.Color)ColorConverter.ConvertFromString(value); }
            catch { return fallback; }
        }

        private static double ParseNumber(string text, double fallback, double minimum, double maximum)
        {
            double value;
            return double.TryParse(text, out value) ? Math.Max(minimum, Math.Min(maximum, value)) : fallback;
        }

        private static void SelectComboByTag(ComboBox combo, string tag)
        {
            combo.SelectedItem = combo.Items.Cast<object>().OfType<ComboBoxItem>().FirstOrDefault(
                delegate(ComboBoxItem item) { return string.Equals(Convert.ToString(item.Tag), tag, StringComparison.OrdinalIgnoreCase); });
        }

        private static string GetSelectedTag(ComboBox combo, string fallback)
        {
            ComboBoxItem item = combo.SelectedItem as ComboBoxItem;
            return item == null ? fallback : Convert.ToString(item.Tag);
        }

        private static int ParseIntegerTag(ComboBox combo, int fallback)
        {
            int value;
            return int.TryParse(GetSelectedTag(combo, fallback.ToString()), out value) ?
                Math.Max(0, Math.Min(2, value)) : fallback;
        }

        private static string KindName(EditorComponentKind kind)
        {
            switch (kind)
            {
                case EditorComponentKind.Text: return "文字组件";
                case EditorComponentKind.Time: return "时间组件";
                case EditorComponentKind.Weather: return "天气组件";
                case EditorComponentKind.Sensor: return "监控数值组件";
                case EditorComponentKind.Progress: return "进度条组件";
                case EditorComponentKind.Image: return "图片组件";
                case EditorComponentKind.Video: return "视频组件";
                default: return "组件";
            }
        }
    }
}
