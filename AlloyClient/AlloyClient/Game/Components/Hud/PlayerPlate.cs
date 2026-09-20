using AlloyClient.Game.Components.Options;
using AlloyClient.Game.Objects;
using AlloyClient.Ui;
using AlloyClient.Utils;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;

namespace AlloyClient.Game.Components.Hud;

// The top-left plate: one framed panel holding everything about your character - your class's skin in the portrait frame, your name on
// a plaque, the health / mana / level bars, and your gold and fame.
public sealed class PlayerPlate : Sprite {
    public const int Width = 356;
    public const int Height = 148;

    private const int PortraitScale = 4;
    private const int PortraitSize = 22 * PortraitScale;                  // the portrait frame's art is 22x22 pixels
    private const int PortraitX = 14;
    private const int RightX = PortraitX + PortraitSize + 12;             // where the plaque and the bars start
    private const int RightWidth = Width - RightX - 14;

    private const int PlaqueY = 14;
    private const int PlaqueHeight = 18;
    private const int HpY = 38;
    private const int MpY = 66;
    private const int XpY = 94;
    private const int MoneyY = 118;
    private const int BarExtraRows = 2;      // the bars' frames use up some of their height, so each is a little taller than the art's 3-row interior

    private readonly ObjectRect _skin;
    private readonly HudBar _hp;
    private readonly HudBar _mp;
    private readonly HudBar _xp;
    private readonly HudBar _fame;
    private readonly SimpleText _name;
    private readonly SimpleText _goldText;
    private readonly SimpleText _fameText;

    private int _lastLevel = -1;
    private int _lastGold = int.MinValue;
    private int _lastFame = int.MinValue;

    public PlayerPlate() {
        AddChild(WaWStyle.Panel(Width, Height));

        // portrait: the frame first (its interior is a dark fill), then the class skin on top, inside the frame's 3-pixel border
        var portraitY = (Height - PortraitSize) / 2;
        var frame = WaWStyle.Scaled(SliceLibrary.WaWPortrait, 7, 7, PortraitSize, PortraitSize, PortraitScale);
        frame.X = PortraitX;
        frame.Y = portraitY;
        AddChild(frame);

        var inner = PortraitSize - 6 * PortraitScale;
        _skin = new ObjectRect(new ObjectRectConfig {
            Width = inner,
            Height = inner,
            X = PortraitX + 3 * PortraitScale,
            Y = portraitY + 3 * PortraitScale,
            OutlineEnabled = false,
            GlowEnabled = false
        });
        AddChild(_skin);

        // the name on a dark plaque (no frame around it)
        AddChild(new ColorRect(new ColorRectConfig { X = RightX, Y = PlaqueY, Width = RightWidth, Height = PlaqueHeight, Color = WaWStyle.Ink }));
        _name = OptionsStyle.Label("", FontGroup.MyriadPro, 15f, RightX + 10, PlaqueY + PlaqueHeight / 2, UiAnchor.MiddleLeft, WaWStyle.Highlight, 1);
        AddChild(_name);

        _hp = new HudBar(RightWidth, WaWStyle.BarScale, WaWStyle.Red, 0x3A1B40, "HP", extraRows: BarExtraRows) { X = RightX, Y = HpY };
        _mp = new HudBar(RightWidth, WaWStyle.BarScale, WaWStyle.Teal, 0x2A2942, "MP", extraRows: BarExtraRows) { X = RightX, Y = MpY };
        _xp = new HudBar(RightWidth, 2, WaWStyle.Olive, 0x402E2B, "Lvl X", extraRows: BarExtraRows) { X = RightX, Y = XpY };
        _fame = new HudBar(RightWidth, 2, WaWStyle.Orange, 0x402E2B, "Fame", extraRows: BarExtraRows) { X = RightX, Y = XpY };
        _fame.Visible = false;
        AddChild(_hp);
        AddChild(_mp);
        AddChild(_xp);
        AddChild(_fame);

        // gold and fame: the pack's coin and star with the amounts beside them
        var coin = WaWStyle.Icon("WaW/Coin", 6, 6, 3);
        coin.X = RightX;
        coin.Y = MoneyY + 1;
        AddChild(coin);
        _goldText = OptionsStyle.Label("0", FontGroup.MyriadPro, 17.5f, RightX + coin.Width + 8, MoneyY + 10, UiAnchor.MiddleLeft, WaWStyle.Highlight, 1);
        AddChild(_goldText);

        var star = WaWStyle.Icon("WaW/Star", 7, 7, 3);
        star.X = RightX + RightWidth / 2 + 6;
        star.Y = MoneyY - 1;
        AddChild(star);
        _fameText = OptionsStyle.Label("0", FontGroup.MyriadPro, 17.5f, star.X + star.Width + 8, MoneyY + 10, UiAnchor.MiddleLeft, WaWStyle.Highlight, 1);
        AddChild(_fameText);

        Map.OnPlayerUpdate.Add(OnPlayerUpdate);
    }

    private void OnPlayerUpdate(Player player) {
        _name.SetText(player.Name);
        _skin.ChangeTexture(TextureHelper.Create(player.TextureData.AnimatedTextures.FaceRight[0], TextureType.GameAtlas));
    }

    public void Update() {
        var player = Map.LocalPlayer;

        // at level 20 the experience bar becomes the fame bar
        if (!_fame.Visible && player.Level == 20) {
            _xp.Visible = false;
            _fame.Visible = true;
        }

        if (_xp.Visible) {
            if (player.Level != _lastLevel) {
                _lastLevel = player.Level;
                _xp.UpdateLabel($"Lvl {player.Level}");
            }

            _xp.Update(player.Experience, player.NextLevelExp);
        }

        if (_fame.Visible) {
            _fame.Update(player.CurrentFame, player.FameGoal);
        }

        _hp.Update(player.Hp, player.MaxHp, player.MaxHpBoost, 250, player.Level);
        _mp.Update(player.Mp, player.MaxMp, player.MaxMpBoost, 250, player.Level);

        if (player.Credits != _lastGold) {
            _lastGold = player.Credits;
            _goldText.SetText(player.Credits.ToString("N0"));
        }

        if (player.CurrentFame != _lastFame) {
            _lastFame = player.CurrentFame;
            _fameText.SetText(player.CurrentFame.ToString("N0"));
        }
    }
}
