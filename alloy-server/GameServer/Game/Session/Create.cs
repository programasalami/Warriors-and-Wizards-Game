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

[Packet(PacketId.Create)]
public record Create : IIncomingPacket {
    public short ClassType;
    public short SkinType;

    public void Read(ref SpanReader rdr) {
        ClassType = rdr.ReadInt16();
        SkinType = rdr.ReadInt16();
    }

    public async Task Handle(User user) {
        var result = await Program.AccountServerRpc.CreateCharacter(user.GameInfo.Account, (ushort)ClassType, (ushort)SkinType);
        if (user.State == ConnectionState.Disconnected)
            return;
        var chr = result.Char;
        var status = result.Status;
        if (chr == null) {
            user.SendFailure(Failure.DEFAULT, status.GetDescription());
        }
        else {
            // AccountServer mutated its own copy of the account (NextCharId, Characters) -
            // adopt it here so this session's in-memory account reflects the new character.
            user.GameInfo.Account = result.Account;
            var world = user.GameInfo.World;
            if (world.Deleted) {
                user.SendFailure(Failure.DEFAULT, "Invalid world.");
                return;
            }
        
            user.Load(chr, world);
        }
    }
}