using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace BatteryOne.StreamDeck
{
    public sealed class BatteryStatus
    {
        public string State;      // checking, connected, wheelOff, dongleNotFound, error
        public int Percent;
        public bool Charging;     // on the charger; the wheel does not send inputs then
        public int LowThreshold = 20;
        public string Status;
        public string Detail;

        public bool Known => State != "checking";
        public bool Connected => State == "connected";
    }

    /// <summary>Talks to BatteryOne through its named pipe (see src/PipeServer.cs in BatteryOne).</summary>
    public static class BatteryOneClient
    {
        const string PipeName = "BatteryOne";

        /// <summary>Returns null when BatteryOne is not running.</summary>
        public static BatteryStatus Query(string command, int timeoutMs)
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    pipe.Connect(500);
                    var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
                    writer.WriteLine(command);
                    var reader = new StreamReader(pipe, Encoding.UTF8);
                    var line = reader.ReadLineAsync();
                    if (!line.Wait(timeoutMs) || line.Result == null) return null;
                    return Parse(line.Result);
                }
            }
            catch
            {
                return null;
            }
        }

        static BatteryStatus Parse(string json)
        {
            var d = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
            return new BatteryStatus
            {
                State = d.TryGetValue("state", out object v) ? v as string : "error",
                Percent = d.TryGetValue("percent", out v) && v is int p ? p : 0,
                Charging = d.TryGetValue("charging", out v) && v is bool c && c,
                LowThreshold = d.TryGetValue("lowThreshold", out v) && v is int t ? t : 20,
                Status = d.TryGetValue("status", out v) ? v as string : null,
                Detail = d.TryGetValue("detail", out v) ? v as string : null,
            };
        }

        /// <summary>Starts BatteryOne in the background, using the path it saved the last time it ran.</summary>
        public static bool TryStart()
        {
            try
            {
                string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BatteryOne", "app-path.txt");
                if (!File.Exists(file)) return false;
                string exe = File.ReadAllText(file).Trim();
                if (!File.Exists(exe)) return false;
                Process.Start(new ProcessStartInfo(exe, "--background") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Waits until BatteryOne answers on its pipe.</summary>
        public static async Task<bool> WaitUntilRunning(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (await Task.Run(() => Query("status", 1000)) != null) return true;
                await Task.Delay(500);
            }
            return false;
        }
    }
}
