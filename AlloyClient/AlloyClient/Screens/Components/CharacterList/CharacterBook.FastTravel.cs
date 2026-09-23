using System;
using System.Threading.Tasks;
using AlloyClient.AppEngine;
using AlloyClient.Data;
using AlloyClient.Utils;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using Alloy.UiLib.Extra;

namespace AlloyClient.Screens.Components.CharacterList;

// The SPAWN LOCATION page (tab three, 2026-09-21; "FastTravel" in code - the in-game fast travel feature planned later is a different thing). Left page: the server list (one server for now: the one the client is built for, with its
// live player count from the public API). Right page: where PLAY drops the character - Nexus, Vault, or the Guild Hall when the account is in a
// guild - as tiles with a small preview of each map. The highlighted tile is saved in Settings.FastTravel and read by Client.SendHello.
public sealed partial class CharacterBook {

    private const int TravelTileW = 112;
    private const int TravelTileH = 138;
    private const int TravelTileGap = 12;
    private const int TravelPreviewSize = 96;

    private int _onlineCount = -1;
    private bool _onlineLoading;

    private void RefreshOnlineCount() {
        if (_onlineLoading) {
            return;
        }

        _onlineLoading = true;
        _ = LoadOnlineAsync();
    }

    private async Task LoadOnlineAsync() {
        PortalRequests.Result<PortalOnline> result = default;
        try {
            result = await PortalRequests.GetOnline();
        } catch (Exception) {
            // shown as "?" below
        }

        _uiQueue.Enqueue(() => {
            _onlineLoading = false;
            if (result.Data != null) {
                _onlineCount = result.Data.Online;
            }

            BuildFastTravelPage(_pages[(int) BookPage.FastTravel]);
        });
    }

    private void BuildFastTravelPage(Container page) {
        page.RemoveChildren();

        // ---- left page: the servers ---------------------------------------------------------------------------------------------------------
        page.AddChild(Text("SERVERS", FontGroup.MyriadPro, TitleSize, LeftPageCenterX, PageTop + Sz(16), UiAnchor.Middle));
        page.AddChild(Rule(LeftPageCenterX, PageTop + Sz(46), LeftPageW - Sz(40)));

        var servers = GlobalData.Get<ServerListData>()?.ServerList ?? [];
        var rowX = LeftPageX + Sz(10);
        var rowW = LeftPageW - Sz(20);
        var y = PageTop + Sz(60);
        // No address on the row: the server's IP and port are the client's business, not something to print for everyone.
        if (servers.Length == 0) {
            page.AddChild(ServerRow("Game Server", rowX, y, rowW));
        } else {
            foreach (var server in servers) {
                page.AddChild(ServerRow(server.Name, rowX, y, rowW));
                y += 64;
            }
        }

        // ---- right page: where PLAY takes you ----------------------------------------------------------------------------------------------
        page.AddChild(Text("SPAWN LOCATION", FontGroup.MyriadPro, TitleSize, RightPageCenterX, PageTop + Sz(16), UiAnchor.Middle));
        page.AddChild(Rule(RightPageCenterX, PageTop + Sz(46), RightPageW - Sz(40)));
        page.AddChild(Text("Choose the world that you want to load your character into when you click play. It will stay that world until you change it.",
            FontGroup.MyriadPro, SmallSize, RightPageCenterX, PageTop + Sz(56), UiAnchor.MiddleTop, color: InkSoft, maxWidth: RightPageW - 24, outline: 1));

        // Every destination is shown; one the account cannot use (the Guild Hall without a guild) is darkened and not clickable. The server
        // refuses it too, so nothing depends on the tile alone.
        var selected = FastTravel.Resolve(Settings.FastTravel.Value, _account);
        var count = FastTravel.Destinations.Length;
        var tilesW = count * TravelTileW + (count - 1) * TravelTileGap;
        var tileX = RightPageCenterX - tilesW / 2;
        var tileY = PageTop + Sz(108);
        foreach (var d in FastTravel.Destinations) {
            page.AddChild(TravelTile(d, d.Key == selected.Key, !FastTravel.Available(d, _account), tileX, tileY));
            tileX += TravelTileW + TravelTileGap;
        }
    }

    // One server: name and players online; drawn as the selected row since there is nothing else to pick.
    private Container ServerRow(string name, int x, int y, int width) {
        const int height = 56;
        var row = new Container { X = x, Y = y };
        row.AddChild(new ColorRect(new ColorRectConfig { Width = width, Height = height, Color = TileSelected, Alpha = 0.55f }));
        row.AddChild(Text(name.ToUpperInvariant(), FontGroup.MyriadPro, 24f, 14, 18, UiAnchor.MiddleLeft, outline: 1, maxWidth: width - 130));
        var players = _onlineCount < 0 ? (_onlineLoading ? "..." : "?") : _onlineCount.ToString("N0");
        row.AddChild(Text(players, FontGroup.MyriadPro, 26f, width - 14, 20, UiAnchor.MiddleRight, outline: 1));
        row.AddChild(Text(_onlineCount == 1 ? "player online" : "players online", FontGroup.MyriadPro, 13f, width - 14, height - 16, UiAnchor.MiddleRight, color: InkSoft, outline: 1));
        row.AddChild(Text("SELECTED", FontGroup.MyriadPro, 11f, width / 2, height - 10, UiAnchor.Middle, color: InkSoft, outline: 1));
        return row;
    }

    private Container TravelTile(FastTravelDestination d, bool selected, bool locked, int x, int y) {
        var tile = new Container { X = x, Y = y };
        tile.MouseEnabled = !locked;

        var frame = new ColorRect(new ColorRectConfig { Width = TravelTileW, Height = TravelTileH, Color = selected ? TileSelected : Ink, Alpha = selected ? 0.9f : 0.35f });
        tile.AddChild(frame);
        tile.AddChild(new ColorRect(new ColorRectConfig { Width = TravelTileW - 4, Height = TravelTileH - 4, Color = selected ? TileSelected : TileIdle, Alpha = selected ? 0.6f : 0.2f }) { X = 2, Y = 2 });

        tile.AddChild(new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.FromUiAtlas(d.Preview, 0, false),
            X = TravelTileW / 2,
            Y = 8 + TravelPreviewSize / 2,
            Width = TravelPreviewSize,
            Height = TravelPreviewSize,
            Anchor = UiAnchor.Middle,
            OutlineEnabled = false,
            GlowEnabled = false
        }));
        tile.AddChild(Text(d.Name, CharacterFont, 16f, TravelTileW / 2, TravelTileH - 16, UiAnchor.Middle, color: locked ? InkSoft : Ink, maxWidth: TravelTileW - 6, outline: 1));

        if (locked) {
            // Darkened out: a dark sheet over the whole tile, and no mouse handling at all.
            tile.AddChild(new ColorRect(new ColorRectConfig { Width = TravelTileW, Height = TravelTileH, Color = Ink, Alpha = 0.62f }));
            return tile;
        }

        var down = false;
        tile.AddEventListener(MouseEvent.MouseOver, () => { if (!selected) frame.Alpha = 0.6f; });
        tile.AddEventListener(MouseEvent.MouseOut, () => { if (!selected) frame.Alpha = 0.35f; });
        tile.AddEventListener(MouseEvent.LeftDown, () => down = true);
        tile.AddEventListener(MouseEvent.LeftUp, () => {
            if (down && !_flipping && _entranceComplete) {
                Settings.FastTravel.Value = d.Key;
                Settings.SaveSettings();
                BuildFastTravelPage(_pages[(int) BookPage.FastTravel]);
            }

            down = false;
        });
        return tile;
    }
}
