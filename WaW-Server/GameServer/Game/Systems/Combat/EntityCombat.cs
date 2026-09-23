using System;
using System.Collections.Immutable;
using Common;
using Common.Resources.World;
using Common.Structs;
using Common.Utilities;
using Common.Utilities.Collections;
using GameServer.Game.Systems.Combat;
using GameServer.Game.Systems.Events;
using GameServer.Game.Systems.Stats;
using GameServer.Game.Network;
using GameServer.Game.Network.Messaging;
using GameServer.Game.Session;
using GameServer.Game.Systems.Chat;
using GameServer.Game.Systems.Inventory;
using GameServer.Game.Systems.Projectiles;
using GameServer.Game.Worlds;
using GameServer.Game.Entities;

namespace GameServer.Game.Systems.Combat;

public struct EntityCombat : IEntityIdentifiable, IDisposable {
    public EntityId Id { get; set; }

    public int TotalDamageReceived;
    public readonly SparseSet<DamageRecord> DamageRecords;

    private readonly World _world;

    public EntityCombat(World world, ref Entity en) {
        Id = en.Id;
        _world = world;
        DamageRecords = new SparseSet<DamageRecord>(world.Entities.Count);
    }

    public int GetProjectileDamage(int minDamage, int maxDamage) {
        // TODO: Condition effects
        var dmg = Random.Shared.Next(minDamage, maxDamage);
        return dmg;
    }

    public void Damage(EntityId fromId, int damage, int fromAccId) { // Applies damage directly, perform any modifications to the amount before calling this
        TotalDamageReceived += damage;

        ref var record = ref DamageRecords.GetOrAdd(fromId, out var added);
        if (added) {
            record = new DamageRecord(fromId, damage, fromAccId);
        } else {
            record.DamageDealt += damage;
        }
    }

    public void DamageWithText(EntityId fromId, int damage, int fromAccId) {
        Damage(fromId, damage, fromAccId);
        var user = _world.Users[Id];
        user.SendPacket(new Notification(Id, "-" + damage, 0xFF0000, 24));
    }

    public void Tick(ref RealmTime time) {
        ref var stats = ref _world.EntityStats.Get(Id);
        if (stats.Id == EntityId.Null)
            return;

        if (TotalDamageReceived > 0 && _world.Users.TryGetValue(Id, out var godUser) && godUser.GameInfo.God) {
            TotalDamageReceived = 0;       // /god: the hits land (texts, records) but never lower the health
            return;
        }

        var hp = stats.GetInt(StatType.HP);
        var newHp = hp - TotalDamageReceived;
        stats.Set(StatType.HP, newHp);

        if (newHp <= 0)
            Death(ref stats);

        TotalDamageReceived = 0;
    }

    private void Death(ref EntityStats stats) {
        ref var en = ref _world.Entities.Get(Id);
        if (en.Type == EntityType.Player) {
            // TODO: Spawn gravestone, announce death, register death in database
            _world.Users[Id].Disconnect(reason: DisconnectReason.Death);
            return;
        }
        Progression.AwardKill(_world, ref en, ref this);        // XP / levels / fame for every player in the damage records (2026-09-22)
        if (en.Desc.Enemy && _world.TryGetMapOrigin(Id, out var origin))
            _world.AddTimedAction(ProgressionRules.MapEnemyRespawnMs, w => w.SpawnFromMap(origin));      // the Nexus targets come back
        _world.LeaveWorld(Id);
    }

    public void Dispose() {
    }
}