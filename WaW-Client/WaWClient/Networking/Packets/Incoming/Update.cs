using System.Collections.Concurrent;
using WaWClient.Assets.Libraries;
using WaWClient.Game;
using WaWClient.Game.Objects;
using WaWClient.Networking.Packets.Outgoing;
using WaWClient.Networking.Structs.DataObjects;
using WaWClient.Utils;
using WaW.Common;
using Microsoft.Extensions.Logging;

namespace WaWClient.Networking.Packets.Incoming;

public class Update : IncomingPacket<Update> {
    
    public override PacketId PacketId => PacketId.Update;
    
    // Owned by THIS packet, never shared: it is parsed on the network thread and applied on the main thread a little later, so several Updates can be
    // waiting at once. These used to be static, and a newer Update overwrote an older one's tiles before they were applied - short runs of tiles
    // (a line, often diagonal) never reached the map and were drawn black. (Pooled packets keep their arrays, so this does not allocate per packet.)
    private TileDef[]  _tilesBuffer   = new TileDef[256];
    private ObjectDef[] _newObjsBuffer = new ObjectDef[256];
    private int[]       _dropsBuffer   = new int[256];
    private readonly StatPool _stats = new();

    public int TileCount;
    public int NewObjCount;
    public int DropCount;

    public TileDef[]  Tiles   => _tilesBuffer;
    public ObjectDef[] NewObjs => _newObjsBuffer;
    public int[]       Drops   => _dropsBuffer;

    public override void Reset() {
        TileCount = NewObjCount = DropCount = 0;
    }

    public override void Read(ref SpanReader reader) {
        _stats.Reset();

        TileCount = reader.ReadInt16();
        EnsureCapacity(ref _tilesBuffer, TileCount);
        for (int i = 0; i < TileCount; i++)
            _tilesBuffer[i].Read(ref reader);

        NewObjCount = reader.ReadInt16();
        EnsureCapacity(ref _newObjsBuffer, NewObjCount);
        for (int i = 0; i < NewObjCount; i++) {
            _newObjsBuffer[i].Pool = _stats;
            _newObjsBuffer[i].Read(ref reader);
        }

        DropCount = reader.ReadInt16();
        EnsureCapacity(ref _dropsBuffer, DropCount);
        for (int i = 0; i < DropCount; i++) {
            _dropsBuffer[i] = reader.ReadInt32();
            _ = reader.ReadBoolean();
        }
    }
    
    private static void EnsureCapacity<T>(ref T[] array, int needed) {
        if (array.Length < needed)
            array = new T[needed * 2]; // double to avoid frequent resizes
    }

    public override void Handle() {
        Client.QueuePacket(UpdateAck.CreatePacket());


        for (int i = 0; i < TileCount; i++)
            Map.SetTileData(Tiles[i].X, Tiles[i].Y, Tiles[i].Type);

        for (int i = 0; i < NewObjCount; i++) {
            var newObj = NewObjs[i];
            Entity entity;

            if (!ObjectLibrary.TypeToObjectProps.TryGetValue(newObj.ObjectType, out var props)) {
                Client.Logger.Log(LogLevel.Warning, $"[Update] Unable to parse props for id: {newObj.ObjectType:x4}");
                props = ObjectLibrary.TypeToObjectProps[0x600]; //Pirate Placeholder
            }

            if (props.IsPlayer) {
                entity = new Player();
            }
            // else if (props.IsEnemy) {
            //    entity = new Enemy();
            // }
            // else if (props.IsAlly) {
            //     entity = new Ally();
            // }
            else {
                entity = new Entity();
            }

            entity.Properties = props;

            entity.SetObjectId(newObj.Id);
            entity.SetType(newObj.ObjectType);
            Map.AddEntity(entity, newObj.Position);

            entity.SetPos(newObj.Position.X, newObj.Position.Y);

            entity.UpdateStats(newObj.Pool.Data, newObj.StatOffset, newObj.StatCount);
            entity.OnTickPosition(newObj.Position.X, newObj.Position.Y, 0, 0, props.IsPlayer);


            if (newObj.Id == Map.LocalPlayerId) {
                Map.OnLocalPlayerCreated(entity);
            }
        }

        for (int i = 0; i < DropCount; i++)
            Map.RemoveEntity(Drops[i]);
    }

    public override string ToString() {
        return $"Tiles: {Tiles}, NewObjs: {NewObjs}, Drops: {Drops}";
    }
}