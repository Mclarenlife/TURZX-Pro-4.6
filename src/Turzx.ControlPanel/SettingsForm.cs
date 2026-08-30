using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Turzx.ControlPanel
{
    internal sealed class SettingsForm : Form
    {
        private readonly ControlPanelSettings _original;
        private readonly ControlPanelSettings _draft;
        private readonly Action<ThemeMode> _previewTheme;
        private readonly Func<Task> _refreshDevice;
        private readonly Func<Task> _sendStatic;
        private readonly Func<Action<int>, Task> _runMotion;
        private readonly string _sensorSummary;
        private readonly bool _deviceActionsEnabled;
        private readonly Dictionary<string, Button> _navigation = new Dictionary<string, Button>();
        private readonly FlowLayoutPanel _content;
        private readonly ToolTip _toolTip = new ToolTip();
        private bool _saved;
        private bool _autostart;
        private string _activeCategory;
        private ProgressBar _deviceProgress;

        public SettingsForm(ControlPanelSettings settings,
            Action<ThemeMode> previewTheme,
            Func<Task> refreshDevice,
            Func<Task> sendStatic,
            Func<Action<int>, Task> runMotion,
            string sensorSummary,
            bool deviceActionsEnabled)
        {
            _original = settings.Clone();
            _draft = settings.Clone();
            _previewTheme = previewTheme;
            _refreshDevice = refreshDevice;
            _sendStatic = sendStatic;
            _runMotion = runMotion;
            _sensorSummary = string.IsNullOrWhiteSpace(sensorSummary) ? "尚未采集" : sensorSummary;
            _deviceActionsEnabled = deviceActionsEnabled;
            _autostart = ControlPanelSettings.IsAutostartEnabled();

            Text = "设置";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(720, 570);
            MinimumSize = new Size(680, 540);
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei UI", 9.0f, FontStyle.Regular, GraphicsUnit.Point);
            Tag = ThemeRole.Window;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 2;
            root.RowCount = 3;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 166));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            root.Tag = ThemeRole.Window;
            Controls.Add(root);

            Panel header = BuildHeader();
            root.Controls.Add(header, 0, 0);
            root.SetColumnSpan(header, 2);

            FlowLayoutPanel navigation = BuildNavigation();
            root.Controls.Add(navigation, 0, 1);

            _content = new FlowLayoutPanel();
            _content.Dock = DockStyle.Fill;
            _content.FlowDirection = FlowDirection.TopDown;
            _content.WrapContents = false;
            _content.AutoScroll = true;
            _content.Padding = new Padding(28, 20, 28, 20);
            _content.Tag = ThemeRole.Window;
            root.Controls.Add(_content, 1, 1);

            Panel footer = BuildFooter();
            root.Controls.Add(footer, 0, 2);
            root.SetColumnSpan(footer, 2);

            ShowCategory("外观");
            ApplyTheme();
        }

        public ControlPanelSettings ResultSettings
        {
            get { return _draft.Clone(); }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyTheme();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (!_saved && _previewTheme != null)
            {
                _previewTheme(_original.AppearanceMode);
            }
            _toolTip.Dispose();
            base.OnFormClosed(e);
        }

        private Panel BuildHeader()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.Padding = new Padding(24, 14, 24, 8);
            panel.Tag = ThemeRole.Surface;

            Label title = new Label();
            title.AutoSize = true;
            title.Text = "设置";
            title.Font = new Font("Microsoft YaHei UI", 17.0f, FontStyle.Bold, GraphicsUnit.Point);
            title.Location = new Point(22, 12);
            title.Tag = ThemeRole.Text;
            panel.Controls.Add(title);

            Label subtitle = new Label();
            subtitle.AutoSize = true;
            subtitle.Text = "外观、屏幕与设备选项";
            subtitle.Location = new Point(92, 21);
            subtitle.Tag = ThemeRole.Muted;
            panel.Controls.Add(subtitle);
            return panel;
        }

        private FlowLayoutPanel BuildNavigation()
        {
            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.FlowDirection = FlowDirection.TopDown;
            panel.WrapContents = false;
            panel.Padding = new Padding(14, 20, 14, 12);
            panel.Tag = ThemeRole.Surface;

            string[] categories = { "外观", "启动与行为", "屏幕", "数据", "设备", "关于" };
            foreach (string category in categories)
            {
                Button button = new Button();
                button.Text = category;
                button.Width = 136;
                button.Height = 39;
                button.Margin = new Padding(0, 0, 0, 7);
                button.TextAlign = ContentAlignment.MiddleLeft;
                button.Padding = new Padding(12, 0, 0, 0);
                button.Cursor = Cursors.Hand;
                button.Tag = ThemeRole.SecondaryButton;
                string captured = category;
                button.Click += delegate { ShowCategory(captured); };
                _navigation[category] = button;
                panel.Controls.Add(button);
            }
            return panel;
        }

        private Panel BuildFooter()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.Padding = new Padding(18, 13, 22, 12);
            panel.Tag = ThemeRole.Surface;

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.AutoSize = true;
            actions.FlowDirection = FlowDirection.LeftToRight;
            actions.WrapContents = false;
            actions.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            actions.Location = new Point(500, 12);
            actions.Tag = ThemeRole.Surface;
            panel.Controls.Add(actions);

            Button cancel = CreateButton("取消", false, 86);
            cancel.DialogResult = DialogResult.Cancel;
            actions.Controls.Add(cancel);

            Button save = CreateButton("保存", true, 92);
            save.Click += delegate { SaveAndClose(); };
            actions.Controls.Add(save);

            panel.Resize += delegate
            {
                actions.Left = panel.ClientSize.Width - actions.Width - 22;
            };
            CancelButton = cancel;
            AcceptButton = save;
            return panel;
        }

        private void ShowCategory(string category)
        {
            _activeCategory = category;
            _content.SuspendLayout();
            _content.Controls.Clear();

            foreach (KeyValuePair<string, Button> item in _navigation)
            {
                item.Value.Tag = item.Key == category ?
                    ThemeRole.PrimaryButton : ThemeRole.SecondaryButton;
            }

            if (category == "外观") BuildAppearance();
            else if (category == "启动与行为") BuildStartup();
            else if (category == "屏幕") BuildScreen();
            else if (category == "数据") BuildData();
            else if (category == "设备") BuildDevice();
            else BuildAbout();

            _content.ResumeLayout();
            ApplyTheme();
        }

        private void BuildAppearance()
        {
            AddHeading("外观", "选择控制软件的黑白配色。此设置不会改变小屏中的仪表盘主题。");

            FlowLayoutPanel options = new FlowLayoutPanel();
            options.Width = 470;
            options.Height = 54;
            options.WrapContents = false;
            options.Margin = new Padding(0, 12, 0, 16);
            options.Tag = ThemeRole.Window;
            _content.Controls.Add(options);

            AddThemeOption(options, "跟随系统", ThemeMode.System);
            AddThemeOption(options, "浅色", ThemeMode.Light);
            AddThemeOption(options, "深色", ThemeMode.Dark);

            AddSection("界面说明");
            AddParagraph("跟随系统会读取 Windows 的应用模式，并在系统深浅色变化时自动同步。手动选择浅色或深色后，系统变化不会覆盖你的选择。");
            AddInfoCard("黑白简约", "界面不使用自定义强调色；只有警告和错误保留少量语义色。", false);
        }

        private void AddThemeOption(FlowLayoutPanel host, string text, ThemeMode mode)
        {
            RadioButton option = new RadioButton();
            option.Appearance = Appearance.Button;
            option.Text = text;
            option.TextAlign = ContentAlignment.MiddleCenter;
            option.Width = 132;
            option.Height = 40;
            option.Margin = new Padding(0, 0, 10, 0);
            option.Tag = ThemeRole.ThemeOption;
            option.Checked = _draft.AppearanceMode == mode;
            option.CheckedChanged += delegate
            {
                if (!option.Checked)
                {
                    return;
                }
                _draft.AppearanceMode = mode;
                if (_previewTheme != null)
                {
                    _previewTheme(mode);
                }
                ApplyTheme();
            };
            host.Controls.Add(option);
        }

        private void BuildStartup()
        {
            AddHeading("启动与行为", "管理软件启动方式。未完成项目已预留固定位置。");
            CheckBox autostart = AddCheckBox("开机时启动 TURZX 屏幕控制", _autostart, true);
            autostart.CheckedChanged += delegate { _autostart = autostart.Checked; };

            AddCheckBox("启动后自动开始实时监控   ·   即将支持", false, false);
            AddCheckBox("关闭窗口时最小化到系统托盘   ·   即将支持", false, false);
            AddCheckBox("启动时最小化   ·   即将支持", false, false);
        }

        private void BuildScreen()
        {
            AddHeading("屏幕", "显示刷新和当前设备布局信息。");
            AddSection("刷新频率");

            FlowLayoutPanel row = new FlowLayoutPanel();
            row.Width = 470;
            row.Height = 46;
            row.WrapContents = false;
            row.Tag = ThemeRole.Window;
            NumericUpDown refresh = new NumericUpDown();
            refresh.Minimum = 500;
            refresh.Maximum = 5000;
            refresh.Increment = 250;
            refresh.Value = _draft.RefreshMilliseconds;
            refresh.Width = 150;
            refresh.Height = 34;
            refresh.Tag = ThemeRole.Input;
            refresh.ValueChanged += delegate { _draft.RefreshMilliseconds = (int)refresh.Value; };
            row.Controls.Add(refresh);
            Label unit = CreateLabel("毫秒（推荐 1000）", true);
            unit.Margin = new Padding(12, 8, 0, 0);
            row.Controls.Add(unit);
            _content.Controls.Add(row);

            AddSection("当前布局");
            AddInfoCard("TURZX1.0", "960 × 320 · 横向反装 · WinUSB", false);
            AddInfoCard("屏幕方向", "当前方向已锁定，避免误操作造成倒置或花屏。", false);
            AddPlaceholderButton("管理屏幕布局   ·   即将支持");
        }

        private void BuildData()
        {
            AddHeading("数据", "查看实时监控使用的数据源与传感器状态。");
            AddInfoCard("数据来源", "Windows 系统数据 · LibreHardwareMonitor · NVIDIA SMI", false);
            AddInfoCard("最近一次传感器状态", _sensorSummary, false);
            AddInfoCard("CPU 温度与风扇", "普通权限下可能不可用；当前版本不会自动安装驱动。", false);
            AddPlaceholderButton("管理传感器映射   ·   即将支持");
            AddPlaceholderButton("管理员增强模式   ·   即将支持");
        }

        private void BuildDevice()
        {
            AddHeading("设备", "连接信息与安全测试。测试不会更新固件。");
            AddInfoCard("USB 设备", "TURZX1.0 · VID 1CBE · PID 0046", false);
            AddInfoCard("显示格式", "320 × 960 RGBA PNG · 逻辑画布 960 × 320", false);

            AddSection("设备操作");
            Button refresh = AddActionButton("重新检测设备");
            refresh.Enabled = _deviceActionsEnabled;
            refresh.Click += async delegate { await RunDeviceActionAsync(refresh, _refreshDevice); };
            Button staticTest = AddActionButton("发送静态测试画面");
            staticTest.Enabled = _deviceActionsEnabled;
            staticTest.Click += async delegate { await RunDeviceActionAsync(staticTest, _sendStatic); };
            Button motion = AddActionButton("运行 12 帧动态测试");
            motion.Enabled = _deviceActionsEnabled;
            motion.Click += async delegate { await RunMotionActionAsync(motion); };

            _deviceProgress = new ProgressBar();
            _deviceProgress.Width = 300;
            _deviceProgress.Height = 12;
            _deviceProgress.Minimum = 0;
            _deviceProgress.Maximum = 12;
            _deviceProgress.Margin = new Padding(0, 5, 0, 8);
            _content.Controls.Add(_deviceProgress);

            if (!_deviceActionsEnabled)
            {
                _toolTip.SetToolTip(refresh, "实时监控运行期间不能执行设备测试。");
                _toolTip.SetToolTip(staticTest, "实时监控运行期间不能执行设备测试。");
                _toolTip.SetToolTip(motion, "实时监控运行期间不能执行设备测试。");
            }

            AddParagraph("运行动态测试时，设置窗口会暂时禁用对应按钮；执行结果会通过连接状态和提示对话框反馈。");
        }

        private void BuildAbout()
        {
            AddHeading("关于", "TURZX 4.6 英寸 USB 屏幕的独立控制软件。");
            AddInfoCard("TURZX 屏幕控制", "UI V2 · WinUSB · .NET Framework 4.8", false);
            AddInfoCard("设置文件", ControlPanelSettings.GetSettingsPath(), false);
            AddInfoCard("第三方组件", "LibreHardwareMonitor 0.9.6 · MPL-2.0", false);
            AddPlaceholderButton("导入配置   ·   即将支持");
            AddPlaceholderButton("导出配置   ·   即将支持");
        }

        private async Task RunDeviceActionAsync(Button button, Func<Task> action)
        {
            if (action == null)
            {
                return;
            }
            button.Enabled = false;
            string previous = button.Text;
            button.Text = "正在执行…";
            try
            {
                await action();
            }
            finally
            {
                if (!IsDisposed)
                {
                    button.Text = previous;
                    button.Enabled = true;
                    ApplyTheme();
                }
            }
        }

        private async Task RunMotionActionAsync(Button button)
        {
            if (_runMotion == null)
            {
                return;
            }
            button.Enabled = false;
            string previous = button.Text;
            button.Text = "正在执行…";
            if (_deviceProgress != null)
            {
                _deviceProgress.Value = 0;
            }
            try
            {
                await _runMotion(delegate(int value)
                {
                    if (_deviceProgress != null && !_deviceProgress.IsDisposed)
                    {
                        _deviceProgress.Value = Math.Max(0, Math.Min(12, value));
                    }
                });
            }
            finally
            {
                if (!IsDisposed)
                {
                    button.Text = previous;
                    button.Enabled = true;
                    ApplyTheme();
                }
            }
        }

        private void AddHeading(string title, string description)
        {
            Label heading = CreateLabel(title, false);
            heading.Font = new Font("Microsoft YaHei UI", 18.0f, FontStyle.Bold, GraphicsUnit.Point);
            heading.Width = 470;
            heading.Height = 42;
            heading.Margin = new Padding(0);
            _content.Controls.Add(heading);

            Label text = CreateLabel(description, true);
            text.Width = 470;
            text.Height = 45;
            text.Margin = new Padding(0, 0, 0, 12);
            _content.Controls.Add(text);
        }

        private void AddSection(string text)
        {
            Label label = CreateLabel(text, false);
            label.Font = new Font("Microsoft YaHei UI", 10.0f, FontStyle.Bold, GraphicsUnit.Point);
            label.Width = 470;
            label.Height = 32;
            label.Margin = new Padding(0, 12, 0, 4);
            _content.Controls.Add(label);
        }

        private void AddParagraph(string text)
        {
            Label label = CreateLabel(text, true);
            label.Width = 470;
            label.Height = 54;
            label.Margin = new Padding(0, 4, 0, 10);
            _content.Controls.Add(label);
        }

        private CheckBox AddCheckBox(string text, bool value, bool enabled)
        {
            CheckBox check = new CheckBox();
            check.Text = text;
            check.Checked = value;
            check.AutoCheck = enabled;
            check.TabStop = enabled;
            check.Width = 470;
            check.Height = 42;
            check.Margin = new Padding(0, 6, 0, 2);
            check.Tag = enabled ? ThemeRole.Text : ThemeRole.Muted;
            _content.Controls.Add(check);
            return check;
        }

        private void AddInfoCard(string title, string description, bool placeholder)
        {
            Panel card = new Panel();
            card.Width = 470;
            card.Height = 74;
            card.Margin = new Padding(0, 5, 0, 5);
            card.BorderStyle = BorderStyle.FixedSingle;
            card.Tag = placeholder ? ThemeRole.SurfaceAlt : ThemeRole.Surface;

            Label titleLabel = CreateLabel(title, false);
            titleLabel.Location = new Point(14, 8);
            titleLabel.Size = new Size(438, 24);
            titleLabel.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
            card.Controls.Add(titleLabel);
            Label descriptionLabel = CreateLabel(description, true);
            descriptionLabel.Location = new Point(14, 34);
            descriptionLabel.Size = new Size(438, 27);
            card.Controls.Add(descriptionLabel);
            _content.Controls.Add(card);
        }

        private Button AddActionButton(string text)
        {
            Button button = CreateButton(text, false, 300);
            button.Margin = new Padding(0, 4, 0, 4);
            _content.Controls.Add(button);
            return button;
        }

        private void AddPlaceholderButton(string text)
        {
            Button button = CreateButton(text, false, 300);
            button.Tag = ThemeRole.Placeholder;
            button.TabStop = false;
            button.Cursor = Cursors.Default;
            button.Margin = new Padding(0, 6, 0, 2);
            _toolTip.SetToolTip(button, "此入口已预留，将在后续版本中实现。");
            _content.Controls.Add(button);
        }

        private static Button CreateButton(string text, bool primary, int width)
        {
            Button button = new Button();
            button.Text = text;
            button.Width = width;
            button.Height = 40;
            button.Margin = new Padding(5, 0, 0, 0);
            button.Cursor = Cursors.Hand;
            button.Tag = primary ? ThemeRole.PrimaryButton : ThemeRole.SecondaryButton;
            return button;
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

        private void SaveAndClose()
        {
            try
            {
                ControlPanelSettings.SetAutostart(_autostart, Application.ExecutablePath);
                _saved = true;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "无法保存设置",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ApplyTheme()
        {
            AppTheme theme = ThemeManager.Resolve(_draft.AppearanceMode);
            ThemeManager.Apply(this, theme);
            foreach (KeyValuePair<string, Button> item in _navigation)
            {
                item.Value.Tag = item.Key == _activeCategory ?
                    ThemeRole.PrimaryButton : ThemeRole.SecondaryButton;
                ThemeManager.Apply(item.Value, theme);
            }
        }
    }
}
