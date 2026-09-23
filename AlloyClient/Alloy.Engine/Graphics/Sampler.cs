namespace Alloy.Engine.Graphics;

public sealed class Sampler {

    internal readonly int Handle;

    internal readonly int TextureHandle;

    internal uint TextureUnit;
    
    public Sampler(Texture texture) {
        GL.GenSampler(out Handle);
        TextureHandle = texture.Handle;
        SetFilter(TextureFilter.Nearest);
        ClampToEdge();
    }

    public Sampler(Texture texture, uint textureUnit) {
        GL.GenSampler(out Handle);
        TextureHandle = texture.Handle;
        SetFilter(TextureFilter.Nearest);
        ClampToEdge();
        Bind(textureUnit);
    }

    public Sampler(Texture texture, TextureFilter filter) {
        GL.GenSampler(out Handle);
        TextureHandle = texture.Handle;
        SetFilter(filter);
        ClampToEdge();
    }

    public Sampler(Texture texture, TextureFilter filter, uint textureUnit) {
        GL.GenSampler(out Handle);
        TextureHandle = texture.Handle;
        Bind(textureUnit);
        SetFilter(filter);
        ClampToEdge();
    }

    public void Bind(uint textureUnit) {
        if (textureUnit > 15) {
            throw new ArgumentOutOfRangeException(nameof(textureUnit), textureUnit, null);
        }


        TextureUnit = textureUnit;
        GL.ActiveTexture((OpenTK.Graphics.OpenGL.TextureUnit)((int)OpenTK.Graphics.OpenGL.TextureUnit.Texture0 + textureUnit));
        GL.BindTexture(TextureTarget.Texture2D, TextureHandle);
        GL.BindSampler(textureUnit, Handle);
    }
    
    // Nothing in this engine tiles a texture through the sampler; the GL default (repeat) let a LINEAR sample on a quad's edge blend in the texel from
    // the far side of the texture (the title backdrop showed the map's top row as a line under every glow sprite, 2026-09-22).
    public void ClampToEdge() {
        var clamp = 0x812F;             // GL_CLAMP_TO_EDGE as a literal: the web build's GL shim (WebClient/web/shim/Enums.g.cs) has no TextureWrapMode enum
        GL.SamplerParameterIi(Handle, SamplerParameterI.TextureWrapS, in clamp);
        GL.SamplerParameterIi(Handle, SamplerParameterI.TextureWrapT, in clamp);
    }

    public void SetFilter(TextureFilter filter) {
        GL.SamplerParameterIi(Handle, SamplerParameterI.TextureMagFilter, in filter.MagFilter);
        GL.SamplerParameterIi(Handle, SamplerParameterI.TextureMinFilter, in filter.MinFilter);
    }

    public void Delete() => GL.DeleteSampler(Handle);

}