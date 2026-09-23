using WaWClient.Game.Components.Options;
using WaWClient.Game.Objects;
using WaW.UiLib.BuiltIn;
using WaW.UiLib.Core;

namespace WaWClient.Game.Components.Hud.Inventory;

public sealed class InventoryGrid : Sprite {

    // 8 slots in two rows of four: 12px padding inside the frame, 48px slots, 5px gaps. Matches EquippedGrid's width.
    public const int Width = 232;
    public const int Height = 126;

    private const int NumSlots = 8;

    private static readonly CutEdges[] Cuts = [CutEdges.TopLeft, CutEdges.None, CutEdges.None, CutEdges.TopRight, CutEdges.BottomLeft, CutEdges.None, CutEdges.None, CutEdges.BottomRight];
    private readonly ItemTile[] _tiles = new ItemTile[NumSlots];

    private readonly Entity _owner;

    private readonly int _offset;

    private readonly bool _interactive;

    private bool _backpack;


    public InventoryGrid(Entity owner, int offset, bool oneWay = false, bool isBackpack = false) {
        _owner = owner;
        _offset = offset;
        _backpack = isBackpack;
        _interactive = owner == Map.LocalPlayer || owner.Properties.Container;

        if (owner == Map.LocalPlayer) {
            AddChild(OptionsStyle.Panel(Width, Height));
        }

        _owner.InventoryUpdate.Add(OnInventoryChange);

        for (var i = 0; i < NumSlots; i++)
        {
            var slot = new ItemTile(owner, (byte)(i + offset), _interactive, Cuts[i], oneWay, tileSize: 48);
            slot.SetTileNumber(i + 1);
            slot.X = i % 4 * (48 + 5) + 12;
            slot.Y = i / 4 * (48 + 5) + 12;
            AddChild(slot);
            _tiles[i] = slot;
        }
    }
    
    private void OnInventoryChange(int slot) 
    {
        if (!Visible) //Unsure how reliable this is, its to stop issues with the Backpack & Inventory trying to update when hidden
        {
            return;
        }

        if (slot < _offset || slot >= _offset + NumSlots) return;
        _tiles[slot - _offset].SetItem(_owner.Equipment[slot]);
    }
    

    // Self-healing display (2026-09-22): redraws every tile whose picture no longer matches the owner's inventory. The HUD calls it each frame for the
    // player's own grids. The InventoryUpdate signal is still the normal path; this catches an update that was missed (in the browser build a /give
    // put the item in the inventory but the slot stayed empty until the HUD was rebuilt - the trigger that was lost could not be found by reading).
    // Eight reference comparisons per frame; SetItem only runs on a mismatch.
    public void Sync() {
        for (var i = 0; i < NumSlots; i++) {
            var slot = i + _offset;
            var item = slot < _owner.Equipment.Length ? _owner.Equipment[slot] : null;
            if (!ReferenceEquals(_tiles[i].ItemDesc, item))
                _tiles[i].SetItem(item);
        }
    }
}
