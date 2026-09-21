using Common.Database;
using GameServer.Game.Entities.Extensions;
using GameServer.Game.Systems.Chat;
using GameServer.Game.Systems.Combat;
using GameServer.Game.Network;

namespace GameServer.Game.Systems.Chat.Commands;

[Command("commands", CommandPermissionLevel.Player)]
public class CommandListCommand : Command {
    public override async Task ExecuteAsync(User user, string args) {
        var cmdList = string.Join(", ", CommandManager.GetCommandList(Ranks.Of(user.GameInfo.Account)));
        user.SendInfo($"Available commands: {cmdList}");
    }
}

[Command("online", CommandPermissionLevel.Player)]
public class OnlineCommand : Command
{
    public override async Task ExecuteAsync(User user, string args)
    {
        var totalCount = RealmManager.Users.Count;
        var localCount = user.GameInfo.World.Users.Count;
        user.SendInfo($"There are {totalCount} players online. {localCount} of them are in this world.");
    }
}

[Command("rank", CommandPermissionLevel.Player)]
public class RankCommand : Command {
    public override async Task ExecuteAsync(User user, string args) {
        user.SendInfo($"Your rank is {Ranks.Name(Ranks.Of(user.GameInfo.Account))}.");
    }
}
