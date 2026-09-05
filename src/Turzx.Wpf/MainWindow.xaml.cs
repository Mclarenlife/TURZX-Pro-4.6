using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Turzx.ControlPanel;
using Turzx.Desktop.Models;
using Turzx.Desktop.Services;
using Turzx.ProtocolProbe;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Turzx.Desktop
{
    public partial class MainWindow : FluentWindow
    {
        private const ushort VendorId = 0x1CBE;
        private const ushort ProductId = 0x0046;

        private sealed class MediaLibraryItem
        {
            public string Name { get; set; }
            public string FullPath { get; set; }

            public override string ToString()
            {
                return Name ?? string.Empty;
            }
        }

        private readonly ObservableCollection<SensorReading> _sensors =
            new ObservableCollection<SensorReading>();
        private readonly ObservableCollection<EditorComponent> _components =
            new ObservableCollection<EditorComponent>();
        private readonly ObservableCollection<MonitorMetricDefinition> _metrics =
            new ObservableCollection<MonitorMetricDefinition>(MonitorMetrics.All);
        private readonly ObservableCollection<ThemePresetItem> _themePresets =
            new ObservableCollection<ThemePresetItem>();
        private readonly ObservableCollection<MediaLibraryItem> _imageLibraryItems =
            new ObservableCollection<MediaLibraryItem>();
        private readonly ObservableCollection<MediaLibraryItem> _videoLibraryItems =
            new ObservableCollection<MediaLibraryItem>();
        private readonly Dictionary<string, SensorReading> _sensorMap =
            new Dictionary<string, SensorReading>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Border> _componentVisuals =
            new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
        private readonly List<ComboBox> _sensorMappingCombos = new List<ComboBox>();
        private readonly DispatcherTimer _refreshTimer;
        private readonly ListCollectionView _layerView;
        private static readonly object UsbCommandSync = new object();
        private readonly object _displaySettingsSync = new object();

        private SensorCatalogService _sensorService;
        private readonly WeatherService _weatherService;
        private WeatherSettings _weatherSettings;
        private readonly AiImageService _aiImageService;
        private AiImageSettings _aiImageSettings;
        private DisplaySettings _displaySettings;
        private WeatherSnapshot _weatherSnapshot;
        private DateTime _nextWeatherRefresh = DateTime.MinValue;
        private bool _weatherRefreshBusy;
        private ThemeProject _project;
        private EditorComponent _selectedComponent;
        private CanvasPreviewWindow _canvasPreviewWindow;
        private Border _dragVisual;
        private System.Windows.Point _dragOffset;
        private bool _sensorRefreshBusy;
        private bool _propertyLoading;
        private bool _sensorMappingLoading;
        private bool _themePresetLoading;
        private SensorMappingSettings _sensorMappings;
        private string _imagePath;
        private string _videoPath;
        private VideoMetadata _videoMetadata;
        private bool _videoScreenEncoded;
        private CancellationTokenSource _videoCancellation;
        private CancellationTokenSource _themeCancellation;
        private CancellationTokenSource _creativeGenerationCancellation;
        private byte[] _creativeImageBytes;
        private bool _imageDragging;
        private System.Windows.Point _imageDragPoint;
        private bool _videoDragging;
        private System.Windows.Point _videoDragPoint;
        private System.Windows.Controls.TextBox _numericDragTextBox;
        private System.Windows.Point _numericDragStartPoint;
        private double _numericDragStartValue;
        private bool _numericDragActive;
        private System.Windows.Forms.NotifyIcon _trayIcon;
        private System.Drawing.Icon _trayIconImage;
        private bool _allowExit;
        private bool _trayHintShown;
        private bool _startupToggleLoading;
        private bool _mediaLibraryLoading;
        private bool _displaySettingsLoading;
        private readonly bool _startMinimized;
        private Task _startupOutputTask;

        public MainWindow()
        {
            _startMinimized = Environment.GetCommandLineArgs().Any(delegate(string argument)
            {
                return string.Equals(argument, "--startup",
                    StringComparison.OrdinalIgnoreCase);
            });
            if (_startMinimized)
            {
                ShowActivated = false;
                ShowInTaskbar = false;
                WindowState = WindowState.Minimized;
            }
            InitializeComponent();
            ImageLibraryCombo.ItemsSource = _imageLibraryItems;
            VideoLibraryCombo.ItemsSource = _videoLibraryItems;
            RefreshMediaLibraries();
            InitializeTrayIcon();
            LoadStartupSetting();
            _layerView = new ListCollectionView(_components);
            _layerView.CustomSort = new LayerZOrderComparer(_components);
            LayerList.ItemsSource = _layerView;
            PropertyMetric.ItemsSource = _metrics;
            SensorGrid.ItemsSource = _sensors;
            ThemePresetCombo.ItemsSource = _themePresets;
            _sensorMappings = SensorMappingStore.LoadOrDefault();
            _weatherSettings = WeatherSettingsStore.LoadOrDefault();
            _weatherService = new WeatherService();
            _aiImageSettings = AiImageSettingsStore.LoadOrDefault();
            _aiImageService = new AiImageService();
            _displaySettings = DisplaySettingsStore.LoadOrDefault();
            InitializeSensorMappingControls();
            LoadWeatherSettingsIntoUi();
            LoadAiSettingsIntoUi();
            LoadDisplaySettingsIntoUi();
            try
            {
                _project = File.Exists(ThemeProjectStore.DefaultPath) ?
                    ThemeProjectStore.Load(ThemeProjectStore.DefaultPath) : CreateDefaultProject();
            }
            catch
            {
                _project = CreateDefaultProject();
            }
            LoadProjectIntoEditor(_project);
            RefreshThemePresets();
            InitializeAutomations();

            _refreshTimer = new DispatcherTimer();
            _refreshTimer.Interval = TimeSpan.FromSeconds(1);
            _refreshTimer.Tick += RefreshTimer_Tick;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (_startMinimized)
            {
                ShowInTaskbar = false;
                Hide();
            }
            try
            {
                ApplicationThemeManager.ApplySystemTheme();
            }
            catch
            {
            }

            SetDashboardPreview(TestPattern.CreateLandscapePng(0, 1));
            SensorPermissionText.Text = "正在检查后台传感器服务…";
            await RefreshSensorServiceStateAsync();
            await CheckDeviceAsync();
            await ApplyHardwareBrightnessAsync(false);
            try
            {
                _sensorService = new SensorCatalogService();
                await RefreshSensorsAsync();
                await RefreshWeatherAsync(false);
                _refreshTimer.Start();
            }
            catch (Exception exception)
            {
                SensorPermissionText.Text = "传感器不可用：" + exception.Message;
            }

            if (_startMinimized) await RestoreStartupOutputAsync();
            StartAutomationEngine();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (!_allowExit && _trayIcon != null)
            {
                e.Cancel = true;
                if (_canvasPreviewWindow != null) _canvasPreviewWindow.Close();
                ShowInTaskbar = false;
                Hide();
                if (!_trayHintShown && _trayIcon != null)
                {
                    _trayHintShown = true;
                    _trayIcon.ShowBalloonTip(2200, "TURZX 屏幕控制",
                        "软件仍在后台运行，可双击托盘图标恢复窗口。",
                        System.Windows.Forms.ToolTipIcon.Info);
                }
                return;
            }

            _refreshTimer.Stop();
            StopAutomationEngine();
            if (_videoCancellation != null) _videoCancellation.Cancel();
            if (_themeCancellation != null) _themeCancellation.Cancel();
            if (_creativeGenerationCancellation != null)
                _creativeGenerationCancellation.Cancel();
            if (_canvasPreviewWindow != null) _canvasPreviewWindow.Close();
            StopAllComponentVideos();
            if (_sensorService != null) _sensorService.Dispose();
            _weatherService.Dispose();
            _aiImageService.Dispose();
            try
            {
                ThemeProjectStore.Save(BuildProjectFromEditor(), ThemeProjectStore.DefaultPath);
                SensorMappingStore.Save(_sensorMappings);
                WeatherSettingsStore.Save(_weatherSettings);
                AiImageSettingsStore.Save(_aiImageSettings);
                DisplaySettingsStore.Save(_displaySettings);
            }
            catch
            {
            }
            DisposeTrayIcon();
        }

        private void InitializeTrayIcon()
        {
            try
            {
                _trayIconImage = System.Drawing.Icon.ExtractAssociatedIcon(
                    Process.GetCurrentProcess().MainModule.FileName);
                System.Windows.Forms.ContextMenuStrip menu =
                    new System.Windows.Forms.ContextMenuStrip();
                menu.ShowImageMargin = false;
                System.Windows.Forms.ToolStripMenuItem openItem =
                    new System.Windows.Forms.ToolStripMenuItem("打开主界面");
                openItem.Font = new System.Drawing.Font(openItem.Font,
                    System.Drawing.FontStyle.Bold);
                openItem.Click += delegate { Dispatcher.BeginInvoke(
                    new Action(ShowFromTray)); };
                System.Windows.Forms.ToolStripMenuItem exitItem =
                    new System.Windows.Forms.ToolStripMenuItem("退出软件");
                exitItem.Click += delegate { Dispatcher.BeginInvoke(
                    new Action(ExitFromTray)); };
                menu.Items.Add(openItem);
                menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                menu.Items.Add(exitItem);

                _trayIcon = new System.Windows.Forms.NotifyIcon();
                _trayIcon.Text = "TURZX 屏幕控制";
                _trayIcon.Icon = _trayIconImage ?? System.Drawing.SystemIcons.Application;
                _trayIcon.ContextMenuStrip = menu;
                _trayIcon.Visible = true;
                _trayIcon.DoubleClick += delegate { Dispatcher.BeginInvoke(
                    new Action(ShowFromTray)); };
            }
            catch
            {
                DisposeTrayIcon();
            }
        }

        private void ShowFromTray()
        {
            ShowInTaskbar = true;
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        private void ExitFromTray()
        {
            _allowExit = true;
            Application.Current.Shutdown();
        }

        internal void PrepareForSystemShutdown()
        {
            _allowExit = true;
        }

        private void DisposeTrayIcon()
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                if (_trayIcon.ContextMenuStrip != null)
                    _trayIcon.ContextMenuStrip.Dispose();
                _trayIcon.Dispose();
                _trayIcon = null;
            }
            if (_trayIconImage != null)
            {
                _trayIconImage.Dispose();
                _trayIconImage = null;
            }
        }

        private void LoadStartupSetting()
        {
            _startupToggleLoading = true;
            try
            {
                bool enabled = StartupManager.IsEnabled;
                if (enabled)
                {
                    try { StartupManager.SetEnabled(true); }
                    catch { }
                }
                StartupToggle.IsChecked = enabled;
                StartupStatusText.Text = enabled ? "开机后在托盘运行并恢复上次输出" :
                    "关闭后不会随 Windows 启动";
            }
            finally { _startupToggleLoading = false; }
        }

        private void StartupToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_startupToggleLoading || StartupToggle == null) return;
            bool enabled = StartupToggle.IsChecked == true;
            try
            {
                StartupManager.SetEnabled(enabled);
                StartupStatusText.Text = enabled ? "开机后在托盘运行并恢复上次输出" :
                    "关闭后不会随 Windows 启动";
            }
            catch (Exception exception)
            {
                _startupToggleLoading = true;
                StartupToggle.IsChecked = !enabled;
                _startupToggleLoading = false;
                StartupStatusText.Text = "设置失败：" + exception.Message;
            }
        }

        private void LoadDisplaySettingsIntoUi()
        {
            _displaySettingsLoading = true;
            try
            {
                ScreenBrightnessSlider.Value = _displaySettings.Brightness;
                ScreenTemperatureSlider.Value = _displaySettings.ColorTemperature;
                ScreenHueSlider.Value = _displaySettings.Hue;
                UpdateDisplaySettingsLabels();
                DisplaySettingsStatusText.Text = "显示设置会在软件启动时自动恢复";
            }
            finally
            {
                _displaySettingsLoading = false;
            }
        }

        private void ScreenBrightnessSlider_ValueChanged(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (ScreenBrightnessValueText != null)
                ScreenBrightnessValueText.Text = Math.Round(e.NewValue) + "%";
        }

        private void ScreenColorTemperatureSlider_ValueChanged(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (ScreenTemperatureValueText != null)
                ScreenTemperatureValueText.Text = Math.Round(e.NewValue) + " K";
        }

        private void ScreenHueSlider_ValueChanged(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (ScreenHueValueText != null)
                ScreenHueValueText.Text = Math.Round(e.NewValue) + "°";
        }

        private void UpdateDisplaySettingsLabels()
        {
            ScreenBrightnessValueText.Text = Math.Round(ScreenBrightnessSlider.Value) + "%";
            ScreenTemperatureValueText.Text = Math.Round(ScreenTemperatureSlider.Value) + " K";
            ScreenHueValueText.Text = Math.Round(ScreenHueSlider.Value) + "°";
        }

        private async void ApplyDisplaySettings_Click(object sender, RoutedEventArgs e)
        {
            if (_displaySettingsLoading) return;
            await CommitDisplaySettingsAsync();
        }

        private async void ResetDisplaySettings_Click(object sender, RoutedEventArgs e)
        {
            _displaySettingsLoading = true;
            try
            {
                ScreenBrightnessSlider.Value = 100;
                ScreenTemperatureSlider.Value = 6500;
                ScreenHueSlider.Value = 0;
                UpdateDisplaySettingsLabels();
            }
            finally
            {
                _displaySettingsLoading = false;
            }
            await CommitDisplaySettingsAsync();
        }

        private async Task CommitDisplaySettingsAsync()
        {
            ApplyDisplaySettingsButton.IsEnabled = false;
            DisplaySettingsStatusText.Text = "正在应用到屏幕…";
            try
            {
                lock (_displaySettingsSync)
                {
                    _displaySettings.Brightness = (int)Math.Round(
                        ScreenBrightnessSlider.Value);
                    _displaySettings.ColorTemperature = (int)Math.Round(
                        ScreenTemperatureSlider.Value);
                    _displaySettings.Hue = (int)Math.Round(ScreenHueSlider.Value);
                    _displaySettings.Normalize();
                    DisplaySettingsStore.Save(_displaySettings);
                }

                await ApplyHardwareBrightnessAsync(true);

                OutputSessionSettings output = OutputSessionStore.LoadOrDefault();
                if (output.Mode == OutputSessionMode.Image &&
                    !string.IsNullOrWhiteSpace(_imagePath) && File.Exists(_imagePath))
                    await SendImageAsync(false);

                DisplaySettingsStatusText.Text = "已应用：亮度 " +
                    _displaySettings.Brightness + "% · 色温 " +
                    _displaySettings.ColorTemperature + " K · 色调 " +
                    _displaySettings.Hue + "°";
            }
            catch (Exception exception)
            {
                DisplaySettingsStatusText.Text = "应用失败：" + exception.Message;
            }
            finally
            {
                ApplyDisplaySettingsButton.IsEnabled = true;
            }
        }

        private async Task ApplyHardwareBrightnessAsync(bool throwOnFailure)
        {
            int brightness;
            lock (_displaySettingsSync) brightness = _displaySettings.Brightness;
            try
            {
                await Task.Run(delegate
                {
                    lock (UsbCommandSync)
                    {
                        using (WinUsbDevice device = WinUsbDevice.Open(VendorId, ProductId))
                        {
                            RequireSuccess(device.Exchange(
                                TurzxProtocol.CreateBrightnessCommand(brightness), 14),
                                "设置亮度");
                        }
                    }
                });
            }
            catch
            {
                if (throwOnFailure) throw;
            }
        }

        private async Task RestoreStartupOutputAsync()
        {
            OutputSessionSettings settings = OutputSessionStore.LoadOrDefault();
            try
            {
                switch (settings.Mode)
                {
                    case OutputSessionMode.Image:
                        if (string.IsNullOrWhiteSpace(settings.ImagePath) ||
                            !File.Exists(settings.ImagePath))
                            throw new FileNotFoundException("上次使用的图片不存在。",
                                settings.ImagePath);
                        _imagePath = settings.ImagePath;
                        ImagePathText.Text = Path.GetFileName(_imagePath);
                        ImagePreview.Source = LoadBitmapSource(_imagePath);
                        ImageFitCombo.SelectedIndex = ClampIndex(settings.ImageFitIndex, 4);
                        ImageRotationCombo.SelectedIndex = ClampIndex(
                            settings.ImageRotationIndex, 4);
                        ImagePreviewScale.ScaleX = ClampScale(settings.ImageScale);
                        ImagePreviewScale.ScaleY = ImagePreviewScale.ScaleX;
                        ImagePreviewTranslate.X = settings.ImageOffsetX;
                        ImagePreviewTranslate.Y = settings.ImageOffsetY;
                        if (await SendImageAsync(false))
                            SetTrayStatus("图片显示");
                        break;

                    case OutputSessionMode.Video:
                        if (string.IsNullOrWhiteSpace(settings.VideoPath) ||
                            !File.Exists(settings.VideoPath))
                            throw new FileNotFoundException("上次使用的视频不存在。",
                                settings.VideoPath);
                        string ffmpeg = VideoDecoderLocator.FindFfmpeg();
                        if (string.IsNullOrEmpty(ffmpeg))
                            throw new InvalidOperationException("未找到 FFmpeg，无法恢复视频播放。");
                        _videoPath = settings.VideoPath;
                        VideoPathText.Text = Path.GetFileName(_videoPath);
                        VideoFitCombo.SelectedIndex = ClampIndex(settings.VideoFitIndex, 4);
                        VideoRotationCombo.SelectedIndex = ClampIndex(
                            settings.VideoRotationIndex, 4);
                        VideoFpsCombo.SelectedIndex = ClampIndex(settings.VideoFpsIndex, 6);
                        VideoLoopCheck.IsChecked = settings.VideoLoop;
                        _videoMetadata = await Task.Run(delegate
                        {
                            return VideoProbe.Read(ffmpeg, _videoPath);
                        });
                        _videoScreenEncoded = _videoMetadata.LooksScreenEncoded;
                        VideoPreview.Width = _videoMetadata.Width;
                        VideoPreview.Height = _videoMetadata.Height;
                        ApplyVideoPreviewRotation();
                        VideoPreviewScale.ScaleX = ClampScale(settings.VideoScale);
                        VideoPreviewScale.ScaleY = VideoPreviewScale.ScaleX;
                        VideoPreviewTranslate.X = settings.VideoOffsetX;
                        VideoPreviewTranslate.Y = settings.VideoOffsetY;
                        VideoPreview.Source = new Uri(_videoPath);
                        VideoPreview.Play();
                        SetTrayStatus("视频播放");
                        _startupOutputTask = PlayVideoAsync(false);
                        break;

                    case OutputSessionMode.Theme:
                        SetTrayStatus("实时监控");
                        _startupOutputTask = StartThemeAsync(false);
                        break;

                    default:
                        SetTrayStatus("后台待机");
                        break;
                }
            }
            catch (Exception exception)
            {
                SetTrayStatus("自动恢复失败");
                ShowTrayError("自动恢复失败", exception.Message);
            }
        }

        private OutputSessionSettings CaptureImageOutput()
        {
            return new OutputSessionSettings
            {
                Mode = OutputSessionMode.Image,
                ImagePath = _imagePath,
                ImageFitIndex = ImageFitCombo.SelectedIndex,
                ImageRotationIndex = ImageRotationCombo.SelectedIndex,
                ImageScale = ImagePreviewScale.ScaleX,
                ImageOffsetX = ImagePreviewTranslate.X,
                ImageOffsetY = ImagePreviewTranslate.Y
            };
        }

        private OutputSessionSettings CaptureVideoOutput()
        {
            return new OutputSessionSettings
            {
                Mode = OutputSessionMode.Video,
                VideoPath = _videoPath,
                VideoFitIndex = VideoFitCombo.SelectedIndex,
                VideoRotationIndex = VideoRotationCombo.SelectedIndex,
                VideoFpsIndex = VideoFpsCombo.SelectedIndex,
                VideoLoop = VideoLoopCheck.IsChecked == true,
                VideoScale = VideoPreviewScale.ScaleX,
                VideoOffsetX = VideoPreviewTranslate.X,
                VideoOffsetY = VideoPreviewTranslate.Y
            };
        }

        private static int ClampIndex(int value, int count)
        {
            return Math.Max(0, Math.Min(count - 1, value));
        }

        private static double ClampScale(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 1;
            return Math.Max(1, Math.Min(8, value));
        }

        private void RememberOutput(OutputSessionSettings settings)
        {
            try { OutputSessionStore.Save(settings); }
            catch { }
        }

        private void SetTrayStatus(string status)
        {
            if (_trayIcon == null) return;
            string text = "TURZX 屏幕控制 · " + status;
            _trayIcon.Text = text.Length > 63 ? text.Substring(0, 63) : text;
        }

        private void ShowTrayError(string title, string message)
        {
            if (_trayIcon == null) return;
            string text = string.IsNullOrWhiteSpace(message) ? "未知错误" : message;
            if (text.Length > 180) text = text.Substring(0, 180) + "…";
            _trayIcon.ShowBalloonTip(5000, title, text,
                System.Windows.Forms.ToolTipIcon.Warning);
        }

        private void Navigate_Click(object sender, RoutedEventArgs e)
        {
            FrameworkElement element = sender as FrameworkElement;
            ShowPage(element == null ? "Dashboard" : Convert.ToString(element.Tag));
        }

        private void OpenEditor_Click(object sender, RoutedEventArgs e)
        {
            ShowPage("Editor");
        }

        private void FixedHeightCard_Loaded(object sender, RoutedEventArgs e)
        {
            Wpf.Ui.Controls.Card card = sender as Wpf.Ui.Controls.Card;
            if (card == null) return;
            card.ApplyTemplate();
            Border contentBorder = card.Template.FindName("ContentBorder", card) as Border;
            if (contentBorder == null) return;
            contentBorder.VerticalAlignment = VerticalAlignment.Stretch;
            contentBorder.InvalidateMeasure();
            contentBorder.InvalidateArrange();
            if (card == EditorAddCard)
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
                    new Action(AlignEditorAddCardBottom));
        }

        private void AlignEditorAddCardBottom()
        {
            if (EditorAddCard == null || EditorPresetCard == null || EditorContentGrid == null) return;
            double addBottom = EditorAddCard.TranslatePoint(
                new System.Windows.Point(0, EditorAddCard.ActualHeight), EditorContentGrid).Y;
            double presetBottom = EditorPresetCard.TranslatePoint(
                new System.Windows.Point(0, EditorPresetCard.ActualHeight), EditorContentGrid).Y;
            double correction = addBottom - presetBottom;
            if (correction > 0.01)
                EditorAddCard.Margin = new Thickness(0, 0, 0, correction);
        }

        private void ShowPage(string page)
        {
            DashboardPage.Visibility = page == "Dashboard" ? Visibility.Visible : Visibility.Collapsed;
            ImagePage.Visibility = page == "Image" ? Visibility.Visible : Visibility.Collapsed;
            VideoPage.Visibility = page == "Video" ? Visibility.Visible : Visibility.Collapsed;
            CreativePage.Visibility = page == "Creative" ? Visibility.Visible : Visibility.Collapsed;
            EditorPage.Visibility = page == "Editor" ? Visibility.Visible : Visibility.Collapsed;
            AutomationPage.Visibility = page == "Automation" ? Visibility.Visible : Visibility.Collapsed;
            SensorsPage.Visibility = page == "Sensors" ? Visibility.Visible : Visibility.Collapsed;
            SettingsPage.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;

            Wpf.Ui.Controls.Button[] buttons =
            {
                NavDashboard, NavImage, NavVideo, NavCreative, NavEditor, NavAutomation,
                NavSensors, NavSettings
            };
            foreach (Wpf.Ui.Controls.Button button in buttons)
            {
                button.Appearance = Convert.ToString(button.Tag) == page ?
                    ControlAppearance.Primary : ControlAppearance.Secondary;
            }
        }

        private async void CheckDevice_Click(object sender, RoutedEventArgs e)
        {
            await CheckDeviceAsync();
        }

        private async Task CheckDeviceAsync()
        {
            NavDeviceStatus.Text = "正在检测设备";
            try
            {
                string details = await Task.Run(delegate
                {
                    using (WinUsbDevice device = WinUsbDevice.Open(VendorId, ProductId))
                    {
                        return string.Format("端点 0x{0:X2} · 数据包 {1} B",
                            device.OutEndpoint, device.OutMaxPacketSize);
                    }
                });
                NavDeviceStatus.Text = "● 已连接";
                NavDeviceStatus.ToolTip = details;
            }
            catch (Exception exception)
            {
                NavDeviceStatus.Text = "○ 未连接";
                NavDeviceStatus.ToolTip = "设备连接失败：" + exception.Message;
            }
        }

        private async void RefreshTimer_Tick(object sender, EventArgs e)
        {
            await RefreshSensorsAsync();
            await RefreshWeatherIfDueAsync();
            RefreshComponentVisuals();
        }

        private async Task RefreshSensorsAsync()
        {
            if (_sensorService == null || _sensorRefreshBusy) return;
            _sensorRefreshBusy = true;
            try
            {
                IList<SensorReading> captured = await Task.Run(
                    delegate { return _sensorService.Capture(); });
                foreach (SensorReading reading in captured)
                {
                    SensorReading existing;
                    if (_sensorMap.TryGetValue(reading.Id, out existing))
                    {
                        existing.Value = reading.Value;
                    }
                    else
                    {
                        _sensorMap[reading.Id] = reading;
                        _sensors.Add(reading);
                    }
                }
                HashSet<string> capturedIds = new HashSet<string>(
                    captured.Select(value => value.Id), StringComparer.OrdinalIgnoreCase);
                for (int index = _sensors.Count - 1; index >= 0; index--)
                {
                    if (!capturedIds.Contains(_sensors[index].Id))
                    {
                        _sensorMap.Remove(_sensors[index].Id);
                        _sensors.RemoveAt(index);
                    }
                }
                RefreshDynamicFanMetrics();
                EnsureDefaultSensorMappings();
                RefreshSensorMappingControls();
                UpdateHardwareModelNames();
                UpdateDashboardValues();
                UpdateEditorSensorValues();
            }
            catch (Exception exception)
            {
                SensorPermissionText.Text = "传感器刷新失败：" + exception.Message;
            }
            finally
            {
                _sensorRefreshBusy = false;
            }
        }

        private void UpdateHardwareModelNames()
        {
            string cpuName = _sensors
                .Where(reading => string.Equals(reading.Category, "CPU",
                    StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(reading.Hardware))
                .Select(reading => reading.Hardware.Trim())
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(cpuName))
            {
                cpuName = _sensorService == null ? null :
                    _sensorService.CpuHardwareName;
            }

            string gpuName = _sensors
                .Where(reading => string.Equals(reading.Category, "GPU",
                    StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(reading.Hardware))
                .Select(reading => reading.Hardware.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(IsPreferredGraphicsAdapter)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(gpuName))
            {
                gpuName = _sensorService == null ? null :
                    _sensorService.GpuHardwareName;
            }

            SetHardwareName(CpuHardwareName, cpuName, "CPU · 等待识别");
            SetHardwareName(GpuHardwareName, gpuName, "GPU · 等待识别");
        }

        private static bool IsPreferredGraphicsAdapter(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return name.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("GeForce", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Arc", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void SetHardwareName(System.Windows.Controls.TextBlock target,
            string name,
            string fallback)
        {
            string displayName = string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
            target.Text = displayName;
            target.ToolTip = displayName;
        }

        private void UpdateDashboardValues()
        {
            CpuValue.Text = GetMetricDisplayValue("cpu.usage");
            MemoryValue.Text = GetMetricDisplayValue("memory.usage");
            CpuTempValue.Text = GetMetricDisplayValue("cpu.temperature");
            GpuValue.Text = GetMetricDisplayValue("gpu.usage");
            CpuPowerValue.Text = GetMetricDisplayValue("cpu.power");
            CpuClockValue.Text = GetMetricDisplayValue("cpu.clock");
            GpuTempValue.Text = GetMetricDisplayValue("gpu.temperature");
            GpuPowerValue.Text = GetMetricDisplayValue("gpu.power");
            GpuClockValue.Text = GetMetricDisplayValue("gpu.clock");
            MemoryClockValue.Text = GetMetricDisplayValue("memory.clock");
            DiskCValue.Text = GetMetricDisplayValue("disk.c.usage");
            DownloadValue.Text = GetMetricDisplayValue("network.download");
            UploadValue.Text = GetMetricDisplayValue("network.upload");
            DiskValue.Text = GetMetricDisplayValue("disk.usage");
            AppMemoryValue.Text = GetMetricDisplayValue("app.memory");
            UptimeValue.Text = GetMetricDisplayValue("system.uptime");
        }

        private string GetSensorValue(string id)
        {
            if (string.IsNullOrEmpty(id)) return "--";
            SensorReading value;
            return _sensorMap.TryGetValue(id, out value) ? value.DisplayValue : "--";
        }

        private string GetMetricDisplayValue(string metricKey)
        {
            return GetSensorValue(GetMappedSensorId(metricKey));
        }

        private string GetMappedSensorId(string metricKey)
        {
            MonitorMetricDefinition dynamicMetric = _metrics.FirstOrDefault(
                delegate(MonitorMetricDefinition metric)
                {
                    return string.Equals(metric.Key, metricKey,
                        StringComparison.OrdinalIgnoreCase);
                });
            if (dynamicMetric != null &&
                dynamicMetric.Key.StartsWith("fan:", StringComparison.OrdinalIgnoreCase))
                return dynamicMetric.DefaultSensorId;
            return _sensorMappings == null ? null : _sensorMappings.GetSensorId(metricKey);
        }

        private void UpdateEditorSensorValues()
        {
            foreach (EditorComponent component in _components)
            {
                if (component.Kind != EditorComponentKind.Sensor &&
                    component.Kind != EditorComponentKind.Progress) continue;
                if (string.IsNullOrEmpty(component.MetricKey))
                {
                    component.MetricKey = InferMetricKey(component);
                }
                if (string.Equals(component.MetricKey, "hardware.cpu.name",
                    StringComparison.OrdinalIgnoreCase))
                {
                    component.SensorId = null;
                    component.SensorValue = FindCpuHardwareName() ?? "--";
                    continue;
                }
                if (string.Equals(component.MetricKey, "hardware.gpu.name",
                    StringComparison.OrdinalIgnoreCase))
                {
                    component.SensorId = null;
                    component.SensorValue = FindGpuHardwareName() ?? "--";
                    continue;
                }
                component.SensorId = GetMappedSensorId(component.MetricKey);
                SensorReading reading;
                component.SensorValue = !string.IsNullOrEmpty(component.SensorId) &&
                    _sensorMap.TryGetValue(component.SensorId, out reading) ? reading.DisplayValue : "--";
            }
            UpdateEditorWeatherValues();
        }

        private string FindCpuHardwareName()
        {
            string name = _sensors.Where(delegate(SensorReading reading)
                {
                    return string.Equals(reading.Category, "CPU",
                        StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(reading.Hardware);
                }).Select(reading => reading.Hardware.Trim()).FirstOrDefault();
            return string.IsNullOrWhiteSpace(name) && _sensorService != null ?
                _sensorService.CpuHardwareName : name;
        }

        private string FindGpuHardwareName()
        {
            string name = _sensors.Where(delegate(SensorReading reading)
                {
                    return string.Equals(reading.Category, "GPU",
                        StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(reading.Hardware);
                }).Select(reading => reading.Hardware.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(IsPreferredGraphicsAdapter).FirstOrDefault();
            return string.IsNullOrWhiteSpace(name) && _sensorService != null ?
                _sensorService.GpuHardwareName : name;
        }

        private void RefreshDynamicFanMetrics()
        {
            List<SensorReading> fans = _sensors.Where(delegate(SensorReading reading)
                {
                    return string.Equals(reading.Type, "Fan",
                        StringComparison.OrdinalIgnoreCase);
                }).OrderBy(reading => reading.Category)
                .ThenBy(reading => reading.Hardware)
                .ThenBy(reading => reading.Name).ToList();
            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < fans.Count; index++)
            {
                SensorReading fan = fans[index];
                string key = "fan:" + fan.Id;
                keys.Add(key);
                string name = "风扇 " + (index + 1) + " · " + fan.Category + " · " + fan.Name;
                MonitorMetricDefinition existing = _metrics.FirstOrDefault(
                    metric => string.Equals(metric.Key, key, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    _metrics.Add(new MonitorMetricDefinition
                    {
                        Key = key,
                        Name = name,
                        PreferredCategory = fan.Category,
                        PreferredType = "Fan",
                        DefaultSensorId = fan.Id
                    });
                }
                else
                {
                    existing.Name = name;
                    existing.DefaultSensorId = fan.Id;
                }
            }

            for (int index = _metrics.Count - 1; index >= 0; index--)
                if (_metrics[index].Key.StartsWith("fan:", StringComparison.OrdinalIgnoreCase) &&
                    !keys.Contains(_metrics[index].Key))
                    _metrics.RemoveAt(index);
        }

        private void UpdateEditorWeatherValues()
        {
            foreach (EditorComponent component in _components)
                if (component.Kind == EditorComponentKind.Weather)
                    component.WeatherValue = BuildWeatherDisplay(component);
        }

        private string BuildWeatherDisplay(EditorComponent component)
        {
            string location = string.IsNullOrWhiteSpace(_weatherSettings.ResolvedName) ?
                _weatherSettings.City : _weatherSettings.ResolvedName;
            string separator = component.WeatherShowSeparator ? " · " : " ";
            if (_weatherSnapshot == null)
            {
                List<string> waitingParts = new List<string>();
                if (component.WeatherShowIcon) waitingParts.Add("◌");
                if (component.WeatherShowLocation) waitingParts.Add(location);
                waitingParts.Add("天气等待更新");
                return string.Join(separator, waitingParts);
            }

            List<string> parts = new List<string>();
            if (component.WeatherShowIcon) parts.Add(WeatherIcon(_weatherSnapshot.WeatherCode));
            if (component.WeatherShowLocation) parts.Add(_weatherSnapshot.LocationName);
            if (component.WeatherShowTemperature)
                parts.Add(_weatherSnapshot.Temperature.ToString("0.#") + _weatherSnapshot.Unit);
            if (component.WeatherShowCondition) parts.Add(_weatherSnapshot.Condition);
            return parts.Count == 0 ? _weatherSnapshot.Temperature.ToString("0.#") +
                _weatherSnapshot.Unit : string.Join(separator, parts);
        }

        private static string WeatherIcon(int code)
        {
            if (code == 0) return "☀";
            if (code <= 3) return "⛅";
            if (code == 45 || code == 48) return "🌫";
            if ((code >= 51 && code <= 67) || (code >= 80 && code <= 82)) return "🌧";
            if (code >= 71 && code <= 77) return "🌨";
            if (code >= 95) return "⛈";
            return "☁";
        }

        private void LoadWeatherSettingsIntoUi()
        {
            WeatherCityText.Text = _weatherSettings.City;
            SelectComboByTag(WeatherUnitCombo, _weatherSettings.TemperatureUnit);
            SelectComboByTag(WeatherRefreshCombo, _weatherSettings.RefreshMinutes.ToString());
            WeatherStatusText.Text = "当前城市：" +
                (string.IsNullOrWhiteSpace(_weatherSettings.ResolvedName) ?
                    _weatherSettings.City : _weatherSettings.ResolvedName);
        }

        private async Task RefreshWeatherIfDueAsync()
        {
            if (DateTime.Now < _nextWeatherRefresh) return;
            await RefreshWeatherAsync(false);
        }

        private async Task RefreshWeatherAsync(bool resolveCity)
        {
            if (_weatherRefreshBusy) return;
            _weatherRefreshBusy = true;
            try
            {
                bool shouldResolveCity = resolveCity || _weatherSettings.GeocodingVersion < 2;
                WeatherStatusText.Text = shouldResolveCity ? "正在查找城市并获取天气…" : "正在更新天气…";
                _weatherSnapshot = shouldResolveCity ?
                    await _weatherService.ResolveAndFetchAsync(_weatherSettings) :
                    await _weatherService.FetchAsync(_weatherSettings);
                _nextWeatherRefresh = DateTime.Now.AddMinutes(Math.Max(5, _weatherSettings.RefreshMinutes));
                WeatherSettingsStore.Save(_weatherSettings);
                WeatherStatusText.Text = _weatherSnapshot.DisplayValue + " · 已更新";
                UpdateEditorWeatherValues();
                RefreshComponentVisuals();
            }
            catch (Exception exception)
            {
                _nextWeatherRefresh = DateTime.Now.AddMinutes(2);
                WeatherStatusText.Text = "天气更新失败：" + exception.Message;
                UpdateEditorWeatherValues();
            }
            finally { _weatherRefreshBusy = false; }
        }

        private async void SaveWeatherSettings_Click(object sender, RoutedEventArgs e)
        {
            string city = (WeatherCityText.Text ?? string.Empty).Trim();
            if (city.Length < 2)
            {
                WeatherStatusText.Text = "请输入至少两个字符的城市名称";
                return;
            }
            _weatherSettings.City = city;
            _weatherSettings.TemperatureUnit = GetSelectedTag(WeatherUnitCombo, "Celsius");
            int minutes;
            _weatherSettings.RefreshMinutes = int.TryParse(
                GetSelectedTag(WeatherRefreshCombo, "15"), out minutes) ? minutes : 15;
            await RefreshWeatherAsync(true);
        }

        private void LoadAiSettingsIntoUi()
        {
            AiApiEndpointText.Text = _aiImageSettings.Endpoint ?? string.Empty;
            AiApiModelText.Text = _aiImageSettings.Model ?? string.Empty;
            AiApiKeyPassword.Password = _aiImageSettings.ApiKey ?? string.Empty;
            bool configured = !string.IsNullOrWhiteSpace(_aiImageSettings.Endpoint) &&
                !string.IsNullOrWhiteSpace(_aiImageSettings.Model) &&
                !string.IsNullOrWhiteSpace(_aiImageSettings.ApiKey);
            AiApiStatusText.Text = configured ? "API 已配置" : "尚未完成 API 配置";
            CreativeStatusText.Text = configured ?
                "输入提示词后即可生成屏幕背景" :
                "在设置中配置图像生成 API 后即可开始创作";
        }

        private void SaveAiApiSettings_Click(object sender, RoutedEventArgs e)
        {
            string endpoint = (AiApiEndpointText.Text ?? string.Empty).Trim();
            string model = (AiApiModelText.Text ?? string.Empty).Trim();
            string apiKey = (AiApiKeyPassword.Password ?? string.Empty).Trim();
            Uri endpointUri;
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out endpointUri) ||
                (endpointUri.Scheme != Uri.UriSchemeHttp &&
                 endpointUri.Scheme != Uri.UriSchemeHttps))
            {
                AiApiStatusText.Text = "请输入有效的 HTTP/HTTPS API 地址";
                return;
            }
            if (model.Length == 0 || apiKey.Length == 0)
            {
                AiApiStatusText.Text = "请完整填写 API 密钥和图像模型";
                return;
            }
            _aiImageSettings.Endpoint = endpoint;
            _aiImageSettings.Model = model;
            _aiImageSettings.ApiKey = apiKey;
            try
            {
                AiImageSettingsStore.Save(_aiImageSettings);
                AiApiStatusText.Text = "API 配置已加密保存";
                CreativeStatusText.Text = "输入提示词后即可生成屏幕背景";
            }
            catch (Exception exception)
            {
                AiApiStatusText.Text = "保存失败：" + exception.Message;
            }
        }

        private string InferMetricKey(EditorComponent component)
        {
            MonitorMetricDefinition direct = MonitorMetrics.All.FirstOrDefault(delegate(MonitorMetricDefinition metric)
            {
                return string.Equals(metric.DefaultSensorId, component.SensorId,
                    StringComparison.OrdinalIgnoreCase);
            });
            if (direct != null) return direct.Key;

            string sensorId = component.SensorId ?? string.Empty;
            if (sensorId.IndexOf("/gpu", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (sensorId.IndexOf("/temperature/", StringComparison.OrdinalIgnoreCase) >= 0) return "gpu.temperature";
                if (sensorId.IndexOf("/power/", StringComparison.OrdinalIgnoreCase) >= 0) return "gpu.power";
                if (sensorId.IndexOf("/clock/", StringComparison.OrdinalIgnoreCase) >= 0) return "gpu.clock";
                if (sensorId.IndexOf("/load/", StringComparison.OrdinalIgnoreCase) >= 0) return "gpu.usage";
            }
            if (sensorId.IndexOf("/cpu", StringComparison.OrdinalIgnoreCase) >= 0 ||
                sensorId.IndexOf("/intelcpu", StringComparison.OrdinalIgnoreCase) >= 0 ||
                sensorId.IndexOf("/amdcpu", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (sensorId.IndexOf("/temperature/", StringComparison.OrdinalIgnoreCase) >= 0) return "cpu.temperature";
                if (sensorId.IndexOf("/power/", StringComparison.OrdinalIgnoreCase) >= 0) return "cpu.power";
                if (sensorId.IndexOf("/clock/", StringComparison.OrdinalIgnoreCase) >= 0) return "cpu.clock";
                if (sensorId.IndexOf("/load/", StringComparison.OrdinalIgnoreCase) >= 0) return "cpu.usage";
            }

            SensorReading reading;
            if (!string.IsNullOrEmpty(component.SensorId) && _sensorMap.TryGetValue(component.SensorId, out reading))
            {
                MonitorMetricDefinition bySensor = MonitorMetrics.All.FirstOrDefault(delegate(MonitorMetricDefinition metric)
                {
                    return metric.PreferredCategory == reading.Category && metric.PreferredType == reading.Type;
                });
                if (bySensor != null) return bySensor.Key;
            }

            string text = component.Text ?? string.Empty;
            if (text.IndexOf("GPU", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (text.Contains("温度")) return "gpu.temperature";
                if (text.Contains("功耗") || text.Contains("功率")) return "gpu.power";
                if (text.Contains("频率")) return "gpu.clock";
                return "gpu.usage";
            }
            if (text.Contains("温度")) return "cpu.temperature";
            if (text.Contains("功耗") || text.Contains("功率")) return "cpu.power";
            if (text.Contains("内存") && text.Contains("频率")) return "memory.clock";
            if (text.Contains("后台内存")) return "app.memory";
            if (text.Contains("C 盘") || text.Contains("C盘")) return "disk.c.usage";
            if (text.Contains("频率")) return "cpu.clock";
            if (text.Contains("内存")) return "memory.usage";
            if (text.Contains("磁盘")) return "disk.usage";
            return "cpu.usage";
        }

        private void InitializeSensorMappingControls()
        {
            _sensorMappingCombos.AddRange(new[]
            {
                SensorMapCpuUsage, SensorMapCpuTemperature, SensorMapCpuPower, SensorMapCpuClock,
                SensorMapGpuUsage, SensorMapGpuTemperature, SensorMapGpuPower, SensorMapGpuClock,
                SensorMapGpuVramUsage,
                SensorMapMemoryUsage, SensorMapMemoryClock, SensorMapDiskCUsage, SensorMapDiskUsage,
                SensorMapDiskTemperature, SensorMapNetworkDownload, SensorMapNetworkUpload,
                SensorMapAppMemory, SensorMapSystemUptime
            });
        }

        private void EnsureDefaultSensorMappings()
        {
            bool changed = false;
            foreach (MonitorMetricDefinition metric in MonitorMetrics.All)
            {
                string mapped = GetMappedSensorId(metric.Key);
                bool mappingIsValid = !string.IsNullOrEmpty(mapped) &&
                    _sensorMap.ContainsKey(mapped);
                if (string.Equals(metric.Key, "cpu.clock", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(mapped, "os/cpu-clock", StringComparison.OrdinalIgnoreCase))
                    mappingIsValid = false;
                if (mappingIsValid) continue;
                SensorReading candidate = FindDefaultSensor(metric);
                if (candidate != null)
                {
                    _sensorMappings.SetSensorId(metric.Key, candidate.Id);
                    changed = true;
                }
            }
            if (changed) SensorMappingStore.Save(_sensorMappings);
        }

        private SensorReading FindDefaultSensor(MonitorMetricDefinition metric)
        {
            SensorReading direct;
            if (!string.IsNullOrEmpty(metric.DefaultSensorId) &&
                _sensorMap.TryGetValue(metric.DefaultSensorId, out direct)) return direct;

            IEnumerable<SensorReading> candidates = _sensors.Where(delegate(SensorReading value)
            {
                return value.Category == metric.PreferredCategory && value.Type == metric.PreferredType;
            });
            return candidates.OrderBy(delegate(SensorReading value)
            {
                string name = value.Name ?? string.Empty;
                if (metric.Key == "cpu.temperature")
                {
                    if (name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0) return 0;
                    if (name.IndexOf("Average", StringComparison.OrdinalIgnoreCase) >= 0) return 1;
                    if (name.IndexOf("Max", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
                }
                if (metric.Key == "cpu.power" && name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0) return 0;
                if ((metric.Key == "gpu.usage" || metric.Key == "gpu.temperature") &&
                    name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0) return 0;
                if (metric.Key == "gpu.vram.usage")
                {
                    if (name.Equals("GPU Memory", StringComparison.OrdinalIgnoreCase)) return 0;
                    if (name.IndexOf("Memory", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        name.IndexOf("Controller", StringComparison.OrdinalIgnoreCase) < 0) return 1;
                }
                if (metric.Key == "cpu.clock" && name.IndexOf("Average", StringComparison.OrdinalIgnoreCase) >= 0) return 0;
                if (metric.Key == "gpu.clock" && name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0) return 0;
                return 10;
            }).FirstOrDefault();
        }

        private void RefreshSensorMappingControls()
        {
            _sensorMappingLoading = true;
            try
            {
                foreach (ComboBox combo in _sensorMappingCombos)
                {
                    string metricKey = Convert.ToString(combo.Tag);
                    MonitorMetricDefinition metric = MonitorMetrics.Find(metricKey);
                    combo.ItemsSource = metric == null ? _sensors.ToList() : GetCompatibleSensors(metric);
                    combo.SelectedValue = GetMappedSensorId(metricKey);
                }
            }
            finally { _sensorMappingLoading = false; }
        }

        private IList<SensorReading> GetCompatibleSensors(MonitorMetricDefinition metric)
        {
            return _sensors.Where(delegate(SensorReading reading)
            {
                if (!string.IsNullOrEmpty(metric.DefaultSensorId) &&
                    string.Equals(reading.Id, metric.DefaultSensorId, StringComparison.OrdinalIgnoreCase)) return true;
                if (metric.Key == "memory.usage")
                    return reading.Category == "内存" && reading.Type == "Load";
                if (metric.Key == "disk.usage")
                    return reading.Category == "存储" && (reading.Type == "Load" || reading.Type == "Level");
                if (!string.IsNullOrEmpty(metric.DefaultSensorId)) return false;
                return reading.Category == metric.PreferredCategory && reading.Type == metric.PreferredType;
            }).ToList();
        }

        private void SensorMapping_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_sensorMappingLoading || _sensorMappings == null) return;
            ComboBox combo = sender as ComboBox;
            SensorReading selected = combo == null ? null : combo.SelectedItem as SensorReading;
            if (combo == null || selected == null) return;
            _sensorMappings.SetSensorId(Convert.ToString(combo.Tag), selected.Id);
            SensorMappingStore.Save(_sensorMappings);
            UpdateDashboardValues();
            UpdateEditorSensorValues();
            RefreshComponentVisuals();
        }

        private void SensorFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SensorGrid == null || SensorFilter == null) return;
            ComboBoxItem selected = SensorFilter.SelectedItem as ComboBoxItem;
            string category = selected == null ? "全部" : Convert.ToString(selected.Content);
            if (category == "全部")
            {
                SensorGrid.ItemsSource = _sensors;
            }
            else
            {
                SensorGrid.ItemsSource = _sensors.Where(delegate(SensorReading value)
                {
                    return value.Category == category;
                }).ToList();
            }
        }

        private void AddComponent_Click(object sender, RoutedEventArgs e)
        {
            FrameworkElement source = sender as FrameworkElement;
            EditorComponentKind kind;
            if (!Enum.TryParse<EditorComponentKind>(Convert.ToString(source == null ? null : source.Tag), out kind))
            {
                return;
            }

            string mediaPath = null;
            if (kind == EditorComponentKind.Image || kind == EditorComponentKind.Video)
            {
                OpenFileDialog dialog = new OpenFileDialog();
                dialog.Filter = kind == EditorComponentKind.Image ?
                    "图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp" :
                    "视频|*.mp4;*.wmv;*.avi;*.mov";
                if (dialog.ShowDialog(this) != true) return;
                mediaPath = dialog.FileName;
            }

            EditorComponent component = CreateComponent(kind, 80 + _components.Count * 18,
                48 + _components.Count * 12);
            if (!string.IsNullOrWhiteSpace(mediaPath))
            {
                component.MediaPath = mediaPath;
                component.Name = (kind == EditorComponentKind.Image ? "图片 · " : "视频 · ") +
                    Path.GetFileNameWithoutExtension(mediaPath);
            }
            _components.Add(component);
            AddComponentVisual(component);
            ReorderComponentVisuals();
            LayerList.SelectedItem = component;
            EditorStatusText.Text = "已添加“" + component.Name + "”";
        }

        private static EditorComponent CreateComponent(EditorComponentKind kind, double x, double y)
        {
            EditorComponent component = new EditorComponent();
            component.Kind = kind;
            component.X = x;
            component.Y = y;
            switch (kind)
            {
                case EditorComponentKind.Text:
                    component.Name = "文字";
                    component.Text = "我的 TURZX";
                    component.FontSize = 28;
                    break;
                case EditorComponentKind.Time:
                    component.Name = "时间";
                    component.Text = "HH:mm:ss";
                    component.FontSize = 34;
                    break;
                case EditorComponentKind.Weather:
                    component.Name = "天气";
                    component.Text = "上海 · --°C · 待配置天气源";
                    component.Width = 310;
                    component.FontSize = 18;
                    break;
                case EditorComponentKind.Sensor:
                    component.Name = "监控数值";
                    component.Text = "CPU 温度";
                    component.MetricKey = "cpu.temperature";
                    component.Width = 250;
                    component.FontSize = 23;
                    break;
                case EditorComponentKind.Progress:
                    component.Name = "进度条";
                    component.Text = "CPU 使用率";
                    component.MetricKey = "cpu.usage";
                    component.Width = 300;
                    component.Height = 58;
                    component.FontSize = 17;
                    component.ProgressLength = 280;
                    break;
                case EditorComponentKind.Image:
                    component.Name = "图片";
                    component.Text = string.Empty;
                    component.Width = 280;
                    component.Height = 160;
                    component.PaddingLeft = 0;
                    component.PaddingTop = 0;
                    component.PaddingRight = 0;
                    component.PaddingBottom = 0;
                    component.BackgroundOpacity = 0;
                    break;
                case EditorComponentKind.Video:
                    component.Name = "视频";
                    component.Text = string.Empty;
                    component.Width = 320;
                    component.Height = 180;
                    component.PaddingLeft = 0;
                    component.PaddingTop = 0;
                    component.PaddingRight = 0;
                    component.PaddingBottom = 0;
                    component.BackgroundOpacity = 0;
                    break;
            }
            return component;
        }

        private void AddComponentVisual(EditorComponent component)
        {
            Border border = new Border();
            border.Tag = component;
            border.CornerRadius = new CornerRadius(9);
            border.BorderThickness = new Thickness(2);
            border.BorderBrush = System.Windows.Media.Brushes.Transparent;
            border.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(76, 0, 0, 0));
            border.Padding = new Thickness(8, 5, 8, 5);
            border.Cursor = Cursors.SizeAll;
            border.MouseLeftButtonDown += Component_MouseLeftButtonDown;
            border.MouseMove += Component_MouseMove;
            border.MouseLeftButtonUp += Component_MouseLeftButtonUp;
            ComponentCanvas.Children.Add(border);
            _componentVisuals[component.Id] = border;
            ApplyComponentToVisual(component, border);
        }

        private void ApplyComponentToVisual(EditorComponent component, Border border)
        {
            border.Width = component.Width;
            border.Height = component.Height;
            border.Visibility = component.Visible ? Visibility.Visible : Visibility.Hidden;
            border.CornerRadius = new CornerRadius(Math.Max(0, component.CornerRadius));
            border.BorderThickness = new Thickness(Math.Max(0, component.BorderThickness));
            border.Background = BrushWithOpacity(component.Background, component.BackgroundOpacity,
                System.Windows.Media.Brushes.Transparent);
            border.Padding = new Thickness(Math.Max(0, component.PaddingLeft),
                Math.Max(0, component.PaddingTop), Math.Max(0, component.PaddingRight),
                Math.Max(0, component.PaddingBottom));
            Canvas.SetLeft(border, component.X);
            Canvas.SetTop(border, component.Y);
            border.BorderBrush = GetComponentBorderBrush(component);
            border.Effect = null;

            if (component.Kind == EditorComponentKind.Image)
            {
                ApplyImageComponentToVisual(component, border);
                return;
            }
            if (component.Kind == EditorComponentKind.Video)
            {
                ApplyVideoComponentToVisual(component, border);
                return;
            }

            Grid grid = new Grid();
            System.Windows.Controls.TextBlock text = new System.Windows.Controls.TextBlock();
            text.Text = component.GetDisplayText(DateTime.Now);
            text.FontSize = component.FontSize;
            text.FontWeight = FontWeights.SemiBold;
            try { text.FontFamily = new System.Windows.Media.FontFamily(component.FontFamilyName); }
            catch { text.FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI"); }
            text.HorizontalAlignment = HorizontalAlignment.Stretch;
            text.TextAlignment = ParseTextAlignment(component.HorizontalTextAlignment);
            text.VerticalAlignment = ParseVerticalAlignment(component.VerticalTextAlignment);
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Foreground = ParseBrush(component.Foreground,
                System.Windows.Media.Brushes.White);
            TextOptions.SetTextRenderingMode(text, TextRenderingMode.Grayscale);
            TextOptions.SetTextFormattingMode(text, TextFormattingMode.Display);
            grid.Children.Add(text);

            if (component.Kind == EditorComponentKind.Progress)
            {
                double value;
                value = double.TryParse((component.SensorValue ?? string.Empty).Split(' ')[0], out value) ?
                    Math.Max(0, Math.Min(100, value)) : 0;
                grid.Children.Add(CreateProgressVisual(component, value));
            }
            border.Child = grid;
        }

        private static void ApplyImageComponentToVisual(EditorComponent component,
            Border border)
        {
            StopComponentVideo(border.Child);
            System.Windows.Controls.Image image = border.Child as
                System.Windows.Controls.Image;
            if (image == null)
            {
                image = new System.Windows.Controls.Image
                {
                    Stretch = Stretch.UniformToFill,
                    SnapsToDevicePixels = true
                };
                border.Child = image;
            }
            if (string.Equals(Convert.ToString(image.Tag), component.MediaPath,
                StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                image.Source = File.Exists(component.MediaPath) ?
                    LoadBitmapSource(component.MediaPath) : null;
                image.Tag = component.MediaPath;
            }
            catch
            {
                image.Source = null;
                image.Tag = component.MediaPath;
            }
        }

        private static void ApplyVideoComponentToVisual(EditorComponent component,
            Border border)
        {
            MediaElement media = border.Child as MediaElement;
            if (media != null && string.Equals(Convert.ToString(media.Tag),
                component.MediaPath, StringComparison.OrdinalIgnoreCase)) return;
            StopComponentVideo(border.Child);
            media = new MediaElement
            {
                Stretch = Stretch.UniformToFill,
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Stop,
                IsMuted = true,
                Tag = component.MediaPath
            };
            media.MediaEnded += ComponentVideo_MediaEnded;
            if (File.Exists(component.MediaPath))
            {
                media.Source = new Uri(component.MediaPath);
                media.Play();
            }
            border.Child = media;
        }

        private static void ComponentVideo_MediaEnded(object sender, RoutedEventArgs e)
        {
            MediaElement media = sender as MediaElement;
            if (media == null) return;
            media.Position = TimeSpan.Zero;
            media.Play();
        }

        private static void StopComponentVideo(UIElement child)
        {
            MediaElement media = child as MediaElement;
            if (media == null) return;
            try
            {
                media.Stop();
                media.Source = null;
            }
            catch { }
        }

        private void StopAllComponentVideos()
        {
            foreach (Border visual in _componentVisuals.Values)
                StopComponentVideo(visual.Child);
        }

        private static FrameworkElement CreateProgressVisual(EditorComponent component, double value)
        {
            double thickness = Math.Max(2, Math.Min(40, component.ProgressThickness));
            double fallbackLength = Math.Max(20, component.Width - component.PaddingLeft -
                component.PaddingRight - component.BorderThickness * 2);
            double length = component.ProgressLength > 0 ? component.ProgressLength : fallbackLength;
            string style = string.IsNullOrWhiteSpace(component.ProgressStyle) ? "Rounded" : component.ProgressStyle;
            System.Windows.Media.Brush accent = ParseBrush(component.Accent,
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 194, 255)));
            System.Windows.Media.Brush track = ParseBrush(component.ProgressTrackColor,
                new SolidColorBrush(System.Windows.Media.Color.FromArgb(51, 255, 255, 255)));

            if (string.Equals(style, "Segmented", StringComparison.OrdinalIgnoreCase))
            {
                UniformGrid segments = new UniformGrid();
                segments.Columns = 10;
                segments.Height = thickness;
                segments.Width = length;
                segments.HorizontalAlignment = HorizontalAlignment.Left;
                segments.VerticalAlignment = VerticalAlignment.Bottom;
                int activeSegments = (int)Math.Ceiling(value / 10.0);
                for (int index = 0; index < 10; index++)
                {
                    Border segment = new Border();
                    segment.Margin = new Thickness(index == 0 ? 0 : 2, 0, 0, 0);
                    segment.CornerRadius = new CornerRadius(Math.Min(3, thickness / 2));
                    segment.Background = index < activeSegments ? accent : track;
                    segments.Children.Add(segment);
                }
                return segments;
            }

            Grid bar = new Grid();
            bar.Height = thickness;
            bar.Width = length;
            bar.HorizontalAlignment = HorizontalAlignment.Left;
            bar.VerticalAlignment = VerticalAlignment.Bottom;
            double radius = string.Equals(style, "Flat", StringComparison.OrdinalIgnoreCase) ? 0 : thickness / 2;
            Border trackBorder = new Border();
            trackBorder.Background = track;
            trackBorder.CornerRadius = new CornerRadius(radius);
            bar.Children.Add(trackBorder);

            Border fill = new Border();
            fill.Background = accent;
            fill.CornerRadius = new CornerRadius(radius);
            fill.HorizontalAlignment = HorizontalAlignment.Left;
            fill.Width = length * value / 100.0;
            bar.Children.Add(fill);
            return bar;
        }

        private static System.Windows.Media.Brush ParseBrush(string value,
            System.Windows.Media.Brush fallback)
        {
            try { return (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(value); }
            catch { return fallback; }
        }

        private static System.Windows.Media.Brush BrushWithOpacity(string value, double opacity,
            System.Windows.Media.Brush fallback)
        {
            try
            {
                System.Windows.Media.Color color =
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value);
                color.A = (byte)Math.Round(Math.Max(0, Math.Min(1, opacity)) * 255);
                return new SolidColorBrush(color);
            }
            catch { return fallback; }
        }

        private static System.Windows.Media.Brush GetComponentBorderBrush(EditorComponent component)
        {
            return BrushWithOpacity(component.BorderColor, component.BorderOpacity,
                System.Windows.Media.Brushes.Transparent);
        }

        private static TextAlignment ParseTextAlignment(string value)
        {
            TextAlignment parsed;
            return Enum.TryParse<TextAlignment>(value, true, out parsed) ? parsed : TextAlignment.Left;
        }

        private static VerticalAlignment ParseVerticalAlignment(string value)
        {
            VerticalAlignment parsed;
            return Enum.TryParse<VerticalAlignment>(value, true, out parsed) ? parsed : VerticalAlignment.Center;
        }

        private void RefreshComponentVisuals()
        {
            foreach (EditorComponent component in _components)
            {
                Border visual;
                if (_componentVisuals.TryGetValue(component.Id, out visual))
                {
                    ApplyComponentToVisual(component, visual);
                }
            }
            if (_canvasPreviewWindow != null)
                _canvasPreviewWindow.RefreshOverlays(_selectedComponent);
        }

        private void Component_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Border border = sender as Border;
            if (border == null) return;
            EditorComponent component = border.Tag as EditorComponent;
            SelectComponent(component);
            _dragVisual = border;
            System.Windows.Point point = e.GetPosition(ComponentCanvas);
            _dragOffset = new System.Windows.Point(point.X - Canvas.GetLeft(border),
                point.Y - Canvas.GetTop(border));
            border.CaptureMouse();
            e.Handled = true;
        }

        private void Component_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragVisual == null || e.LeftButton != MouseButtonState.Pressed) return;
            EditorComponent component = _dragVisual.Tag as EditorComponent;
            if (component == null) return;
            System.Windows.Point point = e.GetPosition(ComponentCanvas);
            component.X = Math.Max(0, Math.Min(960 - component.Width, point.X - _dragOffset.X));
            component.Y = Math.Max(0, Math.Min(320 - component.Height, point.Y - _dragOffset.Y));
            Canvas.SetLeft(_dragVisual, component.X);
            Canvas.SetTop(_dragVisual, component.Y);
            PropertyX.Text = component.X.ToString("0");
            PropertyY.Text = component.Y.ToString("0");
            if (_canvasPreviewWindow != null)
                _canvasPreviewWindow.RefreshOverlays(component);
        }

        private void Component_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragVisual != null) _dragVisual.ReleaseMouseCapture();
            _dragVisual = null;
            e.Handled = true;
        }

        private void LayerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectComponent(LayerList.SelectedItem as EditorComponent);
        }

        private void SelectComponent(EditorComponent component)
        {
            _selectedComponent = component;
            if (component != null && LayerList.SelectedItem != component) LayerList.SelectedItem = component;
            if (CustomizeComponentButton != null) CustomizeComponentButton.IsEnabled = component != null;
            LoadPropertyEditor(component);
            RefreshComponentVisuals();
        }

        private void LoadPropertyEditor(EditorComponent component)
        {
            _propertyLoading = true;
            try
            {
                bool enabled = component != null;
                Control[] controls =
                {
                    PropertyName, PropertyText, PropertyMetric, PropertyFontFamily, PropertyFontSize,
                    PropertyHorizontalAlignment, PropertyVerticalAlignment, PropertyX, PropertyY,
                    PropertyWidth, PropertyHeight, PropertyPaddingLeft, PropertyPaddingTop,
                    PropertyPaddingRight, PropertyPaddingBottom, PropertyColorButton, PropertyBackgroundButton,
                    PropertyBackgroundOpacity, PropertyBorderColorButton, PropertyBorderOpacity,
                    PropertyBorderThickness, PropertyCornerRadius
                };
                foreach (Control control in controls) control.IsEnabled = enabled;
                bool mediaComponent = enabled &&
                    (component.Kind == EditorComponentKind.Image ||
                    component.Kind == EditorComponentKind.Video);
                PropertyText.IsEnabled = enabled &&
                    component.Kind != EditorComponentKind.Time && !mediaComponent;
                PropertyMetric.IsEnabled = enabled &&
                    (component.Kind == EditorComponentKind.Sensor || component.Kind == EditorComponentKind.Progress);
                PropertyFontFamily.IsEnabled = enabled && !mediaComponent;
                PropertyFontSize.IsEnabled = enabled && !mediaComponent;
                PropertyHorizontalAlignment.IsEnabled = enabled && !mediaComponent;
                PropertyVerticalAlignment.IsEnabled = enabled && !mediaComponent;
                PropertyColorButton.IsEnabled = enabled && !mediaComponent;
                if (!enabled) return;
                PropertyName.Text = component.Name;
                PropertyText.Text = component.Text;
                PropertyMetric.SelectedValue = component.MetricKey;
                SelectComboByContent(PropertyFontFamily, component.FontFamilyName);
                PropertyFontSize.Text = component.FontSize.ToString("0.#");
                SelectComboByTag(PropertyHorizontalAlignment, component.HorizontalTextAlignment);
                SelectComboByTag(PropertyVerticalAlignment, component.VerticalTextAlignment);
                PropertyX.Text = component.X.ToString("0");
                PropertyY.Text = component.Y.ToString("0");
                PropertyWidth.Text = component.Width.ToString("0");
                PropertyHeight.Text = component.Height.ToString("0");
                PropertyPaddingLeft.Text = component.PaddingLeft.ToString("0");
                PropertyPaddingTop.Text = component.PaddingTop.ToString("0");
                PropertyPaddingRight.Text = component.PaddingRight.ToString("0");
                PropertyPaddingBottom.Text = component.PaddingBottom.ToString("0");
                PropertyBackgroundOpacity.Text = component.BackgroundOpacity.ToString("0.##");
                PropertyBorderOpacity.Text = component.BorderOpacity.ToString("0.##");
                PropertyBorderThickness.Text = component.BorderThickness.ToString("0.#");
                PropertyCornerRadius.Text = component.CornerRadius.ToString("0.#");
                UpdateColorSwatches(component);
            }
            finally
            {
                _propertyLoading = false;
            }
        }

        private void PropertyEditor_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyPropertyEditor();
        }

        private void PropertyEditor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            ApplyPropertyEditor();
            EditorStatusText.Text = "属性已应用";
            e.Handled = true;
        }

        private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            System.Windows.Controls.TextBox focused = Keyboard.FocusedElement as
                System.Windows.Controls.TextBox;
            if (focused == null || !IsDescendantOf(focused, EditorPropertiesCard)) return;
            DependencyObject clicked = e.OriginalSource as DependencyObject;
            if (clicked != null && IsDescendantOf(clicked, focused)) return;
            ApplyPropertyEditor();
            Keyboard.ClearFocus();
        }

        private static bool IsDescendantOf(DependencyObject child, DependencyObject ancestor)
        {
            DependencyObject current = child;
            while (current != null)
            {
                if (ReferenceEquals(current, ancestor)) return true;
                try { current = VisualTreeHelper.GetParent(current); }
                catch { current = LogicalTreeHelper.GetParent(current); }
            }
            return false;
        }

        private void PropertySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ApplyPropertyEditor();
        }

        private void PropertyMetric_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_propertyLoading || _selectedComponent == null) return;
            MonitorMetricDefinition metric = PropertyMetric.SelectedItem as MonitorMetricDefinition;
            if (metric == null) return;
            _selectedComponent.MetricKey = metric.Key;
            _selectedComponent.SensorId = GetMappedSensorId(metric.Key);
            _selectedComponent.Text = metric.DisplayName;
            PropertyText.Text = metric.DisplayName;
            UpdateEditorSensorValues();
            RefreshComponentVisuals();
        }

        private void PropertyFontFamily_SelectionChanged(object sender,
            SelectionChangedEventArgs e)
        {
            if (_propertyLoading || _selectedComponent == null) return;
            ComboBoxItem selected = PropertyFontFamily.SelectedItem as ComboBoxItem;
            string familyName = selected == null ? PropertyFontFamily.Text :
                Convert.ToString(selected.Content);
            if (string.IsNullOrWhiteSpace(familyName)) return;
            _selectedComponent.FontFamilyName = familyName.Trim();
            RefreshComponentVisuals();
            EditorStatusText.Text = "字体已应用：" + _selectedComponent.FontFamilyName;
        }

        private void PropertyAlignment_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_propertyLoading || _selectedComponent == null) return;
            _selectedComponent.HorizontalTextAlignment = GetSelectedTag(PropertyHorizontalAlignment, "Left");
            _selectedComponent.VerticalTextAlignment = GetSelectedTag(PropertyVerticalAlignment, "Center");
            RefreshComponentVisuals();
        }

        private void PropertyAppearanceSlider_ValueChanged(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            ApplyPropertyEditor();
        }

        private void ApplyPropertyEditor()
        {
            if (_propertyLoading || _selectedComponent == null) return;
            _selectedComponent.Name = PropertyName.Text;
            if (_selectedComponent.Kind != EditorComponentKind.Time)
                _selectedComponent.Text = PropertyText.Text;
            _selectedComponent.FontFamilyName = GetSelectedContent(PropertyFontFamily,
                _selectedComponent.FontFamilyName);
            _selectedComponent.FontSize = ParseDouble(PropertyFontSize.Text, _selectedComponent.FontSize);
            _selectedComponent.HorizontalTextAlignment = GetSelectedTag(PropertyHorizontalAlignment, "Left");
            _selectedComponent.VerticalTextAlignment = GetSelectedTag(PropertyVerticalAlignment, "Center");
            _selectedComponent.X = ParseDouble(PropertyX.Text, _selectedComponent.X);
            _selectedComponent.Y = ParseDouble(PropertyY.Text, _selectedComponent.Y);
            _selectedComponent.Width = ParseDouble(PropertyWidth.Text, _selectedComponent.Width);
            _selectedComponent.Height = ParseDouble(PropertyHeight.Text, _selectedComponent.Height);
            _selectedComponent.BackgroundOpacity = ParseDouble(PropertyBackgroundOpacity.Text,
                _selectedComponent.BackgroundOpacity);
            _selectedComponent.BorderOpacity = ParseDouble(PropertyBorderOpacity.Text,
                _selectedComponent.BorderOpacity);
            _selectedComponent.PaddingLeft = Math.Max(0, ParseDouble(PropertyPaddingLeft.Text, _selectedComponent.PaddingLeft));
            _selectedComponent.PaddingTop = Math.Max(0, ParseDouble(PropertyPaddingTop.Text, _selectedComponent.PaddingTop));
            _selectedComponent.PaddingRight = Math.Max(0, ParseDouble(PropertyPaddingRight.Text, _selectedComponent.PaddingRight));
            _selectedComponent.PaddingBottom = Math.Max(0, ParseDouble(PropertyPaddingBottom.Text, _selectedComponent.PaddingBottom));
            _selectedComponent.BorderThickness = Math.Max(0, ParseDouble(PropertyBorderThickness.Text, _selectedComponent.BorderThickness));
            _selectedComponent.CornerRadius = Math.Max(0, ParseDouble(PropertyCornerRadius.Text, _selectedComponent.CornerRadius));
            RefreshComponentVisuals();
        }

        private void NumericInput_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            System.Windows.Controls.TextBox textBox = sender as System.Windows.Controls.TextBox;
            if (textBox == null || !textBox.IsEnabled) return;
            _numericDragTextBox = textBox;
            _numericDragStartPoint = e.GetPosition(this);
            _numericDragStartValue = ParseDouble(textBox.Text, 0);
            _numericDragActive = false;
        }

        private void NumericInput_MouseMove(object sender, MouseEventArgs e)
        {
            System.Windows.Controls.TextBox textBox = sender as System.Windows.Controls.TextBox;
            if (textBox == null || textBox != _numericDragTextBox ||
                e.LeftButton != MouseButtonState.Pressed) return;
            double delta = e.GetPosition(this).X - _numericDragStartPoint.X;
            if (!_numericDragActive && Math.Abs(delta) < 4) return;
            if (!_numericDragActive)
            {
                _numericDragActive = true;
                textBox.CaptureMouse();
            }
            string kind = Convert.ToString(textBox.Tag);
            double step = kind.EndsWith("Opacity", StringComparison.OrdinalIgnoreCase) ? 0.005 :
                kind == "BorderThickness" ? 0.05 : kind == "FontSize" ? 0.2 : 0.5;
            double value = ClampNumericValue(kind, _numericDragStartValue + delta * step);
            textBox.Text = (kind.EndsWith("Opacity", StringComparison.OrdinalIgnoreCase) ?
                value.ToString("0.00") : value.ToString("0.#"));
            ApplyPropertyEditor();
            e.Handled = true;
        }

        private void NumericInput_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            System.Windows.Controls.TextBox textBox = sender as System.Windows.Controls.TextBox;
            if (textBox != null && textBox.IsMouseCaptured) textBox.ReleaseMouseCapture();
            _numericDragTextBox = null;
            _numericDragActive = false;
        }

        private static double ClampNumericValue(string kind, double value)
        {
            if (kind == "FontSize") return Math.Max(8, Math.Min(120, value));
            if (kind.EndsWith("Opacity", StringComparison.OrdinalIgnoreCase))
                return Math.Max(0, Math.Min(1, value));
            if (kind == "X") return Math.Max(0, Math.Min(960, value));
            if (kind == "Y") return Math.Max(0, Math.Min(320, value));
            if (kind == "Width") return Math.Max(40, Math.Min(960, value));
            if (kind == "Height") return Math.Max(24, Math.Min(320, value));
            if (kind == "BorderThickness") return Math.Max(0, Math.Min(20, value));
            if (kind == "CornerRadius") return Math.Max(0, Math.Min(160, value));
            return Math.Max(0, Math.Min(500, value));
        }

        private static void SelectComboByTag(ComboBox combo, string tag)
        {
            combo.SelectedItem = combo.Items.Cast<object>().OfType<ComboBoxItem>().FirstOrDefault(
                delegate(ComboBoxItem item)
                {
                    return string.Equals(Convert.ToString(item.Tag), tag, StringComparison.OrdinalIgnoreCase);
                });
        }

        private static void SelectComboByContent(ComboBox combo, string content)
        {
            ComboBoxItem selected = combo.Items.Cast<object>().OfType<ComboBoxItem>()
                .FirstOrDefault(delegate(ComboBoxItem item)
                {
                    return string.Equals(Convert.ToString(item.Content), content,
                        StringComparison.OrdinalIgnoreCase);
                });
            combo.SelectedItem = selected ?? combo.Items.Cast<object>()
                .OfType<ComboBoxItem>().FirstOrDefault();
        }

        private static string GetSelectedContent(ComboBox combo, string fallback)
        {
            ComboBoxItem selected = combo.SelectedItem as ComboBoxItem;
            string content = selected == null ? null : Convert.ToString(selected.Content);
            return string.IsNullOrWhiteSpace(content) ? fallback : content;
        }

        private static string GetSelectedTag(ComboBox combo, string fallback)
        {
            ComboBoxItem item = combo.SelectedItem as ComboBoxItem;
            return item == null ? fallback : Convert.ToString(item.Tag);
        }

        private void OpenColorPicker_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedComponent == null) return;
            FrameworkElement source = sender as FrameworkElement;
            string target = Convert.ToString(source == null ? null : source.Tag);
            string current = target == "Foreground" ? _selectedComponent.Foreground :
                target == "Background" ? _selectedComponent.Background : _selectedComponent.BorderColor;
            System.Windows.Media.Color mediaColor;
            try
            {
                mediaColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(current);
            }
            catch
            {
                mediaColor = System.Windows.Media.Colors.White;
            }

            using (System.Windows.Forms.ColorDialog dialog = new System.Windows.Forms.ColorDialog())
            {
                dialog.AnyColor = true;
                dialog.AllowFullOpen = true;
                dialog.FullOpen = true;
                dialog.SolidColorOnly = false;
                dialog.Color = System.Drawing.Color.FromArgb(mediaColor.R, mediaColor.G, mediaColor.B);
                EditorStatusText.Text = "正在选择颜色…";
                IntPtr ownerHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (dialog.ShowDialog(new WindowHandleWrapper(ownerHandle)) !=
                    System.Windows.Forms.DialogResult.OK)
                {
                    EditorStatusText.Text = "已取消颜色选择";
                    return;
                }
                string hex = string.Format("#{0:X2}{1:X2}{2:X2}",
                    dialog.Color.R, dialog.Color.G, dialog.Color.B);
                if (target == "Foreground") _selectedComponent.Foreground = "#FF" + hex.Substring(1);
                else if (target == "Background") _selectedComponent.Background = hex;
                else _selectedComponent.BorderColor = hex;
            }
            UpdateColorSwatches(_selectedComponent);
            RefreshComponentVisuals();
            EditorStatusText.Text = "颜色已更新";
        }

        private void UpdateColorSwatches(EditorComponent component)
        {
            if (component == null) return;
            PropertyColorSwatch.Background = ParseBrush(component.Foreground,
                System.Windows.Media.Brushes.White);
            PropertyBackgroundSwatch.Background = ParseBrush(component.Background,
                System.Windows.Media.Brushes.Transparent);
            PropertyBorderColorSwatch.Background = ParseBrush(component.BorderColor,
                System.Windows.Media.Brushes.Transparent);
        }

        private void ToggleLayerVisibility_Click(object sender, RoutedEventArgs e)
        {
            FrameworkElement source = sender as FrameworkElement;
            EditorComponent component = source == null ? null : source.Tag as EditorComponent;
            if (component == null) return;
            component.Visible = !component.Visible;
            Border visual;
            if (_componentVisuals.TryGetValue(component.Id, out visual))
                ApplyComponentToVisual(component, visual);
            EditorStatusText.Text = component.Visible ? "图层已显示" : "图层已隐藏";
            e.Handled = true;
        }

        private sealed class WindowHandleWrapper : System.Windows.Forms.IWin32Window
        {
            public WindowHandleWrapper(IntPtr handle) { Handle = handle; }
            public IntPtr Handle { get; private set; }
        }

        private static double ParseDouble(string value, double fallback)
        {
            double parsed;
            return double.TryParse(value, out parsed) ? parsed : fallback;
        }

        private void DeleteLayer_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedComponent == null) return;
            Border visual;
            if (_componentVisuals.TryGetValue(_selectedComponent.Id, out visual))
            {
                StopComponentVideo(visual.Child);
                ComponentCanvas.Children.Remove(visual);
                _componentVisuals.Remove(_selectedComponent.Id);
            }
            _components.Remove(_selectedComponent);
            ReorderComponentVisuals();
            SelectComponent(_components.LastOrDefault());
        }

        private void MoveLayerUp_Click(object sender, RoutedEventArgs e)
        {
            MoveLayer(1);
        }

        private void MoveLayerDown_Click(object sender, RoutedEventArgs e)
        {
            MoveLayer(-1);
        }

        private void MoveLayer(int delta)
        {
            if (_selectedComponent == null) return;
            int index = _components.IndexOf(_selectedComponent);
            int target = Math.Max(0, Math.Min(_components.Count - 1, index + delta));
            if (target == index) return;
            _components.Move(index, target);
            ReorderComponentVisuals();
        }

        private void ReorderComponentVisuals()
        {
            EditorComponent selected = _selectedComponent;
            foreach (EditorComponent component in _components)
            {
                Border visual;
                if (_componentVisuals.TryGetValue(component.Id, out visual))
                    Panel.SetZIndex(visual, _components.IndexOf(component));
            }
            if (_layerView != null) _layerView.Refresh();
            if (selected != null && _components.Contains(selected))
                LayerList.SelectedItem = selected;
        }

        private sealed class LayerZOrderComparer : IComparer
        {
            private readonly IList<EditorComponent> _components;

            public LayerZOrderComparer(IList<EditorComponent> components)
            {
                _components = components;
            }

            public int Compare(object x, object y)
            {
                EditorComponent left = x as EditorComponent;
                EditorComponent right = y as EditorComponent;
                return _components.IndexOf(right).CompareTo(_components.IndexOf(left));
            }
        }

        private void OpenCanvasPreview_Click(object sender, RoutedEventArgs e)
        {
            ApplyPropertyEditor();
            RefreshComponentVisuals();
            if (_canvasPreviewWindow != null)
            {
                _canvasPreviewWindow.Activate();
                _canvasPreviewWindow.RefreshOverlays(_selectedComponent);
                return;
            }
            _canvasPreviewWindow = new CanvasPreviewWindow(DesignerSurface, _components,
                PreviewSelectComponent, PreviewComponentMoved);
            _canvasPreviewWindow.Owner = this;
            _canvasPreviewWindow.Closed += delegate { _canvasPreviewWindow = null; };
            _canvasPreviewWindow.Show();
        }

        private void PreviewSelectComponent(EditorComponent component)
        {
            if (component == null) return;
            SelectComponent(component);
            EditorStatusText.Text = "已在放大画布中选择：" + component.Name;
        }

        private void PreviewComponentMoved(EditorComponent component)
        {
            if (component == null) return;
            if (_selectedComponent != component) SelectComponent(component);
            PropertyX.Text = component.X.ToString("0");
            PropertyY.Text = component.Y.ToString("0");
            RefreshComponentVisuals();
        }

        private void PhotoBackground_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp";
            if (dialog.ShowDialog(this) == true) SetEditorBackground("Photo", dialog.FileName);
        }

        private void VideoBackground_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "视频|*.mp4;*.wmv;*.avi;*.mov";
            if (dialog.ShowDialog(this) == true) SetEditorBackground("Video", dialog.FileName);
        }

        private void ClearBackground_Click(object sender, RoutedEventArgs e)
        {
            SetEditorBackground("Color", null);
        }

        private void SetEditorBackground(string kind, string path)
        {
            _project.BackgroundKind = kind;
            _project.BackgroundPath = path;
            EditorBackgroundVideo.Stop();
            EditorBackgroundVideo.Source = null;
            EditorBackgroundImage.Source = null;
            if (kind == "Photo" && File.Exists(path))
            {
                EditorBackgroundImage.Source = LoadBitmapSource(path);
                EditorStatusText.Text = "照片背景已载入";
            }
            else if (kind == "Video" && File.Exists(path))
            {
                EditorBackgroundVideo.Source = new Uri(path);
                EditorBackgroundVideo.Play();
                EditorStatusText.Text = "视频背景正在预览";
            }
            else
            {
                DesignerSurface.Background = ParseBrush(_project.BackgroundColor,
                    new SolidColorBrush(System.Windows.Media.Color.FromRgb(8, 17, 31)));
                EditorStatusText.Text = "已使用纯色背景";
            }
        }

        private void EditorBackgroundVideo_MediaEnded(object sender, RoutedEventArgs e)
        {
            EditorBackgroundVideo.Position = TimeSpan.Zero;
            EditorBackgroundVideo.Play();
        }

        private void SaveTheme_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Filter = "TURZX 监控主题|*.turzxtheme.xml";
            dialog.FileName = "我的监控主题.turzxtheme.xml";
            dialog.InitialDirectory = ThemeProjectStore.PresetDirectory;
            if (dialog.ShowDialog(this) == true)
            {
                try
                {
                    ThemeProjectStore.Save(BuildProjectFromEditor(), dialog.FileName);
                    RefreshThemePresets(dialog.FileName);
                    EditorStatusText.Text = "主题、组件与背景资源已完整保存";
                }
                catch (Exception exception)
                {
                    EditorStatusText.Text = "主题保存失败：" + exception.Message;
                }
            }
        }

        private void OpenTheme_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "TURZX 监控主题|*.turzxtheme.xml";
            dialog.InitialDirectory = ThemeProjectStore.PresetDirectory;
            if (dialog.ShowDialog(this) == true)
            {
                LoadProjectIntoEditor(ThemeProjectStore.Load(dialog.FileName));
                EditorStatusText.Text = "主题已打开";
            }
        }

        private void RefreshThemePresets_Click(object sender, RoutedEventArgs e)
        {
            RefreshThemePresets();
            EditorStatusText.Text = _themePresets.Count + " 个主题预设";
        }

        private void RefreshThemePresets(string preferredPath = null)
        {
            _themePresetLoading = true;
            try
            {
                Directory.CreateDirectory(ThemeProjectStore.PresetDirectory);
                string[] paths = Directory.GetFiles(ThemeProjectStore.PresetDirectory,
                    "*.turzxtheme.xml", SearchOption.TopDirectoryOnly);
                if (paths.Length == 0)
                {
                    string defaultPreset = Path.Combine(ThemeProjectStore.PresetDirectory,
                        "默认监控主题.turzxtheme.xml");
                    ThemeProjectStore.Save(CreateDefaultProject(), defaultPreset);
                    paths = new[] { defaultPreset };
                }

                _themePresets.Clear();
                foreach (string path in paths.OrderBy(delegate(string value) { return value; }))
                {
                    string fileName = Path.GetFileName(path);
                    const string suffix = ".turzxtheme.xml";
                    string name = fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ?
                        fileName.Substring(0, fileName.Length - suffix.Length) :
                        Path.GetFileNameWithoutExtension(fileName);
                    _themePresets.Add(new ThemePresetItem { Name = name, Path = path });
                }

                ThemePresetItem selected = !string.IsNullOrWhiteSpace(preferredPath) ?
                    _themePresets.FirstOrDefault(delegate(ThemePresetItem item)
                    {
                        return string.Equals(item.Path, preferredPath, StringComparison.OrdinalIgnoreCase);
                    }) : null;
                ThemePresetCombo.SelectedItem = selected;
                if (selected == null) ThemePresetCombo.Text = "选择一个主题预设";
            }
            catch (Exception exception)
            {
                EditorStatusText.Text = "读取主题预设失败：" + exception.Message;
            }
            finally { _themePresetLoading = false; }
        }

        private void ThemePresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_themePresetLoading) return;
            ThemePresetItem preset = ThemePresetCombo.SelectedItem as ThemePresetItem;
            if (preset == null || string.IsNullOrWhiteSpace(preset.Path)) return;
            try
            {
                LoadProjectIntoEditor(ThemeProjectStore.Load(preset.Path));
                EditorStatusText.Text = "已应用主题预设：" + preset.Name;
            }
            catch (Exception exception)
            {
                EditorStatusText.Text = "主题预设读取失败：" + exception.Message;
            }
        }

        private void CustomizeComponent_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedComponent == null)
            {
                EditorStatusText.Text = "请先在画布或图层列表中选择组件";
                return;
            }
            ApplyPropertyEditor();
            ComponentCustomizerWindow dialog = new ComponentCustomizerWindow(_selectedComponent);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                UpdateEditorWeatherValues();
                LoadPropertyEditor(_selectedComponent);
                RefreshComponentVisuals();
                EditorStatusText.Text = "已应用组件定制：" + _selectedComponent.Name;
            }
        }

        private ThemeProject BuildProjectFromEditor()
        {
            _project.Components = _components.ToList();
            return _project;
        }

        private void LoadProjectIntoEditor(ThemeProject project)
        {
            _project = project ?? CreateDefaultProject();
            StopAllComponentVideos();
            _components.Clear();
            _componentVisuals.Clear();
            ComponentCanvas.Children.Clear();
            foreach (EditorComponent component in _project.Components)
            {
                if (string.IsNullOrEmpty(component.Id)) component.Id = Guid.NewGuid().ToString("N");
                if ((component.Kind == EditorComponentKind.Sensor || component.Kind == EditorComponentKind.Progress) &&
                    string.IsNullOrEmpty(component.MetricKey)) component.MetricKey = InferMetricKey(component);
                if (component.Kind == EditorComponentKind.Progress && component.ProgressLength <= 0)
                    component.ProgressLength = Math.Max(20, component.Width - component.PaddingLeft -
                        component.PaddingRight - component.BorderThickness * 2);
                _components.Add(component);
                AddComponentVisual(component);
            }
            SetEditorBackground(_project.BackgroundKind, _project.BackgroundPath);
            ReorderComponentVisuals();
            SelectComponent(_components.LastOrDefault());
        }

        private static ThemeProject CreateDefaultProject()
        {
            ThemeProject project = new ThemeProject();
            project.Components.Add(CreateComponent(EditorComponentKind.Text, 34, 24));
            project.Components.Add(CreateComponent(EditorComponentKind.Time, 720, 22));
            EditorComponent cpu = CreateComponent(EditorComponentKind.Sensor, 34, 216);
            cpu.Text = "CPU 总负载";
            cpu.MetricKey = "cpu.usage";
            cpu.SensorId = "os/cpu-total";
            project.Components.Add(cpu);
            EditorComponent temperature = CreateComponent(EditorComponentKind.Sensor, 320, 216);
            temperature.Text = "CPU 温度";
            temperature.MetricKey = "cpu.temperature";
            project.Components.Add(temperature);
            EditorComponent memory = CreateComponent(EditorComponentKind.Progress, 626, 224);
            memory.Text = "内存使用";
            memory.MetricKey = "memory.usage";
            memory.SensorId = "os/memory-used";
            project.Components.Add(memory);
            return project;
        }

        private async void StartTheme_Click(object sender, RoutedEventArgs e)
        {
            await StartThemeAsync(true);
        }

        private async Task StartThemeAsync(bool rememberOutput)
        {
            if (_themeCancellation != null) _themeCancellation.Cancel();
            if (_videoCancellation != null) _videoCancellation.Cancel();

            CancellationTokenSource cancellation = new CancellationTokenSource();
            _themeCancellation = cancellation;
            CancellationToken token = cancellation.Token;
            StartThemeButton.IsEnabled = false;
            StopThemeButton.IsEnabled = true;
            int frame = 0;
            Stopwatch cadence = Stopwatch.StartNew();
            long nextFrameAt = 0;
            try
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    UpdateEditorSensorValues();
                    RefreshComponentVisuals();

                    byte[] logicalPng = RenderDesignerToPng();
                    byte[] transport = await Task.Run(delegate
                    {
                        using (MemoryStream stream = new MemoryStream(logicalPng))
                        using (System.Drawing.Image image = System.Drawing.Image.FromStream(stream))
                        {
                            return ImageContentRenderer.CreateTransportPng(image,
                                ImageFitMode.Stretch, System.Drawing.Color.Black);
                        }
                    }, token);
                    await Task.Run(delegate { SendFrame(transport); }, token);
                    frame++;
                    if (frame == 1)
                    {
                        SetTrayStatus("实时监控");
                        if (rememberOutput)
                        {
                            try
                            {
                                ThemeProjectStore.Save(BuildProjectFromEditor(),
                                    ThemeProjectStore.DefaultPath);
                            }
                            catch { }
                            RememberOutput(new OutputSessionSettings
                            {
                                Mode = OutputSessionMode.Theme
                            });
                        }
                    }
                    EditorStatusText.Text = "实时显示中 · 已刷新 " + frame + " 次";
                    SetDashboardPreview(logicalPng);
                    nextFrameAt += 250;
                    long delay = nextFrameAt - cadence.ElapsedMilliseconds;
                    if (delay > 0) await Task.Delay((int)delay, token);
                    else nextFrameAt = cadence.ElapsedMilliseconds;
                }
            }
            catch (OperationCanceledException)
            {
                EditorStatusText.Text = "实时显示已停止";
            }
            catch (Exception exception)
            {
                EditorStatusText.Text = "实时显示失败：" + exception.Message;
                if (_startMinimized)
                    ShowTrayError("实时监控恢复失败", exception.Message);
            }
            finally
            {
                if (ReferenceEquals(_themeCancellation, cancellation))
                {
                    _themeCancellation.Dispose();
                    _themeCancellation = null;
                    StartThemeButton.IsEnabled = true;
                    StopThemeButton.IsEnabled = false;
                }
            }
        }

        private void StopTheme_Click(object sender, RoutedEventArgs e)
        {
            StopThemeOutput("正在停止实时显示…");
        }

        private void StopThemeOutput(string status)
        {
            if (_themeCancellation == null) return;
            EditorStatusText.Text = status;
            _themeCancellation.Cancel();
        }

        private byte[] RenderDesignerToPng()
        {
            DesignerSurface.Measure(new System.Windows.Size(960, 320));
            DesignerSurface.Arrange(new Rect(0, 0, 960, 320));
            DesignerSurface.UpdateLayout();
            RenderTargetBitmap bitmap = new RenderTargetBitmap(960, 320, 96, 96,
                PixelFormats.Pbgra32);
            bitmap.Render(DesignerSurface);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (MemoryStream stream = new MemoryStream())
            {
                encoder.Save(stream);
                return stream.ToArray();
            }
        }

        private async void CreativeGenerate_Click(object sender, RoutedEventArgs e)
        {
            string prompt = (CreativePromptText.Text ?? string.Empty).Trim();
            if (prompt.Length == 0)
            {
                CreativeStatusText.Text = "请先输入画面提示词";
                return;
            }
            if (string.IsNullOrWhiteSpace(_aiImageSettings.Endpoint) ||
                string.IsNullOrWhiteSpace(_aiImageSettings.Model) ||
                string.IsNullOrWhiteSpace(_aiImageSettings.ApiKey))
            {
                CreativeStatusText.Text = "请先在设置中完成 API 配置";
                return;
            }

            string style = GetSelectedTag(CreativeStyleCombo, string.Empty);
            if (!string.IsNullOrWhiteSpace(style))
                prompt += "\n视觉风格：" + style + "。";
            prompt += "\n请为 3:1 横向小屏幕创作背景，主体清晰，构图横向延展，" +
                "重要内容避开边缘，不要添加文字、水印或可读数值。";
            string quality = GetSelectedTag(CreativeQualityCombo, "standard");

            if (_creativeGenerationCancellation != null)
                _creativeGenerationCancellation.Cancel();
            _creativeGenerationCancellation = new CancellationTokenSource();
            CreativeGenerateButton.IsEnabled = false;
            CreativeGenerateButton.Content = "生成中…";
            CreativeStatusText.Text = "AI 正在绘制背景，通常需要几十秒…";
            CreativePreviewStateText.Text = "正在生成";
            try
            {
                _creativeImageBytes = await _aiImageService.GenerateAsync(
                    _aiImageSettings, prompt, quality,
                    _creativeGenerationCancellation.Token);
                CreativePreviewImage.Source = LoadBitmapSource(_creativeImageBytes);
                CreativeSaveButton.IsEnabled = true;
                CreativePreviewStateText.Text = "生成完成";
                CreativeStatusText.Text = "背景已生成，可保存并用于主题编辑器";
            }
            catch (OperationCanceledException)
            {
                CreativeStatusText.Text = "生成已取消";
                CreativePreviewStateText.Text = "等待生成";
            }
            catch (Exception exception)
            {
                CreativeStatusText.Text = "生成失败：" + exception.Message;
                CreativePreviewStateText.Text = "生成失败";
            }
            finally
            {
                CreativeGenerateButton.IsEnabled = true;
                CreativeGenerateButton.Content = "生成背景";
            }
        }

        private void CreativeLoadPrompt_Click(object sender, RoutedEventArgs e)
        {
            PromptLibraryWindow dialog = new PromptLibraryWindow
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true ||
                string.IsNullOrWhiteSpace(dialog.SelectedPrompt)) return;
            CreativePromptText.Text = dialog.SelectedPrompt;
            CreativePromptText.Focus();
            CreativePromptText.CaretIndex = CreativePromptText.Text.Length;
            CreativeStatusText.Text = "预设提示词已载入，可继续修改后生成";
        }

        private void CreativeSaveImage_Click(object sender, RoutedEventArgs e)
        {
            if (CreativePreviewImage.Source == null) return;
            string imagesDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images");
            try
            {
                Directory.CreateDirectory(imagesDirectory);
            }
            catch (Exception exception)
            {
                CreativeStatusText.Text = "无法创建 Images 文件夹：" + exception.Message;
                return;
            }
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Filter = "PNG 图片|*.png";
            dialog.InitialDirectory = imagesDirectory;
            dialog.FileName = "TURZX-AI-Background-" +
                DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png";
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                File.WriteAllBytes(dialog.FileName,
                    RenderElementToPng(CreativePreviewSurface, 960, 320));
                CreativeStatusText.Text = "图片已保存：" + Path.GetFileName(dialog.FileName);
            }
            catch (Exception exception)
            {
                CreativeStatusText.Text = "保存失败：" + exception.Message;
            }
        }

        private void CreativeClear_Click(object sender, RoutedEventArgs e)
        {
            if (_creativeGenerationCancellation != null)
                _creativeGenerationCancellation.Cancel();
            _creativeImageBytes = null;
            CreativePreviewImage.Source = null;
            CreativePromptText.Clear();
            CreativeSaveButton.IsEnabled = false;
            CreativePreviewStateText.Text = "等待生成";
            CreativeStatusText.Text = "已清空";
        }

        private void RefreshMediaLibraries()
        {
            string imageSelection = ImageLibraryCombo.SelectedValue as string ?? _imagePath;
            string videoSelection = VideoLibraryCombo.SelectedValue as string ?? _videoPath;
            string automationSelection = AutomationMediaCombo.SelectedValue as string;
            _mediaLibraryLoading = true;
            try
            {
                RefreshMediaCollection(_imageLibraryItems,
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images"),
                    new[] { ".png", ".jpg", ".jpeg", ".bmp" });
                RefreshMediaCollection(_videoLibraryItems,
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "videos"),
                    new[] { ".mp4", ".mov", ".avi", ".mkv", ".wmv" });
                SelectMediaPath(ImageLibraryCombo, imageSelection);
                SelectMediaPath(VideoLibraryCombo, videoSelection);
                if (_selectedAutomationAction != null)
                {
                    if (_selectedAutomationAction.Kind == AutomationActionKind.ShowImage)
                        AutomationMediaCombo.ItemsSource = _imageLibraryItems;
                    else if (_selectedAutomationAction.Kind == AutomationActionKind.PlayVideo)
                        AutomationMediaCombo.ItemsSource = _videoLibraryItems;
                    SelectMediaPath(AutomationMediaCombo,
                        string.IsNullOrWhiteSpace(automationSelection) ?
                        _selectedAutomationAction.FilePath : automationSelection);
                }
            }
            finally
            {
                _mediaLibraryLoading = false;
            }
        }

        private static void RefreshMediaCollection(
            ObservableCollection<MediaLibraryItem> target, string directory,
            IEnumerable<string> extensions)
        {
            HashSet<string> allowed = new HashSet<string>(extensions,
                StringComparer.OrdinalIgnoreCase);
            target.Clear();
            if (!Directory.Exists(directory)) return;
            foreach (string file in Directory.GetFiles(directory)
                .Where(delegate(string path)
                {
                    return allowed.Contains(Path.GetExtension(path));
                })
                .OrderBy(delegate(string path)
                {
                    return Path.GetFileName(path);
                }, StringComparer.CurrentCultureIgnoreCase))
                target.Add(new MediaLibraryItem
                {
                    Name = Path.GetFileName(file),
                    FullPath = Path.GetFullPath(file)
                });
        }

        private static void SelectMediaPath(ComboBox combo, string path)
        {
            if (combo == null) return;
            combo.SelectedValue = string.IsNullOrWhiteSpace(path) ? null :
                Path.GetFullPath(path);
        }

        private void ImageLibraryCombo_DropDownOpened(object sender, EventArgs e)
        {
            RefreshMediaLibraries();
        }

        private void ImageLibraryCombo_SelectionChanged(object sender,
            SelectionChangedEventArgs e)
        {
            if (_mediaLibraryLoading) return;
            string path = ImageLibraryCombo.SelectedValue as string;
            if (!string.IsNullOrWhiteSpace(path)) LoadImageFile(path);
        }

        private void LoadImageFile(string path)
        {
            try
            {
                _imagePath = path;
                ImagePathText.Text = Path.GetFileName(_imagePath);
                ImagePreview.Source = LoadBitmapSource(_imagePath);
                ResetImageTransform();
                ImageStatusText.Text = "图片已载入";
                if (!_mediaLibraryLoading)
                {
                    _mediaLibraryLoading = true;
                    SelectMediaPath(ImageLibraryCombo, _imagePath);
                    _mediaLibraryLoading = false;
                }
            }
            catch (Exception exception)
            {
                ImageStatusText.Text = "图片读取失败：" + exception.Message;
            }
        }

        private void ChooseImage_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp";
            if (dialog.ShowDialog(this) == true)
                LoadImageFile(dialog.FileName);
        }

        private void ImageFitCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ImageContentHost == null) return;
            switch (ImageFitCombo.SelectedIndex)
            {
                case 0: ImageContentHost.Stretch = Stretch.Uniform; break;
                case 1: ImageContentHost.Stretch = Stretch.UniformToFill; break;
                case 2: ImageContentHost.Stretch = Stretch.Fill; break;
                default: ImageContentHost.Stretch = Stretch.Uniform; break;
            }
            ResetImageTransform();
        }

        private void ImageRotationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ImagePreviewRotation == null) return;
            ImagePreviewRotation.Angle = GetRotationDegrees(ImageRotationCombo.SelectedIndex);
            ResetImageTransform();
        }

        private void ResetImageTransform()
        {
            if (ImagePreviewScale == null || ImagePreviewTranslate == null) return;
            ImagePreviewScale.ScaleX = 1;
            ImagePreviewScale.ScaleY = 1;
            ImagePreviewTranslate.X = 0;
            ImagePreviewTranslate.Y = 0;
        }

        private void ImagePreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ImageFitCombo.SelectedIndex != 3) ImageFitCombo.SelectedIndex = 3;
            _imageDragging = true;
            _imageDragPoint = e.GetPosition(ImagePreviewSurface);
            ImagePreviewSurface.CaptureMouse();
            e.Handled = true;
        }

        private void ImagePreview_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_imageDragging || e.LeftButton != MouseButtonState.Pressed) return;
            System.Windows.Point current = e.GetPosition(ImagePreviewSurface);
            ImagePreviewTranslate.X += current.X - _imageDragPoint.X;
            ImagePreviewTranslate.Y += current.Y - _imageDragPoint.Y;
            _imageDragPoint = current;
            e.Handled = true;
        }

        private void ImagePreview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _imageDragging = false;
            ImagePreviewSurface.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void ImagePreview_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (ImageFitCombo.SelectedIndex != 3) ImageFitCombo.SelectedIndex = 3;
            double factor = e.Delta > 0 ? 1.08 : 1.0 / 1.08;
            double scale = Math.Max(1, Math.Min(8, ImagePreviewScale.ScaleX * factor));
            ImagePreviewScale.ScaleX = scale;
            ImagePreviewScale.ScaleY = scale;
            e.Handled = true;
        }

        private async void SendImage_Click(object sender, RoutedEventArgs e)
        {
            await SendImageAsync(true);
        }

        private async Task<bool> SendImageAsync(bool rememberOutput)
        {
            if (string.IsNullOrEmpty(_imagePath))
            {
                ImageStatusText.Text = "请先选择图片";
                return false;
            }
            try
            {
                StopThemeOutput("图片输出已接管屏幕");
                ImageStatusText.Text = "正在生成并发送…";
                string path = _imagePath;
                int fitIndex = ImageFitCombo.SelectedIndex;
                int rotation = GetRotationDegrees(ImageRotationCombo.SelectedIndex);
                if (fitIndex == 3)
                {
                    byte[] logicalPng = RenderElementToPng(ImagePreviewSurface, 960, 320);
                    await Task.Run(delegate
                    {
                        using (MemoryStream stream = new MemoryStream(logicalPng))
                        using (System.Drawing.Image image = System.Drawing.Image.FromStream(stream))
                        {
                            SendFrame(ImageContentRenderer.CreateTransportPng(image,
                                ImageFitMode.Stretch, System.Drawing.Color.Black));
                        }
                    });
                }
                else
                {
                    await Task.Run(delegate
                    {
                        using (Bitmap bitmap = ImageContentRenderer.LoadOrientedBitmap(path))
                        {
                            ApplyManualRotation(bitmap,
                                rotation);
                            ImageFitMode fit = fitIndex == 0 ? ImageFitMode.Contain :
                                fitIndex == 2 ? ImageFitMode.Stretch : ImageFitMode.Cover;
                            byte[] png = ImageContentRenderer.CreateTransportPng(bitmap, fit,
                                System.Drawing.Color.Black);
                            SendFrame(png);
                        }
                    });
                }
                if (rememberOutput) RememberOutput(CaptureImageOutput());
                SetTrayStatus("图片显示");
                ImageStatusText.Text = "图片已显示在屏幕上";
                return true;
            }
            catch (Exception exception)
            {
                ImageStatusText.Text = "发送失败：" + exception.Message;
                if (_startMinimized)
                    ShowTrayError("图片恢复失败", exception.Message);
                return false;
            }
        }

        private void VideoLibraryCombo_DropDownOpened(object sender, EventArgs e)
        {
            RefreshMediaLibraries();
        }

        private async void VideoLibraryCombo_SelectionChanged(object sender,
            SelectionChangedEventArgs e)
        {
            if (_mediaLibraryLoading) return;
            string path = VideoLibraryCombo.SelectedValue as string;
            if (!string.IsNullOrWhiteSpace(path)) await LoadVideoFileAsync(path);
        }

        private async Task LoadVideoFileAsync(string path)
        {
            _videoPath = path;
            VideoPathText.Text = Path.GetFileName(_videoPath);
            VideoStatusText.Text = "正在读取视频信息…";
            try
            {
                string ffmpeg = VideoDecoderLocator.FindFfmpeg();
                if (string.IsNullOrEmpty(ffmpeg))
                    throw new InvalidOperationException("未找到 FFmpeg。");
                string selectedPath = _videoPath;
                _videoMetadata = await Task.Run(delegate
                {
                    return VideoProbe.Read(ffmpeg, selectedPath);
                });
                _videoScreenEncoded = _videoMetadata.LooksScreenEncoded;
                VideoPreview.Width = _videoMetadata.Width;
                VideoPreview.Height = _videoMetadata.Height;
                ApplyVideoPreviewRotation();
                ResetVideoTransform();
                VideoPreview.Source = new Uri(_videoPath);
                VideoPreview.Play();
                VideoStatusText.Text = "视频已载入 · " + _videoMetadata.Width + " × " +
                    _videoMetadata.Height;
                if (!_mediaLibraryLoading)
                {
                    _mediaLibraryLoading = true;
                    SelectMediaPath(VideoLibraryCombo, _videoPath);
                    _mediaLibraryLoading = false;
                }
            }
            catch (Exception exception)
            {
                VideoStatusText.Text = "视频读取失败：" + exception.Message;
            }
        }

        private async void ChooseVideo_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "视频|*.mp4;*.mov;*.avi;*.mkv;*.wmv";
            if (dialog.ShowDialog(this) == true)
                await LoadVideoFileAsync(dialog.FileName);
        }

        private void VideoFitCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VideoContentHost == null) return;
            switch (VideoFitCombo.SelectedIndex)
            {
                case 0: VideoContentHost.Stretch = Stretch.UniformToFill; break;
                case 1: VideoContentHost.Stretch = Stretch.Uniform; break;
                case 2: VideoContentHost.Stretch = Stretch.Fill; break;
                default: VideoContentHost.Stretch = Stretch.Uniform; break;
            }
            ResetVideoTransform();
        }

        private void VideoRotationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyVideoPreviewRotation();
            ResetVideoTransform();
        }

        private void ApplyVideoPreviewRotation()
        {
            if (VideoPreviewRotation == null) return;
            int normalization = _videoScreenEncoded ? 270 : 0;
            VideoPreviewRotation.Angle = (normalization +
                GetRotationDegrees(VideoRotationCombo == null ? 0 :
                    VideoRotationCombo.SelectedIndex)) % 360;
        }

        private void ResetVideoTransform()
        {
            if (VideoPreviewScale == null || VideoPreviewTranslate == null) return;
            VideoPreviewScale.ScaleX = 1;
            VideoPreviewScale.ScaleY = 1;
            VideoPreviewTranslate.X = 0;
            VideoPreviewTranslate.Y = 0;
        }

        private void VideoPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (VideoFitCombo.SelectedIndex != 3) VideoFitCombo.SelectedIndex = 3;
            _videoDragging = true;
            _videoDragPoint = e.GetPosition(VideoPreviewSurface);
            VideoPreviewSurface.CaptureMouse();
            e.Handled = true;
        }

        private void VideoPreview_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_videoDragging || e.LeftButton != MouseButtonState.Pressed) return;
            System.Windows.Point current = e.GetPosition(VideoPreviewSurface);
            VideoPreviewTranslate.X += current.X - _videoDragPoint.X;
            VideoPreviewTranslate.Y += current.Y - _videoDragPoint.Y;
            _videoDragPoint = current;
            e.Handled = true;
        }

        private void VideoPreview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _videoDragging = false;
            VideoPreviewSurface.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void VideoPreview_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (VideoFitCombo.SelectedIndex != 3) VideoFitCombo.SelectedIndex = 3;
            double factor = e.Delta > 0 ? 1.08 : 1.0 / 1.08;
            double scale = Math.Max(1, Math.Min(8, VideoPreviewScale.ScaleX * factor));
            VideoPreviewScale.ScaleX = scale;
            VideoPreviewScale.ScaleY = scale;
            e.Handled = true;
        }

        private void VideoPreview_MediaEnded(object sender, RoutedEventArgs e)
        {
            VideoPreview.Position = TimeSpan.Zero;
            VideoPreview.Play();
        }

        private async void PlayVideo_Click(object sender, RoutedEventArgs e)
        {
            await PlayVideoAsync(true);
        }

        private async Task PlayVideoAsync(bool rememberOutput)
        {
            if (string.IsNullOrEmpty(_videoPath))
            {
                VideoStatusText.Text = "请先选择视频";
                return;
            }
            if (_videoCancellation != null) _videoCancellation.Cancel();
            StopThemeOutput("视频输出已接管屏幕");
            _videoCancellation = new CancellationTokenSource();
            CancellationToken token = _videoCancellation.Token;
            string path = _videoPath;
            int fitIndex = VideoFitCombo.SelectedIndex;
            int fps = GetSelectedFps();
            bool loop = VideoLoopCheck.IsChecked == true;
            int rotation = GetRotationDegrees(VideoRotationCombo.SelectedIndex);
            float customZoom = (float)VideoPreviewScale.ScaleX;
            float customOffsetX = (float)VideoPreviewTranslate.X;
            float customOffsetY = (float)VideoPreviewTranslate.Y;
            OutputSessionSettings rememberedSession = rememberOutput ?
                CaptureVideoOutput() : null;
            VideoStatusText.Text = "正在播放到屏幕…";
            try
            {
                await Task.Run(delegate
                {
                    string ffmpeg = VideoDecoderLocator.FindFfmpeg();
                    if (string.IsNullOrEmpty(ffmpeg)) throw new InvalidOperationException("未找到 FFmpeg。" );
                    VideoMetadata metadata = VideoProbe.Read(ffmpeg, path);
                    VideoPlaybackRequest request = new VideoPlaybackRequest
                    {
                        FilePath = path,
                        FitMode = fitIndex == 1 ? ImageFitMode.Contain :
                            fitIndex == 2 ? ImageFitMode.Stretch :
                            fitIndex == 3 ? ImageFitMode.Custom : ImageFitMode.Cover,
                        BackgroundWhite = false,
                        FramesPerSecond = fps,
                        OrientationMode = VideoOrientationMode.Auto,
                        Loop = loop,
                        CustomCrop = RectangleF.Empty,
                        RotationDegrees = rotation,
                        CustomZoom = customZoom,
                        CustomOffsetX = customOffsetX,
                        CustomOffsetY = customOffsetY,
                        CustomAspectRatio = CalculateVideoAspect(metadata, rotation)
                    };
                    PlayVideoFrames(ffmpeg, metadata, request, token,
                        rememberedSession == null ? (Action)null : delegate
                        {
                            RememberOutput(rememberedSession);
                            Dispatcher.BeginInvoke(new Action(delegate
                            {
                                SetTrayStatus("视频播放");
                            }));
                        });
                }, token);
                VideoStatusText.Text = "视频播放完成";
            }
            catch (OperationCanceledException)
            {
                VideoStatusText.Text = "视频已停止";
            }
            catch (Exception exception)
            {
                VideoStatusText.Text = "播放失败：" + exception.Message;
                if (_startMinimized)
                    ShowTrayError("视频恢复失败", exception.Message);
            }
        }

        private void PlayVideoFrames(string ffmpeg, VideoMetadata metadata,
            VideoPlaybackRequest request, CancellationToken token, Action started)
        {
            using (VideoFrameDecoder decoder = new VideoFrameDecoder(ffmpeg, request, metadata))
            using (WinUsbDevice device = WinUsbDevice.Open(VendorId, ProductId))
            {
                lock (UsbCommandSync)
                    RequireSuccess(device.Exchange(TurzxProtocol.CreateCommand(10), 10), "同步");
                int interval = 1000 / Math.Max(1, request.FramesPerSecond);
                int frame = 0;
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    Stopwatch timer = Stopwatch.StartNew();
                    byte[] png = decoder.ReadNextTransportPng(token);
                    if (png == null) break;
                    png = ApplyDisplayColorSettings(png);
                    lock (UsbCommandSync)
                        RequireSuccess(device.Exchange(TurzxProtocol.CreateImageUpload(png), 102), "视频帧");
                    frame++;
                    if (frame == 1 && started != null)
                    {
                        started();
                        started = null;
                    }
                    if (frame % 5 == 0)
                    {
                        int shown = frame;
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            VideoStatusText.Text = "已发送 " + shown + " 帧 · " + request.FramesPerSecond + " FPS";
                        }));
                    }
                    int wait = interval - (int)timer.ElapsedMilliseconds;
                    if (wait > 0) token.WaitHandle.WaitOne(wait);
                }
            }
        }

        private void StopVideo_Click(object sender, RoutedEventArgs e)
        {
            if (_videoCancellation != null) _videoCancellation.Cancel();
            VideoPreview.Stop();
            VideoStatusText.Text = "正在停止…";
        }

        private int GetSelectedFps()
        {
            int[] values = { 5, 10, 12, 15, 20, 24 };
            int index = Math.Max(0, Math.Min(values.Length - 1, VideoFpsCombo.SelectedIndex));
            return values[index];
        }

        private static int GetRotationDegrees(int selectedIndex)
        {
            int[] values = { 0, 90, 180, 270 };
            return values[Math.Max(0, Math.Min(values.Length - 1, selectedIndex))];
        }

        private static void ApplyManualRotation(Bitmap bitmap, int degrees)
        {
            switch (degrees)
            {
                case 90: bitmap.RotateFlip(RotateFlipType.Rotate90FlipNone); break;
                case 180: bitmap.RotateFlip(RotateFlipType.Rotate180FlipNone); break;
                case 270: bitmap.RotateFlip(RotateFlipType.Rotate270FlipNone); break;
            }
        }

        private static float CalculateVideoAspect(VideoMetadata metadata, int rotation)
        {
            double width = metadata.Width;
            double height = metadata.Height;
            if (metadata.LooksScreenEncoded)
            {
                double swap = width; width = height; height = swap;
            }
            if (rotation == 90 || rotation == 270)
            {
                double swap = width; width = height; height = swap;
            }
            return height <= 0 ? 3.0f : (float)(width / height);
        }

        private byte[] ApplyDisplayColorSettings(byte[] png)
        {
            int temperature;
            int hue;
            lock (_displaySettingsSync)
            {
                temperature = _displaySettings.ColorTemperature;
                hue = _displaySettings.Hue;
            }
            return DisplayColorProcessor.Apply(png, temperature, hue);
        }

        private void SendFrame(byte[] png)
        {
            byte[] adjusted = ApplyDisplayColorSettings(png);
            lock (UsbCommandSync)
            {
                using (WinUsbDevice device = WinUsbDevice.Open(VendorId, ProductId))
                {
                    RequireSuccess(device.Exchange(TurzxProtocol.CreateCommand(10), 10), "同步");
                    RequireSuccess(device.Exchange(TurzxProtocol.CreateImageUpload(adjusted), 102), "发送画面");
                }
            }
        }

        private static void RequireSuccess(byte[] response, string operation)
        {
            if (response == null || response.Length < 2 || response[1] != 0xC8)
                throw new InvalidOperationException(operation + "未得到设备成功响应。" );
        }

        private static byte[] RenderElementToPng(FrameworkElement element, int width, int height)
        {
            RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96, 96,
                PixelFormats.Pbgra32);
            bitmap.Render(element);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (MemoryStream stream = new MemoryStream())
            {
                encoder.Save(stream);
                return stream.ToArray();
            }
        }

        private async Task RefreshSensorServiceStateAsync()
        {
            SensorServiceState state = await Task.Run(
                delegate { return SensorServiceManager.GetState(); });
            if (!state.Installed)
            {
                SensorPermissionText.Text = "后台传感器服务尚未安装。当前只读取 Windows 基础数据；安装后可在普通权限下读取 CPU 温度、功耗和电压。";
            }
            else if (state.Reachable)
            {
                SensorPermissionText.Text = "后台传感器服务运行正常。当前界面保持普通权限，温度、功耗和电压由本机服务提供。";
            }
            else
            {
                SensorPermissionText.Text = "后台传感器服务状态：" + state.Detail;
            }
            InstallSensorServiceButton.IsEnabled = !state.Installed;
            UninstallSensorServiceButton.IsEnabled = state.Installed;
            RefreshSensorServiceButton.IsEnabled = true;
        }

        private async void InstallSensorService_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.MessageBoxResult confirmation = System.Windows.MessageBox.Show(this,
                "此操作将安装官方 PawnIO 2.x 内核驱动，并创建以 LocalSystem 权限自动启动的 TURZX 传感器服务。\n\n" +
                "内核驱动拥有系统级权限；TURZX 只让普通界面通过本机命名管道读取传感器，不会把驱动设备直接开放给普通用户。卸载 TURZX 服务时不会自动卸载 PawnIO，以免影响其他硬件监控软件；PawnIO 可在 Windows“已安装的应用”中单独移除。\n\n" +
                "是否继续？", "安装硬件传感器驱动",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);
            if (confirmation != System.Windows.MessageBoxResult.Yes) return;

            SetSensorServiceButtonsEnabled(false);
            SensorPermissionText.Text = "正在安装并启动后台传感器服务…";
            string operationMessage = null;
            try
            {
                int exitCode = await Task.Run(delegate
                {
                    return SensorServiceManager.RunElevatedCommand("--install");
                });
                if (exitCode != 0) operationMessage =
                    "传感器服务安装失败，退出代码：" + exitCode;
            }
            catch (Win32Exception exception)
            {
                operationMessage = exception.NativeErrorCode == 1223 ?
                    "已取消传感器服务安装。" : "无法启动安装程序：" + exception.Message;
            }
            catch (Exception exception)
            {
                operationMessage = "传感器服务安装失败：" + exception.Message;
            }
            await Task.Delay(700);
            await RefreshSensorServiceStateAsync();
            if (!string.IsNullOrEmpty(operationMessage))
                SensorPermissionText.Text = operationMessage;
            await RefreshSensorsAsync();
        }

        private async void UninstallSensorService_Click(object sender, RoutedEventArgs e)
        {
            SetSensorServiceButtonsEnabled(false);
            SensorPermissionText.Text = "正在停止并卸载后台传感器服务…";
            string operationMessage = null;
            try
            {
                int exitCode = await Task.Run(delegate
                {
                    return SensorServiceManager.RunElevatedCommand("--uninstall");
                });
                if (exitCode != 0) operationMessage =
                    "传感器服务卸载失败，退出代码：" + exitCode;
            }
            catch (Win32Exception exception)
            {
                operationMessage = exception.NativeErrorCode == 1223 ?
                    "已取消传感器服务卸载。" : "无法启动卸载程序：" + exception.Message;
            }
            catch (Exception exception)
            {
                operationMessage = "传感器服务卸载失败：" + exception.Message;
            }
            await Task.Delay(500);
            await RefreshSensorServiceStateAsync();
            if (!string.IsNullOrEmpty(operationMessage))
                SensorPermissionText.Text = operationMessage;
            await RefreshSensorsAsync();
        }

        private async void RefreshSensorService_Click(object sender, RoutedEventArgs e)
        {
            SetSensorServiceButtonsEnabled(false);
            try
            {
                await RefreshSensorServiceStateAsync();
                await RefreshSensorsAsync();
            }
            catch (Exception exception)
            {
                SensorPermissionText.Text = "刷新服务状态失败：" + exception.Message;
                SetSensorServiceButtonsEnabled(true);
            }
        }

        private void SetSensorServiceButtonsEnabled(bool enabled)
        {
            InstallSensorServiceButton.IsEnabled = enabled;
            UninstallSensorServiceButton.IsEnabled = enabled;
            RefreshSensorServiceButton.IsEnabled = enabled;
        }

        private static BitmapSource LoadBitmapSource(string path)
        {
            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private static BitmapSource LoadBitmapSource(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                throw new InvalidOperationException("图片数据为空。");
            BitmapImage bitmap = new BitmapImage();
            using (MemoryStream stream = new MemoryStream(bytes))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
            }
            return bitmap;
        }

        private void SetDashboardPreview(byte[] png)
        {
            byte[] previewBytes = png;
            try
            {
                using (MemoryStream probeStream = new MemoryStream(png))
                using (System.Drawing.Image probe = System.Drawing.Image.FromStream(probeStream))
                {
                    if (probe.Height > probe.Width)
                    {
                        using (Bitmap logical = ImageContentRenderer.CreateLogicalPreview(png))
                        using (MemoryStream converted = new MemoryStream())
                        {
                            logical.Save(converted, System.Drawing.Imaging.ImageFormat.Png);
                            previewBytes = converted.ToArray();
                        }
                    }
                }
            }
            catch
            {
            }
            BitmapImage bitmap = new BitmapImage();
            using (MemoryStream stream = new MemoryStream(previewBytes))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
            }
            DashboardPreviewImage.Source = bitmap;
        }

        private void ThemeSystem_Click(object sender, RoutedEventArgs e)
        {
            ApplicationThemeManager.ApplySystemTheme();
        }

        private void ThemeLight_Click(object sender, RoutedEventArgs e)
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Light);
        }

        private void ThemeDark_Click(object sender, RoutedEventArgs e)
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Dark);
        }
    }
}
