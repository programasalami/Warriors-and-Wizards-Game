using WaWClient.Display;
using WaWClient.Screens;
using WaW.UiLib.Extra;
using WaW.UiLib;

namespace WaWClient.Networking.Packets.Incoming;

public class Death : IncomingPacket<Death> {
    public int AccountId;
    public int CharId;
    public string KilledBy;

    public override PacketId PacketId => PacketId.Death;

    public override void Reset() {
        AccountId = 0;
        CharId = 0;
        KilledBy = string.Empty;
    }

    public override void Read(ref SpanReader reader) {
        AccountId = reader.ReadInt32();
        CharId = reader.ReadInt32();
        KilledBy = reader.ReadUTF();
    }

    public override void Handle() {
        ScreenManager.FadeToScreen(new TitleScreen(), Easing.SineInOut, 1000, 0x0);
    }

    public override string ToString() {
        return $"AccountId: {AccountId}, CharId: {CharId}, KilledBy: {KilledBy}";
    }
}