namespace Alloy.Engine.Graphics;

public sealed class VertexArrayObject {

    internal readonly int Handle = GL.GenVertexArray();

    public void Bind() => GL.BindVertexArray(Handle);

    public void Dispose() => GL.DeleteBuffer(Handle);
}