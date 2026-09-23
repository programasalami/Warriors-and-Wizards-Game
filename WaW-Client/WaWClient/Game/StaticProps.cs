using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using WaWClient.Game.Objects;
using WaWClient.Rendering;
using WaWClient.Rendering.VertexData;
using OpenTK.Mathematics;

namespace WaWClient.Game;

// Static props (walls, boards, tree / bush / rock stars, stacked logs) grouped into 16x16-tile areas, each baked into one GPU mesh that is
// rebuilt only when a prop in that area arrives or leaves (2026-09-22). Before, the model pass expanded and uploaded every one of them every
// frame - the largest measured part of the frame, and it grew with every prop that came into view while walking. Now a frame draws each
// visible area's mesh with one draw call and uploads nothing. Moving or non-static model objects still take the per-frame path.
//
// Map.AddEntity / RemoveEntity route qualifying entities here instead of into Map.EntityStorage; Map.Draw calls DrawShadows in the shadow
// pass and Draw in the model pass. Clear can run on the network thread (Map.Reset), so GPU objects are deleted on the next Draw.
public static class StaticProps {
    public const int AreaSize = 16;

    private sealed class Area {
        public readonly List<Entity> Props = [];
        public bool Dirty = true;
        public StaticPropMesh Mesh;
    }

    private static readonly Dictionary<Vector2i, Area> Areas = [];
    private static readonly Dictionary<int, Vector2i> AreaOf = [];
    private static readonly List<StaticPropMesh> Graveyard = [];
    private static readonly List<ModelVertexExpanded> Scratch = new(8192);
    private static readonly object Sync = new();

    public static int LastDrawnProps { get; private set; }
    public static int Count { get { lock (Sync) return AreaOf.Count; } }

    // WAW_NO_STATIC_BAKE=1 draws static props the old per-frame way (for A/B checks of the look and the cost).
    private static readonly bool Disabled = Environment.GetEnvironmentVariable("WAW_NO_STATIC_BAKE") == "1";

    public static bool Qualifies(Entity entity) =>
        !Disabled && entity?.Properties != null && entity.Properties.Static && entity.RenderBaseType is IStaticProp;

    public static Vector2i AreaKey(float x, float y) => new((int)MathF.Floor(x / AreaSize), (int)MathF.Floor(y / AreaSize));

    public static bool TryAdd(Entity entity, float x, float y) {
        if (!Qualifies(entity))
            return false;
        var key = AreaKey(x, y);
        lock (Sync) {
            if (AreaOf.ContainsKey(entity.ObjectId))
                return true;
            if (!Areas.TryGetValue(key, out var area))
                Areas[key] = area = new Area();
            area.Props.Add(entity);
            area.Dirty = true;
            AreaOf[entity.ObjectId] = key;
        }
        return true;
    }

    public static bool Remove(Entity entity) {
        lock (Sync) {
            if (entity == null || !AreaOf.Remove(entity.ObjectId, out var key))
                return false;
            if (Areas.TryGetValue(key, out var area)) {
                area.Props.Remove(entity);
                area.Dirty = true;
            }
            return true;
        }
    }

    public static void Clear() {
        lock (Sync) {
            foreach (var area in Areas.Values)
                if (area.Mesh != null)
                    Graveyard.Add(area.Mesh);
            Areas.Clear();
            AreaOf.Clear();
        }
    }

    private static bool InView(Vector2i key, Vector2 camera, float radius) {
        var dx = (key.X + 0.5f) * AreaSize - camera.X;
        var dy = (key.Y + 0.5f) * AreaSize - camera.Y;
        return dx * dx + dy * dy <= radius * radius;
    }

    // An area is drawn when its centre is within the cull radius plus the area's half-diagonal (~11.3 tiles).
    private static float Radius(in Camera camera) => CullRules.Radius(camera.VisibleTileRadius) + AreaSize * 0.75f;

    // In the model pass (model shader applied, back faces culled). Leaves some area's VAO bound.
    public static void Draw(in Camera camera) {
        lock (Sync) {
            foreach (var mesh in Graveyard)
                mesh.Delete();
            Graveyard.Clear();

            var radius = Radius(camera);
            var drawn = 0;
            foreach (var (key, area) in Areas) {
                if (!InView(key, camera.Position, radius))
                    continue;
                if (area.Dirty)
                    Rebuild(area);
                area.Mesh?.Draw();
                drawn += area.Props.Count;
            }
            LastDrawnProps = drawn;
        }
    }

    // In the shadow pass: the props that cast a shadow (the card stars), in the areas in view.
    public static void DrawShadows(in Camera camera) {
        lock (Sync) {
            var radius = Radius(camera);
            foreach (var (key, area) in Areas) {
                if (!InView(key, camera.Position, radius))
                    continue;
                foreach (var entity in area.Props)
                    if (entity.RenderBaseType.HasShadow)
                        entity.RenderBaseType.DrawShadow();
            }
        }
    }

    private static void Rebuild(Area area) {
        Scratch.Clear();
        foreach (var entity in area.Props)
            ((IStaticProp)entity.RenderBaseType).Bake(Scratch);
        area.Mesh ??= new StaticPropMesh();
        area.Mesh.Upload(CollectionsMarshal.AsSpan(Scratch));
        area.Dirty = false;
    }
}
