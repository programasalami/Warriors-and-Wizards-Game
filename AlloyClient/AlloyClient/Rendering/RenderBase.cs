using System;
using System.Collections.Generic;
using AlloyClient.Assets;
using AlloyClient.Game.Objects;
using AlloyClient.Rendering.VertexData;
using Alloy.Common;
using Alloy.Common.Structs;
using OpenTK.Mathematics;


namespace AlloyClient.Rendering;

public abstract class RenderBase : IComparable<RenderBase> {
    public abstract ModelType ModelType { get; }
    
    public abstract bool HasShadow { get; }

    public bool Visible;

    public float RotationAngle;

    public float Size;

    protected internal Entity Entity;
    
    public Vector3 Position = Vector3.Zero;
    public Vector4 UV = Vector4.Zero;
    public Vector4 Scale = Vector4.Zero;
    public Vector4 Rotation = Vector4.Zero;
    public ExtraData Extra;
    public Color Color = Color.Transparent;
    
    public abstract void SetPosition(float x, float y, float z = 0);

    // The render-scale/shift math below was built around 8x8-pixel sheet cells (the original
    // stock sprite convention) - it bakes that in as a hardcoded literal 8 rather than deriving
    // it from anything. A type whose own sheet uses a genuinely different native cell size (see
    // TypePlayer, whose "players" sheet is 32x32) overrides this so the same formula treats that
    // size AS its own baseline instead of comparing it against the 8x8 assumption, which is what
    // was blowing player sprites up ~4x and shifting them proportionally further above their own
    // shadow (both driven by this same ratio) when that sheet was rebuilt at higher resolution.
    protected virtual float TextureBaseUnit => 8f;

    // The vertical shift below (padY, which becomes the -Y translation Object.vert applies to
    // every vertex of the sprite) assumes the visible art fills its cell edge to edge - true for
    // the original 8x8 sprites, so it never needed a separate correction. A type whose art has its
    // own built-in transparent margin below the character's feet (see TypePlayer, measured
    // directly against the cell height via Python/PIL getbbox() on several frames) returns that
    // margin here as a fraction of cell height, so the sprite shifts down to compensate instead of
    // floating above its own shadow by that same margin.
    protected virtual float TextureBottomInset => 0f;

    // The horizontal shift below (padX) exists because an attack frame is double-width (it needs
    // extra room for a drawn weapon), and re-centers that wider frame so the character's body
    // doesn't visibly jump sideways versus the narrow idle/walk frames. It assumes the original
    // convention of body-in-the-left-quarter, weapon-extending-right (a constant 0.25 "quarter
    // into the content width" anchor baked into the w/4 term below). A type whose attack art is
    // actually centered in its own wide frame (see TypePlayer, measured directly via getbbox() -
    // the content center sits at ~50% either way, not 25%) overrides this to 0.5 so no shift gets
    // applied where none is needed - left at the old 0.25 default, this was shoving player sprites
    // sideways by nothing to do with any actual content asymmetry, worse the higher RealSize goes
    // since this scales with the same k every Size change does.
    protected virtual float AttackFrameBiasFraction => 0.25f;

    public void SetTexture(AtlasData texture) => SetTexture(texture, false);

    public virtual void SetTexture(AtlasData texture, bool attackFrame) {
        UV = texture.ToVector4();

        var frameMult = attackFrame ? 2f : 1f;
        var w = texture.RawW() - AtlasConfig.Padding * 2;
        var h = texture.RawH() - AtlasConfig.Padding * 2;

        // this should be padding * 2 but the attack frame doesnt line up unless its 3 for some fucking reason
        var padW = 1.0f + AtlasConfig.Padding * 3 / texture.RawW();
        var padH = 1.0f + AtlasConfig.Padding * 3 / texture.RawH();

        var ratio = w / h / frameMult * MathF.Max(w / frameMult / TextureBaseUnit, h / TextureBaseUnit);

        var widthScale = 0.75f * ratio * frameMult * padW;
        var heightScale = 0.75f * ratio * padH;
        
        var padX = attackFrame ? widthScale * (0.5f - (AtlasConfig.Padding + w * AttackFrameBiasFraction) / texture.RawW()) : 0f;
        var padY = heightScale * (0.5f - AtlasConfig.Padding / texture.RawH() - TextureBottomInset);

        Scale = new Vector4(widthScale, heightScale, padX, -padY);
    }

    public abstract void SetVisibility(bool visible);

    public abstract void SetDepth(float depth);
    public abstract void SetName(string name);
    public abstract void SetAlpha(float alpha);
    
    public void SetRotation(float rotation) => RotationAngle = rotation;

    public void SetSize(float size) => Size = size;

    public abstract void Draw(List<VertexObject> targets, double time);
    
    public virtual void DrawShadow() { }

    public int CompareTo(RenderBase other) {
        if (Extra.SortId < other.Extra.SortId) {
            return 1;
        }

        if (Extra.SortId > other.Extra.SortId) {
            return -1;
        }
        
        return 0;
    }
}

public abstract class SubRenderBase {
    public abstract float Height { get; }

    protected RenderBase Parent;
    
    protected Entity Entity;
    
    public Vector3 Position = Vector3.Zero;
    public Vector4 UV = Vector4.Zero;
    public Vector4 Scale = Vector4.Zero;
    public Vector4 Rotation = Vector4.Zero;
    public ExtraData Extra;
    public Color Color = Color.Transparent;

    public void SetDepth(float depth) => Extra.SortId = depth;

    public void SetAlpha(float alpha) => Extra.Alpha = alpha;
    
    public abstract void Draw(float yOffset, List<VertexObject> targets, double time);
}

public struct ExtraData {

    public Vector4 Data => _internal;
    
    public float SortId {
        get => _internal.Y;
        set => _internal.Y = value;
    }

    public float Alpha {
        get => _internal.W;
        set => _internal.W = value;
    }

    private Vector4 _internal;

    public ExtraData(float type, float shade) {
        _internal = new Vector4(type, 0f, shade, 1f);
    }
    
    public ExtraData(float type, float sort, float shade, float alpha) {
        _internal = new Vector4(type, sort, shade, alpha);
    }

    public static ExtraData NewShadedObject(float sortId, float alpha) => new ExtraData(RenderConfig.TypeGameObject, sortId, RenderConfig.Shade, alpha);
}