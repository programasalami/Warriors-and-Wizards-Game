using Common.Network;
using GameServer.Game.Network.Messaging;

namespace GameServer.Game.Session;

public readonly record struct AccountList(int AccountListId, int[] AccountIDs) : IOutgoingPacket {
    public const int Locked = 0;
    public const int Ignored = 1;
    public PacketId ID => PacketId.ACCOUNTLIST;

    public void Write(ref SpanWriter wtr) {
        wtr.Write(AccountListId);
        wtr.Write(AccountIDs);
    }
}