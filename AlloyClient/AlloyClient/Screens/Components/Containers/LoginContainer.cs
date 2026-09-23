using AlloyClient.AppEngine;
using AlloyClient.Display;
using AlloyClient.Ui;
using AlloyClient.Ui.Components.Dialogs;
using AlloyClient.Ui.Components.Panels;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using AlloyClient.Ui.Components.Buttons;

namespace AlloyClient.Screens.Components.Containers;

public class LoginContainer : Overlay {

    public static readonly EventType<Event> LoginEvent = "loginSuccess";

    private const int FrameWidth = 480;
    private const int FrameHeight = 400;
    private const int FrameCutX = 14;
    private const int FrameCutY = 28;

    private readonly TextInput _emailInput;
    private readonly TextInput _passwordInput;

    public LoginContainer() {
        SetAnchor(UiAnchor.Middle);

        var background = new NineSliceRect(new NineSliceConfig {
            SliceData = SliceLibrary.LoginFrame,
            CutX = FrameCutX,
            CutY = FrameCutY,
            Width = FrameWidth,
            Height = FrameHeight
        });
        AddChild(background);

        var title = new SimpleText(new TextConfig { Text = "Log in", FontSize = 26, FontType = FontType.Bold, X = Width / 2, Y = 45, Color = 0xFFFFFF, OutlineColor = 0x000000, OutlineThickness = 4, Anchor = UiAnchor.Middle });
        AddChild(title);

        var emailConfig = new InputConfig { X = Width / 2, Y = 115, FontSize = 24, FontType = FontType.Bold, Color = 0xFFFFFF, Width = 350, DefaultText = "Username", Anchor = UiAnchor.Middle };
        _emailInput = new TextInput(emailConfig);
        AddChild(_emailInput);

        var passwordConfig = new InputConfig { X = Width / 2, Y = 175, FontSize = 24, FontType = FontType.Bold, Color = 0xFFFFFF, Width = 350, DefaultText = "Password", Password = true, Anchor = UiAnchor.Middle };
        _passwordInput = new TextInput(passwordConfig);
        _emailInput.OnSubmit = OnLogin;              // Enter = Log in (2026-09-22)
        _passwordInput.OnSubmit = OnLogin;
        AddChild(_passwordInput);

        var registerConfig = new TextButtonConfig { Text = "New user? Click here to Register!", FontSize = 16, OnClicked = () => { OverlayManager.Set(new RegisterContainer()); }, FontType = FontType.Bold, X = Width / 2, Y = _passwordInput.Y + 40, Anchor = UiAnchor.Middle };
        var registerButton = new TextButton(registerConfig);
        AddChild(registerButton);

        var loginConfig = new TextButtonConfig { Text = "Log in", FontSize = 28, OnClicked = OnLogin, FontType = FontType.Normal, X = Width - 25, Y = Height - 25, Anchor = UiAnchor.RightBottom };
        var loginButton = new TextButton(loginConfig);
        AddChild(loginButton);

        var cancelConfig = new TextButtonConfig { Text = "Cancel", FontSize = 28, OnClicked = CloseOverlay, FontType = FontType.Normal, X = loginButton.X - loginButton.Width - 35, Y = Height - 25, Anchor = UiAnchor.RightBottom };
        var cancelButton = new TextButton(cancelConfig);
        AddChild(cancelButton);
    }

    private void OnLogin() {
        AddEventListener(AppRequests.VerifyAsync(_emailInput.Text, _passwordInput.Text, true), OnLoginResponse);
    }

    private void OnLoginResponse(AppResponse response) {
        if (!response.Success) {
            var dialog = new Dialog("Login Error", response.Message, new DialogOption("Ok"));
            DialogManager.Enqueue(dialog);
            return;
        }
        CloseOverlay();
        DispatchEvent(new Event(LoginEvent));
    }
}
