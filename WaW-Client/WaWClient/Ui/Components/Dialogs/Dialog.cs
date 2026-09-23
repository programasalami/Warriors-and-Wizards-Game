using System;
using WaW.UiLib.BuiltIn;
using WaW.UiLib.Core;
using WaWClient.Game.Components.Options;

namespace WaWClient.Ui.Components.Dialogs;

public sealed record DialogOption(string Text, Action Callback = null);

public enum DialogState {
    Active = 0,
    Closed = 1,
    Finished = 2
}

public class Dialog : UiElement {

    // The in-game look since 2026-09-23 (it was a plain grey box): the Options window's gem-cornered brown panel, a gold title, cream text
    // and parchment-scroll buttons. FIXED size - never grown from the message (the user's rule); the message wraps inside TextWidth, and the
    // longest one we show (the web "out of date" text, ~200 characters) takes five lines.
    private const int BoxWidth = 500;
    private const int BoxHeight = 270;
    private const int TextSideMargin = 36;
    private const int TextWidth = BoxWidth - TextSideMargin * 2;
    private const int TitleY = 40;
    private const int MessageY = 74;
    private const int ButtonWidth = 170;
    private const int ButtonHeight = 44;
    private const int ButtonY = BoxHeight - ButtonHeight - 26;

    public DialogState State = DialogState.Active;

    public Dialog(string title, string message, DialogOption confirm, DialogOption cancel = null) {
        X = Settings.DefaultScreenWidth / 2;
        Y = Settings.DefaultScreenHeight / 2;
        SetAnchor(UiAnchor.Middle);

        // Every child gets its final position BEFORE it is added, so the bounds union never includes a stale (0,0) spot (that once pushed
        // the buttons outside the box).
        var panel = OptionsStyle.Panel(BoxWidth, BoxHeight);
        AddChild(panel);

        AddChild(OptionsStyle.Label(title.ToUpperInvariant(), FontGroup.MyriadPro, 28f, BoxWidth / 2, TitleY, UiAnchor.Middle, OptionsStyle.Gold, 2));

        AddChild(new SimpleText(new TextConfig {
            Text = message,
            FontSize = 19,
            FontType = FontType.Bold,
            FontGroup = FontGroup.MyriadPro,
            Color = OptionsStyle.Cream,
            OutlineColor = OptionsStyle.OutlineDark,
            OutlineThickness = 1,
            MaxWidth = TextWidth,
            X = BoxWidth / 2,
            Y = MessageY,
            Anchor = UiAnchor.MiddleTop
        }));

        if (cancel != null) {
            // cancel on the left, confirm on the right (as before)
            AddChild(Button(cancel, BoxWidth / 4 - ButtonWidth / 2 + 12));
            AddChild(Button(confirm, 3 * BoxWidth / 4 - ButtonWidth / 2 - 12));
        } else {
            AddChild(Button(confirm, BoxWidth / 2 - ButtonWidth / 2));
        }
    }

    private ParchmentButton Button(DialogOption option, int x) {
        return new ParchmentButton(option.Text.ToUpperInvariant(), option.Text, ButtonWidth, ButtonHeight, 22f, () => {
            option.Callback?.Invoke();
            State = DialogState.Closed;
        }) { X = x, Y = ButtonY };
    }

    protected override void OnResize(ResizeEvent args) {
        Scale = Stage.ScreenScale;
        X = args.Width / 2;
        Y = args.Height / 2;
    }
}