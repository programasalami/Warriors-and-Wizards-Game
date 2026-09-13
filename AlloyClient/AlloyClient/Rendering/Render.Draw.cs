using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using AlloyClient.Assets;
using AlloyClient.Rendering.VertexData;
using Alloy.Common;
using OpenTK.Graphics.OpenGL;

namespace AlloyClient.Rendering;

public static partial class Render {
    public static int LastDrawCountTiles;
    public static int LastDrawCountShadows;
    public static int LastDrawCountEntities;
    
    private static int _shadowCount;
    private static ModelType _entityModel;

    #region Render Tile

    public static void DrawTiles(ReadOnlySpan<TileData> span) {
        LastDrawCountTiles = span.Length;

        for (var i = 0; i < span.Length; i++) {
            var tile = span[i];
            var baseIdx = i * 6;
            for (var c = 0; c < 6; c++) {
                _tileVertexData[baseIdx + c] = new TileVertexExpanded(TileCorners[c], TileCorners[c], tile);
            }
        }

        _tileBuffer.SetData(_tileVertexData.AsSpan(0, span.Length * 6));

        _tileVao.Bind();
        _shaderGround.Apply();

        GL.DrawArrays(PrimitiveType.Triangles, 0, span.Length * 6);
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

    public static void StartDrawModel() {
        LastDrawCountEntities = 0;
        _modelInstanceCount = 0;

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

        _modelInstanceCount = 0;
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

            // Pass 2: glow/outline pixels only — depth writes OFF, test still rejects hidden glows
            GL.DepthMask(false);
            GL.DepthFunc(DepthFunction.Lequal);
            GL.Enable(EnableCap.Blend);
            _shaderObject.SetValue("RenderPass", 1);
            GL.DrawArrays(PrimitiveType.Triangles, 0, len * 6);

            // Restore
            GL.DepthMask(true);
            GL.DepthFunc(DepthFunction.Less);
        }
    }

    #endregion
}