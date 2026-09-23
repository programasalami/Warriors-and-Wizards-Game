namespace WaW.Engine.Graphics.Buffers;

public sealed unsafe class StorageBuffer<T> where T : unmanaged, IBufferData<T> {

    public readonly int Length;
    
    internal readonly int Handle;

    public StorageBuffer(int elementCount) {
        if (sizeof(T) % 16 != 0) throw new Exception("[SSBO] data size not multiple of 16, requirement of (stb140)");

        Length = elementCount;
        
        GL.GenBuffer(out Handle);
        GL.BindBuffer(BufferTarget.CopyWriteBuffer, Handle);
        GL.BufferData(BufferTarget.CopyWriteBuffer, elementCount * sizeof(T), IntPtr.Zero, BufferUsage.DynamicDraw);
    }

    public void SetData(ReadOnlySpan<T> data) {
        if (data.Length > Length) {
            throw new Exception("Data larger than buffer");
        }

        GL.BindBuffer(BufferTarget.CopyWriteBuffer, Handle);

        // Orphan before writing - see the matching comment in InstanceAttributeBuffer.SetData.
        GL.BufferData(BufferTarget.CopyWriteBuffer, Length * sizeof(T), IntPtr.Zero, BufferUsage.DynamicDraw);
        GL.BufferSubData(BufferTarget.CopyWriteBuffer, 0, sizeof(T) * data.Length, data);
    }

    public void BindToIndex(uint index) => GL.BindBufferBase(BufferTarget.ShaderStorageBuffer, index, Handle);
}