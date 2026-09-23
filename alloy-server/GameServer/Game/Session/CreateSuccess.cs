using Common.Network;
using GameServer.Game.Network.Messaging;
using Common.Utilities.Collections;

namespace GameServer.Game.Session;

public readonly record struct CreateSuccess(EntityId ObjectId, int CharId) : IOutgoingPacket {
    public PacketId ID => PacketId.CreateSuccess;

    public void Write(ref SpanWriter wtr) {
        wtr.Write(ObjectId.Value);
        wtr.Write(CharId);
    }
}