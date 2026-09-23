using System.Collections.Generic;
using Alloy.Common;
using AlloyClient.Game.Objects;
using AlloyClient.Rendering.VertexData;
using OpenTK.Mathematics;

namespace AlloyClient.Rendering.Types.SubTypes;

public class TypeBar : SubRenderBase {

    public override float Height {
        get => 0.12f * 2;
    }

    public const float FillDepthOffset = 0.0004f;
    public const float BackgroundDepthOffset = 0.0002f;
    private Color _bgColor = Color.FromHexRGB(0x111111);
    private Vector4 _bgScale = new Vector4(0.72f, 0.12f, 0, 0);

    public TypeBar(RenderBase parent, Entity entity, Color color) {
        Parent = parent;
        Entity = entity;
        Color = color;

        UV = new Vector4();
        Scale = new Vector4(0.68f, 0.08f, 0, 0);
        Rotation = new Vector4(0, 1, 1, -1);
        Extra = new ExtraData(RenderConfig.TypeBar, RenderConfig.NoShade);
    }

    public void SetFill(float percent) {
        if (percent < 0f) return;
        
        Scale.Z = 0.68f * percent - 0.68f;
        Scale.X = 0.68f * percent;
    }
    
    public override void Draw(float yOffset, List<VertexObject> targets, double time) {
        _bgScale.W = yOffset;
        Scale.W = yOffset;
        // The fill, its dark background and the character sprite used to share ONE depth value; the entity list is sorted with an
        // unstable sort every frame, so the order of the two bar quads flipped between frames and the background sometimes covered
        // the fill (the depth test rejects equal depth): the bars blinked whenever the sort input changed - walking, rotating,
        // shooting. Depth = SortId in Object.vert (smaller is nearer): fill nearest, background just behind it, both in front of
        // the sprite. The offsets are far smaller than the spacing between neighbouring entities (about 0.028 per tile).
        var fill = Extra;
        fill.SortId -= FillDepthOffset;
        var background = Extra;
        background.SortId -= BackgroundDepthOffset;
        targets.Add(new VertexObject(Parent.Position, UV, Scale, Rotation, fill, Color));
        targets.Add(new VertexObject(Parent.Position, UV, _bgScale, Rotation, background, _bgColor));
    }
}