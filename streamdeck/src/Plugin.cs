using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Drawing = System.Drawing;

namespace BatteryOne.StreamDeck
{
    /// <summary>
    /// Stream Deck plugin: shows the BatteryOne tray icon on a key.
    /// The key mirrors BatteryOne every few seconds; pressing it makes BatteryOne read the wheel right away.
    /// Stream Deck starts this exe with: -port N -pluginUUID X -registerEvent Y -info JSON
    /// </summary>
    public sealed class Plugin
    {
        static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
        const int KeySize = 144; // Stream Deck keys are 72x72, 144x144 on high-DPI models

        readonly int _port;
        readonly string _uuid;
        readonly string _registerEvent;
        readonly ClientWebSocket _socket = new ClientWebSocket();
        readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        readonly HashSet<string> _contexts = new HashSet<string>();
        string _lastImage;
        int _busy; // 1 while a key press is being handled

        static void Main(string[] args)
        {
            var arguments = new Dictionary<string, string>();
            for (int i = 0; i + 1 < args.Length; i += 2) arguments[args[i].TrimStart('-')] = args[i + 1];
            if (!arguments.ContainsKey("port") || !arguments.ContainsKey("pluginUUID") || !arguments.ContainsKey("registerEvent"))
            {
                Console.Error.WriteLine("This program is started by the Stream Deck app.");
                return;
            }
            try
            {
                new Plugin(int.Parse(arguments["port"]), arguments["pluginUUID"], arguments["registerEvent"]).Run().Wait();
            }
            catch (Exception e)
            {
                Log(e.ToString());
            }
        }

        Plugin(int port, string uuid, string registerEvent)
        {
            _port = port;
            _uuid = uuid;
            _registerEvent = registerEvent;
        }

        async Task Run()
        {
            await _socket.ConnectAsync(new Uri($"ws://127.0.0.1:{_port}"), CancellationToken.None);
            await Send(new Dictionary<string, object> { ["event"] = _registerEvent, ["uuid"] = _uuid });

            var poll = PollLoop();
            await ReceiveLoop();
        }

        async Task ReceiveLoop()
        {
            var buffer = new byte[64 * 1024];
            var message = new MemoryStream();
            while (_socket.State == WebSocketState.Open)
            {
                var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) return;
                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;

                string text = Encoding.UTF8.GetString(message.ToArray());
                message.SetLength(0);
                try { await Handle((Dictionary<string, object>)_json.DeserializeObject(text)); }
                catch (Exception e) { Log(e.ToString()); }
            }
        }

        async Task Handle(Dictionary<string, object> msg)
        {
            string ev = msg.TryGetValue("event", out object v) ? v as string : null;
            string context = msg.TryGetValue("context", out v) ? v as string : null;

            switch (ev)
            {
                case "willAppear":
                    lock (_contexts) _contexts.Add(context);
                    if (_lastImage != null) await SetImage(context, _lastImage);
                    else _ = Update("status");
                    break;

                case "willDisappear":
                    lock (_contexts) _contexts.Remove(context);
                    break;

                case "keyDown":
                    _ = OnKeyPressed(context);
                    break;
            }
        }

        async Task OnKeyPressed(string context)
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                await ShowOnAllKeys(ToDataUrl(BatteryIcon.Render(KeySize, false, false, 0, 0)), remember: false);

                var status = await Task.Run(() => BatteryOneClient.Query("refresh", 8000));
                if (status == null && BatteryOneClient.TryStart() && await BatteryOneClient.WaitUntilRunning(TimeSpan.FromSeconds(15)))
                    status = await Task.Run(() => BatteryOneClient.Query("refresh", 8000));

                if (status == null) await Send(new Dictionary<string, object> { ["event"] = "showAlert", ["context"] = context });
                await ShowStatus(status, force: true);
            }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        async Task PollLoop()
        {
            while (_socket.State == WebSocketState.Open)
            {
                if (Volatile.Read(ref _busy) == 0)
                {
                    try { await Update("status"); }
                    catch (Exception e) { Log(e.ToString()); }
                }
                await Task.Delay(PollInterval);
            }
        }

        async Task Update(string command)
        {
            var status = await Task.Run(() => BatteryOneClient.Query(command, 8000));
            await ShowStatus(status, force: false);
        }

        Task ShowStatus(BatteryStatus status, bool force)
        {
            Drawing.Bitmap bmp = status == null
                ? RenderAppClosed()
                : BatteryIcon.Render(KeySize, status.Known, status.Connected, status.Percent, status.LowThreshold, status.Charging);
            string image = ToDataUrl(bmp);
            if (!force && image == _lastImage) return Task.CompletedTask;
            return ShowOnAllKeys(image, remember: true);
        }

        async Task ShowOnAllKeys(string image, bool remember)
        {
            if (remember) _lastImage = image;
            string[] contexts;
            lock (_contexts) contexts = _contexts.ToArray();
            foreach (var c in contexts) await SetImage(c, image);
        }

        Task SetImage(string context, string image) => Send(new Dictionary<string, object>
        {
            ["event"] = "setImage",
            ["context"] = context,
            ["payload"] = new Dictionary<string, object> { ["image"] = image, ["target"] = 0 },
        });

        /// <summary>Shown when BatteryOne is not running. Pressing the key starts it.</summary>
        static Drawing.Bitmap RenderAppClosed()
        {
            var bmp = new Drawing.Bitmap(KeySize, KeySize);
            using (var g = Drawing.Graphics.FromImage(bmp))
            {
                g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.Clear(Drawing.Color.FromArgb(0x2B, 0x2B, 0x2B));
                using (var font = new Drawing.Font("Segoe UI", 24, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel))
                using (var format = new Drawing.StringFormat { Alignment = Drawing.StringAlignment.Center, LineAlignment = Drawing.StringAlignment.Center })
                {
                    g.DrawString("BatteryOne\nclosed", font, Drawing.Brushes.White, new Drawing.RectangleF(0, 0, KeySize, KeySize), format);
                }
            }
            return bmp;
        }

        static string ToDataUrl(Drawing.Bitmap bmp)
        {
            using (bmp)
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, Drawing.Imaging.ImageFormat.Png);
                return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
            }
        }

        async Task Send(Dictionary<string, object> message)
        {
            var bytes = Encoding.UTF8.GetBytes(_json.Serialize(message));
            await _sendLock.WaitAsync();
            try
            {
                if (_socket.State == WebSocketState.Open)
                    await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            finally { _sendLock.Release(); }
        }

        static void Log(string text)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BatteryOne");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "streamdeck.log"), $"[{DateTime.Now:u}] {text}\r\n");
            }
            catch { }
        }
    }
}
