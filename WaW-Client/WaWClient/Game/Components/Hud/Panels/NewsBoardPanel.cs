using WaWClient.Game.Components.Options;
using WaWClient.Game.Objects;
using WaW.UiLib.BuiltIn;
using WaW.UiLib.Core;

namespace WaWClient.Game.Components.Hud.Panels;

// What appears at the bottom of the screen when you stand next to the News Board in the Nexus (2026-09-21): same shape as the Bug Board's panel.
public class NewsBoardPanel : Panel {

    public NewsBoardPanel(Entity entity) {
        var name = new SimpleText(new TextConfig {
            X = PanelWidth / 2,
            Y = 12,
            Text = entity.Properties.DisplayName,
            FontSize = 22,
            FontType = FontType.Bold,
            Color = WaWStyle.Highlight,
            OutlineColor = OptionsStyle.OutlineDark,
            OutlineThickness = 1,
            Anchor = UiAnchor.MiddleTop,
            MaxWidth = PanelWidth - 16
        });
        AddChild(name);

        AddChild(new SimpleText(new TextConfig {
            X = PanelWidth / 2,
            Y = name.Height + 18,
            Text = "What changed in the latest updates.",
            FontSize = 15,
            FontType = FontType.Normal,
            Color = WaWStyle.TextDim,
            OutlineColor = OptionsStyle.OutlineDark,
            OutlineThickness = 1,
            Anchor = UiAnchor.MiddleTop,
            MaxWidth = PanelWidth - 24
        }));

        const int buttonWidth = 120;
        var open = WaWStyle.TextButton("Read", buttonWidth, 36, 18f, OnInteractKey);
        open.X = PanelWidth / 2 - buttonWidth / 2;
        open.Y = name.Height + 56;
        AddChild(open);
    }

    protected override void OnInteractKey() => GameScreen.GameSprite?.OpenNewsBoard();
}
