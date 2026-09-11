using Common;
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
using GameServer.Game.Worlds;

namespace GameServer.Game.Systems.Combat;

public static class CombatUtils {
    extension(World world) {
        public EntityId GetAttackTarget(WorldPosData pos, float radiusSqr, BehaviorScript.TargetType targetType) {
            switch (targetType) {
                case BehaviorScript.TargetType.ClosestPlayer:
                    return world.Map.GetNearestPlayer(pos, radiusSqr);
                case BehaviorScript.TargetType.RandomPlayerPerBehavior:
                case BehaviorScript.TargetType.RandomPlayerPerCycle:
                    return world.Map.GetPlayersWithin(pos, MathF.Sqrt(radiusSqr)).RandomElement();
                case BehaviorScript.TargetType.FarthestPlayer:
                    return world.Map.GetFarthestPlayer(pos, radiusSqr);
                default:
                    return EntityId.Null;
            }
        }
    }
}