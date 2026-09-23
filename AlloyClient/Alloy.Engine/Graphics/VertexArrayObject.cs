namespace Alloy.Engine.Graphics;

public sealed class VertexArrayObject {

    internal readonly int Handle = GL.GenVertexArray();

    public void Bind() => GL.BindVertexArray(Handle);

    public void Dispose() => GL.DeleteVertexArray(Handle);   // was GL.DeleteBuffer (wrong object type: the VAO leaked)
}