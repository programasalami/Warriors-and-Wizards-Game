using GameServer.Game.Network.Messaging;
using Common.Network;
using GameServer.Game.Entities.Extensions;
using GameServer.Game.Systems.Chat;
using GameServer.Game.Systems.Combat;
using GameServer.Game.Worlds;
using GameServer.Game.Network;

namespace GameServer.Game.Session;

[Packet(PacketId.Escape)]
public record Escape : IIncomingPacket {
    public async Task Handle(User user) {
        if (user.GameInfo.State != GameState.Playing)
            return;

        if (user.GameInfo.World.Id == World.NEXUS_ID) {
            user.SendInfo("You're already in the Nexus!");
            return;
        }

        user.ReconnectTo(RealmManager.Worlds[World.NEXUS_ID]);
    }

    public void Read(ref SpanReader rdr) { }
}