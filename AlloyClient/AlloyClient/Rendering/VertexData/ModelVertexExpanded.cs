using System;
using System.Runtime.InteropServices;
using Alloy.Engine.Graphics.Buffers;
using OpenTK.Mathematics;

namespace AlloyClient.Rendering.VertexData;

// One real, non-instanced vertex: a copy of the base mesh vertex (Position/BaseUV) plus a
// full copy of the owning instance's data. Drawing walks each instance's mesh indices and
// bakes the resolved base vertex + instance data into one of these per real vertex, so
// drawing never depends on GL hardware instancing - see InstanceAttributeBuffer.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ModelVertexExpanded(VertexBase baseVertex, VertexModel instance) : IBufferData<ModelVertexExpanded> {
    public Vector3 Position = baseVertex.Position;
    public Vector2 BaseUV = baseVertex.UV;
    public Vector3 IPosition = instance.Position;
    public Vector4 IUV = instance.UV;
    public Vector3 IExtra = instance.Extra;

    public bool Equals(ModelVertexExpanded other) =>
        Position.Equals(other.Position) && BaseUV.Equals(other.BaseUV) && IPosition.Equals(other.IPosition) &&
        IUV.Equals(other.IUV) && IExtra.Equals(other.IExtra);

    public override bool Equals(object obj) => obj is ModelVertexExpanded other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Position, BaseUV, IPosition, IUV, IExtra);
}
