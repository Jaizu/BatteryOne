using System;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BatteryOne
{
    /// <summary>
    /// Lets other programs on this PC (such as the Stream Deck plugin) read the battery level.
    /// Connect to the named pipe \\.\pipe\BatteryOne, send one line, and read one line of JSON back:
    ///   "status"  - the last reading
    ///   "refresh" - reads the wheel now, then answers
    /// Also writes the path of BatteryOne.exe to %APPDATA%\BatteryOne\app-path.txt so clients can start it.
    /// </summary>
    public static class PipeServer
    {
        public const string PipeName = "BatteryOne";

        public static void Start(App app)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BatteryOne");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "app-path.txt"), System.Reflection.Assembly.GetExecutingAssembly().Location);
            }
            catch { }

            new Thread(() => Listen(app)) { IsBackground = true, Name = "PipeServer" }.Start();
        }

        static void Listen(App app)
        {
            while (true)
            {
                NamedPipeServerStream pipe;
                try
                {
                    pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    pipe.WaitForConnection();
                }
                catch
                {
                    // e.g. the previous instance has not released the pipe yet
                    Thread.Sleep(1000);
                    continue;
                }
                Task.Run(() => Handle(app, pipe));
            }
        }

        static void Handle(App app, NamedPipeServerStream pipe)
        {
            using (pipe)
            {
                try
                {
                    var reader = new StreamReader(pipe, Encoding.UTF8);
                    string command = (reader.ReadLine() ?? "").Trim().ToLowerInvariant();

                    if (command == "refresh")
                    {
                        var task = (Task)app.Dispatcher.Invoke(new Func<Task>(app.RefreshAsync));
                        task.Wait(5000);
                    }

                    var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
                    writer.WriteLine(ToJson(app.Battery, app.IsRefreshing, app.Settings.LowThreshold));
                    pipe.WaitForPipeDrain();
                }
                catch { }
            }
        }

        static string ToJson(WheelBattery b, bool refreshing, int lowThreshold)
        {
            string state = b == null ? "checking"
                : b.State == WheelState.Connected ? "connected"
                : b.State == WheelState.WheelOff ? "wheelOff"
                : b.State == WheelState.DongleNotFound ? "dongleNotFound"
                : "error";
            var sb = new StringBuilder("{");
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"state\":\"{0}\"", state);
            sb.AppendFormat(CultureInfo.InvariantCulture, ",\"refreshing\":{0}", refreshing ? "true" : "false");
            sb.AppendFormat(CultureInfo.InvariantCulture, ",\"lowThreshold\":{0}", lowThreshold);
            if (b != null)
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, ",\"percent\":{0}", b.Percent);
                sb.AppendFormat(",\"status\":{0}", Quote(b.Status));
                sb.AppendFormat(",\"health\":{0}", Quote(b.Health));
                sb.AppendFormat(",\"detail\":{0}", Quote(b.DetailText));
                sb.AppendFormat(",\"updated\":\"{0:yyyy-MM-ddTHH:mm:ss}\"", b.Time);
            }
            return sb.Append('}').ToString();
        }

        static string Quote(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
