using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Common;
using Common.Resources.World;
using Common.Structs;
using Common.Utilities.Collections;
using GameServer.Game.Systems.Behaviors;
using GameServer.Game.Systems.Chat;
using GameServer.Game.Systems.Combat;
using GameServer.Game.Systems.Events;
using GameServer.Game.Systems.Inventory;
using GameServer.Game.Systems.Portals;
using GameServer.Game.Systems.Projectiles;
using GameServer.Game.Systems.Sight;
using GameServer.Game.Systems.Stats;
using GameServer.Game.Network;
using GameServer.Game.Network.Messaging;
using GameServer.Game.Session;
using GameServer.Game.Worlds;
using GameServer.Utilities;
using GameServer.Game.Entities;

namespace GameServer.Game.Systems.Portals;

public class PortalDatasManager(World world, int capacity) : ManagerBase<PortalData>(world, capacity) {

    private long _nextTick; // Tick portals every second
    
    public override void Tick(ref RealmTime time) {
        if (time.TotalElapsedMs < _nextTick)
            return;
        
        foreach (ref var portal in this) {
            portal.Tick(ref time);
        }

        _nextTick = time.TotalElapsedMs + 1000;
    }
}