using Common.Database;
using GameServer.Game.Entities;
using GameServer.Game.Entities.Extensions;
using GameServer.Game.Systems.Chat;
using GameServer.Game.Systems.Combat;
using Common.Network;
using GameServer.Game.Network;
using GameServer.Game.Network.Messaging;
using GameServer.Game.Session;

namespace GameServer.Game.Systems.Chat.Commands;

// Moderator commands: find, kick, ban, unban, mute, unmute. Who may do what to whom (a moderator must outrank the target; nobody touches an Owner) is decided
// in Common.Database.AdminDb / Ranks, so it is the same for every way of triggering these commands.
public static class ModTargets {
    public static User FindOnline(string name) =>
        RealmManager.Users.Values.FirstOrDefault(c => c.GameInfo?.Account != null && string.Equals(c.GameInfo.Account.Name, name, StringComparison.OrdinalIgnoreCase));

    // Re-reads a player's mute from the account server and remembers it on their live session (so it takes effect at once).
    public static async Task RefreshMuteAsync(User target) {
        var state = await Program.AccountServerRpc.GetMuteState(target.GameInfo.Account.Id);
        target.GameInfo.MuteEndUnix = state.MuteEndUnix;
    }

    public static int Minutes(TimeSpan? duration) => duration == null ? 0 : Math.Max(1, (int)Math.Ceiling(duration.Value.TotalMinutes));
}

[Command("find", CommandPermissionLevel.Moderator)]
public class FindPlayerCommand : Command {
    public override async Task ExecuteAsync(User user, string args) {
        if (string.IsNullOrEmpty(args)) {
            user.SendError("Usage: /find <player>");
            return;
        }

        // Find locally first
        var target = ModTargets.FindOnline(args)?.GameInfo.Data;
        if (target == null) {
            target = await Program.AccountServerRpc.GetUserInfo(args, -1);
            if (target == null) {
                user.SendError($"Player {args} not found.");
                return;
            }
        }

        user.SendInfo($"Player {args} is in {target?.WorldName}({target?.WorldId}) at {target?.Position}");
    }
}

[Command("kick", CommandPermissionLevel.Moderator)]
public class KickCommand : Command {
    public override async Task ExecuteAsync(User user, string args) {
        if (string.IsNullOrWhiteSpace(args)) {
            user.SendError("Usage: /kick <player>");
            return;
        }

        var target = ModTargets.FindOnline(args.Trim());
        if (target == null) {
            user.SendError($"Player {args} could not be found online.");
            return;
        }

        if (!Ranks.CanModerate(user.GameInfo.Account, target.GameInfo.Account)) {
            user.SendError($"You cannot kick {target.GameInfo.Account.Name}: they are the same rank as you or higher (or yourself).");
            return;
        }

        target.SendFailure(Failure.DEFAULT, "You were kicked from the game.");
        target.Disconnect();
        user.SendInfo($"Player {target.GameInfo.Account.Name} was kicked.");
    }
}

[Command("ban", CommandPermissionLevel.Moderator)]
public class BanCommand : Command {
    public override async Task ExecuteAsync(User user, string args) {
        if (!CommandArgs.TryModeration(args, "Usage: /ban <player> <perm|30m|12h|7d|2w> [reason]", out var name, out var duration, out var reason, out var error)) {
            user.SendError(error);
            return;
        }

        var result = await Program.AccountServerRpc.Moderate(new("ban", user.GameInfo.Account.Id, name, reason, ModTargets.Minutes(duration), 0));
        if (result.Error != null) {
            user.SendError(result.Error);
            return;
        }

        var online = ModTargets.FindOnline(name);
        if (online != null) {
            online.SendFailure(Failure.DEFAULT, "You have been banned" + (reason.Length > 0 ? $": {reason}" : "."));
            online.Disconnect();
        }

        user.SendInfo($"Banned {name} {ModerationRules.Describe(duration)}.");
    }
}

[Command("unban", CommandPermissionLevel.Moderator)]
public class UnbanCommand : Command {
    public override async Task ExecuteAsync(User user, string args) {
        if (string.IsNullOrWhiteSpace(args)) {
            user.SendError("Usage: /unban <player>");
            return;
        }

        var result = await Program.AccountServerRpc.Moderate(new("unban", user.GameInfo.Account.Id, args.Trim(), null, 0, 0));
        if (result.Error != null)
            user.SendError(result.Error);
        else
            user.SendInfo($"Unbanned {args.Trim()}.");
    }
}

[Command("mute", CommandPermissionLevel.Moderator)]
public class MuteCommand : Command {
    public override async Task ExecuteAsync(User user, string args) {
        if (!CommandArgs.TryModeration(args, "Usage: /mute <player> <perm|30m|12h|7d|2w> [reason]", out var name, out var duration, out var reason, out var error)) {
            user.SendError(error);
            return;
        }

        var result = await Program.AccountServerRpc.Moderate(new("mute", user.GameInfo.Account.Id, name, reason, ModTargets.Minutes(duration), 0));
        if (result.Error != null) {
            user.SendError(result.Error);
            return;
        }

        var online = ModTargets.FindOnline(name);
        if (online != null) {
            await ModTargets.RefreshMuteAsync(online);
            online.SendError($"You have been muted {ModerationRules.Describe(duration)}" + (reason.Length > 0 ? $": {reason}" : "."));
        }

        user.SendInfo($"Muted {name} {ModerationRules.Describe(duration)}.");
    }
}

[Command("unmute", CommandPermissionLevel.Moderator)]
public class UnmuteCommand : Command {
    public override async Task ExecuteAsync(User user, string args) {
        if (string.IsNullOrWhiteSpace(args)) {
            user.SendError("Usage: /unmute <player>");
            return;
        }

        var name = args.Trim();
        var result = await Program.AccountServerRpc.Moderate(new("unmute", user.GameInfo.Account.Id, name, null, 0, 0));
        if (result.Error != null) {
            user.SendError(result.Error);
            return;
        }

        var online = ModTargets.FindOnline(name);
        if (online != null) {
            await ModTargets.RefreshMuteAsync(online);
            online.SendInfo("You can chat again.");
        }

        user.SendInfo($"Unmuted {name}.");
    }
}
