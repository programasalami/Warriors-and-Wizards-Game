using System;
using System.Collections.Generic;

namespace AlloyClient.Networking.Structs.DataObjects;

public struct ObjectDef : IDataObject {
    public ushort ObjectType;
    public int Id;
    public Position Position;
    public int StatOffset; // where in Pool this entity's stats begin
    public int StatCount;

    // The stat list of the packet this object is read from (set by that packet before Read; never shared between packets).
    public StatPool Pool;

    public void Reset() {
        ObjectType = 0;
        Id = 0;
        Position.Reset();
        StatOffset = 0;
        StatCount = 0;
    }

    public void Read(ref SpanReader reader) {
        ObjectType = reader.ReadUInt16();
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
        writer.Write(ObjectType);
        writer.Write(Id);
        Position.Write(ref writer);

        writer.Write((short)StatCount);

        for (var i = 0; i < StatCount; i++) {
            Pool.Data[StatOffset + i].Write(ref writer);
        }
    }

    public override string ToString() {
        return $"ObjectType: {ObjectType}, Id: {Id}, Position: {Position}";
    }
}