using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace BatteryOne
{
    public partial class MainWindow : Window
    {
        readonly App _app;
        bool _loading = true;
        bool _streamDeckBusy;
        string _streamDeckNote;

        public MainWindow(App app)
        {
            _app = app;
            InitializeComponent();

            BackgroundToggle.IsChecked = app.Settings.KeepRunningInBackground;
            TrayToggle.IsChecked = app.Settings.ShowTrayIcon;
            StartupToggle.IsChecked = AutoStart.IsEnabled;
            (app.Settings.Theme == ThemeMode.Light ? ThemeLight : app.Settings.Theme == ThemeMode.Dark ? ThemeDark : ThemeSystem).IsChecked = true;
            AutoUpdateToggle.IsChecked = app.Settings.CheckForUpdates;
            UpdateBackgroundSubtitle();
            UpdateStreamDeckRow();
            ShowUpdates();
            _loading = false;

            // On small screens the window scrolls instead of growing past the screen
            MaxHeight = SystemParameters.WorkArea.Height;

            SourceInitialized += (s, e) => Theme.ApplyTitleBar(this);
            // The plugin may have been installed or removed from the Stream Deck app meanwhile
            Activated += (s, e) => UpdateStreamDeckRow();
            app.BatteryChanged += ShowBattery;
            app.UpdatesChanged += () => { ShowUpdates(); UpdateStreamDeckRow(); };
            ShowBattery();
        }

        bool _updating;

        void ShowUpdates()
        {
            VersionText.Text = $"BatteryOne {Versions.Display(AppInfo.AppVersion)}";
            if (_updating) return;
            UpdateButton.IsEnabled = !_app.IsCheckingForUpdates;

            if (_app.IsCheckingForUpdates)
            {
                UpdateStatus.Text = "Checking for updates…";
                UpdateButton.Content = "Check for updates";
            }
            else if (_app.IsAppUpdateAvailable)
            {
                UpdateStatus.Text = $"Version {Versions.Display(_app.LatestRelease.AppVersion)} is available.";
                UpdateButton.Content = "Update";
                UpdateButton.Style = (Style)FindResource("AccentButton");
                return;
            }
            else if (_app.UpdateError != null)
            {
                UpdateStatus.Text = _app.UpdateError;
                UpdateButton.Content = "Check for updates";
            }
            else if (_app.LastUpdateCheck != null)
            {
                UpdateStatus.Text = $"You have the latest version. Checked at {_app.LastUpdateCheck:t}.";
                UpdateButton.Content = "Check for updates";
            }
            else
            {
                UpdateStatus.Text = "Not checked yet.";
                UpdateButton.Content = "Check for updates";
            }
            UpdateButton.Style = (Style)FindResource("StandardButton");
        }

        async void Update_Click(object sender, RoutedEventArgs e)
        {
            if (!_app.IsAppUpdateAvailable)
            {
                await _app.CheckForUpdatesAsync();
                return;
            }

            var release = _app.LatestRelease;
            string notes = string.IsNullOrWhiteSpace(release.Notes) ? "" : release.Notes.Trim();
            // Release notes are Markdown; drop the symbols that would show up literally
            notes = System.Text.RegularExpressions.Regex.Replace(notes, @"\*\*|__|`", "");
            notes = System.Text.RegularExpressions.Regex.Replace(notes, @"^#+\s*", "", System.Text.RegularExpressions.RegexOptions.Multiline);
            if (notes.Length > 600) notes = notes.Substring(0, 600) + "…";
            if (!Dialog.Show(this, $"Update to BatteryOne {Versions.Display(release.AppVersion)}?",
                    (notes.Length > 0 ? notes + "\n\n" : "") + "BatteryOne will download the update and restart.", "Update"))
                return;

            _updating = true;
            UpdateButton.IsEnabled = false;
            UpdateStatus.Text = "Downloading the update…";
            try
            {
                await _app.InstallAppUpdateAsync();
            }
            catch (UnauthorizedAccessException)
            {
                _updating = false;
                UpdateStatus.Text = "BatteryOne cannot replace itself in this folder. Download the new version from GitHub instead.";
                UpdateButton.IsEnabled = true;
                Updater.OpenReleasesPage(release.PageUrl);
            }
            catch (Exception ex)
            {
                _updating = false;
                UpdateStatus.Text = "The update failed: " + ex.Message;
                UpdateButton.IsEnabled = true;
            }
        }

        void AutoUpdateToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _app.Settings.CheckForUpdates = AutoUpdateToggle.IsChecked == true;
            _app.Settings.Save();
        }

        void ShowBattery()
        {
            var b = _app.Battery;
            RefreshButton.IsEnabled = !_app.IsRefreshing;

            if (b == null)
            {
                SetColor("Neutral");
                StateText.Text = "Checking…";
                PercentText.Text = "–";
                SetBar(0);
                DetailText.Text = "Looking for the wheel";
                UpdatedText.Text = "";
                return;
            }

            StateText.Text = b.StateText;
            DetailText.Text = b.DetailText;
            UpdatedText.Text = _app.IsRefreshing ? "Refreshing…" : $"Updated {b.Time:T}";

            if (b.IsConnected)
            {
                PercentText.Text = $"{b.Percent}%";
                SetBar(b.Percent);
                SetColor(b.IsCharging ? "Charging"
                    : b.Percent <= _app.Settings.LowThreshold ? "Critical" : b.Percent <= 50 ? "Warning" : "Good");
            }
            else
            {
                PercentText.Text = "OFF";
                SetBar(0);
                SetColor("Neutral");
            }
        }

        void SetColor(string resourceKey)
        {
            StateDot.SetResourceReference(Shape.FillProperty, resourceKey);
            BarFill.SetResourceReference(Border.BackgroundProperty, resourceKey);
            if (resourceKey == "Neutral") PercentText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            else PercentText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        }

        void SetBar(int percent)
        {
            FillColumn.Width = new GridLength(percent, GridUnitType.Star);
            RestColumn.Width = new GridLength(100 - percent, GridUnitType.Star);
        }

        void UpdateBackgroundSubtitle() =>
            BackgroundSubtitle.Text = BackgroundToggle.IsChecked == true
                ? "Closing or minimizing hides this window. Open BatteryOne again to bring it back."
                : "Closing this window exits BatteryOne.";

        void UpdateStreamDeckRow()
        {
            if (_streamDeckBusy) return;
            if (!StreamDeckPlugin.IsStreamDeckInstalled)
            {
                StreamDeckStatus.Text = "The Stream Deck app is not installed on this PC.";
                StreamDeckButton.Visibility = Visibility.Collapsed;
                StreamDeckUpdateButton.Visibility = Visibility.Collapsed;
                return;
            }
            bool installed = StreamDeckPlugin.IsInstalled;
            bool update = installed && _app.IsPluginUpdateAvailable;
            string installedVersion = Versions.Display(StreamDeckPlugin.InstalledVersion);

            StreamDeckStatus.Text = _streamDeckNote ?? (
                update ? $"Version {Versions.Display(_app.AvailablePluginVersion)} is available (installed: {installedVersion})."
                : installed ? $"Version {installedVersion} installed. Add the Wheel battery action to a key in the Stream Deck app."
                : "Shows the battery level on a Stream Deck key.");
            StreamDeckButton.Content = installed ? "Uninstall" : "Install";
            StreamDeckButton.Visibility = Visibility.Visible;
            StreamDeckUpdateButton.Visibility = update ? Visibility.Visible : Visibility.Collapsed;
        }

        async void StreamDeckButton_Click(object sender, RoutedEventArgs e)
        {
            if (StreamDeckPlugin.IsInstalled)
            {
                if (!Dialog.Show(this, "Uninstall the Stream Deck plugin?",
                        "The Stream Deck app will close and reopen to remove the plugin.", "Uninstall"))
                    return;
                await RunStreamDeckTask("Uninstalling…", async () => { await StreamDeckPlugin.UninstallAsync(); return true; });
            }
            else
            {
                await RunStreamDeckTask("Click Install in the Stream Deck app to finish.", _app.InstallPluginAsync);
            }
        }

        async void StreamDeckUpdate_Click(object sender, RoutedEventArgs e) =>
            await RunStreamDeckTask("Click Install in the Stream Deck app to finish the update.", _app.InstallPluginAsync);

        async System.Threading.Tasks.Task RunStreamDeckTask(string status, Func<System.Threading.Tasks.Task<bool>> task)
        {
            _streamDeckNote = null;
            _streamDeckBusy = true;
            StreamDeckButton.IsEnabled = StreamDeckUpdateButton.IsEnabled = false;
            StreamDeckStatus.Text = status;
            try
            {
                if (!await task())
                    _streamDeckNote = "Not finished. Try again and confirm in the Stream Deck app.";
            }
            catch (Exception ex)
            {
                _streamDeckNote = "Something went wrong: " + ex.Message;
            }
            finally
            {
                _streamDeckBusy = false;
                StreamDeckButton.IsEnabled = StreamDeckUpdateButton.IsEnabled = true;
                UpdateStreamDeckRow();
            }
        }

        void Refresh_Click(object sender, RoutedEventArgs e) => _app.Refresh();

        void Exit_Click(object sender, RoutedEventArgs e) => _app.RequestExit(this);

        void BackgroundToggle_Changed(object sender, RoutedEventArgs e)
        {
            UpdateBackgroundSubtitle();
            if (!_loading) _app.SetKeepRunningInBackground(BackgroundToggle.IsChecked == true);
        }

        void TrayToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!_loading) _app.SetTrayIconVisible(TrayToggle.IsChecked == true);
        }

        void StartupToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            try { AutoStart.SetEnabled(StartupToggle.IsChecked == true); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not change the startup setting:\n" + ex.Message, "BatteryOne",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                _loading = true;
                StartupToggle.IsChecked = AutoStart.IsEnabled;
                _loading = false;
            }
        }

        void Theme_Checked(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _app.SetTheme(sender == ThemeLight ? ThemeMode.Light : sender == ThemeDark ? ThemeMode.Dark : ThemeMode.System);
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            // In background mode, minimizing hides the window from the taskbar too.
            // It stays minimized while hidden; ShowMainWindow restores it.
            if (WindowState == WindowState.Minimized && _app.Settings.KeepRunningInBackground && IsVisible && !_app.IsExiting)
                Hide();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_app.IsExiting)
            {
                e.Cancel = true;
                if (_app.Settings.KeepRunningInBackground) Hide();
                else _app.RequestExit(this);
            }
            base.OnClosing(e);
        }
    }
}
