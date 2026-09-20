using Alloy.UiLib.Core;
using Alloy.UiLib.Signals;

namespace AlloyClient.Game.Components.Hud.Panels;

public abstract class Panel : Sprite {

    // The interact area at the bottom of the HUD. A panel has no size of its own (it is just a holder for children, and the old
    // SetBaseDimensions(218, 110) call below never got ported), so anything centred on `Width / 2` ended up centred on the panel's
    // LEFT edge. Lay panels out against these fixed numbers instead.
    public const int PanelWidth = 232;
    public const int PanelHeight = 126;

    public static readonly Signal OnInteract = new(); 
    
    protected Panel() {
        //todo:SetBaseDimensions(218, 110);
        AddEventListener(Event.AddedToStage, () => { OnInteract.Add(OnInteractKey); });
        AddEventListener(Event.RemovedFromStage, () => { OnInteract.Remove(OnInteractKey); });
    }

    protected virtual void OnInteractKey() {
        
    }
}