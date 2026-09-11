using Common.Network;
using GameServer.Game.Network.Messaging;

namespace GameServer.Game.Session;

public readonly record struct Reconnect(int GameId) : IOutgoingPacket {
    public PacketId ID => PacketId.RECONNECT;

    public void Write(ref SpanWriter wtr) {
        wtr.Write(GameId);
    }
}