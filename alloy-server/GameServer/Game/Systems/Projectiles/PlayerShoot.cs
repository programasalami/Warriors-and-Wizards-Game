using GameServer.Game.Network.Messaging;
using System;
using System.Numerics;
using Common;
using Common.Network;
using Common.Projectiles.ProjectilePaths;
using Common.Resources.Xml;
using Common.Resources.Xml.Descriptors;
using Common.Structs;
using Common.Utilities;
using GameServer.Game.Entities;
using GameServer.Game.Entities.Extensions;
using GameServer.Game.Systems.Chat;
using GameServer.Game.Systems.Combat;
using GameServer.Game.Network;

namespace GameServer.Game.Systems.Projectiles;

[Packet(PacketId.PlayerShoot)]
public record PlayerShoot : IIncomingPacket {
    private static readonly Logger _log = new(typeof(PlayerShoot));
    public float Angle;

    public async Task Handle(User user) {
        if (user.State != ConnectionState.Ready || user.GameInfo.State != GameState.Playing)
            return;

        var player = new EntityView(user.GameInfo.World, user.GameInfo.PlayerId);
        var weapon = player.Inventory[0];
        if (weapon == null || weapon.ObjectType == 0)
            return;

        var projDesc = weapon.Projectiles[0];
        if (projDesc == null)
            return;

        // Log-only (2026-09-21 audit, C7): is the client shooting faster than its dexterity and weapon allow? Nothing is refused yet.
        var info = user.GameInfo;
        var period = FireRateBucket.AttackPeriodMs(player.Stats.GetInt(StatType.Dexterity), weapon.RateOfFire);
        if (!info.FireRate.TryShoot(GameLogic.WorldTime.TotalElapsedMs, period, weapon.NumProjectiles)) {
            info.FireRateViolations++;
            if (info.FireRateViolations == 1 || info.FireRateViolations % 50 == 0)
                _log.Warn($"[PLAUSIBILITY] user {user.Id} ({info.Account?.Name}) fires faster than allowed (period {period:F0} ms, {weapon.NumProjectiles} per attack); {info.FireRateViolations} so far this session");
        }

        var damage = player.Combat.GetProjectileDamage(projDesc.MinDamage, projDesc.MaxDamage);
        var pos = player.Stats.Pos;
        var world = player.World;
        GameLogic.Enqueue(() => world.SpawnProjectiles(pos, user.GameInfo.PlayerId, Angle.Rad2Deg(), weapon.ArcGap, damage, weapon.NumProjectiles,
            ProjectilePathSegment.ParsePath(projDesc).ToPath(), projDesc.LifetimeMS, projDesc.MultiHit,
            ref GameLogic.WorldTime));
    }

    public void Read(ref SpanReader rdr) {
        Angle = rdr.ReadSingle();
    }
}