using AlloyClient.AppEngine;
using AlloyClient.Display;
using AlloyClient.Ui;
using AlloyClient.Ui.Components.Dialogs;
using AlloyClient.Ui.Components.Panels;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using Alloy.UiLib.Extra;
using AlloyClient.Ui.Components.Buttons;

namespace AlloyClient.Screens.Components.Containers;

public class RegisterContainer : Overlay {

    private const int FrameWidth = 480;
    private const int FrameHeight = 400;
    private const int FrameCutX = 14;
    private const int FrameCutY = 28;

    private readonly TextInput _usernameInput;
    private readonly TextInput _passwordInput;

    public RegisterContainer() {
        SetAnchor(UiAnchor.Middle);

        var background = new NineSliceRect(new NineSliceConfig {
            SliceData = SliceLibrary.LoginFrame,
            CutX = FrameCutX,
            CutY = FrameCutY,
            Width = FrameWidth,
            Height = FrameHeight
        });
        AddChild(background);

        var title = new SimpleText(new TextConfig { Text = "Register", FontSize = 26, FontType = FontType.Bold, X = Width / 2, Y = 45, Color = 0xFFFFFF, OutlineColor = 0x000000, OutlineThickness = 4, Anchor = UiAnchor.Middle });
        AddChild(title);

        var emailConfig = new InputConfig { X = Width / 2, Y = 115, FontSize = 24, FontType = FontType.Bold, Color = 0xFFFFFF, Width = 350, DefaultText = "Username", Anchor = UiAnchor.Middle };
        _usernameInput = new TextInput(emailConfig);
        AddChild(_usernameInput);

        var passwordConfig = new InputConfig { X = Width / 2, Y = 175, FontSize = 24, FontType = FontType.Bold, Color = 0xFFFFFF, Width = 350, DefaultText = "Password", Password = true, Anchor = UiAnchor.Middle };
        _passwordInput = new TextInput(passwordConfig);
        AddChild(_passwordInput);

        var registerConfig = new TextButtonConfig { Text = "Existing user? Click here to login!", FontSize = 16, OnClicked = () => { OverlayManager.Set(new LoginContainer()); }, FontType = FontType.Bold, X = Width / 2, Y = _passwordInput.Y + 40, Anchor = UiAnchor.Middle };
        var registerButton = new TextButton(registerConfig);
        AddChild(registerButton);

        var loginConfig = new TextButtonConfig { Text = "Create", FontSize = 28, OnClicked = OnRegister, FontType = FontType.Normal, X = Width - 25, Y = Height - 25, Anchor = UiAnchor.RightBottom };
        var loginButton = new TextButton(loginConfig);
        AddChild(loginButton);

        var cancelConfig = new TextButtonConfig { Text = "Cancel", FontSize = 28, OnClicked = CloseOverlay, FontType = FontType.Normal, X = loginButton.X - loginButton.Width - 35, Y = Height - 25, Anchor = UiAnchor.RightBottom };
        var cancelButton = new TextButton(cancelConfig);
        AddChild(cancelButton);
    }

    private void OnRegister() {
        AddEventListener(AppRequests.Register(_usernameInput.Text, _passwordInput.Text), OnLoginResponse);
    }

    private void OnLoginResponse(AppResponse response) {
        if (!response.Success) {
            var dialog = new Dialog("Register Error", response.Message, new DialogOption("Ok"));
            DialogManager.Enqueue(dialog);
            return;
        }


        CloseOverlay();
        ScreenManager.FadeToScreen(new TitleScreen(), Easing.SineInOut, 500, 0x0);
    }
}
