using WaW.Common.SourceGen;
using WaW.Engine.Graphics;
using WaW.Engine.Graphics.Buffers;
using WaWClient.Assets;
using WaWClient.Game;
using WaWClient.Rendering.VertexData;
using WaW.Engine;
using WaW.UiLib.Data;
using OpenTK.Mathematics;

namespace WaWClient.Rendering;

public static partial class Render {

    // Sprites per entity-pass upload (more are drawn in several chunks). Was 10000 (an 8.6 MB buffer re-allocated every frame); the Nexus
    // draws ~100-250 sprites, a busy fight a few thousand.
    private const int BufferSize = 4000;

    // (A ring of separate buffers written WITHOUT the re-allocation was tried for the model and sprite passes on 2026-09-22 and measured
    // slower on the HD 4400 - the driver still waited on the writes. The per-write orphan stays; see Docs/EngineeringAudit.md section 9.)
    // Vertices needed to expand ONE chunk (every layer of every tile): the scratch array for TileChunkMesh uploads.
    public const int TileChunkVertexCapacity = TileMap.ChunkRenderData * 6;
    private const int ShadowBufferSize = 4096;

    private static readonly (string, string)[] ShadowDefines = [("ShadowBuffer", $"{ShadowBufferSize}")];

    // Shader Sources
    [Shader("Ground")] private static partial ShaderSource GroundShaderSource { get; }
    [Shader("Shadow")] private static partial ShaderSource ShadowShaderSource { get; }
    [Shader("Model")] private static partial ShaderSource ModelShaderSource { get; }
    [Shader("Object")] private static partial ShaderSource ObjectShaderSource { get; }
    [Shader("Particle")] private static partial ShaderSource ParticleShaderSource { get; }

    // Shaders
    private static Shader _shaderGround;
    private static Shader _shaderShadow;
    private static Shader _shaderModel;
    private static Shader _shaderObject;
    private static Shader _shaderParticle;

    // Vertex Objects
    private static VertexArrayObject _defaultVao;
    private static VertexArrayObject _modelVao;
    private static VertexArrayObject _entityVao;

    // Buffers
    private static VertexModel[] _modelInstances;
    private static ModelVertexExpanded[] _modelVertexExpandedData;
    private static InstanceAttributeBuffer<ModelVertexExpanded> _modelExpandedBuffer;

    private static TileVertexExpanded[] _tileVertexData;

    private static ShadowData[] _shadowData;
    private static UniformBuffer _shadowBuffer;

    private static VertexObject[] _entityData;
    private static EntityVertexExpanded[] _entityVertexData;
    private static InstanceAttributeBuffer<EntityVertexExpanded> _entityDataBuffer;

    private static readonly Vector2[] TileCorners = [new(0, 1), new(1, 1), new(0, 0), new(0, 0), new(1, 1), new(1, 0)];
    private static readonly Vector2[] ObjectCorners = [new(-0.5f, 0.5f), new(0.5f, 0.5f), new(-0.5f, -0.5f), new(-0.5f, -0.5f), new(0.5f, 0.5f), new(0.5f, -0.5f)];
    private static readonly Vector2[] ObjectUVs = [new(0, 1), new(1, 1), new(0, 0), new(0, 0), new(1, 1), new(1, 0)];

    public static unsafe void FirstTimeInit(Sampler atlas, BitmapFamily font) {
        // Shaders
        _shaderGround = Shader.FromSource(GroundShaderSource);
        _shaderGround.SetValue("GameTexture", atlas);

        _shaderShadow = Shader.FromSource(ShadowShaderSource, ShadowDefines);

        _shaderModel = Shader.FromSource(ModelShaderSource);
        _shaderModel.SetValue("GameTexture", atlas);

        _shaderObject = Shader.FromSource(ObjectShaderSource);
        _shaderObject.SetValue("GameTexture", atlas);

        _shaderObject.SetValue("PixelRange", font.PixelRange);
        _shaderObject.SetValue("TextTextureSize", new Vector2(font.Atlas.Width, font.Atlas.Height));
        _shaderObject.SetValue("TextTexture", font.Sampler);

        _shaderParticle = Shader.FromSource(ParticleShaderSource);

        _defaultVao = new VertexArrayObject();

        // Ground: each chunk owns its own static mesh (TileChunkMesh); this is only the scratch space one rebuild expands into.
        _tileVertexData = new TileVertexExpanded[TileChunkVertexCapacity];

        _shadowData = new ShadowData[ShadowBufferSize];
        _shadowBuffer = new UniformBuffer(_shadowData.Length * sizeof(ShadowData));

        _modelInstances = new VertexModel[2000];
        _modelVertexExpandedData = new ModelVertexExpanded[20000];
        _modelExpandedBuffer = new InstanceAttributeBuffer<ModelVertexExpanded>(_modelVertexExpandedData.Length);

        _modelVao = new VertexArrayObject();
        _modelExpandedBuffer.BindAttribute(_modelVao, 0, 3, 0);   // Position
        _modelExpandedBuffer.BindAttribute(_modelVao, 1, 2, 12);  // BaseUV
        _modelExpandedBuffer.BindAttribute(_modelVao, 2, 3, 20);  // iPosition
        _modelExpandedBuffer.BindAttribute(_modelVao, 3, 4, 32);  // iUV
        _modelExpandedBuffer.BindAttribute(_modelVao, 4, 3, 48);  // iExtra


        _entityData = new VertexObject[BufferSize];
        _entityVertexData = new EntityVertexExpanded[BufferSize * 6];
        _entityDataBuffer = new InstanceAttributeBuffer<EntityVertexExpanded>(_entityVertexData.Length);
        _entityVao = new VertexArrayObject();
        _entityDataBuffer.BindAttribute(_entityVao, 0, 2, 0);    // iLocalPos
        _entityDataBuffer.BindAttribute(_entityVao, 1, 2, 8);    // iLocalUV
        _entityDataBuffer.BindAttribute(_entityVao, 2, 4, 16);   // iPosition
        _entityDataBuffer.BindAttribute(_entityVao, 3, 4, 32);   // iUV
        _entityDataBuffer.BindAttribute(_entityVao, 4, 4, 48);   // iScale
        _entityDataBuffer.BindAttribute(_entityVao, 5, 4, 64);   // iRotation
        _entityDataBuffer.BindAttribute(_entityVao, 6, 4, 80);   // iExtra
        _entityDataBuffer.BindAttribute(_entityVao, 7, 4, 96);   // iColor
        _entityDataBuffer.BindAttribute(_entityVao, 8, 4, 112);  // iMask1
        _entityDataBuffer.BindAttribute(_entityVao, 9, 4, 128);  // iMask2

        BuildParticleBuffers();
    }

    // The camera's depth column (how the sort value changes per world unit), for geometry that needs a depth per corner (crossed cards).
    public static DepthMatrix Depth;

    public static void SetShaderParams(GameTime gameTime, Camera camera) {
        Depth = camera.DepthMatrix;
        _shaderGround.SetValue("FullMatrix", camera.Matrix);
        _shaderGround.SetValue("GameTime", (float)(gameTime.TotalMs / 1000.0f));

        _shaderShadow.SetValue("FullMatrix", camera.Matrix);
        _shaderShadow.SetValue("BillMatrix", camera.BillboardMatrix);

        _shaderModel.SetValue("FullMatrix", camera.Matrix);
        _shaderModel.SetValue("DepthColumn", new Vector4(camera.DepthMatrix.M12, camera.DepthMatrix.M22, camera.DepthMatrix.M32, camera.DepthMatrix.M42));

        _shaderObject.SetValue("FullMatrix", camera.Matrix);
        _shaderObject.SetValue("BillMatrix", camera.BillboardMatrix);
        // Zoom uniform no longer used by Object.frag (glow/outline pass disabled)

        _shaderParticle.SetValue("FullMatrix", camera.Matrix);
        _shaderParticle.SetValue("BillMatrix", camera.BillboardMatrix);
    }
}