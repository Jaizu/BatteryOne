using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace BatteryOne
{
    public enum ThemeMode { System, Light, Dark }

    /// <summary>Settings stored as key=value lines in %APPDATA%\BatteryOne\settings.ini.</summary>
    public sealed class Settings
    {
        public bool ShowTrayIcon { get; set; } = true;
        /// <summary>Closing or minimizing the window hides it instead of exiting.</summary>
        public bool KeepRunningInBackground { get; set; } = true;
        /// <summary>Warn before exiting that the Stream Deck key stops updating.</summary>
        public bool ConfirmExit { get; set; } = true;
        /// <summary>Check GitHub for new versions at startup and once a day.</summary>
        public bool CheckForUpdates { get; set; } = true;
        public ThemeMode Theme { get; set; } = ThemeMode.System;
        public int RefreshSeconds { get; set; } = 60;
        public int LowThreshold { get; set; } = 20;

        static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BatteryOne", "settings.ini");

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
                if (values.TryGetValue("ShowTrayIcon", out string v) && bool.TryParse(v, out bool b)) s.ShowTrayIcon = b;
                if (values.TryGetValue("KeepRunningInBackground", out v) && bool.TryParse(v, out b)) s.KeepRunningInBackground = b;
                if (values.TryGetValue("ConfirmExit", out v) && bool.TryParse(v, out b)) s.ConfirmExit = b;
                if (values.TryGetValue("CheckForUpdates", out v) && bool.TryParse(v, out b)) s.CheckForUpdates = b;
                if (values.TryGetValue("Theme", out v) && Enum.TryParse(v, true, out ThemeMode t)) s.Theme = t;
                if (values.TryGetValue("RefreshSeconds", out v) && int.TryParse(v, out int n)) s.RefreshSeconds = Math.Max(5, Math.Min(3600, n));
                if (values.TryGetValue("LowThreshold", out v) && int.TryParse(v, out n)) s.LowThreshold = Math.Max(0, Math.Min(100, n));
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, new[]
                {
                    $"ShowTrayIcon={ShowTrayIcon}",
                    $"KeepRunningInBackground={KeepRunningInBackground}",
                    $"ConfirmExit={ConfirmExit}",
                    $"CheckForUpdates={CheckForUpdates}",
                    $"Theme={Theme}",
                    $"RefreshSeconds={RefreshSeconds}",
                    $"LowThreshold={LowThreshold}",
                });
            }
            catch { }
        }
    }

    /// <summary>"Start with Windows", through the current user's Run registry key.</summary>
    public static class AutoStart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "BatteryOne";
        public const string BackgroundArg = "--background";

        static string Command => $"\"{System.Reflection.Assembly.GetExecutingAssembly().Location}\" {BackgroundArg}";

        public static bool IsEnabled
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                    return key?.GetValue(ValueName) != null;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) key.SetValue(ValueName, Command);
                else if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName);
            }
        }

        /// <summary>Keeps the startup entry pointing at this exe if it was moved.</summary>
        public static void RefreshPath()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (key?.GetValue(ValueName) is string current && current != Command) key.SetValue(ValueName, Command);
            }
        }
    }
}
