using Common.Structs;

namespace Common.Network;

public static class WorldPosDataIO {
    public static WorldPosData Read(ref SpanReader rdr) {
        return new WorldPosData(rdr.ReadSingle(), rdr.ReadSingle());
    }
}
