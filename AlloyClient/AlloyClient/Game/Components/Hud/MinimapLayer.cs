using System;
using Alloy.Common;
using AlloyClient.Game.Objects;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using Alloy.UiLib.Rendering;
using OpenTK.Mathematics;

namespace AlloyClient.Game.Components.Hud;

public sealed class MinimapLayer : Container {

    // |ratio| beyond this puts the dot (3.25px radius) partly or wholly outside the 230px map
    private const float DotLimit = 1f - 4f / (Minimap.MapSize / 2f);

    private const int MaxEntities = 1000;
    private const int VertexSize = MaxEntities * 4;
    private const int IndexSize = MaxEntities * 6;

    private int _count = 0;
    private float _size;

    private static Entity _focus;

    public MinimapLayer() : base(new ContainerConfig { EnableClip = true, Width = Minimap.MapSize, Height = Minimap.MapSize }) {
        // Not mouse-enabled: Container turns that on for clip containers, and this layer must not swallow clicks meant for the map.
        MouseEnabled = false;
        TextureId = TextureType.Color;
        
        AddEventListener(Event.EnterFrame, OnFrameEnter);

        ResizeBackBuffer();
    }

    private void ResizeBackBuffer() {
        VertexData = new VertexUi[VertexSize];
        Indices = new ushort[IndexSize];
        OverridePrimCount = 0;

        // A sprite only draws once SetGraphicsBuffer() has run (it starts with "no render data"), and this layer never called it,
        // so the dots were computed every frame but never drawn. SetGraphicsBuffer also sizes the clip rect from the largest vertex
        // position, so park one unused vertex in the far corner to make that the full map size instead of 0x0.
        VertexData[0] = new VertexUi(new Vector2(Minimap.MapSize, Minimap.MapSize));
        SetGraphicsBuffer();
    }

    public static void SetFocus(Entity entity) {
        _focus = entity;
    }

    public void SetSize(float size) => _size = size;

    private void AddObject(Vector2 pos, uint rgb) {
        if (_count >= MaxEntities) return;

        const float size = 3.25f;

        var color = Color.FromHexRGB(rgb);
        VertexData[_count * 4 + 0] = new VertexUi(new Vector2(pos.X - size, pos.Y - size), color);
        VertexData[_count * 4 + 1] = new VertexUi(new Vector2(pos.X + size, pos.Y - size), color);
        VertexData[_count * 4 + 2] = new VertexUi(new Vector2(pos.X + size, pos.Y + size), color);
        VertexData[_count * 4 + 3] = new VertexUi(new Vector2(pos.X - size, pos.Y + size), color);

        Indices[_count * 6 + 0] = (ushort)(_count * 4 + 0);
        Indices[_count * 6 + 1] = (ushort)(_count * 4 + 1);
        Indices[_count * 6 + 2] = (ushort)(_count * 4 + 2);
        Indices[_count * 6 + 3] = (ushort)(_count * 4 + 0);
        Indices[_count * 6 + 4] = (ushort)(_count * 4 + 2);
        Indices[_count * 6 + 5] = (ushort)(_count * 4 + 3);

        _count++;
        OverridePrimCount += 2;
    }

    private void OnFrameEnter() {
        if (Map.LocalPlayer == null) return;
        
        _count = 0;
        OverridePrimCount = 0;

        foreach (var kvp in Map.Entities) {
            var entity = kvp.Value;
            
            if (entity.Properties.Static || entity.Properties.NoMiniMap || entity.ObjectId == _focus.ObjectId) continue;

            var fillColor = 0u;
            
            if (entity is Player player) {
                if (player.HasConditionEffect(ConditionEffect.Paused)) {
                    fillColor = 0x7F7F7F;
                } else if (player.IsFellowGuild) {
                    fillColor = 0x00FF00;
                } else {
                    fillColor = 0xFFFF00;
                }
            } else {
                if (entity.Properties.IsEnemy) {
                    fillColor = 0xFF0000;
                } else if (entity.Properties.Class is "Portal" or "GuildHallPortal") {
                    fillColor = 0x0000FF;
                } else {
                    continue;
                }
            }

            var ratio = (entity.Position - Map.LocalPlayer.Position) / _size;

            // Only entities inside the map window get a dot (kept a dot's radius inside the edge). Relying on the clip rectangle alone
            // left dots for far-away entities floating around the screen instead of being hidden.
            if (MathF.Abs(ratio.X) > DotLimit || MathF.Abs(ratio.Y) > DotLimit) {
                continue;
            }

            var pos = new Vector2(Minimap.MapSize / 2f) + new Vector2(Minimap.MapSize / 2f) * ratio;
            AddObject(pos, fillColor);
        }

    }
}