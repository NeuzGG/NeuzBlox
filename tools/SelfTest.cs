using System;
using System.Net;
using NeuzBlox;

// Dev helper: exercises the non-UI plumbing without needing a real account.
static class SelfTest
{
    static int _fail;

    static void Main()
    {
        ServicePointManager.SecurityProtocol =
            (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;

        Section("client discovery");
        string current = RobloxClient.CurrentVersionFolder(true);
        Check("Roblox reports a current client build", !string.IsNullOrEmpty(current), current);

        string player = RobloxClient.FindPlayer("");
        Check("RobloxPlayerBeta.exe located", !string.IsNullOrEmpty(player), player);
        Check("version readable", RobloxClient.Version(player) != "not found", RobloxClient.Version(player));
        Check("we launch the build Roblox says is current, not whatever is registered",
              string.IsNullOrEmpty(current) || (player != null && player.IndexOf(current, StringComparison.OrdinalIgnoreCase) >= 0),
              "registered=" + (RobloxClient.RegisteredVersionFolder() ?? "none"));

        string mismatch = RobloxClient.HandlerMismatch();
        Console.WriteLine("    handler: " + (mismatch ?? "matches Roblox's current build"));

        Section("join target parsing");
        ParseOk("https://www.roblox.com/games/920587237/Adopt-Me", JoinMode.Place, 920587237);
        ParseOk("920587237", JoinMode.Place, 920587237);
        ParseOk("roblox.com/games/2753915549/Blox-Fruits?privateServerLinkCode=abc123",
                JoinMode.PrivateServer, 2753915549);
        string err;
        JoinTarget app = JoinTarget.Parse("", JoinMode.App, out err);
        Check("empty input -> app mode", app != null && app.Mode == JoinMode.App, err);
        JoinTarget bad = JoinTarget.Parse("not a link", JoinMode.Place, out err);
        Check("garbage rejected with a message", bad == null && !string.IsNullOrEmpty(err), err);
        JoinTarget share = JoinTarget.Parse("https://www.roblox.com/share?code=abc&type=Server", JoinMode.Place, out err);
        Check("share link explained, not silently wrong", share == null && err.Contains("share"), err);

        Section("launch uri");
        JoinTarget place = JoinTarget.Parse("920587237", JoinMode.Place, out err);
        string uri = RobloxClient.BuildLaunchUri("TICKET123", place, "112233445566", "en_us");
        Check("starts with the roblox-player protocol", uri.StartsWith("roblox-player:1+launchmode:play"), null);
        Check("carries the ticket", uri.Contains("+gameinfo:TICKET123"), null);
        Check("carries an encoded placelauncherurl", uri.Contains("+placelauncherurl:https%3A%2F%2F"), null);
        Check("carries the browser tracker", uri.Contains("+browsertrackerid:112233445566"), null);
        Console.WriteLine("    " + uri.Substring(0, Math.Min(150, uri.Length)) + "...");

        string appUri = RobloxClient.BuildLaunchUri("T", app, "1", "en_us");
        Check("app mode omits placelauncherurl",
              appUri.Contains("launchmode:app") && !appUri.Contains("placelauncherurl"), null);

        Section("roblox connectivity (no account needed)");
        // A cookie-shaped value that is not a real session: Roblox answers 401,
        // which proves the request itself was accepted (headers, TLS, endpoint).
        string fake = "_|WARNING:-DO-NOT-SHARE-THIS.--" + new string('A', 64);
        UserResult who = RobloxApi.WhoAmI(fake, "112233445566");
        Check("users.roblox.com answers and a dead session is reported clearly",
              !who.Ok && who.Error.IndexOf("expired", StringComparison.OrdinalIgnoreCase) >= 0, who.Error);

        TicketResult tr = RobloxApi.GetAuthTicket(fake, "112233445566");
        Check("ticket request fails gracefully on a dead session", !tr.Ok && tr.Error.Length > 0, tr.Error);

        string name = RobloxApi.GetPlaceName(920587237);
        Check("place name lookup", !string.IsNullOrEmpty(name), name);

        Section("cookie handling");
        string norm = RobloxApi.NormalizeCookie("  .ROBLOSECURITY=_|WARNING:-DO-NOT-SHARE-THIS.--x; path=/  ");
        Check("full cookie string normalised", norm == "_|WARNING:-DO-NOT-SHARE-THIS.--x", norm);
        Check("obvious junk rejected", !RobloxApi.LooksLikeCookie("hello"), null);

        Section("account vault round-trip");
        var probe = new Account();
        probe.Alias = "selftest";
        probe.Cookie = "_|WARNING:-DO-NOT-SHARE-THIS.--selftest";
        probe.UserId = 42;
        Account back = Account.FromJson(Json.Parse(probe.ToJson()));
        Check("account serialises and reloads",
              back.Alias == "selftest" && back.UserId == 42 && back.Cookie == probe.Cookie, null);

        Console.WriteLine();
        Console.WriteLine(_fail == 0 ? "ALL CHECKS PASSED" : _fail + " CHECK(S) FAILED");
        Environment.Exit(_fail == 0 ? 0 : 1);
    }

    static void ParseOk(string input, JoinMode mode, long expectPlace)
    {
        string err;
        JoinTarget t = JoinTarget.Parse(input, mode, out err);
        Check("parse " + Trim(input, 46),
              t != null && t.Mode == mode && t.PlaceId == expectPlace,
              t == null ? err : "placeId=" + t.PlaceId + " mode=" + t.Mode);
    }

    static void Section(string s)
    {
        Console.WriteLine();
        Console.WriteLine("-- " + s);
    }

    static void Check(string what, bool ok, string detail)
    {
        if (!ok) _fail++;
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what + (string.IsNullOrEmpty(detail) ? "" : "  [" + detail + "]"));
    }

    static string Trim(string s, int n)
    {
        return s.Length <= n ? s : s.Substring(0, n) + "...";
    }
}
