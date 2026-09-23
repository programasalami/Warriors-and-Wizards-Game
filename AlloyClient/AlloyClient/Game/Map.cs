using System;
using System.Collections.Generic;
using System.Diagnostics;
using AlloyClient.Assets;
using AlloyClient.Game.Components.Hud;
using AlloyClient.Game.Objects;
using AlloyClient.Networking.Structs.DataObjects;
using AlloyClient.ParticleEffects;
using AlloyClient.Rendering;
using AlloyClient.Rendering.Types;
using AlloyClient.Rendering.VertexData;
using Alloy.UiLib.Signals;
using Alloy.Engine;
using AlloyClient.Logging;
using AlloyClient.Utils;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace AlloyClient.Game;

public class TileMap {

    public const int ChunkSize = 16;
    public const int ChunkArea = ChunkSize * ChunkSize;
    public const int ChunkRenderData = ChunkArea * MapTile.MaxTileData;

    private int _width;
    private int _height;

    private readonly Dictionary<Vector2i, TileChunk> _chunks = [];

    public MapTile this[Vector2i coords] => Get(coords);

    public void SetDimensions(int width, int height) {
        _width = width;
        _height = height;
    }

    public void SetTileChange(Vector2i coords) {
        var chunkId = new Vector2i(coords.X / ChunkSize, coords.Y / ChunkSize);

        if (!_chunks.TryGetValue(chunkId, out var chunk)) {
            return;
        }

        chunk.SetDirty();
    }

    public System.Collections.Generic.IEnumerable<Vector2i> DiagChunkKeys() => _chunks.Keys;

    public bool GetChunkData(Vector2i chunkCoords, out ReadOnlySpan<TileData> data) {
        if (!_chunks.TryGetValue(chunkCoords, out var chunk)) {
            data = default;
            return false;
        }

        data = chunk.GetTileData();
        return true;
    }

    // Draws one chunk (main thread only): rebuilds its GPU mesh first if its tiles changed since the last draw.
    public bool DrawChunk(Vector2i chunkCoords) {
        if (!_chunks.TryGetValue(chunkCoords, out var chunk))
            return false;
        chunk.Draw();
        return true;
    }

    // Meshes of chunks that were cleared. Clear() can run on the network thread (Client.Disconnect -> Map.Reset), where no GL call
    // is allowed, so the GPU objects are handed to the next Draw on the main thread instead.
    private static readonly List<TileChunkMesh> Graveyard = [];

    public void Clear() {
        lock (Graveyard) {
            foreach (var chunk in _chunks.Values) {
                var mesh = chunk.TakeMesh();
                if (mesh != null)
                    Graveyard.Add(mesh);
            }
        }
        _chunks.Clear();
    }

    public static void DeleteOrphanedMeshes() {
        lock (Graveyard) {
            foreach (var mesh in Graveyard)
                mesh.Delete();
            Graveyard.Clear();
        }
    }
    private MapTile Get(Vector2i coords) {
        if (coords.X < 0 || coords.X >= _width || coords.Y < 0 || coords.Y >= _height) {   // was '>' (off by one: a phantom chunk row/column)
            return null;
        }

        var chunkId = new Vector2i(coords.X / ChunkSize, coords.Y / ChunkSize);

        if (!_chunks.TryGetValue(chunkId, out var chunk)) {
            chunk = _chunks[chunkId] = new TileChunk();
        }

        return chunk.Get(coords);
    }

    private class TileChunk {

        private readonly MapTile[] _tiles = new MapTile[ChunkArea];

        private readonly TileData[] _data = new TileData[ChunkRenderData];

        private bool _dirty;

        private int _renderCount;

        public MapTile Get(Vector2i coords) {
            var index = (coords.Y % ChunkSize) * ChunkSize + (coords.X % ChunkSize);
            return _tiles[index] ?? (_tiles[index] = new MapTile(coords));
        }

        private TileChunkMesh _mesh;
        private bool _meshDirty = true;

        public void SetDirty() {
            _dirty = true;
            _meshDirty = true;
        }

        public TileChunkMesh TakeMesh() {
            var mesh = _mesh;
            _mesh = null;
            _meshDirty = true;
            return mesh;
        }

        public void Draw() {
            if (_meshDirty) {
                _mesh ??= new TileChunkMesh();
                Render.UploadTileChunk(_mesh, GetTileData());
                _meshDirty = false;
            }
            if (_mesh != null)
                Render.DrawTileChunk(_mesh);
        }

        public ReadOnlySpan<TileData> GetTileData() {
            if (!_dirty) {
                return new ReadOnlySpan<TileData>(_data, 0, _renderCount);
            }

            var count = 0;
            foreach (var tile in _tiles) {
                if (tile is null || tile.Type == Const.DefaultTile) {
                    continue;
                }

                var chunkData = _data.AsSpan(count);
                var tileData = tile.DrawTile();

                tileData.CopyTo(chunkData);
                count += tileData.Length;
            }

            _renderCount = count;
            _dirty = false;

            return new ReadOnlySpan<TileData>(_data, 0, _renderCount);
        }
    }
}

public static class Map {

    public const int ViewRadiusX = 3;
    public const int ViewRadiusY = 2;
    public const int ViewDiameterX = ViewRadiusX * 2 + 1;
    public const int ViewDiameterY = ViewRadiusY * 2 + 1;
    public const int VisibleChunks = ViewDiameterX * ViewDiameterY;

    private static readonly ILogger Logger = ILogger.CreateLogger(nameof(Map));

    public const int TileRenderDistance = 20;

    public static GameTime LastGameTime;

    public static double CurrentTime;

    public static int Width;
    public static int Height;
    public static string Name;
    public static string DisplayName;
    public static int Difficulty;
    public static uint Seed;
    public static int Background;
    public static bool AllowPlayerTeleport;
    public static bool ShowDisplays;

    private static readonly TileMap Tiles = new ();
    public static readonly RenderStorage EntityStorage = new();
    public static readonly Dictionary<int, Player> Players = new();
    public static readonly Dictionary<int, Entity> Entities = new(); // todo: add players to separate dic for minimap prio
    public static readonly Dictionary<int, Entity> InteractiveObjects = new();
    // Only entities flagged IsEnemy: what the projectile hit tests scan (they used to walk every entity of the world, 60 times a
    // second per bullet - 2026-09-21 audit, M2).
    public static readonly Dictionary<int, Entity> Enemies = new();

    public static readonly List<ParticleEffect> ParticleGenerators = [];

    public static int ParticleGenCount;

    private static readonly List<Projectile> Projectiles = [];

    public static int LocalPlayerId;
    public static Player LocalPlayer;

    public static int LastTickId;

    public static readonly Signal<Player> OnPlayerUpdate = new();

    private static int _particleCount;
    private static readonly ParticleData[] Particles = new ParticleData[30000];

    private static readonly List<VertexObject> RenderTargets = [];

    private static readonly HashSet<Vector2i> SightCircle = [];

    static Map() {
        for (var x = -TileRenderDistance; x < TileRenderDistance; x++) {
            for (var y = -TileRenderDistance; y < TileRenderDistance; y++) {
                if (x * x + y * y >= TileRenderDistance * TileRenderDistance) {
                    continue;
                }

                SightCircle.Add(new Vector2i(x, y));
            }
        }
    }

    public static void InitMap(int width, int height, string name, string display, int diff, uint seed, int background, bool allowTp, bool showDisplays) {
        Width = width;
        Height = height;
        Name = name;
        DisplayName = display;
        Difficulty = diff;
        Seed = seed;
        Background = background;
        AllowPlayerTeleport = allowTp;
        ShowDisplays = showDisplays;

        Minimap.OnNewMap.Dispatch(width, height);
        Tiles.SetDimensions(width, height);
    }

    public static void Update(in GameTime gameTime, in Camera camera) {
        CurrentTime = gameTime.TotalMs;
        var time = gameTime.TotalMs;
        var dt = gameTime.ElapsedMs;

        _particleCount = 0;
        var fullMatrix = camera.Matrix;
        var matrix = new DepthMatrix(camera.Matrix);
        var cullRadius = CullRules.Radius(camera.VisibleTileRadius);
        var cameraPos = camera.Position;

        foreach (var (objectId, entity) in Entities) {
            if (!entity.Update(time, dt)) {
                Entities.Remove(objectId);
                Enemies.Remove(objectId);
            }

            entity.UpdateVisibility(ref fullMatrix, in cameraPos, cullRadius);
        }

        for (var i = ParticleGenCount - 1; i >= 0; i--) {
            var gen = ParticleGenerators[i];
            if (gen.Update(time, dt)) {
                continue;
            }

            ParticleGenCount--;
            ParticleGenerators[i] = ParticleGenerators[ParticleGenCount];
            ParticleGenerators[ParticleGenCount] = null;
        }

        PerfCounters.ParticleGenerators = ParticleGenCount;

        for (var i = Projectiles.Count - 1; i >= 0; i--) {
            var proj = Projectiles[i];
            if (proj.Update(gameTime))
                continue;

            ObjectPools.Projectiles.Push(proj);

            var idx = Projectiles.Count - 1;
            Projectiles[i] = Projectiles[idx];
            Projectiles.RemoveAt(idx);
        }

        PerfCounters.Projectiles = Projectiles.Count;
    }

    public static void FixedUpdate(in GameTime gameTime) {
        foreach (var projectile in Projectiles) {
            projectile.FixedUpdate(in gameTime);
        }
    }


    public static void Draw(in GameTime gameTime, in Camera camera) {
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);

        LastGameTime = gameTime;

        #region Tile

        TileMap.DeleteOrphanedMeshes();     // GPU objects of a world we left (queued from whatever thread cleared the map)

        var camChunkPos = new Vector2i((int)camera.Position.X / TileMap.ChunkSize, (int)camera.Position.Y / TileMap.ChunkSize) - new Vector2i(ViewRadiusX, ViewRadiusY);

        var sect = PerfSections.Begin();
        Render.BeginTiles();
        for (var i = 0; i < VisibleChunks; i++) {
            Tiles.DrawChunk(camChunkPos + new Vector2i(i % ViewDiameterX, i / ViewDiameterX));
        }

        PerfSections.End(PerfSections.Section.DrawTiles, sect);
        #endregion
        #region Shadows

        sect = PerfSections.Begin();
        Render.StartDrawShadow();

        foreach (var type in EntityStorage[ModelType.PbObject]) {
            if (type.Visible)
                type.DrawShadow();
        }

        foreach (var type in EntityStorage[ModelType.CrossedCards]) {
            if (type.Visible)
                type.DrawShadow();
        }

        StaticProps.DrawShadows(camera);

        foreach (var projectile in Projectiles) {
            Render.DrawShadow(projectile.DrawShadow());
        }

        Render.EndShadowDraw();
        PerfSections.End(PerfSections.Section.DrawShadows, sect);

        #endregion

        GL.Enable(EnableCap.DepthTest);

        #region Particles

        sect = PerfSections.Begin();
        Render.DrawParticles(Particles, _particleCount);
        PerfSections.End(PerfSections.Section.DrawParticles, sect);

        #endregion

        GL.Enable(EnableCap.CullFace);

        #region Entities

        RenderTargets.Clear();

        sect = PerfSections.Begin();
        Render.StartDrawModel();

        for (var i = 0; i < EntityStorage.Types.Length; i++) {
            var type = (ModelType)i;
            var list = EntityStorage.Types[i];
            if (type == ModelType.Null || type == ModelType.PbObject) continue;

            Render.SetEntityModel(type);

            foreach (var entity in list) {
                if (entity.Visible) {
                    entity.Draw(RenderTargets, gameTime.TotalMs);
                    Render.LastDrawCountEntities++;
                }

            }

            Render.FlushBufferModel();
        }

        StaticProps.Draw(camera);          // every static wall / board / star / log in view: one pre-built mesh per area, nothing uploaded
        Render.LastDrawCountEntities += StaticProps.LastDrawnProps;

        GL.Disable(EnableCap.CullFace);
        PerfSections.End(PerfSections.Section.DrawModels, sect);

        sect = PerfSections.Begin();
        Render.StartDrawEntity();

        foreach (var type in EntityStorage[ModelType.PbObject]) {
            if (type.Visible) {
                type.Draw(RenderTargets, gameTime.TotalMs);
            }
        }

        foreach (var projectile in Projectiles) {
            RenderTargets.Add(projectile.Draw(in camera.DepthMatrix));
        }

        Render.FlushBufferEntity(RenderTargets);
        Render.LastDrawCountEntities += RenderTargets.Count;
        PerfSections.End(PerfSections.Section.DrawEntities, sect);

        #endregion
    }

    public static MapTile LookupTile(Vector2 position) => LookupTile((int)position.X, (int)position.Y);

    public static bool LookupTile(int x, int y, out MapTile tile) => (tile = LookupTile(x, y)) != null;

    public static MapTile LookupTile(int x, int y) => LookupTile(new Vector2i(x, y));

    public static bool LookupTile(Vector2i position, out MapTile tile) => (tile = LookupTile(position)) != null;

    public static MapTile LookupTile(Vector2i position) => Tiles[position];

    private static readonly MapTile[] RebuildData = new MapTile[9];

    public static void SetTileData(int x, int y, ushort type) {
        if (!LookupTile(x, y, out var tile)) {
            return;
        }

        tile.SetType(type);

        for (var y1 = y - 1; y1 <= y + 1; y1++){
            for (var x1 = x - 1; x1 <= x + 1; x1++) {
                RebuildTile(LookupTile(x1, y1));
            }
        }

        Array.Clear(RebuildData);
    }

    private static void RebuildTile(MapTile tile) {
        if (tile is null) {
            return;
        }

        var idx = 0;
        for (var y1 = tile.Y - 1; y1 <= tile.Y + 1; y1++){
            for (var x1 = tile.X - 1; x1 <= tile.X + 1; x1++) {
                RebuildData[idx++] = LookupTile(x1, y1);
            }
        }

        tile.Rebuild(RebuildData);
        Tiles.SetTileChange(new Vector2i(tile.X, tile.Y));
    }

    // Hard budget for live particle effects (trails, hit sparks, fountains). Beyond it new effects are dropped rather than letting a
    // busy fight grow the list without limit (2026-09-21 audit: the runaway fixed-update loop used to add thousands per frame).
    public const int MaxParticleGenerators = 300;

    public static void AddParticleEffect(ParticleEffect effect) {
        if (effect == null) {
            return;
        }

        if (ParticleGenCount >= MaxParticleGenerators) {
            PerfCounters.ParticleEffectsDroppedTotal++;
            return;
        }

        if (ParticleGenCount == ParticleGenerators.Count) {
            ParticleGenerators.Add(effect);
        } else {
            ParticleGenerators[ParticleGenCount] = effect;
        }

        ParticleGenCount++;
        PerfCounters.ParticleGenerators = ParticleGenCount;
    }

    // Everything that belongs to the world the player is leaving: projectiles back to their pool, particle effects, the player /
    // interactive lookups and any queued damage text. Used by Reset (disconnect) and by Reconnect (world switch) - the switch used to
    // clear only Entities, leaving ghost bullets, stale "nearby players" and old effects running in the next world.
    public static void ClearWorldObjects() {
        foreach (var projectile in Projectiles) {
            ObjectPools.Projectiles.Push(projectile);
        }
        Projectiles.Clear();

        for (var i = 0; i < ParticleGenerators.Count; i++) {
            ParticleGenerators[i] = null;
        }
        ParticleGenerators.Clear();
        ParticleGenCount = 0;
        _particleCount = 0;

        Entities.Clear();
        Enemies.Clear();
        Players.Clear();
        InteractiveObjects.Clear();
        EntityStorage.Clear();
        StaticProps.Clear();
        RenderTargets.Clear();
        Ui.Character.NotificationLayer.ClearQueued();

        PerfCounters.Projectiles = 0;
        PerfCounters.ParticleGenerators = 0;
    }

    public static void AddEntity(Entity en, Position position) {
        if (!Entities.TryAdd(en.ObjectId, en))
            return;

        if (en.Properties != null && en.Properties.IsEnemy)
            Enemies[en.ObjectId] = en;

        // Static walls / boards / stars / logs go into a pre-built area mesh (StaticProps); everything else is drawn per frame.
        if (!StaticProps.TryAdd(en, position.X, position.Y))
            EntityStorage.Add(en);

        if (en is Player p) {
            if(p.ObjectId != LocalPlayerId)
                Players.TryAdd(p.ObjectId, p);
            p.Ignored = PartyData.IgnoredPlayers.Contains(p.AccountId);
            p.Locked = PartyData.LockedPlayers.Contains(p.AccountId);
        }


        if (InteractPanel.IsInteractiveObject(en))
            InteractiveObjects.TryAdd(en.ObjectId, en);

        en.OnAddedToMap(position);
    }

    public static void RemoveEntity(int id) {
        if (!Entities.Remove(id, out var en))
            return;

        Enemies.Remove(id);
        Players.Remove(id);
        InteractiveObjects.Remove(id);

        if (!StaticProps.Remove(en))
            EntityStorage.Remove(en);
        en.OnRemovedFromMap();
    }

    public static void AddProjectile(Projectile proj) {
        Projectiles.Add(proj);
    }

    public static void AddParticles(ParticleData[] particles, int count) {
        if (_particleCount + count > Particles.Length)
            count = Particles.Length - _particleCount;

        if (count < 1) return;

        Array.Copy(particles, 0, Particles, _particleCount, count);
        _particleCount += count;
    }

    public static void AddParticle(ParticleData particle) {
        if (_particleCount + 1 > Particles.Length) return;

        Particles[_particleCount] = particle;
        _particleCount++;
    }

    public static void Reset() {
        Height = 0;
        Name = null;
        DisplayName = null;
        Difficulty = 0;
        Seed = 0;
        Background = 0;
        AllowPlayerTeleport = false;
        ShowDisplays = false;

        PartyData.Clear();
        ClearWorldObjects();
        LocalPlayerId = 0;
        LocalPlayer = null;

        LastTickId = 0;

        Tiles.Clear();
    }

    public static void OnLocalPlayerCreated(Entity entity) {
        if (LocalPlayer != null) {
            Logger.Log(LogLevel.Error, "Local player already exists");
            return;
        }

        if (entity is not Player player) {
            Logger.Log(LogLevel.Error, "Local player is not a player");
            return;
        }

        LocalPlayer = player;
        MinimapLayer.SetFocus(player);
        GameScreen.GameSprite.CreatePlayerDependentAssets();
        OnPlayerUpdate.Dispatch(player);
        Loading.WorldLoad.Mark(Loading.WorldMilestone.PlayerSpawned);
    }
}

public class RenderStorage {
    public readonly HashSet<RenderBase>[] Types = new HashSet<RenderBase>[(int)ModelType.Count];

    public HashSet<RenderBase> this[ModelType modelType] => Types[(int)modelType];

    public RenderStorage() {
        for (var i = 0; i < Types.Length; i++)
            Types[i] = new HashSet<RenderBase>();
    }

    public void Clear() {
        for (var i = 0; i < Types.Length; i++)
            Types[i].Clear();
    }

    public void Add(Entity entity) {
        var type = entity.RenderBaseType;
        var list = Types[(int)type.ModelType];
        list.Add(type);

        switch (type) {
            case TypeWall w:
                Add(w.Top);
                break;
        }
    }

    private void Add(RenderBase type) {
        if (type == null) {
            return;
        }

        var list = Types[(int)type.ModelType];
        list.Add(type);
    }

    public void Remove(Entity entity) {
        var type = entity.RenderBaseType;
        var list = Types[(int)type.ModelType];

        list.Remove(type);

        if (type is TypeWall w) {
            Remove(w.Top);
        }
    }

    private void Remove(RenderBase type) {
        var list = Types[(int)type.ModelType];

        list.Remove(type);
    }
}