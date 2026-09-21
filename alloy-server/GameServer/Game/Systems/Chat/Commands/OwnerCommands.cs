using Common.Database;
using Common.Resources.Xml;
using Common.Resources.Xml.Descriptors;
using GameServer.Game.Entities;
using GameServer.Game.Entities.Extensions;
using GameServer.Game.Systems.Chat;
using GameServer.Game.Systems.Combat;
using GameServer.Game.Network;

namespace GameServer.Game.Systems.Chat.Commands;

// Owner-only commands: give (items), setrank, mail. Nothing here is available to a Moderator or a Player.

[Command("give", CommandPermissionLevel.Owner)]
public class GiveCommand : Command {
    public override async Task ExecuteAsync(User user, string args) {
        if (!CommandArgs.TryGive(args, out var count, out var query, out var error)) {
            user.SendError(error);
            return;
        }

        var matches = ItemSearch.Find(XmlLibrary.ItemDescs.Values.Select(i => new ItemSearch.Entry(i.ObjectType, i.DisplayId ?? i.ObjectId)), query);
        if (matches.Count == 0) {
            user.SendError($"No item matches '{query}'.");
            return;
        }

        if (!ItemSearch.IsClearWinner(matches, query)) {
            user.SendInfo($"{matches.Count} items match '{query}': " + string.Join(", ", matches.Take(8).Select(m => m.Name)) + (matches.Count > 8 ? ", ..." : string.Empty) + ". Be more specific.");
            return;
        }

        var chosen = matches[0];
        var world = user.GameInfo.World;
        ref var inv = ref world.EntityInventories.Get(user.GameInfo.PlayerId);
        if (inv.Id == Common.Utilities.Collections.EntityId.Null) {
            user.SendError("You have no inventory right now.");
            return;
        }

        var given = 0;
        for (var i = 0; i < count; i++) {
            if (inv.TryAdd(new Item(XmlLibrary.ItemDescs[chosen.Type].Root)) == -1)
                break;
            given++;
        }

        if (given == 0)
            user.SendError("Your inventory is full.");
        else
            user.SendInfo($"Gave you {(given > 1 ? given + " x " : string.Empty)}{chosen.Name}." + (given < count ? " (Your inventory is full.)" : string.Empty));
    }
}

[Command("setrank", CommandPermissionLevel.Owner)]
public class SetRankCommand : Command {
    public override async Task ExecuteAsync(User user, string args) {
        if (!CommandArgs.TrySetRank(args, out var name, out var rank, out var error)) {
            user.SendError(error);
            return;
        }

        var result = await Program.AccountServerRpc.Moderate(new("setrank", user.GameInfo.Account.Id, name, null, 0, rank));
        if (result.Error != null) {
            user.SendError(result.Error);
            return;
        }

        // Someone who is playing right now gets the new rank at once (their commands change immediately).
        var online = ModTargets.FindOnline(name);
        if (online != null) {
            Ranks.Set(online.GameInfo.Account, rank);
            online.SendInfo($"Your rank is now {Ranks.Name(rank)}.");
        }

        user.SendInfo($"{name} is now a {Ranks.Name(rank)}.");
    }
}

[Command("mail", CommandPermissionLevel.Owner)]
public class MailCommand : Command {
    public override async Task ExecuteAsync(User user, string args) {
        if (!CommandArgs.TryMail(args, out var name, out var gold, out var fame, out var message, out var error)) {
            user.SendError(error);
            return;
        }

        var result = await Program.AccountServerRpc.SendMail(new(name, user.GameInfo.Account.Name, "A message for you", message, gold, fame));
        if (result.Error != null)
            user.SendError(result.Error);
        else
            user.SendInfo($"Mail sent to {name}" + (gold > 0 || fame > 0 ? $" with {gold:N0} gold and {fame:N0} fame." : "."));
    }
}
