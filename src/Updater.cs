using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace BatteryOne
{
    public static class AppInfo
    {
        public static Version AppVersion { get; } = Versions.Normalize(Assembly.GetExecutingAssembly().GetName().Version);

        /// <summary>Version of the Stream Deck plugin embedded in this exe.</summary>
        public static Version EmbeddedPluginVersion { get; } = Versions.Parse(
            Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "PluginVersion")?.Value);
    }

    public static class Versions
    {
        /// <summary>Parses "1.2.3", "v1.2.3" or "1.2.3.0" into a three-part version; null if invalid.</summary>
        public static Version Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            return Version.TryParse(text.Trim().TrimStart('v', 'V'), out Version v) ? Normalize(v) : null;
        }

        public static Version Normalize(Version v) => v == null ? null : new Version(v.Major, v.Minor, Math.Max(0, v.Build));

        public static string Display(Version v) => v == null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    public sealed class ReleaseAsset
    {
        public string Name;
        public string Url;
        /// <summary>SHA-256 that GitHub reports for the file, when available.</summary>
        public string Sha256;
    }

    public sealed class ReleaseInfo
    {
        public Version AppVersion;
        public Version PluginVersion;
        public string PageUrl;
        public string Notes;
        public ReleaseAsset Exe;
        public ReleaseAsset Plugin;
    }

    /// <summary>
    /// Checks the latest GitHub release and installs app updates.
    /// Each release has the assets BatteryOne.exe, BatteryOne.streamDeckPlugin and versions.json
    /// ({"app":"1.0.0","plugin":"1.0.0"}); the app version is also the release tag (v1.0.0).
    /// </summary>
    public static class Updater
    {
        public const string RepositoryUrl = "https://github.com/Jaizu/BatteryOne";
        public const string ReleasesUrl = RepositoryUrl + "/releases";
        const string LatestReleaseApi = "https://api.github.com/repos/Jaizu/BatteryOne/releases/latest";

        /// <summary>Passed to the new exe after an update, with the old process id.</summary>
        public const string UpdatedArg = "--updated";

        static readonly HttpClient Http = CreateClient();

        static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"BatteryOne/{Versions.Display(AppInfo.AppVersion)}");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        /// <returns>The latest release, or null when none has been published.</returns>
        public static async Task<ReleaseInfo> GetLatestAsync()
        {
            var json = new JavaScriptSerializer();
            string body;
            using (var response = await Http.GetAsync(LatestReleaseApi))
            {
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();
                body = await response.Content.ReadAsStringAsync();
            }
            var release = (Dictionary<string, object>)json.DeserializeObject(body);

            var info = new ReleaseInfo
            {
                AppVersion = Versions.Parse(release.TryGetValue("tag_name", out object v) ? v as string : null),
                PageUrl = release.TryGetValue("html_url", out v) ? v as string : ReleasesUrl,
                Notes = release.TryGetValue("body", out v) ? v as string : null,
            };

            ReleaseAsset versionsFile = null;
            if (release.TryGetValue("assets", out v) && v is IEnumerable assets)
            {
                foreach (Dictionary<string, object> a in assets.OfType<Dictionary<string, object>>())
                {
                    var asset = new ReleaseAsset
                    {
                        Name = a.TryGetValue("name", out v) ? v as string : null,
                        Url = a.TryGetValue("browser_download_url", out v) ? v as string : null,
                        Sha256 = a.TryGetValue("digest", out v) && v is string d && d.StartsWith("sha256:") ? d.Substring(7) : null,
                    };
                    if (string.Equals(asset.Name, "BatteryOne.exe", StringComparison.OrdinalIgnoreCase)) info.Exe = asset;
                    else if (string.Equals(asset.Name, "BatteryOne.streamDeckPlugin", StringComparison.OrdinalIgnoreCase)) info.Plugin = asset;
                    else if (string.Equals(asset.Name, "versions.json", StringComparison.OrdinalIgnoreCase)) versionsFile = asset;
                }
            }

            if (versionsFile != null)
            {
                var versions = (Dictionary<string, object>)json.DeserializeObject(await Http.GetStringAsync(versionsFile.Url));
                if (versions.TryGetValue("app", out v)) info.AppVersion = Versions.Parse(v as string) ?? info.AppVersion;
                if (versions.TryGetValue("plugin", out v)) info.PluginVersion = Versions.Parse(v as string);
            }
            return info;
        }

        /// <summary>Downloads an asset and checks its SHA-256 when GitHub provides one.</summary>
        public static async Task DownloadAsync(ReleaseAsset asset, string path)
        {
            byte[] data = await Http.GetByteArrayAsync(asset.Url);
            if (asset.Sha256 != null)
            {
                using (var sha = SHA256.Create())
                {
                    string actual = BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "");
                    if (!actual.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"The download of {asset.Name} is corrupted (checksum mismatch).");
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, data);
        }

        /// <summary>
        /// Replaces BatteryOne.exe with the new version and starts it. A running exe cannot be overwritten
        /// but it can be renamed, so the current one becomes BatteryOne.exe.old (deleted by the new version).
        /// The caller must exit right after this returns.
        /// </summary>
        public static async Task InstallAppUpdateAsync(ReleaseInfo release)
        {
            string exe = Assembly.GetExecutingAssembly().Location;
            string newExe = exe + ".new";
            string oldExe = exe + ".old";

            await DownloadAsync(release.Exe, newExe);
            if (File.Exists(oldExe)) File.Delete(oldExe);
            File.Move(exe, oldExe);
            try
            {
                File.Move(newExe, exe);
            }
            catch
            {
                File.Move(oldExe, exe);
                throw;
            }

            Process.Start(new ProcessStartInfo(exe, $"{UpdatedArg} {Process.GetCurrentProcess().Id}")
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe),
            });
        }

        /// <summary>Called by the new version: waits for the old one to exit, then deletes it.</summary>
        public static void FinishUpdate(int oldProcessId)
        {
            try { Process.GetProcessById(oldProcessId).WaitForExit(15000); } catch { }
            string oldExe = Assembly.GetExecutingAssembly().Location + ".old";
            for (int i = 0; i < 10 && File.Exists(oldExe); i++)
            {
                try { File.Delete(oldExe); }
                catch { System.Threading.Thread.Sleep(500); }
            }
        }

        public static void OpenReleasesPage(string url = null)
        {
            try { Process.Start(new ProcessStartInfo(url ?? ReleasesUrl) { UseShellExecute = true }); } catch { }
        }
    }
}
