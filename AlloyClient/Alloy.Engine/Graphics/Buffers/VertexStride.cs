namespace Alloy.Engine.Graphics.Buffers;

public readonly struct VertexStride {

    public readonly uint Stride;

    public readonly bool Instanced;

    public readonly ElementFormat[] Layout;

    public VertexStride(ElementFormat[] layout, bool instanced = false) {
        Stride = (uint)layout.Sum(e => e.Bytes);
        Layout = layout;
        Instanced = instanced;
    }

    public void BindAttributes(VertexArrayObject vao, uint index) {
        vao.Bind();

        var offset = 0u;
        for (var i = 0u; i < Layout.Length; i++) {
            var e = Layout[i];

            GL.EnableVertexAttribArray(e.Location);
            GL.VertexAttribBinding(e.Location, index);

            if (Instanced) {
                GL.VertexBindingDivisor(index, 1);
            }

            switch (e.Type) {
                case VertexAttribType.Byte:
                case VertexAttribType.UnsignedByte:
                case VertexAttribType.Short:
                case VertexAttribType.UnsignedShort:
                case VertexAttribType.Int:
                case VertexAttribType.UnsignedInt:
                    GL.VertexAttribIFormat(e.Location, (int)e.Format, (VertexAttribIType)e.Type, offset);
                    break;
                case VertexAttribType.Float:
                case VertexAttribType.HalfFloat:
                    GL.VertexAttribFormat(e.Location, (int)e.Format, e.Type, false, offset);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(e.Type), e.Type, null);
            }

            offset += e.Bytes;
        }
    }
}