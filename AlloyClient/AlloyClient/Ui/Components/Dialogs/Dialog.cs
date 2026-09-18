using System;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using AlloyClient.Ui.Components.Buttons;

namespace AlloyClient.Ui.Components.Dialogs;

public sealed record DialogOption(string Text, Action Callback = null);

public enum DialogState {
    Active = 0,
    Closed = 1,
    Finished = 2
}

public class Dialog : UiElement {

    private const int BoxWidth = 300;

    // Horizontal margin the message text wraps within, so a message close to (or longer than)
    // BoxWidth wraps onto a second line instead of running past the box's edges - the box itself
    // stays a fixed size (BoxWidth, and a boxHeight computed below), never grown to fit content;
    // content wraps/fits within it instead, per this codebase's usual convention.
    private const int TextSideMargin = 20;

    public DialogState State = DialogState.Active;

    public Dialog(string title, string message, DialogOption confirm, DialogOption cancel = null) {
        X = Settings.DefaultScreenWidth / 2;
        Y = Settings.DefaultScreenHeight / 2;
        SetAnchor(UiAnchor.Middle);

        var boxConfig = new ColorRectConfig { Width = BoxWidth, Height = 75, Color = 0x1C1C1C, Alpha = 0.8f, Anchor = UiAnchor.LeftTop};
        var box = new ColorRect(boxConfig);
        AddChild(box);

        var titleConfig = new TextConfig { Text = title, FontSize = 24, FontType = FontType.Bold, Color = 0xFFFFFF, Anchor = UiAnchor.MiddleTop};
        var titleText = new SimpleText(titleConfig);

        var messageConfig = new TextConfig { Text = message, FontSize = 20, Color = 0xFFFFFF, Anchor = UiAnchor.MiddleTop, MaxWidth = BoxWidth - TextSideMargin * 2 };
        var messageText = new SimpleText(messageConfig);

        var confirmConfig = new TextButtonConfig { Text = confirm.Text, FontSize = 22, OnClicked = () => { confirm.Callback?.Invoke(); State = DialogState.Closed; }, Anchor = UiAnchor.MiddleBottom};
        var confirmButton = new TextButton(confirmConfig);

        // boxHeight/every position below is derived from the literal BoxWidth constant and each
        // element's own Height, never by reading box.Width/box.Height back. box.ContentWidth is
        // the union of every child's bounds at the moment each one is added (see
        // DisplayContainer.UpdateBounds) - titleText/messageText/confirmButton used to get
        // box.AddChild'ed while still sitting at their default (0,0) position, and their
        // Middle-anchored offsets extend into negative territory from there, so that stale bounds
        // union permanently inflated box.ContentWidth/Height (and, since that propagates to
        // Parent.UpdateBounds too, this Dialog's own centering size) well past the box's actual
        // fixed, rendered 300-wide size - which is exactly why buttons/text used to land outside
        // or off-center from the visible box. Fixed by giving each child its final X/Y BEFORE
        // adding it to box, so UpdateBounds only ever unions their real, intended position.
        var boxHeight = titleText.Height + 10 + messageText.Height + 20 + confirmButton.Height + 10;
        box.Resize(BoxWidth, boxHeight);

        titleText.X = BoxWidth / 2;
        titleText.Y = 10;
        box.AddChild(titleText);

        messageText.X = BoxWidth / 2;
        messageText.Y = titleText.Height + 20;
        box.AddChild(messageText);

        confirmButton.Y = boxHeight - 10;
        if (cancel != null) {
            confirmButton.X = 3 * BoxWidth / 4;
            box.AddChild(confirmButton);

            var cancelConfig = new TextButtonConfig { Text = cancel.Text, FontSize = 22, OnClicked = () => {cancel.Callback?.Invoke(); State = DialogState.Closed; }, Anchor = UiAnchor.MiddleBottom };
            var cancelButton = new TextButton(cancelConfig);
            cancelButton.Y = boxHeight - 10;
            cancelButton.X = BoxWidth / 4;
            box.AddChild(cancelButton);
        } else {
            confirmButton.X = BoxWidth / 2;
            box.AddChild(confirmButton);
        }
    }

    protected override void OnResize(ResizeEvent args) {
        Scale = Stage.ScreenScale;
        X = args.Width / 2;
        Y = args.Height / 2;
    }
}