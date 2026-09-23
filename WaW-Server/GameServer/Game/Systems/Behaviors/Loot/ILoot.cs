using Common.Resources.Xml.Descriptors;
using GameServer.Game.Systems.Combat;
using GameServer.Game.Entities;

namespace GameServer.Game.Systems.Behaviors.Loot;

public interface ILoot {
    void Populate(ref EntityView host, ref Queue<Item> drops, ref DamageRecord record);
}