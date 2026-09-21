using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;

namespace AlloyClient.Data;

// What a reward pays: gold (the account's Credits, the gold coin) and fame (the copper coin).
public readonly record struct RewardAmount(int Gold, int Fame) {
    public bool IsEmpty => Gold <= 0 && Fame <= 0;
}

public sealed record InboxMessage(int Id, string Sender, string Subject, string Body, long CreatedUnix, bool IsRead, bool Claimed, int Gold, int Fame) {
    public bool HasGift => Gold > 0 || Fame > 0;

    // A gift is only worth attention while it is still unclaimed.
    public bool CanClaim => HasGift && !Claimed;

    public RewardAmount Gift => new(Gold, Fame);
}

// The Inbox page: <Inbox><Message id sender subject created read claimed gold fame>body</Message>...</Inbox>. Anything else (an <Error>, an empty answer from
// an older server, broken XML) is "not an inbox".
public sealed record InboxData(IReadOnlyList<InboxMessage> Messages) {
    public int Unread {
        get {
            var n = 0;
            foreach (var m in Messages) {
                if (!m.IsRead || m.CanClaim) {
                    n++;
                }
            }

            return n;
        }
    }

    public static bool TryParse(string response, out InboxData data) {
        data = null;
        if (string.IsNullOrWhiteSpace(response)) {
            return false;
        }

        try {
            var root = XElement.Parse(response);
            if (root.Name.LocalName != "Inbox") {
                return false;
            }

            var list = new List<InboxMessage>();
            foreach (var e in root.Elements("Message")) {
                if (!TryInt(e, "id", out var id) || !long.TryParse((string)e.Attribute("created"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var created)) {
                    continue;     // one damaged entry must not hide the rest
                }

                TryInt(e, "gold", out var gold);
                TryInt(e, "fame", out var fame);
                list.Add(new InboxMessage(id, (string)e.Attribute("sender") ?? string.Empty, (string)e.Attribute("subject") ?? string.Empty, e.Value,
                    created, (string)e.Attribute("read") == "true", (string)e.Attribute("claimed") == "true", Math.Max(0, gold), Math.Max(0, fame)));
            }

            data = new InboxData(list);
            return true;
        } catch (Exception) {
            return false;
        }
    }

    internal static bool TryInt(XElement e, string name, out int value) => int.TryParse((string)e.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    // "just now", "5 min ago", "3 h ago", "2 d ago"
    public static string Ago(long createdUnix, long nowUnix) => BugBoardData.Ago(createdUnix, nowUnix);
}

// The Daily Gift and Daily Spin pages: what is ready now, the week's gifts and the wheel's prizes (sent by the server, so the client draws exactly what will be paid).
public sealed record DailyData(bool GiftReady, int GiftDay, int Streak, bool SpinReady, int Unread, int SecondsToGift, int SecondsToSpin, IReadOnlyList<RewardAmount> Gifts, IReadOnlyList<RewardAmount> Prizes) {

    // <Daily giftReady giftDay streak spinReady unread secondsToGift secondsToSpin><Gift day gold fame/>x7 <Prize gold fame/>...</Daily>
    public static bool TryParse(string response, out DailyData data) {
        data = null;
        if (string.IsNullOrWhiteSpace(response)) {
            return false;
        }

        try {
            var root = XElement.Parse(response);
            if (root.Name.LocalName != "Daily") {
                return false;
            }

            var gifts = ReadRewards(root, "Gift");
            var prizes = ReadRewards(root, "Prize");
            if (gifts.Count == 0 || prizes.Count == 0) {
                return false;
            }

            InboxData.TryInt(root, "giftDay", out var day);
            InboxData.TryInt(root, "streak", out var streak);
            InboxData.TryInt(root, "unread", out var unread);
            // Each reward has its own wait (24 hours after it was last used). An older server sent one shared "secondsToReset" instead.
            InboxData.TryInt(root, "secondsToReset", out var shared);
            var toGift = InboxData.TryInt(root, "secondsToGift", out var g) ? g : shared;
            var toSpin = InboxData.TryInt(root, "secondsToSpin", out var s) ? s : shared;
            data = new DailyData((string)root.Attribute("giftReady") == "true", Math.Clamp(day, 0, gifts.Count - 1), Math.Max(0, streak),
                (string)root.Attribute("spinReady") == "true", Math.Max(0, unread), Math.Max(0, toGift), Math.Max(0, toSpin), gifts, prizes);
            return true;
        } catch (Exception) {
            return false;
        }
    }

    private static List<RewardAmount> ReadRewards(XElement root, string name) {
        var list = new List<RewardAmount>();
        foreach (var e in root.Elements(name)) {
            InboxData.TryInt(e, "gold", out var gold);
            InboxData.TryInt(e, "fame", out var fame);
            list.Add(new RewardAmount(Math.Max(0, gold), Math.Max(0, fame)));
        }

        return list;
    }

    // "5h 12m", "42m" - the time left until a daily reward can be used again, for the "come back in ..." lines.
    public static string Countdown(int seconds) {
        seconds = Math.Max(0, seconds);
        var hours = seconds / 3600;
        var minutes = seconds % 3600 / 60;
        if (hours > 0) {
            return $"{hours}h {minutes:00}m";
        }

        return $"{Math.Max(1, minutes)}m";
    }
}

// What claiming a gift / message or spinning the wheel answered: what was paid, the account's new balance, and (for the wheel) which segment it landed on.
public sealed record RewardResult(RewardAmount Paid, int BalanceGold, int BalanceFame, int SegmentIndex) {

    // <Claimed gold fame balanceGold balanceFame/>   <Gift day streak gold fame balanceGold balanceFame/>   <Spin index gold fame balanceGold balanceFame/>
    public static bool TryParse(string response, out RewardResult result) {
        result = null;
        if (string.IsNullOrWhiteSpace(response)) {
            return false;
        }

        try {
            var root = XElement.Parse(response);
            if (root.Name.LocalName is not ("Claimed" or "Gift" or "Spin")) {
                return false;
            }

            InboxData.TryInt(root, "gold", out var gold);
            InboxData.TryInt(root, "fame", out var fame);
            InboxData.TryInt(root, "balanceGold", out var balanceGold);
            InboxData.TryInt(root, "balanceFame", out var balanceFame);
            var index = -1;
            if (root.Name.LocalName == "Spin" && !InboxData.TryInt(root, "index", out index)) {
                index = -1;
            }

            result = new RewardResult(new RewardAmount(gold, fame), balanceGold, balanceFame, index);
            return true;
        } catch (Exception) {
            return false;
        }
    }
}
