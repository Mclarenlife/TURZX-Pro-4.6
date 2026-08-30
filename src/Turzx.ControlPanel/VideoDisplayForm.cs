using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Turzx.ControlPanel
{
    internal sealed class VideoDisplayForm : Form
    {
        private ThemeMode _appearanceMode;
        private readonly bool _embedded;
        private readonly Func<VideoPlaybackRequest, Action<byte[], int>, CancellationToken,
            Task<bool>> _playVideo;
        private CropPreviewBox _preview;
        private ComboBox _videoList;
        private ComboBox _orientation;
        private ComboBox _fit;
        private ComboBox _fps;
        private RadioButton _blackBackground;
        private RadioButton _whiteBackground;
        private CheckBox _loop;
        private Label _infoLabel;
        private Label _statusLabel;
        private RoundedButton _chooseButton;
        private RoundedButton _playButton;
        private RoundedButton _stopButton;
        private string _videoPath;
        private string _lastDirectory;
        private string _ffmpegPath;
        private VideoMetadata _metadata;
        private CancellationTokenSource _playbackCancellation;
        private bool _ready;
        private RectangleF _customCrop;

        public VideoDisplayForm(ControlPanelSettings settings, ThemeMode appearanceMode,
            Func<VideoPlaybackRequest, Action<byte[], int>, CancellationToken, Task<bool>> playVideo,
            bool embedded = false)
        {
            _appearanceMode = appearanceMode;
            _embedded = embedded;
            _playVideo = playVideo;
            _lastDirectory = settings.LastVideoDirectory;

            Text = "视频播放";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1040, 660);
            MinimumSize = embedded ? Size.Empty : new Size(960, 610);
            ShowInTaskbar = false;
            if (embedded)
            {
                FormBorderStyle = FormBorderStyle.None;
                TopLevel = false;
            }
            Font = new Font("Microsoft YaHei UI", 9.0f, FontStyle.Regular, GraphicsUnit.Point);
            Tag = ThemeRole.Window;

            BuildInterface();
            SelectComboValue(_orientation, settings.VideoOrientationMode);
            SelectComboValue(_fit, settings.VideoFitMode);
            SelectFps(settings.VideoFramesPerSecond);
            if (_fps.SelectedIndex < 0)
            {
                _fps.SelectedIndex = 1;
            }
            _whiteBackground.Checked = settings.VideoBackgroundWhite;
            _blackBackground.Checked = !settings.VideoBackgroundWhite;
            _loop.Checked = settings.VideoLoop;
            _ffmpegPath = VideoDecoderLocator.FindFfmpeg();
            _ready = true;
            ApplyTheme();
            LoadInitialVideoList();
        }

        public ImageFitMode SelectedFitMode
        {
            get
            {
                OptionItem item = _fit.SelectedItem as OptionItem;
                return item == null ? ImageFitMode.Cover : (ImageFitMode)item.Value;
            }
        }

        public bool BackgroundWhite
        {
            get { return _whiteBackground.Checked; }
        }

        public int FramesPerSecond
        {
            get
            {
                string text = Convert.ToString(_fps.SelectedItem, CultureInfo.InvariantCulture);
                int separator = string.IsNullOrWhiteSpace(text) ? -1 : text.IndexOf(' ');
                int value;
                return separator > 0 && int.TryParse(text.Substring(0, separator), out value) ?
                    value : 5;
            }
        }

        public VideoOrientationMode SelectedOrientation
        {
            get
            {
                OptionItem item = _orientation.SelectedItem as OptionItem;
                return item == null ? VideoOrientationMode.Auto : (VideoOrientationMode)item.Value;
            }
        }

        public bool LoopPlayback
        {
            get { return _loop.Checked; }
        }

        public string LastDirectory
        {
            get { return _lastDirectory ?? string.Empty; }
        }

        public void SetAppearance(ThemeMode appearanceMode)
        {
            _appearanceMode = appearanceMode;
            ApplyTheme();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_playbackCancellation != null)
            {
                _playbackCancellation.Cancel();
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
        }

        private void BuildInterface()
        {
            _statusLabel = CreateLabel(string.Empty, true);
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = _embedded ? new Padding(0) : new Padding(20, 12, 20, 18);
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, _embedded ? 0 : 72));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, _embedded ? 0 : 50));
            root.Tag = ThemeRole.Window;
            Controls.Add(root);

            if (!_embedded)
            {
                root.Controls.Add(BuildHeader(), 0, 0);
            }
            root.Controls.Add(BuildBody(), 0, 1);
            if (!_embedded)
            {
                root.Controls.Add(BuildFooter(), 0, 2);
            }
        }

        private Control BuildHeader()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.Tag = ThemeRole.Window;

            Label title = CreateLabel("视频播放", false);
            title.AutoSize = true;
            title.Font = new Font("Microsoft YaHei UI", 20.0f, FontStyle.Bold, GraphicsUnit.Point);
            title.Location = new Point(0, 5);
            panel.Controls.Add(title);

            Label subtitle = CreateLabel("导入 MP4 视频并循环发送到 960 × 320 屏幕", true);
            subtitle.AutoSize = true;
            subtitle.Location = new Point(3, 47);
            panel.Controls.Add(subtitle);
            return panel;
        }

        private Control BuildBody()
        {
            TableLayoutPanel body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.ColumnCount = 2;
            body.RowCount = 1;
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.Tag = ThemeRole.Window;

            Panel previewCard = CreateCard();
            previewCard.Dock = DockStyle.Fill;
            previewCard.Margin = new Padding(0, 0, 6, 0);
            previewCard.Padding = new Padding(14);

            Panel heading = new Panel();
            heading.Dock = DockStyle.Top;
            heading.Height = 48;
            heading.Tag = ThemeRole.Surface;
            Label previewTitle = CreateLabel("横版预览", false);
            previewTitle.Font = new Font("Microsoft YaHei UI", 10.0f, FontStyle.Bold, GraphicsUnit.Point);
            previewTitle.Dock = DockStyle.Left;
            previewTitle.Width = 120;
            heading.Controls.Add(previewTitle);

            _playButton = CreateButton("播放到屏幕", true, 132);
            _playButton.Height = 38;
            _playButton.Enabled = false;
            _playButton.Click += async delegate { await PlayAsync(); };
            heading.Controls.Add(_playButton);

            _stopButton = CreateButton("停止", false, 76);
            _stopButton.Height = 38;
            _stopButton.Enabled = false;
            _stopButton.Click += delegate
            {
                if (_playbackCancellation != null)
                {
                    _statusLabel.Text = "正在停止视频…";
                    _playbackCancellation.Cancel();
                }
            };
            heading.Controls.Add(_stopButton);
            heading.Resize += delegate
            {
                _playButton.Location = new Point(
                    heading.ClientSize.Width - _playButton.Width, 2);
                _stopButton.Location = new Point(
                    _playButton.Left - _stopButton.Width - 8, 2);
            };

            _preview = new CropPreviewBox();
            _preview.BackColor = Color.Black;
            _preview.CropChanged += delegate
            {
                if (SelectedFitMode == ImageFitMode.Custom)
                {
                    _customCrop = _preview.NormalizedCrop;
                    _statusLabel.Text = "拖动缩放位置已更新";
                }
            };
            AspectRatioHost previewHost = new AspectRatioHost();
            previewHost.Dock = DockStyle.Fill;
            previewHost.TargetAspectRatio = 3.0f;
            previewHost.Tag = ThemeRole.Surface;
            previewHost.Controls.Add(_preview);
            previewCard.Controls.Add(previewHost);
            previewCard.Controls.Add(heading);
            heading.BringToFront();
            body.Controls.Add(previewCard, 0, 0);

            Panel options = BuildOptionsCard();
            options.Margin = new Padding(6, 0, 0, 0);
            body.Controls.Add(options, 1, 0);
            return body;
        }

        private Panel BuildOptionsCard()
        {
            Panel card = CreateCard();
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(14);

            FlowLayoutPanel stack = new FlowLayoutPanel();
            stack.Dock = DockStyle.Fill;
            stack.FlowDirection = FlowDirection.TopDown;
            stack.WrapContents = false;
            stack.AutoScroll = true;
            stack.Tag = ThemeRole.Surface;
            card.Controls.Add(stack);

            stack.Controls.Add(CreateSection("视频文件"));
            _videoList = CreateComboBox(265);
            _videoList.SelectedIndexChanged += delegate
            {
                if (_ready && _videoList.SelectedItem is VideoFileItem)
                {
                    LoadVideo(((VideoFileItem)_videoList.SelectedItem).Path);
                }
            };
            stack.Controls.Add(_videoList);

            _chooseButton = CreateButton("选择其他视频", false, 265);
            _chooseButton.Margin = new Padding(0, 6, 0, 0);
            _chooseButton.Click += delegate { ChooseVideo(); };
            stack.Controls.Add(_chooseButton);

            stack.Controls.Add(CreateSection("素材方向"));
            _orientation = CreateEnumComboBox(265, new[]
            {
                new OptionItem("自动识别（推荐）", VideoOrientationMode.Auto),
                new OptionItem("屏幕素材（竖向编码）", VideoOrientationMode.ScreenEncoded),
                new OptionItem("普通横版视频", VideoOrientationMode.Normal)
            });
            _orientation.SelectedIndexChanged += delegate
            {
                if (_ready)
                {
                    _customCrop = RectangleF.Empty;
                }
                RebuildPreview();
            };
            stack.Controls.Add(_orientation);

            stack.Controls.Add(CreateSection("画面适配"));
            _fit = CreateEnumComboBox(265, new[]
            {
                new OptionItem("裁剪铺满", ImageFitMode.Cover),
                new OptionItem("完整显示", ImageFitMode.Contain),
                new OptionItem("拉伸", ImageFitMode.Stretch),
                new OptionItem("拖动缩放适配", ImageFitMode.Custom)
            });
            _fit.SelectedIndexChanged += delegate { RebuildPreview(); };
            stack.Controls.Add(_fit);

            stack.Controls.Add(CreateSection("播放帧率"));
            _fps = CreateComboBox(265);
            _fps.Items.AddRange(new object[]
            {
                "2 FPS", "5 FPS", "10 FPS", "15 FPS（推荐上限）",
                "20 FPS（实验）", "24 FPS（实验）", "30 FPS（实验）"
            });
            _fps.SelectedIndexChanged += delegate { RebuildPreview(); };
            stack.Controls.Add(_fps);

            stack.Controls.Add(CreateSection("留边颜色"));
            FlowLayoutPanel backgrounds = new FlowLayoutPanel();
            backgrounds.Width = 265;
            backgrounds.Height = 42;
            backgrounds.WrapContents = false;
            backgrounds.Tag = ThemeRole.Surface;
            _blackBackground = CreateOption("黑色", 126);
            _whiteBackground = CreateOption("白色", 126);
            _blackBackground.CheckedChanged += delegate { RebuildPreview(); };
            _whiteBackground.CheckedChanged += delegate { RebuildPreview(); };
            backgrounds.Controls.Add(_blackBackground);
            backgrounds.Controls.Add(_whiteBackground);
            stack.Controls.Add(backgrounds);

            _loop = new CheckBox();
            _loop.Text = "循环播放";
            _loop.Width = 265;
            _loop.Height = 32;
            _loop.Margin = new Padding(0, 8, 0, 0);
            _loop.Tag = ThemeRole.Text;
            stack.Controls.Add(_loop);

            _infoLabel = CreateLabel("等待选择视频", true);
            _infoLabel.Width = 265;
            _infoLabel.Height = 58;
            _infoLabel.Margin = new Padding(0, 8, 0, 0);
            stack.Controls.Add(_infoLabel);
            return card;
        }

        private Panel BuildFooter()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.Padding = new Padding(0, 13, 0, 0);
            panel.Tag = ThemeRole.Window;

            _statusLabel.Text = "请选择视频";
            _statusLabel.Dock = DockStyle.Fill;
            panel.Controls.Add(_statusLabel);

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Right;
            actions.Width = 340;
            actions.WrapContents = false;
            actions.FlowDirection = FlowDirection.LeftToRight;
            actions.Tag = ThemeRole.Window;
            panel.Controls.Add(actions);

            if (!_embedded)
            {
                RoundedButton close = CreateButton("关闭", false, 82);
                close.Click += delegate { Close(); };
                actions.Controls.Add(close);
            }

            actions.BringToFront();
            return panel;
        }

        private void LoadInitialVideoList()
        {
            string defaultDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "video");
            string directory = !string.IsNullOrWhiteSpace(_lastDirectory) && Directory.Exists(_lastDirectory) ?
                _lastDirectory : defaultDirectory;
            if (!Directory.Exists(directory))
            {
                return;
            }

            _lastDirectory = directory;
            string[] files = Directory.GetFiles(directory, "*.mp4");
            Array.Sort(files, StringComparer.CurrentCultureIgnoreCase);
            foreach (string file in files)
            {
                _videoList.Items.Add(new VideoFileItem(file));
            }
            if (_videoList.Items.Count > 0)
            {
                _videoList.SelectedIndex = 0;
            }
        }

        private void ChooseVideo()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择要播放的视频";
                dialog.Filter = "MP4 视频|*.mp4|所有文件|*.*";
                dialog.CheckFileExists = true;
                if (!string.IsNullOrWhiteSpace(_lastDirectory) && Directory.Exists(_lastDirectory))
                {
                    dialog.InitialDirectory = _lastDirectory;
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    VideoFileItem item = FindVideoItem(dialog.FileName);
                    if (item == null)
                    {
                        item = new VideoFileItem(dialog.FileName);
                        _videoList.Items.Add(item);
                    }
                    bool alreadySelected = ReferenceEquals(_videoList.SelectedItem, item);
                    _videoList.SelectedItem = item;
                    if (alreadySelected)
                    {
                        LoadVideo(item.Path);
                    }
                }
            }
        }

        private VideoFileItem FindVideoItem(string path)
        {
            foreach (object value in _videoList.Items)
            {
                VideoFileItem item = value as VideoFileItem;
                if (item != null && string.Equals(item.Path, path,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }
            return null;
        }

        private void LoadVideo(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_ffmpegPath))
                {
                    throw new InvalidOperationException(
                        "未找到 FFmpeg 解码器。请把 ffmpeg.exe 放入软件目录的 ffmpeg 文件夹。");
                }
                _videoPath = path;
                _lastDirectory = Path.GetDirectoryName(path) ?? string.Empty;
                _customCrop = RectangleF.Empty;
                _metadata = VideoProbe.Read(_ffmpegPath, path);
                RebuildPreview();
                _statusLabel.Text = "视频已载入，可以播放到屏幕";
            }
            catch (Exception exception)
            {
                _videoPath = null;
                _metadata = null;
                _playButton.Enabled = false;
                MessageBox.Show(this, "无法打开视频：" + exception.Message,
                    "视频读取失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RebuildPreview()
        {
            if (!_ready || string.IsNullOrWhiteSpace(_videoPath) || _metadata == null ||
                _playbackCancellation != null)
            {
                return;
            }
            try
            {
                _statusLabel.Text = "正在生成视频预览…";
                bool screenEncoded = SelectedOrientation == VideoOrientationMode.ScreenEncoded ||
                    (SelectedOrientation == VideoOrientationMode.Auto && _metadata.LooksScreenEncoded);
                if (SelectedFitMode == ImageFitMode.Custom)
                {
                    using (Bitmap sourcePreview = VideoFrameDecoder.ReadSourcePreview(
                        _ffmpegPath, _videoPath, _metadata, screenEncoded))
                    {
                        _preview.SetImage(sourcePreview, _customCrop.Width <= 0);
                    }
                    if (_customCrop.Width > 0)
                    {
                        _preview.SetCrop(_customCrop);
                    }
                    _preview.CropEnabled = true;
                    _customCrop = _preview.NormalizedCrop;
                }
                else
                {
                    byte[] transportPng = VideoFrameDecoder.ReadPreview(
                        _ffmpegPath, CreateRequest(), _metadata);
                    SetPreview(transportPng);
                }
                _infoLabel.Text = string.Format(CultureInfo.InvariantCulture,
                    "{0} × {1}  ·  {2:0.##} 秒  ·  {3:0.##} FPS\r\n识别方向：{4}{5}",
                    _metadata.Width, _metadata.Height, _metadata.DurationSeconds,
                    _metadata.SourceFramesPerSecond,
                    screenEncoded ? "屏幕素材" : "普通视频",
                    SelectedFitMode == ImageFitMode.Custom ? " · 拖动缩放适配" : string.Empty);
                _playButton.Enabled = true;
                _statusLabel.Text = "视频已载入，可以播放到屏幕";
            }
            catch (Exception exception)
            {
                _playButton.Enabled = false;
                _statusLabel.Text = "预览生成失败：" + exception.Message;
            }
        }

        private async Task PlayAsync()
        {
            if (_metadata == null || _playVideo == null || _playbackCancellation != null)
            {
                return;
            }

            CancellationTokenSource source = new CancellationTokenSource();
            _playbackCancellation = source;
            SetPlaying(true);
            _statusLabel.Text = "正在向 TURZX 屏幕播放视频…";
            try
            {
                bool success = await _playVideo(CreateRequest(), UpdatePlaybackPreview, source.Token);
                if (!IsDisposed)
                {
                    _statusLabel.Text = success ? "视频播放已结束" : "播放失败，请查看主页面状态";
                }
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed)
                {
                    _statusLabel.Text = "视频已停止，屏幕保留最后一帧";
                }
            }
            finally
            {
                if (ReferenceEquals(_playbackCancellation, source))
                {
                    _playbackCancellation = null;
                }
                source.Dispose();
                if (!IsDisposed)
                {
                    SetPlaying(false);
                }
            }
        }

        private void UpdatePlaybackPreview(byte[] transportPng, int frameNumber)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            BeginInvoke(new Action(delegate
            {
                if (IsDisposed)
                {
                    return;
                }
                if (frameNumber == 1 || frameNumber % 2 == 0)
                {
                    SetPreview(transportPng);
                }
                _statusLabel.Text = string.Format("正在播放 · 第 {0:N0} 帧 · {1} FPS",
                    frameNumber, FramesPerSecond);
            }));
        }

        private VideoPlaybackRequest CreateRequest()
        {
            return new VideoPlaybackRequest
            {
                FilePath = _videoPath,
                FitMode = SelectedFitMode,
                BackgroundWhite = BackgroundWhite,
                FramesPerSecond = FramesPerSecond,
                OrientationMode = SelectedOrientation,
                Loop = LoopPlayback,
                CustomCrop = _customCrop
            };
        }

        private void SetPreview(byte[] transportPng)
        {
            using (Bitmap preview = ImageContentRenderer.CreateLogicalPreview(transportPng))
            {
                _preview.SetImage(preview, false);
            }
            _preview.CropEnabled = false;
        }

        private void SetPlaying(bool playing)
        {
            _videoList.Enabled = !playing;
            _chooseButton.Enabled = !playing;
            _orientation.Enabled = !playing;
            _fit.Enabled = !playing;
            _fps.Enabled = !playing;
            _blackBackground.Enabled = !playing;
            _whiteBackground.Enabled = !playing;
            _loop.Enabled = !playing;
            _playButton.Enabled = !playing && _metadata != null;
            _stopButton.Enabled = playing;
            _playButton.Text = playing ? "正在播放…" : "播放到屏幕";
            ApplyTheme();
        }

        private void ApplyTheme()
        {
            ThemeManager.Apply(this, ThemeManager.Resolve(_appearanceMode));
            _preview.BackColor = Color.Black;
        }

        private static void SelectComboValue(ComboBox comboBox, object value)
        {
            for (int index = 0; index < comboBox.Items.Count; index++)
            {
                OptionItem item = comboBox.Items[index] as OptionItem;
                if (item != null && Equals(item.Value, value))
                {
                    comboBox.SelectedIndex = index;
                    return;
                }
            }
            comboBox.SelectedIndex = 0;
        }

        private void SelectFps(int framesPerSecond)
        {
            string prefix = framesPerSecond.ToString(CultureInfo.InvariantCulture) + " ";
            for (int index = 0; index < _fps.Items.Count; index++)
            {
                string text = Convert.ToString(_fps.Items[index], CultureInfo.InvariantCulture);
                if (text != null && text.StartsWith(prefix, StringComparison.Ordinal))
                {
                    _fps.SelectedIndex = index;
                    return;
                }
            }
            _fps.SelectedIndex = 1;
        }

        private static ComboBox CreateEnumComboBox(int width, OptionItem[] options)
        {
            ComboBox comboBox = CreateComboBox(width);
            comboBox.DisplayMember = "Text";
            comboBox.ValueMember = "Value";
            comboBox.Items.AddRange(options);
            comboBox.SelectedIndex = 0;
            return comboBox;
        }

        private static ComboBox CreateComboBox(int width)
        {
            ComboBox comboBox = new ComboBox();
            comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox.Width = width;
            comboBox.Height = 34;
            comboBox.Tag = ThemeRole.Input;
            return comboBox;
        }

        private static RadioButton CreateOption(string text, int width)
        {
            RadioButton option = new RadioButton();
            option.Appearance = Appearance.Button;
            option.Text = text;
            option.TextAlign = ContentAlignment.MiddleCenter;
            option.Width = width;
            option.Height = 38;
            option.Margin = new Padding(0, 0, 6, 0);
            option.Tag = ThemeRole.ThemeOption;
            return option;
        }

        private static Panel CreateCard()
        {
            Panel panel = new RoundedPanel();
            panel.Tag = ThemeRole.Surface;
            return panel;
        }

        private static Label CreateSection(string text)
        {
            Label label = CreateLabel(text, false);
            label.Width = 265;
            label.Height = 28;
            label.Margin = new Padding(0, 7, 0, 3);
            label.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
            return label;
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

        private static RoundedButton CreateButton(string text, bool primary, int width)
        {
            RoundedButton button = new RoundedButton();
            button.Text = text;
            button.Width = width;
            button.Height = 40;
            button.Cursor = Cursors.Hand;
            button.Tag = primary ? ThemeRole.PrimaryButton : ThemeRole.SecondaryButton;
            return button;
        }

        private sealed class OptionItem
        {
            public OptionItem(string text, object value)
            {
                Text = text;
                Value = value;
            }

            public string Text { get; private set; }
            public object Value { get; private set; }

            public override string ToString()
            {
                return Text;
            }
        }

        private sealed class VideoFileItem
        {
            public VideoFileItem(string path)
            {
                Path = path;
            }

            public string Path { get; private set; }

            public override string ToString()
            {
                string name = System.IO.Path.GetFileName(Path);
                while (name.EndsWith(".mp4.mp4", StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(0, name.Length - 4);
                }
                return name;
            }
        }
    }
}
