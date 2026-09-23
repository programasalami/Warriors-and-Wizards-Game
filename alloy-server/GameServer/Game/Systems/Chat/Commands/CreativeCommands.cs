using Common.Resources.Xml;
using GameServer.Game.Entities;
using GameServer.Game.Entities.Extensions;
using GameServer.Game.Systems.Chat;
using GameServer.Game.Systems.Combat;
using GameServer.Game.Network;

namespace GameServer.Game.Systems.Chat.Commands;

// /god - your own character takes no damage until you type it again (owner only, 2026-09-22; made for measuring the client among enemies).
[Command("god", CommandPermissionLevel.Owner)]
public class GodCommand : Command {
    public override Task ExecuteAsync(User user, string args) {
        var on = args?.Trim().ToLowerInvariant() switch {
            "on" => true,
            "off" => false,
            _ => !user.GameInfo.God
        };
        user.GameInfo.God = on;
        user.SendInfo(on ? "God mode ON: you take no damage." : "God mode OFF.");
        return Task.CompletedTask;
    }
}

[Command("spawn", CommandPermissionLevel.Owner)]
public class SpawnCommand : Command {
    public override async Task ExecuteAsync(User user, string args) {
        // if (user.GameInfo.Account.Rank < (int)CommandPermissionLevel.Moderator && player.World is not TestWorld) {
        //     user.SendError("Can only use this command in a test world.");
        //     return;
        // }

        if (string.IsNullOrWhiteSpace(args)) {
            user.SendHelp("/spawn <count> <entity>");
            return;
        }

        var rgs = args.Split(' ');

        int spawnCount;
        if (!int.TryParse(rgs[0], out spawnCount))
            spawnCount = -1;

        // Forgiving name lookup (SpawnRules): any case, a typed plural, the display name or a unique partial match; players are never spawnable.
        var desc = SpawnRules.Resolve(string.Join(' ', spawnCount == -1 ? rgs : rgs.Skip(1)), XmlLibrary.ObjectDescs.Values, out var lookupError);
        if (spawnCount == -1)
            spawnCount = 1;

        if (desc == null) {
            user.SendError(lookupError);
            return;
        }

        user.SendInfo($"Spawning <{spawnCount}> <{desc.DisplayId}> in 2 seconds");

        var world = user.GameInfo.World;
        ref var pos = ref world.EntityStats.Get(user.GameInfo.PlayerId).Pos;
        var x = pos.X;
        var y = pos.Y;

        var entity = new Entity(desc.ObjectType);
        world.AddTimedAction(2000, w => {
            for (var i = 0; i < spawnCount; i++) {
                ref var en = ref w.EnterWorld(ref entity);
                en.Move(world, x, y);
            }
        });
    }
}