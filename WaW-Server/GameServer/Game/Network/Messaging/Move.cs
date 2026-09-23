#region

using Common.Network;
using Common.Structs;
using GameServer.Game.Entities.Extensions;
using GameServer.Game.Systems.Chat;
using GameServer.Game.Systems.Combat;

#endregion

namespace GameServer.Game.Network.Messaging;

[Packet(PacketId.Move)]
public record Move : IIncomingPacket {
    private static readonly Common.Utilities.Logger _log = new(typeof(Move));
    public WorldPosData Pos;

    public async Task Handle(User user) {
        if (user.GameInfo.State != GameState.Playing)
            return;

        ref var player = ref user.GameInfo.Player;

        // Log-only (2026-09-21 audit, C7): how far did the client claim to move since its last Move packet? Nothing is corrected yet.
        var info = user.GameInfo;
        var now = GameLogic.WorldTime.TotalElapsedMs;
        if (info.LastMoveAtMs > 0) {
            ref var stats = ref user.GameInfo.World.EntityStats.Get(info.PlayerId);
            if (stats.Id != Common.Utilities.Collections.EntityId.Null) {
                var dx = Pos.X - stats.Pos.X;
                var dy = Pos.Y - stats.Pos.Y;
                var distance = MathF.Sqrt(dx * dx + dy * dy);
                if (!MovementRules.IsPlausible(distance, now - info.LastMoveAtMs)) {
                    info.MoveViolations++;
                    if (info.MoveViolations == 1 || info.MoveViolations % 50 == 0)
                        _log.Warn($"[PLAUSIBILITY] user {user.Id} ({info.Account?.Name}) moved {distance:F2} tiles in {now - info.LastMoveAtMs} ms (limit {MovementRules.MaxDistance(now - info.LastMoveAtMs):F2}); {info.MoveViolations} so far this session");
                }
            }
        }
        info.LastMoveAtMs = now;

        player.Move(user.GameInfo.World, Pos.X, Pos.Y);
    }

    public void Read(ref SpanReader rdr) {
        Pos = WorldPosDataIO.Read(ref rdr);
    }
}