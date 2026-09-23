using Common.Utilities;
using GameServer.Game.Entities;
using GameServer.Game.Systems.Behaviors.Actions;
using GameServer.Tests.Fixtures;

namespace GameServer.Tests.Behaviors;

public class RingAttackTests {
    // Regression coverage for a real bug: Start() previously never copied the `targeted`
    // constructor argument into RingAttackInfo.Targeted, so RingAttack silently always
    // behaved as untargeted regardless of what was configured.
    public class StartTargetedFlag {
        [Fact]
        public void Start_TargetedTrue_IsReflectedInState() {
            var world = TestWorldFactory.CreateWorld();
            var id = TestWorldFactory.SpawnEnemy(world);
            var view = new EntityView(world, id);

            var behavior = new RingAttack(radius: 0, count: 1, offset: 0, projectileIndex: 0,
                angleToIncrement: 0, targeted: true);
            behavior.Start(ref view);

            var state = (RingAttackInfo)view.Behavior.Resources.GetResource(behavior);
            Assert.True(state.Targeted);
        }

        [Fact]
        public void Start_TargetedFalse_IsReflectedInState() {
            var world = TestWorldFactory.CreateWorld();
            var id = TestWorldFactory.SpawnEnemy(world);
            var view = new EntityView(world, id);

            var behavior = new RingAttack(radius: 0, count: 1, offset: 0, projectileIndex: 0,
                angleToIncrement: 0, targeted: false);
            behavior.Start(ref view);

            var state = (RingAttackInfo)view.Behavior.Resources.GetResource(behavior);
            Assert.False(state.Targeted);
        }

        [Fact]
        public void Start_AlsoCopiesAngleAndCooldownConfig() {
            var world = TestWorldFactory.CreateWorld();
            var id = TestWorldFactory.SpawnEnemy(world);
            var view = new EntityView(world, id);

            var behavior = new RingAttack(radius: 0, count: 1, offset: 0, projectileIndex: 0,
                angleToIncrement: 45, fixedAngle: 90, coolDownMS: 2500);
            behavior.Start(ref view);

            var state = (RingAttackInfo)view.Behavior.Resources.GetResource(behavior);
            Assert.Equal(2500, state.CoolDownLeft);
            // Constructor converts degrees to radians for both angle fields.
            Assert.Equal(45f.Deg2Rad(), state.AngleToIncrement, precision: 5);
            Assert.Equal(90f.Deg2Rad(), state.FixedAngle, precision: 5);
        }
    }
}
