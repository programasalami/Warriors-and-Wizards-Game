using System;
using System.Collections.Generic;

namespace WaWClient.Networking.Structs.DataObjects;

public struct ObjectStats : IDataObject {
    public int Id;
    public Position Position;
    public int StatOffset;
    public int StatCount;

    // The stat list of the packet this entry is read from (set by that packet before Read; never shared between packets).
    public StatPool Pool;

    public void Reset() {
        Id = 0;
        Position.Reset();
        StatOffset = 0;
        StatCount = 0;
    }

    public void Read(ref SpanReader reader) {
        Id = reader.ReadInt32();
        Position.Read(ref reader);

        var len = reader.ReadByte();
        StatOffset = Pool.Count;
        StatCount = len;

        Pool.EnsureRoomFor(len);
        for (int i = 0; i < len; i++)
            Pool.Data[Pool.Count++].Read(ref reader);
    }

    public void Write(ref SpanWriter writer) {
        writer.Write(Id);
        Position.Write(ref writer);

        writer.Write((byte)StatCount);

        for (var i = 0; i < StatCount; i++) {
            Pool.Data[StatOffset + i].Write(ref writer);
        }
    }

    public override string ToString() {
        return $"Id: {Id}, Position: {Position}";
    }
}