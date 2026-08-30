using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Turzx.ProtocolProbe;

namespace Turzx.ControlPanel
{
    internal sealed class ImageDisplayForm : Form
    {
        private ThemeMode _appearanceMode;
        private readonly bool _embedded;
        private readonly Func<byte[], string, Task<bool>> _sendImage;
        private readonly ToolTip _toolTip = new ToolTip();
        private CropPreviewBox _preview;
        private Label _infoLabel;
        private Label _statusLabel;
        private RoundedButton _sendButton;
        private Bitmap _source;
        private byte[] _transportPng;
        private string _imagePath;
        private ImageFitMode _fitMode;
        private bool _backgroundWhite;
        private bool _transportDirty;
        private string _lastDirectory;
        private RectangleF _customCrop;

        public ImageDisplayForm(ControlPanelSettings settings, ThemeMode appearanceMode,
            Func<byte[], string, Task<bool>> sendImage, bool embedded = false)
        {
            _appearanceMode = appearanceMode;
            _embedded = embedded;
            _sendImage = sendImage;
            _fitMode = settings.ImageFitMode;
            _backgroundWhite = settings.ImageBackgroundWhite;
            _lastDirectory = settings.LastImageDirectory;

            Text = "图片显示";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(980, 620);
            MinimumSize = embedded ? Size.Empty : new Size(900, 570);
            ShowInTaskbar = false;
            if (embedded)
            {
                FormBorderStyle = FormBorderStyle.None;
                TopLevel = false;
            }
            Font = new Font("Microsoft YaHei UI", 9.0f, FontStyle.Regular, GraphicsUnit.Point);
            Tag = ThemeRole.Window;
            AllowDrop = true;
            DragEnter += OnImageDragEnter;
            DragDrop += OnImageDragDrop;

            BuildInterface();
            ApplyTheme();
        }

        public ImageFitMode SelectedFitMode
        {
            get { return _fitMode; }
        }

        public bool BackgroundWhite
        {
            get { return _backgroundWhite; }
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

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyTheme();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_source != null)
            {
                _source.Dispose();
                _source = null;
            }
            _toolTip.Dispose();
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

            Label title = CreateLabel("图片显示", false);
            title.AutoSize = true;
            title.Font = new Font("Microsoft YaHei UI", 20.0f, FontStyle.Bold, GraphicsUnit.Point);
            title.Location = new Point(0, 5);
            panel.Controls.Add(title);

            Label subtitle = CreateLabel("选择或拖入图片，调整适配方式后发送到 960 × 320 屏幕", true);
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
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 67));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.Tag = ThemeRole.Window;

            Panel previewCard = BuildPreviewCard();
            previewCard.Margin = new Padding(0, 0, 6, 0);
            body.Controls.Add(previewCard, 0, 0);

            Panel options = BuildOptionsCard();
            options.Margin = new Padding(6, 0, 0, 0);
            body.Controls.Add(options, 1, 0);
            return body;
        }

        private Panel BuildPreviewCard()
        {
            Panel card = CreateCard();
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(14);

            Panel heading = new Panel();
            heading.Dock = DockStyle.Top;
            heading.Height = 48;
            heading.Tag = ThemeRole.Surface;

            Label title = CreateLabel("横版预览", false);
            title.Font = new Font("Microsoft YaHei UI", 10.0f, FontStyle.Bold, GraphicsUnit.Point);
            title.Dock = DockStyle.Left;
            title.Width = 120;
            heading.Controls.Add(title);

            _sendButton = CreateButton("发送到屏幕", true, 124);
            _sendButton.Dock = DockStyle.Right;
            _sendButton.Height = 38;
            _sendButton.Enabled = false;
            _sendButton.Click += async delegate { await SendAsync(); };
            heading.Controls.Add(_sendButton);

            _preview = new CropPreviewBox();
            _preview.BackColor = Color.Black;
            _preview.AllowDrop = true;
            _preview.DragEnter += OnImageDragEnter;
            _preview.DragDrop += OnImageDragDrop;
            _preview.CropChanged += delegate
            {
                if (_fitMode == ImageFitMode.Custom && _source != null)
                {
                    _customCrop = _preview.NormalizedCrop;
                    _transportDirty = true;
                    _infoLabel.Text = string.Format(
                        "原图 {0} × {1}\r\n拖动缩放适配 · 发送时生成画面",
                        _source.Width, _source.Height);
                    _sendButton.Enabled = true;
                }
            };
            _toolTip.SetToolTip(_preview, "可以把 PNG、JPG 或 BMP 文件拖到此处。");

            AspectRatioHost previewHost = new AspectRatioHost();
            previewHost.Dock = DockStyle.Fill;
            previewHost.TargetAspectRatio = 3.0f;
            previewHost.Tag = ThemeRole.Surface;
            previewHost.Controls.Add(_preview);
            card.Controls.Add(previewHost);
            card.Controls.Add(heading);
            heading.BringToFront();
            return card;
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

            RoundedButton choose = CreateButton("选择图片", true, 240);
            choose.Click += delegate { ChooseImage(); };
            stack.Controls.Add(choose);

            Label hint = CreateLabel("支持 PNG、JPG、JPEG、BMP；也可以直接拖入文件。", true);
            hint.Width = 240;
            hint.Height = 34;
            hint.Margin = new Padding(0, 4, 0, 4);
            stack.Controls.Add(hint);

            stack.Controls.Add(CreateSection("适配方式"));
            FlowLayoutPanel fitOptions = CreateOptionRow(265);
            AddFitOption(fitOptions, "完整显示", ImageFitMode.Contain);
            AddFitOption(fitOptions, "裁剪铺满", ImageFitMode.Cover);
            AddFitOption(fitOptions, "拉伸", ImageFitMode.Stretch);
            AddFitOption(fitOptions, "拖动缩放", ImageFitMode.Custom);
            stack.Controls.Add(fitOptions);

            stack.Controls.Add(CreateSection("留边颜色"));
            FlowLayoutPanel backgrounds = CreateOptionRow(240);
            AddBackgroundOption(backgrounds, "黑色", false);
            AddBackgroundOption(backgrounds, "白色", true);
            stack.Controls.Add(backgrounds);

            stack.Controls.Add(CreateSection("图片信息"));
            _infoLabel = CreateLabel("等待选择图片", true);
            _infoLabel.Width = 240;
            _infoLabel.Height = 56;
            _infoLabel.Margin = new Padding(0, 2, 0, 0);
            stack.Controls.Add(_infoLabel);
            return card;
        }

        private Panel BuildFooter()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.Padding = new Padding(0, 13, 0, 0);
            panel.Tag = ThemeRole.Window;

            _statusLabel.Text = "请选择图片";
            _statusLabel.Dock = DockStyle.Fill;
            panel.Controls.Add(_statusLabel);

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Right;
            actions.Width = 230;
            actions.WrapContents = false;
            actions.FlowDirection = FlowDirection.LeftToRight;
            actions.Tag = ThemeRole.Window;
            panel.Controls.Add(actions);

            if (!_embedded)
            {
                RoundedButton close = CreateButton("关闭", false, 90);
                close.Click += delegate { Close(); };
                actions.Controls.Add(close);
            }

            actions.BringToFront();
            return panel;
        }

        private void ChooseImage()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择要显示的图片";
                dialog.Filter = "支持的图片|*.png;*.jpg;*.jpeg;*.bmp|PNG 图片|*.png|JPEG 图片|*.jpg;*.jpeg|BMP 图片|*.bmp";
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;
                if (!string.IsNullOrWhiteSpace(_lastDirectory) && Directory.Exists(_lastDirectory))
                {
                    dialog.InitialDirectory = _lastDirectory;
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    LoadImage(dialog.FileName);
                }
            }
        }

        private void OnImageDragEnter(object sender, DragEventArgs e)
        {
            string path = GetSingleDroppedFile(e.Data);
            e.Effect = IsSupportedPath(path) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnImageDragDrop(object sender, DragEventArgs e)
        {
            string path = GetSingleDroppedFile(e.Data);
            if (IsSupportedPath(path))
            {
                LoadImage(path);
            }
        }

        private void LoadImage(string path)
        {
            try
            {
                Bitmap loaded = ImageContentRenderer.LoadOrientedBitmap(path);
                if (_source != null)
                {
                    _source.Dispose();
                }
                _source = loaded;
                _imagePath = path;
                _lastDirectory = Path.GetDirectoryName(path) ?? string.Empty;
                _customCrop = RectangleF.Empty;
                RebuildPreview();
                _statusLabel.Text = "图片已载入，可以发送到屏幕";
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, "无法打开图片：" + exception.Message,
                    "图片读取失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RebuildPreview()
        {
            if (_source == null)
            {
                return;
            }

            try
            {
                Color background = _backgroundWhite ? Color.White : Color.Black;
                if (_fitMode == ImageFitMode.Custom)
                {
                    _preview.SetImage(_source, _customCrop.Width <= 0);
                    if (_customCrop.Width > 0)
                    {
                        _preview.SetCrop(_customCrop);
                    }
                    _preview.CropEnabled = true;
                    _customCrop = _preview.NormalizedCrop;
                }
                else
                {
                    _preview.CropEnabled = false;
                }
                RebuildTransportOnly();
                if (_fitMode != ImageFitMode.Custom)
                {
                    using (Bitmap logicalPreview = ImageContentRenderer.CreateLogicalPreview(
                        _transportPng))
                    {
                        _preview.SetImage(logicalPreview, false);
                    }
                }
                ApplyTheme();
            }
            catch (Exception exception)
            {
                _sendButton.Enabled = false;
                _statusLabel.Text = "预览生成失败";
                MessageBox.Show(this, "无法生成屏幕画面：" + exception.Message,
                    "图片处理失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RebuildTransportOnly()
        {
            if (_source == null)
            {
                return;
            }
            Color background = _backgroundWhite ? Color.White : Color.Black;
            _transportPng = ImageContentRenderer.CreateTransportPng(
                _source, _fitMode, background, _customCrop);
            _transportDirty = false;
            TurzxProtocol.DescribePanelPng(_transportPng);
            _infoLabel.Text = string.Format("原图 {0} × {1}\r\n传输 PNG {2:N0} 字节{3}",
                _source.Width, _source.Height, _transportPng.Length,
                _fitMode == ImageFitMode.Custom ? " · 拖动缩放适配" : string.Empty);
            _sendButton.Enabled = true;
        }

        private async Task SendAsync()
        {
            if (_source == null || _sendImage == null)
            {
                return;
            }

            _sendButton.Enabled = false;
            _sendButton.Text = "正在发送…";
            _statusLabel.Text = "正在写入 TURZX 屏幕…";
            try
            {
                if (_transportDirty || _transportPng == null)
                {
                    RebuildTransportOnly();
                    _sendButton.Enabled = false;
                }
                bool success = await _sendImage(_transportPng,
                    Path.GetFileName(_imagePath) ?? "图片");
                _statusLabel.Text = success ? "图片已显示在屏幕上" : "发送失败，请查看主页面状态";
            }
            catch (Exception exception)
            {
                _statusLabel.Text = "图片生成或发送失败";
                MessageBox.Show(this, "无法发送图片：" + exception.Message,
                    "图片发送失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (!IsDisposed)
                {
                    _sendButton.Text = "发送到屏幕";
                    _sendButton.Enabled = _transportPng != null;
                    ApplyTheme();
                }
            }
        }

        private void AddFitOption(FlowLayoutPanel host, string text, ImageFitMode mode)
        {
            RadioButton option = CreateOption(text, _fitMode == mode, 60);
            option.CheckedChanged += delegate
            {
                if (option.Checked)
                {
                    _fitMode = mode;
                    RebuildPreview();
                }
            };
            host.Controls.Add(option);
        }

        private void AddBackgroundOption(FlowLayoutPanel host, string text, bool white)
        {
            RadioButton option = CreateOption(text, _backgroundWhite == white, 114);
            option.CheckedChanged += delegate
            {
                if (option.Checked)
                {
                    _backgroundWhite = white;
                    RebuildPreview();
                }
            };
            host.Controls.Add(option);
        }

        private RadioButton CreateOption(string text, bool selected, int width)
        {
            RadioButton option = new RadioButton();
            option.Appearance = Appearance.Button;
            option.Text = text;
            option.TextAlign = ContentAlignment.MiddleCenter;
            option.Width = width;
            option.Height = 38;
            option.Margin = new Padding(0, 0, 6, 0);
            option.Checked = selected;
            option.Tag = ThemeRole.ThemeOption;
            return option;
        }

        private static FlowLayoutPanel CreateOptionRow(int width)
        {
            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.Width = width;
            panel.Height = 43;
            panel.WrapContents = false;
            panel.Tag = ThemeRole.Surface;
            return panel;
        }

        private static Label CreateSection(string text)
        {
            Label label = CreateLabel(text, false);
            label.Width = 240;
            label.Height = 31;
            label.Margin = new Padding(0, 10, 0, 3);
            label.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
            return label;
        }

        private static string GetSingleDroppedFile(IDataObject data)
        {
            if (data == null || !data.GetDataPresent(DataFormats.FileDrop))
            {
                return null;
            }
            string[] files = data.GetData(DataFormats.FileDrop) as string[];
            return files != null && files.Length == 1 ? files[0] : null;
        }

        private static bool IsSupportedPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".png" || extension == ".jpg" ||
                extension == ".jpeg" || extension == ".bmp";
        }

        private void ApplyTheme()
        {
            AppTheme theme = ThemeManager.Resolve(_appearanceMode);
            ThemeManager.Apply(this, theme);
            _preview.BackColor = Color.Black;
        }

        private static Panel CreateCard()
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
    }
}
