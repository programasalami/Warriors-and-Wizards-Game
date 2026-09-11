using System;
using System.Collections.Generic;
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
using GameServer.Game.Worlds;
using GameServer.Game.Entities;

namespace GameServer.Game.Systems.Stats;

public class EntityStatsManager(World world, int capacity) : ManagerBase<EntityStats>(world, capacity) {
    
    public override void Tick(ref RealmTime time) {
        foreach (ref var stats in this){
            stats.Tick();
        }
    }
}