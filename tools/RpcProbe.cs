using System;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Text;
using System.Threading;

// Dev probe: asks Discord directly whether it will accept a raw image URL as a
// Rich Presence asset, versus an uploaded asset key. Prints Discord's own reply
// frame for each attempt, then clears the presence again.
// usage: RpcProbe.exe <appId> [placeId]
static class RpcProbe
{
    static NamedPipeClientStream _pipe;

    static void Main(string[] args)
    {
        ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;

        string appId = args.Length > 0 ? args[0] : "";
        string placeId = args.Length > 1 ? args[1] : "920587237";
        if (appId.Length == 0) { Console.WriteLine("need an app id"); return; }

        string icon = RobloxGameIcon(placeId);
        Console.WriteLine("roblox game icon url: " + (icon ?? "<lookup failed>"));
        Console.WriteLine();

        if (!Connect(appId)) { Console.WriteLine("could not connect to Discord"); return; }
        Console.WriteLine("connected + handshaked");
        Console.WriteLine();

        if (icon != null)
        {
            Console.WriteLine("TEST 1: large_image = raw https URL");
            Console.WriteLine(Reply(Activity("Probe: raw url", "test 1", icon, null)));
            Console.WriteLine();

            Console.WriteLine("TEST 2: large_image = mp:external style");
            string mp = "mp:external/" + icon.Replace("https://", "");
            Console.WriteLine(Reply(Activity("Probe: mp external", "test 2", mp, null)));
            Console.WriteLine();
        }

        Console.WriteLine("TEST 3: large_image = uploaded asset key 'logo_big' + small 'logo_small'");
        Console.WriteLine(Reply(Activity("Probe: asset keys", "test 3", "logo_big", "logo_small")));
        Console.WriteLine();

        Console.WriteLine("clearing presence");
        Send(1, "{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"pid\":"
            + System.Diagnostics.Process.GetCurrentProcess().Id + "},\"nonce\":\"clear\"}");
        Read();
        _pipe.Dispose();
    }

    static string Activity(string details, string state, string large, string small)
    {
        var sb = new StringBuilder();
        sb.Append("{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"pid\":");
        sb.Append(System.Diagnostics.Process.GetCurrentProcess().Id);
        sb.Append(",\"activity\":{\"type\":0,\"details\":").Append(Q(details));
        sb.Append(",\"state\":").Append(Q(state));
        sb.Append(",\"assets\":{\"large_image\":").Append(Q(large));
        sb.Append(",\"large_text\":\"probe\"");
        if (small != null) sb.Append(",\"small_image\":").Append(Q(small)).Append(",\"small_text\":\"probe\"");
        sb.Append("}}},\"nonce\":").Append(Q(Guid.NewGuid().ToString("N"))).Append("}");
        return sb.ToString();
    }

    static string Reply(string json)
    {
        Send(1, json);
        string body = Read();
        if (body == null) return "  <no reply>";
        bool err = body.IndexOf("\"evt\":\"ERROR\"", StringComparison.OrdinalIgnoreCase) >= 0;
        string shown = body.Length > 600 ? body.Substring(0, 600) + "..." : body;
        return (err ? "  REJECTED -> " : "  ACCEPTED -> ") + shown;
    }

    static string Q(string s)
    {
        return "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    static bool Connect(string appId)
    {
        for (int i = 0; i < 10; i++)
        {
            try
            {
                var p = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
                p.Connect(400);
                _pipe = p;
                Send(0, "{\"v\":1,\"client_id\":" + Q(appId) + "}");
                string body = Read();
                if (body != null && body.IndexOf("READY", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                Console.WriteLine("handshake reply: " + body);
                return body != null;
            }
            catch { }
        }
        return false;
    }

    static void Send(int op, string json)
    {
        byte[] payload = Encoding.UTF8.GetBytes(json);
        var frame = new byte[8 + payload.Length];
        Buffer.BlockCopy(BitConverter.GetBytes(op), 0, frame, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(payload.Length), 0, frame, 4, 4);
        Buffer.BlockCopy(payload, 0, frame, 8, payload.Length);
        _pipe.Write(frame, 0, frame.Length);
        _pipe.Flush();
    }

    static string Read()
    {
        try
        {
            var head = new byte[8];
            if (!Exact(head, 8)) return null;
            int len = BitConverter.ToInt32(head, 4);
            if (len <= 0) return "";
            var buf = new byte[len];
            if (!Exact(buf, len)) return null;
            return Encoding.UTF8.GetString(buf);
        }
        catch { return null; }
    }

    static bool Exact(byte[] b, int n)
    {
        int got = 0;
        while (got < n)
        {
            int r = _pipe.Read(b, got, n - got);
            if (r <= 0) return false;
            got += r;
        }
        return true;
    }

    static string RobloxGameIcon(string placeId)
    {
        try
        {
            string u = Get("https://apis.roblox.com/universes/v1/places/" + placeId + "/universe");
            int i = u.IndexOf("universeId");
            if (i < 0) return null;
            string digits = "";
            for (int k = i; k < u.Length; k++)
            {
                if (char.IsDigit(u[k])) digits += u[k];
                else if (digits.Length > 0) break;
            }
            string t = Get("https://thumbnails.roblox.com/v1/games/icons?universeIds=" + digits
                         + "&size=512x512&format=Png&isCircular=false");
            int j = t.IndexOf("imageUrl");
            if (j < 0) return null;
            int q1 = t.IndexOf('"', t.IndexOf(':', j)) + 1;
            int q2 = t.IndexOf('"', q1);
            return t.Substring(q1, q2 - q1).Replace("\\/", "/");
        }
        catch { return null; }
    }

    static string Get(string url)
    {
        var req = (HttpWebRequest)WebRequest.Create(url);
        req.UserAgent = "Roblox/WinInet";
        req.Timeout = 12000;
        using (var resp = (HttpWebResponse)req.GetResponse())
        using (var sr = new StreamReader(resp.GetResponseStream()))
            return sr.ReadToEnd();
    }
}
