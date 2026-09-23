using Common.Network;
using Common.Utilities;
using GameServer.Game.Network;
using GameServer.Game.Network.Messaging;
using GameServer.Game.Session;
using GameServer.Game.Systems.Chat;
using GameServer.Game.Systems.Combat;
using GameServer.Game.Systems.Inventory;
using GameServer.Game.Systems.Projectiles;

namespace GameServer.Game.Session;

[Packet(PacketId.Load)]
public record Load : IIncomingPacket {
    public int CharId;

    public void Read(ref SpanReader rdr) {
        CharId = rdr.ReadInt32();
    }

    public async Task Handle(User user) {
        if (user.GameInfo.Account.IsBanned) {
            user.SendFailure(Failure.DEFAULT, "Account has been banned.");
            return;
        }
        
        var chr = user.GameInfo.Char;
        if (user.State != ConnectionState.Reconnecting) {
            chr = await Program.AccountServerRpc.GetCharacter(user.GameInfo.Account.Id, CharId);
            if (user.State == ConnectionState.Disconnected)
                return;
            if (chr == null) {
                user.SendFailure(Failure.DEFAULT, $"Failed to load character #{CharId}");
                return;
            }
        }
        
        if (chr == null) {
            user.SendFailure(Failure.DEFAULT, "Invalid reconnect state.");
            return;
        }
        
        if (chr.IsDead) {
            user.SendFailure(Failure.DEFAULT, "Character is dead.");
            return;
        }
        
        var world = user.GameInfo.World;
        if (world.Deleted) {
            user.SendFailure(Failure.DEFAULT, "Invalid world.");
            return;
        }
        
        user.Load(chr, world);
    }
}