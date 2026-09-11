using GameServer.Game.Network.Messaging;
using Common;
using Common.Network;
using Common.Resources.Xml.Descriptors;
using Common.Utilities.Collections;
using GameServer.Game.Entities;
using GameServer.Utilities;
using GameServer.Game.Systems.Combat;
using GameServer.Game.Systems.Inventory;
using GameServer.Game.Network;

namespace GameServer.Game.Systems.Inventory;

[Packet(PacketId.INVDROP)]
public record InvDrop : IIncomingPacket {
    public byte SlotId;

    public async Task Handle(User user) {
        var world = user.GameInfo.World;
        GameLogic.Enqueue(() => {
            ref var playerInv = ref world.EntityInventories.Get(user.GameInfo.PlayerId);
            if (playerInv.Id == EntityId.Null)
                return;

            var item = playerInv[SlotId];
            if (item == null)
                return;

            var bag = new Entity(InventoryUtils.GetBagIdFromType(BagType.Pink));
            world.EnterWorld(ref bag);
            ref var bagInv = ref world.EntityInventories.Get(bag.Id);
            bagInv.SetItem(0, item);
            
            playerInv.SetItem(SlotId, null);
        });
    }

    public void Read(ref SpanReader rdr) {
        SlotId = rdr.ReadByte();
    }
}