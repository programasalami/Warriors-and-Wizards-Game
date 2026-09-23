// The slice of OpenTK's GL class the client actually calls (about 75 functions), forwarded to WebGL2 through wwwroot/gl.js.
// Handles are small integers that index tables in JS. Desktop-GL-only calls the client relies on (separate vertex-attribute format,
// DSA-style ProgramUniform*, program-interface queries, shader storage buffers) are emulated on the JS side.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using OpenTK.Mathematics;

namespace OpenTK.Graphics.OpenGL;

public static unsafe partial class GL {
    private const string M = "gl";

    // ---- JS imports (implemented in gl.js) -----------------------------------------------------------------------------
    [JSImport("init", M)] internal static partial int JsInit(string canvasId);
    [JSImport("canvasWidth", M)] internal static partial int CanvasWidth();
    [JSImport("canvasHeight", M)] internal static partial int CanvasHeight();
    [JSImport("activeTexture", M)] private static partial void JsActiveTexture(int unit);
    [JSImport("attachShader", M)] private static partial void JsAttachShader(int p, int s);
    [JSImport("bindBuffer", M)] private static partial void JsBindBuffer(int target, int buf);
    [JSImport("bindBufferBase", M)] private static partial void JsBindBufferBase(int target, int index, int buf);
    [JSImport("bindSampler", M)] private static partial void JsBindSampler(int unit, int s);
    [JSImport("bindTexture", M)] private static partial void JsBindTexture(int target, int t);
    [JSImport("bindVertexArray", M)] private static partial void JsBindVertexArray(int v);
    [JSImport("bindVertexBuffer", M)] private static partial void JsBindVertexBuffer(int binding, int buf, int offset, int stride);
    [JSImport("blendFunc", M)] private static partial void JsBlendFunc(int s, int d);
    [JSImport("bufferData", M)] private static partial void JsBufferData(int target, int size, int usage);
    [JSImport("bufferSubData", M)] private static partial void JsBufferSubData(int target, int offset, [JSMarshalAs<JSType.MemoryView>] Span<byte> data);
    [JSImport("clear", M)] private static partial void JsClear(int mask);
    [JSImport("clearColor", M)] private static partial void JsClearColor(float r, float g, float b, float a);
    [JSImport("compileShader", M)] private static partial void JsCompileShader(int s);
    [JSImport("createProgram", M)] private static partial int JsCreateProgram();
    [JSImport("createShader", M)] private static partial int JsCreateShader(int type);
    [JSImport("cullFace", M)] private static partial void JsCullFace(int mode);
    [JSImport("deleteBuffer", M)] private static partial void JsDeleteBuffer(int b);
    [JSImport("deleteVertexArray", M)] private static partial void JsDeleteVertexArray(int v);
    [JSImport("deleteSampler", M)] private static partial void JsDeleteSampler(int s);
    [JSImport("deleteShader", M)] private static partial void JsDeleteShader(int s);
    [JSImport("depthFunc", M)] private static partial void JsDepthFunc(int f);
    [JSImport("depthMask", M)] private static partial void JsDepthMask(bool m);
    [JSImport("detachShader", M)] private static partial void JsDetachShader(int p, int s);
    [JSImport("disable", M)] private static partial void JsDisable(int cap);
    [JSImport("enable", M)] private static partial void JsEnable(int cap);
    [JSImport("drawArrays", M)] private static partial void JsDrawArrays(int mode, int first, int count);
    [JSImport("drawElements", M)] private static partial void JsDrawElements(int mode, int count, int type, int offset);
    [JSImport("enableVertexAttribArray", M)] private static partial void JsEnableVertexAttribArray(int loc);
    [JSImport("genBuffer", M)] private static partial int JsGenBuffer();
    [JSImport("genSampler", M)] private static partial int JsGenSampler();
    [JSImport("genTexture", M)] private static partial int JsGenTexture();
    [JSImport("genVertexArray", M)] private static partial int JsGenVertexArray();
    [JSImport("getActiveUniformName", M)] private static partial string JsGetActiveUniformName(int p, int i);
    [JSImport("getActiveUniformSize", M)] private static partial int JsGetActiveUniformSize(int p, int i);
    [JSImport("getActiveUniformType", M)] private static partial int JsGetActiveUniformType(int p, int i);
    [JSImport("getAttribLocation", M)] private static partial int JsGetAttribLocation(int p, string name);
    [JSImport("getProgrami", M)] private static partial int JsGetProgrami(int p, int pname);
    [JSImport("getProgramInfoLog", M)] private static partial string JsGetProgramInfoLog(int p);
    [JSImport("getUniformBlockCount", M)] private static partial int JsGetUniformBlockCount(int p);
    [JSImport("getUniformBlockName", M)] private static partial string JsGetUniformBlockName(int p, int i);
    [JSImport("getShaderi", M)] private static partial int JsGetShaderi(int s, int pname);
    [JSImport("getShaderInfoLog", M)] private static partial string JsGetShaderInfoLog(int s);
    [JSImport("getUniformLocation", M)] private static partial int JsGetUniformLocation(int p, string name);
    [JSImport("linkProgram", M)] private static partial void JsLinkProgram(int p);
    [JSImport("programUniform1i", M)] private static partial void JsProgramUniform1i(int p, int loc, int v);
    [JSImport("programUniform1f", M)] private static partial void JsProgramUniform1f(int p, int loc, float v);
    [JSImport("programUniformNf", M)] private static partial void JsProgramUniformNf(int p, int loc, int n, int count, [JSMarshalAs<JSType.MemoryView>] Span<byte> v);
    [JSImport("programUniformMatrix4f", M)] private static partial void JsProgramUniformMatrix4f(int p, int loc, int count, bool transpose, [JSMarshalAs<JSType.MemoryView>] Span<byte> v);
    [JSImport("samplerParameteri", M)] private static partial void JsSamplerParameteri(int s, int pname, int v);
    [JSImport("shaderSource", M)] private static partial void JsShaderSource(int s, string src);
    [JSImport("texStorage2D", M)] private static partial void JsTexStorage2D(int target, int levels, int ifmt, int w, int h);
    [JSImport("texSubImage2D", M)] private static partial void JsTexSubImage2D(int target, int level, int x, int y, int w, int h, int format, int type, [JSMarshalAs<JSType.MemoryView>] Span<byte> data);
    [JSImport("useProgram", M)] private static partial void JsUseProgram(int p);
    [JSImport("vertexAttribBinding", M)] private static partial void JsVertexAttribBinding(int loc, int binding);
    [JSImport("vertexAttribFormat", M)] private static partial void JsVertexAttribFormat(int loc, int size, int type, bool norm, int rel);
    [JSImport("vertexAttribIFormat", M)] private static partial void JsVertexAttribIFormat(int loc, int size, int type, int rel);
    [JSImport("vertexAttribPointer", M)] private static partial void JsVertexAttribPointer(int loc, int size, int type, bool norm, int stride, int offset);
    [JSImport("vertexBindingDivisor", M)] private static partial void JsVertexBindingDivisor(int binding, int divisor);
    [JSImport("viewport", M)] private static partial void JsViewport(int x, int y, int w, int h);
    [JSImport("createStorageTexture", M)] private static partial int JsCreateStorageTexture(int bytes);
    [JSImport("storageTextureData", M)] private static partial void JsStorageTextureData(int id, [JSMarshalAs<JSType.MemoryView>] Span<int> data);
    [JSImport("bindStorageTexture", M)] private static partial void JsBindStorageTexture(int index, int id);

    // ---- OpenTK-shaped API ---------------------------------------------------------------------------------------------
    public static void ActiveTexture(TextureUnit unit) => JsActiveTexture((int)unit);
    public static void AttachShader(int program, int shader) => JsAttachShader(program, shader);
    public static void BindBuffer(BufferTarget target, int buffer) => JsBindBuffer((int)target, buffer);
    public static void BindBufferBase(BufferTarget target, uint index, int buffer) => JsBindBufferBase((int)target, (int)index, buffer);
    public static void BindSampler(uint unit, int sampler) => JsBindSampler((int)unit, sampler);
    public static void BindTexture(TextureTarget target, int texture) => JsBindTexture((int)target, texture);
    public static void BindVertexArray(int vao) => JsBindVertexArray(vao);
    public static void BindVertexBuffer(uint binding, int buffer, nint offset, int stride) => JsBindVertexBuffer((int)binding, buffer, (int)offset, stride);
    public static void BlendFunc(BlendingFactor src, BlendingFactor dst) => JsBlendFunc((int)src, (int)dst);
    public static void BufferData(BufferTarget target, int size, IntPtr data, BufferUsage usage) => JsBufferData((int)target, size, (int)usage);

    public static void BufferSubData<T>(BufferTarget target, int offset, int size, ReadOnlySpan<T> data) where T : unmanaged {
        var bytes = MemoryMarshal.AsBytes(data);
        JsBufferSubData((int)target, offset, MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(bytes), Math.Min(size, bytes.Length)));
    }

    public static void Clear(ClearBufferMask mask) => JsClear((int)mask);
    public static void ClearColor(float r, float g, float b, float a) => JsClearColor(r, g, b, a);
    public static void CompileShader(int shader) => JsCompileShader(shader);
    public static int CreateProgram() => JsCreateProgram();
    public static int CreateShader(ShaderType type) => JsCreateShader((int)type);
    public static void CullFace(TriangleFace face) => JsCullFace((int)face);
    public static void DeleteBuffer(int buffer) => JsDeleteBuffer(buffer);
    public static void DeleteVertexArray(int vao) => JsDeleteVertexArray(vao);
    public static void DeleteSampler(int sampler) => JsDeleteSampler(sampler);
    public static void DeleteShader(int shader) => JsDeleteShader(shader);
    public static void DepthFunc(DepthFunction func) => JsDepthFunc((int)func);
    public static void DepthMask(bool flag) => JsDepthMask(flag);
    public static void DetachShader(int program, int shader) => JsDetachShader(program, shader);
    public static void Disable(EnableCap cap) => JsDisable((int)cap);
    public static void Enable(EnableCap cap) => JsEnable((int)cap);
    public static void DrawArrays(PrimitiveType mode, int first, int count) => JsDrawArrays((int)mode, first, count);
    public static void DrawElements(PrimitiveType mode, int count, DrawElementsType type, nint offset) => JsDrawElements((int)mode, count, (int)type, (int)offset);
    public static void EnableVertexAttribArray(int index) => JsEnableVertexAttribArray(index);
    public static void EnableVertexAttribArray(uint index) => JsEnableVertexAttribArray((int)index);
    public static void GenBuffer(out int buffer) => buffer = JsGenBuffer();
    public static int GenBuffer() => JsGenBuffer();
    public static void GenSampler(out int sampler) => sampler = JsGenSampler();
    public static int GenTexture() => JsGenTexture();
    public static int GenVertexArray() => JsGenVertexArray();

    public static string GetActiveUniform(int program, uint index, int bufSize, out int length, out int size, out UniformType type) {
        var name = JsGetActiveUniformName(program, (int)index);
        length = name.Length;
        size = JsGetActiveUniformSize(program, (int)index);
        type = (UniformType)JsGetActiveUniformType(program, (int)index);
        return name;
    }

    public static int GetAttribLocation(int program, string name) => JsGetAttribLocation(program, name);
    public static void GetProgrami(int program, ProgramProperty pname, out int value) => value = JsGetProgrami(program, (int)pname);
    public static void GetProgramInfoLog(int program, out string info) => info = JsGetProgramInfoLog(program);

    public static void GetProgramInterfacei(int program, ProgramInterface iface, ProgramInterfacePName pname, out int value) {
        var count = JsGetUniformBlockCount(program);
        if (pname == ProgramInterfacePName.ActiveResources) { value = count; return; }
        var max = 0;
        for (var i = 0; i < count; i++) max = Math.Max(max, JsGetUniformBlockName(program, i).Length + 1);
        value = max;
    }

    public static string GetProgramResourceName(int program, ProgramInterface iface, uint index, int bufSize, out int length) {
        var name = JsGetUniformBlockName(program, (int)index);
        length = name.Length;
        return name;
    }

    public static void GetShaderi(int shader, ShaderParameterName pname, out int value) => value = JsGetShaderi(shader, (int)pname);
    public static void GetShaderInfoLog(int shader, out string info) => info = JsGetShaderInfoLog(shader);
    public static int GetUniformLocation(int program, string name) => JsGetUniformLocation(program, name);
    public static void LinkProgram(int program) => JsLinkProgram(program);

    public static void ProgramUniform1i(int program, int location, int v) => JsProgramUniform1i(program, location, v);
    public static void ProgramUniform1f(int program, int location, float v) => JsProgramUniform1f(program, location, v);
    public static void ProgramUniform2f(int program, int location, int count, in Vector2 v) => JsProgramUniformNf(program, location, 2, count, MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref Unsafe.As<Vector2, float>(ref Unsafe.AsRef(in v)), 2)));
    public static void ProgramUniform3f(int program, int location, int count, in Vector3 v) => JsProgramUniformNf(program, location, 3, count, MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref Unsafe.As<Vector3, float>(ref Unsafe.AsRef(in v)), 3)));
    public static void ProgramUniform4f(int program, int location, int count, in Vector4 v) => JsProgramUniformNf(program, location, 4, count, MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref Unsafe.As<Vector4, float>(ref Unsafe.AsRef(in v)), 4)));
    public static void ProgramUniformMatrix4f(int program, int location, int count, bool transpose, in Matrix4 m) => JsProgramUniformMatrix4f(program, location, count, transpose, MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref Unsafe.As<Matrix4, float>(ref Unsafe.AsRef(in m)), 16)));

    public static void SamplerParameterIi(int sampler, SamplerParameterI pname, in int value) => JsSamplerParameteri(sampler, (int)pname, value);
    public static void ShaderSource(int shader, string source) => JsShaderSource(shader, source);
    public static void TexStorage2D(TextureTarget target, int levels, SizedInternalFormat format, int width, int height) => JsTexStorage2D((int)target, levels, (int)format, width, height);

    public static void TexSubImage2D<T>(TextureTarget target, int level, int x, int y, int width, int height, PixelFormat format, PixelType type, ReadOnlySpan<T> pixels) where T : unmanaged {
        var bytes = MemoryMarshal.AsBytes(pixels);
        JsTexSubImage2D((int)target, level, x, y, width, height, (int)format, (int)type, MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(bytes), bytes.Length));
    }

    public static void UseProgram(int program) => JsUseProgram(program);
    public static void VertexAttribBinding(int attrib, uint binding) => JsVertexAttribBinding(attrib, (int)binding);
    public static void VertexAttribBinding(uint attrib, uint binding) => JsVertexAttribBinding((int)attrib, (int)binding);
    public static void VertexAttribFormat(int attrib, int size, VertexAttribType type, bool normalized, int relativeOffset) => JsVertexAttribFormat(attrib, size, (int)type, normalized, relativeOffset);
    public static void VertexAttribFormat(uint attrib, int size, VertexAttribType type, bool normalized, uint relativeOffset) => JsVertexAttribFormat((int)attrib, size, (int)type, normalized, (int)relativeOffset);
    public static void VertexAttribIFormat(int attrib, int size, VertexAttribIType type, int relativeOffset) => JsVertexAttribIFormat(attrib, size, (int)type, relativeOffset);
    public static void VertexAttribIFormat(uint attrib, int size, VertexAttribIType type, uint relativeOffset) => JsVertexAttribIFormat((int)attrib, size, (int)type, (int)relativeOffset);
    public static void VertexAttribPointer(int attrib, int size, VertexAttribPointerType type, bool normalized, int stride, nint offset) => JsVertexAttribPointer(attrib, size, (int)type, normalized, stride, (int)offset);
    public static void VertexAttribPointer(uint attrib, int size, VertexAttribPointerType type, bool normalized, int stride, nint offset) => JsVertexAttribPointer((int)attrib, size, (int)type, normalized, stride, (int)offset);
    public static void VertexBindingDivisor(uint binding, uint divisor) => JsVertexBindingDivisor((int)binding, (int)divisor);
    public static void Viewport(int x, int y, int width, int height) => JsViewport(x, y, width, height);

    // ---- storage-buffer emulation (used by StorageBuffer<T>) -------------------------------------------------------------
    public static int CreateStorageTexture(int bytes) => JsCreateStorageTexture(bytes);
    public static void StorageTextureData(int handle, ReadOnlySpan<int> data) => JsStorageTextureData(handle, MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(data), data.Length));
    public static void BindStorageTexture(uint index, int handle) => JsBindStorageTexture((int)index, handle);
}
