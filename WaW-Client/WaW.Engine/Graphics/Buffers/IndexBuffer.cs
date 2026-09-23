namespace WaW.Engine.Graphics.Buffers;

public sealed unsafe class IndexBuffer {
    
    public readonly int Length;
    
    public readonly int LengthBytes;
    
    internal readonly int Handle;

    public IndexBuffer(int indicesCount) {
        if (indicesCount < 0) throw new Exception("Element count must be >= 0");
        
        Length = indicesCount;
        LengthBytes = indicesCount * sizeof(ushort);

        GL.GenBuffer(out Handle);
        GL.BindBuffer(BufferTarget.CopyWriteBuffer, Handle);
        GL.BufferData(BufferTarget.CopyWriteBuffer, Length * sizeof(ushort), IntPtr.Zero, BufferUsage.DynamicDraw);
    }

    public void SetData(ReadOnlySpan<ushort> indices, int startIndex, int count, int bufferElementOffset) {
        if (count > Length - bufferElementOffset) throw new Exception("count & bufferOffset exceeds the length of the buffer");
        if (bufferElementOffset < 0 || bufferElementOffset > Length) throw new Exception("bufferOffset is outside the bounds of the buffer");

        GL.BindBuffer(BufferTarget.CopyWriteBuffer, Handle);
        GL.BufferSubData(BufferTarget.CopyWriteBuffer, sizeof(ushort) * bufferElementOffset, sizeof(ushort) * count, indices.Slice(startIndex, count));
    }

    public void SetData(ReadOnlySpan<ushort> indices) {
        if (indices.Length > Length) throw new Exception("Data larger than buffer");

        GL.BindBuffer(BufferTarget.CopyWriteBuffer, Handle);
        GL.BufferSubData(BufferTarget.CopyWriteBuffer, 0, sizeof(ushort) * indices.Length, indices);
    }

    public void BindTo(VertexArrayObject vao) {
        vao.Bind();
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, Handle);
    }

    public void Delete() => GL.DeleteBuffer(Handle);
}