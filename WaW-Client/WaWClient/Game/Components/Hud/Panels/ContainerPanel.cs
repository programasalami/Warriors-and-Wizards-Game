using WaWClient.Game.Components.Hud.Inventory;
using WaWClient.Game.Objects;

namespace WaWClient.Game.Components.Hud.Panels;

public class ContainerPanel : Panel {

    public ContainerPanel(Entity entity, bool oneWay) {
        var grid = new InventoryGrid(entity, 0, oneWay, false);
        AddChild(grid);
    }
    
}