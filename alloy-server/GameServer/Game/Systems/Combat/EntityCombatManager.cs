using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Common;
using Common.Game;
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

namespace GameServer.Game.Systems.Combat;

public class EntityCombatManager(World world, int capacity) : ManagerBase<EntityCombat>(world, capacity) {

    public override void Tick(ref RealmTime time) {
        foreach (ref var combat in this) {
            combat.Tick(ref time);
        }
    }
}