using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;

namespace Turzx.Desktop.Models
{
    public enum AutomationTriggerKind
    {
        DailyTime,
        Interval,
        ProcessStarted
    }

    public enum AutomationActionKind
    {
        Notification,
        Wait,
        PlayVideo,
        ShowImage,
        StartTheme
    }

    public enum AutomationFlashSpeed
    {
        Slow,
        Fast,
        UltraFast
    }

    [Serializable]
    public sealed class AutomationRule : INotifyPropertyChanged
    {
        private string _name;
        private bool _enabled;
        private AutomationTriggerKind _triggerKind;

        public AutomationRule()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = "新自动化";
            DailyTime = "22:00";
            IntervalMinutes = 60;
            IntervalStartTime = "00:00";
            IntervalEndTime = "23:59";
            Actions = new ObservableCollection<AutomationAction>();
        }

        public string Id { get; set; }

        public string Name
        {
            get { return _name; }
            set
            {
                if (_name == value) return;
                _name = value;
                Raise("Name");
            }
        }

        public bool Enabled
        {
            get { return _enabled; }
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                Raise("Enabled");
            }
        }

        public AutomationTriggerKind TriggerKind
        {
            get { return _triggerKind; }
            set
            {
                if (_triggerKind == value) return;
                _triggerKind = value;
                Raise("TriggerKind");
                Raise("TriggerSummary");
            }
        }

        public string DailyTime { get; set; }
        public int IntervalMinutes { get; set; }
        public string IntervalStartTime { get; set; }
        public string IntervalEndTime { get; set; }
        public string ProcessPath { get; set; }
        public DateTime LastTriggeredAt { get; set; }
        public ObservableCollection<AutomationAction> Actions { get; set; }

        public string TriggerSummary
        {
            get
            {
                switch (TriggerKind)
                {
                    case AutomationTriggerKind.Interval:
                        return (string.IsNullOrWhiteSpace(IntervalStartTime) ?
                            "00:00" : IntervalStartTime) + "–" +
                            (string.IsNullOrWhiteSpace(IntervalEndTime) ?
                            "23:59" : IntervalEndTime) + " · 每隔 " +
                            Math.Max(1, IntervalMinutes) + " 分钟";
                    case AutomationTriggerKind.ProcessStarted:
                        return string.IsNullOrWhiteSpace(ProcessPath) ? "程序启动时" :
                            Path.GetFileName(ProcessPath) + " 启动时";
                    default:
                        return "每天 " + (string.IsNullOrWhiteSpace(DailyTime) ?
                            "22:00" : DailyTime);
                }
            }
        }

        public void RefreshSummary()
        {
            Raise("TriggerSummary");
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this,
                new PropertyChangedEventArgs(propertyName));
        }
    }

    [Serializable]
    public sealed class AutomationAction : INotifyPropertyChanged
    {
        private AutomationActionKind _kind;

        public AutomationAction()
        {
            Id = Guid.NewGuid().ToString("N");
            Kind = AutomationActionKind.Notification;
            Text = "该休息一下了";
            DurationSeconds = 2;
            NotificationSystem = true;
            NotificationFlashSpeed = AutomationFlashSpeed.Slow;
        }

        public string Id { get; set; }

        public AutomationActionKind Kind
        {
            get { return _kind; }
            set
            {
                if (_kind == value) return;
                _kind = value;
                Raise("Kind");
                Raise("DisplaySummary");
            }
        }

        public string Text { get; set; }
        public string FilePath { get; set; }
        public string ThemePath { get; set; }
        public double DurationSeconds { get; set; }
        public bool NotificationSystem { get; set; }
        public bool NotificationScreen { get; set; }
        public bool NotificationStrong { get; set; }
        public AutomationFlashSpeed NotificationFlashSpeed { get; set; }

        public string DisplaySummary
        {
            get
            {
                switch (Kind)
                {
                    case AutomationActionKind.Wait:
                        return "等待 " + Math.Max(0, DurationSeconds).ToString("0.#") + " 秒";
                    case AutomationActionKind.PlayVideo:
                        return "播放视频 " + FileNameOrEmpty(FilePath) + " · " +
                            Math.Max(0.1, DurationSeconds).ToString("0.#") + " 秒";
                    case AutomationActionKind.ShowImage:
                        return "显示图片 " + FileNameOrEmpty(FilePath) + " · " +
                            Math.Max(0.1, DurationSeconds).ToString("0.#") + " 秒";
                    case AutomationActionKind.StartTheme:
                        return "启动主题 " + (string.IsNullOrWhiteSpace(ThemePath) ?
                            "当前主题" : FileNameOrEmpty(ThemePath));
                    default:
                        string channels = NotificationSystem && NotificationScreen ?
                            "系统+屏幕" : NotificationScreen ? "屏幕" :
                            NotificationSystem ? "系统" : "未选择方式";
                        return channels + "提醒 · " + (string.IsNullOrWhiteSpace(Text) ?
                            "未填写内容" : Text) + (NotificationScreen ? " · " +
                            Math.Max(0.5, DurationSeconds).ToString("0.#") + " 秒 · " +
                            GetFlashSpeedName(NotificationFlashSpeed) : string.Empty);
                }
            }
        }

        public void RefreshSummary()
        {
            Raise("DisplaySummary");
        }

        private static string FileNameOrEmpty(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? "未选择" : Path.GetFileName(path);
        }

        private static string GetFlashSpeedName(AutomationFlashSpeed speed)
        {
            switch (speed)
            {
                case AutomationFlashSpeed.Fast: return "快";
                case AutomationFlashSpeed.UltraFast: return "超快";
                default: return "慢";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this,
                new PropertyChangedEventArgs(propertyName));
        }
    }
}
