using GameServer.Game.Entities.Extensions;
using GameServer.Game.Systems.Chat;
using GameServer.Game.Systems.Combat;
using GameServer.Game.Network;

namespace GameServer.Game.Systems.Chat.Commands;

[Command("reloadbehaviors", CommandPermissionLevel.Developer)]
public class ReloadBehaviorsCommand : Command {
    public override async Task ExecuteAsync(User user, string args) {
        user.SendInfo("Reloading behavior files...");
        if (await RealmManager.ReloadAllBehaviors())
            user.SendInfo("Successfully reloaded behaviors.");
        else user.SendError("Failed to reload behaviors. Check GameServer console for more information.");
    }
}