using System;
using System.Runtime.InteropServices;
using Alloy.Engine.Graphics.Buffers;
using OpenTK.Mathematics;

namespace AlloyClient.Rendering.VertexData;

// One real, non-instanced vertex: the local quad corner plus a full copy of the owning
// tile's data. 6 of these (one per corner) are uploaded per visible tile so drawing never
// depends on GL hardware instancing or gl_VertexID arithmetic - see InstanceAttributeBuffer.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TileVertexExpanded(Vector2 localPos, Vector2 localUv, TileData tile) : IBufferData<TileVertexExpanded> {
    public Vector2 LocalPos = localPos;
    public Vector2 LocalUV = localUv;
    public Vector4 Position = tile.Position;
    public Vector4 UV = tile.UV;
    public Vector4 Animate = tile.Animate;
    public Vector4 Mask = tile.Mask;
    public Vector4 Temp = tile.Temp;

    public bool Equals(TileVertexExpanded other) =>
        LocalPos.Equals(other.LocalPos) && LocalUV.Equals(other.LocalUV) && Position.Equals(other.Position) &&
        UV.Equals(other.UV) && Animate.Equals(other.Animate) && Mask.Equals(other.Mask) && Temp.Equals(other.Temp);

    public override bool Equals(object obj) => obj is TileVertexExpanded other && Equals(other);

    public override int GetHashCode() {
        var hc = new HashCode();
        hc.Add(LocalPos); hc.Add(LocalUV); hc.Add(Position); hc.Add(UV); hc.Add(Animate); hc.Add(Mask); hc.Add(Temp);
        return hc.ToHashCode();
    }
}
