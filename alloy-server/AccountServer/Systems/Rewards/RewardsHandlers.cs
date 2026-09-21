#region

using System;
using System.Collections.Specialized;
using System.Globalization;
using System.Threading.Tasks;
using System.Xml.Linq;
using AccountServer.Systems.Board;
using Common.Database;
using Common.Utilities;

#endregion

namespace AccountServer.Systems.Rewards;

// The Inbox, the Daily Gift and the Daily Spin (the Character Book pages). All of them need a real signed-in account; the server decides everything (what is
// claimable, what the wheel lands on), the client only shows it. each daily reward can be used again 24 hours after it was last used.
internal static class RewardsResponse {
    public const string SignIn = "Sign in first.";

    public static bool TryId(NameValueCollection query, out int id) => int.TryParse(query["id"], NumberStyles.None, CultureInfo.InvariantCulture, out id);

    public static XElement Balance(XElement element, (int Gold, int Fame) balance) {
        element.Add(new XAttribute("balanceGold", balance.Gold), new XAttribute("balanceFame", balance.Fame));
        return element;
    }
}

public class InboxList : RequestHandler {
    public override string Path => "/inbox/list";

    public override async Task<string> Handle(string ip, NameValueCollection query) {
        var acc = await BoardAuth.VerifyAsync(query);
        if (acc == null)
            return WriteError(RewardsResponse.SignIn);

        var messages = await RewardsDb.ListAsync(acc.Id);
        var root = new XElement("Inbox");
        foreach (var m in messages) {
            root.Add(new XElement("Message",
                new XAttribute("id", m.Id),
                new XAttribute("sender", m.Sender ?? string.Empty),
                new XAttribute("subject", m.Subject ?? string.Empty),
                new XAttribute("created", m.CreatedUnix),
                new XAttribute("read", m.IsRead ? "true" : "false"),
                new XAttribute("claimed", m.Claimed ? "true" : "false"),
                new XAttribute("gold", m.Gold),
                new XAttribute("fame", m.Fame),
                m.Body ?? string.Empty));
        }

        return root.ToString(SaveOptions.DisableFormatting);
    }
}

public class InboxRead : RequestHandler {
    public override string Path => "/inbox/read";

    public override async Task<string> Handle(string ip, NameValueCollection query) {
        var acc = await BoardAuth.VerifyAsync(query);
        if (acc == null || !RewardsResponse.TryId(query, out var id))
            return WriteError(RewardsResponse.SignIn);

        await RewardsDb.MarkReadAsync(acc.Id, id);
        return WriteSuccess();
    }
}

public class InboxClaim : RequestHandler {
    private static readonly Logger Log = new(typeof(InboxClaim));

    public override string Path => "/inbox/claim";

    public override async Task<string> Handle(string ip, NameValueCollection query) {
        var acc = await BoardAuth.VerifyAsync(query);
        if (acc == null || !RewardsResponse.TryId(query, out var id))
            return WriteError(RewardsResponse.SignIn);

        var reward = await RewardsDb.ClaimAsync(acc.Id, id);
        if (reward == null)
            return WriteError("There is nothing to claim in that message.");

        Log.Info($"Inbox: {acc.Name} claimed {reward.Value.Gold} gold / {reward.Value.Fame} fame from message {id}");
        var element = new XElement("Claimed", new XAttribute("gold", reward.Value.Gold), new XAttribute("fame", reward.Value.Fame));
        return RewardsResponse.Balance(element, await RewardsDb.BalanceAsync(acc.Id)).ToString(SaveOptions.DisableFormatting);
    }
}

public class InboxDelete : RequestHandler {
    public override string Path => "/inbox/delete";

    public override async Task<string> Handle(string ip, NameValueCollection query) {
        var acc = await BoardAuth.VerifyAsync(query);
        if (acc == null || !RewardsResponse.TryId(query, out var id))
            return WriteError(RewardsResponse.SignIn);

        var problem = await RewardsDb.DeleteAsync(acc.Id, id);
        return problem == null ? WriteSuccess() : WriteError(problem);
    }
}

public class DailyStatus : RequestHandler {
    public override string Path => "/daily/status";

    public override async Task<string> Handle(string ip, NameValueCollection query) {
        var acc = await BoardAuth.VerifyAsync(query);
        if (acc == null)
            return WriteError(RewardsResponse.SignIn);

        var s = await RewardsDb.StatusAsync(acc.Id, RewardsDb.UtcNow());
        var root = new XElement("Daily",
            new XAttribute("giftReady", s.GiftReady ? "true" : "false"),
            new XAttribute("giftDay", s.GiftDay),
            new XAttribute("streak", s.Streak),
            new XAttribute("spinReady", s.SpinReady ? "true" : "false"),
            new XAttribute("unread", s.Unread),
            new XAttribute("secondsToGift", s.SecondsToGift),
            new XAttribute("secondsToSpin", s.SecondsToSpin),
            new XAttribute("secondsToReset", Math.Max(s.SecondsToGift, s.SecondsToSpin)));       // only for clients built before the two separate timers existed

        // The whole week and the wheel are sent so the client draws exactly what the server will pay.
        for (var day = 0; day < DailyGiftRules.Days; day++) {
            var reward = DailyGiftRules.RewardFor(day);
            root.Add(new XElement("Gift", new XAttribute("day", day), new XAttribute("gold", reward.Gold), new XAttribute("fame", reward.Fame)));
        }

        foreach (var prize in SpinWheel.Prizes)
            root.Add(new XElement("Prize", new XAttribute("gold", prize.Reward.Gold), new XAttribute("fame", prize.Reward.Fame)));

        return root.ToString(SaveOptions.DisableFormatting);
    }
}

public class DailyClaim : RequestHandler {
    private static readonly Logger Log = new(typeof(DailyClaim));

    public override string Path => "/daily/claim";

    public override async Task<string> Handle(string ip, NameValueCollection query) {
        var acc = await BoardAuth.VerifyAsync(query);
        if (acc == null)
            return WriteError(RewardsResponse.SignIn);

        var result = await RewardsDb.ClaimGiftAsync(acc.Id, RewardsDb.UtcNow());
        if (result.Error != null)
            return WriteError(result.Error);

        Log.Info($"Daily gift: {acc.Name} day {result.Day + 1} (streak {result.Streak})");
        var element = new XElement("Gift", new XAttribute("day", result.Day), new XAttribute("streak", result.Streak), new XAttribute("gold", result.Reward.Gold), new XAttribute("fame", result.Reward.Fame));
        return RewardsResponse.Balance(element, await RewardsDb.BalanceAsync(acc.Id)).ToString(SaveOptions.DisableFormatting);
    }
}

public class DailySpin : RequestHandler {
    private static readonly Logger Log = new(typeof(DailySpin));

    public override string Path => "/daily/spin";

    public override async Task<string> Handle(string ip, NameValueCollection query) {
        var acc = await BoardAuth.VerifyAsync(query);
        if (acc == null)
            return WriteError(RewardsResponse.SignIn);

        var result = await RewardsDb.SpinAsync(acc.Id, RewardsDb.UtcNow());
        if (result.Error != null)
            return WriteError(result.Error);

        Log.Info($"Daily spin: {acc.Name} won {result.Reward.Gold} gold / {result.Reward.Fame} fame (segment {result.Index})");
        var element = new XElement("Spin", new XAttribute("index", result.Index), new XAttribute("gold", result.Reward.Gold), new XAttribute("fame", result.Reward.Fame));
        return RewardsResponse.Balance(element, await RewardsDb.BalanceAsync(acc.Id)).ToString(SaveOptions.DisableFormatting);
    }
}
