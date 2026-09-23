using GameServer.Game.Network.Messaging;
using Common.Network;
using GameServer.Game.Entities.Extensions;
using GameServer.Game.Systems.Chat;
using GameServer.Game.Systems.Combat;
using GameServer.Game.Network;

namespace GameServer.Game.Systems.Chat;

[Packet(PacketId.PlayerText)]
public record PlayerText : IIncomingPacket {
    public string Text;

    public async Task Handle(User user) {
        user.GameInfo.Player.Speak(user.GameInfo.World, Text);
    }

    public void Read(ref SpanReader rdr) {
        Text = rdr.ReadUTF();
    }
}