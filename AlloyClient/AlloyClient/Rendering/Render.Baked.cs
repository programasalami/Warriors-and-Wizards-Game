using System;
using System.Collections.Generic;
using AlloyClient.Assets;
using AlloyClient.Rendering.VertexData;
using OpenTK.Mathematics;

namespace AlloyClient.Rendering;

// Static props baked ONCE into world-space vertices for a per-area mesh (StaticProps.cs, 2026-09-22). The per-frame model pass re-expanded
// and re-uploaded every wall, board, tree star and log every frame although none of them ever move - measured as the largest part of the
// frame. The vertices are exactly those the per-frame paths produce (DrawModel / DrawCrossedCards / DrawFlatStack); only the depth changes:
// it depends on the camera, so a baked vertex carries a CODE that Model.vert turns into the same depth the CPU would have computed.
public static partial class Render {
    public const float BakedAtPosition = -10f;       // ground point = iPosition.xy (props, card corners)
    public const float BakedAtTileCentre = -20f;     // ground point = iPosition.xy + 0.5 (walls and wall tops, stored at the tile corner)

    // The code Model.vert decodes; the nudge is Entity.Jitter (+-0.00001), scaled so it survives in the float next to the code.
    public static float BakedDepthCode(float kind, float jitter) => kind + jitter * 1000f;

    // A mesh model (wall, wall top, board, jukebox) as world vertices: the base mesh plus the instance, like FlushBufferModel.
    public static void BakeModel(ModelType type, VertexModel instance, List<ModelVertexExpanded> into) {
        if (!ModelData.ModelRenderInfo.TryGetValue(type, out var info))
            return;
        var count = info.PrimitiveCount * 3;
        for (var v = 0; v < count; v++)
            into.Add(new ModelVertexExpanded(ModelData.Vertices[ModelData.Indices[info.IndexOffset + v]], instance));
    }

    // DrawCrossedCards, baked: each corner's ground point is written into iPosition (so the shader derives the per-corner depth from it)
    // and only the height stays in the local position.
    public static void BakeCrossedCards(Vector3 position, Vector4 uv, float width, float height, float bottom, int cards, float jitter, List<ModelVertexExpanded> into) {
        cards = Math.Clamp(cards, 1, 8);
        var hw = width * 0.5f;
        var z0 = bottom;
        var z1 = bottom + height;
        var code = BakedDepthCode(BakedAtPosition, jitter);

        for (var i = 0; i < cards; i++) {
            var angle = MathF.PI * i / cards;
            var a = new Vector2(-MathF.Cos(angle) * hw, -MathF.Sin(angle) * hw);
            var b = -a;
            BakedCardFace(position, uv, code, a, b, z0, z1, into);
            BakedCardFace(position, uv, code, b, a, z0, z1, into);
        }
    }

    private static void BakedCardFace(Vector3 position, Vector4 uv, float code, Vector2 left, Vector2 right, float z0, float z1, List<ModelVertexExpanded> into) {
        CardCorner(position, uv, code, left, z1, UvTl, into);
        CardCorner(position, uv, code, right, z1, UvTr, into);
        CardCorner(position, uv, code, right, z0, UvBr, into);
        CardCorner(position, uv, code, left, z1, UvTl, into);
        CardCorner(position, uv, code, right, z0, UvBr, into);
        CardCorner(position, uv, code, left, z0, UvBl, into);
    }

    private static void CardCorner(Vector3 position, Vector4 uv, float code, Vector2 ground, float z, Vector2 baseUv, List<ModelVertexExpanded> into) =>
        into.Add(new ModelVertexExpanded(
            new VertexBase(new Vector3(0f, 0f, z), baseUv),
            new VertexModel(new Vector3(position.X + ground.X, position.Y + ground.Y, position.Z), uv, new Vector3(0f, code, RenderConfig.Shade))));

    // DrawFlatStack, baked. Its depth never depended on the camera (flat props sort behind everything), so it is stored as-is.
    public static void BakeFlatStack(Vector3 position, Vector4 uv, float width, float height, float rotation, float thickness, float sortId, List<ModelVertexExpanded> into) {
        var layers = Math.Clamp((int)MathF.Round(thickness * 24f), 2, 16);
        var hw = width * 0.5f;
        var hh = height * 0.5f;
        var c = MathF.Cos(rotation);
        var s = MathF.Sin(rotation);
        var tl = new Vector3(-hw * c + hh * s, -hw * s - hh * c, 0);
        var tr = new Vector3(hw * c + hh * s, hw * s - hh * c, 0);
        var br = new Vector3(hw * c - hh * s, hw * s + hh * c, 0);
        var bl = new Vector3(-hw * c - hh * s, -hw * s + hh * c, 0);

        for (var i = 0; i < layers; i++) {
            var z = thickness * i / (layers - 1);
            var extra = new Vector3(0f, sortId - 0.0004f * i, i == layers - 1 ? RenderConfig.Shade : SideShade);
            var instance = new VertexModel(position, uv, extra);
            FlatFace(instance, tl with { Z = z }, tr with { Z = z }, br with { Z = z }, bl with { Z = z }, into);
            FlatFace(instance, tr with { Z = z }, tl with { Z = z }, bl with { Z = z }, br with { Z = z }, into);
        }
    }

    private static void FlatFace(VertexModel instance, Vector3 tl, Vector3 tr, Vector3 br, Vector3 bl, List<ModelVertexExpanded> into) {
        into.Add(new ModelVertexExpanded(new VertexBase(tl, UvTl), instance));
        into.Add(new ModelVertexExpanded(new VertexBase(tr, UvTr), instance));
        into.Add(new ModelVertexExpanded(new VertexBase(br, UvBr), instance));
        into.Add(new ModelVertexExpanded(new VertexBase(tl, UvTl), instance));
        into.Add(new ModelVertexExpanded(new VertexBase(br, UvBr), instance));
        into.Add(new ModelVertexExpanded(new VertexBase(bl, UvBl), instance));
    }
}

// A render type that can be baked into a per-area static mesh (static props only: walls, boards, card stars, stacked logs).
public interface IStaticProp {
    void Bake(List<ModelVertexExpanded> into);
}
