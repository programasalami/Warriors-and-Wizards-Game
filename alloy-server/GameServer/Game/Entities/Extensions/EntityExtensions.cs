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
using GameServer.Game.Worlds;

namespace GameServer.Game.Entities.Extensions;

public static class EntityExtensions {
    extension(Entity en) {
        public void Move(World world, float newX, float newY) {
            ref var stats = ref world.EntityStats.Get(en.Id);
            stats.Move(newX, newY);
        }

        public void Init(World world, WorldPosData spawnPos) {
            ref var stats = ref world.EntityStats.Get(en.Id);
            stats.Move(spawnPos.X, spawnPos.Y);
            
            if (en.Desc.Static) {
                var tile = world.Map[(int)spawnPos.X, (int)spawnPos.Y];
                if (tile.ObjectId == EntityId.Null)
                    tile.ObjectId = en.Id;
            }
        }
    }
}