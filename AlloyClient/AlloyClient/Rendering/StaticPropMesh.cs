using System;
using Alloy.Engine.Graphics;
using Alloy.Engine.Graphics.Buffers;
using AlloyClient.Rendering.VertexData;
using OpenTK.Graphics.OpenGL;

namespace AlloyClient.Rendering;

// The GPU copy of one area's static props (StaticProps.cs): written only when a prop in the area arrives or leaves, drawn as-is every frame
// by the model pass. Same pattern as TileChunkMesh (proven on the HD 4400): a rebuild orphans and uploads, a handful of times per world.
public sealed class StaticPropMesh {
    private InstanceAttributeBuffer<ModelVertexExpanded> _buffer;
    private VertexArrayObject _vao;
    private int _capacity;

    public int VertexCount { get; private set; }

    public void Upload(ReadOnlySpan<ModelVertexExpanded> vertices) {
        if (vertices.Length > _capacity) {
            Delete();
            _capacity = (Math.Max(vertices.Length, 1024) + 1023) / 1024 * 1024;
            _buffer = new InstanceAttributeBuffer<ModelVertexExpanded>(_capacity);
            _vao = new VertexArrayObject();
            _buffer.BindAttribute(_vao, 0, 3, 0);   // Position
            _buffer.BindAttribute(_vao, 1, 2, 12);  // BaseUV
            _buffer.BindAttribute(_vao, 2, 3, 20);  // iPosition
            _buffer.BindAttribute(_vao, 3, 4, 32);  // iUV
            _buffer.BindAttribute(_vao, 4, 3, 48);  // iExtra
            GL.BindVertexArray(0);
        }

        VertexCount = vertices.Length;
        if (VertexCount > 0)
            _buffer.SetData(vertices);
    }

    // The model shader must already be applied (inside the model pass).
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
