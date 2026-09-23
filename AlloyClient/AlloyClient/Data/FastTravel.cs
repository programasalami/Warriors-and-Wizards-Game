using Common.Structs;

namespace AlloyClient.Data;

// Where PLAY drops the character: the FAST TRAVEL page of the Character Book shows these as tiles, the choice is saved in Settings.FastTravel,
// and Client.SendHello asks the server for that kind of world (WorldIds). A destination the account cannot use (the Guild Hall without a
// guild) falls back to the Nexus.
public sealed record FastTravelDestination(string Key, string Name, int GameId, string Preview, bool NeedsGuild);

public static class FastTravel {
    public const string DefaultKey = "Nexus";

    public static readonly FastTravelDestination[] Destinations = [
        new("Nexus", "NEXUS", WorldIds.Nexus, "BookGems/Maps/Nexus", false),
        new("Vault", "VAULT", WorldIds.Vault, "BookGems/Maps/Vault", false),
        new("GuildHall", "GUILD HALL", WorldIds.GuildHall, "BookGems/Maps/GuildHall", true)
    ];

    public static bool Available(FastTravelDestination destination, AccountData account) =>
        !destination.NeedsGuild || (account != null && !string.IsNullOrEmpty(account.GuildName));

    // The saved choice if it exists and the account may use it, else the Nexus.
    public static FastTravelDestination Resolve(string key, AccountData account) {
        foreach (var d in Destinations) {
            if (d.Key == key && Available(d, account)) {
                return d;
            }
        }

        return Destinations[0];
    }

    public static int GameIdFor(string key, AccountData account) => Resolve(key, account).GameId;
}
