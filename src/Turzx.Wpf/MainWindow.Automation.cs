using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using Turzx.ControlPanel;
using Turzx.Desktop.Models;
using Turzx.Desktop.Services;

namespace Turzx.Desktop
{
    public partial class MainWindow
    {
        private ObservableCollection<AutomationRule> _automationRules;
        private AutomationRule _selectedAutomation;
        private AutomationAction _selectedAutomationAction;
        private bool _automationUiLoading;
        private DispatcherTimer _automationTimer;
        private CancellationTokenSource _automationCancellation;
        private readonly Queue<AutomationRule> _automationQueue =
            new Queue<AutomationRule>();
        private readonly HashSet<string> _automationPending =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _automationProcessStates =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private bool _automationQueueBusy;
        private bool _automationTickBusy;
        private Task _automationOutputTask;
        private AutomationFlashSpeed _automationFlashSpeedSelection;

        private void InitializeAutomations()
        {
            _automationRules = AutomationStore.LoadOrDefault();
            if (_automationRules.Count == 0)
            {
                foreach (AutomationRule rule in CreateStarterAutomations())
                    _automationRules.Add(rule);
                SaveAutomations(false);
            }

            bool changed = false;
            foreach (AutomationRule rule in _automationRules)
            {
                if (string.IsNullOrWhiteSpace(rule.Id))
                {
                    rule.Id = Guid.NewGuid().ToString("N");
                    changed = true;
                }
                if (rule.Actions == null)
                {
                    rule.Actions = new ObservableCollection<AutomationAction>();
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(rule.IntervalStartTime))
                {
                    rule.IntervalStartTime = "00:00";
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(rule.IntervalEndTime))
                {
                    rule.IntervalEndTime = "23:59";
                    changed = true;
                }
                foreach (AutomationAction action in rule.Actions)
                {
                    if (string.IsNullOrWhiteSpace(action.Id))
                    {
                        action.Id = Guid.NewGuid().ToString("N");
                        changed = true;
                    }
                }
                if (rule.TriggerKind == AutomationTriggerKind.Interval &&
                    rule.LastTriggeredAt == DateTime.MinValue)
                {
                    rule.LastTriggeredAt = DateTime.Now;
                    changed = true;
                }
            }
            if (changed) SaveAutomations(false);

            AutomationRuleList.ItemsSource = _automationRules;
            AutomationCountText.Text = _automationRules.Count + " 条";
            _automationTimer = new DispatcherTimer();
            _automationTimer.Interval = TimeSpan.FromSeconds(1);
            _automationTimer.Tick += AutomationTimer_Tick;
            InitializeAutomationProcessStates();
            if (_automationRules.Count > 0)
                AutomationRuleList.SelectedIndex = 0;
        }

        private static IEnumerable<AutomationRule> CreateStarterAutomations()
        {
            AutomationRule sleep = new AutomationRule
            {
                Name = "晚上十点提醒睡觉",
                Enabled = false,
                TriggerKind = AutomationTriggerKind.DailyTime,
                DailyTime = "22:00"
            };
            sleep.Actions.Add(new AutomationAction
            {
                Kind = AutomationActionKind.Notification,
                Text = "已经晚上十点了，该准备休息了。"
            });

            AutomationRule water = new AutomationRule
            {
                Name = "每小时提醒喝水",
                Enabled = false,
                TriggerKind = AutomationTriggerKind.Interval,
                IntervalMinutes = 60,
                IntervalStartTime = "00:00",
                IntervalEndTime = "23:59",
                LastTriggeredAt = DateTime.Now
            };
            water.Actions.Add(new AutomationAction
            {
                Kind = AutomationActionKind.Notification,
                Text = "休息一下，记得喝水。"
            });

            AutomationRule program = new AutomationRule
            {
                Name = "启动程序后切换监控",
                Enabled = false,
                TriggerKind = AutomationTriggerKind.ProcessStarted
            };
            program.Actions.Add(new AutomationAction
            {
                Kind = AutomationActionKind.PlayVideo,
                DurationSeconds = 2
            });
            program.Actions.Add(new AutomationAction
            {
                Kind = AutomationActionKind.StartTheme
            });
            return new[] { sleep, water, program };
        }

        private void StartAutomationEngine()
        {
            if (_automationTimer == null || _automationTimer.IsEnabled) return;
            if (_automationCancellation != null) _automationCancellation.Dispose();
            _automationCancellation = new CancellationTokenSource();
            InitializeAutomationProcessStates();
            _automationTimer.Start();
            AutomationGlobalStatusText.Text = "自动化正在后台运行";
        }

        private void StopAutomationEngine()
        {
            if (_automationTimer != null) _automationTimer.Stop();
            if (_automationCancellation != null)
            {
                _automationCancellation.Cancel();
                _automationCancellation.Dispose();
                _automationCancellation = null;
            }
            SaveAutomations(false);
        }

        private void InitializeAutomationProcessStates()
        {
            if (_automationRules == null) return;
            foreach (AutomationRule rule in _automationRules.Where(delegate(AutomationRule item)
            {
                return item.TriggerKind == AutomationTriggerKind.ProcessStarted;
            }))
                _automationProcessStates[rule.Id] = IsConfiguredProcessRunning(rule.ProcessPath);
        }

        private void AutomationTimer_Tick(object sender, EventArgs e)
        {
            if (_automationTickBusy || _automationRules == null) return;
            _automationTickBusy = true;
            try
            {
                DateTime now = DateTime.Now;
                bool triggeredAny = false;
                foreach (AutomationRule rule in _automationRules.ToList())
                {
                    bool due = false;
                    if (rule.TriggerKind == AutomationTriggerKind.ProcessStarted)
                    {
                        bool running = IsConfiguredProcessRunning(rule.ProcessPath);
                        bool previous;
                        if (!_automationProcessStates.TryGetValue(rule.Id, out previous))
                            previous = running;
                        _automationProcessStates[rule.Id] = running;
                        due = rule.Enabled && !previous && running;
                    }
                    else if (rule.Enabled)
                    {
                        due = IsTimedAutomationDue(rule, now);
                    }

                    if (!due) continue;
                    rule.LastTriggeredAt = now;
                    triggeredAny = true;
                    EnqueueAutomation(rule);
                }
                if (triggeredAny) SaveAutomations(false);
            }
            finally
            {
                _automationTickBusy = false;
            }
        }

        private static bool IsTimedAutomationDue(AutomationRule rule, DateTime now)
        {
            if (rule.TriggerKind == AutomationTriggerKind.Interval)
            {
                int minutes = Math.Max(1, rule.IntervalMinutes);
                TimeSpan startTime;
                TimeSpan endTime;
                if (!TryParseAutomationTime(rule.IntervalStartTime, out startTime) ||
                    !TryParseAutomationTime(rule.IntervalEndTime, out endTime)) return false;

                DateTime rangeStart = now.Date.Add(startTime);
                DateTime rangeEnd = now.Date.Add(endTime);
                if (endTime < startTime)
                {
                    if (now.TimeOfDay <= endTime)
                        rangeStart = rangeStart.AddDays(-1);
                    else
                        rangeEnd = rangeEnd.AddDays(1);
                }
                if (now < rangeStart || now > rangeEnd) return false;

                double elapsedMinutes = (now - rangeStart).TotalMinutes;
                long slotIndex = (long)Math.Floor(elapsedMinutes / minutes);
                DateTime intervalScheduledAt = rangeStart.AddMinutes(slotIndex * minutes);
                return intervalScheduledAt <= rangeEnd &&
                    rule.LastTriggeredAt < intervalScheduledAt;
            }
            TimeSpan time;
            if (!TryParseAutomationTime(rule.DailyTime, out time)) return false;
            DateTime scheduledAt = now.Date.Add(time);
            return now >= scheduledAt && rule.LastTriggeredAt < scheduledAt;
        }

        private static bool TryParseAutomationTime(string text, out TimeSpan time)
        {
            if (!TimeSpan.TryParseExact((text ?? string.Empty).Trim(), @"hh\:mm",
                CultureInfo.InvariantCulture, out time) &&
                !TimeSpan.TryParse((text ?? string.Empty).Trim(), out time)) return false;
            return time >= TimeSpan.Zero && time < TimeSpan.FromDays(1);
        }

        private static bool IsConfiguredProcessRunning(string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath)) return false;
            string processName = Path.GetFileNameWithoutExtension(configuredPath.Trim());
            if (string.IsNullOrWhiteSpace(processName)) return false;
            Process[] processes = null;
            try
            {
                processes = Process.GetProcessesByName(processName);
                if (!Path.IsPathRooted(configuredPath)) return processes.Length > 0;
                string expected = Path.GetFullPath(configuredPath);
                foreach (Process process in processes)
                {
                    try
                    {
                        string actual = process.MainModule.FileName;
                        if (string.Equals(actual, expected,
                            StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    catch
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (processes != null)
                    foreach (Process process in processes) process.Dispose();
            }
        }

        private void EnqueueAutomation(AutomationRule rule)
        {
            if (rule == null || _automationPending.Contains(rule.Id)) return;
            _automationPending.Add(rule.Id);
            _automationQueue.Enqueue(rule);
            if (!_automationQueueBusy) ProcessAutomationQueueAsync();
        }

        private async void ProcessAutomationQueueAsync()
        {
            if (_automationQueueBusy) return;
            _automationQueueBusy = true;
            try
            {
                while (_automationQueue.Count > 0)
                {
                    AutomationRule rule = _automationQueue.Dequeue();
                    CancellationToken token = _automationCancellation == null ?
                        CancellationToken.None : _automationCancellation.Token;
                    try
                    {
                        AutomationGlobalStatusText.Text = "正在运行：" + rule.Name;
                        await ExecuteAutomationAsync(rule, token);
                        AutomationGlobalStatusText.Text = "已完成：" + rule.Name;
                    }
                    catch (OperationCanceledException)
                    {
                        AutomationGlobalStatusText.Text = "自动化已停止";
                    }
                    catch (Exception exception)
                    {
                        AutomationGlobalStatusText.Text = "运行失败：" + exception.Message;
                        ShowTrayError("自动化运行失败", rule.Name + "：" + exception.Message);
                    }
                    finally
                    {
                        _automationPending.Remove(rule.Id);
                    }
                }
            }
            finally
            {
                _automationQueueBusy = false;
            }
        }

        private async Task ExecuteAutomationAsync(AutomationRule rule,
            CancellationToken token)
        {
            if (rule.Actions == null || rule.Actions.Count == 0)
                throw new InvalidOperationException("该自动化还没有动作。");
            int step = 0;
            foreach (AutomationAction action in rule.Actions.ToList())
            {
                token.ThrowIfCancellationRequested();
                step++;
                AutomationEditorStatusText.Text = "正在执行第 " + step + " 步：" +
                    action.DisplaySummary;
                switch (action.Kind)
                {
                    case AutomationActionKind.Notification:
                        if (action.NotificationSystem)
                            ShowAutomationNotification(rule.Name, action.Text);
                        if (action.NotificationScreen)
                            await ExecuteScreenNotificationAsync(action, token);
                        break;

                    case AutomationActionKind.Wait:
                        await Task.Delay(ToDelayMilliseconds(action.DurationSeconds), token);
                        break;

                    case AutomationActionKind.PlayVideo:
                        await ExecuteAutomationVideoAsync(action, token);
                        break;

                    case AutomationActionKind.ShowImage:
                        await ExecuteAutomationImageAsync(action, token);
                        break;

                    case AutomationActionKind.StartTheme:
                        await ExecuteAutomationThemeAsync(action, token);
                        break;
                }
            }
            AutomationEditorStatusText.Text = "执行完成：" + rule.Name;
        }

        private void ShowAutomationNotification(string title, string message)
        {
            if (_trayIcon == null) return;
            string content = string.IsNullOrWhiteSpace(message) ? "自动化提醒" : message.Trim();
            if (content.Length > 200) content = content.Substring(0, 200) + "…";
            _trayIcon.ShowBalloonTip(6000,
                string.IsNullOrWhiteSpace(title) ? "TURZX 自动化" : title,
                content, System.Windows.Forms.ToolTipIcon.Info);
        }

        private async Task ExecuteScreenNotificationAsync(AutomationAction action,
            CancellationToken token)
        {
            string message = string.IsNullOrWhiteSpace(action.Text) ?
                "自动化提醒" : action.Text.Trim();
            OutputSessionSettings previousOutput = OutputSessionStore.LoadOrDefault();
            bool themeWasRunning = _themeCancellation != null;
            bool videoWasRunning = _videoCancellation != null;
            bool restorePreviousOutput = previousOutput.Mode == OutputSessionMode.Image ||
                (previousOutput.Mode == OutputSessionMode.Theme && themeWasRunning) ||
                (previousOutput.Mode == OutputSessionMode.Video && videoWasRunning);
            if (_videoCancellation != null) _videoCancellation.Cancel();
            StopThemeOutput("屏幕提醒已接管屏幕");

            System.Drawing.Color firstBackground = action.NotificationStrong ?
                System.Drawing.Color.FromArgb(220, 0, 0) : System.Drawing.Color.Black;
            System.Drawing.Color firstText = action.NotificationStrong ?
                System.Drawing.Color.Yellow : System.Drawing.Color.White;
            System.Drawing.Color secondBackground = action.NotificationStrong ?
                System.Drawing.Color.Yellow : System.Drawing.Color.White;
            System.Drawing.Color secondText = action.NotificationStrong ?
                System.Drawing.Color.FromArgb(220, 0, 0) : System.Drawing.Color.Black;
            byte[] firstFrame = CreateReminderTransportFrame(message,
                firstBackground, firstText);
            byte[] secondFrame = CreateReminderTransportFrame(message,
                secondBackground, secondText);
            TimeSpan duration = TimeSpan.FromSeconds(Math.Max(0.5,
                Math.Min(3600, action.DurationSeconds)));
            int frameDelay = GetReminderFrameDelay(action.NotificationFlashSpeed);
            Exception reminderException = null;
            try
            {
                Stopwatch timer = Stopwatch.StartNew();
                bool first = true;
                while (timer.Elapsed < duration)
                {
                    token.ThrowIfCancellationRequested();
                    byte[] frame = first ? firstFrame : secondFrame;
                    await Task.Run(delegate { SendFrame(frame); }, token);
                    first = !first;
                    TimeSpan remaining = duration - timer.Elapsed;
                    if (remaining <= TimeSpan.Zero) break;
                    int delay = Math.Min(frameDelay, Math.Max(1,
                        (int)remaining.TotalMilliseconds));
                    await Task.Delay(delay, token);
                }
            }
            catch (Exception exception)
            {
                reminderException = exception;
            }
            if (restorePreviousOutput)
                await RestoreStartupOutputAsync();
            if (reminderException != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo
                    .Capture(reminderException).Throw();
        }

        private static int GetReminderFrameDelay(AutomationFlashSpeed speed)
        {
            switch (speed)
            {
                case AutomationFlashSpeed.Fast: return 250;
                case AutomationFlashSpeed.UltraFast: return 120;
                default: return 500;
            }
        }

        private static byte[] CreateReminderTransportFrame(string message,
            System.Drawing.Color background, System.Drawing.Color foreground)
        {
            using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(960, 320,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb))
            using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.Clear(background);
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                graphics.TextRenderingHint =
                    System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                System.Drawing.RectangleF area = new System.Drawing.RectangleF(
                    28, 18, 904, 284);
                using (System.Drawing.StringFormat format = new System.Drawing.StringFormat())
                {
                    format.Alignment = System.Drawing.StringAlignment.Center;
                    format.LineAlignment = System.Drawing.StringAlignment.Center;
                    format.Trimming = System.Drawing.StringTrimming.EllipsisCharacter;
                    format.FormatFlags = System.Drawing.StringFormatFlags.LineLimit;
                    float size = FindReminderFontSize(graphics, message, area, format);
                    using (System.Drawing.Font font = new System.Drawing.Font(
                        "Microsoft YaHei UI", size, System.Drawing.FontStyle.Bold,
                        System.Drawing.GraphicsUnit.Pixel))
                    using (System.Drawing.Brush brush = new System.Drawing.SolidBrush(foreground))
                        graphics.DrawString(message, font, brush, area, format);
                }
                return ImageContentRenderer.CreateTransportPng(bitmap,
                    ImageFitMode.Stretch, System.Drawing.Color.Black);
            }
        }

        private static float FindReminderFontSize(System.Drawing.Graphics graphics,
            string message, System.Drawing.RectangleF area,
            System.Drawing.StringFormat format)
        {
            for (float size = 156; size >= 26; size -= 4)
            {
                using (System.Drawing.Font font = new System.Drawing.Font(
                    "Microsoft YaHei UI", size, System.Drawing.FontStyle.Bold,
                    System.Drawing.GraphicsUnit.Pixel))
                {
                    System.Drawing.SizeF measured = graphics.MeasureString(message,
                        font, (int)area.Width, format);
                    if (measured.Height <= area.Height) return size;
                }
            }
            return 26;
        }

        private async Task ExecuteAutomationVideoAsync(AutomationAction action,
            CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(action.FilePath) || !File.Exists(action.FilePath))
                throw new FileNotFoundException("自动化视频不存在。", action.FilePath);
            if (string.IsNullOrEmpty(VideoDecoderLocator.FindFfmpeg()))
                throw new InvalidOperationException("未找到 FFmpeg，无法执行视频动作。");
            _videoPath = action.FilePath;
            VideoPathText.Text = Path.GetFileName(_videoPath);
            VideoFitCombo.SelectedIndex = 0;
            VideoRotationCombo.SelectedIndex = 0;
            VideoFpsCombo.SelectedIndex = 3;
            VideoLoopCheck.IsChecked = true;
            ResetVideoTransform();
            Task playback = PlayVideoAsync(false);
            Task duration = Task.Delay(ToDelayMilliseconds(
                Math.Max(0.1, action.DurationSeconds)), token);
            Task completed = await Task.WhenAny(playback, duration);
            if (completed == duration)
            {
                token.ThrowIfCancellationRequested();
                if (_videoCancellation != null) _videoCancellation.Cancel();
                await playback;
            }
        }

        private async Task ExecuteAutomationImageAsync(AutomationAction action,
            CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(action.FilePath) || !File.Exists(action.FilePath))
                throw new FileNotFoundException("自动化图片不存在。", action.FilePath);
            _imagePath = action.FilePath;
            ImagePathText.Text = Path.GetFileName(_imagePath);
            ImagePreview.Source = LoadBitmapSource(_imagePath);
            ImageFitCombo.SelectedIndex = 1;
            ImageRotationCombo.SelectedIndex = 0;
            ResetImageTransform();
            if (!await SendImageAsync(false))
                throw new InvalidOperationException("图片发送失败。");
            await Task.Delay(ToDelayMilliseconds(
                Math.Max(0.1, action.DurationSeconds)), token);
        }

        private async Task ExecuteAutomationThemeAsync(AutomationAction action,
            CancellationToken token)
        {
            if (!string.IsNullOrWhiteSpace(action.ThemePath))
            {
                if (!File.Exists(action.ThemePath))
                    throw new FileNotFoundException("自动化主题不存在。", action.ThemePath);
                LoadProjectIntoEditor(ThemeProjectStore.Load(action.ThemePath));
            }
            _automationOutputTask = StartThemeAsync(false);
            Task warmup = Task.Delay(700, token);
            await Task.WhenAny(_automationOutputTask, warmup);
            token.ThrowIfCancellationRequested();
        }

        private static int ToDelayMilliseconds(double seconds)
        {
            double safe = Math.Max(0, Math.Min(86400, seconds));
            return (int)Math.Round(safe * 1000);
        }

        private void AutomationNew_Click(object sender, RoutedEventArgs e)
        {
            CommitSelectedAutomation(false);
            AutomationRule rule = new AutomationRule();
            rule.Actions.Add(new AutomationAction());
            _automationRules.Add(rule);
            AutomationCountText.Text = _automationRules.Count + " 条";
            AutomationRuleList.SelectedItem = rule;
            SaveAutomations(false);
        }

        private void AutomationDuplicate_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedAutomation == null) return;
            CommitSelectedAutomation(false);
            AutomationRule copy = new AutomationRule
            {
                Name = _selectedAutomation.Name + " 副本",
                Enabled = false,
                TriggerKind = _selectedAutomation.TriggerKind,
                DailyTime = _selectedAutomation.DailyTime,
                IntervalMinutes = _selectedAutomation.IntervalMinutes,
                IntervalStartTime = _selectedAutomation.IntervalStartTime,
                IntervalEndTime = _selectedAutomation.IntervalEndTime,
                ProcessPath = _selectedAutomation.ProcessPath,
                LastTriggeredAt = DateTime.Now
            };
            foreach (AutomationAction source in _selectedAutomation.Actions)
                copy.Actions.Add(new AutomationAction
                {
                    Kind = source.Kind,
                    Text = source.Text,
                    FilePath = source.FilePath,
                    ThemePath = source.ThemePath,
                    DurationSeconds = source.DurationSeconds,
                    NotificationSystem = source.NotificationSystem,
                    NotificationScreen = source.NotificationScreen,
                    NotificationStrong = source.NotificationStrong,
                    NotificationFlashSpeed = source.NotificationFlashSpeed
                });
            _automationRules.Add(copy);
            AutomationCountText.Text = _automationRules.Count + " 条";
            AutomationRuleList.SelectedItem = copy;
            SaveAutomations(false);
        }

        private void AutomationDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedAutomation == null) return;
            MessageBoxResult result = MessageBox.Show(this,
                "确定删除自动化“" + _selectedAutomation.Name + "”吗？",
                "删除自动化", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
            int index = _automationRules.IndexOf(_selectedAutomation);
            _automationProcessStates.Remove(_selectedAutomation.Id);
            _automationRules.Remove(_selectedAutomation);
            AutomationCountText.Text = _automationRules.Count + " 条";
            AutomationRuleList.SelectedIndex = _automationRules.Count == 0 ? -1 :
                Math.Max(0, Math.Min(index, _automationRules.Count - 1));
            SaveAutomations(false);
        }

        private void AutomationSave_Click(object sender, RoutedEventArgs e)
        {
            if (CommitSelectedAutomation(true))
            {
                SaveAutomations(true);
                InitializeAutomationProcessStates();
            }
        }

        private void AutomationRunNow_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedAutomation == null) return;
            if (!CommitSelectedAutomation(true)) return;
            _selectedAutomation.LastTriggeredAt = DateTime.Now;
            SaveAutomations(false);
            EnqueueAutomation(_selectedAutomation);
        }

        private void AutomationRuleList_SelectionChanged(object sender,
            SelectionChangedEventArgs e)
        {
            if (_automationUiLoading) return;
            if (_selectedAutomation != null) CommitSelectedAutomation(false);
            _selectedAutomation = AutomationRuleList.SelectedItem as AutomationRule;
            LoadAutomationEditor(_selectedAutomation);
        }

        private void AutomationRuleEnabled_Click(object sender, RoutedEventArgs e)
        {
            AutomationRule rule = (sender as FrameworkElement) == null ? null :
                (sender as FrameworkElement).DataContext as AutomationRule;
            if (rule != null && rule.Enabled &&
                rule.TriggerKind == AutomationTriggerKind.ProcessStarted &&
                string.IsNullOrWhiteSpace(rule.ProcessPath))
            {
                rule.Enabled = false;
                AutomationEditorStatusText.Text = "请先选择需要监听的 EXE 程序。";
            }
            if (rule != null && rule.Enabled &&
                rule.TriggerKind == AutomationTriggerKind.Interval)
                rule.LastTriggeredAt = DateTime.Now;
            SaveAutomations(false);
        }

        private void LoadAutomationEditor(AutomationRule rule)
        {
            _automationUiLoading = true;
            try
            {
                AutomationEditorPanel.IsEnabled = rule != null;
                if (rule == null)
                {
                    AutomationNameText.Text = string.Empty;
                    AutomationActionList.ItemsSource = null;
                    _selectedAutomationAction = null;
                    return;
                }
                AutomationNameText.Text = rule.Name;
                AutomationEnabledToggle.IsChecked = rule.Enabled;
                AutomationTriggerCombo.SelectedIndex = (int)rule.TriggerKind;
                AutomationDailyTimeText.Text = rule.DailyTime;
                AutomationIntervalStartTimeText.Text =
                    string.IsNullOrWhiteSpace(rule.IntervalStartTime) ?
                    "00:00" : rule.IntervalStartTime;
                AutomationIntervalEndTimeText.Text =
                    string.IsNullOrWhiteSpace(rule.IntervalEndTime) ?
                    "23:59" : rule.IntervalEndTime;
                AutomationIntervalMinutesText.Text =
                    Math.Max(1, rule.IntervalMinutes).ToString(CultureInfo.InvariantCulture);
                AutomationProcessPathText.Text = rule.ProcessPath ?? string.Empty;
                UpdateAutomationTriggerPanels();
                AutomationActionList.ItemsSource = rule.Actions;
                AutomationActionList.SelectedIndex = rule.Actions.Count > 0 ? 0 : -1;
            }
            finally
            {
                _automationUiLoading = false;
            }
            _selectedAutomationAction = AutomationActionList.SelectedItem as AutomationAction;
            LoadAutomationActionEditor(_selectedAutomationAction);
        }

        private bool CommitSelectedAutomation(bool showError)
        {
            if (_selectedAutomation == null) return false;
            bool wasEnabled = _selectedAutomation.Enabled;
            CommitSelectedAction();
            AutomationAction emptyNotification = _selectedAutomation.Actions == null ? null :
                _selectedAutomation.Actions.FirstOrDefault(delegate(AutomationAction action)
                {
                    return action.Kind == AutomationActionKind.Notification &&
                        !action.NotificationSystem && !action.NotificationScreen;
                });
            if (emptyNotification != null)
                return AutomationValidationError(
                    "提醒动作至少要选择系统提醒或屏幕提醒。", showError);
            _selectedAutomation.Name = string.IsNullOrWhiteSpace(AutomationNameText.Text) ?
                "未命名自动化" : AutomationNameText.Text.Trim();
            _selectedAutomation.Enabled = AutomationEnabledToggle.IsChecked == true;
            _selectedAutomation.TriggerKind = (AutomationTriggerKind)Math.Max(0,
                AutomationTriggerCombo.SelectedIndex);
            if (_selectedAutomation.TriggerKind == AutomationTriggerKind.DailyTime)
            {
                TimeSpan time;
                if (!TimeSpan.TryParse(AutomationDailyTimeText.Text, out time) ||
                    time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
                    return AutomationValidationError("时间格式应为 HH:mm，例如 22:00。", showError);
                _selectedAutomation.DailyTime = time.ToString(@"hh\:mm");
                AutomationDailyTimeText.Text = _selectedAutomation.DailyTime;
            }
            else if (_selectedAutomation.TriggerKind == AutomationTriggerKind.Interval)
            {
                int minutes;
                if (!int.TryParse(AutomationIntervalMinutesText.Text, out minutes) || minutes < 1)
                    return AutomationValidationError("间隔时间必须是大于 0 的整数分钟。", showError);
                TimeSpan startTime;
                TimeSpan endTime;
                if (!TryParseAutomationTime(AutomationIntervalStartTimeText.Text,
                    out startTime))
                    return AutomationValidationError(
                        "开始时间格式应为 HH:mm，例如 14:10。", showError);
                if (!TryParseAutomationTime(AutomationIntervalEndTimeText.Text,
                    out endTime))
                    return AutomationValidationError(
                        "结束时间格式应为 HH:mm，例如 18:25。", showError);
                string startText = startTime.ToString(@"hh\:mm");
                string endText = endTime.ToString(@"hh\:mm");
                bool scheduleChanged = _selectedAutomation.IntervalMinutes != minutes ||
                    !string.Equals(_selectedAutomation.IntervalStartTime, startText,
                        StringComparison.Ordinal) ||
                    !string.Equals(_selectedAutomation.IntervalEndTime, endText,
                        StringComparison.Ordinal);
                _selectedAutomation.IntervalMinutes = minutes;
                _selectedAutomation.IntervalStartTime = startText;
                _selectedAutomation.IntervalEndTime = endText;
                AutomationIntervalStartTimeText.Text = startText;
                AutomationIntervalEndTimeText.Text = endText;
                if (scheduleChanged ||
                    (!wasEnabled && _selectedAutomation.Enabled) ||
                    _selectedAutomation.LastTriggeredAt == DateTime.MinValue)
                    _selectedAutomation.LastTriggeredAt = DateTime.Now;
            }
            else
            {
                _selectedAutomation.ProcessPath =
                    (AutomationProcessPathText.Text ?? string.Empty).Trim();
                if (_selectedAutomation.Enabled &&
                    string.IsNullOrWhiteSpace(_selectedAutomation.ProcessPath))
                    return AutomationValidationError("请选择需要监听的 EXE 程序。", showError);
            }
            _selectedAutomation.RefreshSummary();
            AutomationRuleList.Items.Refresh();
            return true;
        }

        private bool AutomationValidationError(string message, bool showError)
        {
            AutomationEditorStatusText.Text = message;
            if (showError) MessageBox.Show(this, message, "自动化设置",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        private void AutomationTriggerCombo_SelectionChanged(object sender,
            SelectionChangedEventArgs e)
        {
            if (AutomationDailyPanel == null) return;
            UpdateAutomationTriggerPanels();
        }

        private void UpdateAutomationTriggerPanels()
        {
            int index = Math.Max(0, AutomationTriggerCombo.SelectedIndex);
            AutomationDailyPanel.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
            AutomationIntervalPanel.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
            AutomationProcessPanel.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AutomationChooseProcess_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "可执行程序|*.exe";
            if (dialog.ShowDialog(this) == true)
                AutomationProcessPathText.Text = dialog.FileName;
        }

        private void AutomationActionList_SelectionChanged(object sender,
            SelectionChangedEventArgs e)
        {
            if (_automationUiLoading) return;
            CommitSelectedAction();
            _selectedAutomationAction = AutomationActionList.SelectedItem as AutomationAction;
            LoadAutomationActionEditor(_selectedAutomationAction);
        }

        private void AutomationActionType_SelectionChanged(object sender,
            SelectionChangedEventArgs e)
        {
            if (_automationUiLoading || _selectedAutomationAction == null) return;
            _selectedAutomationAction.Kind = (AutomationActionKind)Math.Max(0,
                AutomationActionTypeCombo.SelectedIndex);
            ConfigureAutomationActionEditor(_selectedAutomationAction.Kind);
            LoadAutomationActionValues(_selectedAutomationAction);
        }

        private void LoadAutomationActionEditor(AutomationAction action)
        {
            _automationUiLoading = true;
            try
            {
                bool enabled = action != null;
                AutomationActionTypeCombo.IsEnabled = enabled;
                AutomationActionValueText.IsEnabled = enabled;
                AutomationMediaCombo.IsEnabled = enabled;
                AutomationActionDurationText.IsEnabled = enabled;
                AutomationActionBrowseButton.IsEnabled = enabled;
                AutomationNotificationOptionsPanel.IsEnabled = enabled;
                AutomationFlashSpeedPanel.IsEnabled = enabled;
                if (!enabled)
                {
                    AutomationActionValueText.Text = string.Empty;
                    AutomationMediaCombo.SelectedIndex = -1;
                    AutomationActionDurationText.Text = string.Empty;
                    return;
                }
                AutomationActionTypeCombo.SelectedIndex = (int)action.Kind;
                ConfigureAutomationActionEditor(action.Kind);
                LoadAutomationActionValues(action);
            }
            finally
            {
                _automationUiLoading = false;
            }
        }

        private void ConfigureAutomationActionEditor(AutomationActionKind kind)
        {
            bool notification = kind == AutomationActionKind.Notification;
            bool media = kind == AutomationActionKind.PlayVideo ||
                kind == AutomationActionKind.ShowImage;
            bool screenNotification = notification &&
                AutomationScreenNotificationCheck.IsChecked == true;
            bool duration = kind == AutomationActionKind.Wait ||
                kind == AutomationActionKind.PlayVideo ||
                kind == AutomationActionKind.ShowImage || screenNotification;
            bool value = kind != AutomationActionKind.Wait;
            bool browse = kind == AutomationActionKind.StartTheme;
            AutomationActionDurationPanel.Visibility = duration ?
                Visibility.Visible : Visibility.Collapsed;
            AutomationActionValuePanel.Visibility = value ?
                Visibility.Visible : Visibility.Collapsed;
            AutomationActionValueText.Visibility = media ?
                Visibility.Collapsed : Visibility.Visible;
            AutomationMediaCombo.Visibility = media ?
                Visibility.Visible : Visibility.Collapsed;
            AutomationActionBrowseButton.Visibility = browse ?
                Visibility.Visible : Visibility.Collapsed;
            AutomationNotificationOptionsPanel.Visibility = notification ?
                Visibility.Visible : Visibility.Collapsed;
            AutomationFlashSpeedPanel.Visibility = notification ?
                Visibility.Visible : Visibility.Collapsed;
            AutomationStrongNotificationCheck.IsEnabled = screenNotification;
            AutomationFlashSpeedPanel.IsEnabled = screenNotification;
            if (media)
            {
                AutomationMediaCombo.ItemsSource =
                    kind == AutomationActionKind.ShowImage ?
                    _imageLibraryItems : _videoLibraryItems;
            }
            switch (kind)
            {
                case AutomationActionKind.Wait:
                    AutomationActionDurationLabel.Text = "等待秒数";
                    AutomationActionHintText.Text = "等待结束后继续执行下一步。";
                    break;
                case AutomationActionKind.PlayVideo:
                    AutomationActionValueLabel.Text = "视频文件";
                    AutomationActionDurationLabel.Text = "播放秒数";
                    AutomationActionHintText.Text = "达到指定时间后停止视频，再执行下一步。";
                    break;
                case AutomationActionKind.ShowImage:
                    AutomationActionValueLabel.Text = "图片文件";
                    AutomationActionDurationLabel.Text = "播放秒数";
                    AutomationActionHintText.Text = "达到指定时间后继续执行下一步。";
                    break;
                case AutomationActionKind.StartTheme:
                    AutomationActionValueLabel.Text = "主题预设";
                    AutomationActionHintText.Text = "留空时启动编辑器中的当前主题。";
                    break;
                default:
                    AutomationActionValueLabel.Text = "提醒内容";
                    AutomationActionDurationLabel.Text = "屏幕提醒秒数";
                    AutomationActionHintText.Text = screenNotification ?
                        "慢、快、超快分别约每 500、250、120 毫秒切换配色；强提醒使用红黄配色。超快闪烁可能引起不适。" :
                        "可同时选择系统通知和屏幕闪烁提醒。";
                    break;
            }
        }

        private void AutomationScreenNotification_Changed(object sender,
            RoutedEventArgs e)
        {
            if (_automationUiLoading || _selectedAutomationAction == null ||
                _selectedAutomationAction.Kind != AutomationActionKind.Notification) return;
            ConfigureAutomationActionEditor(AutomationActionKind.Notification);
        }

        private void AutomationFlashSpeed_Click(object sender, RoutedEventArgs e)
        {
            if (_automationUiLoading || _selectedAutomationAction == null) return;
            FrameworkElement button = sender as FrameworkElement;
            int selected;
            if (button == null || !int.TryParse(Convert.ToString(button.Tag),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out selected)) return;
            _automationFlashSpeedSelection = (AutomationFlashSpeed)Math.Max(0,
                Math.Min(2, selected));
            UpdateAutomationFlashSpeedButtons();
        }

        private void UpdateAutomationFlashSpeedButtons()
        {
            Wpf.Ui.Controls.Button[] buttons =
            {
                AutomationFlashSlowButton,
                AutomationFlashFastButton,
                AutomationFlashUltraFastButton
            };
            for (int index = 0; index < buttons.Length; index++)
                buttons[index].Appearance = index == (int)_automationFlashSpeedSelection ?
                    Wpf.Ui.Controls.ControlAppearance.Primary :
                    Wpf.Ui.Controls.ControlAppearance.Secondary;
        }

        private void AutomationMediaCombo_DropDownOpened(object sender, EventArgs e)
        {
            RefreshMediaLibraries();
        }

        private void AutomationMediaCombo_SelectionChanged(object sender,
            SelectionChangedEventArgs e)
        {
            if (_automationUiLoading || _mediaLibraryLoading ||
                _selectedAutomationAction == null) return;
            if (_selectedAutomationAction.Kind != AutomationActionKind.PlayVideo &&
                _selectedAutomationAction.Kind != AutomationActionKind.ShowImage) return;
            _selectedAutomationAction.FilePath =
                AutomationMediaCombo.SelectedValue as string ?? string.Empty;
            _selectedAutomationAction.RefreshSummary();
            AutomationActionList.Items.Refresh();
        }

        private void LoadAutomationActionValues(AutomationAction action)
        {
            if (action == null) return;
            AutomationSystemNotificationCheck.IsChecked = action.NotificationSystem;
            AutomationScreenNotificationCheck.IsChecked = action.NotificationScreen;
            AutomationStrongNotificationCheck.IsChecked = action.NotificationStrong;
            _automationFlashSpeedSelection = (AutomationFlashSpeed)Math.Max(0,
                Math.Min(2, (int)action.NotificationFlashSpeed));
            UpdateAutomationFlashSpeedButtons();
            switch (action.Kind)
            {
                case AutomationActionKind.PlayVideo:
                case AutomationActionKind.ShowImage:
                    RefreshMediaLibraries();
                    SelectMediaPath(AutomationMediaCombo, action.FilePath);
                    break;
                case AutomationActionKind.StartTheme:
                    AutomationActionValueText.Text = action.ThemePath ?? string.Empty;
                    break;
                default:
                    AutomationActionValueText.Text = action.Text ?? string.Empty;
                    break;
            }
            AutomationActionDurationText.Text = action.DurationSeconds.ToString("0.#",
                CultureInfo.InvariantCulture);
            ConfigureAutomationActionEditor(action.Kind);
        }

        private void CommitSelectedAction()
        {
            if (_selectedAutomationAction == null || _automationUiLoading) return;
            _selectedAutomationAction.Kind = (AutomationActionKind)Math.Max(0,
                AutomationActionTypeCombo.SelectedIndex);
            _selectedAutomationAction.NotificationSystem =
                AutomationSystemNotificationCheck.IsChecked == true;
            _selectedAutomationAction.NotificationScreen =
                AutomationScreenNotificationCheck.IsChecked == true;
            _selectedAutomationAction.NotificationStrong =
                AutomationStrongNotificationCheck.IsChecked == true;
            _selectedAutomationAction.NotificationFlashSpeed =
                _automationFlashSpeedSelection;
            string value = (AutomationActionValueText.Text ?? string.Empty).Trim();
            switch (_selectedAutomationAction.Kind)
            {
                case AutomationActionKind.PlayVideo:
                case AutomationActionKind.ShowImage:
                    _selectedAutomationAction.FilePath =
                        AutomationMediaCombo.SelectedValue as string ??
                        _selectedAutomationAction.FilePath;
                    break;
                case AutomationActionKind.StartTheme:
                    _selectedAutomationAction.ThemePath = value;
                    break;
                default:
                    _selectedAutomationAction.Text = value;
                    break;
            }
            double seconds;
            if (double.TryParse(AutomationActionDurationText.Text,
                NumberStyles.Float, CultureInfo.CurrentCulture, out seconds) ||
                double.TryParse(AutomationActionDurationText.Text,
                    NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
                _selectedAutomationAction.DurationSeconds = Math.Max(0, seconds);
            _selectedAutomationAction.RefreshSummary();
            AutomationActionList.Items.Refresh();
        }

        private void AutomationActionAdd_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedAutomation == null) return;
            CommitSelectedAction();
            AutomationActionKind kind = (AutomationActionKind)Math.Max(0,
                AutomationNewActionTypeCombo.SelectedIndex);
            AutomationAction action = new AutomationAction { Kind = kind };
            if (kind == AutomationActionKind.Wait) action.DurationSeconds = 5;
            if (kind == AutomationActionKind.PlayVideo) action.DurationSeconds = 2;
            if (kind == AutomationActionKind.ShowImage) action.DurationSeconds = 2;
            _selectedAutomation.Actions.Add(action);
            AutomationActionList.SelectedItem = action;
            AutomationEditorStatusText.Text = "已添加动作，可继续添加并调整顺序。";
        }

        private void AutomationActionDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedAutomation == null || _selectedAutomationAction == null) return;
            int index = _selectedAutomation.Actions.IndexOf(_selectedAutomationAction);
            _selectedAutomation.Actions.Remove(_selectedAutomationAction);
            AutomationActionList.SelectedIndex = _selectedAutomation.Actions.Count == 0 ? -1 :
                Math.Max(0, Math.Min(index, _selectedAutomation.Actions.Count - 1));
        }

        private void AutomationActionUp_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedAutomationAction(-1);
        }

        private void AutomationActionDown_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedAutomationAction(1);
        }

        private void MoveSelectedAutomationAction(int offset)
        {
            if (_selectedAutomation == null || _selectedAutomationAction == null) return;
            CommitSelectedAction();
            int oldIndex = _selectedAutomation.Actions.IndexOf(_selectedAutomationAction);
            int newIndex = oldIndex + offset;
            if (newIndex < 0 || newIndex >= _selectedAutomation.Actions.Count) return;
            _selectedAutomation.Actions.Move(oldIndex, newIndex);
            AutomationActionList.SelectedItem = _selectedAutomationAction;
        }

        private void AutomationActionApply_Click(object sender, RoutedEventArgs e)
        {
            CommitSelectedAction();
            AutomationEditorStatusText.Text = _selectedAutomationAction != null &&
                _selectedAutomationAction.Kind == AutomationActionKind.Notification &&
                !_selectedAutomationAction.NotificationSystem &&
                !_selectedAutomationAction.NotificationScreen ?
                "请至少选择系统提醒或屏幕提醒。" : "动作设置已应用。";
        }

        private void AutomationActionBrowse_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedAutomationAction == null) return;
            OpenFileDialog dialog = new OpenFileDialog();
            switch (_selectedAutomationAction.Kind)
            {
                case AutomationActionKind.PlayVideo:
                    dialog.Filter = "视频|*.mp4;*.mov;*.avi;*.mkv;*.wmv";
                    break;
                case AutomationActionKind.ShowImage:
                    dialog.Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp";
                    break;
                case AutomationActionKind.StartTheme:
                    dialog.Filter = "TURZX 监控主题|*.turzxtheme.xml";
                    dialog.InitialDirectory = ThemeProjectStore.PresetDirectory;
                    break;
                default:
                    return;
            }
            if (dialog.ShowDialog(this) == true)
                AutomationActionValueText.Text = dialog.FileName;
        }

        private void SaveAutomations(bool showStatus)
        {
            try
            {
                AutomationStore.Save(_automationRules);
                if (showStatus) AutomationEditorStatusText.Text = "自动化已保存。";
            }
            catch (Exception exception)
            {
                if (AutomationEditorStatusText != null)
                    AutomationEditorStatusText.Text = "保存失败：" + exception.Message;
            }
        }
    }
}
