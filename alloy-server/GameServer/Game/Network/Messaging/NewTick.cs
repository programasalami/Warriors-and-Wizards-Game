using System.Collections.Generic;
using Common.Network;
using Common.Structs;
using Common.Utilities;
using Common.Utilities.Collections;
using GameServer.Game.Entities;
using GameServer.Game.Systems.Behaviors;
using GameServer.Game.Systems.Chat;
using GameServer.Game.Systems.Combat;
using GameServer.Game.Systems.Events;
using GameServer.Game.Systems.Inventory;
using GameServer.Game.Systems.Portals;
using GameServer.Game.Systems.Projectiles;
using GameServer.Game.Systems.Sight;
using GameServer.Game.Systems.Stats;
using GameServer.Game.Network.Messaging;

namespace GameServer.Game.Network.Messaging;

public readonly struct NewTick : IOutgoingPacket {
    public PacketId ID => PacketId.NewTick;

    private readonly PooledList<ObjectStatusData> _statuses;

    public NewTick(PooledList<ObjectStatusData> statuses) {
        _statuses = statuses;
    }
    
    public void Write(ref SpanWriter wtr) {
        var span = _statuses.AsSpan();
        wtr.Write((short)span.Length);
        for (int i = 0; i < span.Length; i++)
        {
            ref readonly var status = ref span[i];
            status.WriteForNewTick(ref wtr);
        }
    }
}