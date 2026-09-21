using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using AlloyClient.AppEngine;
using AlloyClient.Data;
using AlloyClient.Ui;
using AlloyClient.Utils;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using Alloy.UiLib.Extra;

namespace AlloyClient.Screens.Components.CharacterList;

// The three "account" pages of the Character Book: Inbox, Daily Gift and Daily Spin. The server decides everything (what a message pays, what today's gift is,
// where the wheel lands); these pages only show it and send the click. Answers arrive on a worker thread, so they are queued and applied in the book's frame
// loop (never from the worker itself).
public sealed partial class CharacterBook {

    private const string CoinGold = "Console/GoldCoin0";
    private const string CoinFame = "Console/CopperCoin0";
    private const uint Good = 0x2F6B2A;
    private const uint Bad = 0x8A2A1A;

    private int _lifetimeGoldEarned;
    private int _lifetimeFameEarned;

    private readonly ConcurrentQueue<Action> _uiQueue = new();
    private bool _rewardsBusy;      // a claim / spin / delete is in flight: further clicks are ignored
    private bool _rewardsLoading;

    private InboxData _inbox;
    private string _inboxError;
    private DailyData _daily;
    private string _dailyError;

    private int _selectedMessageId = -1;
    private int _inboxScroll;
    private string _inboxNote = string.Empty;
    private string _giftNote = string.Empty;
    private string _spinNote = string.Empty;

    // The spin animation: a highlight runs round the ring of prize chips, slowing down until it stops on the segment the server picked.
    private bool _spinning;
    private RewardResult _spinResult;
    private double _spinElapsedMs;
    private double[] _spinStepTimes;
    private int _spinStepShown;
    private int _spinChipCount;
    private ColorRect _spinHighlight;
    private Container[] _spinChipBoxes;
    private (int X, int Y)[] _spinChipCenters;
    private int _spinShownIndex;

    private const int RowH = 46;
    private static readonly int ListTop = PageTop + Sz(56);
    private static readonly int ContentBottom = PageBottom - Sz(8);

    private static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    #region Loading and the frame hook

    // Asks the server for the inbox and the daily state and rebuilds the three pages when the answers arrive.
    private void RefreshRewards() {
        if (_rewardsLoading) {
            return;
        }

        _rewardsLoading = true;
        _ = LoadRewardsAsync();
    }

    private async Task LoadRewardsAsync() {
        AppRequests.InboxResult inbox = default;
        AppRequests.DailyResult daily = default;
        try {
            inbox = await AppRequests.GetInbox();
            daily = await AppRequests.GetDaily();
        } catch (Exception) {
            // shown as "could not reach the server" below
        }

        _uiQueue.Enqueue(() => {
            _rewardsLoading = false;
            _inbox = inbox.Data ?? _inbox;
            _inboxError = inbox.Data == null ? inbox.Error ?? "Could not reach the server." : null;
            _daily = daily.Data ?? _daily;
            _dailyError = daily.Data == null ? daily.Error ?? "Could not reach the server." : null;
            RebuildRewardPages();
        });
    }

    private void UpdateRewards(double dt) {
        while (_uiQueue.TryDequeue(out var action)) {
            action();
        }

        if (_spinning) {
            UpdateSpin(dt);
        }
    }

    private void RebuildRewardPages() {
        BuildInboxPage(_pages[(int) BookPage.Inbox]);
        BuildDailySpinPage(_pages[(int) BookPage.DailySpin]);
        BuildDailyGiftPage(_pages[(int) BookPage.DailyGift]);
        UpdateTabBadges();
    }

    // Runs a server call without blocking the game; the answer is applied on the frame loop. Only one at a time.
    private void RewardCall<T>(Func<Task<T>> request, Action<T> done) {
        if (_rewardsBusy || _spinning) {
            return;
        }

        _rewardsBusy = true;
        _ = RunCallAsync(request, done);
    }

    private async Task RunCallAsync<T>(Func<Task<T>> request, Action<T> done) {
        var result = default(T);
        try {
            result = await request();
        } catch (Exception) {
            // result stays empty: the callers treat that as "could not reach the server"
        }

        _uiQueue.Enqueue(() => {
            _rewardsBusy = false;
            done(result);
        });
    }

    // The new gold / fame after a claim or spin: the Profile page's coins update at once.
    private void ApplyBalance(RewardResult result) {
        _gold = result.BalanceGold;
        _fame = result.BalanceFame;
        // The lifetime totals on the Profile page come from the account data fetched when the book opened, so what was earned since then is added on top
        // (the server already counted it; the page just has not been told).
        _lifetimeGoldEarned += Math.Max(0, result.Paid.Gold);
        _lifetimeFameEarned += Math.Max(0, result.Paid.Fame);
        BuildProfilePage(_pages[(int) BookPage.Profile]);
    }

    #endregion

    #region Small shared pieces

    // A row of coin icons with amounts, e.g. [gold] 250   [copper] 50. Centred on (centerX, centerY), or starting at centerX when leftAligned.
    private static Container RewardLine(RewardAmount reward, int centerX, int centerY, float textSize, int iconSize, bool leftAligned = false) {
        var row = new Container();
        var parts = new List<(string Icon, SimpleText Text)>();
        if (reward.Gold > 0) {
            parts.Add((CoinGold, Text(reward.Gold.ToString("N0"), FontGroup.MyriadPro, textSize, 0, centerY, UiAnchor.MiddleLeft)));
        }

        if (reward.Fame > 0) {
            parts.Add((CoinFame, Text(reward.Fame.ToString("N0"), FontGroup.MyriadPro, textSize, 0, centerY, UiAnchor.MiddleLeft)));
        }

        const int gap = 6;
        const int between = 16;
        var total = 0;
        foreach (var part in parts) {
            total += iconSize + gap + part.Text.Width;
        }

        total += Math.Max(0, parts.Count - 1) * between;

        var x = leftAligned ? centerX : centerX - total / 2;
        foreach (var part in parts) {
            row.AddChild(new ObjectRect(new ObjectRectConfig {
                Texture = TextureHelper.FromUiAtlas(part.Icon, 0, false),
                X = x + iconSize / 2,
                Y = centerY,
                Width = iconSize,
                Height = iconSize,
                Anchor = UiAnchor.Middle,
                OutlineEnabled = false,
                GlowEnabled = false
            }));
            part.Text.X = x + iconSize + gap;
            row.AddChild(part.Text);
            x += iconSize + gap + part.Text.Width + between;
        }

        return row;
    }

    private static string Shorten(string text, int max) => string.IsNullOrEmpty(text) ? string.Empty : text.Length <= max ? text : text[..(max - 3)].TrimEnd() + "...";

    private static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

    private static string Describe(RewardAmount r) => r.Gold > 0 && r.Fame > 0 ? $"{r.Gold:N0} gold and {r.Fame:N0} fame" : r.Fame > 0 ? $"{r.Fame:N0} fame" : $"{r.Gold:N0} gold";

    // The page's whole left side when the server could not be reached (or has no such feature yet).
    private void BuildOfflinePage(Container page, string title, string message) {
        page.AddChild(Text(title, FontGroup.MyriadPro, TitleSize, LeftPageCenterX, PageTop + Sz(16), UiAnchor.Middle));
        page.AddChild(Rule(LeftPageCenterX, PageTop + Sz(46), LeftPageW - Sz(40)));
        page.AddChild(Text(message, FontGroup.MyriadPro, BodySize - 2, LeftPageCenterX, PageTop + Sz(120), UiAnchor.Middle, maxWidth: LeftPageW - 30));
        page.AddChild(BuildScrollButton("TRY AGAIN", LeftPageCenterX, PageTop + Sz(190), 190, 50, RefreshRewards));
    }

    private static Container Chip(int width, int height, float alpha, uint color = Ink) {
        var chip = new Container();
        chip.AddChild(new ColorRect(new ColorRectConfig { Width = width, Height = height, Color = color, Alpha = alpha }));
        return chip;
    }

    #endregion

    #region Inbox

    private InboxMessage SelectedMessage() {
        if (_inbox == null) {
            return null;
        }

        foreach (var m in _inbox.Messages) {
            if (m.Id == _selectedMessageId) {
                return m;
            }
        }

        return null;
    }

    private void BuildInboxPage(Container page) {
        page.RemoveChildren();

        if (_inbox == null) {
            if (_inboxError != null) {
                BuildOfflinePage(page, "INBOX", _inboxError);
            } else {
                page.AddChild(Text("INBOX", FontGroup.MyriadPro, TitleSize, LeftPageCenterX, PageTop + Sz(16), UiAnchor.Middle));
                page.AddChild(Text("Loading...", FontGroup.MyriadPro, BodySize, LeftPageCenterX, PageTop + Sz(120), UiAnchor.Middle));
            }

            return;
        }

        // ---- left page: the list -------------------------------------------------------------------------------------------------------------------
        var unread = _inbox.Unread;
        page.AddChild(Text(unread > 0 ? $"INBOX  ({unread})" : "INBOX", FontGroup.MyriadPro, TitleSize, LeftPageCenterX, PageTop + Sz(16), UiAnchor.Middle));
        page.AddChild(Rule(LeftPageCenterX, PageTop + Sz(46), LeftPageW - Sz(40)));

        if (_inbox.Messages.Count == 0) {
            page.AddChild(Text("No messages.", FontGroup.MyriadPro, BodySize, LeftPageCenterX, PageTop + Sz(120), UiAnchor.Middle));
            page.AddChild(Text("Gifts and news will arrive here.", FontGroup.MyriadPro, SmallSize, LeftPageCenterX, PageTop + Sz(152), UiAnchor.Middle, color: InkSoft, maxWidth: LeftPageW - 30));
            return;
        }

        var footer = Sz(30);
        var rows = Math.Max(1, (ContentBottom - footer - ListTop) / RowH);
        _inboxScroll = Math.Clamp(_inboxScroll, 0, Math.Max(0, _inbox.Messages.Count - rows));
        var rowW = LeftPageW - Sz(20);
        var rowX = LeftPageX + Sz(10);

        for (var i = 0; i < rows && _inboxScroll + i < _inbox.Messages.Count; i++) {
            var message = _inbox.Messages[_inboxScroll + i];
            page.AddChild(BuildMessageRow(message, rowX, ListTop + i * RowH, rowW, RowH - 4));
        }

        // Newer / Older
        var navY = ContentBottom - footer / 2;
        if (_inboxScroll > 0) {
            page.AddChild(BuildTextButton("NEWER", FontGroup.MyriadPro, SmallSize, rowX, navY, UiAnchor.MiddleLeft, () => { _inboxScroll = Math.Max(0, _inboxScroll - rows); BuildInboxPage(page); }));
        }

        if (_inboxScroll + rows < _inbox.Messages.Count) {
            page.AddChild(BuildTextButton("OLDER", FontGroup.MyriadPro, SmallSize, rowX + rowW, navY, UiAnchor.MiddleRight, () => { _inboxScroll += rows; BuildInboxPage(page); }));
        }

        // ---- right page: the open message -----------------------------------------------------------------------------------------------------------
        BuildMessageDetail(page, SelectedMessage());
    }

    private Sprite BuildMessageRow(InboxMessage message, int x, int y, int width, int height) {
        var row = new Container { X = x, Y = y };
        row.MouseEnabled = true;

        var selected = message.Id == _selectedMessageId;
        var bg = new ColorRect(new ColorRectConfig { Width = width, Height = height, Color = selected ? TileSelected : Ink, Alpha = selected ? 0.55f : 0.14f });
        row.AddChild(bg);

        var fresh = !message.IsRead || message.CanClaim;
        if (fresh) {
            row.AddChild(new ColorRect(new ColorRectConfig { X = 8, Y = height / 2 - 5, Width = 10, Height = 10, Color = Hover, Alpha = 1f }));
        }

        row.AddChild(Text(Shorten(message.Subject, 24), FontGroup.MyriadPro, 20f, 26, 13, UiAnchor.MiddleLeft, outline: fresh ? 2 : 1));
        row.AddChild(Text($"{Shorten(message.Sender, 20)} - {InboxData.Ago(message.CreatedUnix, NowUnix())}", FontGroup.MyriadPro, 14f, 26, height - 11, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));

        if (message.CanClaim) {
            row.AddChild(new ObjectRect(new ObjectRectConfig {
                Texture = TextureHelper.FromUiAtlas(message.Gold > 0 ? CoinGold : CoinFame, 0, false),
                X = width - 22,
                Y = height / 2,
                Width = 24,
                Height = 24,
                Anchor = UiAnchor.Middle,
                OutlineEnabled = false,
                GlowEnabled = false
            }));
        }

        var down = false;
        row.AddEventListener(MouseEvent.MouseOver, () => bg.Alpha = selected ? 0.55f : 0.3f);
        row.AddEventListener(MouseEvent.MouseOut, () => bg.Alpha = selected ? 0.55f : 0.14f);
        row.AddEventListener(MouseEvent.LeftDown, () => down = true);
        row.AddEventListener(MouseEvent.LeftUp, () => {
            if (down) {
                OpenMessage(message);
            }

            down = false;
        });
        return row;
    }

    private void OpenMessage(InboxMessage message) {
        _selectedMessageId = message.Id;
        _inboxNote = string.Empty;

        if (!message.IsRead) {
            // Mark it read here at once, and on the server in the background.
            ReplaceMessage(message with { IsRead = true });
            _ = AppRequests.MarkInboxRead(message.Id);
        }

        BuildInboxPage(_pages[(int) BookPage.Inbox]);
        UpdateTabBadges();
    }

    private void ReplaceMessage(InboxMessage updated) {
        var list = new List<InboxMessage>(_inbox.Messages);
        for (var i = 0; i < list.Count; i++) {
            if (list[i].Id == updated.Id) {
                list[i] = updated;
            }
        }

        _inbox = new InboxData(list);
    }

    private void BuildMessageDetail(Container page, InboxMessage message) {
        page.AddChild(Text(message == null ? "MESSAGE" : "FROM " + Shorten(message.Sender, 16).ToUpperInvariant(), FontGroup.MyriadPro, TitleSize, RightPageCenterX, PageTop + Sz(16), UiAnchor.Middle, maxWidth: RightPageW - 20));
        page.AddChild(Rule(RightPageCenterX, PageTop + Sz(46), RightPageW - Sz(40)));

        if (message == null) {
            page.AddChild(Text("Pick a message on the left", FontGroup.MyriadPro, SmallSize + 2, RightPageCenterX, PageTop + Sz(120), UiAnchor.Middle, color: InkSoft, maxWidth: RightPageW - 30));
            page.AddChild(Text("to read it.", FontGroup.MyriadPro, SmallSize + 2, RightPageCenterX, PageTop + Sz(146), UiAnchor.Middle, color: InkSoft));
            return;
        }

        var left = RightPageX + Sz(14);
        var width = RightPageW - Sz(28);
        page.AddChild(Text(message.Subject, FontGroup.MyriadPro, 26f, left, PageTop + Sz(60), UiAnchor.LeftTop, maxWidth: width));
        page.AddChild(Text(InboxData.Ago(message.CreatedUnix, NowUnix()), FontGroup.MyriadPro, 16f, left, PageTop + Sz(96), UiAnchor.LeftTop, color: InkSoft, outline: 1));
        page.AddChild(Rule(RightPageCenterX, PageTop + Sz(122), RightPageW - Sz(40)));
        page.AddChild(Text(Shorten(message.Body, message.HasGift ? 230 : 420), FontGroup.MyriadPro, 18f, left, PageTop + Sz(136), UiAnchor.LeftTop, maxWidth: width, outline: 1));

        // The gift (if any) and the buttons sit at the bottom of the page.
        var giftY = PageBottom - Sz(58);
        if (message.HasGift) {
            page.AddChild(Rule(RightPageCenterX, giftY - 34, RightPageW - Sz(40)));
            page.AddChild(RewardLine(message.Gift, RightPageCenterX, giftY - 10, 28f, 30));
            if (message.CanClaim) {
                page.AddChild(BuildScrollButton("CLAIM", RightPageCenterX, giftY + 38, 190, 50, ClaimSelectedMessage));
            } else {
                page.AddChild(Text("Gift claimed", FontGroup.MyriadPro, 20f, RightPageCenterX, giftY + 30, UiAnchor.Middle, color: InkSoft, outline: 1));
            }
        }

        if (!message.CanClaim) {
            page.AddChild(BuildTextButton("DELETE", FontGroup.MyriadPro, SmallSize, RightPageX + RightPageW - Sz(14), PageBottom - Sz(26), UiAnchor.RightBottom, DeleteSelectedMessage));
        }

        if (_inboxNote.Length > 0) {
            page.AddChild(Text(_inboxNote, FontGroup.MyriadPro, 18f, RightPageCenterX, PageBottom - 2, UiAnchor.MiddleBottom, color: _inboxNote.StartsWith("You got", StringComparison.Ordinal) ? Good : Bad, maxWidth: RightPageW - 30, outline: 1));
        }
    }

    private void ClaimSelectedMessage() {
        var message = SelectedMessage();
        if (message == null || !message.CanClaim) {
            return;
        }

        RewardCall(() => AppRequests.ClaimInboxMessage(message.Id), call => {
            if (call.Result == null) {
                _inboxNote = call.Error ?? "Could not reach the server.";
            } else {
                ReplaceMessage(message with { Claimed = true, IsRead = true });
                ApplyBalance(call.Result);
                _inboxNote = "You got " + Describe(message.Gift) + "!";
            }

            BuildInboxPage(_pages[(int) BookPage.Inbox]);
            UpdateTabBadges();
        });
    }

    private void DeleteSelectedMessage() {
        var message = SelectedMessage();
        if (message == null || message.CanClaim) {
            return;
        }

        RewardCall(() => AppRequests.DeleteInboxMessage(message.Id), response => {
            if (!response.Success) {
                _inboxNote = response.Message ?? "Could not reach the server.";
            } else {
                var list = new List<InboxMessage>(_inbox.Messages);
                list.RemoveAll(m => m.Id == message.Id);
                _inbox = new InboxData(list);
                _selectedMessageId = -1;
                _inboxNote = string.Empty;
            }

            BuildInboxPage(_pages[(int) BookPage.Inbox]);
            UpdateTabBadges();
        });
    }

    #endregion

    #region Daily Gift

    private void BuildDailyGiftPage(Container page) {
        page.RemoveChildren();

        if (_daily == null) {
            if (_dailyError != null) {
                BuildOfflinePage(page, "DAILY GIFT", _dailyError);
            } else {
                page.AddChild(Text("DAILY GIFT", FontGroup.MyriadPro, TitleSize, LeftPageCenterX, PageTop + Sz(16), UiAnchor.Middle));
                page.AddChild(Text("Loading...", FontGroup.MyriadPro, BodySize, LeftPageCenterX, PageTop + Sz(120), UiAnchor.Middle));
            }

            return;
        }

        var d = _daily;

        // ---- left page: the week -------------------------------------------------------------------------------------------------------------------
        page.AddChild(Text("THIS WEEK", FontGroup.MyriadPro, TitleSize, LeftPageCenterX, PageTop + Sz(16), UiAnchor.Middle));
        page.AddChild(Rule(LeftPageCenterX, PageTop + Sz(46), LeftPageW - Sz(40)));

        var rowW = LeftPageW - Sz(20);
        var rowX = LeftPageX + Sz(10);
        var rowH = Math.Min(RowH, (ContentBottom - ListTop) / d.Gifts.Count);
        for (var i = 0; i < d.Gifts.Count; i++) {
            var y = ListTop + i * rowH;
            var isNext = i == d.GiftDay;
            var claimed = i < d.GiftDay;
            page.AddChild(new ColorRect(new ColorRectConfig { X = rowX, Y = y, Width = rowW, Height = rowH - 4, Color = isNext ? TileSelected : Ink, Alpha = isNext ? 0.6f : claimed ? 0.06f : 0.14f }));
            page.AddChild(Text($"DAY {i + 1}", FontGroup.MyriadPro, 20f, rowX + 12, y + (rowH - 4) / 2, UiAnchor.MiddleLeft, color: claimed ? InkSoft : Ink, outline: 1));
            page.AddChild(RewardLine(d.Gifts[i], rowX + rowW - 12 - 120, y + (rowH - 4) / 2, 20f, 22, leftAligned: true));
            if (claimed) {
                page.AddChild(Text("CLAIMED", FontGroup.MyriadPro, 13f, rowX + 72, y + (rowH - 4) / 2 + 1, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
            }
        }

        // ---- right page: today's gift -------------------------------------------------------------------------------------------------------------
        page.AddChild(Text(d.GiftReady ? "TODAY'S GIFT" : "COME BACK SOON", FontGroup.MyriadPro, TitleSize, RightPageCenterX, PageTop + Sz(16), UiAnchor.Middle, maxWidth: RightPageW - 10));
        page.AddChild(Rule(RightPageCenterX, PageTop + Sz(46), RightPageW - Sz(40)));

        page.AddChild(Text(d.Streak == 0 ? "Start your streak today!" : $"Streak: {Plural(d.Streak, "day")}", FontGroup.MyriadPro, BodySize - 2, RightPageCenterX, PageTop + Sz(80), UiAnchor.Middle, color: InkSoft, outline: 1));

        var gift = d.Gifts[d.GiftDay];
        page.AddChild(new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.FromUiAtlas("BookGems/Icon/DailyGiftBig", 0, false),
            X = RightPageCenterX,
            Y = PageTop + Sz(160),
            Width = 128,
            Height = 128,
            Anchor = UiAnchor.Middle,
            OutlineEnabled = false,
            GlowEnabled = false
        }));
        page.AddChild(Text($"DAY {d.GiftDay + 1}", FontGroup.MyriadPro, 24f, RightPageCenterX, PageTop + Sz(232), UiAnchor.Middle));
        page.AddChild(RewardLine(gift, RightPageCenterX, PageTop + Sz(262), 34f, 36));

        if (d.GiftReady) {
            page.AddChild(BuildScrollButton("OPEN GIFT", RightPageCenterX, PageTop + Sz(310), 220, 54, ClaimGift));
        } else {
            page.AddChild(Text($"Next gift in {DailyData.Countdown(d.SecondsToGift)}", FontGroup.MyriadPro, 22f, RightPageCenterX, PageTop + Sz(306), UiAnchor.Middle, color: InkSoft, outline: 1));
        }

        if (_giftNote.Length > 0) {
            page.AddChild(Text(_giftNote, FontGroup.MyriadPro, 20f, RightPageCenterX, PageBottom - Sz(10), UiAnchor.MiddleBottom, color: _giftNote.StartsWith("You got", StringComparison.Ordinal) ? Good : Bad, maxWidth: RightPageW - 30, outline: 1));
        }
    }

    private void ClaimGift() {
        RewardCall(AppRequests.ClaimDailyGift, call => {
            if (call.Result == null) {
                _giftNote = call.Error ?? "Could not reach the server.";
                BuildDailyGiftPage(_pages[(int) BookPage.DailyGift]);
                return;
            }

            _giftNote = "You got " + Describe(call.Result.Paid) + "!";
            ApplyBalance(call.Result);
            RefreshRewards();        // the streak, the next day and the "come back" timer
        });
    }

    #endregion

    #region Daily Spin

    private void BuildDailySpinPage(Container page) {
        if (_spinning) {
            return;         // never rebuild the wheel while it is turning
        }

        page.RemoveChildren();

        if (_daily == null) {
            if (_dailyError != null) {
                BuildOfflinePage(page, "DAILY SPIN", _dailyError);
            } else {
                page.AddChild(Text("DAILY SPIN", FontGroup.MyriadPro, TitleSize, LeftPageCenterX, PageTop + Sz(16), UiAnchor.Middle));
                page.AddChild(Text("Loading...", FontGroup.MyriadPro, BodySize, LeftPageCenterX, PageTop + Sz(120), UiAnchor.Middle));
            }

            return;
        }

        var d = _daily;

        // ---- left page: the ring of prizes ---------------------------------------------------------------------------------------------------------
        page.AddChild(Text("THE WHEEL", FontGroup.MyriadPro, TitleSize, LeftPageCenterX, PageTop + Sz(16), UiAnchor.Middle));
        page.AddChild(Rule(LeftPageCenterX, PageTop + Sz(46), LeftPageW - Sz(40)));

        _spinChipCount = d.Prizes.Count;
        _spinChipBoxes = new Container[_spinChipCount];
        _spinChipCenters = new (int, int)[_spinChipCount];
        const int chipW = 100;
        const int chipH = 46;
        var cx = LeftPageCenterX;
        var cy = (ListTop + ContentBottom) / 2;
        var rx = LeftPageW / 2 - chipW / 2 - 8;
        var ry = (ContentBottom - ListTop) / 2 - chipH / 2 - 4;

        for (var i = 0; i < _spinChipCount; i++) {
            var angle = -MathF.PI / 2f + i * MathF.Tau / _spinChipCount;       // the first prize is at the top, then clockwise
            var x = cx + (int) MathF.Round(MathF.Cos(angle) * rx);
            var y = cy + (int) MathF.Round(MathF.Sin(angle) * ry);
            _spinChipCenters[i] = (x, y);

            var chip = new Container { X = x - chipW / 2, Y = y - chipH / 2 };
            chip.AddChild(new ColorRect(new ColorRectConfig { Width = chipW, Height = chipH, Color = Ink, Alpha = 0.2f }));
            chip.AddChild(RewardLine(d.Prizes[i], chipW / 2, chipH / 2, 22f, 24));
            _spinChipBoxes[i] = chip;
            page.AddChild(chip);
        }

        _spinHighlight = new ColorRect(new ColorRectConfig { Width = chipW + 12, Height = chipH + 12, Color = Hover, Alpha = 0.55f });
        _spinHighlight.Visible = false;
        page.AddChild(_spinHighlight);

        // The middle of the ring: what the wheel gave last, or an invitation.
        page.AddChild(new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.FromUiAtlas("BookGems/Icon/DailySpinBig", 0, false),
            X = cx,
            Y = cy,
            Width = 96,
            Height = 96,
            Anchor = UiAnchor.Middle,
            OutlineEnabled = false,
            GlowEnabled = false
        }));

        // ---- right page: the button ----------------------------------------------------------------------------------------------------------------
        page.AddChild(Text("DAILY SPIN", FontGroup.MyriadPro, TitleSize, RightPageCenterX, PageTop + Sz(16), UiAnchor.Middle));
        page.AddChild(Rule(RightPageCenterX, PageTop + Sz(46), RightPageW - Sz(40)));
        page.AddChild(Text("Spin the wheel once a day for gold or fame.", FontGroup.MyriadPro, 20f, RightPageCenterX, PageTop + Sz(84), UiAnchor.Middle, color: InkSoft, maxWidth: RightPageW - 40, outline: 1));

        if (d.SpinReady) {
            page.AddChild(BuildScrollButton("SPIN", RightPageCenterX, PageTop + Sz(180), 220, 60, StartSpin));
        } else {
            page.AddChild(Text("You already used your spin.", FontGroup.MyriadPro, 22f, RightPageCenterX, PageTop + Sz(168), UiAnchor.Middle, maxWidth: RightPageW - 30));
            page.AddChild(Text($"Next spin in {DailyData.Countdown(d.SecondsToSpin)}", FontGroup.MyriadPro, 22f, RightPageCenterX, PageTop + Sz(200), UiAnchor.Middle, color: InkSoft, outline: 1));
        }

        if (_spinNote.Length > 0) {
            page.AddChild(Text(_spinNote, FontGroup.MyriadPro, 26f, RightPageCenterX, PageTop + Sz(262), UiAnchor.Middle, color: _spinNote.StartsWith("You won", StringComparison.Ordinal) ? Good : Bad, maxWidth: RightPageW - 30));
        }
    }

    private void StartSpin() {
        if (_daily == null || !_daily.SpinReady) {
            return;
        }

        RewardCall(AppRequests.SpinDailyWheel, call => {
            if (call.Result == null || call.Result.SegmentIndex < 0 || call.Result.SegmentIndex >= _spinChipCount) {
                _spinNote = call.Error ?? "Could not reach the server.";
                BuildDailySpinPage(_pages[(int) BookPage.DailySpin]);
                return;
            }

            BeginSpinAnimation(call.Result);
        });
    }

    // The server has already decided (and paid): this is only the show. Two and a bit laps that slow to a stop on the winning chip.
    private void BeginSpinAnimation(RewardResult result) {
        var page = _pages[(int) BookPage.DailySpin];
        _spinNote = string.Empty;
        _spinResult = result;

        // Rebuild once so the button is gone (nothing to click twice) - then freeze the page while it turns.
        _daily = new DailyData(_daily.GiftReady, _daily.GiftDay, _daily.Streak, false, _daily.Unread, _daily.SecondsToGift, _daily.SecondsToSpin, _daily.Gifts, _daily.Prizes);
        BuildDailySpinPage(page);

        var steps = _spinChipCount * 2 + result.SegmentIndex + 1;
        _spinStepTimes = new double[steps + 1];
        var t = 0.0;
        for (var k = 1; k <= steps; k++) {
            var f = k / (double) steps;
            t += 45 + 300 * Math.Pow(f, 2.6);      // quick at first, then slower and slower
            _spinStepTimes[k] = t;
        }

        _spinElapsedMs = 0;
        _spinStepShown = 0;
        _spinShownIndex = 0;
        _spinning = true;
        PlaceSpinHighlight(0);
        _spinHighlight.Visible = true;
    }

    private void PlaceSpinHighlight(int index) {
        _spinShownIndex = index % _spinChipCount;
        var (x, y) = _spinChipCenters[_spinShownIndex];
        _spinHighlight.X = x - (int) _spinHighlight.Width / 2;
        _spinHighlight.Y = y - (int) _spinHighlight.Height / 2;
    }

    private void UpdateSpin(double dt) {
        _spinElapsedMs += dt;
        var last = _spinStepTimes.Length - 1;
        while (_spinStepShown < last && _spinElapsedMs >= _spinStepTimes[_spinStepShown + 1]) {
            _spinStepShown++;
            PlaceSpinHighlight(_spinStepShown);
        }

        if (_spinStepShown < last) {
            return;
        }

        // Landed.
        _spinning = false;
        _spinHighlight.Alpha = 0.9f;
        ApplyBalance(_spinResult);
        _spinNote = "You won " + Describe(_spinResult.Paid) + "!";
        _daily = new DailyData(_daily.GiftReady, _daily.GiftDay, _daily.Streak, false, _daily.Unread, _daily.SecondsToGift, _daily.SecondsToSpin, _daily.Gifts, _daily.Prizes);
        BuildDailySpinPage(_pages[(int) BookPage.DailySpin]);
        UpdateTabBadges();
    }

    #endregion

    #region Tab badges

    private readonly ColorRect[] _badges = new ColorRect[6];

    // A small dot on a tab when there is something waiting: unread / unclaimed mail, an unopened daily gift, an unused spin.
    private void UpdateTabBadges() {
        SetBadge((int) BookPage.Inbox, _inbox != null && _inbox.Unread > 0);
        SetBadge((int) BookPage.DailyGift, _daily != null && _daily.GiftReady);
        SetBadge((int) BookPage.DailySpin, _daily != null && _daily.SpinReady);
    }

    private void SetBadge(int tabIndex, bool on) {
        if (_badges[tabIndex] == null) {
            _badges[tabIndex] = new ColorRect(new ColorRectConfig { Width = 12, Height = 12, Color = 0xD8392B, Alpha = 1f, X = 28, Y = 3 });
            _tabs[tabIndex].AddChild(_badges[tabIndex]);
        }

        _badges[tabIndex].Visible = on;
    }

    #endregion
}
