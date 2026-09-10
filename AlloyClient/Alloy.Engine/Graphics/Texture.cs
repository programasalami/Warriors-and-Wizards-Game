using Alloy.Common;
using ReFuel.Stb;

namespace Alloy.Engine.Graphics;

public sealed class Texture {
    
    public readonly int Width;
    
    public readonly int Height;
    
    internal readonly int Handle;

    public Texture(string file) : this(File.ReadAllBytes(file)) { }

    public Texture(ReadOnlySpan<byte> data) : this(StbImage.Load(data, StbiImageFormat.Rgba)) { }
    
    public Texture(StbImage image) : this(image.AsSpan<Color>(), image.Width, image.Height) { }
    
    // Dedicated scratch unit for texture creation/uploads, kept separate from the
    // sampler units (0-5) the renderer actually reads from at draw time. Without
    // this, binding to "whatever unit happens to be active" corrupts rendering:
    // draw calls sample from whatever texture last got uploaded on that unit.
    private const TextureUnit ScratchUnit = TextureUnit.Texture15;
    private const TextureTarget ScratchTarget = TextureTarget.Texture2D;

    public Texture(ReadOnlySpan<Color> data, int width, int height) {
        Width = width;
        Height = height;
        Handle = GL.GenTexture();
        GL.ActiveTexture(ScratchUnit);
        GL.BindTexture(ScratchTarget, Handle);

        GL.TexStorage2D(ScratchTarget, 1, SizedInternalFormat.Rgba8, width, height);
        GL.TexSubImage2D(ScratchTarget, 0, 0, 0, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, data);
    }

    public void SetData(ReadOnlySpan<Color> data, Vector4i rect) {
        GL.ActiveTexture(ScratchUnit);
        GL.BindTexture(ScratchTarget, Handle);
        GL.TexSubImage2D(ScratchTarget, 0, rect.X, rect.Y, rect.Z, rect.W, PixelFormat.Rgba, PixelType.UnsignedByte, data);
    }

    public void SetData(ReadOnlySpan<Color> data, int width, int height) {
        GL.ActiveTexture(ScratchUnit);
        GL.BindTexture(ScratchTarget, Handle);
        GL.TexSubImage2D(ScratchTarget, 0, 0, 0, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, data);
    }
}