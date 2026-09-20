// Replacement for the desktop UniformBuffer (only the shadow batch uses it): backed by the RGBA32UI data texture the shaders read with
// texelFetch, because a big dynamically-indexed uniform block does not compile on some D3D11 drivers behind WebGL.
using System.Runtime.InteropServices;

namespace Alloy.Engine.Graphics.Buffers;

public sealed unsafe class UniformBuffer {
    public readonly int LengthBytes;
    internal readonly int Handle;

    public UniformBuffer(int sizeInBytes) {
        LengthBytes = sizeInBytes;
        Handle = GL.CreateStorageTexture(sizeInBytes);
    }

    public void SetData<T1>(ReadOnlySpan<T1> data, int offsetInBytes) where T1 : unmanaged {
        var size = sizeof(T1) * data.Length;
        if (size + offsetInBytes > LengthBytes) throw new Exception("Data larger than buffer");
        if (offsetInBytes != 0) throw new NotSupportedException("web UniformBuffer only supports writes at offset 0");
        GL.StorageTextureData(Handle, MemoryMarshal.Cast<T1, int>(data));
    }

    internal void BindAsTexture() => GL.BindStorageTexture(0, Handle);

    public void Bind(Shader shader, string uniform) => BindAsTexture();
}
