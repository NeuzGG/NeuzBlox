using System;
using System.Net;
using System.Threading;
using NeuzBlox;

// Dev harness: drives the real DiscordRpc class with the exact payload shape the
// app now sends (game icon in the large slot, our mark in the small one) so the
// JSON is verified as built, not as imagined.
static class RpcHarness
{
    static void Main(string[] args)
    {
        ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;

        string appId = args.Length > 0 ? args[0] : AppInfo.DefaultDiscordAppId;
        long placeId = args.Length > 1 ? long.Parse(args[1]) : 920587237L;

        string icon = RobloxApi.GetGameIcon(placeId);
        Console.WriteLine("game icon: " + (icon ?? "<none>"));

        var rpc = new DiscordRpc();
        if (!rpc.Connect(appId))
        {
            Console.WriteLine("connect failed: " + rpc.LastError);
            return;
        }
        Console.WriteLine("connected: " + rpc.Connected);

        bool sent = rpc.SetActivity("Adopt Me!", "2 clients running", 2, 3, true,
                                    icon, "Adopt Me!", "logo_small", "NeuzBlox");
        Console.WriteLine("queued: " + sent);

        Thread.Sleep(2500);
        Console.WriteLine("still connected after send: " + rpc.Connected);
        Console.WriteLine("last error: " + (rpc.LastError.Length == 0 ? "<none>" : rpc.LastError));

        rpc.Clear();
        Thread.Sleep(800);
        rpc.Dispose();
        Thread.Sleep(400);
        Console.WriteLine("done");
    }
}
