using GameServer.Game.Network.Messaging;
using System.ComponentModel;
using Common.Network;
using Common.Structs;
using GameServer.Game.Entities;
using GameServer.Game.Systems.Behaviors;
using GameServer.Game.Systems.Chat;
using GameServer.Game.Systems.Combat;
using GameServer.Game.Systems.Events;
using GameServer.Game.Systems.Inventory;
using GameServer.Game.Systems.Portals;
using GameServer.Game.Systems.Projectiles;
using GameServer.Game.Systems.Sight;
using GameServer.Game.Systems.Stats;
using GameServer.Game.Worlds;
using GameServer.Utilities;
using GameServer.Game.Network;

namespace GameServer.Game.Systems.Inventory;

[Packet(PacketId.INVSWAP)]
public record InvSwap : IIncomingPacket {
    public SlotObjectData SlotObject1;
    public SlotObjectData SlotObject2;

    public async Task Handle(User user) {
        user.GameInfo.World.EntityInventories.EnqueueSwap(user, SlotObject1, SlotObject2);
    }

    public void Read(ref SpanReader rdr) {
        SlotObject1 = SlotObjectData.Read(ref rdr);
        SlotObject2 = SlotObjectData.Read(ref rdr);
    }
}