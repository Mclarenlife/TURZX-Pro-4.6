using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Xml.Serialization;

namespace Turzx.Desktop.Models
{
    public enum EditorComponentKind
    {
        Text,
        Time,
        Weather,
        Sensor,
        Progress,
        Image,
        Video
    }

    [Serializable]
    public sealed class EditorComponent : INotifyPropertyChanged
    {
        private string _name;
        private string _text;
        private string _sensorId;
        private string _metricKey;
        private string _sensorValue;
        private string _weatherValue;
        private double _x;
        private double _y;
        private double _width;
        private double _height;
        private double _fontSize;
        private string _foreground;
        private string _accent;
        private string _fontFamilyName;
        private string _horizontalTextAlignment;
        private string _verticalTextAlignment;
        private double _paddingLeft;
        private double _paddingTop;
        private double _paddingRight;
        private double _paddingBottom;
        private string _background;
        private double _backgroundOpacity;
        private string _borderColor;
        private double _borderOpacity;
        private double _borderThickness;
        private double _cornerRadius;
        private bool _visible;
        private bool _showLabel;
        private bool _weatherShowIcon;
        private bool _weatherShowLocation;
        private bool _weatherShowTemperature;
        private bool _weatherShowCondition;
        private bool _weatherShowSeparator;
        private int _valueDecimalPlaces;
        private double _progressThickness;
        private double _progressLength;
        private string _progressStyle;
        private string _progressTrackColor;
        private string _mediaPath;

        public EditorComponent()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = "新组件";
            Text = "文本";
            Width = 220;
            Height = 72;
            FontSize = 28;
            Foreground = "#FFFFFFFF";
            Accent = "#FF4CC2FF";
            FontFamilyName = "Microsoft YaHei UI";
            HorizontalTextAlignment = "Left";
            VerticalTextAlignment = "Center";
            PaddingLeft = 8;
            PaddingTop = 5;
            PaddingRight = 8;
            PaddingBottom = 5;
            Background = "#000000";
            BackgroundOpacity = 0.30;
            BorderColor = "#007AFF";
            BorderOpacity = 0;
            BorderThickness = 2;
            CornerRadius = 9;
            Visible = true;
            ShowLabel = true;
            WeatherShowIcon = true;
            WeatherShowLocation = true;
            WeatherShowTemperature = true;
            WeatherShowCondition = true;
            WeatherShowSeparator = true;
            ValueDecimalPlaces = 1;
            ProgressThickness = 8;
            ProgressLength = 0;
            ProgressStyle = "Rounded";
            ProgressTrackColor = "#33FFFFFF";
        }

        public string Id { get; set; }
        public EditorComponentKind Kind { get; set; }

        public string Name
        {
            get { return _name; }
            set { SetField(ref _name, value, "Name"); }
        }

        public string Text
        {
            get { return _text; }
            set { SetField(ref _text, value, "Text"); }
        }

        public string SensorId
        {
            get { return _sensorId; }
            set { SetField(ref _sensorId, value, "SensorId"); }
        }

        public string MetricKey
        {
            get { return _metricKey; }
            set { SetField(ref _metricKey, value, "MetricKey"); }
        }

        [XmlIgnore]
        public string SensorValue
        {
            get { return _sensorValue; }
            set { SetField(ref _sensorValue, value, "SensorValue"); }
        }

        [XmlIgnore]
        public string WeatherValue
        {
            get { return _weatherValue; }
            set { SetField(ref _weatherValue, value, "WeatherValue"); }
        }

        public double X
        {
            get { return _x; }
            set { SetField(ref _x, value, "X"); }
        }

        public double Y
        {
            get { return _y; }
            set { SetField(ref _y, value, "Y"); }
        }

        public double Width
        {
            get { return _width; }
            set { SetField(ref _width, Math.Max(40, value), "Width"); }
        }

        public double Height
        {
            get { return _height; }
            set { SetField(ref _height, Math.Max(24, value), "Height"); }
        }

        public double FontSize
        {
            get { return _fontSize; }
            set { SetField(ref _fontSize, Math.Max(8, value), "FontSize"); }
        }

        public string Foreground
        {
            get { return _foreground; }
            set { SetField(ref _foreground, value, "Foreground"); }
        }

        public string Accent
        {
            get { return _accent; }
            set { SetField(ref _accent, value, "Accent"); }
        }

        public string FontFamilyName
        {
            get { return _fontFamilyName; }
            set { SetField(ref _fontFamilyName, value, "FontFamilyName"); }
        }

        public string HorizontalTextAlignment
        {
            get { return _horizontalTextAlignment; }
            set { SetField(ref _horizontalTextAlignment, value, "HorizontalTextAlignment"); }
        }

        public string VerticalTextAlignment
        {
            get { return _verticalTextAlignment; }
            set { SetField(ref _verticalTextAlignment, value, "VerticalTextAlignment"); }
        }

        public double PaddingLeft { get { return _paddingLeft; } set { SetField(ref _paddingLeft, Math.Max(0, value), "PaddingLeft"); } }
        public double PaddingTop { get { return _paddingTop; } set { SetField(ref _paddingTop, Math.Max(0, value), "PaddingTop"); } }
        public double PaddingRight { get { return _paddingRight; } set { SetField(ref _paddingRight, Math.Max(0, value), "PaddingRight"); } }
        public double PaddingBottom { get { return _paddingBottom; } set { SetField(ref _paddingBottom, Math.Max(0, value), "PaddingBottom"); } }

        public string Background { get { return _background; } set { SetField(ref _background, value, "Background"); } }
        public double BackgroundOpacity { get { return _backgroundOpacity; } set { SetField(ref _backgroundOpacity, Math.Max(0, Math.Min(1, value)), "BackgroundOpacity"); } }
        public string BorderColor { get { return _borderColor; } set { SetField(ref _borderColor, value, "BorderColor"); } }
        public double BorderOpacity { get { return _borderOpacity; } set { SetField(ref _borderOpacity, Math.Max(0, Math.Min(1, value)), "BorderOpacity"); } }
        public double BorderThickness { get { return _borderThickness; } set { SetField(ref _borderThickness, Math.Max(0, value), "BorderThickness"); } }
        public double CornerRadius { get { return _cornerRadius; } set { SetField(ref _cornerRadius, Math.Max(0, value), "CornerRadius"); } }

        public bool Visible
        {
            get { return _visible; }
            set { SetField(ref _visible, value, "Visible"); }
        }

        public bool ShowLabel { get { return _showLabel; } set { SetField(ref _showLabel, value, "ShowLabel"); } }
        public bool WeatherShowIcon { get { return _weatherShowIcon; } set { SetField(ref _weatherShowIcon, value, "WeatherShowIcon"); } }
        public bool WeatherShowLocation { get { return _weatherShowLocation; } set { SetField(ref _weatherShowLocation, value, "WeatherShowLocation"); } }
        public bool WeatherShowTemperature { get { return _weatherShowTemperature; } set { SetField(ref _weatherShowTemperature, value, "WeatherShowTemperature"); } }
        public bool WeatherShowCondition { get { return _weatherShowCondition; } set { SetField(ref _weatherShowCondition, value, "WeatherShowCondition"); } }
        public bool WeatherShowSeparator { get { return _weatherShowSeparator; } set { SetField(ref _weatherShowSeparator, value, "WeatherShowSeparator"); } }
        public int ValueDecimalPlaces { get { return _valueDecimalPlaces; } set { SetField(ref _valueDecimalPlaces, Math.Max(0, Math.Min(2, value)), "ValueDecimalPlaces"); } }
        public double ProgressThickness { get { return _progressThickness; } set { SetField(ref _progressThickness, Math.Max(2, Math.Min(40, value)), "ProgressThickness"); } }
        public double ProgressLength { get { return _progressLength; } set { SetField(ref _progressLength, value <= 0 ? 0 : Math.Max(20, Math.Min(920, value)), "ProgressLength"); } }
        public string ProgressStyle { get { return _progressStyle; } set { SetField(ref _progressStyle, value, "ProgressStyle"); } }
        public string ProgressTrackColor { get { return _progressTrackColor; } set { SetField(ref _progressTrackColor, value, "ProgressTrackColor"); } }
        public string MediaPath { get { return _mediaPath; } set { SetField(ref _mediaPath, value, "MediaPath"); } }

        public string GetDisplayText(DateTime now)
        {
            if (Kind == EditorComponentKind.Time)
            {
                string format = string.IsNullOrWhiteSpace(Text) ? "HH:mm:ss" : Text;
                try { return now.ToString(format); }
                catch { return now.ToString("HH:mm:ss"); }
            }
            if (Kind == EditorComponentKind.Sensor)
            {
                string value = FormatSensorValue();
                return ShowLabel && !string.IsNullOrWhiteSpace(Text) ?
                    Text.Trim() + "  " + value : value;
            }
            if (Kind == EditorComponentKind.Progress)
            {
                if (!ShowLabel) return string.Empty;
                string value = FormatSensorValue();
                return string.IsNullOrWhiteSpace(Text) ? value :
                    Text.Trim() + "  " + value;
            }
            if (Kind == EditorComponentKind.Weather && !string.IsNullOrWhiteSpace(WeatherValue))
                return WeatherValue;
            return Text ?? string.Empty;
        }

        private string FormatSensorValue()
        {
            string source = string.IsNullOrWhiteSpace(SensorValue) ? "--" : SensorValue.Trim();
            int separator = source.IndexOf(' ');
            string numberText = separator < 0 ? source : source.Substring(0, separator);
            double number;
            if (!double.TryParse(numberText, NumberStyles.Float,
                CultureInfo.CurrentCulture, out number) &&
                !double.TryParse(numberText, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out number))
                return source;
            string suffix = separator < 0 ? string.Empty : source.Substring(separator);
            return number.ToString("F" + ValueDecimalPlaces,
                CultureInfo.CurrentCulture) + suffix;
        }

        [field: NonSerialized]
        public event PropertyChangedEventHandler PropertyChanged;

        private void SetField<T>(ref T field, T value, string propertyName)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }
            field = value;
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }

    public sealed class MonitorMetricDefinition
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string PreferredCategory { get; set; }
        public string PreferredType { get; set; }
        public string DefaultSensorId { get; set; }

        public string DisplayName { get { return Name; } }
    }

    public static class MonitorMetrics
    {
        public static readonly MonitorMetricDefinition[] All =
        {
            Metric("hardware.cpu.name", "CPU 名称", "硬件", "Text", null),
            Metric("hardware.gpu.name", "GPU 名称", "硬件", "Text", null),
            Metric("cpu.usage", "CPU 使用率", "系统", "Load", "os/cpu-total"),
            Metric("cpu.temperature", "CPU 温度", "CPU", "Temperature", null),
            Metric("cpu.power", "CPU 功耗", "CPU", "Power", null),
            Metric("cpu.clock", "CPU 频率", "CPU", "Clock", "os/cpu-clock"),
            Metric("gpu.usage", "GPU 使用率", "GPU", "Load", null),
            Metric("gpu.temperature", "GPU 温度", "GPU", "Temperature", null),
            Metric("gpu.power", "GPU 功耗", "GPU", "Power", null),
            Metric("gpu.clock", "GPU 频率", "GPU", "Clock", null),
            Metric("gpu.vram.usage", "显存使用率", "GPU", "Load", null),
            Metric("memory.usage", "内存使用率", "系统", "Load", "os/memory-used"),
            Metric("memory.clock", "内存频率", "内存", "Clock", "os/memory-clock"),
            Metric("disk.c.usage", "C 盘使用率", "系统", "Load", "os/disk-c-used"),
            Metric("disk.usage", "硬盘总使用率", "系统", "Load", "os/disk-used"),
            Metric("disk.temperature", "磁盘温度", "存储", "Temperature", null),
            Metric("network.download", "网络下载", "网络", "Throughput", "os/network-down"),
            Metric("network.upload", "网络上传", "网络", "Throughput", "os/network-up"),
            Metric("app.memory", "软件后台内存占用率", "系统", "Load", "os/app-memory"),
            Metric("system.uptime", "系统运行时间", "系统", "Time", "os/uptime")
        };

        private static MonitorMetricDefinition Metric(string key, string name,
            string category, string type, string defaultSensorId)
        {
            return new MonitorMetricDefinition
            {
                Key = key,
                Name = name,
                PreferredCategory = category,
                PreferredType = type,
                DefaultSensorId = defaultSensorId
            };
        }

        public static MonitorMetricDefinition Find(string key)
        {
            foreach (MonitorMetricDefinition metric in All)
                if (string.Equals(metric.Key, key, StringComparison.OrdinalIgnoreCase)) return metric;
            return null;
        }
    }

    [Serializable]
    public sealed class SensorMappingEntry
    {
        public string MetricKey { get; set; }
        public string SensorId { get; set; }
    }

    [Serializable]
    public sealed class SensorMappingSettings
    {
        public SensorMappingSettings()
        {
            Mappings = new List<SensorMappingEntry>();
        }

        public List<SensorMappingEntry> Mappings { get; set; }

        public string GetSensorId(string metricKey)
        {
            SensorMappingEntry entry = Mappings.Find(delegate(SensorMappingEntry value)
            {
                return string.Equals(value.MetricKey, metricKey, StringComparison.OrdinalIgnoreCase);
            });
            return entry == null ? null : entry.SensorId;
        }

        public void SetSensorId(string metricKey, string sensorId)
        {
            SensorMappingEntry entry = Mappings.Find(delegate(SensorMappingEntry value)
            {
                return string.Equals(value.MetricKey, metricKey, StringComparison.OrdinalIgnoreCase);
            });
            if (entry == null)
            {
                entry = new SensorMappingEntry { MetricKey = metricKey };
                Mappings.Add(entry);
            }
            entry.SensorId = sensorId;
        }
    }

    [Serializable]
    public sealed class WeatherSettings
    {
        public WeatherSettings()
        {
            City = "上海";
            ResolvedName = "上海";
            Latitude = 31.2304;
            Longitude = 121.4737;
            TemperatureUnit = "Celsius";
            RefreshMinutes = 15;
        }

        public string City { get; set; }
        public string ResolvedName { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string TemperatureUnit { get; set; }
        public int RefreshMinutes { get; set; }
        public int GeocodingVersion { get; set; }
    }

    public sealed class WeatherSnapshot
    {
        public string LocationName { get; set; }
        public double Temperature { get; set; }
        public int WeatherCode { get; set; }
        public string Condition { get; set; }
        public string Unit { get; set; }

        public string DisplayValue
        {
            get { return LocationName + " · " + Temperature.ToString("0.#") + " " + Unit + " · " + Condition; }
        }
    }

    [Serializable]
    public sealed class AiImageSettings
    {
        public AiImageSettings()
        {
            Endpoint = string.Empty;
            Model = string.Empty;
            EncryptedApiKey = string.Empty;
        }

        public string Endpoint { get; set; }
        public string Model { get; set; }
        public string EncryptedApiKey { get; set; }

        [XmlIgnore]
        public string ApiKey { get; set; }
    }

    [Serializable]
    public sealed class ThemeProject
    {
        public ThemeProject()
        {
            Name = "默认监控主题";
            BackgroundKind = "Color";
            BackgroundColor = "#FF08111F";
            Components = new List<EditorComponent>();
        }

        public string Name { get; set; }
        public string BackgroundKind { get; set; }
        public string BackgroundPath { get; set; }
        public string BackgroundColor { get; set; }
        public List<EditorComponent> Components { get; set; }
    }

    public sealed class ThemePresetItem
    {
        public string Name { get; set; }
        public string Path { get; set; }
    }

    public sealed class SensorReading : INotifyPropertyChanged
    {
        private double? _value;

        public string Id { get; set; }
        public string Category { get; set; }
        public string Hardware { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string Unit { get; set; }

        public double? Value
        {
            get { return _value; }
            set
            {
                if (_value == value) { return; }
                _value = value;
                Raise("Value");
                Raise("DisplayValue");
            }
        }

        public string DisplayName
        {
            get { return Category + " · " + Name + "  (" + Hardware + ")"; }
        }

        public string DisplayValue
        {
            get
            {
                return Value.HasValue ? Value.Value.ToString("0.##") +
                    (string.IsNullOrWhiteSpace(Unit) ? string.Empty : " " + Unit) : "--";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
