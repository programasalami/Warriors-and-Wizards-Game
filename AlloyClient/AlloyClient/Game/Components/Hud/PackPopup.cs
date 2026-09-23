using AlloyClient.Game.Components.Hud.Inventory;

namespace AlloyClient.Game.Components.Hud;

// The backpack's eight slots, opened from the PACK side tab (that tab only appears once the character has a backpack).
public sealed class PackPopup : HudPopup {
    private readonly InventoryGrid _grid;

    // The backpack grid follows the inventory the same way as the main grid (see InventoryGrid.Sync).
    public void Sync() => _grid?.Sync();

    public PackPopup() : base("BACKPACK", PlayerPlate.Width, InventoryGrid.Height + 80) {
        // slot ids 12..19 are the backpack's
        _grid = new InventoryGrid(Map.LocalPlayer, 12, false, true) { Y = 64 };
        AddChild(_grid);
        OnResized();
    }

    protected override void OnResized() {
        if (_grid != null) {
            _grid.X = (PopupWidth - InventoryGrid.Width) / 2;
        }
    }
}
