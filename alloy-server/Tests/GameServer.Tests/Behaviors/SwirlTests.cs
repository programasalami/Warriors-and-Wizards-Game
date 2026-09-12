using Common.Utilities.Collections;
using GameServer.Game;
using GameServer.Game.Entities;
using GameServer.Game.Systems.Behaviors;
using GameServer.Game.Systems.Behaviors.Actions;
using GameServer.Tests.Fixtures;

namespace GameServer.Tests.Behaviors;

public class SwirlTests {
    // Regression coverage for a real bug: the acquire check used to compare a non-nullable
    // EntityId against `null`, which is always true - so it treated "no target found"
    // (EntityId.Null) as if a valid target had been acquired. It's now `!= EntityId.Null`.
    public class AcquireWithNoTarget {
        [Fact]
        public void Tick_WithNoOtherEntitiesNearby_DoesNotAcquire() {
            var world = TestWorldFactory.CreateWorld();
            var id = TestWorldFactory.SpawnEnemy(world);
            var view = new EntityView(world, id);
            view.Stats.Move(1, 1);

            var behavior = new Swirl(targeted: true);
            behavior.Start(ref view);

            var time = new RealmTime { ElapsedMsDelta = 50 };
            var state = behavior.Tick(ref view, ref time);

            var swirlState = (SwirlInfo)view.Behavior.Resources.GetResource(behavior);
            Assert.False(swirlState.Acquired);
            Assert.Equal(BehaviorScript.BehaviorTickState.BehaviorActive, state);
        }

        [Fact]
        public void Tick_WithNoOtherEntitiesNearby_DoesNotThrow() {
            var world = TestWorldFactory.CreateWorld();
            var id = TestWorldFactory.SpawnEnemy(world);
            var view = new EntityView(world, id);
            view.Stats.Move(1, 1);

            var behavior = new Swirl(targeted: true);
            behavior.Start(ref view);

            var time = new RealmTime { ElapsedMsDelta = 50 };
            // Several ticks in a row - if the old `!= null` bug were reintroduced, this would
            // read EntityStats for EntityId.Null and corrupt the swirl center with garbage math.
            for (var i = 0; i < 5; i++)
                behavior.Tick(ref view, ref time);
        }

        [Fact]
        public void Tick_WithAnotherEntityNearby_Acquires() {
            var world = TestWorldFactory.CreateWorld();
            var hostId = TestWorldFactory.SpawnEnemy(world);
            var otherId = TestWorldFactory.SpawnCharacter(world);

            var hostView = new EntityView(world, hostId);
            hostView.Stats.Move(1, 1);
            var otherView = new EntityView(world, otherId);
            otherView.Stats.Move(2, 2);

            var behavior = new Swirl(targeted: true, acquireRange: 10, radius: 1);
            behavior.Start(ref hostView);

            var time = new RealmTime { ElapsedMsDelta = 50 };
            world.Map.Tick(ref time); // rebuilds the chunk map so the spatial query can see the moved entities
            behavior.Tick(ref hostView, ref time);

            var swirlState = (SwirlInfo)hostView.Behavior.Resources.GetResource(behavior);
            Assert.True(swirlState.Acquired);
        }
    }
}
