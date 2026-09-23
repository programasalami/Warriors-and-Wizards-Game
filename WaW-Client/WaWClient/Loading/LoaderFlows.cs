using WaWClient.AppEngine;
using WaWClient.Data;
using WaWClient.Display;
using WaWClient.Screens;
using WaW.UiLib.Extra;

namespace WaWClient.Loading;

// Menu-side transitions that need data go through the logo loader with a real plan, never a bare fade.
public static class LoaderFlows {

    // Title -> character select. The character list was already fetched with the sign-in (or at startup), so this goes
    // straight to the book with a short fade; the logo loader below is only the fallback for when that fetch didn't
    // happen or failed.
    public static void FromTitleToCharacterList() {
        var login = GlobalData.Get<LoginData>();
        if (login != null && AppRequests.HasCharListFor(login.Username)) {
            ScreenManager.FadeToScreen(new CharacterListScreen(dataLoaded: true), Easing.SineInOut, 800, 0x0);
            return;
        }

        ToCharacterList();
    }

    // Disconnect (and the title fallback) -> character select (the book): fetch the account + character list from the account server,
    // then build the screen - the bar reflects those two things actually finishing.
    public static void ToCharacterList() {
        var plan = new LoadPlan();
        CharacterListScreen screen = null;

        var characters = plan.AddTask("Your characters", 70, () => AppRequests.GetCharList());
        plan.AddMainThread("Opening the book", 30, () => screen = new CharacterListScreen(dataLoaded: true), characters);

        // If building the screen failed there is still somewhere to go: a screen that fetches for itself.
        ScreenManager.FadeToScreen(new LoaderScreen(plan, () => screen ?? new CharacterListScreen(), 1400), Easing.SineInOut, 700, 0x0);
    }
}
