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

// Character select is "the book" now (CharacterBook, from the Pocket Inventory Series #7 "Gems of
// Status" pack - it replaced the handheld console): a mostly-screen-sized, slightly transparent open
// book over the forest backdrop, opening on the Profile page with side tabs for Characters,
// Graveyard, Inbox, Daily Spin and Daily Gift. This screen just owns the backdrop and the
// character-list data loading; everything else - pages, selecting, playing, the account readout -
// lives in the book itself.
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
    private readonly ForestBackdrop _background;
    private readonly CharacterBook _book;

    // dataLoaded: the loader (LoaderFlows.ToCharacterList) already fetched the character list into GlobalData, so
    // there is nothing to request again.
    public CharacterListScreen(bool dataLoaded = false) {
        // Dedicated forest backdrop for this screen (see ForestBackdrop) instead of the sci-fi
        // starfield MilkyWayBackground used to draw - per request, to match the parchment/forest
        // theme the rest of the menus were rebuilt around. Lives outside _root (added before it,
        // so it still renders behind everything) and gets its own real-window-size Resize() call
        // below instead of sharing _root's uniform Stage.ScreenScale - see ForestBackdrop's own
        // class comment for why a full-screen backdrop specifically needs that instead of the
        // fixed-canvas scaling every other element on this screen uses.
        _background = new ForestBackdrop();
        AddChild(_background);

        _root = new Container();
        AddChild(_root);

        var account = GlobalData.Get<AccountData>();

        _book = new CharacterBook(OnPlay, ShowCharacterCreate, OnBack);
        _book.SetAccountInfo(account.Stats.Credits, account.Stats.Fame);
        _root.AddChild(_book);

        MouseEnabled = true;

        if (dataLoaded) {
            LoadCharacterList();
        } else {
            AddEventListener(AppRequests.GetCharList(), LoadCharacterList);
        }

        CheckForAppFailure();

        // Kicked back here because the server moved to a newer build while this one was open (see Failure.Handle).
        VersionCheck.ShowDialogIfOutdated();
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

        // Open with the last-played character selected (falls back to the first, i.e. highest-fame, one).
        var lastPlayed = sorted.FindIndex(c => c.Id == (int) Settings.LastPlayedCharacterId);
        _book.SetCharacters(sorted, lastPlayed >= 0 ? lastPlayed : 0);
    }

    // ALLOY_PERFTEST only (Game/DevPerfTest.cs): play the last-played character by itself.
    private double _perfTestWaitMs;
    private bool _perfTestPressed;

    private bool _outdatedShown;

    public override void Update(Alloy.Engine.GameTime gameTime) {
        base.Update(gameTime);
        // The version check that Disconnect started can answer after this screen was built: put the prompt up as soon as it knows.
        if (!_outdatedShown && AppEngine.VersionCheck.IsOutdated) {
            _outdatedShown = true;
            AppEngine.VersionCheck.ShowDialogIfOutdated();
        }
        if (!AlloyClient.Dev.DevPerfTest.Enabled || _perfTestPressed)
            return;
        _perfTestWaitMs += gameTime.ElapsedMs;
        if (_perfTestWaitMs < 3000)
            return;
        var chars = GlobalData.Get<CharacterListData>()?.Characters;
        if (chars == null || chars.Length == 0)
            return;
        _perfTestPressed = true;
        if (AlloyClient.Dev.DevPerfTest.TimelineMode)
            Settings.FastTravel.Set(Data.FastTravel.DefaultKey);     // the Nexus, where a second player can walk in (changes the saved Spawn Location)
        var pick = System.Array.Find(chars, c => c.Id == (int) Settings.LastPlayedCharacterId) ?? chars[0];
        OnPlay(pick);
    }

    // The book says which character to play: the selected one on the Characters page, the last-played one on the Profile page.
    private void OnPlay(Character character) {
        if (character == null) {
            ShowCharacterCreate();
            return;
        }

        GlobalData.SelectedCharacterId = character.Id;
        Settings.LastPlayedCharacterId.Set(character.Id);
        Settings.SaveSettings();
        ScreenManager.FadeToScreen(new GameScreen(), Easing.SineInOut, 1000, 0x0);
    }

    private static void OnBack() {
        ScreenManager.FadeToScreen(new TitleScreen(), Easing.SineInOut, 1400, 0x0);
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
