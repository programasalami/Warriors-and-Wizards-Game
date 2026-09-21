using System.Collections.Generic;
using AlloyClient.Display;
using AlloyClient.Networking;
using AlloyClient.Ui;
using AlloyClient.Ui.Components.Panels;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using Alloy.UiLib.Signals;

namespace AlloyClient.Game.Components.Options;

public sealed class OptionsView : Overlay {
    
    public const string ControlsTab = "Controls";
    public const string HotkeysTab = "Hot Keys";
    public const string ChatTab = "Chat";
    public const string GraphicsTab = "Graphics";
    public const string SoundTab = "Sound";
    public const string ExtraTab = "Extra";

    private static readonly string[] Tabs = [ControlsTab, HotkeysTab, ChatTab, GraphicsTab, SoundTab, ExtraTab];

    // Layout on the 1280x720 design canvas: one big walnut panel, the title and a row of parchment tabs across its top, the
    // active tab's options in the middle and three parchment buttons along the bottom.
    private const int PanelX = 40;
    private const int PanelY = 24;
    private const int PanelWidth = 1200;
    private const int PanelHeight = 672;
    private const int PanelCut = 16;

    private const int TitleY = 62;
    private const int TabsY = 100;
    private const int TabWidth = 170;
    private const int TabHeight = 44;
    private const int TabGap = 12;
    public const int ViewX = PanelX;
    public const int ViewY = 160;
    public const int ViewWidth = PanelWidth;
    public const int ViewHeight = 456;
    private const int ButtonsY = 636;
    private const int ButtonHeight = 48;

    private readonly Dictionary<string, OptionTabView> _tabViews = [];

    public static readonly SingleSignal RefreshOptions = new ();//TODO: holds refs, redo

    private ParchmentButton _selectedTab;

    // Every child below is laid out on the 1280x720 design canvas from this sprite's own top-left corner, so the whole canvas is centred (see Overlay.FixedSize).
    public override (int Width, int Height)? FixedSize => (Settings.DefaultScreenWidth, Settings.DefaultScreenHeight);

    public OptionsView() {
        RefreshOptions.Set(Refresh);

        //todo:SetBaseDimensions(Settings.DefaultScreenWidth, Settings.DefaultScreenHeight);

        var panel = OptionsStyle.Panel(PanelWidth, PanelHeight);
        panel.X = PanelX;
        panel.Y = PanelY;
        AddChild(panel);

        AddChild(OptionsStyle.Label("OPTIONS", FontGroup.MyriadPro, 44f, Settings.DefaultScreenWidth / 2, TitleY, UiAnchor.Middle, OptionsStyle.Gold, 2));

        var centerX = Settings.DefaultScreenWidth / 2;
        AddChild(new ParchmentButton("CONTINUE", "continue", 210, ButtonHeight, 26f, OnContinue) { X = centerX - 105, Y = ButtonsY });
        AddChild(new ParchmentButton("RESET TO DEFAULTS", "reset", 320, ButtonHeight, 24f, OnResetToDefaults) { X = PanelX + 40, Y = ButtonsY });
        AddChild(new ParchmentButton("HOME", "home", 170, ButtonHeight, 26f, OnHome) { X = PanelX + PanelWidth - 40 - 170, Y = ButtonsY });

        AddTabs();
    }

    private void AddTabs() {
        var first = true;
        var xOffset = (Settings.DefaultScreenWidth - (Tabs.Length * TabWidth + (Tabs.Length - 1) * TabGap)) / 2;
        foreach (var tabName in Tabs) {
            var tab = new ParchmentButton(tabName.ToUpperInvariant(), tabName, TabWidth, TabHeight, 22f, null, false) {
                X = xOffset,
                Y = TabsY
            };
            tab.AddEventListener(MouseEvent.LeftClick, OnSelectTab);
            AddChild(tab);

            var view = new OptionTabView(tabName) {
                Visible = false,
                X = ViewX,
                Y = ViewY
            };
            _tabViews[tabName] = view;
            AddChild(view);

            if (first) {
                first = false;
                SelectTab(tab);
            }

            xOffset += TabWidth + TabGap;
        }
    }

    private void OnSelectTab(MouseEvent args) {
        SelectTab(args.CurrentTarget as ParchmentButton);
    }

    private void SelectTab(ParchmentButton tab) {
        if (_selectedTab != null) {
            _selectedTab.SetSelected(false);
            _tabViews[_selectedTab.Key].Visible = false;
        }

        _selectedTab = tab;
        _selectedTab!.SetSelected(true);
        _tabViews[_selectedTab.Key].Visible = true;
    }
    
    public void Refresh() {
        foreach (var tabView in _tabViews.Values) {
            tabView.Refresh();
        }
    }

    private void OnResetToDefaults() {
        Settings.ResetToDefault();
        Refresh();
    }

    private void OnContinue() {
        CloseOverlay();
        UserInput.SetManualFocus(true);
    }

    private void OnHome() {
        OnContinue();
        Client.Disconnect();
        Map.Reset();
    }
}