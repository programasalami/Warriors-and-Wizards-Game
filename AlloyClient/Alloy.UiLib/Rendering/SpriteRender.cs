using System;
using Alloy.Engine.Graphics;
using Alloy.Engine.Graphics.Buffers;
using OpenTK.Graphics.OpenGL;

namespace Alloy.UiLib.Rendering;

public static class SpriteRender {

    private const int InstanceBufferSize = 1000;
    private const int IndexBufferSize = InstanceBufferSize * 6; // Most sprites are a quad which has 6 indices
    private const int VertexBufferSize = InstanceBufferSize * 4; // Most sprites are a quad which has 4 vertices

    // The UI is drawn in several batches per frame (each top-level layer ends its own batch, and a batch is also cut when it fills up), and every batch used
    // to be uploaded into the SAME three GPU buffers. The Intel HD 4400's driver does not wait for a draw that is still reading a buffer, so when the GPU was
    // busy (loading, a lot of UI appearing at once, fast camera rotation) the next batch's upload landed under the previous batch's draw and pieces of the GUI
    // were drawn from the wrong data for a frame: the loading cover with a small black corner and no logo, tabs / buttons stretching, the dashboard flickering.
    // Fix: a small ring of SEPARATE buffer sets, one per batch in turn, so an upload never touches memory a recent draw may still be reading. (Orphaning the
    // buffers instead - re-allocating them for every upload - made this driver flash black frames, so the buffers are never re-allocated.)
    private const int BufferSetCount = 8;

    private sealed class BufferSet {
        public StorageBuffer<SpriteInstanceData> Instances;
        public IndexBuffer Indices;
        public VertexBuffer<SpriteVertexData> Vertices;
        public VertexArrayObject Vao;
    }

    private static BufferSet[] _sets;
    private static int _nextSet;

    private static ushort _instanceCount;
    private static SpriteInstanceData[] _instanceData;

    private static int _indexCount;
    private static ushort[] _indices;

    private static ushort _vertexCount;
    private static SpriteVertexData[] _vertices;

    internal static void Init() {
        _instanceData = new SpriteInstanceData[InstanceBufferSize];
        _indices = new ushort[IndexBufferSize];
        _vertices = new SpriteVertexData[VertexBufferSize];

        _sets = new BufferSet[BufferSetCount];
        for (var i = 0; i < BufferSetCount; i++) {
            var set = new BufferSet {
                Instances = new StorageBuffer<SpriteInstanceData>(InstanceBufferSize),
                Indices = new IndexBuffer(IndexBufferSize),
                Vertices = new VertexBuffer<SpriteVertexData>(SpriteVertexData.VertexStride, VertexBufferSize),
                Vao = new VertexArrayObject()
            };

            set.Vertices.BindTo(set.Vao);
            set.Indices.BindTo(set.Vao);
            _sets[i] = set;
        }

        GL.BindVertexArray(0);
    }

    internal static void StartDraw() {
        UiRender.UiShader.Apply();
        
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.StencilTest);

        _instanceCount = 0;
        _indexCount = 0;
        _vertexCount = 0;
    }

    internal static void Draw(SpriteInstanceData data, ReadOnlySpan<ushort> indices, ReadOnlySpan<VertexUi> vertices) {
        if (_instanceCount + 1 > InstanceBufferSize || _indexCount + indices.Length > IndexBufferSize || _vertexCount + vertices.Length > VertexBufferSize)
            Flush();
        
        _instanceData[_instanceCount] = data;
        var instanceId = _instanceCount++;
        var numVertices = (ushort)0;

        var len = indices.Length;
        for (var i = 0; i < len; i++) {
            _indices[_indexCount + i] = (ushort)(_vertexCount + indices[i]);
            numVertices = Math.Max(indices[i], numVertices);// Get highest vertex index
        }
        _indexCount += len;

        numVertices++;
        for (var i = 0; i < numVertices; i++) {
            _vertices[_vertexCount + i] = new SpriteVertexData(vertices[i], instanceId);
        }
        _vertexCount += numVertices;
        
        UiRender.LastRenderCount++;
    }

    internal static void EndDraw() {
        Flush();
        GL.BindVertexArray(0);
    }

    private static void Flush() {
        if (_indexCount == 0) {
            _instanceCount = 0;
            _vertexCount = 0;
            return;
        }

        var set = _sets[_nextSet];
        _nextSet = (_nextSet + 1) % BufferSetCount;

        set.Vao.Bind();
        set.Instances.BindToIndex(0);
        set.Instances.SetData(_instanceData.AsSpan());
        set.Indices.SetData(_indices.AsSpan());
        set.Vertices.SetData(_vertices.AsSpan());

        GL.DrawElements(PrimitiveType.Triangles, _indexCount, DrawElementsType.UnsignedShort, 0);
        
        _instanceCount = 0;
        _indexCount = 0;
        _vertexCount = 0;
    }
}