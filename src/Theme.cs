using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace BatteryOne
{
    /// <summary>Light/dark palette in the style of Windows 11, applied as application resources.</summary>
    public static class Theme
    {
        public static bool IsDark { get; private set; }

        public static event Action Changed;

        static ThemeMode _mode = ThemeMode.System;

        public static void Init(ThemeMode mode)
        {
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (_mode == ThemeMode.System && e.Category == UserPreferenceCategory.General)
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() => Apply(_mode)));
            };
            Apply(mode);
        }

        public static void Apply(ThemeMode mode)
        {
            _mode = mode;
            IsDark = mode == ThemeMode.Dark || (mode == ThemeMode.System && SystemUsesDarkTheme());
            var r = Application.Current.Resources;

            if (IsDark)
            {
                Set(r, "WindowBg", "#202020");
                Set(r, "CardBg", "#2B2B2B");
                Set(r, "CardBorder", "#1C1C1C");
                Set(r, "CardHover", "#323232");
                Set(r, "Divider", "#3A3A3A");
                Set(r, "TextPrimary", "#FFFFFF");
                Set(r, "TextSecondary", "#C5C5C5");
                Set(r, "Track", "#3D3D3D");
                Set(r, "ControlBg", "#2D2D2D");
                Set(r, "ControlBorder", "#9A9A9A");
                Set(r, "Accent", "#4CC2FF");
                Set(r, "OnAccent", "#000000");
                Set(r, "Good", "#6CCB5F");
                Set(r, "Warning", "#FCB040");
                Set(r, "Critical", "#FF99A4");
                Set(r, "Neutral", "#9A9A9A");
                Set(r, "ButtonBg", "#2D2D2D");
                Set(r, "ButtonHover", "#383838");
                Set(r, "ButtonBorder", "#3F3F3F");
                Set(r, "DangerBg", "#C42B1C");
                Set(r, "DangerHover", "#D13A2B");
                Set(r, "OnDanger", "#FFFFFF");
            }
            else
            {
                Set(r, "WindowBg", "#F3F3F3");
                Set(r, "CardBg", "#FFFFFF");
                Set(r, "CardBorder", "#E5E5E5");
                Set(r, "CardHover", "#F6F6F6");
                Set(r, "Divider", "#EAEAEA");
                Set(r, "TextPrimary", "#1B1B1B");
                Set(r, "TextSecondary", "#5F5F5F");
                Set(r, "Track", "#E0E0E0");
                Set(r, "ControlBg", "#F7F7F7");
                Set(r, "ControlBorder", "#8A8A8A");
                Set(r, "Accent", "#0067C0");
                Set(r, "OnAccent", "#FFFFFF");
                Set(r, "Good", "#0F7B0F");
                Set(r, "Warning", "#9D5D00");
                Set(r, "Critical", "#C42B1C");
                Set(r, "Neutral", "#8A8A8A");
                Set(r, "ButtonBg", "#FBFBFB");
                Set(r, "ButtonHover", "#F3F3F3");
                Set(r, "ButtonBorder", "#D9D9D9");
                Set(r, "DangerBg", "#C42B1C");
                Set(r, "DangerHover", "#B02719");
                Set(r, "OnDanger", "#FFFFFF");
            }

            foreach (Window w in Application.Current.Windows) ApplyTitleBar(w);
            Changed?.Invoke();
        }

        static void Set(ResourceDictionary r, string key, string color)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            r[key] = brush;
        }

        static bool SystemUsesDarkTheme()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch { return false; }
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>Makes the native title bar follow the theme (Windows 10 20H1+ and Windows 11).</summary>
        public static void ApplyTitleBar(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int dark = IsDark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int)); // older Windows 10 builds
        }
    }
}
