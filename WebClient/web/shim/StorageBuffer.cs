// WebGL2 has no shader storage buffers, so the "storage buffer" is a RGBA32UI data texture the shaders read with texelFetch (see
// wwwroot/gl.js createStorageTexture and the ported shaders in ../shaders). Same class the renderers already use.
using System.Runtime.InteropServices;

namespace WaW.Engine.Graphics.Buffers;

public sealed unsafe class StorageBuffer<T> where T : unmanaged, IBufferData<T> {
    public readonly int Length;
    internal readonly int Handle;

    public StorageBuffer(int elementCount) {
        if (sizeof(T) % 16 != 0) throw new Exception("[SSBO] data size not multiple of 16");
        Length = elementCount;
        Handle = GL.CreateStorageTexture(elementCount * sizeof(T));
    }

    public void SetData(ReadOnlySpan<T> data) {
        if (data.Length > Length) throw new Exception("Data larger than buffer");
        GL.StorageTextureData(Handle, MemoryMarshal.Cast<T, int>(data));
    }

    public void BindToIndex(uint index) => GL.BindStorageTexture(index, Handle);
}
