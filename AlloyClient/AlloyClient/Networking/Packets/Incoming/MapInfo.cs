using AlloyClient.Data;
using AlloyClient.Game.Music;
using AlloyClient.Game;
using AlloyClient.Loading;
using AlloyClient.Networking.Packets.Outgoing;
using Microsoft.Extensions.Logging;

namespace AlloyClient.Networking.Packets.Incoming;

public class MapInfo : IncomingPacket<MapInfo> {
    public int Width;
    public int Height;
    public string Name;
    public string DisplayName;
    public int Difficulty;
    public uint Seed;
    public int Background;
    public bool AllowPlayerTeleport;
    public bool ShowDisplays;
    public string Music;          // the world config's music name ("Nexus", "Realm", "Vault", "GuildHall"): InGameMusic routes on it

    public override PacketId PacketId => PacketId.MapInfo;

    public override void Reset() {
        Width = 0;
        Height = 0;
        Name = null;
        DisplayName = null;
        Difficulty = 0;
        Seed = 0;
        Background = 0;
        AllowPlayerTeleport = false;
        ShowDisplays = false;
    }

    public override void Read(ref SpanReader reader) {
        // The same order the server writes (GameServer MapInfo.Write). Until 2026-09-21 this read Difficulty where the server had written the
        // seed and never read the music name at all - the fields were only ever used loosely, so nobody noticed.
        Width = reader.ReadInt32();
        Height = reader.ReadInt32();
        Name = reader.ReadUTF();
        DisplayName = reader.ReadUTF();
        Seed = reader.ReadUInt32();
        Background = reader.ReadInt32();
        ShowDisplays = reader.ReadBoolean();
        AllowPlayerTeleport = reader.ReadBoolean();
        Music = reader.ReadUTF();
        Difficulty = reader.ReadInt32();
    }

    public override void Handle() {
        Map.Reset();


        Map.InitMap(Width, Height, Name, DisplayName, Difficulty, Seed, Background,
            AllowPlayerTeleport, ShowDisplays);

        LoadOrCreate();

        InGameMusic.OnWorld(Music);

        Client.IsReconnecting = false;
        WorldLoad.Mark(WorldMilestone.MapInfo);
    }

    private static void LoadOrCreate() {
        if (GlobalData.CharacterType > 0) {
            var create = Create.CreatePacket();
            create.ClassType = GlobalData.CharacterType;
            create.SkinType = 0;
            Client.QueuePacket(create);

            GlobalData.CharacterType = 0;
            return;
        }
        
        var load = Load.CreatePacket();
        load.CharId = GlobalData.SelectedCharacterId;
        Client.QueuePacket(load);
    }

    public override string ToString() {
        return $"Width: {Width}, Height: {Height}, Name: {Name}, DisplayName: {DisplayName}, Difficulty: {Difficulty}, Seed: {Seed}, Background: {Background}, AllowPlayerTeleport: {AllowPlayerTeleport}, ShowDisplays: {ShowDisplays}";
    }
}