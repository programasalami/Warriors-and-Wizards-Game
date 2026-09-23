using GameServer.Game.Systems.Combat;

namespace GameServer.Tests.Combat;

// The log-only sanity checks on client movement and fire rate (2026-09-21 audit, C7). They must let every honest player through,
// including a laggy one, and only flag what no client could produce.
public class PlausibilityRulesTests {

    [Theory]
    [InlineData(0.9f, 50, true)]        // one tick, a normal step (max real speed is ~0.48 tiles per 50 ms tick)
    [InlineData(1.9f, 50, true)]        // slack: fast + jitter + the flat tile
    [InlineData(3.0f, 50, false)]       // three tiles in one tick: not a walk
    [InlineData(6.0f, 500, true)]       // half a second of lag at top speed
    [InlineData(80.0f, 60_000, false)]  // a teleport across the map, however old the last move is (allowance is capped)
    public void MovementDistanceAgainstElapsedTime(float distance, long elapsedMs, bool expected) {
        Assert.Equal(expected, MovementRules.IsPlausible(distance, elapsedMs));
    }

    [Fact]
    public void MovementAllowanceIsCappedForVeryOldMoves() {
        Assert.Equal(MovementRules.MaxDistance(MovementRules.MaxElapsedMs), MovementRules.MaxDistance(MovementRules.MaxElapsedMs * 100));
    }

    [Fact]
    public void AttackPeriodMatchesTheClientsFormula() {
        // dexterity 0, rate of fire 1: 1 / 0.0015 = 666.7 ms; dexterity 75: 1 / 0.008 = 125 ms
        Assert.Equal(666.67, FireRateBucket.AttackPeriodMs(0, 1f), 1);
        Assert.Equal(125.0, FireRateBucket.AttackPeriodMs(75, 1f), 1);
        Assert.Equal(250.0, FireRateBucket.AttackPeriodMs(75, 0.5f), 1);
    }

    [Fact]
    public void ShootingAtTheWeaponsRateIsAlwaysPlausible() {
        var bucket = new FireRateBucket();
        const double period = 200;
        long now = 0;
        for (var i = 0; i < 100; i++) {
            Assert.True(bucket.TryShoot(now, period, 1));
            now += 200;
        }
    }

    [Fact]
    public void AMultiShotWeaponSendsOnePacketPerProjectilePerAttack() {
        var bucket = new FireRateBucket();
        const double period = 300;
        long now = 0;
        for (var attack = 0; attack < 50; attack++) {
            for (var p = 0; p < 3; p++)
                Assert.True(bucket.TryShoot(now, period, 3));
            now += 300;
        }
    }

    [Fact]
    public void FiringTwiceAsFastAsAllowedIsFlaggedAfterTheBurstAllowanceIsSpent() {
        var bucket = new FireRateBucket();
        const double period = 200;
        long now = 0;
        var flagged = 0;
        for (var i = 0; i < 40; i++) {
            if (!bucket.TryShoot(now, period, 1)) flagged++;
            now += 100;     // twice the allowed rate
        }
        Assert.True(flagged > 10, $"only {flagged} flagged");
    }

    [Fact]
    public void ABurstAfterAPauseIsFine() {
        var bucket = new FireRateBucket();
        Assert.True(bucket.TryShoot(0, 200, 1));
        Assert.True(bucket.TryShoot(10_000, 200, 1));   // long pause, then two quick shots (the bucket holds two attacks)
        Assert.True(bucket.TryShoot(10_010, 200, 1));
    }
}
