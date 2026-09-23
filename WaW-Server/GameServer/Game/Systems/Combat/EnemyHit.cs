using Common.Network;
using GameServer.Game.Network.Messaging;
using Common.Structs;
using Common.Utilities.Collections;
using GameServer.Game;
using GameServer.Game.Entities;
using GameServer.Game.Network;

namespace GameServer.Game.Systems.Combat;

[Packet(PacketId.EnemyHit)]
public record EnemyHit : IIncomingPacket {
    public ushort ProjectileId;
    public EntityId TargetId;

    public async Task Handle(User user) {
        if (user.GameInfo.State != GameState.Playing)
            return;

        ref var targetCombat = ref user.GameInfo.World.EntityCombat.Get(TargetId);
        if (targetCombat.Id == EntityId.Null)
            return;

        var plrId = user.GameInfo.PlayerId;
        var world = user.GameInfo.World;
        GameLogic.Enqueue(() => {
            ref var enProjs = ref world.EntityProjectiles.Get(plrId);
            if (enProjs.Id == EntityId.Null)
                return;
            
            ref var proj = ref world.Projectiles.Get(enProjs.GetGlobalId(ProjectileId));
            if (proj.Id == EntityId.Null)
                return;

            if (!proj.IsHitPlausible(TargetId, GameLogic.WorldTime.TotalElapsedMs))
                return;         // a hit report for something the bullet was nowhere near (see HitValidation)

            proj.TryHitEntity(TargetId);
        });
    }

    public void Read(ref SpanReader rdr) {
        ProjectileId = rdr.ReadUInt16();
        TargetId = EntityId.Read(ref rdr);
    }
}