using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Turzx.ProtocolProbe;

namespace Turzx.ControlPanel
{
    internal sealed class ControlPanelForm : Form
    {
        private const ushort VendorId = 0x1CBE;
        private const ushort ProductId = 0x0046;
        private static readonly Color ScreenAccent = Color.FromArgb(46, 230, 166);

        private readonly ControlPanelSettings _settings;
        private readonly ToolTip _toolTip = new ToolTip();
        private AppTheme _theme;
        private TableLayoutPanel _root;
        private Label _deviceStatus;
        private SmoothImageBox _preview;
        private RoundedButton _refreshButton;
        private RoundedButton _settingsButton;
        private RoundedButton _liveButton;
        private RoundedButton _brightnessButton;
        private RoundedButton _systemModeButton;
        private RoundedButton _imageModeButton;
        private RoundedButton _videoModeButton;
        private Panel _contentHost;
        private Panel _systemPage;
        private ImageDisplayForm _imagePage;
        private VideoDisplayForm _videoPage;
        private Label _hardwareNames;
        private Label _cpuMetric;
        private Label _cpuTemperatureMetric;
        private Label _gpuMetric;
        private Label _gpuTemperatureMetric;
        private Label _memoryMetric;
        private Label _diskMetric;
        private Label _networkMetric;
        private Label _powerMetric;
        private Label _previewModeLabel;
        private TrackBar _brightness;
        private Label _brightnessValue;
        private CancellationTokenSource _motionCancellation;
        private CancellationTokenSource _liveCancellation;
        private TelemetrySnapshot _lastSnapshot;
        private StatusKind _statusKind = StatusKind.Working;
        private string _statusText = "正在检测";
        private bool _isBusy;

        public ControlPanelForm()
        {
            _settings = ControlPanelSettings.Load();
            _theme = ThemeManager.Resolve(_settings.AppearanceMode);

            Text = "TURZX 屏幕控制";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1180, 820);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            SizeGripStyle = SizeGripStyle.Hide;
            Font = new Font("Microsoft YaHei UI", 9.0f, FontStyle.Regular, GraphicsUnit.Point);
            Tag = ThemeRole.Window;

            BuildInterface();
            _brightness.Value = _settings.BrightnessPercent;
            SetPreview(TestPattern.CreateLandscapePng(0, 1));
            ApplyTheme(_settings.AppearanceMode);

            SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyTheme(_settings.AppearanceMode);
            await RefreshDeviceAsync();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_motionCancellation != null)
            {
                _motionCancellation.Cancel();
            }
            if (_liveCancellation != null)
            {
                _liveCancellation.Cancel();
            }

            SaveEmbeddedPageSettings();
            if (WindowState == FormWindowState.Normal)
            {
                _settings.WindowWidth = ClientSize.Width;
                _settings.WindowHeight = ClientSize.Height;
            }
            _settings.BrightnessPercent = _brightness.Value;
            _settings.Save();
            SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
            _toolTip.Dispose();

            if (_preview.Image != null)
            {
                _preview.Image.Dispose();
                _preview.Image = null;
            }
            base.OnFormClosing(e);
        }

        private void BuildInterface()
        {
            _root = new TableLayoutPanel();
            _root.Dock = DockStyle.Fill;
            _root.Padding = new Padding(24, 14, 24, 18);
            _root.ColumnCount = 1;
            _root.RowCount = 4;
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 260));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _root.Tag = ThemeRole.Window;
            Controls.Add(_root);

            _root.Controls.Add(BuildHeader(), 0, 0);
            _root.Controls.Add(BuildScreenPreviewSection(), 0, 1);
            _root.Controls.Add(BuildModeNavigation(), 0, 2);
            _root.Controls.Add(BuildContentArea(), 0, 3);
            BuildEmbeddedPages();
            ShowContentPage(ContentPage.System);
        }

        private enum ContentPage
        {
            System,
            Image,
            Video
        }

        private Control BuildScreenPreviewSection()
        {
            TableLayoutPanel section = new TableLayoutPanel();
            section.Dock = DockStyle.Fill;
            section.Margin = new Padding(0);
            section.ColumnCount = 3;
            section.RowCount = 1;
            section.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 780));
            section.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 12));
            section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            section.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            section.Tag = ThemeRole.Window;

            _preview = new SmoothImageBox();
            _preview.Dock = DockStyle.Fill;
            _preview.BackColor = Color.Black;
            _preview.CornerRadius = 18;
            section.Controls.Add(_preview, 0, 0);

            RoundedPanel infoCard = new RoundedPanel();
            infoCard.Dock = DockStyle.Fill;
            infoCard.Margin = new Padding(0);
            infoCard.Padding = new Padding(22, 16, 22, 16);
            infoCard.CornerRadius = 18;
            infoCard.Tag = ThemeRole.Surface;

            Label infoTitle = CreateLabel("屏幕信息", false);
            infoTitle.Location = new Point(22, 16);
            infoTitle.Size = new Size(160, 30);
            infoTitle.Font = new Font("Microsoft YaHei UI", 12.0f, FontStyle.Bold,
                GraphicsUnit.Point);
            infoCard.Controls.Add(infoTitle);

            _deviceStatus = CreateLabel("正在检测", false);
            _deviceStatus.Location = new Point(22, 50);
            _deviceStatus.Size = new Size(270, 30);
            _deviceStatus.Font = new Font("Microsoft YaHei UI", 10.0f, FontStyle.Bold,
                GraphicsUnit.Point);
            infoCard.Controls.Add(_deviceStatus);

            Label specification = CreateLabel("4.6 英寸  ·  960 × 320  ·  USB", true);
            specification.Location = new Point(22, 84);
            specification.Size = new Size(280, 28);
            infoCard.Controls.Add(specification);

            Label modeCaption = CreateLabel("当前显示", true);
            modeCaption.Location = new Point(22, 120);
            modeCaption.Size = new Size(78, 26);
            infoCard.Controls.Add(modeCaption);
            _previewModeLabel = CreateLabel("系统监控", false);
            _previewModeLabel.Location = new Point(104, 120);
            _previewModeLabel.Size = new Size(184, 26);
            _previewModeLabel.Font = new Font("Microsoft YaHei UI", 9.0f, FontStyle.Bold,
                GraphicsUnit.Point);
            infoCard.Controls.Add(_previewModeLabel);

            _refreshButton = CreateButton("检查连接", false);
            _refreshButton.Location = new Point(22, 174);
            _refreshButton.Size = new Size(128, 42);
            _refreshButton.Click += async delegate { await RefreshDeviceAsync(); };
            infoCard.Controls.Add(_refreshButton);

            _settingsButton = CreateButton("⚙  设置", false);
            _settingsButton.Location = new Point(162, 174);
            _settingsButton.Size = new Size(128, 42);
            _settingsButton.Click += delegate { OpenSettings(); };
            infoCard.Controls.Add(_settingsButton);
            section.Controls.Add(infoCard, 2, 0);
            return section;
        }

        private Control BuildModeNavigation()
        {
            RoundedPanel bar = new RoundedPanel();
            bar.Dock = DockStyle.Fill;
            bar.Margin = new Padding(0, 12, 0, 0);
            bar.Padding = new Padding(10, 7, 10, 7);
            bar.CornerRadius = 16;
            bar.Tag = ThemeRole.Surface;

            FlowLayoutPanel tabs = new FlowLayoutPanel();
            tabs.Dock = DockStyle.Left;
            tabs.Width = 480;
            tabs.WrapContents = false;
            tabs.Tag = ThemeRole.Surface;
            bar.Controls.Add(tabs);

            _systemModeButton = CreateButton("系统监控", true);
            _systemModeButton.Size = new Size(148, 40);
            _systemModeButton.Margin = new Padding(0, 0, 8, 0);
            _systemModeButton.Click += delegate { ShowContentPage(ContentPage.System); };
            tabs.Controls.Add(_systemModeButton);

            _imageModeButton = CreateButton("图片", false);
            _imageModeButton.Size = new Size(128, 40);
            _imageModeButton.Margin = new Padding(0, 0, 8, 0);
            _imageModeButton.Click += delegate { ShowContentPage(ContentPage.Image); };
            tabs.Controls.Add(_imageModeButton);

            _videoModeButton = CreateButton("视频", false);
            _videoModeButton.Size = new Size(128, 40);
            _videoModeButton.Margin = new Padding(0);
            _videoModeButton.Click += delegate { ShowContentPage(ContentPage.Video); };
            tabs.Controls.Add(_videoModeButton);

            return bar;
        }

        private Control BuildContentArea()
        {
            RoundedPanel card = new RoundedPanel();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0, 12, 0, 0);
            card.Padding = new Padding(14);
            card.CornerRadius = 22;
            card.Tag = ThemeRole.Surface;

            _contentHost = new Panel();
            _contentHost.Dock = DockStyle.Fill;
            _contentHost.Tag = ThemeRole.Surface;
            card.Controls.Add(_contentHost);
            return card;
        }

        private void BuildEmbeddedPages()
        {
            _systemPage = BuildSystemPage();
            _systemPage.Dock = DockStyle.Fill;
            _contentHost.Controls.Add(_systemPage);

            _imagePage = new ImageDisplayForm(_settings, _settings.AppearanceMode,
                SendImageContentAsync, true);
            _imagePage.Dock = DockStyle.Fill;
            _contentHost.Controls.Add(_imagePage);
            _imagePage.Show();

            _videoPage = new VideoDisplayForm(_settings, _settings.AppearanceMode,
                PlayVideoContentAsync, true);
            _videoPage.Dock = DockStyle.Fill;
            _contentHost.Controls.Add(_videoPage);
            _videoPage.Show();
        }

        private Panel BuildSystemPage()
        {
            Panel page = new Panel();
            page.Tag = ThemeRole.Surface;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 3;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            layout.Tag = ThemeRole.Surface;
            page.Controls.Add(layout);

            Panel heading = new Panel();
            heading.Dock = DockStyle.Fill;
            heading.Tag = ThemeRole.Surface;
            Label title = CreateLabel("实时系统信息", false);
            title.Location = new Point(4, 2);
            title.Size = new Size(220, 28);
            title.Font = new Font("Microsoft YaHei UI", 13.0f, FontStyle.Bold,
                GraphicsUnit.Point);
            heading.Controls.Add(title);
            _hardwareNames = CreateLabel("等待采集硬件信息", true);
            _hardwareNames.Location = new Point(5, 32);
            _hardwareNames.Size = new Size(700, 26);
            _hardwareNames.AutoEllipsis = true;
            heading.Controls.Add(_hardwareNames);
            _liveButton = CreateButton("开始监控", true);
            _liveButton.Size = new Size(138, 42);
            _liveButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _liveButton.Location = new Point(850, 8);
            _liveButton.Click += async delegate { await ToggleLiveAsync(); };
            heading.Controls.Add(_liveButton);
            heading.Resize += delegate
            {
                _liveButton.Left = heading.ClientSize.Width - _liveButton.Width - 4;
                _hardwareNames.Width = Math.Max(240, _liveButton.Left - 20);
            };
            layout.Controls.Add(heading, 0, 0);

            TableLayoutPanel metrics = new TableLayoutPanel();
            metrics.Dock = DockStyle.Fill;
            metrics.ColumnCount = 4;
            metrics.RowCount = 2;
            for (int column = 0; column < 4; column++)
            {
                metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            }
            metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            metrics.Tag = ThemeRole.Surface;
            metrics.Controls.Add(CreateMetricCard("CPU 使用率", out _cpuMetric), 0, 0);
            metrics.Controls.Add(CreateMetricCard("CPU 温度", out _cpuTemperatureMetric), 1, 0);
            metrics.Controls.Add(CreateMetricCard("GPU 使用率", out _gpuMetric), 2, 0);
            metrics.Controls.Add(CreateMetricCard("GPU 温度", out _gpuTemperatureMetric), 3, 0);
            metrics.Controls.Add(CreateMetricCard("内存", out _memoryMetric), 0, 1);
            metrics.Controls.Add(CreateMetricCard("磁盘", out _diskMetric), 1, 1);
            metrics.Controls.Add(CreateMetricCard("网络", out _networkMetric), 2, 1);
            metrics.Controls.Add(CreateMetricCard("GPU 功耗", out _powerMetric), 3, 1);
            layout.Controls.Add(metrics, 0, 1);

            RoundedPanel brightnessPanel = new RoundedPanel();
            brightnessPanel.Dock = DockStyle.Fill;
            brightnessPanel.Margin = new Padding(6, 6, 6, 0);
            brightnessPanel.Padding = new Padding(14, 8, 14, 8);
            brightnessPanel.CornerRadius = 14;
            brightnessPanel.Tag = ThemeRole.SurfaceAlt;
            Label brightnessTitle = CreateLabel("屏幕亮度", false);
            brightnessTitle.Dock = DockStyle.Left;
            brightnessTitle.Width = 100;
            brightnessPanel.Controls.Add(brightnessTitle);
            _brightnessValue = CreateLabel("40%", true);
            _brightnessValue.Dock = DockStyle.Right;
            _brightnessValue.Width = 56;
            _brightnessValue.TextAlign = ContentAlignment.MiddleRight;
            brightnessPanel.Controls.Add(_brightnessValue);
            _brightnessButton = CreateButton("应用", false);
            _brightnessButton.Dock = DockStyle.Right;
            _brightnessButton.Width = 88;
            _brightnessButton.Margin = new Padding(8, 0, 0, 0);
            _brightnessButton.Click += async delegate { await ApplyBrightnessAsync(); };
            brightnessPanel.Controls.Add(_brightnessButton);
            _brightness = new TrackBar();
            _brightness.Dock = DockStyle.Fill;
            _brightness.Minimum = 0;
            _brightness.Maximum = 100;
            _brightness.TickStyle = TickStyle.None;
            _brightness.Tag = ThemeRole.Input;
            _brightness.ValueChanged += delegate
            {
                _brightnessValue.Text = _brightness.Value + "%";
                _settings.BrightnessPercent = _brightness.Value;
            };
            brightnessPanel.Controls.Add(_brightness);
            _brightness.BringToFront();
            layout.Controls.Add(brightnessPanel, 0, 2);
            return page;
        }

        private Control CreateMetricCard(string titleText, out Label valueLabel)
        {
            RoundedPanel card = new RoundedPanel();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(6);
            card.Padding = new Padding(14, 8, 14, 8);
            card.CornerRadius = 15;
            card.Tag = ThemeRole.SurfaceAlt;
            Label title = CreateLabel(titleText, true);
            title.Dock = DockStyle.Top;
            title.Height = 24;
            card.Controls.Add(title);
            valueLabel = CreateLabel("--", false);
            valueLabel.Dock = DockStyle.Fill;
            valueLabel.Font = new Font("Microsoft YaHei UI", 17.0f, FontStyle.Bold,
                GraphicsUnit.Point);
            card.Controls.Add(valueLabel);
            valueLabel.BringToFront();
            return card;
        }

        private void ShowContentPage(ContentPage page)
        {
            if (_systemPage == null || _imagePage == null || _videoPage == null)
            {
                return;
            }
            _systemPage.Visible = page == ContentPage.System;
            _imagePage.Visible = page == ContentPage.Image;
            _videoPage.Visible = page == ContentPage.Video;
            if (page == ContentPage.System)
            {
                _systemPage.BringToFront();
            }
            else if (page == ContentPage.Image)
            {
                _imagePage.BringToFront();
            }
            else
            {
                _videoPage.BringToFront();
            }

            _systemModeButton.Tag = page == ContentPage.System ?
                ThemeRole.PrimaryButton : ThemeRole.SecondaryButton;
            _imageModeButton.Tag = page == ContentPage.Image ?
                ThemeRole.PrimaryButton : ThemeRole.SecondaryButton;
            _videoModeButton.Tag = page == ContentPage.Video ?
                ThemeRole.PrimaryButton : ThemeRole.SecondaryButton;
            if (_theme != null)
            {
                ThemeManager.Apply(_systemModeButton, _theme);
                ThemeManager.Apply(_imageModeButton, _theme);
                ThemeManager.Apply(_videoModeButton, _theme);
            }
        }

        private Control BuildHeader()
        {
            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            header.Tag = ThemeRole.Window;

            Label title = new Label();
            title.AutoSize = true;
            title.Text = "TURZX 屏幕控制";
            title.Font = new Font("Microsoft YaHei UI", 21.0f, FontStyle.Bold, GraphicsUnit.Point);
            title.Location = new Point(0, 10);
            title.Tag = ThemeRole.Text;
            header.Controls.Add(title);

            Label subtitle = new Label();
            subtitle.AutoSize = true;
            subtitle.Text = "USB DISPLAY CONTROL";
            subtitle.Location = new Point(292, 25);
            subtitle.Tag = ThemeRole.Muted;
            header.Controls.Add(subtitle);
            return header;
        }

        private async Task RefreshDeviceAsync()
        {
            await RunOperationAsync("正在检测 TURZX1.0…", delegate
            {
                EnsureVendorApplicationClosed();
                using (WinUsbDevice device = WinUsbDevice.Open(VendorId, ProductId))
                {
                    return string.Format(
                        "设备已连接：输出端点 0x{0:X2}，输入端点 0x{1:X2}，数据包 {2}/{3} 字节。",
                        device.OutEndpoint, device.InEndpoint,
                        device.OutMaxPacketSize, device.InMaxPacketSize);
                }
            });
        }

        private async Task SendStaticAsync()
        {
            byte[] png = TestPattern.CreateLandscapePng(0, 1);
            TurzxProtocol.DescribePanelPng(png);
            SetPreview(png);
            _previewModeLabel.Text = "设备测试";

            await RunOperationAsync("正在发送横版静态测试画面…", delegate
            {
                EnsureVendorApplicationClosed();
                using (WinUsbDevice device = WinUsbDevice.Open(VendorId, ProductId))
                {
                    RequireSuccess(device.Exchange(TurzxProtocol.CreateCommand(10), 10), "同步");
                    RequireSuccess(device.Exchange(TurzxProtocol.CreateImageUpload(png), 102), "发送画面");
                    return string.Format("静态测试画面发送成功，共 {0:N0} 字节。", png.Length);
                }
            });
        }

        private async Task RunMotionAsync(Action<int> progress)
        {
            SetBusy(true);
            _motionCancellation = new CancellationTokenSource();
            CancellationToken cancellation = _motionCancellation.Token;
            SetStatus("正在运行动态测试", StatusKind.Working);
            AppendLog("开始运行 12 帧动态测试。", true);

            try
            {
                await Task.Run(delegate
                {
                    EnsureVendorApplicationClosed();
                    using (WinUsbDevice device = WinUsbDevice.Open(VendorId, ProductId))
                    {
                        RequireSuccess(device.Exchange(TurzxProtocol.CreateCommand(10), 10), "同步");
                        for (int frame = 0; frame < 12; frame++)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            byte[] png = TestPattern.CreateLandscapePng(frame, 12);
                            TurzxProtocol.DescribePanelPng(png);
                            RequireSuccess(device.Exchange(
                                TurzxProtocol.CreateImageUpload(png), 102), "发送测试帧");

                            int displayedFrame = frame + 1;
                            BeginInvoke(new Action(delegate
                            {
                                SetPreview(png);
                                _previewModeLabel.Text = "设备测试";
                                if (progress != null)
                                {
                                    progress(displayedFrame);
                                }
                                AppendLog(string.Format("动态测试：第 {0:00}/12 帧已接收。",
                                    displayedFrame), false);
                            }));
                            Thread.Sleep(750);
                        }
                    }
                }, cancellation);

                SetStatus("已连接 · 动态测试正常", StatusKind.Connected);
                AppendLog("动态测试完成，方向和刷新均正常。", true);
            }
            catch (OperationCanceledException)
            {
                AppendLog("动态测试已取消。", true);
            }
            catch (Exception exception)
            {
                SetStatus("设备通信异常", StatusKind.Error);
                AppendLog("错误：" + exception.Message, true);
                ShowError(exception.Message, "动态测试失败");
            }
            finally
            {
                if (_motionCancellation != null)
                {
                    _motionCancellation.Dispose();
                    _motionCancellation = null;
                }
                SetBusy(false);
            }
        }

        private async Task ApplyBrightnessAsync()
        {
            int percent = _brightness.Value;
            await RunOperationAsync("正在应用屏幕亮度 " + percent + "%…", delegate
            {
                EnsureVendorApplicationClosed();
                using (WinUsbDevice device = WinUsbDevice.Open(VendorId, ProductId))
                {
                    RequireSuccess(device.Exchange(TurzxProtocol.CreateCommand(10), 10), "同步");
                    RequireSuccess(device.Exchange(
                        TurzxProtocol.CreateBrightnessCommand(percent), 14), "设置亮度");
                    return "屏幕亮度已设为 " + percent + "% 。";
                }
            });
        }

        private async Task ToggleLiveAsync()
        {
            if (_liveCancellation != null)
            {
                AppendLog("正在停止实时监控…", true);
                _liveCancellation.Cancel();
                return;
            }

            CancellationTokenSource cancellationSource = new CancellationTokenSource();
            _liveCancellation = cancellationSource;
            CancellationToken cancellation = cancellationSource.Token;
            SetLiveMode(true);
            SetStatus("实时监控运行中", StatusKind.Connected);
            AppendLog("开始发送 CPU、GPU、内存、磁盘和网络实时数据。", true);

            try
            {
                await Task.Run(delegate
                {
                    EnsureVendorApplicationClosed();
                    SystemTelemetry telemetry = new SystemTelemetry();
                    telemetry.Capture();
                    cancellation.WaitHandle.WaitOne(300);

                    using (HardwareSensorProvider sensors = new HardwareSensorProvider())
                    using (WinUsbDevice device = WinUsbDevice.Open(VendorId, ProductId))
                    {
                        RequireSuccess(device.Exchange(TurzxProtocol.CreateCommand(10), 10), "同步");
                        int frameNumber = 0;
                        while (!cancellation.IsCancellationRequested)
                        {
                            TelemetrySnapshot snapshot = telemetry.Capture();
                            sensors.Enrich(snapshot);
                            byte[] png = DashboardRenderer.CreateTransportPng(snapshot, ScreenAccent);
                            TurzxProtocol.DescribePanelPng(png);
                            RequireSuccess(device.Exchange(
                                TurzxProtocol.CreateImageUpload(png), 102), "发送实时画面");
                            frameNumber++;

                            int displayedFrame = frameNumber;
                            TelemetrySnapshot displayedSnapshot = snapshot;
                            byte[] displayedPng = png;
                            if (!IsDisposed && IsHandleCreated)
                            {
                            BeginInvoke(new Action(delegate
                            {
                                _lastSnapshot = displayedSnapshot;
                                UpdateSystemMetrics(displayedSnapshot);
                                SetPreview(displayedPng);
                                    _previewModeLabel.Text = "系统监控";
                                    SetStatus(string.Format("实时监控 · CPU {0:0}% · GPU {1:0}%",
                                        displayedSnapshot.CpuPercent,
                                        displayedSnapshot.GpuLoadPercent ?? 0), StatusKind.Connected);
                                    if (displayedFrame == 1 || displayedFrame % 10 == 0)
                                    {
                                        AppendLog(string.Format(
                                            "实时帧 {0}：CPU {1:0.0}%/{2}，GPU {3:0}%/{4}，功耗 {5:0} W。",
                                            displayedFrame, displayedSnapshot.CpuPercent,
                                            FormatTemperature(displayedSnapshot.CpuTemperatureC),
                                            displayedSnapshot.GpuLoadPercent ?? 0,
                                            FormatTemperature(displayedSnapshot.GpuTemperatureC),
                                            displayedSnapshot.GpuPowerWatts ?? 0), false);
                                    }
                                }));
                            }

                            int refreshMilliseconds = Math.Max(500,
                                Math.Min(5000, _settings.RefreshMilliseconds));
                            if (cancellation.WaitHandle.WaitOne(refreshMilliseconds))
                            {
                                break;
                            }
                        }
                    }
                }, cancellation);

                SetStatus("已连接", StatusKind.Connected);
                AppendLog("实时监控已停止。", true);
            }
            catch (OperationCanceledException)
            {
                SetStatus("已连接", StatusKind.Connected);
                AppendLog("实时监控已停止。", true);
            }
            catch (Exception exception)
            {
                SetStatus("设备通信异常", StatusKind.Error);
                AppendLog("错误：" + exception.Message, true);
                ShowError(exception.Message, "实时监控失败");
            }
            finally
            {
                if (ReferenceEquals(_liveCancellation, cancellationSource))
                {
                    _liveCancellation = null;
                }
                cancellationSource.Dispose();
                SetLiveMode(false);
            }
        }

        private void OpenSettings()
        {
            SaveEmbeddedPageSettings();
            string sensorSummary = "尚未采集";
            if (_lastSnapshot != null)
            {
                sensorSummary = string.Format("来源：{0}；GPU {1}，{2:0} W",
                    _lastSnapshot.SensorSource ?? "未知",
                    FormatTemperature(_lastSnapshot.GpuTemperatureC),
                    _lastSnapshot.GpuPowerWatts ?? 0);
            }

            using (SettingsForm dialog = new SettingsForm(
                _settings,
                delegate(ThemeMode mode) { ApplyTheme(mode); },
                RefreshDeviceAsync,
                SendStaticAsync,
                RunMotionAsync,
                sensorSummary,
                _liveCancellation == null))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _settings.CopyFrom(dialog.ResultSettings);
                    _settings.Save();
                    ApplyTheme(_settings.AppearanceMode);
                    AppendLog("设置已保存。", true);
                }
                else
                {
                    ApplyTheme(_settings.AppearanceMode);
                }
            }
        }

        private void OpenImageDisplay()
        {
            if (_liveCancellation != null)
            {
                return;
            }

            using (ImageDisplayForm dialog = new ImageDisplayForm(
                _settings, _settings.AppearanceMode, SendImageContentAsync))
            {
                dialog.ShowDialog(this);
                _settings.ImageFitMode = dialog.SelectedFitMode;
                _settings.ImageBackgroundWhite = dialog.BackgroundWhite;
                _settings.LastImageDirectory = dialog.LastDirectory;
                _settings.Save();
            }
            ApplyTheme(_settings.AppearanceMode);
        }

        private async Task<bool> SendImageContentAsync(byte[] png, string fileName)
        {
            SetBusy(true);
            SetStatus("正在发送图片", StatusKind.Working);
            AppendLog("正在发送图片：“" + fileName + "”…", true);
            try
            {
                TurzxProtocol.DescribePanelPng(png);
                await Task.Run(delegate
                {
                    EnsureVendorApplicationClosed();
                    using (WinUsbDevice device = WinUsbDevice.Open(VendorId, ProductId))
                    {
                        RequireSuccess(device.Exchange(TurzxProtocol.CreateCommand(10), 10), "同步");
                        RequireSuccess(device.Exchange(
                            TurzxProtocol.CreateImageUpload(png), 102), "发送图片");
                    }
                });

                SetPreview(png);
                _previewModeLabel.Text = "图片";
                SetStatus("已连接 · 图片已显示", StatusKind.Connected);
                AppendLog(string.Format("图片“{0}”发送成功，共 {1:N0} 字节。",
                    fileName, png.Length), true);
                return true;
            }
            catch (Exception exception)
            {
                SetStatus("设备通信异常", StatusKind.Error);
                AppendLog("错误：" + exception.Message, true);
                ShowError(exception.Message, "图片发送失败");
                return false;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void OpenVideoDisplay()
        {
            if (_liveCancellation != null)
            {
                return;
            }

            using (VideoDisplayForm dialog = new VideoDisplayForm(
                _settings, _settings.AppearanceMode, PlayVideoContentAsync))
            {
                dialog.ShowDialog(this);
                _settings.VideoFitMode = dialog.SelectedFitMode;
                _settings.VideoBackgroundWhite = dialog.BackgroundWhite;
                _settings.VideoFramesPerSecond = dialog.FramesPerSecond;
                _settings.VideoOrientationMode = dialog.SelectedOrientation;
                _settings.VideoLoop = dialog.LoopPlayback;
                _settings.LastVideoDirectory = dialog.LastDirectory;
                _settings.Save();
            }
            ApplyTheme(_settings.AppearanceMode);
        }

        private async Task<bool> PlayVideoContentAsync(VideoPlaybackRequest request,
            Action<byte[], int> progress, CancellationToken cancellationToken)
        {
            SetBusy(true);
            SetStatus("正在播放视频", StatusKind.Working);
            AppendLog("开始播放视频：“" + Path.GetFileName(request.FilePath) + "”…", true);
            try
            {
                string ffmpegPath = VideoDecoderLocator.FindFfmpeg();
                if (string.IsNullOrWhiteSpace(ffmpegPath))
                {
                    throw new InvalidOperationException(
                        "未找到 FFmpeg 解码器。请把 ffmpeg.exe 放入软件目录的 ffmpeg 文件夹。");
                }
                VideoMetadata metadata = await Task.Run(delegate
                {
                    return VideoProbe.Read(ffmpegPath, request.FilePath);
                }, cancellationToken);

                int frameCount = 0;
                await Task.Run(delegate
                {
                    EnsureVendorApplicationClosed();
                    using (VideoFrameDecoder decoder = new VideoFrameDecoder(
                        ffmpegPath, request, metadata))
                    using (WinUsbDevice device = WinUsbDevice.Open(VendorId, ProductId))
                    {
                        RequireSuccess(device.Exchange(
                            TurzxProtocol.CreateCommand(10), 10), "同步");
                        int interval = 1000 / Math.Max(1, request.FramesPerSecond);
                        while (true)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            Stopwatch frameTimer = Stopwatch.StartNew();
                            byte[] png = decoder.ReadNextTransportPng(cancellationToken);
                            if (png == null)
                            {
                                break;
                            }
                            if (frameCount == 0)
                            {
                                TurzxProtocol.DescribePanelPng(png);
                            }
                            RequireSuccess(device.Exchange(
                                TurzxProtocol.CreateImageUpload(png), 102), "发送视频帧");
                            frameCount++;

                            int displayedFrame = frameCount;
                            byte[] displayedPng = png;
                            if (!IsDisposed && IsHandleCreated)
                            {
                                BeginInvoke(new Action(delegate
                                {
                                    if (displayedFrame == 1 || displayedFrame % 2 == 0)
                                    {
                                        SetPreview(displayedPng);
                                    }
                                    _previewModeLabel.Text = "视频";
                                    SetStatus(string.Format("视频播放中 · 第 {0:N0} 帧", displayedFrame),
                                        StatusKind.Connected);
                                }));
                            }
                            if (progress != null)
                            {
                                progress(png, displayedFrame);
                            }

                            int remaining = interval - (int)frameTimer.ElapsedMilliseconds;
                            if (remaining > 0 && cancellationToken.WaitHandle.WaitOne(remaining))
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                            }
                        }
                    }
                }, cancellationToken);

                SetStatus("已连接 · 视频播放结束", StatusKind.Connected);
                AppendLog(string.Format("视频播放结束，共显示 {0:N0} 帧。", frameCount), true);
                return true;
            }
            catch (OperationCanceledException)
            {
                SetStatus("已连接 · 视频已停止", StatusKind.Connected);
                AppendLog("视频播放已停止，屏幕保留最后一帧。", true);
                throw;
            }
            catch (Exception exception)
            {
                SetStatus("设备通信异常", StatusKind.Error);
                AppendLog("错误：" + exception.Message, true);
                ShowError(exception.Message, "视频播放失败");
                return false;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (_settings.AppearanceMode != ThemeMode.System || IsDisposed || !IsHandleCreated)
            {
                return;
            }
            BeginInvoke(new Action(delegate { ApplyTheme(ThemeMode.System); }));
        }

        private void ApplyTheme(ThemeMode mode)
        {
            _theme = ThemeManager.Resolve(mode);
            ThemeManager.Apply(this, _theme);
            if (_imagePage != null)
            {
                _imagePage.SetAppearance(mode);
            }
            if (_videoPage != null)
            {
                _videoPage.SetAppearance(mode);
            }
            _preview.BackColor = Color.Black;
            RefreshStatusColor();
        }

        private void SetLiveMode(bool running)
        {
            SetBusy(running);
            _liveButton.Enabled = true;
            _liveButton.Text = running ? "停止实时监控" : "开始实时监控";
            _systemModeButton.Enabled = true;
            _imageModeButton.Enabled = !running;
            _videoModeButton.Enabled = !running;
            _settingsButton.Enabled = true;
            UseWaitCursor = false;
            ApplyTheme(_settings.AppearanceMode);
        }

        private async Task RunOperationAsync(string startMessage, Func<string> operation)
        {
            SetBusy(true);
            SetStatus("正在处理", StatusKind.Working);
            AppendLog(startMessage, true);
            try
            {
                string result = await Task.Run(operation);
                SetStatus("已连接", StatusKind.Connected);
                AppendLog(result, true);
            }
            catch (Exception exception)
            {
                SetStatus("设备通信异常", StatusKind.Error);
                AppendLog("错误：" + exception.Message, true);
                ShowError(exception.Message, "设备操作失败");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private static void EnsureVendorApplicationClosed()
        {
            Process[] processes = Process.GetProcessesByName("TURZX");
            if (processes.Length > 0)
            {
                throw new InvalidOperationException(
                    "原厂 TURZX 软件正在运行。请先退出原厂软件，再控制屏幕。");
            }
        }

        private static void RequireSuccess(byte[] response, string operation)
        {
            if (response == null || response.Length < 2 || response[1] != 0xC8)
            {
                throw new InvalidOperationException(operation + "返回了异常的设备响应。");
            }
        }

        private void SetPreview(byte[] transportPng)
        {
            Bitmap logicalPreview;
            using (MemoryStream stream = new MemoryStream(transportPng))
            using (Image transportImage = Image.FromStream(stream))
            {
                logicalPreview = new Bitmap(transportImage);
            }

            logicalPreview.RotateFlip(RotateFlipType.Rotate270FlipNone);
            Image old = _preview.Image;
            _preview.Image = logicalPreview;
            if (old != null)
            {
                old.Dispose();
            }
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            _refreshButton.Enabled = !busy;
            _settingsButton.Enabled = !busy;
            _systemModeButton.Enabled = !busy;
            _imageModeButton.Enabled = !busy;
            _videoModeButton.Enabled = !busy;
            _brightnessButton.Enabled = !busy;
            _brightness.Enabled = !busy;
            _liveButton.Enabled = !busy;
            UseWaitCursor = busy;
            if (_theme != null)
            {
                ThemeManager.Apply(this, _theme);
                RefreshStatusColor();
            }
        }

        private void SetStatus(string text, StatusKind kind)
        {
            _statusText = text;
            _statusKind = kind;
            _deviceStatus.Text = "●  " + text;
            RefreshStatusColor();
        }

        private void RefreshStatusColor()
        {
            if (_deviceStatus == null || _theme == null)
            {
                return;
            }
            _deviceStatus.Text = "●  " + _statusText;
            if (_statusKind == StatusKind.Connected)
            {
                _deviceStatus.ForeColor = _theme.Success;
            }
            else if (_statusKind == StatusKind.Working)
            {
                _deviceStatus.ForeColor = _theme.Warning;
            }
            else if (_statusKind == StatusKind.Error)
            {
                _deviceStatus.ForeColor = _theme.Error;
            }
            else
            {
                _deviceStatus.ForeColor = _theme.Text;
            }
        }

        private void AppendLog(string message, bool important)
        {
            Debug.WriteLine(string.Format("[{0:HH:mm:ss}] {1}", DateTime.Now, message));
        }

        private void ShowError(string detail, string title)
        {
            MessageBox.Show(this, detail, title,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private Panel CreateCardPanel()
        {
            Panel panel = new RoundedPanel();
            panel.Tag = ThemeRole.Surface;
            return panel;
        }

        private static Label CreateLabel(string text, bool muted)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Tag = muted ? ThemeRole.Muted : ThemeRole.Text;
            return label;
        }

        private static RoundedButton CreateButton(string text, bool primary)
        {
            RoundedButton button = new RoundedButton();
            button.Text = text;
            button.Height = 40;
            button.Cursor = Cursors.Hand;
            button.Tag = primary ? ThemeRole.PrimaryButton : ThemeRole.SecondaryButton;
            return button;
        }

        private static string FormatTemperature(double? value)
        {
            return value.HasValue ? value.Value.ToString("0") + "°C" : "--°C";
        }

        private void UpdateSystemMetrics(TelemetrySnapshot snapshot)
        {
            if (snapshot == null || _cpuMetric == null)
            {
                return;
            }
            _cpuMetric.Text = snapshot.CpuPercent.ToString("0.0") + "%";
            _cpuTemperatureMetric.Text = FormatTemperature(snapshot.CpuTemperatureC);
            _gpuMetric.Text = (snapshot.GpuLoadPercent ?? 0).ToString("0.0") + "%";
            _gpuTemperatureMetric.Text = FormatTemperature(snapshot.GpuTemperatureC);
            _memoryMetric.Text = string.Format("{0:0.0} / {1:0.0} GB",
                ToGigabytes(snapshot.MemoryUsedBytes), ToGigabytes(snapshot.MemoryTotalBytes));
            _diskMetric.Text = snapshot.DiskPercent.ToString("0.0") + "%";
            _networkMetric.Text = string.Format("↓ {0:0.0}  ↑ {1:0.0} Mb/s",
                snapshot.NetworkDownloadMbps, snapshot.NetworkUploadMbps);
            _powerMetric.Text = snapshot.GpuPowerWatts.HasValue ?
                snapshot.GpuPowerWatts.Value.ToString("0.0") + " W" : "-- W";
            _hardwareNames.Text = string.Format("CPU：{0}    GPU：{1}",
                snapshot.CpuName ?? "未知", snapshot.GpuName ?? "未知");
        }

        private static double ToGigabytes(ulong bytes)
        {
            return bytes / 1073741824.0;
        }

        private void SaveEmbeddedPageSettings()
        {
            if (_imagePage != null && !_imagePage.IsDisposed)
            {
                _settings.ImageFitMode = _imagePage.SelectedFitMode;
                _settings.ImageBackgroundWhite = _imagePage.BackgroundWhite;
                _settings.LastImageDirectory = _imagePage.LastDirectory;
            }
            if (_videoPage != null && !_videoPage.IsDisposed)
            {
                _settings.VideoFitMode = _videoPage.SelectedFitMode;
                _settings.VideoBackgroundWhite = _videoPage.BackgroundWhite;
                _settings.VideoFramesPerSecond = _videoPage.FramesPerSecond;
                _settings.VideoOrientationMode = _videoPage.SelectedOrientation;
                _settings.VideoLoop = _videoPage.LoopPlayback;
                _settings.LastVideoDirectory = _videoPage.LastDirectory;
            }
        }
    }
}
