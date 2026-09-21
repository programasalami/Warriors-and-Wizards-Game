using System.Numerics;
using GameServer.Game.Systems.Combat;

namespace GameServer.Tests.Combat;

public class HitValidationTests {
    // A bullet flying along +X at 8 tiles per second from the origin, lifetime 1 s.
    private static Vector2 Bullet(int ms) => new(ms * 8f / 1000f, 0f);

    private const int Lifetime = 1000;

    [Fact]
    public void ClientAndServerAgreeOnTheHitRadius() {
        Assert.Equal(0.5f, HitValidation.HitRadius);
        Assert.Equal(0.25f, GameServer.Game.Systems.Projectiles.Projectile.HIT_DIST_SQR);
    }

    [Fact]
    public void TargetOnTheBulletsPath_IsBelieved() {
        // 250 ms after firing the bullet is 2 tiles out; the target stands right there.
        Assert.True(HitValidation.IsPlausible(Bullet, 250, Lifetime, new Vector2(2f, 0.2f)));
    }

    [Fact]
    public void TargetTheBulletPassedAFewTenthsOfASecondAgo_IsBelieved_BecauseOfLag() {
        // Now is 500 ms (bullet at 4 tiles), the hit was really at ~250 ms when the bullet was at 2 tiles.
        Assert.True(HitValidation.IsPlausible(Bullet, 500, Lifetime, new Vector2(2f, 0f)));
    }

    [Fact]
    public void TargetThatMovedABitSinceTheHit_IsBelieved() {
        Assert.True(HitValidation.IsPlausible(Bullet, 250, Lifetime, new Vector2(2f, 1.2f)));
    }

    [Fact]
    public void TargetAcrossTheMap_IsRejected() {
        Assert.False(HitValidation.IsPlausible(Bullet, 250, Lifetime, new Vector2(60f, 40f)));
    }

    [Fact]
    public void TargetBehindTheShooter_IsRejected() {
        Assert.False(HitValidation.IsPlausible(Bullet, 500, Lifetime, new Vector2(-5f, 0f)));
    }

    [Fact]
    public void TargetTheBulletPassedLongAgo_IsRejected() {
        // Now is 900 ms (bullet at 7.2 tiles). A target at 1 tile was passed ~800 ms ago, well outside the lag window.
        Assert.False(HitValidation.IsPlausible(Bullet, 900, Lifetime, new Vector2(1f, 0f)));
    }

    [Fact]
    public void TargetAheadOfTheBulletBeyondTheSlack_IsRejected() {
        // 250 ms: the bullet is at 2 tiles, the target is 6 tiles down the line: a bullet cannot have hit it yet.
        Assert.False(HitValidation.IsPlausible(Bullet, 250, Lifetime, new Vector2(8f, 0f)));
    }

    [Fact]
    public void BulletThatHasEndedRecently_CanStillReportItsLastHit() {
        // Lifetime over 100 ms ago, packet arrives late: the bullet's last position (8 tiles) is still counted.
        Assert.True(HitValidation.IsPlausible(Bullet, Lifetime + 100, Lifetime, new Vector2(8f, 0f)));
    }

    [Fact]
    public void BulletThatEndedLongAgo_IsRejected() {
        Assert.False(HitValidation.IsPlausible(Bullet, Lifetime + 2000, Lifetime, new Vector2(8f, 0f)));
    }

    [Fact]
    public void BulletThatIsNotFiredYet_IsRejected() {
        Assert.False(HitValidation.IsPlausible(Bullet, -50, Lifetime, new Vector2(0f, 0f)));
    }
}
