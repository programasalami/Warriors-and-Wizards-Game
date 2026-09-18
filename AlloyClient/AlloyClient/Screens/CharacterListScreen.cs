using System.Collections.Generic;
using AlloyClient.AppEngine;
using AlloyClient.Data;
using AlloyClient.Display;
using AlloyClient.Game;
using AlloyClient.Screens.Components;
using AlloyClient.Screens.Components.CharacterList;
using AlloyClient.Screens.Components.Containers;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using Alloy.UiLib.Extra;
using AlloyClient.Ui.Components.Dialogs;
using AlloyClient.Ui.Components.Graphics;

namespace AlloyClient.Screens;

// "The Console" (2026-09-17, fourth structural pass) - CharacterConsole (a handheld-console shell
// from the Pocket Inventory Series #9 "Pixel Console" pack) *is* the profile screen now: no more
// separate header chrome above it (name banner, gold/fame readout, Back button all removed per
// request - the console's own screen shows the account/character info now, and its B button
// already covers what the header's Back button did). This screen just owns the starfield
// background and character-list data loading; everything else - browsing, selecting, playing,
// the account readout - lives in the console itself.
public class CharacterListScreen : TitleScreenBase {
    // Every real content element below is built against the fixed Settings.DefaultScreenWidth/
    // Height design canvas (same old convention as ClassContainer/CharacterWheel/ClassInfo) -
    // none of it was ever rescaled to the actual window, unlike TitleScreenBase's own background
    // rect (which already resizes to fill the real window every frame). That mismatch is exactly
    // what read as "scaling broken/glitchy": the dark background correctly fills the whole
    // window, but everything on top of it stayed pinned at native 1280x720 pixel coordinates.
    // Wrapping it all in this root and scaling just the root (see OnResize) fixes that the same
    // way TitleScreen/GameScreen already scale their own root/_hud.
    private readonly Container _root;
    private readonly MilkyWayBackground _background;
    private readonly CharacterConsole _console;

    public CharacterListScreen() {
        // Dedicated animated starfield for this screen (see MilkyWayBackground) instead of the
        // title screen's shared castle courtyard - per request. Lives outside _root (added
        // before it, so it still renders behind everything) and gets its own real-window-size
        // Resize() call below instead of sharing _root's uniform Stage.ScreenScale - see
        // MilkyWayBackground's own class comment for why a full-screen backdrop specifically
        // needs that instead of the fixed-canvas scaling every other element on this screen uses.
        _background = new MilkyWayBackground();
        AddChild(_background);

        _root = new Container();
        AddChild(_root);

        // No more header chrome (name banner, gold/fame readout, Back button) sitting above the
        // console - per request, the console itself *is* the profile screen now, so all of that
        // moved onto its own screen content (see SetAccountInfo below); B on the console's own
        // ABXY cluster already covers what the header's Back button did.
        var account = GlobalData.Get<AccountData>();

        _console = new CharacterConsole(OnPlay, ShowCharacterCreate, OnBack);
        _console.SetAccountInfo(account.Stats.Credits, account.Stats.Fame);
        _root.AddChild(_console);

        MouseEnabled = true;

        AddEventListener(AppRequests.GetCharList(), LoadCharacterList);

        CheckForAppFailure();
    }

    // TitleScreenBase's own OnResize only resizes the dark background rect to fill the real
    // window - _root (everything else) needs its own scale applied here, same as
    // TitleScreen/GameScreen already do for their own root/_hud.
    //
    // Stage.ScreenScale is a "contain" fit (UiRender.OnResize takes the *smaller* of the X/Y
    // ratios so the 1280x720 design canvas never stretches), so on any real window whose aspect
    // ratio isn't exactly 1280:720 there's leftover space on one axis. _root.X/Y were never set,
    // so that scaled content just sat flush against the top-left corner instead of being
    // centered, pushing everything on this screen visibly left/up with all the slack landing on
    // the right/bottom - this is what read as "not centered". TitleScreen.cs already explicitly
    // centers its own _root against the real Stage.StageWidth/Height for this exact reason; this
    // screen never did.
    protected override void OnResize(ResizeEvent args) {
        base.OnResize(args);
        var scale = Stage.ScreenScale;
        _root.Scale = scale;
        _root.X = (int)((Stage.StageWidth - Settings.DefaultScreenWidth * scale.X) / 2f);
        _root.Y = (int)((Stage.StageHeight - Settings.DefaultScreenHeight * scale.Y) / 2f);
        _background.Resize(args.Width, args.Height);
    }

    private void LoadCharacterList() {
        var charModel = GlobalData.Get<CharacterListData>();
        if (charModel == null) {
            return;
        }

        var sorted = new List<Character>();
        if (charModel.Characters != null) {
            sorted.AddRange(charModel.Characters);
            // List.Sort isn't stable, so a bare Fame comparison can reorder same-Fame characters
            // differently between calls (this screen's own GetCharList event can fire more than
            // once per visit) - the displayed character would flicker between two entries with
            // equal Fame. Id as a tie-breaker makes the order deterministic.
            sorted.Sort((a, b) => {
                var fameCompare = b.CurrentFame.CompareTo(a.CurrentFame);
                return fameCompare != 0 ? fameCompare : a.Id.CompareTo(b.Id);
            });
        }

        _console.SetCharacters(sorted, 0);
    }

    private void OnPlay() {
        var character = _console.CurrentCharacter;
        if (character == null) {
            ShowCharacterCreate();
            return;
        }

        GlobalData.SelectedCharacterId = character.Id;
        ScreenManager.FadeToScreen(new GameScreen(), Easing.SineInOut, 1000, 0x0);
    }

    private static void OnBack() {
        ScreenManager.FadeToScreen(new TitleScreen(), Easing.SineInOut, 1000, 0x0);
    }

    public void ShowCharacterCreate() {
        OverlayManager.Set(new ClassContainer());
    }

    public void HideCharacterCreate() {
    }

    private void CheckForAppFailure() {
        if (!GlobalData.TryRemove<AppRequestFailedFlag>(out var data)) {
            return;
        }

        AddChild(new ScreenDarkenOverlay());

        DialogManager.Enqueue(new RetryLoadDialog(data.Message));
    }
}
