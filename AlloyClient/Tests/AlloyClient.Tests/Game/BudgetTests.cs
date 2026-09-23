using AlloyClient.Game;
using AlloyClient.ParticleEffects;
using AlloyClient.Ui.Character;

namespace AlloyClient.Tests.Game;

// Hard caps added by the 2026-09-21 audit: live particle effects and floating status texts are bounded, so a burst (the old
// runaway hit loop made hundreds per hit) can never grow without limit and freeze the client.
public class BudgetTests {

    private sealed class NeverEndingEffect : ParticleEffect {
        public override bool Update(double time, double dt) => true;
    }

    [Fact]
    public void ParticleEffectsAreRefusedBeyondTheBudget() {
        Map.ClearWorldObjects();
        var droppedBefore = PerfCounters.ParticleEffectsDroppedTotal;

        for (var i = 0; i < Map.MaxParticleGenerators + 50; i++)
            Map.AddParticleEffect(new NeverEndingEffect());

        Assert.Equal(Map.MaxParticleGenerators, Map.ParticleGenCount);
        Assert.Equal(droppedBefore + 50, PerfCounters.ParticleEffectsDroppedTotal);

        Map.ClearWorldObjects();
        Assert.Equal(0, Map.ParticleGenCount);
        Assert.Empty(Map.ParticleGenerators);
    }

    [Fact]
    public void ANullEffectIsIgnored() {
        Map.ClearWorldObjects();
        Map.AddParticleEffect(null);
        Assert.Equal(0, Map.ParticleGenCount);
    }

    [Theory]
    [InlineData(0, 48, 0)]
    [InlineData(48, 48, 0)]
    [InlineData(49, 48, 1)]
    [InlineData(1000, 48, 952)]
    public void StatusTextOverflowDropsOnlyWhatIsOverTheCap(int count, int max, int expectedDrop) {
        Assert.Equal(expectedDrop, NotificationLayer.OverflowToDrop(count, max));
    }

    [Fact]
    public void StatusTextCapsAreSane() {
        Assert.True(NotificationLayer.MaxLive is >= 16 and <= 200);
        Assert.True(NotificationLayer.MaxQueued >= NotificationLayer.MaxLive);
        Assert.True(Map.MaxParticleGenerators is >= 100 and <= 2000);
    }
}
