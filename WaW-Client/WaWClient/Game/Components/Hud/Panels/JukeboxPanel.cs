using WaWClient.Data;
using WaWClient.Game.Components.Options;
using WaWClient.Game.Music;
using WaWClient.Game.Objects;
using WaW.UiLib.BuiltIn;
using WaW.UiLib.Core;

namespace WaWClient.Game.Components.Hud.Panels;

// What appears at the bottom of the screen when you stand next to the Jukebox in the Nexus: the name, what is playing right now, and an Open button.
public class JukeboxPanel : Panel {

    private readonly SimpleText _nowPlaying;
    private int _shownSerial = -1;

    public JukeboxPanel(Entity entity) {
        var name = new SimpleText(new TextConfig {
            X = PanelWidth / 2,
            Y = 10,
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

        _nowPlaying = new SimpleText(new TextConfig {
            X = PanelWidth / 2,
            Y = name.Height + 14,
            Text = string.Empty,
            FontSize = 15,
            FontType = FontType.Normal,
            Color = WaWStyle.TextDim,
            OutlineColor = OptionsStyle.OutlineDark,
            OutlineThickness = 1,
            Anchor = UiAnchor.MiddleTop,
            MaxWidth = PanelWidth - 24
        });
        AddChild(_nowPlaying);

        const int buttonWidth = 120;
        var open = WaWStyle.TextButton("Open", buttonWidth, 36, 18f, OnInteractKey);
        open.X = PanelWidth / 2 - buttonWidth / 2;
        open.Y = name.Height + 56;
        AddChild(open);

        AddEventListener(Event.AddedToStage, () => AddEventListener(Event.EnterFrame, OnFrame));
        AddEventListener(Event.RemovedFromStage, () => RemoveEventListener(Event.EnterFrame, OnFrame));
    }

    protected override void OnInteractKey() => GameScreen.GameSprite?.OpenJukebox();

    private void OnFrame() {
        var state = InGameMusic.State;
        if (state == null || state.Serial == _shownSerial) {
            return;
        }

        _shownSerial = state.Serial;
        _nowPlaying.SetText($"Now playing: {state.Find(state.CurrentId).Title}");
    }
}
