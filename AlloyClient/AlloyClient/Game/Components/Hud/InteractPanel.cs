using AlloyClient.Game.Components.Hud.Panels;
using AlloyClient.Game.Components.Options;
using AlloyClient.Game.Objects;
using AlloyClient.Game.Objects.Util;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using Alloy.UiLib.Signals;

namespace AlloyClient.Game.Components.Hud;

public sealed class InteractPanel : Sprite {

    public static readonly SingleSignal<Panel> AddOverride = new();

    private Entity _currentObject;

    private Panel _currentPanel;

    private Panel _overridePanel;

    private readonly PartyPanel _partyPanel = new();

    // the walnut backdrop behind a portal / loot bag panel (the old HUD's grey rectangle used to be that backdrop)
    private readonly NineSliceRect _frame = OptionsStyle.Panel(Panel.PanelWidth, Panel.PanelHeight);

    public bool ShowsFrame => _frame.Visible;

    public InteractPanel() {
        _frame.Visible = false;
        AddChild(_frame);
        AddOverride.Set(SetOverride);
    }

    private void SetOverride(Panel panel) {
        if (_overridePanel != null)
            RemoveChild(_overridePanel);

        _currentPanel.Visible = false;
        _overridePanel = panel;
        _frame.Visible = true;
        AddChild(_overridePanel);
    }
    

    public void Update() {
        if (_overridePanel != null) {
            return;
        }

        if (!EntityUtils.FindClosestInteractableInRadius(Map.LocalPlayer.Position, 1f, out var obj)) {
            _currentObject = null;
            SetPanel(_partyPanel);
            return;
        }

        if (obj == _currentObject && _currentPanel != null) {
            return;
        }
        
        _currentObject = obj;
        SetPanel(GetInteractPanel(_currentObject));
    }

    private void SetPanel(Panel panel) {
        if (panel == _currentPanel) {
            return;
        }
        
        RemoveChild(_currentPanel);
        _currentPanel = panel;
        _frame.Visible = panel != null && panel != _partyPanel;

        if (_currentPanel == null)
            return;
        
        AddChild(_currentPanel);
    }
    
    public static bool IsInteractiveObject(Entity entity) {
        return entity.Properties.Class switch {
            "Container" => true,
            "OneWayContainer" => true,
            "Portal" => true,
            "BugBoard" => true,
            "NewsBoard" => true,
            "Jukebox" => true,
            _ => false
        };
    }

    private static Panel GetInteractPanel(Entity entity) {
        if (entity == null)
            return null;
        
        return entity.Properties.Class switch {
            "Container" => new ContainerPanel(entity, false),
            "OneWayContainer" => new ContainerPanel(entity, true),
            "Portal" => new PortalPanel(entity),
            "BugBoard" => new BugBoardPanel(entity),
            "NewsBoard" => new NewsBoardPanel(entity),
            "Jukebox" => new JukeboxPanel(entity),
            _ => null
        };
    }
}