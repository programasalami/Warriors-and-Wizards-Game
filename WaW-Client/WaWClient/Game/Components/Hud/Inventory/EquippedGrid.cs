using System.Collections.Generic;
using WaWClient.Game.Components.Options;
using WaWClient.Assets.XmlStructs;
using WaWClient.Game.Objects;
using WaW.UiLib.BuiltIn;
using WaW.UiLib.Core;

namespace WaWClient.Game.Components.Hud.Inventory;

public sealed class EquippedGrid : Sprite {

    // Same width as the inventory panel underneath, and the same 48px slots / 5px gaps (12px of padding inside the frame), so the columns line up.
    public const int Width = 232;
    public const int Height = 72;
    private const int SlotSize = 48;
    private const int Gap = 5;

    private const byte NumSlots = 4;

    private static readonly CutEdges[] Cuts = [CutEdges.Left, CutEdges.None, CutEdges.None, CutEdges.Right];
    private readonly ItemTile[] _tileSlots = new ItemTile[4];

    private readonly Player _owner;

    public EquippedGrid(Player owner) {
        _owner = owner;
        
        AddChild(OptionsStyle.Panel(Width, Height));
        
        _owner.InventoryUpdate.Add(OnInventoryChange);

        for (byte i = 0; i < NumSlots; i++) {
            var slot = new ItemTile(_owner, i, true, Cuts[i], false, (byte)_owner.Properties.SlotTypes[i], tileSize: SlotSize);
            slot.X = i % 4 * (SlotSize + Gap) + 12;
            slot.Y = 12;
            AddChild(slot);
            _tileSlots[i] = slot;
        }
    }
    
    public EquippedGrid(ItemDesc[] items, List<int> slotTypes) {
        AddChild(OptionsStyle.Panel(Width, Height));

        for (byte i = 0; i < NumSlots; i++) {
            var slot = new ItemTile(null, i, false, Cuts[i], false, (byte)slotTypes[i], tileSize: SlotSize);
            slot.X = i % 4 * (SlotSize + Gap) + 12;
            slot.Y = 12;
            slot.SetItem(items[i]);
            AddChild(slot);
            _tileSlots[i] = slot;
        }
    }

    public void UpdateAbilitySlot() {
        var slot = _tileSlots[1];

        if (slot.ItemDesc == null || slot.ItemDesc.ObjectType == 0)
            return;

        var noMana = Map.LocalPlayer.Mp < slot.ItemDesc.MpCost;
        //todo: silence check
        
        slot.SetDim(noMana);
    }

    private void OnInventoryChange(int slot) {
        if (slot >= NumSlots) return;
        _tileSlots[slot].SetItem(_owner.Equipment[slot]);
    }

    // See InventoryGrid.Sync: redraw a gear tile whose picture no longer matches the player's inventory (called by the HUD every frame).
    public void Sync() {
        for (var i = 0; i < NumSlots; i++) {
            if (!ReferenceEquals(_tileSlots[i].ItemDesc, _owner.Equipment[i]))
                _tileSlots[i].SetItem(_owner.Equipment[i]);
        }
    }
}
