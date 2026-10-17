using System;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace BatteryOne
{
    /// <summary>Notification-area icon that shows the battery percentage, or OFF.</summary>
    public sealed class TrayIcon : IDisposable
    {
        readonly Forms.NotifyIcon _icon = new Forms.NotifyIcon();
        Drawing.Icon _current;

        public TrayIcon(App app)
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Open BatteryOne", null, (s, e) => app.ShowMainWindow());
            menu.Items.Add("Refresh now", null, (s, e) => app.Refresh());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => app.RequestExit(null));
            _icon.ContextMenuStrip = menu;
            _icon.MouseClick += (s, e) => { if (e.Button == Forms.MouseButtons.Left) app.ShowMainWindow(); };
            Update(null, app.Settings.LowThreshold);
        }

        public bool Visible
        {
            get => _icon.Visible;
            set => _icon.Visible = value;
        }

        public void Update(WheelBattery b, int lowThreshold)
        {
            var old = _current;
            _current = Render(b, lowThreshold);
            _icon.Icon = _current;
            old?.Dispose();
            _icon.Text = b?.TrayText ?? "BatteryOne: checking…";
        }

        public void ShowLowBattery(WheelBattery b) =>
            _icon.ShowBalloonTip(5000, "Wheel battery low", $"{b.Percent}% left", Forms.ToolTipIcon.Warning);

        public void ShowMessage(string title, string text) =>
            _icon.ShowBalloonTip(5000, title, text, Forms.ToolTipIcon.Info);

        static Drawing.Icon Render(WheelBattery b, int lowThreshold)
        {
            using (var bmp = BatteryIcon.Render(32, b != null, b != null && b.IsConnected, b?.Percent ?? 0, lowThreshold))
            {
                IntPtr handle = bmp.GetHicon();
                var icon = (Drawing.Icon)Drawing.Icon.FromHandle(handle).Clone();
                DestroyIcon(handle);
                return icon;
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
            _current?.Dispose();
        }
    }
}
