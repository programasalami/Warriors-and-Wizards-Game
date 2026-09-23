using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Alloy.Engine.Graphics;
using AlloyClient.Assets;
using AlloyClient.Rendering.VertexData;
using Alloy.Common;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace AlloyClient.Rendering;

public static partial class Render {
    public static int LastDrawCountTiles;
    public static int LastDrawCountShadows;
    public static int LastDrawCountEntities;

    private static int _shadowCount;
    private static ModelType _entityModel;

    #region Render Tile

    // Expands tiles into real vertices (6 per tile). Pure: no GL. Returns the number of vertices written.
    public static int ExpandTiles(ReadOnlySpan<TileData> tiles, Span<TileVertexExpanded> vertices) {
        if (vertices.Length < tiles.Length * 6)
            throw new ArgumentException($"{tiles.Length} tiles need {tiles.Length * 6} vertices, target holds {vertices.Length}");

        for (var i = 0; i < tiles.Length; i++) {
            var tile = tiles[i];
            var baseIdx = i * 6;
            for (var c = 0; c < 6; c++) {
                vertices[baseIdx + c] = new TileVertexExpanded(TileCorners[c], TileCorners[c], tile);
            }
        }
        return tiles.Length * 6;
    }

    // Rebuilds one chunk's GPU mesh from its tile data (only when the chunk changed - see TileMap.TileChunk).
    public static void UploadTileChunk(TileChunkMesh mesh, ReadOnlySpan<TileData> tiles) {
        var count = ExpandTiles(tiles, _tileVertexData);
        mesh.Upload(_tileVertexData.AsSpan(0, count));
    }

    public static void BeginTiles() {
        LastDrawCountTiles = 0;
        _shaderGround.Apply();
    }

    public static void DrawTileChunk(TileChunkMesh mesh) {
        mesh.Draw();
        LastDrawCountTiles += mesh.TileCount;
    }

    #endregion

    #region Render Shadow

    public static void StartDrawShadow() {
        LastDrawCountShadows = _shadowCount = 0;

        _defaultVao.Bind();
        _shaderShadow.SetValue("ShadowData", _shadowBuffer);
        _shaderShadow.Apply();
    }

    public static void DrawShadow(ShadowData shadow) {
        _shadowData[_shadowCount] = shadow;
        _shadowCount++;

        if (_shadowCount == _shadowData.Length) {
            FlushBufferShadow();
        }
    }

    private static void FlushBufferShadow() {
        _shadowBuffer.SetData(_shadowData.AsSpan(0, _shadowCount), 0);

        GL.DrawArrays(PrimitiveType.Triangles, 0, _shadowCount * 6);
        GpuStats.DrawCalls++;
        GpuStats.UploadBytes += (long)_shadowCount * System.Runtime.CompilerServices.Unsafe.SizeOf<ShadowData>();

        LastDrawCountShadows += _shadowCount;
        _shadowCount = 0;
    }

    public static void EndShadowDraw() {
        if (_shadowCount == 0) {
            return;
        }

        FlushBufferShadow();
    }

    #endregion

    #region Render Model

    private static int _modelInstanceCount;

    // Vertices written straight into the expanded buffer (crossed cards: their shape depends on the sprite, so there is no shared mesh).
    private static int _directVertexCount;

    public static void StartDrawModel() {
        LastDrawCountEntities = 0;
        _modelInstanceCount = 0;
        _directVertexCount = 0;

        _shaderModel.Apply();
        _modelVao.Bind();
    }

    public static void SetEntityModel(ModelType model) => _entityModel = model;

    // Each model instance's mesh is expanded into real, non-instanced vertices (base mesh
    // vertex + a full copy of the instance data) rather than drawn via a shared per-instance
    // attribute buffer with glDrawElementsInstanced - real GL instancing (that, plus
    // glVertexBindingDivisor) proved unreliable on some older/low-end GPU drivers (e.g. older
    // Intel integrated graphics). See InstanceAttributeBuffer and ModelVertexExpanded.
    public static void DrawModel(VertexModel vertexModel) {
        var info = ModelData.ModelRenderInfo[_entityModel];
        var vertsPerInstance = info.PrimitiveCount * 3;

        if (_modelInstanceCount == _modelInstances.Length ||
            (_modelInstanceCount + 1) * vertsPerInstance > _modelVertexExpandedData.Length) {
            FlushBufferModel();
        }

        _modelInstances[_modelInstanceCount] = vertexModel;
        _modelInstanceCount++;
    }

    public static void FlushBufferModel() {
        if (_directVertexCount > 0) {
            _modelExpandedBuffer.SetData(_modelVertexExpandedData.AsSpan(0, _directVertexCount));
            GL.DrawArrays(PrimitiveType.Triangles, 0, _directVertexCount);
            GpuStats.DrawCalls++;
            _directVertexCount = 0;
        }

        if (_modelInstanceCount < 1) {
            return;
        }

        var info = ModelData.ModelRenderInfo[_entityModel];
        var vertsPerInstance = info.PrimitiveCount * 3;
        var totalVerts = 0;

        for (var i = 0; i < _modelInstanceCount; i++) {
            var instance = _modelInstances[i];
            for (var v = 0; v < vertsPerInstance; v++) {
                var baseVertex = ModelData.Vertices[ModelData.Indices[info.IndexOffset + v]];
                _modelVertexExpandedData[totalVerts] = new ModelVertexExpanded(baseVertex, instance);
                totalVerts++;
            }
        }

        _modelExpandedBuffer.SetData(_modelVertexExpandedData.AsSpan(0, totalVerts));
        GL.DrawArrays(PrimitiveType.Triangles, 0, totalVerts);
        GpuStats.DrawCalls++;

        _modelInstanceCount = 0;
    }

    #endregion

    #region Render Crossed Cards

    private const int VerticesPerCard = 2 * 6;     // both faces of a card (the model pass culls one side), 6 vertices per face

    // `cards` upright copies of one picture spread evenly round the vertical axis (2 = a cross along x and y, 4 = a star every 45
    // degrees), standing in the world at `position`, `width` wide, `height` tall, their bottom edge `bottom` above (negative: below) the
    // position. `uv` is the atlas region, `sortId` the object's depth at its position. Every corner gets ITS OWN depth - the value an
    // object standing on that corner's ground point would get (Entity.UpdateVisibility: 0.5 + 0.4 x the camera's depth column) - so the
    // cards interleave per pixel where they cross and sort properly against the billboards around them. Windings copy ModelData.Props.
    public static void DrawCrossedCards(Vector3 position, Vector4 uv, float width, float height, float bottom, float sortId, int cards = 2) {
        cards = Math.Clamp(cards, 1, 8);
        if (_directVertexCount + cards * VerticesPerCard > _modelVertexExpandedData.Length) {
            FlushBufferModel();
        }

        var hw = width * 0.5f;
        var z0 = bottom;
        var z1 = bottom + height;
        var dx = 0.4f * Depth.M12;
        var dy = 0.4f * Depth.M22;

        for (var i = 0; i < cards; i++) {
            var angle = MathF.PI * i / cards;
            var ex = MathF.Cos(angle) * hw;       // half of the card's span in the ground plane
            var ey = MathF.Sin(angle) * hw;
            var a = new Vector3(-ex, -ey, 0);
            var b = new Vector3(ex, ey, 0);
            // one face for each side: (a -> b) and (b -> a) along the top edge
            Face(position, uv, sortId, dx, dy, a with { Z = z1 }, b with { Z = z1 }, b with { Z = z0 }, a with { Z = z0 });
            Face(position, uv, sortId, dx, dy, b with { Z = z1 }, a with { Z = z1 }, a with { Z = z0 }, b with { Z = z0 });
        }
    }

    private static readonly Vector2 UvTl = new(0, 0), UvTr = new(1, 0), UvBr = new(1, 1), UvBl = new(0, 1);

    private static void Face(Vector3 position, Vector4 uv, float sortId, float dx, float dy, Vector3 tl, Vector3 tr, Vector3 br, Vector3 bl, float shade = RenderConfig.Shade) {
        CardVertex(position, uv, sortId, dx, dy, tl, UvTl, shade);
        CardVertex(position, uv, sortId, dx, dy, tr, UvTr, shade);
        CardVertex(position, uv, sortId, dx, dy, br, UvBr, shade);
        CardVertex(position, uv, sortId, dx, dy, tl, UvTl, shade);
        CardVertex(position, uv, sortId, dx, dy, br, UvBr, shade);
        CardVertex(position, uv, sortId, dx, dy, bl, UvBl, shade);
    }

    // A flat picture (lying in the ground plane, `width` x `height` tiles, turned by `rotation` about the object) drawn as a stack of layers
    // from the ground up to `thickness`, top layer nearest (a slightly smaller depth per layer, so the depth test lets each layer draw over
    // the one below). Both windings of every layer, because the model pass culls one side. The picture's top edge lies towards -y (screen
    // up at camera angle 0), the same as the flat billboard it replaces.
    public static void DrawFlatStack(Vector3 position, Vector4 uv, float width, float height, float rotation, float thickness, float sortId) {
        var layers = Math.Clamp((int)MathF.Round(thickness * 24f), 2, 16);      // about one layer per 4 screen pixels at 100% zoom
        if (_directVertexCount + layers * 12 > _modelVertexExpandedData.Length) {
            FlushBufferModel();
        }

        var hw = width * 0.5f;
        var hh = height * 0.5f;
        var c = MathF.Cos(rotation);
        var s = MathF.Sin(rotation);
        // the picture's corners in the ground plane: top-left, top-right, bottom-right, bottom-left
        var tl = new Vector3(-hw * c + hh * s, -hw * s - hh * c, 0);
        var tr = new Vector3(hw * c + hh * s, hw * s - hh * c, 0);
        var br = new Vector3(hw * c - hh * s, hw * s + hh * c, 0);
        var bl = new Vector3(-hw * c - hh * s, -hw * s + hh * c, 0);

        for (var i = 0; i < layers; i++) {
            var z = thickness * i / (layers - 1);
            var depth = sortId - 0.0004f * i;
            // The layers under the top one are the object's SIDE: shaded hard towards black (Model.frag darkens by shade x nearness to the
            // ground), so the side is a dark wall instead of the picture's details repeated down the screen, which looked dragged / smeared.
            var shade = i == layers - 1 ? RenderConfig.Shade : SideShade;
            Face(position, uv, depth, 0, 0, tl with { Z = z }, tr with { Z = z }, br with { Z = z }, bl with { Z = z }, shade);
            Face(position, uv, depth, 0, 0, tr with { Z = z }, tl with { Z = z }, bl with { Z = z }, br with { Z = z }, shade);
        }
    }

    private const float SideShade = 4f;

    private static void CardVertex(Vector3 position, Vector4 uv, float sortId, float dx, float dy, Vector3 local, Vector2 baseUv, float shade) {
        var depth = sortId + local.X * dx + local.Y * dy;
        _modelVertexExpandedData[_directVertexCount++] = new ModelVertexExpanded(
            new VertexBase(local, baseUv),
            new VertexModel(position, uv, new Vector3(0f, depth, shade)));
    }

    #endregion


    #region Render Entity

    public static void StartDrawEntity() {
        LastDrawCountEntities = 0;

        _entityVao.Bind();
        _shaderObject.Apply();
    }

    public static void FlushBufferEntity(List<VertexObject> targets) {
        if (targets.Count < 1) return;

        var chunks = 1 + targets.Count / _entityData.Length;
        var span = CollectionsMarshal.AsSpan(targets);
        span.Sort();

        for (var i = 0; i < chunks; i++) {
            var start = i * _entityData.Length;
            var len = Math.Min(_entityData.Length, span.Length - start);
            var slice = span.Slice(start, len);

            for (var j = 0; j < len; j++) {
                var entity = slice[j];
                var baseIdx = j * 6;
                for (var c = 0; c < 6; c++) {
                    _entityVertexData[baseIdx + c] = new EntityVertexExpanded(ObjectCorners[c], ObjectUVs[c], entity);
                }
            }

            _entityDataBuffer.SetData(_entityVertexData.AsSpan(0, len * 6));

            // Pass 1: opaque pixels only — depth writes ON, no blend
            GL.DepthMask(true);
            GL.DepthFunc(DepthFunction.Less);
            GL.Disable(EnableCap.Blend);
            _shaderObject.SetValue("RenderPass", 0);
            GL.DrawArrays(PrimitiveType.Triangles, 0, len * 6);
            GpuStats.DrawCalls++;

            // Pass 2: glow/outline pixels only — depth writes OFF, test still rejects hidden glows
            GL.DepthMask(false);
            GL.DepthFunc(DepthFunction.Lequal);
            GL.Enable(EnableCap.Blend);
            _shaderObject.SetValue("RenderPass", 1);
            GL.DrawArrays(PrimitiveType.Triangles, 0, len * 6);
            GpuStats.DrawCalls++;

            // Restore
            GL.DepthMask(true);
            GL.DepthFunc(DepthFunction.Less);
        }
    }

    #endregion
}