using System;
using System.Collections.Generic;
using Alloy.Common;
using AlloyClient.Assets;
using AlloyClient.Assets.Libraries;
using AlloyClient.Game.Objects;
using AlloyClient.Rendering.Types.SubTypes;
using AlloyClient.Rendering.VertexData;
using OpenTK.Mathematics;

namespace AlloyClient.Rendering.Types;

public sealed class TypeGameObject : RenderBase {
    
    public override ModelType ModelType {
        get => ModelType.PbObject;
    }

    public override bool HasShadow {
        get => true;
    }

    // Per-object <BottomInset> from XML (default 0) - lets sheets whose art has a built-in margin or
    // a base patch below the "contact point" (e.g. the forest trees, whose grass patch is centred on
    // the shadow) sit on their shadow instead of floating above it. Looked up by type because
    // SetTexture runs in this constructor before Entity.Properties is guaranteed to be assigned.
    protected override float TextureBottomInset =>
        ObjectLibrary.TypeToObjectProps.TryGetValue(Entity.Type, out var props) ? props.BottomInset : 0f;

    // Sort id for flat props: behind every standing object (standing depths span ~0.1-0.9).
    private const float FlatSortId = 0.97f;

    private readonly bool _flat;

    private readonly TypeName _name;

    private readonly TypeHpBar _hpBar;
    private readonly TypeEffects _effects;

    public TypeGameObject(Entity entity) {
        Entity = entity;
        _flat = ObjectLibrary.TypeToObjectProps.TryGetValue(entity.Type, out var flatProps) && flatProps.FlatOnGround;
        SetTexture(entity.GetTexture());
        Extra = new ExtraData(RenderConfig.TypeGameObject, RenderConfig.Shade);
        _name = new TypeName(this, entity);
        _hpBar = new TypeHpBar(this, entity);
        _effects = new TypeEffects(this, entity);

        Color = Color.Black;
    }
    
    public override void SetPosition(float x, float y, float z = 0) {
        Position.X = x;
        Position.Y = y;
        Position.Z = z;
    }
    
    public override void SetVisibility(bool visible) {
        Visible = visible;
    }

    public override void SetDepth(float depth) {
        Extra.SortId = _flat ? FlatSortId : depth;
        _name.SetDepth(depth);
        _hpBar.SetDepth(depth);
        _effects.SetDepth(depth);
    }
    
    public override void SetAlpha(float alpha) {
        Extra.Alpha = alpha;
        _name.SetAlpha(alpha);
        _hpBar.SetAlpha(alpha);
        _effects.SetAlpha(alpha);
    }

    public override void SetName(string name) { }

    public override void Draw(List<VertexObject> targets, double time) {
        if (_flat) {
            DrawFlat(targets);
            return;
        }

        var s = MathF.Sin(-Entity.Rotation);
        var c = MathF.Cos(-Entity.Rotation);
        var k = Entity.Size / 100f;
        var f = Entity.Flipped ? 1f : -1f;
        Rotation = new Vector4(s, c, k, f);
        
        Entity.HeightOffset = -1 * Scale.Y * k + Scale.W * k;
        
        targets.Add(new VertexObject(Position, UV, Scale, Rotation, Extra, Color));
        
        if (Entity.Properties.Static) return;
        
        var y = 0.1f;

        if (Entity.MaxHp != 0) {
            _hpBar.SetFill(1f * Entity.Hp / Entity.MaxHp);
            _hpBar.Draw(y, targets, time);
        }
        
        _effects.Draw(Entity.HeightOffset, targets, time);
    }

    // Lying flat: Object.vert's billboard matrix turns every sprite by +CameraAngle, so pre-rotating the
    // quad by -CameraAngle cancels that and leaves it aligned to the world, i.e. it turns with the ground
    // under a rotating camera. (This used to add +CameraAngle, which doubled the turn instead of cancelling it -
    // verified by simulating the shader with the real camera matrices; see Projectile.Update for the same fix.)
    // The pivot is the sprite's centre (no base pad).
    private void DrawFlat(List<VertexObject> targets) {
        var angle = Entity.Rotation - Settings.CameraAngle;
        var k = Entity.Size / 100f;
        var f = Entity.Flipped ? 1f : -1f;
        var flatScale = new Vector4(Scale.X, Scale.Y, 0f, 0f);
        targets.Add(new VertexObject(Position, UV, flatScale, new Vector4(MathF.Sin(-angle), MathF.Cos(-angle), k, f), Extra, Color));
    }

    public override void DrawShadow() {
        if (_flat || Entity.Size == 0) return;
        Render.DrawShadow(new ShadowData(Position.Xy, 1f, Color.Black));
    }
}