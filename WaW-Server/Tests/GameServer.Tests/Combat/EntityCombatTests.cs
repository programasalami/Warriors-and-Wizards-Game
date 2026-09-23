using GameServer.Tests.Fixtures;

namespace GameServer.Tests.Combat;

public class EntityCombatTests {
    public class ProjectileDamage {
        [Fact]
        public void GetProjectileDamage_StaysWithinBounds_AcrossManyRolls() {
            var world = TestWorldFactory.CreateWorld();
            var id = TestWorldFactory.SpawnEnemy(world);
            ref var combat = ref world.EntityCombat.Get(id);

            for (var i = 0; i < 1000; i++) {
                var dmg = combat.GetProjectileDamage(10, 20);
                Assert.InRange(dmg, 10, 19); // Random.Shared.Next(min, max) excludes max
            }
        }

        [Fact]
        public void GetProjectileDamage_EqualMinAndMax_AlwaysReturnsThatValue() {
            var world = TestWorldFactory.CreateWorld();
            var id = TestWorldFactory.SpawnEnemy(world);
            ref var combat = ref world.EntityCombat.Get(id);

            var dmg = combat.GetProjectileDamage(15, 15);

            Assert.Equal(15, dmg);
        }
    }
}
