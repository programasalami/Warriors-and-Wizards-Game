using System.Collections.Generic;
using AlloyClient.Data;
using AlloyClient.Display;
using AlloyClient.Game.Components.Hud.Inventory;
using AlloyClient.Game.Components.Hud.Panels;
using AlloyClient.Game.Components.Options;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;

namespace AlloyClient.Game.Components.Hud;

// The in-game HUD, in the walnut style of the options menu and the creation popup. No more full-height side rectangle - the world uses
// the whole screen and the pieces sit in its corners:
//   top-left     one framed plate: portrait, name plaque, health / mana / level bars, gold and fame
//   top-right    the minimap in a walnut frame
//   bottom-left  equipment, with the inventory directly under it
//   top edge     parchment tabs (stats / pack / menu / dev) hanging from the top of the screen, just right of the plate and growing toward the minimap
//   bottom       the nearby-players frame (always there), to the right of the gear, and the interact panel (portals, loot bags) to the right of that
// All positions are in the 1280x720 design space; GameScreen scales this whole sprite by Stage.ScreenScale and calls Layout with the
// screen size divided by that scale.
public sealed class HudView : Sprite {
    public const int Margin = 10;

    private const int GearGap = 4;
    private const int TabsGap = 14;        // between the player plate and the first tab
    private const int PopupGap = 8;       // above and below a popup, between the plate and the gear
    private const int MinimapFrame = Minimap.MapSize + 20;

    private readonly Sprite _minimapBox;
    private readonly PlayerPlate _plate;

    private Sprite _gear;
    private EquippedGrid _equipped;
    private InventoryGrid _inventory;
    private HudTabs _tabs;
    private StatsPopup _stats;
    private PackPopup _pack;
    private NearbyPlayersPanel _nearby;
    private InteractPanel _interact;

    // set by GameScreen: the MENU tab opens the options, the DEV tab shows / hides the FPS and memory readout
    public System.Action OnOpenMenu;
    public System.Action OnOpenAdmin;
    public System.Action<bool> OnDevChanged;

    private bool _hadBackpack;
    private int _designW = Settings.DefaultScreenWidth;
    private int _designH = Settings.DefaultScreenHeight;

    public HudView() {
        _minimapBox = new Sprite();
        _minimapBox.AddChild(WaWStyle.Panel(MinimapFrame, MinimapFrame));
        _minimapBox.AddChild(new Minimap { X = 10, Y = 10 });
        AddChild(_minimapBox);

        _plate = new PlayerPlate();
        AddChild(_plate);
    }

    public void CreatePlayerDependentAssets() {
        RemoveChild(_gear);
        RemoveChild(_tabs);
        RemoveChild(_stats);
        RemoveChild(_pack);
        RemoveChild(_nearby);
        RemoveChild(_interact);

        _gear = new Sprite();
        _equipped = new EquippedGrid(Map.LocalPlayer);
        _inventory = new InventoryGrid(Map.LocalPlayer, 4, false) { Y = EquippedGrid.Height + GearGap };
        _gear.AddChild(_equipped);
        _gear.AddChild(_inventory);
        AddChild(_gear);

        _nearby = new NearbyPlayersPanel();
        AddChild(_nearby);

        _interact = new InteractPanel();
        AddChild(_interact);

        _tabs = new HudTabs { OnTabClicked = ToggleTab };
        _tabs.SetTabVisible("admin", Admin.AdminRules.IsStaff(GlobalData.Get<AccountData>()?.Rank ?? 0));       // the server still checks every command
        AddChild(_tabs);

        _stats = new StatsPopup { OnClosed = () => _tabs.SetActive("stats", false) };
        _pack = new PackPopup { OnClosed = () => _tabs.SetActive("pack", false) };
        AddChild(_stats);
        AddChild(_pack);

        Layout(_designW, _designH);
    }

    // Only one tab-controlled thing is ever open: the Stats popup, the Backpack popup or the DEV readout. Opening any of them closes the
    // others; the MENU tab is handled by GameScreen (it closes all of these first, and the options menu then locks the screen anyway).
    public bool DevOpen { get; private set; }

    public void CloseTabs() {
        _stats?.Close();
        _pack?.Close();
        SetDev(false);
    }

    public void ToggleTab(string key) {
        if (_tabs == null) {
            return;
        }

        switch (key) {
            case "stats":
                if (_stats.IsOpen) {
                    _stats.Close();
                } else {
                    CloseTabs();
                    _stats.Open();
                    _tabs.SetActive("stats", true);
                }
                break;
            case "pack":
                if (_pack.IsOpen) {
                    _pack.Close();
                } else {
                    CloseTabs();
                    _pack.Open();
                    _tabs.SetActive("pack", true);
                }
                break;
            case "dev":
                if (DevOpen) {
                    SetDev(false);
                } else {
                    CloseTabs();
                    SetDev(true);
                }
                break;
            case "menu":
                OnOpenMenu?.Invoke();
                break;
            case "admin":
                OnOpenAdmin?.Invoke();
                break;
        }
    }

    private void SetDev(bool on) {
        if (DevOpen == on) {
            return;
        }

        DevOpen = on;
        _tabs?.SetActive("dev", on);
        OnDevChanged?.Invoke(on);
    }

    public void Update() {
        if (Map.LocalPlayer == null || _gear == null) {
            return;
        }

        _plate.Update();
        _equipped.UpdateAbilitySlot();
        _nearby.Update();
        _interact.Update();
        if (Map.LocalPlayer.HasBackPack != _hadBackpack) {
            _hadBackpack = Map.LocalPlayer.HasBackPack;
            _tabs.SetTabVisible("pack", _hadBackpack);
            Layout(_designW, _designH);
        }
        if (_stats.IsOpen) {
            _stats.Refresh();
        }
    }

    // The minimap frame's left edge and top edge in design units - the FPS / memory readout hangs off them.
    public float MinimapLeft => _minimapBox.X;
    public float MinimapTop => _minimapBox.Y;

    // Positions everything for a screen of designW x designH design units (the real size divided by the HUD's scale).
    public void Layout(int designW, int designH) {
        _designW = designW;
        _designH = designH;

        _plate.X = Margin;
        _plate.Y = Margin;

        _minimapBox.X = designW - MinimapFrame - Margin;
        _minimapBox.Y = Margin;

        if (_gear == null) {
            return;
        }

        var gearHeight = EquippedGrid.Height + GearGap + InventoryGrid.Height;
        _gear.X = Margin;
        _gear.Y = designH - gearHeight - Margin;

        // the nearby-players frame sits where the interact panel used to pop up; the interact panel slides over to its right, on the same line
        _nearby.X = Margin + EquippedGrid.Width + 12;
        _nearby.Y = designH - Panel.PanelHeight - Margin;
        _interact.X = _nearby.X + NearbyPlayersPanel.Width + 12;
        _interact.Y = _nearby.Y;

        // the tabs hang from the top edge in the middle; the popups open in the gap between the plate and the gear, lined up with the plate's sides
        _tabs.X = Margin + PlayerPlate.Width + TabsGap;
        _tabs.Y = 0;

        var top = Margin + PlayerPlate.Height;
        var bottom = (int) _gear.Y;
        PlacePopup(_stats, top, bottom);
        PlacePopup(_pack, top, bottom);
    }

    private void PlacePopup(HudPopup popup, int top, int bottom) {
        popup.X = Margin;
        popup.Y = top + PopupGap;
        popup.Resize(PlayerPlate.Width, System.Math.Max(200, bottom - top - 2 * PopupGap));
    }

    // True if a click at this screen position (pixels) landed on a piece of HUD, so it must not fire the weapon.
    public bool IsOver(int pixelX, int pixelY, float scale) {
        var x = pixelX / scale;
        var y = pixelY / scale;
        foreach (var (rx, ry, rw, rh) in Rects()) {
            if (x >= rx && x < rx + rw && y >= ry && y < ry + rh) {
                return true;
            }
        }
        return false;
    }

    private IEnumerable<(float X, float Y, float W, float H)> Rects() {
        yield return (_plate.X, _plate.Y, PlayerPlate.Width, PlayerPlate.Height);
        yield return (_minimapBox.X, _minimapBox.Y, MinimapFrame, MinimapFrame);

        if (_gear == null) {
            yield break;
        }

        yield return (_gear.X, _gear.Y, EquippedGrid.Width, EquippedGrid.Height + GearGap + InventoryGrid.Height);
        yield return (_tabs.X, 0, _tabs.TotalWidth, HudTabs.ReachHeight);
        yield return (_nearby.X, _nearby.Y, NearbyPlayersPanel.Width, NearbyPlayersPanel.Height);
        if (_nearby.MenuRect is { } menu) {
            yield return menu;
        }
        if (_interact.ShowsFrame) {
            yield return (_interact.X, _interact.Y, Panel.PanelWidth, Panel.PanelHeight);
        }
        if (_stats.IsOpen) {
            yield return (_stats.X, _stats.Y, _stats.PopupWidth, _stats.PopupHeight);
        }
        if (_pack.IsOpen) {
            yield return (_pack.X, _pack.Y, _pack.PopupWidth, _pack.PopupHeight);
        }
    }
}
