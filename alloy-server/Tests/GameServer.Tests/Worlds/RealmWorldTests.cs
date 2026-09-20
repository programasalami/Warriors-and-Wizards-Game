using Common;
using Common.Resources.Config;
using Common.Resources.World;
using Common.Resources.Xml;
using Common.Utilities;
using GameServer.Game;
using GameServer.Game.Entities;
using GameServer.Game.Worlds;
using GameServer.Game.Worlds.Logic;

namespace GameServer.Tests.Worlds;

/// <summary>
/// Runs against the REAL game data (the same maps, XMLs and configs the server ships), following the server's own start-up order: the Nexus is
/// built, spawns its Realm Portal, and that portal is linked to a live Realm world whose own exit leads back to the Nexus.
/// </summary>
public class RealmWorldTests {
    private const ushort RealmPortalType = 0x0704;

    private static readonly object _lock = new();
    private static bool _loaded;

    private static void EnsureGameDataLoaded() {
        lock (_lock) {
            if (_loaded)
                return;

            var config = GameServerConfig.Config;
            EnumUtils.Load();
            XmlLibrary.Load(config.XmlsDir);
            WorldLibrary.Load(config.WorldsDir);
            RealmManager.Init();
            _loaded = true;
        }
    }

    private static IEnumerable<World> RealmWorlds() => RealmManager.Worlds.Values.OfType<Realm>();

    private static Nexus GetNexus() => (Nexus)RealmManager.Worlds[World.NEXUS_ID];

    [Fact]
    public void RealmMapIsAnExploreableIslandWithASpawnClearing() {
        EnsureGameDataLoaded();

        var map = WorldLibrary.MapDatas["Realm"].Single();
        Assert.Equal((140, 140), (map.Width, map.Height));
        Assert.Equal(16, map.Regions[TileRegion.Spawn].Count);   // the 4x4 spawn square
        Assert.Equal(200, WorldLibrary.WorldConfigs["Realm"].MaxPlayers);
    }

    [Fact]
    public void RealmSpawnTilesAreOpenGround() {
        EnsureGameDataLoaded();

        var realm = RealmWorlds().First();
        foreach (var spawn in realm.Map.Data.Regions[TileRegion.Spawn]) {
            Assert.True(realm.Map.IsPassable(spawn.X, spawn.Y, spawning: true), $"spawn tile {spawn} is blocked");
        }
    }

    [Fact]
    public void NexusMapHasNoBakedInRealmPortalAndOneRealmPortalSpot() {
        EnsureGameDataLoaded();

        // the portal is spawned live by the server (RealmCount), not drawn into the map
        var map = WorldLibrary.MapDatas["Nexus"].Single();
        Assert.DoesNotContain(map.Entities, e => e.ObjType == RealmPortalType);
        Assert.Single(map.Regions[TileRegion.Realm_Portals]);
    }

    [Fact]
    public void NexusSpawnsOneLiveRealmPortalPerRealmCount() {
        EnsureGameDataLoaded();

        var nexus = GetNexus();
        var portals = 0;
        foreach (ref var entity in nexus.Entities) {
            if (entity.ObjectType == RealmPortalType)
                portals++;
        }

        Assert.Equal(GameServerConfig.Config.RealmCount, portals);
        Assert.Equal(GameServerConfig.Config.RealmCount, RealmWorlds().Count());
    }

    [Fact]
    public void TheRealmPortalLeadsToARealmWorld() {
        EnsureGameDataLoaded();

        var nexus = GetNexus();
        World destination = null;
        foreach (ref var entity in nexus.Entities) {
            if (entity.ObjectType != RealmPortalType)
                continue;

            ref var portal = ref nexus.PortalDatas.Get(entity.Id);
            destination = portal.GetWorldInstance(null);
        }

        Assert.IsType<Realm>(destination);
        Assert.Contains(destination, RealmWorlds());
    }

    [Fact]
    public void RealmMapUsesOnlyTheNewForestTileset() {
        EnsureGameDataLoaded();

        // No stock art on this map: every ground is a Woodland* tile and every object a Forest* one (see Tools/ForestContent/gen_realm.py).
        var map = WorldLibrary.MapDatas["Realm"].Single();
        var strangers = new HashSet<string>();
        foreach (var tile in map.Tiles) {
            if (tile.GroundType != 255 && !XmlLibrary.TileDescs[tile.GroundType].GroundId.StartsWith("Woodland "))
                strangers.Add(XmlLibrary.TileDescs[tile.GroundType].GroundId);
            if (tile.ObjectType != 0 && tile.ObjectType != 255 && !XmlLibrary.ObjectDescs[tile.ObjectType].ObjectId.StartsWith("Forest "))
                strangers.Add(XmlLibrary.ObjectDescs[tile.ObjectType].ObjectId);
        }

        Assert.Empty(strangers);
    }

    [Fact]
    public void APortalToTheNexusPlacedInTheRealmLeadsBack() {
        EnsureGameDataLoaded();

        // the Realm has no exit portal yet (no custom portal art) - this is here for when one is added
        var realm = RealmWorlds().First();
        var entity = new Entity(XmlLibrary.Id2Object("Portal to Nexus").ObjectType);
        ref var placed = ref realm.EnterWorld(ref entity);
        ref var portal = ref realm.PortalDatas.Get(placed.Id);

        Assert.Same(GetNexus(), portal.GetWorldInstance(null));
    }
}
