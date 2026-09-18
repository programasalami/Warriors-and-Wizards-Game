using AlloyClient.Game.Objects;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using AlloyClient.Utils;

namespace AlloyClient.Game.Components.Hud;

public sealed class CharacterDetails : Sprite {
    
    private readonly ObjectRect _skin;
    private readonly SimpleText _name;

    public CharacterDetails() {
        // Was 30x30 with outline/glow left on their (GameAtlas-textured ObjectRects default to
        // both enabled) - fine against the old 8x8-cell Players.png sprites, which had almost no
        // internal detail for the neighbor-sampling outline/glow effect (see Ui.frag's
        // RenderOutline, same class of dFdx/dFdy-driven effect as the Object.frag glow bug this
        // GPU already choked on once, see CLAUDE.md) to false-trigger on. The new, much more
        // detailed 32x32-cell art gave it plenty of small internal edges to catch instead,
        // reading as "unviewable" noise at this size - disabled here the same way every other
        // small icon added this session already does. Bumped 30->40 too since it was cramped
        // next to 25pt name text even before that.
        _skin = new ObjectRect(new ObjectRectConfig {
            Width = 40,
            Height = 40,
            OutlineEnabled = false,
            GlowEnabled = false
        });
        _skin.Y = 4;
        _skin.X = 4;
        AddChild(_skin);
        _name = new SimpleText(new TextConfig {
            Text = "test",
            FontSize = 25,
            FontType = FontType.Bolder,
            X = 50,
            Y = 20,
            Color = 0xdadada,
            OutlineThickness = 0,
            OutlineColor = 0,
            Anchor = UiAnchor.MiddleLeft
        });
        AddChild(_name);
        
        Map.OnPlayerUpdate.Add(OnPlayerUpdate);
    }

    private void OnPlayerUpdate(Player player) {
        _name.SetText(player.Name);
        _skin.ChangeTexture(TextureHelper.Create(player.TextureData.AnimatedTextures.FaceRight[0], TextureType.GameAtlas));
    }
}