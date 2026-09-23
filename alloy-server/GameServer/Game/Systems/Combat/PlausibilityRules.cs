using System;

namespace GameServer.Game.Systems.Combat;

// Server-side sanity checks on what the client claims (2026-09-21 audit, C7 / F25). Both are LOG-ONLY for now: a violation is
// counted and logged, nothing is corrected or refused, so honest laggy players are never hurt while the numbers are collected.
// Enforcement (a Goto back to the last good position, dropping the shot) is a later, separate decision.
public static class MovementRules {
    // The client's own movement formula (Player.GetMoveSpeed): MinMoveSpeed + speed/75 * (Max - Min), tiles per millisecond, then
    // x1.5 for Speedy. Slide tiles and pushes exist too, so the allowance is generous: fastest possible speed, plus a flat tile of
    // slack for tick jitter and the first move after a spawn / teleport.
    public const float MaxMoveSpeedTilesPerMs = 0.0096f;
    public const float SpeedyMultiplier = 1.5f;
    public const float Slack = 1.5f;
    public const float FlatSlackTiles = 1.0f;
    public const long MaxElapsedMs = 2000;   // a very old "last move" tells nothing: cap the allowance instead of letting it grow forever

    public static float MaxDistance(long elapsedMs) {
        var ms = Math.Clamp(elapsedMs, 0, MaxElapsedMs);
        return ms * MaxMoveSpeedTilesPerMs * SpeedyMultiplier * Slack + FlatSlackTiles;
    }

    public static bool IsPlausible(float distanceTiles, long elapsedMs) => distanceTiles <= MaxDistance(elapsedMs);
}

// A leaky bucket of shots. Every PlayerShoot packet costs one; the bucket refills at the weapon's real rate (the client sends one
// packet per projectile of an attack, so a full attack costs `projectilesPerAttack`). A negative bucket means the client is firing
// faster than its dexterity and weapon allow. Starts full and can hold two attacks, so a burst after a pause is fine.
public struct FireRateBucket {
    public double Shots;
    public long LastMs;
    public bool Started;

    // periodMs: how long one attack takes at this player's dexterity and weapon (the client's AttackPeriod).
    // Returns false when the shot is faster than plausible (the shot is still counted so the debt accumulates).
    public bool TryShoot(long nowMs, double periodMs, int projectilesPerAttack) {
        if (periodMs <= 0) periodMs = 1;
        if (projectilesPerAttack < 1) projectilesPerAttack = 1;
        var capacity = projectilesPerAttack * 2.0;

        if (!Started) {
            Started = true;
            Shots = capacity;
            LastMs = nowMs;
        }
        else {
            var elapsed = Math.Max(0, nowMs - LastMs);
            Shots = Math.Min(capacity, Shots + elapsed / periodMs * projectilesPerAttack * Slack);
            LastMs = nowMs;
        }

        Shots -= 1;
        return Shots >= -0.5;   // half a shot of rounding slack
    }

    // The client fires 30% faster than the formula says on a lucky frame boundary; allow it.
    public const double Slack = 1.3;

    // The client's attack period (Player.Shoot): 1 / attackFrequency / rateOfFire, with attackFrequency from dexterity.
    public static double AttackPeriodMs(int dexterity, float rateOfFire, bool berserk = false) {
        const float minAttackFreq = 0.0015f, maxAttackFreq = 0.008f;
        var freq = minAttackFreq + Math.Clamp(dexterity, 0, 200) / 75f * (maxAttackFreq - minAttackFreq);
        if (berserk) freq *= 1.25f;
        if (rateOfFire <= 0) rateOfFire = 1;
        return 1 / freq * (1 / rateOfFire);
    }
}
