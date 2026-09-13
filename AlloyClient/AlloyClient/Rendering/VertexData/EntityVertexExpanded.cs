using System;
using System.Runtime.InteropServices;
using Alloy.Engine.Graphics.Buffers;
using OpenTK.Mathematics;

namespace AlloyClient.Rendering.VertexData;

// One real, non-instanced vertex: the local quad corner plus a full copy of the owning
// entity's data. 6 of these (one per corner) are uploaded per entity so drawing never
// depends on GL hardware instancing or gl_VertexID arithmetic - see InstanceAttributeBuffer.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct EntityVertexExpanded(Vector2 localPos, Vector2 localUv, VertexObject entity) : IBufferData<EntityVertexExpanded> {
    public Vector2 LocalPos = localPos;
    public Vector2 LocalUV = localUv;
    public Vector4 Position = entity.Position;
    public Vector4 UV = entity.UV;
    public Vector4 Scale = entity.Scale;
    public Vector4 Rotation = entity.Rotation;
    public Vector4 Extra = entity.Extra;
    public Vector4 Color = entity.Color;
    public Vector4 Mask1 = entity.Mask1;
    public Vector4 Mask2 = entity.Mask2;

    public bool Equals(EntityVertexExpanded other) =>
        LocalPos.Equals(other.LocalPos) && LocalUV.Equals(other.LocalUV) && Position.Equals(other.Position) &&
        UV.Equals(other.UV) && Scale.Equals(other.Scale) && Rotation.Equals(other.Rotation) &&
        Extra.Equals(other.Extra) && Color.Equals(other.Color) && Mask1.Equals(other.Mask1) && Mask2.Equals(other.Mask2);

    public override bool Equals(object obj) => obj is EntityVertexExpanded other && Equals(other);

    public override int GetHashCode() {
        var hc = new HashCode();
        hc.Add(LocalPos); hc.Add(LocalUV); hc.Add(Position); hc.Add(UV); hc.Add(Scale);
        hc.Add(Rotation); hc.Add(Extra); hc.Add(Color); hc.Add(Mask1); hc.Add(Mask2);
        return hc.ToHashCode();
    }
}
