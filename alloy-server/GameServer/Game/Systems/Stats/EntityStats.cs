using System;
using System.Buffers;
using System.Numerics;
using Common;
using Common.Resources.World;
using Common.Structs;
using Common.Utilities;
using Common.Utilities.Collections;
using GameServer.Game.Worlds;
using GameServer.Utilities;
using GameServer.Game.Systems.Combat;
using GameServer.Game.Systems.Inventory;
using GameServer.Game.Entities;

namespace GameServer.Game.Systems.Stats;

public struct EntityStats : IEntityIdentifiable, IDisposable {
    public const int STAT_COUNT = (int)StatType.StatTypeCount;

    public EntityId Id { get; set; }

    public WorldPosData Pos;
    public WorldPosData PrevPos;
    public WorldPosData SpawnPos;
    public MapTileData Tile;
    public BitMask256 Flags;

    public readonly StatValue[] Stats;
    public readonly StatData[] StatUpdates;
    public BitMask256 PublicMask;
    public BitMask256 PrivateMask;
    public int StatUpdateCount;
    public bool PositionUpdate;

    public ConditionEffectSet ConditionEffects;

    private readonly World _world;
    private readonly EntityType _type;
    private BitMask256 _statUpdatesMask;
    private bool _spawnSet = false;
    private float _hpRegenCarry;        // fractions of a point between ticks (Regenerate)
    private float _mpRegenCarry;

    public EntityStats(World world, ref Entity en) {
        Id = en.Id;
        _world = world;
        _type = en.Type;

        Stats = ArrayPool<StatValue>.Shared.Rent(STAT_COUNT);
        Stats.AsSpan(0, STAT_COUNT).Clear();

        StatUpdates = ArrayPool<StatData>.Shared.Rent(STAT_COUNT);
        StatUpdates.AsSpan(0, STAT_COUNT).Clear();

        ConditionEffects = new ConditionEffectSet();

        Set(StatType.Name, en.Desc.ObjectId);
        Set(StatType.HP, en.Desc.MaxHP);
        Set(StatType.MaxHP, en.Desc.MaxHP);
    }

    public bool HasConditionEffect(ConditionEffectIndex effect) {
        return ConditionEffects.Has(effect);
    }

    /// <param name="durationSeconds">Negative means the effect never expires on its own.</param>
    public void ApplyConditionEffect(ConditionEffectIndex effect, float durationSeconds) {
        ConditionEffects.Apply(effect, durationSeconds);
    }

    public void RemoveConditionEffect(ConditionEffectIndex effect) {
        ConditionEffects.Remove(effect);
    }

    public float GetSpeed(float speed) {
        if (_type == EntityType.Player) {
            if (HasConditionEffect(ConditionEffectIndex.Slowed))
                return 1;

            if (HasConditionEffect(ConditionEffectIndex.Speedy))
                speed *= 1.5f;

            var tileSpeedMult = Tile.Desc.Speed; // Sink level is not supported so just use the tile speed
            return speed * tileSpeedMult;
        }

        if (_type == EntityType.Character) {
            if (HasConditionEffect(ConditionEffectIndex.Slowed))
                return 1;

            if (HasConditionEffect(ConditionEffectIndex.Speedy))
                speed *= 1.5f;
            return speed;
        }

        return speed;
    }
    
    public void MoveTowards(ref RealmTime time, ref WorldPosData moveTo, float tilesPerSecond) {
        var angle = this.GetAngleBetween(moveTo);
        var dist = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var speed = GetSpeed(tilesPerSecond) * (time.ElapsedMsDelta / 1000f);
        dist *= speed;

        if (moveTo.DistSqr(Pos) < dist.LengthSquared()) {
            // If the distance we're about to move is greater than the distance to the desired position, set position to the desired position
            Move(moveTo.X, moveTo.Y);
            return;
        }

        Move(Pos + dist);
    }
    
    public void Move(in Vector2 vec) {
        Move(vec.X, vec.Y);
    }
    
    public void Move(float newX, float newY) {
        Pos = new WorldPosData(newX, newY);
        PositionUpdate = true;
        if (!_spawnSet) {
            _spawnSet = true;
            SpawnPos = Pos;
            Tile = _world.Map[(int)newX, (int)newY];
        }
    }

    public int GetInt(StatType s) {
        return Stats[(int)s].IntVal;
    }

    public float GetFloat(StatType s) {
        return Stats[(int)s].FloatVal;
    }

    public string GetString(StatType s) {
        return Stats[(int)s].StrVal;
    }

    public void Set(StatType statType, int value, bool isPrivate = false) {
        SetInternal(statType, StatValue.FromInt(value), isPrivate);
    }

    public void Set(StatType statType, float value, bool isPrivate = false) {
        SetInternal(statType, StatValue.FromFloat(value), isPrivate);
    }

    public void Set(StatType statType, string value, bool isPrivate = false) {
        SetInternal(statType, StatValue.FromString(value), isPrivate);
    }
    
    private void SetInternal(StatType statType, StatValue sv, bool isPrivate) {
        var id = (int)statType;
        if (sv == Stats[id])
            return;

        Stats[id] = sv;
        _statUpdatesMask.Set(id);

        if (!isPrivate)
            PublicMask.Set(id);
        PrivateMask.Set(id);
    }

    public void Tick(ref RealmTime time) {
        PrevPos = Pos;
        Tile = _world.Map[(int)Pos.X, (int)Pos.Y];

        StatUpdateCount = 0;
        if (!_statUpdatesMask.IsEmpty)
            for (var i = 0; i < STAT_COUNT; i++) {
                if (_statUpdatesMask.IsSet(i))
                    StatUpdates[StatUpdateCount++] = new StatData((StatType)i, Stats[i]);
            }

        _statUpdatesMask.Clear();
        PositionUpdate = false;

        ConditionEffects.Tick(time.ElapsedMsDelta);
        if (_type == EntityType.Player)
            Regenerate(time.ElapsedMsDelta);
    }

    // HP from Vitality, MP from Wisdom, every tick while alive (2026-09-22: a player used to stay at whatever HP a fight left them).
    private void Regenerate(int elapsedMs) {
        var hp = GetInt(StatType.HP);
        if (hp <= 0)
            return;
        var maxHp = GetInt(StatType.MaxHP);
        if (hp < maxHp) {
            var add = Systems.Combat.ProgressionRules.Regenerate(Systems.Combat.ProgressionRules.HpRegenPerSecond(GetInt(StatType.Vitality)), elapsedMs, ref _hpRegenCarry);
            if (add > 0) Set(StatType.HP, Math.Min(maxHp, hp + add));
        }
        var mp = GetInt(StatType.MP);
        var maxMp = GetInt(StatType.MaxMP);
        if (mp < maxMp) {
            var add = Systems.Combat.ProgressionRules.Regenerate(Systems.Combat.ProgressionRules.MpRegenPerSecond(GetInt(StatType.Wisdom)), elapsedMs, ref _mpRegenCarry);
            if (add > 0) Set(StatType.MP, Math.Min(maxMp, mp + add));
        }
    }

    public void Dispose() {
        ArrayPool<StatValue>.Shared.Return(Stats);
        ArrayPool<StatData>.Shared.Return(StatUpdates);
        ConditionEffects.Dispose();
    }
}