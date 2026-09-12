using Common;
using GameServer.Game.Entities;

namespace GameServer.Tests.Fixtures;

public class TestWorldFactoryTests {
    [Fact]
    public void CreateWorld_Succeeds() {
        var world = TestWorldFactory.CreateWorld();

        Assert.NotNull(world.Map);
        Assert.Equal(4, world.Map.Data.Width);
        Assert.Equal(4, world.Map.Data.Height);
    }

    [Fact]
    public void SpawnCharacter_ProducesAWorkingEntityView() {
        var world = TestWorldFactory.CreateWorld();
        var id = TestWorldFactory.SpawnCharacter(world);

        Assert.Equal(EntityType.Character, world.Entities.Get(id).Type);

        var view = new EntityView(world, id);
        Assert.Equal(100, view.Stats.GetInt(StatType.HP));
    }

    [Fact]
    public void SpawnEnemy_ProducesAWorkingEntityView() {
        var world = TestWorldFactory.CreateWorld();
        var id = TestWorldFactory.SpawnEnemy(world);

        Assert.Equal(EntityType.Enemy, world.Entities.Get(id).Type);
    }

    [Fact]
    public void GetSpeed_MultipliesByTheTileSpeed() {
        var world = TestWorldFactory.CreateWorld();
        var id = TestWorldFactory.SpawnCharacter(world);
        var view = new EntityView(world, id);
        view.Stats.Move(0, 0); // establishes Tile from the map

        var speed = view.Stats.GetSpeed(1f);

        Assert.Equal(1f, speed); // our test ground tile defaults to Speed=1.0
    }

    [Fact]
    public void HasConditionEffect_WorksThroughEntityView() {
        var world = TestWorldFactory.CreateWorld();
        var id = TestWorldFactory.SpawnCharacter(world);
        var view = new EntityView(world, id);

        Assert.False(view.HasConditionEffect(ConditionEffectIndex.Slowed));

        view.ApplyConditionEffect(ConditionEffectIndex.Slowed, 5f);

        Assert.True(view.HasConditionEffect(ConditionEffectIndex.Slowed));
    }
}
