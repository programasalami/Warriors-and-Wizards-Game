using System;
using System.Collections.Generic;
using AlloyClient.Game.Components.Options;
using AlloyClient.Ui;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using Alloy.UiLib.Extra;

namespace AlloyClient.Game.Components.Hud;

// The parchment tabs hanging from the top edge of the screen, in the middle. Each one is mostly above the screen; hovering slides it down and
// clicking it opens (or closes) its popup - Stats, Backpack, Menu (the options) - or toggles the DEV readout; staff also get an ADMIN tab (the dashboard). The icons are the user's own art
// (Content/Ui/Tabs).
public sealed class HudTabs : Sprite {
    public const int TabWidth = 60;
    public const int TabHeight = 104;
    public const int Gap = 8;
    // how much of a tab is above the top of the screen while it rests
    private const int Hidden = 52;
    // how far a tab drops when hovered / when its popup is open
    private const int HoverOut = 12;
    private const int ActiveOut = 20;
    private const int IconSize = 32;

    private static readonly ColorTransform ActiveTint = new(1.12f, 1.0f, 0.78f, 1f);

    private readonly List<HangingTab> _tabs = [];

    public Action<string> OnTabClicked;

    public HudTabs() {
        Add("stats", "Tabs/StatTab", null);
        Add("pack", "Tabs/BackpackTab", null);
        Add("menu", "Tabs/MenuTab", null);
        Add("dev", "Tabs/DevTab", null);
        Add("admin", "Tabs/AdminTab", null);
        SetTabVisible("pack", false);
        SetTabVisible("admin", false);       // shown only to moderators and owners (see HudView)
    }

    private void Add(string key, string iconLookup, string text) {
        var tab = new HangingTab(key, iconLookup, text, this);
        _tabs.Add(tab);
        AddChild(tab);
        LayoutTabs();
    }

    public void SetTabVisible(string key, bool visible) {
        foreach (var tab in _tabs) {
            if (tab.Key == key) {
                tab.Visible = visible;
            }
        }
        LayoutTabs();
    }

    public void SetActive(string key, bool active) {
        foreach (var tab in _tabs) {
            if (tab.Key == key) {
                tab.Active = active;
            }
        }
    }

    // Lines the visible tabs up left to right.
    private void LayoutTabs() {
        var x = 0;
        foreach (var tab in _tabs) {
            if (!tab.Visible) {
                continue;
            }
            tab.X = x;
            x += TabWidth + Gap;
        }
    }

    public int TotalWidth {
        get {
            var visible = 0;
            foreach (var tab in _tabs) {
                if (tab.Visible) {
                    visible++;
                }
            }
            return visible == 0 ? 0 : visible * TabWidth + (visible - 1) * Gap;
        }
    }

    // How far the tabs reach down into the screen at most (used to keep clicks on them from shooting).
    public static int ReachHeight => TabHeight - Hidden + ActiveOut;

    private sealed class HangingTab : Container {
        public readonly string Key;
        public bool Active;

        private readonly NineSliceRect _plate;
        private bool _hovered;

        public HangingTab(string key, string iconLookup, string text, HudTabs owner) {
            Key = key;
            MouseEnabled = true;
            Y = -Hidden;

            _plate = new NineSliceRect(new NineSliceConfig {
                SliceData = SliceLibrary.DarkAgesParchment,
                CutX = OptionsStyle.ScrollCut,
                CutY = OptionsStyle.ScrollCut,
                Width = TabWidth,
                Height = TabHeight
            });
            AddChild(_plate);

            // centred in the part that hangs into the screen
            var visibleCentreY = Hidden + (TabHeight - Hidden) / 2;
            if (iconLookup != null) {
                var icon = WaWStyle.Icon(iconLookup, IconSize, IconSize, 1);
                icon.X = (TabWidth - IconSize) / 2;
                icon.Y = visibleCentreY - IconSize / 2;
                AddChild(icon);
            } else {
                AddChild(OptionsStyle.Label(text, FontGroup.MyriadPro, 15f, TabWidth / 2, visibleCentreY, UiAnchor.Middle, OptionsStyle.ParchmentInk));
            }

            AddEventListener(MouseEvent.MouseOver, () => _hovered = true);
            AddEventListener(MouseEvent.MouseOut, () => _hovered = false);
            AddEventListener(MouseEvent.LeftClick, () => owner.OnTabClicked?.Invoke(Key));
            AddEventListener(Event.EnterFrame, OnFrame);
        }

        // Eased by hand every frame rather than with a tween: a tween marks the sprite as busy, which blocks its mouse events and would make the
        // hover flicker on and off as it slides.
        private void OnFrame() {
            var target = -Hidden + (Active ? ActiveOut : _hovered ? HoverOut : 0);
            var dt = (float) Stage.GameTime.ElapsedMs;
            var k = 1f - MathF.Exp(-dt / 55f);
            var next = Y + (target - Y) * k;
            Y = Math.Abs(target - next) < 0.5f ? target : (int) MathF.Round(next);

            _plate.ColorTransformation = Active ? ActiveTint : _hovered ? OptionsStyle.SlotHoverTint : OptionsStyle.NormalTint;
        }
    }
}
