using Common.Network;
using GameServer.Game.Network.Messaging;
using Common.Utilities.Collections;

namespace GameServer.Game.Systems.Chat;

public readonly record struct Text(string Name, EntityId ObjId, int NumStars, byte BubbleTime, string Recipent, string Txt)
    : IOutgoingPacket {
    public PacketId ID => PacketId.Text;

    public void Write(ref SpanWriter wtr) {
        wtr.WriteUTF(Name);
        wtr.Write(ObjId.Value);
        wtr.Write(NumStars);
        wtr.Write(BubbleTime);
        wtr.WriteUTF(Recipent);
        wtr.WriteUTF(Txt);
    }
}