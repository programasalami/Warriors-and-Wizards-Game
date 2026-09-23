using WaWClient.Game.Components.Options;
using WaWClient.Game.Objects;
using WaWClient.Ui;
using WaWClient.Utils;
using WaW.UiLib.BuiltIn;
using WaW.UiLib.Core;
using Common.Structs;

namespace WaWClient.Game.Components.Hud;

// The top-left plate: one framed panel holding everything about your character - your class's skin in the portrait frame, your name, the health / mana / level bars, and your gold and fame.
public sealed class PlayerPlate : Sprite {
    public const int Width = 356;
    private const int PortraitScale = 4;
    private const int PortraitSize = 22 * PortraitScale;                  // the portrait frame's art is 22x22 pixels
    private const int PortraitX = 14;
    private const int RightX = PortraitX + PortraitSize + 12;             // where the plaque and the bars start
    private const int RightWidth = Width - RightX - 14;

    // no name row any more: four bars of the SAME size (HP, MP, level, fame), then the gold and fame amounts. Every row is the same distance from the next
    // (RowGap) and the top and bottom margins match, so no gap is odd. The Height follows from these numbers - change a row and it stays even.
    private const int BarExtraRows = 2;      // the bars' frames use up some of their height, so each is a little taller than the art's 3-row interior
    private const int BarHeight = (6 + BarExtraRows) * WaWStyle.BarScale;      // HudBar's art is 6 rows tall
    private const int MoneyHeight = 22;      // the coin icons
    private const int RowGap = 6;
    private const int EdgeMargin = 12;
    private const int HpY = EdgeMargin;
    private const int MpY = HpY + BarHeight + RowGap;
    private const int XpY = MpY + BarHeight + RowGap;
    private const int FameY = XpY + BarHeight + RowGap;
    private const int MoneyY = FameY + BarHeight + RowGap;

    public const int Height = MoneyY + MoneyHeight + EdgeMargin;

    private readonly ObjectRect _skin;
    private readonly HudBar _hp;
    private readonly HudBar _mp;
    private readonly HudBar _xp;
    private readonly HudBar _fame;
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

        _hp = new HudBar(RightWidth, WaWStyle.BarScale, WaWStyle.Red, 0x3A1B40, "HP", extraRows: BarExtraRows) { X = RightX, Y = HpY };
        _mp = new HudBar(RightWidth, WaWStyle.BarScale, WaWStyle.Teal, 0x2A2942, "MP", extraRows: BarExtraRows) { X = RightX, Y = MpY };
        _xp = new HudBar(RightWidth, WaWStyle.BarScale, WaWStyle.Olive, 0x402E2B, "Lvl X", extraRows: BarExtraRows) { X = RightX, Y = XpY };
        _fame = new HudBar(RightWidth, WaWStyle.BarScale, WaWStyle.Orange, 0x402E2B, "Fame", extraRows: BarExtraRows) { X = RightX, Y = FameY };
        AddChild(_hp);
        AddChild(_mp);
        AddChild(_xp);
        AddChild(_fame);

        // gold and fame: the same two coins the menus use (the gold coin and the copper coin of the Character Book), with the amounts beside them
        var coin = CurrencyIcon("Console/GoldCoin0");
        coin.X = RightX;
        coin.Y = MoneyY;
        AddChild(coin);
        _goldText = OptionsStyle.Label("0", FontGroup.MyriadPro, 19.5f, RightX + coin.Width + 8, MoneyY + MoneyHeight / 2 - 2, UiAnchor.MiddleLeft, WaWStyle.Highlight, 1);
        AddChild(_goldText);

        var star = CurrencyIcon("Console/CopperCoin0");
        star.X = RightX + RightWidth / 2 + 6;
        star.Y = MoneyY;
        AddChild(star);
        _fameText = OptionsStyle.Label("0", FontGroup.MyriadPro, 19.5f, star.X + star.Width + 8, MoneyY + MoneyHeight / 2 - 2, UiAnchor.MiddleLeft, WaWStyle.Highlight, 1);
        AddChild(_fameText);

        Map.OnPlayerUpdate.Add(OnPlayerUpdate);
    }

    // 32x32 coin art drawn at a fixed 22 px so the plate's rows keep their height (menus draw the same art at 26 px).
    private static ObjectRect CurrencyIcon(string lookup) => new(new ObjectRectConfig {
        Texture = TextureHelper.FromUiAtlas(lookup, 0, false),
        Width = MoneyHeight,
        Height = MoneyHeight,
        OutlineEnabled = false,
        GlowEnabled = false
    });

    private void OnPlayerUpdate(Player player) {
        _skin.ChangeTexture(TextureHelper.Create(player.TextureData.AnimatedTextures.FaceRight[0], TextureType.GameAtlas));
    }

    public void Update() {
        var player = Map.LocalPlayer;

        // Both bars are always there: the level bar (levels go up to LevelRules.MaxLevel, 999) and, under it, the fame bar. (The original game turned the level bar
        // into the fame bar at level 20; with 999 levels most players would never see fame.) At the highest level the level bar stays full.
        if (player.Level != _lastLevel) {
            _lastLevel = player.Level;
            _xp.UpdateLabel($"Lvl {player.Level}");
        }

        if (LevelRules.IsMaxLevel(player.Level)) {
            _xp.SetRatio(1f);
        } else {
            _xp.Update(player.Experience, player.NextLevelExp);
        }

        _fame.Update(player.CurrentFame, player.FameGoal);

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
