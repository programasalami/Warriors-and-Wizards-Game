using System.Numerics;

namespace GameServer.Game.Systems.Combat;

// The client tells the server "my bullet hit that target" (EnemyHit / PlayerHit) so a hit still lands when the two sides disagree by a little lag. The server
// therefore cannot demand an exact match, but it must not believe a hit from nowhere either: a hacked client could otherwise damage anything in the world by
// sending hit packets. A hit is believed when, at some moment within the last LagWindowMs, the projectile was within (HitRadius + SlackRadius) of the target's
// position as the server knows it. The slack covers the target having moved since (a fast enemy over ~0.4 s) and the small position lag of the target itself.
public static class HitValidation {
    // How close a bullet has to be to count as a hit. Both sides use this same number (the client: EntityUtils, the server: Projectile.HIT_DIST_SQR).
    public const float HitRadius = 0.5f;

    // How far back the client's view of the world may be behind ours when it reports a hit, and how far the target may have moved meanwhile.
    public const int LagWindowMs = 400;
    public const float SlackRadius = 1.5f;

    private const int SampleStepMs = 50;

    // positionAt: where the projectile is (world position) at the given number of ms after it was fired.
    public static bool IsPlausible(Func<int, Vector2> positionAt, long elapsedNowMs, int lifetimeMs, Vector2 target) {
        var latest = Math.Min(elapsedNowMs, lifetimeMs);
        var earliest = Math.Max(0, elapsedNowMs - LagWindowMs);
        if (latest < earliest)
            return false;       // not fired yet, or already gone for longer than the lag window

        const float accept = HitRadius + SlackRadius;
        const float acceptSqr = accept * accept;
        for (var t = earliest; t < latest; t += SampleStepMs) {
            if (Vector2.DistanceSquared(positionAt((int)t), target) <= acceptSqr)
                return true;
        }

        return Vector2.DistanceSquared(positionAt((int)latest), target) <= acceptSqr;
    }
}
