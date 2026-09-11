using System.Collections.Generic;
using Common.Game;
using Common.Utilities;
using Common.Utilities.Collections;
using GameServer.Game.Systems.Behaviors;
using GameServer.Game.Systems.Chat;
using GameServer.Game.Systems.Combat;
using GameServer.Game.Systems.Events;
using GameServer.Game.Systems.Inventory;
using GameServer.Game.Systems.Portals;
using GameServer.Game.Systems.Projectiles;
using GameServer.Game.Systems.Sight;
using GameServer.Game.Systems.Stats;
using GameServer.Game.Worlds;

namespace GameServer.Game.Entities;

public abstract class ManagerBase<T> where T : struct, IEntityIdentifiable, IDisposable {

    public readonly SparseSet<T> Set;
    protected readonly World _world;
    
    protected ManagerBase(World world, int capacity) {
        _world = world;
        Set = new SparseSet<T>(capacity, capacity);
    }
    
    public virtual ref T Add(ref T elem) {
        return ref Set.Add(ref elem);
    }

    public virtual void Remove(EntityId id) {
        if (Set.Remove(id, out var elem))
            elem.Dispose();
    }

    public ref T Get(EntityId id) {
        return ref Set.Get(id);
    }

    public abstract void Tick(ref RealmTime time);
    
    public SparseEnumerator<T> GetEnumerator() => new(Set);
}