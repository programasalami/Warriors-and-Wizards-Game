using Common.Structs;
using WaWClient.Game.Components.Options;
using WaWClient.Game.Objects;
using WaWClient.Ui;
using WaW.UiLib.BuiltIn;
using WaW.UiLib.Core;

namespace WaWClient.Game.Components.Hud.Inventory;

// The Skill row (2026-09-23): its own frame on the left edge, just above the gear, holding player slot 20 on (InventoryLayout). It starts
// with ONE slot; more slots make it wider (Width grows by SlotSize + Gap each), never taller. Only Skill items (SlotType 30) fit - the
// server checks that too. Same 48px walnut slots and 12px padding as the gear frame under it.
public sealed class SkillBar : Sprite {
    private const int SlotSize = 48;
    private const int Gap = 5;
    private const int Padding = 12;

    public const int Width = Padding * 2 + InventoryLayout.SkillSlots * SlotSize + (InventoryLayout.SkillSlots - 1) * Gap;
    public const int Height = Padding * 2 + SlotSize;

    private readonly Player _owner;
    private readonly ItemTile[] _tiles = new ItemTile[InventoryLayout.SkillSlots];

    public SkillBar(Player owner) {
        _owner = owner;
        AddChild(OptionsStyle.Panel(Width, Height));

        for (var i = 0; i < InventoryLayout.SkillSlots; i++) {
            var tile = new ItemTile(_owner, (byte)(InventoryLayout.SkillSlot + i), true, CutEdges.None, false, ItemConstants.SkillType, tileSize: SlotSize) {
                X = Padding + i * (SlotSize + Gap),
                Y = Padding
            };
            AddChild(tile);
            _tiles[i] = tile;
        }

        _owner.InventoryUpdate.Add(OnInventoryChange);
    }

    private void OnInventoryChange(int slot) {
        if (InventoryLayout.IsSkillSlot(slot))
            _tiles[slot - InventoryLayout.SkillSlot].SetItem(_owner.Equipment[slot]);
    }

    // Called by the HUD every frame, like the gear's Sync: redraw a tile whose picture no longer matches the inventory.
    public void Sync() {
        for (var i = 0; i < _tiles.Length; i++) {
            var item = _owner.Equipment[InventoryLayout.SkillSlot + i];
            if (!ReferenceEquals(_tiles[i].ItemDesc, item))
                _tiles[i].SetItem(item);
        }
    }
}
