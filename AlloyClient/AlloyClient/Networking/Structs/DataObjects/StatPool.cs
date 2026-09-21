using System;

namespace AlloyClient.Networking.Structs.DataObjects;

// The stat entries read for ONE packet's objects. Every Update / NewTick owns its own pool (packets are parsed on the network thread and applied on the main
// thread later, so several can be waiting at once; a pool shared between them let a newer packet overwrite an older one's stats before they were applied).
public sealed class StatPool {
    public StatData[] Data = new StatData[256];
    public int Count;

    public void Reset() => Count = 0;

    public void EnsureRoomFor(int extra) {
        if (Count + extra > Data.Length) {
            Array.Resize(ref Data, (Count + extra) * 2);
        }
    }
}
