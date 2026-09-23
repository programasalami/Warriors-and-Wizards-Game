using System;
using System.Collections.Generic;
using WaW.Common;
using WaWClient.Assets;
using WaWClient.Game;
using WaWClient.Game.Objects;
using WaWClient.Rendering.Types.SubTypes;
using WaWClient.Rendering.VertexData;
using OpenTK.Mathematics;

namespace WaWClient.Rendering.Types;

public sealed class TypePlayer : RenderBase {
    
    public override ModelType ModelType {
        get => ModelType.PbObject;
    }

    public override bool HasShadow {
        get => true;
    }

    // The "players" sheet's cells are natively 32x32 (see Game.atlas) - see the property's own
    // doc comment on RenderBase for why this has to match the sheet's real cell size.
    protected override float TextureBaseUnit => 32f;

    // Measured directly (Python/PIL getbbox() on several idle/walk/attack frames of both Wizard
    // and Warrior, 2026-09-18) - the art consistently leaves an 8-9px transparent gap below the
    // character's feet within its 32x32 cell, unlike the original 8x8 sprites which had none.
    // 9/32 rather than 8/32 to lean toward slightly under-correcting (leaving the character a
    // touch high) rather than over-correcting into the shadow/ground.
    protected override float TextureBottomInset => 9f / 32f;

    // Measured the same way - the wide attack-release frame's actual content (body + weapon) sits
    // centered around ~51% of its own double-width canvas, essentially identical to the ~50% the
    // narrow idle frame's content sits at in its own single-width canvas. There's no left/right
    // asymmetry to correct for in this art, unlike the original convention RenderBase's default
    // (0.25) assumes - leaving that default in was shifting the whole sprite sideways by nothing
    // to do with the actual art, and it's what read as the character "lunging a full tile" when
    // shooting (worse the higher RealSize goes, since this shift scales with the same k).
    protected override float AttackFrameBiasFraction => 0.5f;

    private readonly Player _player;
    
    private TypeName _typeName;
    private readonly TypeHpBar _hpBar;
    private readonly TypeBar _mpBar;
    private readonly TypeEffects _effects;

    public TypePlayer(Player player) {
        Entity = player;
        _player = player;
        
        SetTexture(player.GetTexture());
        Extra = new ExtraData(RenderConfig.TypeGameObject, RenderConfig.Shade);
        
        _typeName = new TypeName(this, player);
        _hpBar = new TypeHpBar(this, player);
        _mpBar = new TypeBar(this, player, Color.FromHexRGB(0x6084E0));
        _effects = new TypeEffects(this, player);
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
        Extra.SortId = depth;
        _typeName.SetDepth(depth);
        _hpBar.SetDepth(depth);
        _mpBar.SetDepth(depth);
        _effects.SetDepth(depth);
    }
    
    public override void SetAlpha(float alpha) {
        Extra.Alpha = alpha;
        _typeName.SetAlpha(alpha);
        _hpBar.SetAlpha(alpha);
        _mpBar.SetAlpha(alpha);
        _effects.SetAlpha(alpha);
    }

    public override void SetName(string name) {
        _typeName.Name = name;
        _typeName.SetTextures();
    }

    public override void Draw(List<VertexObject> targets, double time) {
        var s = MathF.Sin(-Entity.Rotation);
        var c = MathF.Cos(-Entity.Rotation);
        var k = Entity.Size / 100f;
        var f = Entity.Flipped ? 1f : -1f;
        Rotation = new Vector4(s, c, k, f);
        
        Entity.HeightOffset = -0.5f * Scale.Y * k + Scale.W * k;
        
        targets.Add(new VertexObject(Position, UV, Scale, Rotation, Extra, Color));
        var y = 0.1f;
        if (_player != Map.LocalPlayer) {
            _typeName.Draw(y, targets, time);
            y += _typeName.Height;
        }
        
        // The small HP / MP bars under the character; the player can hide their own (Settings.ShowStatusBars / hotkey).
        if (_player != Map.LocalPlayer || Settings.ShowStatusBars) {
            _hpBar.SetFill(1f * _player.Hp / _player.MaxHp);
            _hpBar.Draw(y, targets, time);
            y += _hpBar.Height;
            _mpBar.Draw(y, targets, time);
        }
        
        _effects.Draw(Entity.HeightOffset, targets, time);
        
    }

    public override void DrawShadow() {
        if (Entity.Size == 0) return;
        Render.DrawShadow(new ShadowData(Position.Xy, 1f, Color.Black));
    }
}