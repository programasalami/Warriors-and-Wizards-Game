using System.Collections.Generic;
using WaW.Common;
using WaWClient.Game.Objects;
using WaWClient.Rendering.VertexData;
using WaW.UiLib;
using WaW.UiLib.Core;
using OpenTK.Mathematics;

namespace WaWClient.Rendering.Types.SubTypes;

public class TypeName : SubRenderBase {

    private float _height;
    public override float Height {
        get => _height * 1.75f;
    }

    public string Name;

    private GlyphData[] _glyphs;

    public TypeName(RenderBase parent, Entity entity) {
        Parent = parent;
        Entity = entity;
        if (entity is Player player) {
            Name = player.Name;
        } else {
            Name = entity.Properties.DisplayName;
        }

        if (string.IsNullOrEmpty(Name))
            Name = "Default";
        
        SetTextures();
        Extra = new ExtraData(RenderConfig.TypeText, RenderConfig.NoShade);
    }

    public void SetTextures() {
        const float size = 0.34f;   // em size in tiles: MyriadPro caps are ~0.67 em, so ~0.23 tile tall
        
        // Must be the family Render.FirstTimeInit bound to the object shader's text sampler (MyriadPro, the client's one font).
        var font = UiRender.GetFont(FontType.Normal);
        _glyphs = new GlyphData[Name.Length];

        _height = font.Ascender * size;
        var zero = new Vector2(0f, _height);

        var len = Name.Length;
        for (var i = 0; i < len; i++) {
            var c = Name[i];
            if (!font.Glyphs.TryGetValue(c, out var glyph)) {
                continue;
            }

            var uv = glyph.UV;
            var pos = glyph.Position;

            // The object shader's quad spans -0.5..0.5, so iScale.xy is the quad's FULL width/height (this used to pass half of
            // each while still spacing letters by full advances -> tiny, thin, blurry letters with huge gaps). The offset is the
            // quad's centre in the same units as the advances.
            var w = (pos.X1 - pos.X0) * size;
            var h = (pos.Y0 - pos.Y1) * size;
            var cx = zero.X + (pos.X0 + pos.X1) / 2f * size;
            var cy = zero.Y - (pos.Y0 + pos.Y1) / 2f * size;

            _glyphs[i] = new GlyphData(uv.ToVector4(), w, h, cx, cy);

            if (i < len - 1) {
                font.Kernings.TryGetValue((c, Name[i + 1]), out var kern);
                zero.X += kern * size;
            }

            zero.X += glyph.Advance * size;
        }

        for (var index = 0; index < _glyphs.Length; index++) {
            _glyphs[index].Center(zero.X / 2f);
        }

        Rotation = new Vector4(0, 1, 1, -1);
        Color = new Color(0xFC, 0xDF, 0, 1);
    }
    
    public override void Draw(float yOffset, List<VertexObject> targets, double time) {
        for (var index = 0; index < _glyphs.Length; index++) {
            var g = _glyphs[index];
            g.Scale.W += yOffset;
            targets.Add(new VertexObject(Parent.Position, g.UV, g.Scale, Rotation, Extra, Color));
        }
    }
    
    private struct GlyphData(Vector4 uv, float w, float h, float x, float y) {
        public Vector4 UV = uv;
        public Vector4 Scale = new(w, h, x, y);

        public void Center(float x) {
            Scale.Z -= x;
        }
    }
}