namespace Alloy.Engine.Graphics.Buffers;

// Deliberately avoids the GL 4.3 "separate attribute format" path (glBindVertexBuffer /
// glVertexAttribFormat / glVertexAttribBinding, as used by VertexBuffer<T>/VertexStride)
// and real GL hardware instancing (glDrawArraysInstanced/glDrawElementsInstanced +
// glVertexAttribDivisor). Both have shown up as unreliable on older/low-end GPU drivers
// (e.g. older Intel integrated graphics). This uses only glVertexAttribPointer with plain,
// non-instanced draw calls (matching the technique Alloy.UiLib's SpriteRender already uses
// successfully) to upload real per-vertex attribute data.
public sealed unsafe class InstanceAttributeBuffer<T> where T : unmanaged, IBufferData<T> {

    public readonly int Length;

    internal readonly int Handle;

    public InstanceAttributeBuffer(int elementCount) {
        Length = elementCount;

        GL.GenBuffer(out Handle);
        GL.BindBuffer(BufferTarget.ArrayBuffer, Handle);
        GL.BufferData(BufferTarget.ArrayBuffer, elementCount * sizeof(T), IntPtr.Zero, BufferUsage.DynamicDraw);
    }

    public void SetData(ReadOnlySpan<T> data) {
        if (data.Length > Length) {
            throw new Exception("Data larger than buffer");
        }

        GL.BindBuffer(BufferTarget.ArrayBuffer, Handle);

        // Orphan the buffer's storage before writing new data (re-specify with a null pointer)
        // instead of BufferSubData-ing into the existing storage. Without this, a driver that
        // doesn't synchronize CPU writes against an in-flight GPU read of this same buffer from
        // the previous frame's draw call can hand the GPU torn/mid-write data - this GPU/driver
        // has shown that kind of unreliability in several other ways tonight, and it shows up
        // here as flickering between correct and garbled-looking geometry frame to frame.
        GL.BufferData(BufferTarget.ArrayBuffer, Length * sizeof(T), IntPtr.Zero, BufferUsage.DynamicDraw);
        GL.BufferSubData(BufferTarget.ArrayBuffer, 0, sizeof(T) * data.Length, data);
    }

    /// <summary>
    /// Binds a vec4 attribute at <paramref name="location"/>, reading <paramref name="components"/>
    /// floats starting at <paramref name="offsetBytes"/> within each element of this buffer. All
    /// attributes bound this way are real per-vertex data (divisor 0) - no instancing involved.
    /// </summary>
    public void BindAttribute(VertexArrayObject vao, uint location, int components, int offsetBytes) {
        vao.Bind();
        GL.BindBuffer(BufferTarget.ArrayBuffer, Handle);

        GL.EnableVertexAttribArray(location);
        GL.VertexAttribPointer(location, components, VertexAttribPointerType.Float, false, sizeof(T), new IntPtr(offsetBytes));
    }

    public void Delete() => GL.DeleteBuffer(Handle);
}
