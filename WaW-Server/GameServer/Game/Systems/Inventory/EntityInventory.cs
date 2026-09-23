using System;
using System.Buffers;
using Common;
using Common.Database;
using Common.Database.Models;
using Common.Resources.World;
using Common.Resources.Xml;
using Common.Resources.Xml.Descriptors;
using Common.Structs;
using Common.Utilities;
using Common.Utilities.Collections;
using GameServer.Game.Network;
using GameServer.Game.Worlds;
using GameServer.Game.Entities;

namespace GameServer.Game.Systems.Inventory;

public struct EntityInventory : IEntityIdentifiable, IDisposable {
    public EntityId Id { get; set; }

    public readonly List<int> OwnerAccIds = [];
    public Item this[int slot] {
        get {
            if (slot < 0 || slot >= _size)
                return null;
            return _items[slot];
        }
    }

    private readonly World _world;
    private readonly int _size;
    private readonly int[] _slotTypes;
    private readonly Item[] _items;
    private readonly int[] _potionStacks;
    private BitMask256 _itemUpdates;

    public EntityInventory(World world, ref Entity en, int size) {
        Id = en.Id;
        _world = world;
        _size = size;
        
        _slotTypes = ArrayPool<int>.Shared.Rent(size);
        Array.Fill(_slotTypes, 0);
        _items = ArrayPool<Item>.Shared.Rent(size);
        Array.Fill(_items, null);
        _potionStacks = ArrayPool<int>.Shared.Rent(2);
        Array.Fill(_potionStacks, 0);
    }

    public void Init(Span<int> slotTypes, Span<int> itemTypes) {
        slotTypes.CopyTo(_slotTypes);
        for (var i = 0; i < itemTypes.Length; i++) {
            var itemType = itemTypes[i];
            if (itemType == -1 || !XmlLibrary.ItemDescs.TryGetValue((ushort)itemType, out var desc))
                continue;               // -1 = empty; an unknown type (a removed item) stays empty too

            _items[i] = new Item(desc.Root);
        }
    }

    public void SetItem(int slot, Item item) {
        if (slot < 0 || slot >= _size)
            return;

        if (item != null && _slotTypes[slot] != 0 && item.SlotType != _slotTypes[slot])
            return;
        
        _items[slot] = item;
        _itemUpdates.Set(slot);
    }
    
    // Puts the item in the first free general slot (not an equipment slot). Returns the slot, or -1 when the inventory is full.
    public int TryAdd(Item item) {
        for (var i = 0; i < _size; i++) {
            if (_items[i] != null || _slotTypes[i] != 0)
                continue;

            _items[i] = item;
            _itemUpdates.Set(i);
            return i;
        }

        return -1;
    }

    public void SetItems(IEnumerable<Item> items) {
        var slot = 0;
        foreach (var item in items) {
            if (item != null && _slotTypes[slot] != 0 && item.SlotType != _slotTypes[slot])
                continue;

            _items[slot] = item;
            _itemUpdates.Set(slot);
            slot++;
        }
    }

    public bool IsEmpty() {
        foreach (var item in _items)
            if (item != null)
                return false;
        return true;
    }
    
    // Nothing (an empty slot being swapped into this one) fits anywhere. Before 2026-09-21 a null item threw here, so taking an equipped item off
    // into an EMPTY inventory slot crashed the swap (the world tick logged a NullReferenceException and the client was never answered).
    public bool IsEquippable(Item item, int slot) {
        if (item == null)
            return true;
        var slotType = _slotTypes[slot];
        return slotType == 0 || slotType == item.SlotType;
    }

    public bool StackPotion(Item item) {
        if (item.ObjectType is not (2594 or 2595))
            return false;
        
        var stackIdx = item.ObjectType == 2594 ? 0 : 1;
        _potionStacks[stackIdx]++;
        return true;
    }

    public Item UnstackPotion(int slotFrom) {
        if (slotFrom is not (255 or 254))
            return null;

        var itemType = slotFrom == 255 ? 2594 : 2595;
        var item = new Item(XmlLibrary.ItemDescs[(ushort)itemType].Root);
        var stackIdx = item.ObjectType == 2594 ? 0 : 1;
        _potionStacks[stackIdx]--;
        return item;
    }

    public void SwapSlots(int slot1, int slot2) {
        if (slot1 < 0 || slot1 >= _size || slot2 < 0 || slot2 >= _size)
            return;
        
        var temp = _items[slot1];
        _items[slot1] = _items[slot2];
        _items[slot2] = temp;
        _itemUpdates.Set(slot1);
        _itemUpdates.Set(slot2);
    }

    public bool OwnedBy(int accId) {
        return OwnerAccIds.Count == 0 || OwnerAccIds.Contains(accId);
    }
    
    public void Tick(ref RealmTime time) {
        ref var stats = ref _world.EntityStats.Get(Id);
        if (stats.Id == EntityId.Null || _itemUpdates.IsEmpty)
            return;
        
        stats.Set(StatType.HealthPotionStack, _potionStacks[0]);
        stats.Set(StatType.MagicPotionStack, _potionStacks[1]);

        const int inventorySlots = (int)StatType.Inventory11 - (int)StatType.Inventory0 + 1;

        for (var i = 0; i < _size; i++)
            if (_itemUpdates.IsSet(i)) {
                // Slots beyond the 12 regular inventory slots are backpack slots - they need
                // their own StatType range, not a continuation of Inventory0+i, which would
                // overflow into unrelated stats (Attack, Defense, Speed, Vitality, Wisdom,
                // Dexterity, Condition1, NumStars, ...) for anyone with a backpack.
                // Slot 20 on is the Skill row (InventoryLayout): its own stats too - InventoryData0 + 20 would be Glow.
                var skill = i >= InventoryLayout.SkillSlot;
                var statType = skill ? StatType.Skill0 + (i - InventoryLayout.SkillSlot)
                    : i < inventorySlots ? StatType.Inventory0 + i : StatType.Backpack0 + (i - inventorySlots);
                stats.Set(statType, _items[i]?.ObjectType ?? -1);
                stats.Set(skill ? StatType.SkillData0 + (i - InventoryLayout.SkillSlot) : StatType.InventoryData0 + i, _items[i]?.ExportString());
            }

        _itemUpdates.Clear();
    }

    // Puts items into the slots (types; -1 or unknown = empty) and tells the client about every slot. Used to fill a Vault Chest from the account.
    public void LoadItems(int[] itemTypes) {
        for (var i = 0; i < _size; i++) {
            var type = itemTypes != null && i < itemTypes.Length ? itemTypes[i] : -1;
            _items[i] = type >= 0 && XmlLibrary.ItemDescs.TryGetValue((ushort)type, out var desc) ? new Item(desc.Root) : null;
            _itemUpdates.Set(i);
        }
    }

    // The item type in every slot, -1 for an empty one.
    public int[] ItemTypes() {
        var types = new int[_size];
        for (var i = 0; i < _size; i++)
            types[i] = _items[i]?.ObjectType ?? -1;
        return types;
    }

    public void Save(Character chr) {
        // Characters saved before the Skill slot (2026-09-23) have 20 entries; grow the array so slot 20 can be written.
        if (chr.ItemTypes == null || chr.ItemTypes.Length < _size) {
            var grown = Enumerable.Repeat(-1, _size).ToArray();
            chr.ItemTypes?.CopyTo(grown, 0);
            chr.ItemTypes = grown;
        }

        var itemDatas = new List<byte>();
        for (var i = 0; i < _size; i++) {
            var item = _items[i];
            if (item == null) {
                chr.ItemTypes[i] = -1;
                itemDatas.Add(0);
                continue;
            }
            
            chr.ItemTypes[i] = item.ObjectType;
            item.Export(itemDatas);
        }

        chr.ItemDatas = itemDatas.ToArray();
    }

    public void Dispose() {
        ArrayPool<int>.Shared.Return(_slotTypes);
        ArrayPool<Item>.Shared.Return(_items);
    }
}