using WaWClient.Data;
using WaWClient.Game;
using WaWClient.Loading;
using WaWClient.Networking.Packets.Outgoing;

namespace WaWClient.Networking.Packets.Incoming;

public class Reconnect : IncomingPacket<Reconnect> {
    public int GameId;

    public override PacketId PacketId => PacketId.Reconnect;

    public override void Reset() {
        GameId = 0;
    }

    public override void Read(ref SpanReader reader) {
        GameId = reader.ReadInt32();
    }

    public override void Handle() {
        // A world switch: bring the loading cover back over the old world while the new one loads.
        WorldLoad.Begin(true);
        GameScreen.GameSprite?.OnWorldLoadBegan(true);

        // Everything of the old world goes: entities, projectiles, particle effects, player / interactive lookups, queued texts.
        Map.ClearWorldObjects();

        var login = GlobalData.Get<LoginData>();
        var hello = Hello.CreatePacket();
        hello.BuildVersion = Settings.BuildVersion;
        hello.GameId = GameId;
        hello.Username = login.Username;
        hello.Password = login.Password;
        hello.MapJSON = "";
        Client.QueuePacket(hello);
    }

    public override string ToString() {
        return $"GameId: {GameId}";
    }
}