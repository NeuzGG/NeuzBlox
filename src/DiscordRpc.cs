using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace NeuzBlox
{
    /// <summary>
    /// Discord Rich Presence over Discord's local IPC pipe. No third-party library:
    /// each message is a little-endian opcode, a length, and a JSON body, written to
    /// \\.\pipe\discord-ipc-N. Everything stays on this machine - Discord itself is
    /// what publishes the status.
    ///
    /// Two rules keep this from freezing the app:
    ///   * the pipe is opened ASYNCHRONOUS. On a synchronous handle Windows serialises
    ///     I/O, so the reader thread parked in Read() would block every Write behind it.
    ///   * no caller ever touches the pipe. Sends are queued and a writer thread drains
    ///     them, so the UI thread can never end up waiting on Discord.
    /// </summary>
    public sealed class DiscordRpc : IDisposable
    {
        const int OpHandshake = 0;
        const int OpFrame = 1;
        const int OpClose = 2;
        const int OpPing = 3;
        const int OpPong = 4;

        class Frame
        {
            public int Op;
            public string Json;
        }

        NamedPipeClientStream _pipe;
        Thread _reader;
        Thread _writer;
        readonly Queue<Frame> _outbox = new Queue<Frame>();
        readonly AutoResetEvent _outSignal = new AutoResetEvent(false);
        readonly DateTime _startedAt = DateTime.UtcNow;
        volatile bool _connected;
        volatile bool _stopping;
        string _appId;
        volatile string _lastError = "";
        int _nonce;

        public bool Connected { get { return _connected; } }
        public string LastError { get { return _lastError; } }

        /// <summary>Blocks while it negotiates - call it from a background thread.</summary>
        public bool Connect(string appId)
        {
            Disconnect();
            _stopping = false;

            if (string.IsNullOrEmpty(appId))
            {
                _lastError = "No Discord application ID set.";
                return false;
            }
            _appId = appId.Trim();

            for (int i = 0; i < 10; i++)
            {
                NamedPipeClientStream pipe = null;
                try
                {
                    pipe = new NamedPipeClientStream(".", "discord-ipc-" + i.ToString(CultureInfo.InvariantCulture),
                                                     PipeDirection.InOut, PipeOptions.Asynchronous);
                    pipe.Connect(300);
                    _pipe = pipe;

                    // Nothing else is touching the pipe yet, so this write is safe here.
                    if (!WriteFrame(OpHandshake, "{\"v\":1,\"client_id\":" + Json.Quote(_appId) + "}"))
                    {
                        Drop(pipe);
                        continue;
                    }

                    int op;
                    string body;
                    if (!Read(out op, out body))
                    {
                        _lastError = "Discord did not answer the handshake.";
                        Drop(pipe);
                        continue;
                    }
                    if (body != null && body.IndexOf("\"evt\":\"ERROR\"", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _lastError = "Discord rejected that application ID.";
                        Drop(pipe);
                        return false;
                    }

                    _connected = true;
                    _lastError = "";

                    _reader = new Thread(ReadLoop);
                    _reader.IsBackground = true;
                    _reader.Name = "NeuzBlox.Discord.Read";
                    _reader.Start();

                    _writer = new Thread(WriteLoop);
                    _writer.IsBackground = true;
                    _writer.Name = "NeuzBlox.Discord.Write";
                    _writer.Start();

                    Log.Write("Discord rich presence connected on pipe " + i);
                    return true;
                }
                catch (TimeoutException)
                {
                    Drop(pipe);
                }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                    Drop(pipe);
                }
            }

            if (string.IsNullOrEmpty(_lastError)) _lastError = "Discord is not running.";
            return false;
        }

        void Drop(NamedPipeClientStream pipe)
        {
            if (_pipe == pipe) _pipe = null;
            try { if (pipe != null) pipe.Dispose(); }
            catch { }
        }

        /// <summary>Returns immediately. The pipe is torn down on its own thread.</summary>
        public void Disconnect()
        {
            _stopping = true;
            _connected = false;
            try { _outSignal.Set(); }
            catch { }

            NamedPipeClientStream p = _pipe;
            _pipe = null;
            _reader = null;
            _writer = null;
            lock (_outbox) _outbox.Clear();

            if (p == null) return;
            var th = new Thread(delegate()
            {
                try { p.Dispose(); }
                catch { }
            });
            th.IsBackground = true;
            th.Start();
        }

        /// <summary>
        /// Safe from any thread, including the UI thread: it only queues.
        /// largeImage/smallImage are either an asset key uploaded to the Discord
        /// application, or a raw https URL - Discord proxies those itself.
        /// </summary>
        public bool SetActivity(string details, string state, int partySize, int partyMax, bool showElapsed,
                                string largeImage, string largeText, string smallImage, string smallText)
        {
            if (!_connected) return false;

            var sb = new StringBuilder();
            sb.Append("{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"pid\":");
            sb.Append(Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"activity\":{");
            sb.Append("\"type\":0");
            if (!string.IsNullOrEmpty(details)) sb.Append(",\"details\":").Append(Json.Quote(Clip(details, 128)));
            if (!string.IsNullOrEmpty(state)) sb.Append(",\"state\":").Append(Json.Quote(Clip(state, 128)));

            if (partySize > 0 && partyMax > 0)
            {
                sb.Append(",\"party\":{\"id\":\"neuzblox-clients\",\"size\":[");
                sb.Append(partySize.ToString(CultureInfo.InvariantCulture)).Append(",");
                sb.Append(partyMax.ToString(CultureInfo.InvariantCulture)).Append("]}");
            }

            if (showElapsed)
            {
                long epoch = (long)(_startedAt - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                sb.Append(",\"timestamps\":{\"start\":").Append(epoch.ToString(CultureInfo.InvariantCulture)).Append("}");
            }

            sb.Append(",\"assets\":{");
            bool firstAsset = true;
            AppendAsset(sb, ref firstAsset, "large_image", largeImage);
            AppendAsset(sb, ref firstAsset, "large_text", largeText);
            AppendAsset(sb, ref firstAsset, "small_image", smallImage);
            AppendAsset(sb, ref firstAsset, "small_text", smallText);
            sb.Append("}");

            sb.Append(",\"buttons\":[{\"label\":\"NeuzBlox on GitHub\",\"url\":")
              .Append(Json.Quote(AppInfo.GitHubUrl)).Append("}]");

            sb.Append("}},\"nonce\":").Append(Json.Quote(NextNonce())).Append("}");

            Enqueue(OpFrame, sb.ToString());
            return true;
        }

        public void Clear()
        {
            if (!_connected) return;
            Enqueue(OpFrame, "{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"pid\":"
                + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture)
                + "},\"nonce\":" + Json.Quote(NextNonce()) + "}");
        }

        static void AppendAsset(StringBuilder sb, ref bool first, string key, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (!first) sb.Append(",");
            first = false;
            // Discord caps asset text at 128 chars; URLs are left whole.
            sb.Append(Json.Quote(key)).Append(":")
              .Append(Json.Quote(key.EndsWith("_text") ? Clip(value, 128) : value));
        }

        void Enqueue(int op, string json)
        {
            var f = new Frame();
            f.Op = op;
            f.Json = json;
            lock (_outbox)
            {
                while (_outbox.Count > 8) _outbox.Dequeue();   // presence is a snapshot, not a log
                _outbox.Enqueue(f);
            }
            try { _outSignal.Set(); }
            catch { }
        }

        string NextNonce()
        {
            return Interlocked.Increment(ref _nonce).ToString(CultureInfo.InvariantCulture)
                 + "-" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
        }

        static string Clip(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= max ? s : s.Substring(0, max - 1) + "\u2026";
        }

        void WriteLoop()
        {
            while (!_stopping && _connected)
            {
                _outSignal.WaitOne(750);
                while (!_stopping)
                {
                    Frame f = null;
                    lock (_outbox)
                        if (_outbox.Count > 0) f = _outbox.Dequeue();
                    if (f == null) break;
                    if (!WriteFrame(f.Op, f.Json))
                    {
                        _connected = false;
                        return;
                    }
                }
            }
        }

        bool WriteFrame(int op, string json)
        {
            try
            {
                NamedPipeClientStream p = _pipe;
                if (p == null || !p.IsConnected) return false;

                byte[] payload = Encoding.UTF8.GetBytes(json ?? "");
                var frame = new byte[8 + payload.Length];
                Buffer.BlockCopy(BitConverter.GetBytes(op), 0, frame, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(payload.Length), 0, frame, 4, 4);
                Buffer.BlockCopy(payload, 0, frame, 8, payload.Length);

                p.Write(frame, 0, frame.Length);
                p.Flush();
                return true;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return false;
            }
        }

        bool Read(out int op, out string body)
        {
            op = -1;
            body = null;
            try
            {
                var header = new byte[8];
                if (!ReadExact(header, 8)) return false;
                op = BitConverter.ToInt32(header, 0);
                int len = BitConverter.ToInt32(header, 4);
                if (len < 0 || len > 1024 * 256) return false;
                if (len == 0) { body = ""; return true; }
                var payload = new byte[len];
                if (!ReadExact(payload, len)) return false;
                body = Encoding.UTF8.GetString(payload);
                return true;
            }
            catch { return false; }
        }

        bool ReadExact(byte[] buffer, int count)
        {
            int got = 0;
            while (got < count)
            {
                NamedPipeClientStream p = _pipe;
                if (p == null || !p.IsConnected || _stopping) return false;
                int n = p.Read(buffer, got, count - got);
                if (n <= 0) return false;
                got += n;
            }
            return true;
        }

        void ReadLoop()
        {
            while (!_stopping && _connected)
            {
                int op;
                string body;
                if (!Read(out op, out body))
                {
                    if (_stopping) return;
                    _connected = false;
                    Log.Write("Discord rich presence disconnected.");
                    return;
                }
                if (op == OpPing) Enqueue(OpPong, body ?? "");
                else if (op == OpClose) { _connected = false; return; }
                else if (body != null && body.IndexOf("\"evt\":\"ERROR\"", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Discord answers every command; without this an unusable asset or a
                    // malformed payload would just vanish silently.
                    _lastError = Clip(body, 300);
                    Log.Write("Discord rejected a presence update: " + _lastError);
                }
            }
        }

        public void Dispose()
        {
            // Deliberately does not send anything first - Discord drops the presence when
            // the pipe closes, and a farewell write could block whoever is disposing us.
            Disconnect();
        }
    }
}
