using System;
using AlloyClient.Data;
using AlloyClient.Game.Components.Admin;
using AlloyClient.Game.Components.Options;
using AlloyClient.Game.Components.Hud.Panels;
using AlloyClient.Game.Objects;
using AlloyClient.Networking;
using AlloyClient.Networking.Packets.Outgoing;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;

namespace AlloyClient.Game.Components.Hud;

// The walnut frame at the bottom of the HUD, just right of the gear, that is ALWAYS there: the (up to six) closest other players - their name on the left and their class on
// the right - or a small "No players nearby!" when there are none. The list comes from PartyData (which every half second sorts the players the client knows by distance).
// Clicking a name opens a small menu above the frame with that player's class and level, and - for staff only - Mute / Kick shortcuts (the same chat commands as the admin
// dashboard; the server checks the rank). Trade, whisper and friend requests are not in the game yet (the server has no handlers for them), so there is nothing to offer for them.
public sealed class NearbyPlayersPanel : Sprite {
    public const int Width = Panel.PanelWidth;
    public const int Height = Panel.PanelHeight;

    private const int RowsTop = 12;
    private const int RowHeight = 17;
    private const int SideMargin = 8;
    private const int MenuGap = 6;
    private const float RowFontSize = 14f;

    private readonly Row[] _rows = new Row[PartyData.MaxVisibleMembers];
    private readonly SimpleText _empty;

    private PlayerMenu _menu;
    private int _menuPlayerId = -1;

    public NearbyPlayersPanel() {
        AddChild(OptionsStyle.Panel(Width, Height));

        for (var i = 0; i < _rows.Length; i++) {
            _rows[i] = new Row(this, RowsTop + i * RowHeight);
        }

        _empty = OptionsStyle.Label("No players nearby!", FontGroup.MyriadPro, RowFontSize, Width / 2, Height / 2, UiAnchor.Middle, OptionsStyle.Tan, 1);
        _empty.Alpha = 0.75f;
        AddChild(_empty);
    }

    // The screen area the open menu takes (design units, relative to the HUD), or null - it must not fire the weapon when clicked.
    public (float X, float Y, float W, float H)? MenuRect => _menu == null ? null : (X, Y - _menu.MenuHeight - MenuGap, Width, _menu.MenuHeight);

    public void Update() {
        var shown = 0;
        var members = PartyData.PartyMembers;
        for (var i = 0; i < _rows.Length && i < members.Count; i++) {
            var info = members[i];
            if (info == null || info.Player == null || string.IsNullOrEmpty(info.Player.Name)) {
                continue;
            }

            _rows[shown++].Show(info.Player);
        }

        for (var i = shown; i < _rows.Length; i++) {
            _rows[i].Clear();
        }

        _empty.Visible = shown == 0;

        // the menu goes away with the player it is about (left the area / the map)
        if (_menu != null && !Map.Players.ContainsKey(_menuPlayerId)) {
            CloseMenu();
        }

        foreach (var row in _rows) {
            row.SetSelected(_menu != null && row.PlayerId == _menuPlayerId);
        }
    }

    private void OnRowClicked(Player player) {
        if (_menu != null && _menuPlayerId == player.ObjectId) {
            CloseMenu();
            return;
        }

        CloseMenu();
        _menuPlayerId = player.ObjectId;
        _menu = new PlayerMenu(player, CloseMenu);
        _menu.Y = -_menu.MenuHeight - MenuGap;
        AddChild(_menu);
    }

    public void CloseMenu() {
        if (_menu != null) {
            RemoveChild(_menu);
        }

        _menu = null;
        _menuPlayerId = -1;
    }

    // One line of the list. Its pieces are children of the panel itself (not of a Container): a Container sizes itself from its children, which shifted the
    // right-aligned class text. The invisible ColorRect is the click / hover area and the row's highlight.
    private sealed class Row {
        private readonly ColorRect _hit;
        private readonly SimpleText _name;
        private readonly SimpleText _class;
        private readonly NearbyPlayersPanel _owner;
        private Player _player;
        private string _shownName;
        private string _shownClass;
        private bool _selected;
        private bool _hovered;

        public int PlayerId => _player?.ObjectId ?? -1;

        public Row(NearbyPlayersPanel owner, int y) {
            _owner = owner;

            _hit = new ColorRect(new ColorRectConfig { X = SideMargin, Y = y, Width = Width - 2 * SideMargin, Height = RowHeight - 1, Color = OptionsStyle.Tan, Alpha = 0f, MouseEnabled = true });
            _name = OptionsStyle.Label("", FontGroup.MyriadPro, RowFontSize, SideMargin + 6, y + RowHeight / 2, UiAnchor.MiddleLeft, OptionsStyle.Cream, 1);
            _class = OptionsStyle.Label("", FontGroup.MyriadPro, RowFontSize, Width - SideMargin - 6, y + RowHeight / 2, UiAnchor.MiddleRight, OptionsStyle.Tan, 1);
            owner.AddChild(_hit);
            owner.AddChild(_name);
            owner.AddChild(_class);

            _hit.AddEventListener(MouseEvent.MouseOver, () => { _hovered = true; Tint(); });
            _hit.AddEventListener(MouseEvent.MouseOut, () => { _hovered = false; Tint(); });
            _hit.AddEventListener(MouseEvent.LeftClick, () => {
                if (_player != null) {
                    _owner.OnRowClicked(_player);
                }
            });
            SetVisible(false);
        }

        private void SetVisible(bool visible) {
            _hit.Visible = visible;
            _name.Visible = visible;
            _class.Visible = visible;
        }

        public void Show(Player player) {
            _player = player;
            SetVisible(true);

            if (_shownName != player.Name) {
                _shownName = player.Name;
                _name.SetText(player.Name);
            }

            var cls = player.Properties?.DisplayName ?? string.Empty;
            if (_shownClass != cls) {
                _shownClass = cls;
                _class.SetText(cls);
            }
        }

        public void Clear() {
            _player = null;
            _hovered = false;
            SetVisible(false);
        }

        public void SetSelected(bool selected) {
            if (_selected != selected) {
                _selected = selected;
                Tint();
            }
        }

        private void Tint() => _hit.Alpha = _selected ? 0.24f : _hovered ? 0.14f : 0f;
    }

    // The little menu above the frame.
    private sealed class PlayerMenu : Sprite {
        private const int Pad = 12;

        public int MenuHeight { get; }

        public PlayerMenu(Player player, Action close) {
            var staff = AdminRules.IsStaff(GlobalData.Get<AccountData>()?.Rank ?? 0);
            var kick = staff ? AdminRules.Kick(player.Name) : null;
            var mute = staff ? AdminRules.Mute(player.Name, "30m", null) : null;
            var hasActions = kick != null && mute != null;

            MenuHeight = hasActions ? 112 : 62;
            AddChild(OptionsStyle.Panel(Width, MenuHeight));

            AddChild(OptionsStyle.Label(player.Name, FontGroup.MyriadPro, 20f, Pad, 10, UiAnchor.LeftTop, WaWStyle.Highlight, 1));
            var cls = player.Properties?.DisplayName ?? string.Empty;
            AddChild(OptionsStyle.Label(cls.Length > 0 ? $"{cls}  -  Level {player.Level}" : $"Level {player.Level}", FontGroup.MyriadPro, 14f, Pad, 36, UiAnchor.LeftTop, OptionsStyle.Tan, 1));

            var closeButton = WaWStyle.CloseButton(close);
            closeButton.X = Width - closeButton.Width - 10;
            closeButton.Y = 8;
            AddChild(closeButton);

            if (hasActions) {
                const int buttonWidth = 100;
                var muteButton = WaWStyle.TextButton("Mute 30m", buttonWidth, 34, 16f, () => Do(mute, close));
                muteButton.X = Pad;
                muteButton.Y = 66;
                AddChild(muteButton);

                var kickButton = WaWStyle.TextButton("Kick", buttonWidth, 34, 16f, () => Do(kick, close));
                kickButton.X = Pad + buttonWidth + 8;
                kickButton.Y = 66;
                AddChild(kickButton);
            }
        }

        private static void Do(string command, Action close) {
            var packet = PlayerText.CreatePacket();
            packet.Text = command;
            Client.QueuePacket(packet);
            close();
        }
    }
}
