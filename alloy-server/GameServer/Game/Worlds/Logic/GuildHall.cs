using Common.Database.Models;
using Common.Resources.Config;
using Common.Resources.World;
using GameServer.Game.Network;

namespace GameServer.Game.Worlds.Logic;

// A guild's hall: one world per guild, shared by its members, reached through the Guild Hall Portal in the Nexus or straight from the Character
// Book's FAST TRAVEL page. The config (GuildHall.json) lists four maps, one per hall size; every guild gets the first one until guild levels exist.
// Nothing in here is saved: the hall is only a place to meet.
public class GuildHall : World {
    private static readonly Dictionary<int, GuildHall> _halls = [];

    public const int DefaultMap = 0;

    public int GuildId { get; private set; }
    public string GuildName { get; private set; } = "";

    public GuildHall(int id, int mapId, WorldConfig config) : base(id, mapId, config) {
    }

    public override World GetInstance(User user) => user.GameInfo.Account == null ? null : ForAccount(user.GameInfo.Account);

    // The hall of the account's guild, made on first visit; null when the account is not in a guild.
    public static GuildHall ForAccount(Account account) {
        if (account == null || account.GuildId <= 0 || string.IsNullOrEmpty(account.GuildName))
            return null;

        return ForGuild(account.GuildId, account.GuildName);
    }

    public static GuildHall ForGuild(int guildId, string guildName) {
        if (!_halls.TryGetValue(guildId, out var hall) || hall.Deleted) {
            hall = _halls[guildId] = new GuildHall(0, DefaultMap, WorldLibrary.WorldConfigs["GuildHall"]);
            hall.GuildId = guildId;
            hall.GuildName = guildName;
            hall.DisplayName = guildName + "'s Guild Hall";
            RealmManager.AddWorld(hall);
        }

        return hall;
    }
}
