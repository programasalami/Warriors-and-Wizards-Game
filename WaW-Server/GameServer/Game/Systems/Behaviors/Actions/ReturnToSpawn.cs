using System.Xml.Linq;
using Common.Structs;
using Common.Utilities;
using GameServer.Game.Entities;
using GameServer.Game.Systems.Behaviors;
using GameServer.Utilities;
using GameServer.Game.Systems.Combat;
using GameServer.Game.Systems.Inventory;

namespace GameServer.Game.Systems.Behaviors.Actions;

public record ReturnToSpawn : BehaviorScript {
    private readonly float _distanceFromSpawn;
    private readonly float _speed;

    public ReturnToSpawn(XElement xml) {
        _speed = xml.GetAttribute("speed", 1f);
        _distanceFromSpawn = xml.GetAttribute("distanceFromSpawn", 1f);
    }

    public ReturnToSpawn(float speed = 1f, float distanceFromSpawn = 0f) {
        _speed = speed;
        _distanceFromSpawn = distanceFromSpawn;
    }

    public override BehaviorTickState Tick(ref EntityView host, ref RealmTime time) {
        var distToSpawn = PositionUtils.GetDistanceBetween(host.Stats.Pos, host.Stats.SpawnPos);
        if (distToSpawn <= _distanceFromSpawn + _speed / 50)
            return BehaviorTickState.BehaviorDeactivate;

        host.Stats.MoveTowards(ref time, ref host.Stats.SpawnPos, _speed);
        return BehaviorTickState.BehaviorActive;
    }
}