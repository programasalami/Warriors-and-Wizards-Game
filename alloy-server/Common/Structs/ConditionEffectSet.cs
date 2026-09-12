using System;
using System.Buffers;
using Common.Utilities;

namespace Common.Structs;

/// <summary>
/// Tracks which <see cref="ConditionEffectIndex"/> values are active on an entity, along with
/// how long each one has left before it expires on its own (or -1 if it's permanent).
/// Deliberately has no dependency on World/Entity so it can be constructed and ticked in isolation.
/// </summary>
public struct ConditionEffectSet : IDisposable {
    public const int COUNT = (int)ConditionEffectIndex.ConditionCount;

    public BitMask256 Mask;

    private readonly int[] _msRemaining;

    public ConditionEffectSet() {
        Mask = default;
        _msRemaining = ArrayPool<int>.Shared.Rent(COUNT);
        _msRemaining.AsSpan(0, COUNT).Clear();
    }

    public readonly bool Has(ConditionEffectIndex effect) {
        return Mask.IsSet((int)effect);
    }

    /// <param name="durationSeconds">Negative means the effect never expires on its own.</param>
    public void Apply(ConditionEffectIndex effect, float durationSeconds) {
        var id = (int)effect;
        Mask.Set(id);
        _msRemaining[id] = durationSeconds < 0 ? -1 : (int)(durationSeconds * 1000f);
    }

    public void Remove(ConditionEffectIndex effect) {
        var id = (int)effect;
        Mask.Unset(id);
        _msRemaining[id] = 0;
    }

    public void Tick(int elapsedMs) {
        if (Mask.IsEmpty)
            return;

        for (var i = 0; i < COUNT; i++) {
            if (!Mask.IsSet(i))
                continue;

            if (_msRemaining[i] < 0) // permanent
                continue;

            _msRemaining[i] -= elapsedMs;
            if (_msRemaining[i] <= 0)
                Mask.Unset(i);
        }
    }

    public readonly int MsRemaining(ConditionEffectIndex effect) => _msRemaining[(int)effect];

    public void Dispose() {
        ArrayPool<int>.Shared.Return(_msRemaining);
    }
}
