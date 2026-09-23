using System.Collections.Generic;
using Alloy.Common;
using AlloyClient.Assets;
using AlloyClient.Assets.Libraries;
using AlloyClient.Game.Objects;
using AlloyClient.Rendering.VertexData;

namespace AlloyClient.Rendering.Types;

// <CrossedCards>N</CrossedCards> props - bushes, rocks, trees (2026-09-22, "small props whirl when the camera turns"). The sprite is drawn
// as N upright copies of its picture spread evenly round the vertical axis (2 = a cross, 4 = a star, the trees / bushes / rocks use 4)
// that stand in the WORLD instead of a billboard that turns to face the camera: a billboard's turn on the spot is exactly what read as
// whirling. The cards stay put and the camera simply walks round them, seeing more of one card and less of another.
//
// Same picture, size and foot position as the billboard would have (RenderBase.SetTexture's Scale, the object's Size and BottomInset),
// drawn by the model pass (Model.vert / Model.frag: world-fixed geometry, alpha cut-out) with a depth value per CORNER, so the two cards
// interleave per pixel where they cross and sort correctly against the billboards around them (Render.DrawCrossedCards).
public sealed class TypeCrossedCards : RenderBase, IStaticProp {
    public override ModelType ModelType => ModelType.CrossedCards;

    public override bool HasShadow => true;

    // The same per-object <BottomInset> the billboard would use (see TypeGameObject).
    protected override float TextureBottomInset =>
        ObjectLibrary.TypeToObjectProps.TryGetValue(Entity.Type, out var props) ? props.BottomInset : 0f;

    private float _sortId;
    private readonly int _cards;

    public TypeCrossedCards(Entity entity) {
        Entity = entity;
        _cards = ObjectLibrary.TypeToObjectProps.TryGetValue(entity.Type, out var props) && props.CrossedCards > 0 ? props.CrossedCards : 2;
        SetTexture(entity.GetTexture());
        Extra = new ExtraData(RenderConfig.TypeModel, RenderConfig.Shade);
    }

    public override void SetPosition(float x, float y, float z = 0) {
        Position.X = x;
        Position.Y = y;
        Position.Z = z;
    }

    public override void SetVisibility(bool visible) => Visible = visible;

    public override void SetDepth(float depth) => _sortId = depth;

    public override void SetAlpha(float alpha) { }      // static props do not fade

    public override void SetName(string name) { }

    public override void Draw(List<VertexObject> targets, double time) {
        var k = Entity.Size / 100f;
        var width = Scale.X * k;
        var height = Scale.Y * k;
        // The billboard's bottom edge sits this far below the object's position (atlas padding + BottomInset); Scale.W is -padY.
        var bottom = -(0.5f * Scale.Y + Scale.W) * k;
        Render.DrawCrossedCards(Position, UV, width, height, bottom, _sortId, _cards);
    }

    // The same cards baked into a static area mesh (StaticProps); the shader derives each corner's depth from the camera.
    public void Bake(List<ModelVertexExpanded> into) {
        var k = Entity.Size / 100f;
        var bottom = -(0.5f * Scale.Y + Scale.W) * k;
        Render.BakeCrossedCards(Position, UV, Scale.X * k, Scale.Y * k, bottom, _cards, Entity.Jitter, into);
    }

    public override void DrawShadow() {
        if (Entity.Size == 0) return;
        Render.DrawShadow(new ShadowData(Position.Xy, 1f, Color.Black));
    }
}
