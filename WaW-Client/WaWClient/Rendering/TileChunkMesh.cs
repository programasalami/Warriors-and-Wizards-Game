using System;
using WaW.Engine.Graphics;
using WaW.Engine.Graphics.Buffers;
using WaWClient.Rendering.VertexData;
using OpenTK.Graphics.OpenGL;

namespace WaWClient.Rendering;

// The GPU copy of one 16x16 tile chunk: expanded vertices in a buffer that is written only when the chunk's tiles change (an
// Update packet), then drawn as-is every frame. 2026-09-21 audit (H2 / F17-F18): the ground used to be re-expanded on the CPU and
// re-uploaded (orphan + copy, ~20 MB of driver traffic) EVERY frame although chunk data changes rarely. A rebuild still orphans the
// buffer before writing (the driver rule for this GPU), but that now happens a handful of times per world, not 60 times a second.
public sealed class TileChunkMesh {
    private InstanceAttributeBuffer<TileVertexExpanded> _buffer;
    private VertexArrayObject _vao;
    private int _capacity;

    public int VertexCount { get; private set; }
    public int TileCount => VertexCount / 6;

    public void Upload(ReadOnlySpan<TileVertexExpanded> vertices) {
        if (vertices.Length > _capacity) {
            Delete();
            _capacity = Math.Max(vertices.Length, 6 * 64);      // grow in steps so a chunk that gains a few tiles does not reallocate every time
            _capacity = (_capacity + 6 * 63) / (6 * 64) * (6 * 64);
            _buffer = new InstanceAttributeBuffer<TileVertexExpanded>(_capacity);
            _vao = new VertexArrayObject();
            _buffer.BindAttribute(_vao, 0, 2, 0);   // iLocalPos
            _buffer.BindAttribute(_vao, 1, 2, 8);   // iLocalUV
            _buffer.BindAttribute(_vao, 2, 4, 16);  // iPosition
            _buffer.BindAttribute(_vao, 3, 4, 32);  // iUV
            _buffer.BindAttribute(_vao, 4, 4, 48);  // iAnimate
            _buffer.BindAttribute(_vao, 5, 4, 64);  // iMask
            _buffer.BindAttribute(_vao, 6, 4, 80);  // iTemp
            GL.BindVertexArray(0);
        }

        VertexCount = vertices.Length;
        if (VertexCount > 0)
            _buffer.SetData(vertices);
    }

    // The ground shader must already be applied (Render.BeginTiles).
    public void Draw() {
        if (VertexCount == 0 || _vao == null)
            return;
        _vao.Bind();
        GL.DrawArrays(PrimitiveType.Triangles, 0, VertexCount);
        GpuStats.DrawCalls++;
    }

    public void Delete() {
        _buffer?.Delete();
        _vao?.Dispose();
        _buffer = null;
        _vao = null;
        _capacity = 0;
        VertexCount = 0;
    }
}
