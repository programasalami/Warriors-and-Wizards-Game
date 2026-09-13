namespace Alloy.Engine.Graphics.Buffers;

public sealed unsafe class UniformBuffer {
    
    public readonly int LengthBytes;
    
    internal readonly int Handle;

    public UniformBuffer(int sizeInBytes) {
        if ((sizeInBytes - 1) > ushort.MaxValue) {
            throw new Exception($"Size too big for uniform buffer object, {sizeInBytes}/{ushort.MaxValue}");
        }
        
        LengthBytes = sizeInBytes;
        
        GL.GenBuffer(out Handle);
        GL.BindBuffer(BufferTarget.CopyWriteBuffer, Handle);
        GL.BufferData(BufferTarget.CopyWriteBuffer, sizeInBytes, IntPtr.Zero, BufferUsage.DynamicDraw);
    }

    public void SetData<T1>(ReadOnlySpan<T1> data, int offsetInBytes) where T1: unmanaged {
        var size = sizeof(T1) * data.Length;
        if (size + offsetInBytes > LengthBytes) throw new Exception("Data larger than buffer");

        GL.BindBuffer(BufferTarget.CopyWriteBuffer, Handle);

        // Orphan before writing - see the matching comment in InstanceAttributeBuffer.SetData.
        GL.BufferData(BufferTarget.CopyWriteBuffer, LengthBytes, IntPtr.Zero, BufferUsage.DynamicDraw);
        GL.BufferSubData(BufferTarget.CopyWriteBuffer, offsetInBytes, size, data);
    }

    public void Bind(Shader shader, string uniform) => GL.BindBufferBase(BufferTarget.UniformBuffer, shader.GetUniformBlock(uniform), Handle);
}