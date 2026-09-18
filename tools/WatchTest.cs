using System;
using System.Net;
using System.Threading;
using NeuzBlox;

// Dev harness: runs the game watcher against the real client logs and prints what it
// worked out for each account, so the log parsing is checked against reality.
// usage: WatchTest.exe <userId> [userId...]
static class WatchTest
{
    static void Main(string[] args)
    {
        ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;

        long[] users;
        if (args.Length > 0)
        {
            users = new long[args.Length];
            for (int i = 0; i < args.Length; i++) users[i] = long.Parse(args[i]);
        }
        else
        {
            var accounts = AccountStore.Load();
            users = new long[accounts.Count];
            for (int i = 0; i < accounts.Count; i++) users[i] = accounts[i].UserId;
            Console.WriteLine("loaded " + accounts.Count + " accounts from the vault");
        }

        GameWatcher.Start();
        Console.WriteLine("scanning client logs...");
        Thread.Sleep(6000);

        foreach (long u in users)
        {
            LiveGame g = GameWatcher.ForUser(u);
            if (g == null)
            {
                Console.WriteLine("  user " + u + " -> not in a game (app/home, or no recent log)");
            }
            else
            {
                Console.WriteLine("  user " + u + " -> place " + g.PlaceId
                    + "  name=" + (g.Name.Length == 0 ? "<unresolved>" : g.Name)
                    + "  job=" + (g.JobId.Length > 8 ? g.JobId.Substring(0, 8) + "..." : g.JobId));
                string icon = RobloxApi.GetGameIcon(g.PlaceId);
                Console.WriteLine("       icon: " + (icon ?? "<none>"));
            }
        }

        GameWatcher.Stop();
    }
}
