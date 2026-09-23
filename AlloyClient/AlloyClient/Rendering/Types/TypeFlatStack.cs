using System.Collections.Generic;
using AlloyClient.Assets;
using AlloyClient.Assets.Libraries;
using AlloyClient.Game.Objects;
using AlloyClient.Rendering.VertexData;

namespace AlloyClient.Rendering.Types;

// <FlatOnGround/> + <Thickness>t</Thickness> props - the logs (2026-09-22, "flat logs look like a tileset next to the card-star props"). A flat
// prop lies in the ground plane and turns with the world, which is right for art drawn from above, but it has no height at all. This draws
// the same flat picture as a STACK of layers, each a little higher than the one below (t tiles in total), the way pixel-art games fake
// 3D ("sprite stacking"): the camera's shear lifts each layer a little further up the screen, so below the top picture you see the lower
// layers' bottom edges, which reads as the object's side. Model.frag darkens geometry near the ground, so those sides come out darker
// than the top by themselves. World-fixed like the flat sprite was (rotates with the ground, no shadow), drawn by the model pass.
public sealed class TypeFlatStack : RenderBase, IStaticProp {
    public override ModelType ModelType => ModelType.FlatStack;

    public override bool HasShadow => false;

    // Sort id for flat props: behind every standing object (standing depths span ~0.1-0.9), like TypeGameObject's flat path.
    private const float FlatSortId = 0.97f;

    private readonly float _thickness;

    public TypeFlatStack(Entity entity) {
        Entity = entity;
        _thickness = ObjectLibrary.TypeToObjectProps.TryGetValue(entity.Type, out var props) ? props.Thickness : 0.25f;
        SetTexture(entity.GetTexture());
        Extra = new ExtraData(RenderConfig.TypeModel, RenderConfig.Shade);
    }

    public override void SetPosition(float x, float y, float z = 0) {
        Position.X = x;
        Position.Y = y;
        Position.Z = z;
    }

    public override void SetVisibility(bool visible) => Visible = visible;

    public override void SetDepth(float depth) { }        // always behind standing things, like every flat prop

    public override void SetAlpha(float alpha) { }        // static props do not fade

    public override void SetName(string name) { }

    public override void Draw(List<VertexObject> targets, double time) {
        var k = Entity.Size / 100f;
        // The flat sprite's size: Scale.X x Scale.Y (x Size), pivot at the picture's centre, no base pad (TypeGameObject.DrawFlat).
        Render.DrawFlatStack(Position, UV, Scale.X * k, Scale.Y * k, Entity.Rotation, _thickness, FlatSortId);
    }

    public void Bake(List<ModelVertexExpanded> into) {
        var k = Entity.Size / 100f;
        Render.BakeFlatStack(Position, UV, Scale.X * k, Scale.Y * k, Entity.Rotation, _thickness, FlatSortId, into);
    }
}
