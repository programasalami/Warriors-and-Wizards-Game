using Common;
using Common.Structs;
using GameServer.Game.Network.Messaging;
using GameServer.Game.Session;
using GameServer.Game.Systems.Chat;
using GameServer.Game.Systems.Combat;
using GameServer.Game.Systems.Inventory;
using GameServer.Game.Systems.Projectiles;
using GameServer.Game.Entities;

namespace GameServer.Game.Systems.Behaviors.Actions;

public record Flash : BehaviorScript {
    private readonly int _color;
    private readonly float _flashPeriod;
    private readonly int _flashRepeats;

    public Flash(int color, double flashPeriod, int flashRepeats) {
        _color = color;
        _flashPeriod = (float)flashPeriod;
        _flashRepeats = flashRepeats;
    }

    public override void Start(ref EntityView host) {
        var id = host.Id;
        host.World.Map.BroadcastNearby(host.Stats.Pos, 20f, user => {
            user.SendPacket(new
                ShowEffect(
                    (byte)ShowEffectIndex.Flash,
                    id,
                    _color,
                    0,
                    new WorldPosData(_flashPeriod, _flashRepeats),
                    new WorldPosData()
                ));
        });
    }
}