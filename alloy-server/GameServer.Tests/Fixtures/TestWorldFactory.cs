using System.Buffers.Binary;
using System.Xml.Linq;
using Common.Resources.World;
using Common.Resources.Xml;
using Common.Resources.Xml.Descriptors;
using Common.Utilities.Collections;
using GameServer.Game.Entities;
using GameServer.Game.Worlds;
using Ionic.Zlib;

namespace GameServer.Tests.Fixtures;

/// <summary>
/// Builds a minimal, self-contained World/Entity graph for tests - no XML or map files touched
/// on disk. Registers into the same static XmlLibrary/WorldLibrary dictionaries production code
/// uses, under a dedicated high type-id range and a dedicated config name, so it can't collide
/// with real game content or with other test classes.
/// </summary>
public static class TestWorldFactory {
    public const ushort GroundType = 60000;
    public const ushort CharacterObjectType = 60001;
    public const ushort EnemyObjectType = 60002;

    private const string ConfigName = "__TestWorld__";

    private static readonly object _lock = new();
    private static bool _registered;

    public static World CreateWorld(int width = 4, int height = 4) {
        lock (_lock) {
            EnsureRegistered(width, height);
        }

        return new World(World.TEST_ID, 0, WorldLibrary.WorldConfigs[ConfigName]);
    }

    public static EntityId SpawnCharacter(World world) {
        var entity = new Entity(CharacterObjectType);
        ref var added = ref world.EnterWorld(ref entity);
        return added.Id;
    }

    public static EntityId SpawnEnemy(World world) {
        var entity = new Entity(EnemyObjectType);
        ref var added = ref world.EnterWorld(ref entity);
        return added.Id;
    }

    private static void EnsureRegistered(int width, int height) {
        if (_registered)
            return;

        var groundXml = new XElement("Ground", new XElement("Speed", 1.0f));
        XmlLibrary.TileDescs.TryAdd(GroundType, new TileDesc(groundXml, "__TestGround__", GroundType));

        var characterXml = new XElement("Object", new XElement("Class", "Character"));
        XmlLibrary.ObjectDescs.TryAdd(CharacterObjectType,
            new ObjectDesc(characterXml, "__TestCharacter__", CharacterObjectType));

        var enemyXml = new XElement("Object", new XElement("Enemy"));
        XmlLibrary.ObjectDescs.TryAdd(EnemyObjectType, new ObjectDesc(enemyXml, "__TestEnemy__", EnemyObjectType));

        var config = new WorldConfig {
            Id = World.TEST_ID,
            Name = ConfigName,
            DisplayName = ConfigName,
            Maps = ["test"],
        };
        WorldLibrary.WorldConfigs.TryAdd(ConfigName, config);
        WorldLibrary.MapDatas.TryAdd(ConfigName, [new MapData(BuildWmapBytes(width, height, GroundType), "test.wmap")]);

        _registered = true;
    }

    /// <summary>
    /// Hand-builds a minimal version-0 .wmap payload: one tile template (our ground type, no
    /// placed object, no terrain/region), tiled across the given dimensions. See
    /// <see cref="MapData"/>'s LoadWMap for the format this mirrors.
    /// </summary>
    private static byte[] BuildWmapBytes(int width, int height, ushort groundType) {
        using var payload = new MemoryStream();

        void WriteInt16(short v) {
            var tmp = new byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(tmp, v);
            payload.Write(tmp, 0, 2);
        }

        void WriteUInt16(ushort v) {
            var tmp = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(tmp, v);
            payload.Write(tmp, 0, 2);
        }

        void WriteInt32(int v) {
            var tmp = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(tmp, v);
            payload.Write(tmp, 0, 4);
        }

        WriteInt16(1); // tile template count
        WriteUInt16(groundType); // tileType -> GroundType
        payload.WriteByte(0); // objIdLen (no placed object)
        payload.WriteByte(0); // objectCfg length
        payload.WriteByte(0); // terrain (None)
        payload.WriteByte(0); // region (None)

        WriteInt32(width);
        WriteInt32(height);

        for (var i = 0; i < width * height; i++)
            WriteInt16(0); // every tile uses template index 0

        var decompressed = payload.ToArray();

        using var compressedStream = new MemoryStream();
        using (var zlib = new ZlibStream(compressedStream, CompressionMode.Compress, true))
            zlib.Write(decompressed, 0, decompressed.Length);

        var compressed = compressedStream.ToArray();

        var result = new byte[1 + compressed.Length];
        result[0] = 0; // wmap version
        compressed.CopyTo(result, 1);
        return result;
    }
}
