using Common.Network;
using GameServer.Game.Network.Messaging;

namespace GameServer.Game.Systems.Inventory;

public readonly record struct InvResult(int Result) : IOutgoingPacket {
    public PacketId ID => PacketId.INVRESULT;

    public void Write(ref SpanWriter wtr) {
        wtr.Write(Result);
    }
}