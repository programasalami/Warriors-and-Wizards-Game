using Common;
using Common.Structs;

namespace Common.Tests.Structs;

public class ConditionEffectSetTests {
    public class HasAndApply {
        [Fact]
        public void Has_IsFalse_ForAnyEffect_Initially() {
            using var effects = new ConditionEffectSet();

            Assert.False(effects.Has(ConditionEffectIndex.Slowed));
            Assert.False(effects.Has(ConditionEffectIndex.Paralyzed));
        }

        [Fact]
        public void Apply_MakesHasTrue() {
            using var effects = new ConditionEffectSet();

            effects.Apply(ConditionEffectIndex.Slowed, 5f);

            Assert.True(effects.Has(ConditionEffectIndex.Slowed));
        }

        [Fact]
        public void Apply_OnlyAffectsTheGivenEffect() {
            using var effects = new ConditionEffectSet();

            effects.Apply(ConditionEffectIndex.Slowed, 5f);

            Assert.False(effects.Has(ConditionEffectIndex.Speedy));
            Assert.False(effects.Has(ConditionEffectIndex.Paralyzed));
        }

        [Fact]
        public void Apply_MultipleEffects_AreAllIndependentlyActive() {
            using var effects = new ConditionEffectSet();

            effects.Apply(ConditionEffectIndex.Slowed, 5f);
            effects.Apply(ConditionEffectIndex.Paralyzed, 2f);
            effects.Apply(ConditionEffectIndex.Invincible, -1);

            Assert.True(effects.Has(ConditionEffectIndex.Slowed));
            Assert.True(effects.Has(ConditionEffectIndex.Paralyzed));
            Assert.True(effects.Has(ConditionEffectIndex.Invincible));
            Assert.False(effects.Has(ConditionEffectIndex.Speedy));
        }

        [Fact]
        public void Apply_ConvertsSecondsToMilliseconds() {
            using var effects = new ConditionEffectSet();

            effects.Apply(ConditionEffectIndex.Slowed, 2.5f);

            Assert.Equal(2500, effects.MsRemaining(ConditionEffectIndex.Slowed));
        }

        [Fact]
        public void Apply_NegativeDuration_IsStoredAsPermanentSentinel() {
            using var effects = new ConditionEffectSet();

            effects.Apply(ConditionEffectIndex.Invincible, -1);

            Assert.Equal(-1, effects.MsRemaining(ConditionEffectIndex.Invincible));
        }

        [Fact]
        public void Apply_Reapplying_ResetsRemainingDuration() {
            using var effects = new ConditionEffectSet();
            effects.Apply(ConditionEffectIndex.Slowed, 5f);
            effects.Tick(4000);

            effects.Apply(ConditionEffectIndex.Slowed, 5f);

            Assert.Equal(5000, effects.MsRemaining(ConditionEffectIndex.Slowed));
        }
    }

    public class Remove {
        [Fact]
        public void Remove_ClearsAnActiveEffectImmediately() {
            using var effects = new ConditionEffectSet();
            effects.Apply(ConditionEffectIndex.Slowed, 100f); // long duration

            effects.Remove(ConditionEffectIndex.Slowed);

            Assert.False(effects.Has(ConditionEffectIndex.Slowed));
        }

        [Fact]
        public void Remove_ClearsAPermanentEffect() {
            using var effects = new ConditionEffectSet();
            effects.Apply(ConditionEffectIndex.Invincible, -1);

            effects.Remove(ConditionEffectIndex.Invincible);

            Assert.False(effects.Has(ConditionEffectIndex.Invincible));
        }

        [Fact]
        public void Remove_DoesNotAffectOtherActiveEffects() {
            using var effects = new ConditionEffectSet();
            effects.Apply(ConditionEffectIndex.Slowed, 5f);
            effects.Apply(ConditionEffectIndex.Paralyzed, 5f);

            effects.Remove(ConditionEffectIndex.Slowed);

            Assert.False(effects.Has(ConditionEffectIndex.Slowed));
            Assert.True(effects.Has(ConditionEffectIndex.Paralyzed));
        }

        [Fact]
        public void Remove_NeverAppliedEffect_IsNoOp() {
            using var effects = new ConditionEffectSet();

            effects.Remove(ConditionEffectIndex.Slowed);

            Assert.False(effects.Has(ConditionEffectIndex.Slowed));
        }
    }

    public class Ticking {
        [Fact]
        public void Tick_BeforeExpiry_EffectStaysActive() {
            using var effects = new ConditionEffectSet();
            effects.Apply(ConditionEffectIndex.Slowed, 5f); // 5000ms

            effects.Tick(4999);

            Assert.True(effects.Has(ConditionEffectIndex.Slowed));
        }

        [Fact]
        public void Tick_ExactlyAtExpiry_EffectIsRemoved() {
            using var effects = new ConditionEffectSet();
            effects.Apply(ConditionEffectIndex.Slowed, 5f); // 5000ms

            effects.Tick(5000);

            Assert.False(effects.Has(ConditionEffectIndex.Slowed));
        }

        [Fact]
        public void Tick_PastExpiry_EffectIsRemoved() {
            using var effects = new ConditionEffectSet();
            effects.Apply(ConditionEffectIndex.Slowed, 1f); // 1000ms

            effects.Tick(5000);

            Assert.False(effects.Has(ConditionEffectIndex.Slowed));
        }

        [Fact]
        public void Tick_AccumulatesAcrossMultipleCalls() {
            using var effects = new ConditionEffectSet();
            effects.Apply(ConditionEffectIndex.Slowed, 1f); // 1000ms

            effects.Tick(400);
            Assert.True(effects.Has(ConditionEffectIndex.Slowed));

            effects.Tick(400);
            Assert.True(effects.Has(ConditionEffectIndex.Slowed));

            effects.Tick(400); // total 1200ms > 1000ms
            Assert.False(effects.Has(ConditionEffectIndex.Slowed));
        }

        [Fact]
        public void Tick_PermanentEffect_NeverExpires() {
            using var effects = new ConditionEffectSet();
            effects.Apply(ConditionEffectIndex.Invincible, -1);

            effects.Tick(int.MaxValue);

            Assert.True(effects.Has(ConditionEffectIndex.Invincible));
        }

        [Fact]
        public void Tick_WithNoActiveEffects_IsNoOp() {
            using var effects = new ConditionEffectSet();

            effects.Tick(1000);

            Assert.False(effects.Has(ConditionEffectIndex.Slowed));
        }

        [Fact]
        public void Tick_ExpiresEachEffectIndependentlyByItsOwnDuration() {
            using var effects = new ConditionEffectSet();
            effects.Apply(ConditionEffectIndex.Slowed, 1f);     // expires at 1000ms
            effects.Apply(ConditionEffectIndex.Paralyzed, 3f);  // expires at 3000ms

            effects.Tick(1500);

            Assert.False(effects.Has(ConditionEffectIndex.Slowed));
            Assert.True(effects.Has(ConditionEffectIndex.Paralyzed));

            effects.Tick(1500); // total 3000ms

            Assert.False(effects.Has(ConditionEffectIndex.Paralyzed));
        }

        [Fact]
        public void Tick_PermanentAndTimedEffectsCoexist() {
            using var effects = new ConditionEffectSet();
            effects.Apply(ConditionEffectIndex.Invincible, -1);
            effects.Apply(ConditionEffectIndex.Slowed, 1f);

            effects.Tick(2000);

            Assert.True(effects.Has(ConditionEffectIndex.Invincible));
            Assert.False(effects.Has(ConditionEffectIndex.Slowed));
        }
    }
}
