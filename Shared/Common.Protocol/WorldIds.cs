namespace Common.Structs;

// The world ids a client may ask for in its Hello packet (GameId). Real worlds get positive ids from RealmManager as they are made; these
// negative ones name a KIND of world: the shared Nexus, the account's own Vault, the account's guild's hall (the server finds or creates the
// right instance). The Character Book's FAST TRAVEL page picks one of them; a Reconnect from the server carries a real id instead.
public static class WorldIds {
    public const int Nexus = -1;
    public const int Test = -2;
    public const int Vault = -5;
    public const int GuildHall = -6;
}
