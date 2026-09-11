#region

using Common.Network;
using Common.Structs;
using GameServer.Game.Entities.Extensions;
using GameServer.Game.Systems.Chat;
using GameServer.Game.Systems.Combat;

#endregion

namespace GameServer.Game.Network.Messaging;

[Packet(PacketId.MOVE)]
public record Move : IIncomingPacket {
    public WorldPosData Pos;

    public async Task Handle(User user) {
        if (user.GameInfo.State != GameState.Playing)
            return;

        ref var player = ref user.GameInfo.Player;
        player.Move(user.GameInfo.World, Pos.X, Pos.Y);
    }

    public void Read(ref SpanReader rdr) {
        Pos = WorldPosData.Read(ref rdr);
    }
}