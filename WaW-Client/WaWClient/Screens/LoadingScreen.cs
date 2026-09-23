using WaWClient.AppEngine;
using WaWClient.Loading;

namespace WaWClient.Screens;

// Client start-up: the logo loader (see LoaderScreen) running the startup plan built in Main.BuildStartupPlan - game
// art, fonts, data parsing, account check, renderer setup - then the title screen. The bar is real: it moves only as
// those steps actually finish.
public class LoadingScreen : LoaderScreen {

    public LoadingScreen(LoadPlan plan) : base(plan, () => new TitleScreen(), 1000) { }

    // Retry (from RetryLoadDialog): the game is already loaded, so only the account check runs again.
    public LoadingScreen(bool isRetry) : this(BuildRetryPlan()) { }

    private static LoadPlan BuildRetryPlan() {
        var plan = new LoadPlan();
        plan.AddTask("Your account", 1, AppRequests.Startup);
        return plan;
    }
}
