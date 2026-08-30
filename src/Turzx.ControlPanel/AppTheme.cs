using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Turzx.ControlPanel
{
    internal enum ThemeMode
    {
        System,
        Light,
        Dark
    }

    internal enum StatusKind
    {
        Neutral,
        Connected,
        Working,
        Error
    }

    internal sealed class AppTheme
    {
        public bool IsDark { get; private set; }
        public Color Window { get; private set; }
        public Color Surface { get; private set; }
        public Color SurfaceAlt { get; private set; }
        public Color Text { get; private set; }
        public Color Muted { get; private set; }
        public Color Border { get; private set; }
        public Color PrimaryButton { get; private set; }
        public Color PrimaryButtonText { get; private set; }
        public Color Hover { get; private set; }
        public Color Disabled { get; private set; }
        public Color Success { get; private set; }
        public Color Warning { get; private set; }
        public Color Error { get; private set; }

        public static AppTheme Light()
        {
            return new AppTheme
            {
                IsDark = false,
                Window = Color.FromArgb(242, 242, 247),
                Surface = Color.White,
                SurfaceAlt = Color.FromArgb(247, 247, 249),
                Text = Color.FromArgb(28, 28, 30),
                Muted = Color.FromArgb(99, 99, 102),
                Border = Color.FromArgb(216, 216, 220),
                PrimaryButton = Color.FromArgb(0, 122, 255),
                PrimaryButtonText = Color.White,
                Hover = Color.FromArgb(236, 236, 234),
                Disabled = Color.FromArgb(168, 168, 163),
                Success = Color.FromArgb(70, 102, 84),
                Warning = Color.FromArgb(151, 104, 32),
                Error = Color.FromArgb(166, 63, 63)
            };
        }

        public static AppTheme Dark()
        {
            return new AppTheme
            {
                IsDark = true,
                Window = Color.FromArgb(0, 0, 0),
                Surface = Color.FromArgb(28, 28, 30),
                SurfaceAlt = Color.FromArgb(44, 44, 46),
                Text = Color.FromArgb(245, 245, 245),
                Muted = Color.FromArgb(163, 163, 163),
                Border = Color.FromArgb(58, 58, 60),
                PrimaryButton = Color.FromArgb(10, 132, 255),
                PrimaryButtonText = Color.White,
                Hover = Color.FromArgb(36, 36, 36),
                Disabled = Color.FromArgb(98, 98, 98),
                Success = Color.FromArgb(136, 177, 151),
                Warning = Color.FromArgb(207, 158, 86),
                Error = Color.FromArgb(211, 112, 112)
            };
        }
    }

    internal static class ThemeRole
    {
        public const string Window = "window";
        public const string Surface = "surface";
        public const string SurfaceAlt = "surface-alt";
        public const string Text = "text";
        public const string Muted = "muted";
        public const string PrimaryButton = "primary-button";
        public const string SecondaryButton = "secondary-button";
        public const string Placeholder = "placeholder";
        public const string Input = "input";
        public const string ThemeOption = "theme-option";
    }

    internal static class ThemeManager
    {
        private const string PersonalizeKey =
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        public static AppTheme Resolve(ThemeMode mode)
        {
            if (mode == ThemeMode.Light)
            {
                return AppTheme.Light();
            }
            if (mode == ThemeMode.Dark)
            {
                return AppTheme.Dark();
            }
            return IsSystemDark() ? AppTheme.Dark() : AppTheme.Light();
        }

        public static bool IsSystemDark()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, false))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    if (value is int)
                    {
                        return (int)value == 0;
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        public static void Apply(Control root, AppTheme theme)
        {
            if (root == null || theme == null)
            {
                return;
            }

            ApplyOne(root, theme);
            foreach (Control child in root.Controls)
            {
                Apply(child, theme);
            }
            root.Invalidate(true);
            Form form = root as Form;
            if (form != null && form.IsHandleCreated)
            {
                ApplyWindowChrome(form, theme.IsDark);
            }
        }

        private static void ApplyWindowChrome(Form form, bool dark)
        {
            try
            {
                int enabled = dark ? 1 : 0;
                int result = NativeMethods.DwmSetWindowAttribute(form.Handle, 20,
                    ref enabled, Marshal.SizeOf(typeof(int)));
                if (result != 0)
                {
                    NativeMethods.DwmSetWindowAttribute(form.Handle, 19,
                        ref enabled, Marshal.SizeOf(typeof(int)));
                }
            }
            catch
            {
            }
        }

        private static void ApplyOne(Control control, AppTheme theme)
        {
            string role = control.Tag as string;
            if (control is Form || role == ThemeRole.Window)
            {
                control.BackColor = theme.Window;
                control.ForeColor = theme.Text;
            }
            else if (role == ThemeRole.Surface)
            {
                control.BackColor = theme.Surface;
                control.ForeColor = theme.Text;
            }
            else if (role == ThemeRole.SurfaceAlt)
            {
                control.BackColor = theme.SurfaceAlt;
                control.ForeColor = theme.Text;
            }
            else if (role == ThemeRole.Muted)
            {
                control.BackColor = Color.Transparent;
                control.ForeColor = theme.Muted;
            }
            else if (role == ThemeRole.Text)
            {
                control.BackColor = Color.Transparent;
                control.ForeColor = theme.Text;
            }
            else if (role == ThemeRole.PrimaryButton)
            {
                StyleButton(control, theme.PrimaryButton,
                    theme.PrimaryButtonText, theme.PrimaryButton);
            }
            else if (role == ThemeRole.SecondaryButton)
            {
                StyleButton(control, theme.Surface, theme.Text, theme.Border);
            }
            else if (role == ThemeRole.Placeholder)
            {
                StyleButton(control, theme.SurfaceAlt, theme.Disabled, theme.Border);
            }
            else if (role == ThemeRole.ThemeOption)
            {
                RadioButton option = control as RadioButton;
                bool selected = option != null && option.Checked;
                StyleButton(option,
                    selected ? theme.PrimaryButton : theme.Surface,
                    selected ? theme.PrimaryButtonText : theme.Text,
                    selected ? theme.PrimaryButton : theme.Border);
            }
            else if (role == ThemeRole.Input || control is TextBoxBase ||
                control is NumericUpDown || control is TrackBar)
            {
                control.BackColor = theme.SurfaceAlt;
                control.ForeColor = theme.Text;
            }
            else if (control is Label)
            {
                control.BackColor = Color.Transparent;
                control.ForeColor = theme.Text;
            }
            else if (control is Panel || control is TableLayoutPanel ||
                control is FlowLayoutPanel)
            {
                control.BackColor = theme.Window;
                control.ForeColor = theme.Text;
            }
        }

        private static void StyleButton(Control control, Color background,
            Color foreground, Color border)
        {
            if (control == null)
            {
                return;
            }
            control.BackColor = background;
            control.ForeColor = control.Enabled ? foreground : Color.Gray;
            RoundedButton rounded = control as RoundedButton;
            if (rounded != null)
            {
                rounded.BorderColor = border == background ? Color.Transparent : border;
                rounded.Invalidate();
                return;
            }
            ButtonBase button = control as ButtonBase;
            if (button == null)
            {
                return;
            }
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = border;
            button.FlatAppearance.MouseOverBackColor = background;
            button.FlatAppearance.MouseDownBackColor = background;
        }

        private static class NativeMethods
        {
            [DllImport("dwmapi.dll")]
            internal static extern int DwmSetWindowAttribute(IntPtr window,
                int attribute, ref int value, int valueSize);
        }
    }
}
