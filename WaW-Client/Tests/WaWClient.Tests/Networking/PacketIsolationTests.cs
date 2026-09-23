using WaWClient.Networking;
using WaWClient.Networking.Packets.Incoming;
using WaWClient.Networking.Structs.DataObjects;

namespace WaWClient.Tests.Networking;

// Packets are parsed on the network thread and applied on the main thread a little later, so several packets of the same kind can be parsed before the
// first one is applied (a busy frame, a slow start, the browser's low frame rate). Each packet must keep its OWN data. They used to share static
// buffers: the newer packet overwrote the older one's tiles before they were applied, which left short runs of black tiles in the map at random spots.
public class PacketIsolationTests {
    private static byte[] UpdateWith(params (short X, short Y, ushort Type)[] tiles) {
        var buffer = new byte[2 + tiles.Length * 6 + 2 + 2];
        var writer = new SpanWriter(buffer);
        writer.Write((short)tiles.Length);
        foreach (var (x, y, type) in tiles) {
            var tile = new TileDef { X = x, Y = y, Type = type };
            tile.Write(ref writer);
        }

        writer.Write((short)0);   // no new objects
        writer.Write((short)0);   // no drops
        return buffer;
    }

    private static byte[] NewTickWith(params (int Id, float X, float Y)[] objects) {
        var buffer = new byte[2 + objects.Length * (4 + 8 + 1)];
        var writer = new SpanWriter(buffer);
        writer.Write((short)objects.Length);
        foreach (var (id, x, y) in objects) {
            writer.Write(id);
            writer.Write(new Position(x, y));
            writer.Write((byte)0);   // no stats
        }

        return buffer;
    }

    [Fact]
    public void TwoUpdatesParsedBeforeEitherIsApplied_KeepTheirOwnTiles() {
        var big = Enumerable.Range(0, 40).Select(i => ((short)i, (short)0, (ushort)5)).ToArray();
        var small = new[] { ((short)100, (short)9, (ushort)7), ((short)101, (short)9, (ushort)7), ((short)102, (short)9, (ushort)7) };

        var first = Update.CreatePacket();
        var second = Update.CreatePacket();
        var firstBytes = UpdateWith(big);
        var secondBytes = UpdateWith(small);
        var firstReader = new SpanReader(firstBytes);
        first.Read(ref firstReader);
        var secondReader = new SpanReader(secondBytes);
        second.Read(ref secondReader);   // parsed BEFORE the first one has been applied

        Assert.Equal(40, first.TileCount);
        for (var i = 0; i < 40; i++) {
            Assert.Equal((short)i, first.Tiles[i].X);
            Assert.Equal((short)0, first.Tiles[i].Y);
            Assert.Equal((ushort)5, first.Tiles[i].Type);
        }

        Assert.Equal(3, second.TileCount);
        Assert.Equal((short)100, second.Tiles[0].X);
        Assert.Equal((ushort)7, second.Tiles[2].Type);

        first.ReturnPacket();
        second.ReturnPacket();
    }

    [Fact]
    public void TwoNewTicksParsedBeforeEitherIsApplied_KeepTheirOwnObjects() {
        var first = NewTick.CreatePacket();
        var second = NewTick.CreatePacket();
        var firstBytes = NewTickWith((1, 1f, 1f), (2, 2f, 2f), (3, 3f, 3f), (4, 4f, 4f), (5, 5f, 5f));
        var secondBytes = NewTickWith((100, 9f, 9f), (101, 9f, 9f));
        var firstReader = new SpanReader(firstBytes);
        first.Read(ref firstReader);
        var secondReader = new SpanReader(secondBytes);
        second.Read(ref secondReader);

        Assert.Equal(5, first.ObjectStatsCount);
        for (var i = 0; i < 5; i++) {
            Assert.Equal(i + 1, first.ObjectStats[i].Id);
        }

        Assert.Equal(2, second.ObjectStatsCount);
        Assert.Equal(100, second.ObjectStats[0].Id);

        first.ReturnPacket();
        second.ReturnPacket();
    }

    private const int HpStat = (int)WaWClient.Networking.Enums.StatsType.Hp;

    private static byte[] UpdateWithOneObject(int id, int hp) {
        var buffer = new byte[2 + 2 + (2 + 4 + 8 + 1 + 5) + 2];
        var writer = new SpanWriter(buffer);
        writer.Write((short)0);            // no tiles
        writer.Write((short)1);            // one new object
        writer.Write((ushort)0x0100);
        writer.Write(id);
        writer.Write(new Position(3f, 4f));
        writer.Write((byte)1);             // one stat
        writer.Write((byte)HpStat);
        writer.Write(hp);
        writer.Write((short)0);            // no drops
        return buffer;
    }

    [Fact]
    public void ObjectStatsOfTwoUpdatesDoNotOverwriteEachOther() {
        var first = Update.CreatePacket();
        var second = Update.CreatePacket();
        var firstBytes = UpdateWithOneObject(7, 111);
        var secondBytes = UpdateWithOneObject(8, 222);
        var firstReader = new SpanReader(firstBytes);
        first.Read(ref firstReader);
        var secondReader = new SpanReader(secondBytes);
        second.Read(ref secondReader);

        var a = first.NewObjs[0];
        var b = second.NewObjs[0];
        Assert.Equal(7, a.Id);
        Assert.Equal(111, a.Pool.Data[a.StatOffset].Value);
        Assert.Equal(8, b.Id);
        Assert.Equal(222, b.Pool.Data[b.StatOffset].Value);

        first.ReturnPacket();
        second.ReturnPacket();
    }

    [Fact]
    public void AReusedPacketStartsFromScratch() {
        var packet = Update.CreatePacket();
        var bytes = UpdateWith(Enumerable.Range(0, 30).Select(i => ((short)i, (short)1, (ushort)3)).ToArray());
        var reader = new SpanReader(bytes);
        packet.Read(ref reader);
        Assert.Equal(30, packet.TileCount);
        packet.ReturnPacket();

        var again = Update.CreatePacket();   // may be the same pooled instance
        var smallBytes = UpdateWith(((short)9, (short)9, (ushort)4));
        var smallReader = new SpanReader(smallBytes);
        again.Read(ref smallReader);

        Assert.Equal(1, again.TileCount);
        Assert.Equal((short)9, again.Tiles[0].X);
        again.ReturnPacket();
    }
}
