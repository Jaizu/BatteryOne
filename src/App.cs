using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace BatteryOne
{
    public sealed class App : Application
    {
        const string InstanceMutexName = @"Local\BatteryOne.Instance";
        const string ShowSignalName = @"Local\BatteryOne.Show";

        public Settings Settings { get; } = Settings.Load();
        public WheelBattery Battery { get; private set; }
        public bool IsRefreshing { get; private set; }
        public bool IsExiting { get; private set; }
        public event Action BatteryChanged;

        static Mutex _instanceMutex;
        readonly bool _startInBackground;
        readonly EventWaitHandle _showSignal;
        MainWindow _window;
        TrayIcon _tray;
        DispatcherTimer _timer;
        bool _lowWarned;

        [STAThread]
        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ReportCrash(e.ExceptionObject as Exception);
            bool background = args.Contains(AutoStart.BackgroundArg, StringComparer.OrdinalIgnoreCase);

            // Started by the updater: wait for the old version to exit (it holds the single-instance lock)
            int updated = Array.FindIndex(args, a => a.Equals(Updater.UpdatedArg, StringComparison.OrdinalIgnoreCase));
            if (updated >= 0 && updated + 1 < args.Length && int.TryParse(args[updated + 1], out int oldProcessId))
                Updater.FinishUpdate(oldProcessId);

            // Only one BatteryOne runs at a time. Starting it again brings the running one to the front.
            _instanceMutex = new Mutex(true, InstanceMutexName, out bool isFirstInstance);
            var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
            if (!isFirstInstance)
            {
                if (!background)
                {
                    AllowOtherInstancesToComeToFront();
                    showSignal.Set();
                }
                return;
            }

            new App(background, showSignal).Run();
            GC.KeepAlive(_instanceMutex);
        }

        App(bool startInBackground, EventWaitHandle showSignal)
        {
            _startInBackground = startInBackground;
            _showSignal = showSignal;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        [DllImport("user32.dll")]
        static extern bool AllowSetForegroundWindow(int processId);

        /// <summary>Windows only lets the program the user just started take the foreground, so pass that right on.</summary>
        static void AllowOtherInstancesToComeToFront()
        {
            var current = Process.GetCurrentProcess();
            foreach (var p in Process.GetProcessesByName(current.ProcessName).Where(p => p.Id != current.Id))
                AllowSetForegroundWindow(p.Id);
        }

        /// <summary>Writes the error to %APPDATA%\BatteryOne\error.log and tells the user.</summary>
        static void ReportCrash(Exception ex)
        {
            string log = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BatteryOne", "error.log");
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(log));
                System.IO.File.AppendAllText(log, $"[{DateTime.Now:u}] {ex}\r\n\r\n");
            }
            catch { }
            MessageBox.Show($"BatteryOne stopped because of an error:\n\n{ex?.Message}\n\nDetails were saved to {log}",
                "BatteryOne", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/BatteryOne;component/src/Styles.xaml")
            });
            Theme.Init(Settings.Theme);
            try { AutoStart.RefreshPath(); } catch { }
            PipeServer.Start(this);

            _tray = new TrayIcon(this) { Visible = Settings.ShowTrayIcon };
            _window = new MainWindow(this);

            if (!_startInBackground) ShowMainWindow();
            else if (!Settings.KeepRunningInBackground)
            {
                // Not allowed to run hidden: start minimized on the taskbar
                _window.WindowState = WindowState.Minimized;
                _window.Show();
            }

            // Show the window when BatteryOne is started again
            new Thread(() =>
            {
                while (true)
                {
                    _showSignal.WaitOne();
                    Dispatcher.BeginInvoke(new Action(ShowMainWindow));
                }
            }) { IsBackground = true }.Start();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Settings.RefreshSeconds) };
            _timer.Tick += (s, a) => Refresh();
            _timer.Start();
            Refresh();

            // Automatic update check: shortly after startup, then once a day
            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _updateTimer.Tick += async (s, a) =>
            {
                _updateTimer.Interval = TimeSpan.FromDays(1);
                if (Settings.CheckForUpdates) await CheckForUpdatesAsync();
            };
            _updateTimer.Start();
        }

        // ---- Updates ----

        DispatcherTimer _updateTimer;
        Version _notifiedVersion;

        public ReleaseInfo LatestRelease { get; private set; }
        public bool IsCheckingForUpdates { get; private set; }
        public string UpdateError { get; private set; }
        public DateTime? LastUpdateCheck { get; private set; }
        public event Action UpdatesChanged;

        public bool IsAppUpdateAvailable =>
            LatestRelease?.Exe != null && LatestRelease.AppVersion != null && LatestRelease.AppVersion > AppInfo.AppVersion;

        /// <summary>Newest plugin version this app can install: the embedded one or the one in the latest release.</summary>
        public Version AvailablePluginVersion =>
            RemotePluginIsNewer ? LatestRelease.PluginVersion : AppInfo.EmbeddedPluginVersion;

        bool RemotePluginIsNewer =>
            LatestRelease?.Plugin != null && LatestRelease.PluginVersion != null &&
            (AppInfo.EmbeddedPluginVersion == null || LatestRelease.PluginVersion > AppInfo.EmbeddedPluginVersion);

        public bool IsPluginUpdateAvailable
        {
            get
            {
                var installed = StreamDeckPlugin.InstalledVersion;
                return StreamDeckPlugin.IsInstalled && installed != null && AvailablePluginVersion != null && AvailablePluginVersion > installed;
            }
        }

        public async Task CheckForUpdatesAsync()
        {
            if (IsCheckingForUpdates) return;
            IsCheckingForUpdates = true;
            UpdateError = null;
            UpdatesChanged?.Invoke();
            try
            {
                LatestRelease = await Updater.GetLatestAsync();
                LastUpdateCheck = DateTime.Now;
                if (LatestRelease == null) UpdateError = "No version has been published on GitHub yet.";
            }
            catch (Exception e)
            {
                UpdateError = e is System.Net.Http.HttpRequestException ? "Could not reach GitHub." : e.Message;
            }
            finally
            {
                IsCheckingForUpdates = false;
            }
            UpdatesChanged?.Invoke();

            // Tell the user once per new version, if they are not looking at the window
            if (IsAppUpdateAvailable && _tray.Visible && !_window.IsVisible && _notifiedVersion != LatestRelease.AppVersion)
            {
                _notifiedVersion = LatestRelease.AppVersion;
                _tray.ShowMessage("BatteryOne update available",
                    $"Version {Versions.Display(LatestRelease.AppVersion)} is available. Open BatteryOne to update.");
            }
        }

        /// <summary>Downloads and installs the new BatteryOne, then restarts. Throws on failure.</summary>
        public async Task InstallAppUpdateAsync()
        {
            await Updater.InstallAppUpdateAsync(LatestRelease);
            Quit();
        }

        /// <summary>Installs or updates the Stream Deck plugin with the newest version available.</summary>
        public async Task<bool> InstallPluginAsync()
        {
            if (!RemotePluginIsNewer) return await StreamDeckPlugin.InstallAsync();

            string package = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BatteryOne", "BatteryOne.streamDeckPlugin");
            await Updater.DownloadAsync(LatestRelease.Plugin, package);
            return await StreamDeckPlugin.InstallAsync(package, LatestRelease.PluginVersion);
        }

        Task _refreshTask = Task.CompletedTask;

        public void Refresh() => RefreshAsync();

        /// <summary>Reads the wheel now. If a read is already running, returns that one.</summary>
        public Task RefreshAsync()
        {
            if (!IsRefreshing) _refreshTask = RefreshCore();
            return _refreshTask;
        }

        async Task RefreshCore()
        {
            IsRefreshing = true;
            BatteryChanged?.Invoke();
            try
            {
                Battery = await Task.Run(() => WheelBattery.Read());
            }
            finally { IsRefreshing = false; }

            _tray.Update(Battery, Settings.LowThreshold);
            if (Battery.IsConnected && Battery.Percent <= Settings.LowThreshold && !Battery.IsCharging)
            {
                if (!_lowWarned && _tray.Visible) _tray.ShowLowBattery(Battery);
                _lowWarned = true;
            }
            else _lowWarned = false;
            BatteryChanged?.Invoke();
        }

        public void ShowMainWindow()
        {
            if (IsExiting) return;
            _window.Show();
            if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
            _window.Activate();
            // Make sure it ends up above other windows even if Windows refused the activation
            _window.Topmost = true;
            _window.Topmost = false;
            _window.Focus();
        }

        public void SetTrayIconVisible(bool visible)
        {
            Settings.ShowTrayIcon = visible;
            Settings.Save();
            _tray.Visible = visible;
        }

        public void SetKeepRunningInBackground(bool enabled)
        {
            Settings.KeepRunningInBackground = enabled;
            Settings.Save();
        }

        public void SetTheme(ThemeMode mode)
        {
            Settings.Theme = mode;
            Settings.Save();
            Theme.Apply(mode);
        }

        /// <summary>
        /// Exits BatteryOne, after warning that the Stream Deck key stops updating (if the plugin is installed).
        /// </summary>
        public void RequestExit(Window owner)
        {
            if (IsExiting) return;
            if (Settings.ConfirmExit && StreamDeckPlugin.IsInstalled)
            {
                bool exit = Dialog.Show(owner,
                    "Exit BatteryOne?",
                    "The BatteryOne key on your Stream Deck stops updating while BatteryOne is closed.",
                    "Exit", danger: true, showDontAskAgain: true, out bool dontAskAgain);
                if (!exit) return;
                if (dontAskAgain)
                {
                    Settings.ConfirmExit = false;
                    Settings.Save();
                }
            }
            Quit();
        }

        public void Quit()
        {
            if (IsExiting) return;
            IsExiting = true;
            _timer?.Stop();
            _updateTimer?.Stop();
            _tray?.Dispose();
            Shutdown();
        }
    }
}
