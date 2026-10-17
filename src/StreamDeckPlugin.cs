using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace BatteryOne
{
    /// <summary>
    /// Installs and removes the BatteryOne Stream Deck plugin. The plugin package is embedded in BatteryOne.exe
    /// at build time (see BatteryOne.csproj).
    /// </summary>
    public static class StreamDeckPlugin
    {
        const string FolderName = "com.batteryone.wheel.sdPlugin";
        const string ResourceName = "BatteryOne.streamDeckPlugin";
        const string PluginProcessName = "BatteryOne.StreamDeck";

        static string PluginsDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Elgato", "StreamDeck", "Plugins");

        static string PluginDir => Path.Combine(PluginsDir, FolderName);

        public static bool IsInstalled => File.Exists(Path.Combine(PluginDir, "manifest.json"));

        /// <summary>Version in the installed plugin's manifest, or null when not installed.</summary>
        public static Version InstalledVersion
        {
            get
            {
                try
                {
                    string manifest = Path.Combine(PluginDir, "manifest.json");
                    if (!File.Exists(manifest)) return null;
                    var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(manifest), "\"Version\"\\s*:\\s*\"([^\"]+)\"");
                    return m.Success ? Versions.Parse(m.Groups[1].Value) : null;
                }
                catch { return null; }
            }
        }

        public static bool IsStreamDeckInstalled => FindStreamDeckExe() != null;

        /// <summary>Path of StreamDeck.exe: the running one, the default install folder, or the uninstall entry.</summary>
        static string FindStreamDeckExe()
        {
            foreach (var p in Process.GetProcessesByName("StreamDeck"))
            {
                try { if (File.Exists(p.MainModule.FileName)) return p.MainModule.FileName; } catch { }
            }

            string standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Elgato", "StreamDeck", "StreamDeck.exe");
            if (File.Exists(standard)) return standard;

            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                try
                {
                    using (var uninstall = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if (uninstall == null) continue;
                        foreach (string name in uninstall.GetSubKeyNames())
                        {
                            using (var key = uninstall.OpenSubKey(name))
                            {
                                if (!(key?.GetValue("DisplayName") is string display) || display.IndexOf("Stream Deck", StringComparison.OrdinalIgnoreCase) < 0) continue;
                                if (key.GetValue("InstallLocation") is string dir)
                                {
                                    string exe = Path.Combine(dir, "StreamDeck.exe");
                                    if (File.Exists(exe)) return exe;
                                }
                            }
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// Opens the embedded .streamDeckPlugin file, which Stream Deck installs itself (starting Stream Deck if needed).
        /// If .streamDeckPlugin files are not associated with Stream Deck, copies the plugin and restarts Stream Deck.
        /// Returns true once the plugin is installed.
        /// </summary>
        /// <param name="package">A downloaded .streamDeckPlugin file, or null for the one embedded in this exe.</param>
        /// <param name="version">The version in that package; installation is done once that version is in place.</param>
        public static Task<bool> InstallAsync(string package = null, Version version = null) => Task.Run(() =>
        {
            package = package ?? ExtractPackage();
            version = version ?? AppInfo.EmbeddedPluginVersion;
            bool Done() => IsInstalled && (version == null || InstalledVersion >= version);
            try
            {
                Process.Start(new ProcessStartInfo(package) { UseShellExecute = true });
            }
            catch (Win32Exception)
            {
                CopyAndRestart(package);
            }

            // Stream Deck asks the user to confirm, so give them time
            var deadline = DateTime.UtcNow.AddMinutes(5);
            while (DateTime.UtcNow < deadline)
            {
                if (Done()) return true;
                Thread.Sleep(500);
            }
            return Done();
        });

        /// <summary>Stream Deck has no uninstall command, so: quit Stream Deck, delete the plugin folder, start it again.</summary>
        public static Task UninstallAsync() => Task.Run(() =>
        {
            bool wasRunning = StopStreamDeck(out string exe);
            for (int attempt = 0; attempt < 10 && Directory.Exists(PluginDir); attempt++)
            {
                try { Directory.Delete(PluginDir, true); }
                catch (IOException) { Thread.Sleep(500); }
                catch (UnauthorizedAccessException) { Thread.Sleep(500); }
            }
            if (wasRunning && exe != null) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
            if (Directory.Exists(PluginDir)) throw new IOException("Could not delete " + PluginDir);
        });

        static string ExtractPackage()
        {
            string dir = Path.Combine(Path.GetTempPath(), "BatteryOne");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "BatteryOne.streamDeckPlugin");
            using (var resource = typeof(StreamDeckPlugin).Assembly.GetManifestResourceStream(ResourceName))
            using (var output = File.Create(file))
            {
                if (resource == null) throw new InvalidOperationException("This build of BatteryOne does not include the Stream Deck plugin.");
                resource.CopyTo(output);
            }
            return file;
        }

        static void CopyAndRestart(string package)
        {
            bool wasRunning = StopStreamDeck(out string exe);
            if (Directory.Exists(PluginDir)) Directory.Delete(PluginDir, true);
            Directory.CreateDirectory(PluginsDir);
            ZipFile.ExtractToDirectory(package, PluginsDir);
            if (exe != null) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
        }

        /// <returns>True if Stream Deck was running.</returns>
        static bool StopStreamDeck(out string exe)
        {
            exe = FindStreamDeckExe();
            var running = Process.GetProcessesByName("StreamDeck").ToList();
            foreach (var p in running.Concat(Process.GetProcessesByName(PluginProcessName)))
            {
                try { p.Kill(); p.WaitForExit(5000); } catch { }
            }
            return running.Count > 0;
        }
    }
}
