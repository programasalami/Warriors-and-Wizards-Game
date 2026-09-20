using System;
using AlloyClient.Game.Components.Options;
using AlloyClient.Game.Objects;
using AlloyClient.Networking;
using AlloyClient.Networking.Packets.Outgoing;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using Alloy.Common;
using AlloyClient.Ui.Components.Buttons;

namespace AlloyClient.Game.Components.Hud.Panels;

public class PortalPanel : Panel {

    private readonly Entity _portal;

    private readonly bool _locked;
    
    private readonly SimpleText _fullText;

    private readonly Container _enterButton;

    public PortalPanel(Entity entity) {
        _portal = entity;
        _locked = entity.Properties.LockedPortal;

        var txt = _portal.Properties.DisplayName;

        if (_locked && txt.StartsWith("Locked", StringComparison.Ordinal)) {
            txt = txt[7..];
        }

        var name = new SimpleText(new TextConfig {
            X = PanelWidth / 2,
            Y = 12,
            Text = txt,
            FontSize = 22,
            FontType = FontType.Bold,
            Color = WaWStyle.Highlight,
            OutlineColor = OptionsStyle.OutlineDark,
            OutlineThickness = 1,
            Anchor = UiAnchor.MiddleTop,
            MaxWidth = PanelWidth - 16
        });
        AddChild(name);
        
        _fullText = new SimpleText(new TextConfig {
            X = PanelWidth / 2,
            Y = name.Height + 50,
            Text = _locked ? "Locked" : "Full",
            FontSize = 20,
            FontType = FontType.Bold,
            OutlineColor = 0xFF0000,
            Color = 0xFF0000,
            Anchor = UiAnchor.MiddleTop
        });
        _fullText.Y = name.Height + 10;

        const int buttonWidth = 120;
        _enterButton = WaWStyle.TextButton("Enter", buttonWidth, 36, 18f, OnInteractKey);
        _enterButton.X = PanelWidth / 2 - buttonWidth / 2;
        _enterButton.Y = name.Height + 46;
        AddChild(_enterButton);
        
        AddEventListener(Event.AddedToStage, () => { AddEventListener(Event.EnterFrame, OnFrameEnter);});
        AddEventListener(Event.RemovedFromStage, () => { RemoveEventListener(Event.EnterFrame, OnFrameEnter);});
    }

    protected override void OnInteractKey() {
        var pkt = UsePortal.CreatePacket();
        pkt.ObjectId = _portal.ObjectId;
        Client.QueuePacket(pkt);
    }

    private void OnFrameEnter() {
        if ((!_portal.PortalUsable || _locked) && Contains(_enterButton)) {
            RemoveChild(_enterButton);
            AddChild(_fullText);
        }

        if ((_portal.PortalUsable && !_locked) && Contains(_fullText)) {
            RemoveChild(_fullText);
            AddChild(_enterButton);
        }
    }
}