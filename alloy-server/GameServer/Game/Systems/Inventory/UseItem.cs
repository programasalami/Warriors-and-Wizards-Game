using Common;
using Common.Network;
using Common.Resources.Xml.Descriptors;
using Common.Structs;
using Common.Utilities.Collections;
using GameServer.Game.Network;
using GameServer.Game.Network.Messaging;
using GameServer.Game.Systems.Stats;
using GameServer.Game.Worlds;

namespace GameServer.Game.Systems.Inventory;

// "Use" an item in the player's own inventory (the client sends this for a potion: shift-click, double-click, the C key or a slot key).
// There was NO handler for this packet before 2026-09-22, so a Health Potion could not be drunk. Only consumables are used; the effects
// come from the item's <Activate> tags (Heal = HP, Magic = MP) and the item is gone afterwards.
[Packet(PacketId.UseItem)]
public record UseItem : IIncomingPacket {
    private const int HealColor = 0x3CC46A;
    private const int MagicColor = 0x4FA3E8;

    public int Time;
    public SlotObjectData Slot;
    public WorldPosData Pos;
    public byte UseType;

    public async Task Handle(User user) {
        if (user.GameInfo.State != GameState.Playing)
            return;
        if (Slot.ObjectId != user.GameInfo.PlayerId)
            return;             // only your own inventory (a chest or bag is used through a swap, not a use)

        var world = user.GameInfo.World;
        var playerId = user.GameInfo.PlayerId;
        var slot = Slot.SlotId;
        GameLogic.Enqueue(() => {
            var result = Apply(world, playerId, slot);
            if (result.Healed > 0)
                Send(user, new Notification(playerId, $"+{result.Healed} HP", HealColor, 22));
            if (result.Restored > 0)
                Send(user, new Notification(playerId, $"+{result.Restored} MP", MagicColor, 22));
        });
    }

    public readonly record struct UseResult(bool Used, int Healed, int Restored);

    // The whole effect, without any network (tested in UseItemTests): a consumable in that slot heals / restores and is removed.
    public static UseResult Apply(World world, EntityId playerId, int slot) {
        ref var inv = ref world.EntityInventories.Get(playerId);
        ref var stats = ref world.EntityStats.Get(playerId);
        if (inv.Id == EntityId.Null || stats.Id == EntityId.Null || slot < 0)
            return default;

        Item item;
        try { item = inv[slot]; } catch { return default; }
        if (item == null || !item.Consumable)
            return default;

        var healed = 0;
        var restored = 0;
        foreach (var effect in item.ActivateEffects ?? []) {
            switch (effect.AEIndex) {
                case ActivateEffectIndex.Heal:
                    healed += Raise(ref stats, StatType.HP, StatType.MaxHP, effect.Amount);
                    break;
                case ActivateEffectIndex.Magic:
                    restored += Raise(ref stats, StatType.MP, StatType.MaxMP, effect.Amount);
                    break;
            }
        }

        inv.SetItem(slot, null);
        return new UseResult(true, healed, restored);
    }

    private static int Raise(ref EntityStats stats, StatType stat, StatType max, int amount) {
        var current = stats.GetInt(stat);
        var target = Math.Min(stats.GetInt(max), current + Math.Max(0, amount));
        stats.Set(stat, target);
        return target - current;
    }

    private static void Send(User user, in Notification packet) {
        if (user?.Network == null)
            return;
        try { user.SendPacket(packet); } catch { }
    }

    public void Read(ref SpanReader rdr) {
        Time = rdr.ReadInt32();
        Slot = SlotObjectData.Read(ref rdr);
        Pos = WorldPosDataIO.Read(ref rdr);
        UseType = rdr.ReadByte();
    }
}
