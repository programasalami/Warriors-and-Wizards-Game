using Common.Structs;
using Common.Utilities.Collections;
using GameServer.Game;
using GameServer.Game.Entities;
using GameServer.Game.Entities.Extensions;
using GameServer.Game.Systems.Behaviors;
using GameServer.Tests.Fixtures;

namespace GameServer.Tests.Worlds;

/// <summary>
/// Two things that froze the live Nexus on 2026-09-22 after a "/spawn Pirate": a timed action that threw stayed queued and threw again every tick
/// (World.HandleTimers removed it only after a successful run), and the thing it threw on - an entity with a behaviour had its behaviour LOADED
/// before it was REGISTERED, so State.Enter dereferenced a component that was not there yet (World.AddComponents).
/// </summary>
public class WorldTimerAndSpawnTests {
    private const ushort Pirate = 0x600;      // Shore.xml, the one entity with a behaviour in the library

    [Fact]
    public void TimedActionThatThrowsIsDroppedAndTheNextOneStillRuns() {
        if (GameLogic.TPS == 0)
            GameLogic.TPS = 20;
        var world = TestWorldFactory.CreateWorld();
        var ran = false;
        world.AddTimedAction(0, _ => throw new InvalidOperationException("boom"));
        world.AddTimedAction(0, _ => ran = true);

        var time = new RealmTime();
        world.Tick(ref time);       // both are due: the first throws and is dropped, the second still runs
        Assert.True(ran);

        ran = false;
        world.Tick(ref time);       // the bad one is gone - nothing throws, nothing runs again
        Assert.False(ran);
    }

    [Fact]
    public void SpawningAnEntityWithABehaviourEntersItsRootState() {
        RealmWorldTests.EnsureGameDataLoaded();
        BehaviorLibrary.Load();
        Assert.True(BehaviorLibrary.ClassicBehaviors.ContainsKey("Pirate"), "the Pirate behaviour must exist for this test to mean anything");

        var world = TestWorldFactory.CreateWorld();
        var entity = new Entity(Pirate);
        ref var en = ref world.EnterWorld(ref entity);       // threw NullReferenceException in State.Enter before the fix

        Assert.NotEqual(EntityId.Null, en.Id);
        ref var behavior = ref world.EntityBehaviors.Get(en.Id);
        Assert.NotEqual(EntityId.Null, behavior.Id);
        Assert.NotEmpty(behavior.ActiveStates);                // the root state was entered on the REGISTERED component, not a throwaway copy
    }

    // "/spawn 3 Pirate" reuses ONE Entity value for every EnterWorld call (SpawnCommand); each call must still produce its own entity.
    [Fact]
    public void SpawningACountWithTheSameEntityValueMakesThatManyEntities() {
        RealmWorldTests.EnsureGameDataLoaded();
        BehaviorLibrary.Load();
        var world = TestWorldFactory.CreateWorld();
        var entity = new Entity(Pirate);
        var ids = new HashSet<EntityId>();
        for (var i = 0; i < 3; i++) {
            ref var en = ref world.EnterWorld(ref entity);
            en.Move(world, 1.5f, 1.5f);
            ids.Add(en.Id);
        }

        Assert.Equal(3, ids.Count);
        foreach (var id in ids)
            Assert.NotEqual(EntityId.Null, world.EntityBehaviors.Get(id).Id);
    }
}
