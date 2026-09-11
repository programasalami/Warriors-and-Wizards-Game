using Common.Network;
using GameServer.Game.Network.Messaging;

namespace GameServer.Game.Session;

public readonly record struct MapInfo(
    int MapWidth,
    int MapHeight,
    string Name,
    string DisplayName,
    uint Seed,
    int Background,
    bool ShowDisplays,
    bool AllowPlayerTeleport,
    string Music,
    int Difficulty) : IOutgoingPacket {
    public PacketId ID => PacketId.MAPINFO;

    public void Write(ref SpanWriter wtr) {
        wtr.Write(MapWidth);
        wtr.Write(MapHeight);
        wtr.WriteUTF(Name);
        wtr.WriteUTF(DisplayName);
        wtr.Write(Seed);
        wtr.Write(Background);
        wtr.Write(ShowDisplays);
        wtr.Write(AllowPlayerTeleport);
        wtr.WriteUTF(Music);
        wtr.Write(Difficulty);
    }
}